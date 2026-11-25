using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Mesh arrays prepared on the loader thread (pure data: no Unity objects), turned into a Mesh on the main thread. Positions,
    /// normals and the game's texture coordinates TEX0..TEX4 (UV channels 0..4) stay in game space (the root converts).
    /// </summary>
    public sealed class MpMeshData
    {
        public string Name;
        public Vector3[] Pos;
        public Vector3[] Nrm;
        public int[] PosIdx;            // per vertex: the model's position index (skinning)
        public Vector2[][] Uv = new Vector2[5][];
        public readonly List<int[]> SubTris = new List<int[]>();
        public readonly List<int> SubMaterial = new List<int>();
        public Vector3 Min, Max;
        public bool Transparent;
        public Vector3 SortCenter;

        public int VertexCount { get { return Pos != null ? Pos.Length : 0; } }

        public Mesh ToMesh()
        {
            return ToMesh(null, null);
        }

        /// <summary>The mesh; with <paramref name="weights"/> and <paramref name="bindPoses"/> a skinned one (drawn by a SkinnedMeshRenderer).</summary>
        public Mesh ToMesh(BoneWeight[] weights, Matrix4x4[] bindPoses)
        {
            var m = new Mesh { name = Name };
            if (Pos.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = Pos;
            m.normals = Nrm;
            for (int c = 0; c < 5; c++) if (Uv[c] != null) m.SetUVs(c, Uv[c]);
            m.subMeshCount = SubTris.Count;
            for (int i = 0; i < SubTris.Count; i++) m.SetTriangles(SubTris[i], i, false);
            if (weights != null && bindPoses != null)
            {
                m.boneWeights = weights;
                m.bindposes = bindPoses;
            }

            m.bounds = new Bounds((Min + Max) * 0.5f, Max - Min);
            m.UploadMeshData(true);
            return m;
        }
    }

    public static class MpMeshes
    {
        /// <summary>
        /// One game model (CMDL or MREA world model) as mesh arrays: the opaque surfaces merged into one mesh (a submesh per material),
        /// every sorted surface (blended material) a mesh of its own so Unity sorts it by its own centre, as the game sorts surfaces.
        /// </summary>
        public static List<MpMeshData> Build(MpGeometry g, MpMaterialSet set, string name)
        {
            var result = new List<MpMeshData>();
            var opaque = new List<MpSurface>();
            foreach (MpSurface s in g.Surfaces)
            {
                if (s.Tris == null || s.Tris.Length == 0) continue;
                MpMaterial m = set != null && s.Material < set.Materials.Length ? set.Materials[s.Material] : null;
                if (m == null || MpMaterials.IsOpaque(m)) opaque.Add(s);
                else
                {
                    var one = Merge(new List<MpSurface> { s }, name + " sorted " + result.Count);
                    one.Transparent = true;
                    one.SortCenter = new Vector3(s.CenterX, s.CenterY, s.CenterZ);
                    result.Add(one);
                }
            }

            if (opaque.Count > 0) result.Insert(0, Merge(opaque, name));
            return result;
        }

        private static MpMeshData Merge(List<MpSurface> surfaces, string name)
        {
            var d = new MpMeshData { Name = name };
            int total = 0;
            var uvUsed = new bool[5];
            foreach (MpSurface s in surfaces)
            {
                total += s.VertexCount;
                for (int c = 0; c < 5; c++) if (s.Uv[c] != null) uvUsed[c] = true;
            }

            d.Pos = new Vector3[total];
            d.Nrm = new Vector3[total];
            d.PosIdx = new int[total];
            for (int c = 0; c < 5; c++) if (uvUsed[c]) d.Uv[c] = new Vector2[total];
            var byMaterial = new Dictionary<int, List<int>>();
            var order = new List<int>();
            int at = 0;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            foreach (MpSurface s in surfaces)
            {
                for (int v = 0; v < s.VertexCount; v++)
                {
                    var p = new Vector3(s.Pos[v * 3], s.Pos[v * 3 + 1], s.Pos[v * 3 + 2]);
                    d.Pos[at + v] = p;
                    d.PosIdx[at + v] = s.PosIdx != null ? s.PosIdx[v] : -1;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    d.Nrm[at + v] = new Vector3(s.Nrm[v * 3], s.Nrm[v * 3 + 1], s.Nrm[v * 3 + 2]);
                    for (int c = 0; c < 5; c++)
                        if (s.Uv[c] != null) d.Uv[c][at + v] = new Vector2(s.Uv[c][v * 2], s.Uv[c][v * 2 + 1]);
                }

                List<int> list;
                if (!byMaterial.TryGetValue(s.Material, out list))
                {
                    list = new List<int>();
                    byMaterial[s.Material] = list;
                    order.Add(s.Material);
                }

                for (int i = 0; i < s.Tris.Length; i++) list.Add(s.Tris[i] + at);
                at += s.VertexCount;
            }

            foreach (int mi in order)
            {
                d.SubTris.Add(byMaterial[mi].ToArray());
                d.SubMaterial.Add(mi);
            }

            d.Min = min;
            d.Max = max;
            return d;
        }
    }
}
