using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CMDL models for script actors, doors, platforms, the arm cannon: parsed once on the loader thread (geometry + material sets +
    /// textures to decode), turned into Unity meshes and materials once on the main thread, instantiated many times. A skinned model
    /// (an ANCS character's CMDL with its CSKR and CINF) carries bone weights: each vertex goes with the virtual bone that covers its
    /// position index (CSkinRules), the weights by the rig's bone order, the bind poses the bones' reference positions.
    /// A model has one geometry and one or more material sets (CModel shader sets: the arm cannon has one per beam, actors pick
    /// theirs with the "skin" index): <see cref="MaterialsOf"/> gives a mesh's materials in any set.
    /// </summary>
    public sealed class MpModel
    {
        public uint Id;
        public MpGeometry Geometry;
        public MpMaterialSet Set;
        public List<MpMaterialSet> Sets = new List<MpMaterialSet>();
        public List<MpMeshData> Data;
        public Mesh[] Meshes;
        public Material[][] Materials;
        public int[][] SubMaterialOf;    // per mesh: the game material index of each submesh
        public bool Built;
        public Vector3 Min, Max;
        public bool ActorLighting;

        // skinning (null for a static model)
        public MpSkin Skin;
        public MpRig Rig;
        public Matrix4x4[] BindPoses;
        /// <summary>Per mesh, per vertex: the model's position index and position (kept for skinned models: caps and locators need them).</summary>
        public int[][] PosIdxOf;
        public Vector3[][] PosOf;
        public BoneWeight[][] WeightsOf;

        private readonly Dictionary<int, Material[][]> _bySet = new Dictionary<int, Material[][]>();

        /// <summary>Main thread: meshes and materials, once.</summary>
        public void Build(bool actorLighting)
        {
            if (Built) return;
            Built = true;
            ActorLighting = actorLighting;
            if (Data == null) return;
            Meshes = new Mesh[Data.Count];
            SubMaterialOf = new int[Data.Count][];
            bool skinned = Skin != null && Rig != null;
            if (skinned)
            {
                PosIdxOf = new int[Data.Count][];
                PosOf = new Vector3[Data.Count][];
                WeightsOf = new BoneWeight[Data.Count][];
                BindPoses = new Matrix4x4[Rig.Count];
                for (int b = 0; b < Rig.Count; b++) BindPoses[b] = Matrix4x4.Translate(new Vector3(-Rig.Ref[b * 3], -Rig.Ref[b * 3 + 1], -Rig.Ref[b * 3 + 2]));
            }

            int[] groupOf = skinned ? Skin.GroupPerIndex(Math.Max(Skin.PointCount, MaxPosIdx() + 1)) : null;
            for (int i = 0; i < Data.Count; i++)
            {
                MpMeshData d = Data[i];
                SubMaterialOf[i] = d.SubMaterial.ToArray();
                if (skinned)
                {
                    PosIdxOf[i] = d.PosIdx;
                    PosOf[i] = d.Pos;
                    WeightsOf[i] = Weights(d, groupOf);
                    Meshes[i] = d.ToMesh(WeightsOf[i], BindPoses);
                }
                else Meshes[i] = d.ToMesh();
            }

            Materials = MaterialsOf(0);
            Data = null;
        }

        private int MaxPosIdx()
        {
            int m = 0;
            foreach (MpMeshData d in Data)
                if (d.PosIdx != null)
                    foreach (int p in d.PosIdx) if (p > m) m = p;
            return m;
        }

        /// <summary>The bone weights of a vertex at position index <paramref name="pi"/> (the root bone when the skin does not cover it).</summary>
        public BoneWeight WeightOf(int pi, int[] groupOf)
        {
            int root = Rig.RootIndex >= 0 ? Rig.RootIndex : 0;
            int g = pi >= 0 && pi < groupOf.Length ? groupOf[pi] : -1;
            var bw = new BoneWeight { boneIndex0 = root, weight0 = 1f };
            if (g < 0) return bw;
            MpSkin.Group grp = Skin.Groups[g];
            for (int k = 0; k < grp.Bones.Count && k < 3; k++)
            {
                int id = grp.Bones[k];
                int bi = id >= 0 && id < 256 ? Rig.IndexOfId[id] : -1;
                if (bi < 0) bi = root;
                float wt = grp.Weights[k];
                if (k == 0) { bw.boneIndex0 = bi; bw.weight0 = wt; }
                else if (k == 1) { bw.boneIndex1 = bi; bw.weight1 = wt; }
                else { bw.boneIndex2 = bi; bw.weight2 = wt; }
            }

            float sum = bw.weight0 + bw.weight1 + bw.weight2;
            if (sum > 1e-5f) { bw.weight0 /= sum; bw.weight1 /= sum; bw.weight2 /= sum; }
            else { bw.weight0 = 1f; bw.weight1 = bw.weight2 = 0f; }
            return bw;
        }

        private BoneWeight[] Weights(MpMeshData d, int[] groupOf)
        {
            var w = new BoneWeight[d.VertexCount];
            for (int v = 0; v < w.Length; v++) w[v] = WeightOf(d.PosIdx != null ? d.PosIdx[v] : -1, groupOf);
            return w;
        }

        /// <summary>The materials of every mesh in material set <paramref name="set"/> (falls back to set 0), made once per set.</summary>
        public Material[][] MaterialsOf(int set)
        {
            if (Meshes == null) return null;
            if (set < 0 || set >= Sets.Count) set = 0;
            Material[][] arr;
            if (_bySet.TryGetValue(set, out arr)) return arr;
            MpMaterialSet ms = set < Sets.Count ? Sets[set] : Set;
            var mats = new Material[ms != null ? ms.Materials.Length : 0];
            arr = new Material[Meshes.Length][];
            for (int i = 0; i < Meshes.Length; i++)
            {
                int[] sub = SubMaterialOf[i];
                arr[i] = new Material[sub.Length];
                for (int s = 0; s < sub.Length; s++)
                {
                    int mi = sub[s];
                    if (mi < 0 || mi >= mats.Length) continue;
                    if (mats[mi] == null) mats[mi] = MpMaterials.Make(ms.Materials[mi], ms.TextureIds, ActorLighting, "CMDL " + Id.ToString("X8") + " set " + set + " #" + mi);
                    arr[i][s] = mats[mi];
                }
            }

            _bySet[set] = arr;
            return arr;
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
        private static readonly Dictionary<ulong, MpModel> SkinnedCache = new Dictionary<ulong, MpModel>();
        private static readonly object Lock = new object();
        private static readonly Dictionary<uint, MpAncs> AncsCache = new Dictionary<uint, MpAncs>();
        private static readonly Dictionary<uint, MpRig> RigCache = new Dictionary<uint, MpRig>();

        /// <summary>Loader thread: the model (parsed once). Null if the id is not a readable CMDL.</summary>
        public static MpModel Load(MpAssets assets, uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (Lock)
            {
                MpModel m;
                if (Cache.TryGetValue(id, out m)) return m;
            }

            MpModel model = Parse(assets, id);
            lock (Lock) Cache[id] = model;
            return model;
        }

        private static MpModel Parse(MpAssets assets, uint id)
        {
            try
            {
                byte[] b = assets.Read(id);
                if (b != null && assets.TypeOf(id) == "CMDL")
                {
                    List<MpMaterialSet> sets;
                    var g = MpGeometry.ReadCmdl(b, out sets);
                    var model = new MpModel { Id = id, Geometry = g, Set = sets.Count > 0 ? sets[0] : null, Sets = sets };
                    model.Data = MpMeshes.Build(g, model.Set, "CMDL " + id.ToString("X8"));
                    model.Min = new Vector3(g.Bounds[0], g.Bounds[1], g.Bounds[2]);
                    model.Max = new Vector3(g.Bounds[3], g.Bounds[4], g.Bounds[5]);
                    return model;
                }
            }
            catch (Exception e)
            {
                MpLog.Warn("Models: CMDL " + id.ToString("X8") + " not read: " + e.Message);
            }

            return null;
        }

        /// <summary>Loader thread: a character's model skinned by its CSKR over its CINF (parsed once per pair). Null when any part is unreadable.</summary>
        public static MpModel LoadSkinned(MpAssets assets, uint cmdl, uint cskr, uint cinf)
        {
            if (!MpAssets.Valid(cmdl) || !MpAssets.Valid(cskr) || !MpAssets.Valid(cinf)) return null;
            ulong key = ((ulong)cmdl << 32) | cskr;
            lock (Lock)
            {
                MpModel m;
                if (SkinnedCache.TryGetValue(key, out m)) return m;
            }

            MpModel model = Parse(assets, cmdl);
            if (model != null)
            {
                try
                {
                    model.Skin = MpSkin.Read(assets.Read(cskr));
                    model.Rig = Rig(assets, cinf);
                    if (model.Rig == null) model = null;
                }
                catch (Exception e)
                {
                    MpLog.Warn("Models: skin CSKR " + cskr.ToString("X8") + " / CINF " + cinf.ToString("X8") + " not read: " + e.Message);
                    model = null;
                }
            }

            lock (Lock) SkinnedCache[key] = model;
            return model;
        }

        /// <summary>Loader thread: a CINF as a rig (parsed once).</summary>
        public static MpRig Rig(MpAssets assets, uint cinf)
        {
            lock (Lock)
            {
                MpRig r;
                if (RigCache.TryGetValue(cinf, out r)) return r;
            }

            MpRig rig = null;
            try { rig = MpRig.From(MpSkeleton.Read(assets.Read(cinf))); }
            catch (Exception e) { MpLog.Warn("Models: CINF " + cinf.ToString("X8") + " not read: " + e.Message); }
            lock (Lock) RigCache[cinf] = rig;
            return rig;
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

        /// <summary>Every texture of every material set of a model.</summary>
        public static void TexturesOf(MpModel m, HashSet<uint> into)
        {
            if (m == null) return;
            if (m.Sets != null && m.Sets.Count > 0) foreach (MpMaterialSet s in m.Sets) MpMaterials.TexturesOf(s, into);
            else MpMaterials.TexturesOf(m.Set, into);
        }
    }
}
