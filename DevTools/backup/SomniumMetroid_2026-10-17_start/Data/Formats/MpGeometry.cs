using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>One surface: one material, its vertices expanded from the display list (per-surface index dedupe), triangles.</summary>
    public sealed class MpSurface
    {
        public int Material;
        public float CenterX, CenterY, CenterZ, NormalX, NormalY, NormalZ;
        public int VertexCount;
        public float[] Pos;          // 3 per vertex, game space (Z up)
        public float[] Nrm;          // 3 per vertex
        public uint[] Color;         // RGBA8 or null
        public float[][] Uv = new float[8][];   // TEX0..TEX7, 2 per vertex, the game's own texture coordinates (null = not used)
        public bool PackedUv0;       // TEX0 came from the packed lightmap array
        public int[] Tris;           // 3 per triangle, the game's winding
    }

    /// <summary>
    /// A model's geometry: CMDL (decomp DolphinCModel.cpp / CModelSectionReader.cpp) or one MREA world model (CGameArea.cpp).
    /// Arrays: positions float3, normals float3 or s16x3 (frac 14: the display list's vertex format 1/2 says which), colours RGBA8,
    /// texcoords float2, packed texcoords u16x2 (frac 15: TEX0 of vertex format 2). Surfaces: 44-byte header (centre, material,
    /// display list size | normal hint bit, parent, next, extra size, normal) + extra (bounds) padded to 32, then the display list
    /// (opcode = primitive | vertex format, u16 count, per vertex one index per attribute present).
    /// </summary>
    public sealed class MpGeometry
    {
        public uint Flags;
        public float[] Bounds = new float[6];
        public uint VisorFlags;              // MREA world model header
        public float[] Transform;            // MREA world model header (3x4 rows)
        public readonly List<MpSurface> Surfaces = new List<MpSurface>();

        private struct Arrays
        {
            public byte[] Buf;
            public int PosAt, PosLen, NrmAt, NrmLen, ClrAt, ClrLen, UvAt, UvLen, PUvAt, PUvLen;
            public bool ShortNormals;
        }

        /// <summary>A CMDL: material sets + geometry. Returns the geometry; the sets come out in <paramref name="sets"/>.</summary>
        public static MpGeometry ReadCmdl(byte[] b, out List<MpMaterialSet> sets)
        {
            var g = new MpGeometry();
            sets = new List<MpMaterialSet>();
            if (MpReader.U32At(b, 0) != 0xDEADBABE) throw new FormatException("not a CMDL");
            uint version = MpReader.U32At(b, 4);
            g.Flags = MpReader.U32At(b, 8);
            for (int i = 0; i < 6; i++) g.Bounds[i] = MpReader.F32At(b, 12 + i * 4);
            int sizeTable = version == 1 ? 0x28 : 0x2C;
            int sectionCount = (int)MpReader.U32At(b, 0x24);
            int matSets = version == 1 ? 1 : (int)MpReader.U32At(b, 0x28);
            var at = new int[sectionCount];
            var len = new int[sectionCount];
            int cur = (sizeTable + sectionCount * 4 + 31) & ~31;
            for (int i = 0; i < sectionCount; i++)
            {
                at[i] = cur;
                len[i] = (int)MpReader.U32At(b, sizeTable + i * 4);
                cur += len[i];
            }

            int s = 0;
            for (int i = 0; i < matSets; i++, s++) sets.Add(MpMaterialSet.Read(b, at[s], len[s]));
            var a = new Arrays { Buf = b, ShortNormals = (g.Flags & 2) != 0 };
            a.PosAt = at[s]; a.PosLen = len[s]; s++;
            a.NrmAt = at[s]; a.NrmLen = len[s]; s++;
            a.ClrAt = at[s]; a.ClrLen = len[s]; s++;
            a.UvAt = at[s]; a.UvLen = len[s]; s++;
            if ((g.Flags & 4) != 0) { a.PUvAt = at[s]; a.PUvLen = len[s]; s++; }
            int surfaceCount = (int)MpReader.U32At(b, at[s]);
            s++;
            for (int i = 0; i < surfaceCount && s < sectionCount; i++, s++)
                g.Surfaces.Add(ReadSurface(b, at[s], len[s], ref a, sets.Count > 0 ? sets[0] : null));
            return g;
        }

        /// <summary>One MREA world model from its seven sections + surfaces (index <paramref name="s"/> = the model header section).</summary>
        public static MpGeometry ReadWorldModel(byte[] b, int[] at, int[] len, ref int s, MpMaterialSet set)
        {
            var g = new MpGeometry();
            var h = new MpReader(b, at[s], len[s]);
            g.VisorFlags = h.U32();
            g.Transform = new float[12];
            for (int i = 0; i < 12; i++) g.Transform[i] = h.F32();
            for (int i = 0; i < 6; i++) g.Bounds[i] = h.F32();
            s++;
            var a = new Arrays { Buf = b };
            a.PosAt = at[s]; a.PosLen = len[s]; s++;
            a.NrmAt = at[s]; a.NrmLen = len[s]; s++;
            a.ClrAt = at[s]; a.ClrLen = len[s]; s++;
            a.UvAt = at[s]; a.UvLen = len[s]; s++;
            a.PUvAt = at[s]; a.PUvLen = len[s]; s++;
            int surfaceCount = (int)MpReader.U32At(b, at[s]);
            s++;
            for (int i = 0; i < surfaceCount; i++, s++) g.Surfaces.Add(ReadSurface(b, at[s], len[s], ref a, set));
            return g;
        }

        private struct Key : IEquatable<Key>
        {
            public int Pos, Nrm, Clr, Fmt;
            public long Tex0123, Tex4567;
            public bool Equals(Key o) { return Pos == o.Pos && Nrm == o.Nrm && Clr == o.Clr && Fmt == o.Fmt && Tex0123 == o.Tex0123 && Tex4567 == o.Tex4567; }
            public override bool Equals(object obj) { return obj is Key && Equals((Key)obj); }
            public override int GetHashCode() { unchecked { return ((Pos * 397 ^ Nrm) * 397 ^ Clr * 31 ^ Fmt) * 397 ^ Tex0123.GetHashCode() * 17 ^ Tex4567.GetHashCode(); } }
        }

        private static MpSurface ReadSurface(byte[] b, int at, int length, ref Arrays a, MpMaterialSet set)
        {
            var s = new MpSurface();
            var r = new MpReader(b, at, length);
            s.CenterX = r.F32(); s.CenterY = r.F32(); s.CenterZ = r.F32();
            s.Material = r.S32();
            int dlSize = (int)(r.U32() & 0x7FFFFFFF);
            r.U32(); r.U32();
            int extra = r.S32();
            s.NormalX = r.F32(); s.NormalY = r.F32(); s.NormalZ = r.F32();
            int headerSize = (44 + extra + 31) & ~31;
            MpMaterial mat = set != null && s.Material >= 0 && s.Material < set.Materials.Length ? set.Materials[s.Material] : null;
            uint desc = mat != null ? mat.VtxDesc : 0x3u | (0x3u << 2) | (0x3u << 8);
            int[] types = new int[11];
            for (int i = 0; i < 11; i++) types[i] = (int)((desc >> (i * 2)) & 3);

            var keys = new Dictionary<Key, int>();
            var keyList = new List<Key>();
            var tris = new List<int>();
            var prim = new List<int>();
            var dl = new MpReader(b, at + headerSize, Math.Min(dlSize, length - headerSize));
            var idx = new int[11];
            while (dl.Remaining > 3)
            {
                int op = dl.U8();
                if (op == 0) continue;          // NOP padding
                int kind = op & 0xF8, fmt = op & 7;
                int count = dl.U16();
                prim.Clear();
                for (int v = 0; v < count; v++)
                {
                    for (int i = 0; i < 11; i++)
                    {
                        switch (types[i])
                        {
                            case 3: idx[i] = dl.U16(); break;
                            case 2: idx[i] = dl.U8(); break;
                            case 1: idx[i] = -1; dl.Skip(DirectSize(i)); break;
                            default: idx[i] = -1; break;
                        }
                    }

                    var k = new Key
                    {
                        Pos = idx[0], Nrm = idx[1], Clr = idx[2], Fmt = fmt,
                        Tex0123 = Pack(idx[4], idx[5], idx[6], idx[7]),
                        Tex4567 = Pack(idx[8], idx[9], idx[10], -1),
                    };
                    int vi;
                    if (!keys.TryGetValue(k, out vi))
                    {
                        vi = keyList.Count;
                        keys[k] = vi;
                        keyList.Add(k);
                    }

                    prim.Add(vi);
                }

                switch (kind)
                {
                    case 0x90:
                        for (int i = 0; i + 2 < prim.Count; i += 3) AddTri(tris, prim[i], prim[i + 1], prim[i + 2]);
                        break;
                    case 0x98:
                        for (int i = 0; i + 2 < prim.Count; i++)
                        {
                            if ((i & 1) == 0) AddTri(tris, prim[i], prim[i + 1], prim[i + 2]);
                            else AddTri(tris, prim[i + 1], prim[i], prim[i + 2]);
                        }

                        break;
                    case 0xA0:
                        for (int i = 1; i + 1 < prim.Count; i++) AddTri(tris, prim[0], prim[i], prim[i + 1]);
                        break;
                    case 0x80:
                        for (int i = 0; i + 3 < prim.Count; i += 4)
                        {
                            AddTri(tris, prim[i], prim[i + 1], prim[i + 2]);
                            AddTri(tris, prim[i], prim[i + 2], prim[i + 3]);
                        }

                        break;
                    // lines and points are not drawn as surfaces
                }
            }

            int n = keyList.Count;
            s.VertexCount = n;
            s.Pos = new float[n * 3];
            s.Nrm = new float[n * 3];
            if (types[2] != 0 && a.ClrLen > 0) s.Color = new uint[n];
            for (int t = 0; t < 7; t++) if (types[4 + t] != 0) s.Uv[t] = new float[n * 2];
            for (int v = 0; v < n; v++)
            {
                Key k = keyList[v];
                ReadVec(a.Buf, a.PosAt, a.PosLen, k.Pos, s.Pos, v * 3);
                bool shortNrm = k.Fmt == 1 || k.Fmt == 2 || a.ShortNormals;
                if (k.Nrm >= 0)
                {
                    if (shortNrm)
                    {
                        int o = a.NrmAt + k.Nrm * 6;
                        if (o + 6 <= a.NrmAt + a.NrmLen)
                            for (int c = 0; c < 3; c++) s.Nrm[v * 3 + c] = (short)MpReader.U16At(a.Buf, o + c * 2) / 16384f;
                    }
                    else ReadVec(a.Buf, a.NrmAt, a.NrmLen, k.Nrm, s.Nrm, v * 3);
                }

                if (s.Color != null && k.Clr >= 0 && a.ClrAt + k.Clr * 4 + 4 <= a.ClrAt + a.ClrLen) s.Color[v] = MpReader.U32At(a.Buf, a.ClrAt + k.Clr * 4);
                for (int t = 0; t < 7; t++)
                {
                    if (s.Uv[t] == null) continue;
                    int ti = Unpack(t < 4 ? k.Tex0123 : k.Tex4567, t & 3);
                    if (ti < 0) continue;
                    if (t == 0 && k.Fmt == 2 && a.PUvLen > 0)
                    {
                        int o = a.PUvAt + ti * 4;
                        if (o + 4 <= a.PUvAt + a.PUvLen)
                        {
                            s.Uv[0][v * 2] = MpReader.U16At(a.Buf, o) / 32768f;
                            s.Uv[0][v * 2 + 1] = MpReader.U16At(a.Buf, o + 2) / 32768f;
                            s.PackedUv0 = true;
                        }
                    }
                    else
                    {
                        int o = a.UvAt + ti * 8;
                        if (o + 8 <= a.UvAt + a.UvLen)
                        {
                            s.Uv[t][v * 2] = MpReader.F32At(a.Buf, o);
                            s.Uv[t][v * 2 + 1] = MpReader.F32At(a.Buf, o + 4);
                        }
                    }
                }
            }

            s.Tris = tris.ToArray();
            return s;
        }

        private static void AddTri(List<int> t, int a, int b, int c)
        {
            if (a == b || b == c || a == c) return;
            t.Add(a); t.Add(b); t.Add(c);
        }

        private static int DirectSize(int attr)
        {
            switch (attr)
            {
                case 0: case 1: return 12;
                case 2: case 3: return 4;
                default: return 8;
            }
        }

        private static long Pack(int a, int b, int c, int d)
        {
            return ((long)(a & 0xFFFF)) | ((long)(b & 0xFFFF) << 16) | ((long)(c & 0xFFFF) << 32) | ((long)(d & 0xFFFF) << 48);
        }

        private static int Unpack(long v, int i)
        {
            int x = (int)((v >> (i * 16)) & 0xFFFF);
            return x == 0xFFFF ? -1 : x;
        }

        private static void ReadVec(byte[] b, int at, int len, int index, float[] dst, int d)
        {
            if (index < 0) return;
            int o = at + index * 12;
            if (o + 12 > at + len) return;
            dst[d] = MpReader.F32At(b, o);
            dst[d + 1] = MpReader.F32At(b, o + 4);
            dst[d + 2] = MpReader.F32At(b, o + 8);
        }
    }
}
