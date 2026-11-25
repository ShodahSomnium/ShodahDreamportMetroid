using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// Area collision (decomp CGameArea::PostConstructArea + CAreaOctTree::MakeFromMemory): the section starts with a u32, then
    /// u32 size, then the tree header (u32 magic, u32 version, AABB, u32 root type, u32 tree size, tree) followed by the master
    /// lists: materials (u32 count + u32 material bits), vertex / edge / triangle material indices (u32 count + u8 each), edges
    /// (u32 count + 2 u16 vertex indices), triangle edges (u32 count = 3 x triangles, u16 edge indices), vertices (u32 count +
    /// float3). A triangle's third vertex is the second edge's vertex not on the first edge; material bit 25 flips the winding
    /// (CAreaOctTree::GetMasterListTriangle). Material bits = CMaterialList EMaterialTypes (Collision/CMaterialList.hpp).
    /// </summary>
    public sealed class MpCollision
    {
        // EMaterialTypes bits used here
        public const int Stone = 1, Metal = 2, Grass = 3, Ice = 4, MetalGrating = 6, Phazon = 7, Dirt = 8, Lava = 9, Snow = 11,
            Glass = 15, Shield = 16, Sand = 17, ProjectilePassthrough = 18, Solid = 19, NoPlatformCollision = 20, CameraPassthrough = 21,
            Wood = 22, Organic = 23, FlippedTri = 25, SeeThrough = 26, ScanPassthrough = 27, Ceiling = 29, Wall = 30, Floor = 31;

        public float[] Vertices = new float[0];      // 3 per vertex, game space
        public int[] Triangles = new int[0];         // 3 per triangle, already wound the game's way (flip applied)
        public uint[] TriangleMaterial = new uint[0];

        public int TriangleCount { get { return Triangles.Length / 3; } }

        public static bool Has(uint material, int bit) { return (material & (1u << bit)) != 0; }

        public static MpCollision Read(MpArea area)
        {
            var c = new MpCollision();
            if (area.CollisionSection < 0 || area.CollisionSection >= area.SectionCount) return c;
            byte[] b = area.Data;
            int start = area.SecAt[area.CollisionSection];
            // "++collisionData; align to 4": skip the leading word
            int p = start + 4;
            int size = (int)MpReader.U32At(b, p);
            p += 4;
            var r = new MpReader(b, p, size);
            r.U32();             // magic 0xDEAFBABE
            r.U32();             // version
            r.Skip(24);          // bounds
            r.U32();             // root node type
            int treeSize = r.S32();
            r.Skip(treeSize);

            int matCount = r.S32();
            var mats = new uint[matCount];
            for (int i = 0; i < matCount; i++) mats[i] = r.U32();
            int vm = r.S32(); r.Skip(vm);
            int em = r.S32(); r.Skip(em);
            int pm = r.S32();
            var polyMat = new byte[pm];
            for (int i = 0; i < pm; i++) polyMat[i] = r.U8();
            int edgeCount = r.S32();
            var edges = new ushort[edgeCount * 2];
            for (int i = 0; i < edgeCount * 2; i++) edges[i] = r.U16();
            int polyEdgeCount = r.S32();
            var polyEdges = new ushort[polyEdgeCount];
            for (int i = 0; i < polyEdgeCount; i++) polyEdges[i] = r.U16();
            int vertCount = r.S32();
            c.Vertices = new float[vertCount * 3];
            for (int i = 0; i < vertCount * 3; i++) c.Vertices[i] = r.F32();

            int polys = polyEdgeCount / 3;
            c.Triangles = new int[polys * 3];
            c.TriangleMaterial = new uint[polys];
            for (int t = 0; t < polys; t++)
            {
                int e0 = polyEdges[t * 3], e1 = polyEdges[t * 3 + 1];
                int a0 = edges[e0 * 2], a1 = edges[e0 * 2 + 1];
                int b0 = edges[e1 * 2], b1 = edges[e1 * 2 + 1];
                int v2 = (b0 != a0 && b0 != a1) ? b0 : b1;
                uint m = t < polyMat.Length && polyMat[t] < mats.Length ? mats[polyMat[t]] : 0;
                c.TriangleMaterial[t] = m;
                // GetMasterListTriangle: flipped -> (v[e0.2], v[e0.1], third), else (v[e0.1], v[e0.2], third)
                if (Has(m, FlippedTri)) { c.Triangles[t * 3] = a1; c.Triangles[t * 3 + 1] = a0; }
                else { c.Triangles[t * 3] = a0; c.Triangles[t * 3 + 1] = a1; }
                c.Triangles[t * 3 + 2] = v2;
            }

            return c;
        }

        /// <summary>
        /// DCLN (decomp CCollidableOBBTreeGroupContainer / COBBTree): u32 tree count; per tree u32 magic, u32 version, u32 memory size,
        /// index data (materials u32[], vertex / edge / surface material u8[], edges (u16 pair)[], surface edge indices u16[], vertices
        /// float3[] - each a u32 count first), then the OBB node tree (3x4 transform, float3 extents, bool leaf; leaf: u16[] surfaces;
        /// else two child nodes). All trees' triangles go into one list (same triangle rule as the area tree).
        /// </summary>
        public static MpCollision ReadDcln(byte[] b)
        {
            var c = new MpCollision();
            var r = new MpReader(b);
            int trees = r.S32();
            var verts = new List<float>();
            var tris = new List<int>();
            var mats = new List<uint>();
            for (int t = 0; t < trees; t++)
            {
                r.U32(); r.U32(); r.U32();
                int mc = r.S32();
                var m = new uint[mc];
                for (int i = 0; i < mc; i++) m[i] = r.U32();
                int n = r.S32(); r.Skip(n);
                n = r.S32(); r.Skip(n);
                int pm = r.S32();
                var polyMat = new byte[pm];
                for (int i = 0; i < pm; i++) polyMat[i] = r.U8();
                int ec = r.S32();
                var edges = new ushort[ec * 2];
                for (int i = 0; i < ec * 2; i++) edges[i] = r.U16();
                int si = r.S32();
                var surf = new ushort[si];
                for (int i = 0; i < si; i++) surf[i] = r.U16();
                int vc = r.S32();
                int baseVert = verts.Count / 3;
                for (int i = 0; i < vc * 3; i++) verts.Add(r.F32());
                SkipNode(r);
                for (int p = 0; p < si / 3; p++)
                {
                    int e0 = surf[p * 3], e1 = surf[p * 3 + 1];
                    int a0 = edges[e0 * 2], a1 = edges[e0 * 2 + 1], b0 = edges[e1 * 2], b1 = edges[e1 * 2 + 1];
                    int v2 = (b0 != a0 && b0 != a1) ? b0 : b1;
                    uint mat = p < polyMat.Length && polyMat[p] < m.Length ? m[polyMat[p]] : 0;
                    if (Has(mat, FlippedTri)) { tris.Add(baseVert + a1); tris.Add(baseVert + a0); }
                    else { tris.Add(baseVert + a0); tris.Add(baseVert + a1); }
                    tris.Add(baseVert + v2);
                    mats.Add(mat);
                }
            }

            c.Vertices = verts.ToArray();
            c.Triangles = tris.ToArray();
            c.TriangleMaterial = mats.ToArray();
            return c;
        }

        private static void SkipNode(MpReader r)
        {
            r.Skip(48 + 12);
            bool leaf = r.Bool();
            if (leaf)
            {
                int n = r.S32();
                r.Skip(n * 2);
                return;
            }

            SkipNode(r);
            SkipNode(r);
        }

        /// <summary>The surface kind a footstep or a shot hit sounds like (the first matching EMaterialTypes bit 1..17).</summary>
        public static int SurfaceKind(uint m)
        {
            for (int bit = 1; bit <= 17; bit++) if (Has(m, bit)) return bit;
            return 0;
        }
    }
}
