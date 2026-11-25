using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CMDL models for script actors, doors, platforms, the arm cannon: parsed once on the loader thread (geometry + material set +
    /// textures to decode), turned into Unity meshes and materials once on the main thread, instantiated many times. ANCS
    /// characters resolve to their character's CMDL (the bind pose; animation comes with the skinning work).
    /// </summary>
    public sealed class MpModel
    {
        public uint Id;
        public MpGeometry Geometry;
        public MpMaterialSet Set;
        public List<MpMeshData> Data;
        public Mesh[] Meshes;
        public Material[][] Materials;
        public int[][] SubMaterialOf;    // per mesh: the game material index of each submesh
        public bool Built;
        public Vector3 Min, Max;

        /// <summary>Main thread: meshes and materials, once.</summary>
        public void Build(bool actorLighting)
        {
            if (Built) return;
            Built = true;
            if (Data == null) return;
            var mats = new Material[Set != null ? Set.Materials.Length : 0];
            Meshes = new Mesh[Data.Count];
            Materials = new Material[Data.Count][];
            SubMaterialOf = new int[Data.Count][];
            for (int i = 0; i < Data.Count; i++)
            {
                MpMeshData d = Data[i];
                Meshes[i] = d.ToMesh();
                Materials[i] = new Material[d.SubMaterial.Count];
                SubMaterialOf[i] = d.SubMaterial.ToArray();
                for (int s = 0; s < d.SubMaterial.Count; s++)
                {
                    int mi = d.SubMaterial[s];
                    if (mi < 0 || mi >= mats.Length) continue;
                    if (mats[mi] == null) mats[mi] = MpMaterials.Make(Set.Materials[mi], Set.TextureIds, actorLighting, "CMDL " + Id.ToString("X8") + " #" + mi);
                    Materials[i][s] = mats[mi];
                }
            }

            Data = null;
        }

        /// <summary>A new GameObject showing the model (children per mesh), parented and placed in game space.</summary>
        public GameObject Instantiate(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            if (Meshes == null) return root;
            for (int i = 0; i < Meshes.Length; i++)
            {
                if (Meshes[i] == null) continue;
                var go = i == 0 ? root : new GameObject(name + " " + i);
                if (i > 0) go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Meshes[i];
                go.AddComponent<MeshRenderer>().sharedMaterials = Materials[i];
            }

            return root;
        }
    }

    public static class MpModels
    {
        private static readonly Dictionary<uint, MpModel> Cache = new Dictionary<uint, MpModel>();
        private static readonly object Lock = new object();
        private static readonly Dictionary<uint, MpAncs> AncsCache = new Dictionary<uint, MpAncs>();

        /// <summary>Loader thread: the model (parsed once). Null if the id is not a readable CMDL.</summary>
        public static MpModel Load(MpAssets assets, uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (Lock)
            {
                MpModel m;
                if (Cache.TryGetValue(id, out m)) return m;
            }

            MpModel model = null;
            try
            {
                byte[] b = assets.Read(id);
                if (b != null && assets.TypeOf(id) == "CMDL")
                {
                    List<MpMaterialSet> sets;
                    var g = MpGeometry.ReadCmdl(b, out sets);
                    model = new MpModel { Id = id, Geometry = g, Set = sets.Count > 0 ? sets[0] : null };
                    model.Data = MpMeshes.Build(g, model.Set, "CMDL " + id.ToString("X8"));
                    model.Min = new Vector3(g.Bounds[0], g.Bounds[1], g.Bounds[2]);
                    model.Max = new Vector3(g.Bounds[3], g.Bounds[4], g.Bounds[5]);
                }
            }
            catch (Exception e)
            {
                MpLog.Warn("Models: CMDL " + id.ToString("X8") + " not read: " + e.Message);
                model = null;
            }

            lock (Lock) Cache[id] = model;
            return model;
        }

        /// <summary>Loader thread: the ANCS character set (parsed once).</summary>
        public static MpAncs Ancs(MpAssets assets, uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (Lock)
            {
                MpAncs a;
                if (AncsCache.TryGetValue(id, out a)) return a;
            }

            MpAncs ancs = null;
            try
            {
                byte[] b = assets.Read(id);
                if (b != null && assets.TypeOf(id) == "ANCS") ancs = MpAncs.Read(b);
            }
            catch (Exception e)
            {
                MpLog.Warn("Models: ANCS " + id.ToString("X8") + " not read: " + e.Message);
            }

            lock (Lock) AncsCache[id] = ancs;
            return ancs;
        }

        /// <summary>Loader thread: the CMDL a script model reference shows (static model, or the ANCS character's model).</summary>
        public static uint CmdlOf(MpAssets assets, MpModelRef r)
        {
            if (MpAssets.Valid(r.Cmdl) && assets.TypeOf(r.Cmdl) == "CMDL") return r.Cmdl;
            if (MpAssets.Valid(r.Ancs))
            {
                MpAncs a = Ancs(assets, r.Ancs);
                if (a != null && r.Character >= 0 && r.Character < a.Characters.Count) return a.Characters[r.Character].Cmdl;
                if (a != null && a.Characters.Count > 0) return a.Characters[0].Cmdl;
            }

            return 0xFFFFFFFF;
        }

        public static MpModel Get(uint id)
        {
            lock (Lock)
            {
                MpModel m;
                return Cache.TryGetValue(id, out m) ? m : null;
            }
        }
    }
}
