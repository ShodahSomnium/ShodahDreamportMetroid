using System;
using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>What the loader thread prepares for one area (no Unity objects).</summary>
    public sealed class MpAreaPayload
    {
        public MpWorld.Area Info;
        public MpArea Area;
        public readonly List<MpGeometry> Geometry = new List<MpGeometry>();
        public readonly List<List<MpMeshData>> Meshes = new List<List<MpMeshData>>();
        public MpCollision Collision;
        public List<MpScriptObject> Objects;
        public readonly List<KeyValuePair<uint, MpTexture>> Textures = new List<KeyValuePair<uint, MpTexture>>();
        public readonly List<MpModel> Models = new List<MpModel>();
        public readonly Dictionary<uint, MpCollision> Dclns = new Dictionary<uint, MpCollision>();
        public float LightingLevel = 1f;
        public bool NeedsSky;                       // AreaAttributes: the area shows the sky (CGameArea::GetNeedsSky)
        public uint SkyOverride = 0xFFFFFFFF;       // AreaAttributes: its own sky model instead of the world's
        public long ParseMs;
    }

    /// <summary>
    /// One loaded area: the world models (visor flags kept: bit 1 = not drawn in combat / scan, bit 3 = not in x-ray, CStateManager
    /// ::DrawWorld masks), the collision (solid triangles -> a MeshCollider the avatar walks on; every triangle for shots), and the
    /// script objects (MpScriptWorld). Built a few milliseconds per frame.
    /// </summary>
    public sealed class MpAreaView
    {
        public int Index;
        public MpWorld.Area Info;
        public GameObject Root;
        public MpAreaPayload Payload;
        public bool Ready;
        public readonly List<KeyValuePair<GameObject, uint>> WorldModels = new List<KeyValuePair<GameObject, uint>>();
        public MeshCollider Collider, ShotCollider;
        public Material[] Materials;
        public readonly List<GameObject> Actors = new List<GameObject>();

        /// <summary>Layer for the area collision the shots hit (the avatar's own collider is on layer 2, unaffected).</summary>
        public const int ShotLayer = 0;

        /// <summary>Layer of the two-sided shot collision (Ignore Raycast is 2; 10 is a free user layer the avatar does not collide with in the matrix, see somnium-client-physics).</summary>
        public const int ShotOnlyLayer = 10;

        /// <summary>Each area shot collider's collision material per triangle (what a hit surface is made of: the impact effect, CEnergyProjectile::ResolveCollisionWithWorld).</summary>
        public static readonly Dictionary<Collider, uint[]> ShotMaterials = new Dictionary<Collider, uint[]>();

        /// <summary>The material of the triangle a ray hit on an area's shot collider, or 0.</summary>
        public static uint MaterialAt(RaycastHit hit)
        {
            uint[] m;
            lock (ShotMaterials) if (hit.collider != null && ShotMaterials.TryGetValue(hit.collider, out m) && hit.triangleIndex >= 0 && hit.triangleIndex < m.Length) return m[hit.triangleIndex];
            return 0;
        }

        private static readonly HashSet<uint> Decoding = new HashSet<uint>();
        private static readonly object DecodeLock = new object();

        /// <summary>Loader thread: reads and parses everything for the area.</summary>
        public static MpAreaPayload Prepare(MpAssets assets, MpWorld.Area info)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new MpAreaPayload { Info = info };
            p.Area = MpArea.Read(assets.Read(info.Mrea), true);
            for (int i = 0; i < p.Area.Models.Count; i++)
            {
                MpGeometry g = p.Area.Models[i];
                p.Geometry.Add(g);
                p.Meshes.Add(MpMeshes.Build(g, p.Area.Materials, info.Name + " model " + i));
            }

            p.Collision = MpCollision.Read(p.Area);
            p.Objects = MpScriptLayers.Read(p.Area);

            var tex = new HashSet<uint>();
            MpMaterials.TexturesOf(p.Area.Materials, tex);
            var models = new HashSet<uint>();
            foreach (MpScriptObject o in p.Objects)
            {
                if (!LayerActive(info, o.Layer)) continue;
                MpModelRef mr = default(MpModelRef);
                switch (o.Type)
                {
                    case MpScriptType.Actor: { var a = MpScriptRead.ReadActor(o); if (a != null) mr = a.Model; break; }
                    case MpScriptType.Door: { var d = MpScriptRead.ReadDoor(o); if (d != null) mr = d.Model; break; }
                    case MpScriptType.Platform: { var pl = MpScriptRead.ReadPlatform(o); if (pl != null) { mr = pl.Model; LoadDcln(assets, pl.Dcln, p); } break; }
                    case MpScriptType.AreaAttributes:
                    {
                        var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
                        if (o.PropCount == 9 && r.S32() == 1)
                        {
                            p.NeedsSky = r.Bool();
                            r.S32(); r.F32(); r.F32(); r.F32();
                            p.LightingLevel = r.F32();
                            p.SkyOverride = r.U32();
                            if (MpAssets.Valid(p.SkyOverride) && models.Add(p.SkyOverride))
                            {
                                MpModel sky = MpModels.Load(assets, p.SkyOverride);
                                if (sky != null) { p.Models.Add(sky); MpMaterials.TexturesOf(sky.Set, tex); }
                            }
                        }

                        break;
                    }
                }

                if (!mr.Valid) continue;
                uint cmdl = MpModels.CmdlOf(assets, mr);
                if (!MpAssets.Valid(cmdl) || !models.Add(cmdl)) continue;
                MpModel m = MpModels.Load(assets, cmdl);
                if (m == null) continue;
                p.Models.Add(m);
                MpMaterials.TexturesOf(m.Set, tex);
            }

            foreach (uint id in tex)
            {
                lock (DecodeLock)
                {
                    if (!Decoding.Add(id)) continue;
                }

                try
                {
                    byte[] b = assets.Read(id);
                    if (b != null) p.Textures.Add(new KeyValuePair<uint, MpTexture>(id, MpTexture.Read(b)));
                }
                catch (Exception e)
                {
                    MpLog.Warn("Textures: TXTR " + id.ToString("X8") + " not decoded: " + e.Message);
                }
            }

            p.ParseMs = sw.ElapsedMilliseconds;
            return p;
        }

        private static void LoadDcln(MpAssets assets, uint id, MpAreaPayload p)
        {
            if (!MpAssets.Valid(id) || p.Dclns.ContainsKey(id)) return;
            try { p.Dclns[id] = assets.TypeOf(id) == "DCLN" ? MpCollision.ReadDcln(assets.Read(id)) : null; }
            catch (Exception e) { MpLog.Warn("Collision: DCLN " + id.ToString("X8") + " not read: " + e.Message); p.Dclns[id] = null; }
        }

        public static bool LayerActive(MpWorld.Area info, int layer)
        {
            if (layer >= 64) return true;
            return (info.LayerBits & (1UL << layer)) != 0;
        }

        /// <summary>Main thread, a slice at a time: textures, materials, world models, collision.</summary>
        public IEnumerator Build(MpAreaPayload p, Transform worldRoot, float sliceMs)
        {
            Payload = p;
            Info = p.Info;
            Index = p.Info.Index;
            Root = new GameObject("Area " + Index + " " + p.Info.Name);
            Root.transform.SetParent(worldRoot, false);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var kv in p.Textures)
            {
                MpTextures.Upload(kv.Key, kv.Value);
                if (sw.ElapsedMilliseconds > sliceMs) { yield return null; sw.Reset(); sw.Start(); }
            }

            MpMaterialSet set = p.Area.Materials;
            Materials = new Material[set.Materials.Length];
            for (int i = 0; i < set.Materials.Length; i++)
            {
                Materials[i] = MpMaterials.Make(set.Materials[i], set.TextureIds, false, p.Info.Name + " #" + i);
                if (sw.ElapsedMilliseconds > sliceMs) { yield return null; sw.Reset(); sw.Start(); }
            }

            for (int m = 0; m < p.Meshes.Count; m++)
            {
                uint visor = p.Geometry[m].VisorFlags;
                List<MpMeshData> parts = p.Meshes[m];
                for (int k = 0; k < parts.Count; k++)
                {
                    MpMeshData d = parts[k];
                    var go = new GameObject(d.Name);
                    go.transform.SetParent(Root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = d.ToMesh();
                    var mats = new Material[d.SubMaterial.Count];
                    for (int s = 0; s < mats.Length; s++) mats[s] = d.SubMaterial[s] < Materials.Length ? Materials[d.SubMaterial[s]] : null;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    WorldModels.Add(new KeyValuePair<GameObject, uint>(go, visor));
                }

                if (sw.ElapsedMilliseconds > sliceMs) { yield return null; sw.Reset(); sw.Start(); }
            }

            foreach (MpModel model in p.Models)
            {
                model.Build(true);
                if (sw.ElapsedMilliseconds > sliceMs) { yield return null; sw.Reset(); sw.Start(); }
            }

            BuildCollision(p.Collision);
            Ready = true;
        }

        private void BuildCollision(MpCollision c)
        {
            if (c == null || c.TriangleCount == 0) return;
            var verts = new Vector3[c.Vertices.Length / 3];
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(c.Vertices[i * 3], c.Vertices[i * 3 + 1], c.Vertices[i * 3 + 2]);
            var solid = new List<int>();
            var shots = new List<int>();
            var shotMats = new List<uint>();
            for (int t = 0; t < c.TriangleCount; t++)
            {
                uint m = c.TriangleMaterial[t];
                int a = c.Triangles[t * 3], b = c.Triangles[t * 3 + 1], d = c.Triangles[t * 3 + 2];
                if (MpCollision.Has(m, MpCollision.Solid))
                {
                    solid.Add(a); solid.Add(b); solid.Add(d);
                }

                if (!MpCollision.Has(m, MpCollision.ProjectilePassthrough))
                {
                    // both windings: ray casts hit front faces only, and a shot may come from either side
                    shots.Add(a); shots.Add(b); shots.Add(d);
                    shots.Add(a); shots.Add(d); shots.Add(b);
                    shotMats.Add(m); shotMats.Add(m);
                }
            }

            Collider = MakeCollider("Collision (walk)", verts, solid);
            ShotCollider = MakeCollider("Collision (shots)", verts, shots);
            if (ShotCollider != null) { ShotCollider.gameObject.layer = ShotOnlyLayer; lock (ShotMaterials) ShotMaterials[ShotCollider] = shotMats.ToArray(); }
        }

        private MeshCollider MakeCollider(string name, Vector3[] verts, List<int> tris)
        {
            if (tris.Count == 0) return null;
            var mesh = new Mesh { name = Info.Name + " " + name };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.SetTriangles(tris, 0, true);
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.layer = ShotLayer;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            return mc;
        }

        /// <summary>World models by visor (EPlayerVisor; CStateManager::DrawWorld): combat/scan hide flag bit 1, x-ray (1) hides bit 3, thermal (3) masks 0x34.</summary>
        public void ApplyVisor(int visor)
        {
            int bit = visor == 1 ? 3 : 1;
            foreach (var kv in WorldModels)
            {
                if (kv.Key == null) continue;
                bool show = (kv.Value & (1u << bit)) == 0;
                if (visor == 3) show = (kv.Value & 0x34u) == 0 || (kv.Value & 0x34u) == 0x20 || (kv.Value & 0x34u) == 0x10;
                if (kv.Key.activeSelf != show) kv.Key.SetActive(show);
            }
        }

        public void Destroy()
        {
            if (ShotCollider != null) lock (ShotMaterials) ShotMaterials.Remove(ShotCollider);
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (Materials != null) foreach (Material m in Materials) if (m != null) UnityEngine.Object.Destroy(m);
            Root = null;
        }
    }
}
