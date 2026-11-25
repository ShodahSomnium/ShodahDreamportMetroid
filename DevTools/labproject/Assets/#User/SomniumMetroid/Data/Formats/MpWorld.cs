using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// MLVL (decomp CWorld.cpp + CGameArea.cpp, version 17 on the disc): magic 0xDEAFBABE, version, STRG name, SAVW, skybox CMDL,
    /// relays (u32 count; u32 relay id, u32 target, u16 message, u8 active), u32 area count, u32 (1), the areas (STRG name, 3x4
    /// transform, AABB, MREA id, area id (version &gt; 15), attached areas (u32 count + u16), deps1 and deps2 (u32 count + (id, FourCC)),
    /// layer dependency offsets (u32 count + u32), docks (u32 count; refs (u32 count + area u32 + dock u32), u32 vertex count +
    /// float3 in area space, transformed)), MAPW, world script layer, audio groups (u32 count + group id + AGSC), default track
    /// name, layers (areas: u32 count + (u32 layer count, u64 active bits); names; area name offsets).
    /// </summary>
    public sealed class MpWorld
    {
        public sealed class Dock
        {
            public readonly List<KeyValuePair<int, int>> Refs = new List<KeyValuePair<int, int>>();   // (area index, dock index)
            public readonly List<float[]> Plane = new List<float[]>();    // world space (game Z up)
        }

        public sealed class Area
        {
            public int Index;
            public uint NameStrg, Mrea, AreaId;
            public float[] Transform = new float[12];
            public float[] Bounds = new float[6];         // world space, transformed
            public int[] Attached = new int[0];
            public readonly List<Dock> Docks = new List<Dock>();
            public uint LayerCount;
            public ulong LayerBits;
            public string Name;          // English, from the STRG (filled by the caller)
            public readonly List<string> LayerNames = new List<string>();
        }

        public uint Id, NameStrg, Savw, Skybox, Mapw;
        public int Version;
        public readonly List<Area> Areas = new List<Area>();
        public readonly List<KeyValuePair<uint, uint>> AudioGroups = new List<KeyValuePair<uint, uint>>();   // (group id, AGSC)
        public string DefaultTrack;

        public static MpWorld Read(byte[] b, uint id)
        {
            var r = new MpReader(b);
            var w = new MpWorld { Id = id };
            if (r.U32() != 0xDEAFBABE) throw new FormatException("not an MLVL");
            w.Version = r.S32();
            w.NameStrg = r.U32();
            if (w.Version >= 15) w.Savw = r.U32();
            if (w.Version >= 12) w.Skybox = r.U32();
            if (w.Version >= 17)
            {
                int relays = r.S32();
                r.Skip(relays * 11);
            }

            int areaCount = r.S32();
            r.U32();
            for (int i = 0; i < areaCount; i++)
            {
                var a = new Area { Index = i };
                a.NameStrg = r.U32();
                for (int k = 0; k < 12; k++) a.Transform[k] = r.F32();
                var box = new float[6];
                for (int k = 0; k < 6; k++) box[k] = r.F32();
                a.Bounds = TransformBox(a.Transform, box);
                a.Mrea = r.U32();
                if (w.Version > 15) a.AreaId = r.U32();
                int att = r.S32();
                a.Attached = new int[att];
                for (int k = 0; k < att; k++) a.Attached[k] = r.U16();
                int d1 = r.S32();
                r.Skip(d1 * 8);
                int d2 = r.S32();
                r.Skip(d2 * 8);
                if (w.Version > 13)
                {
                    int lo = r.S32();
                    r.Skip(lo * 4);
                }

                int docks = r.S32();
                for (int d = 0; d < docks; d++)
                {
                    var dock = new Dock();
                    int refs = r.S32();
                    for (int k = 0; k < refs; k++)
                    {
                        int area = r.S32();
                        int di = r.S32();
                        dock.Refs.Add(new KeyValuePair<int, int>(area, di));
                    }

                    int verts = r.S32();
                    for (int k = 0; k < verts; k++)
                    {
                        float x = r.F32(), y = r.F32(), z = r.F32();
                        dock.Plane.Add(Apply(a.Transform, x, y, z));
                    }

                    a.Docks.Add(dock);
                }

                w.Areas.Add(a);
            }

            w.Mapw = r.U32();
            // the world's own script layer (SCLY-style: u8 version?, u32 count, objects); MP1 worlds keep it empty
            SkipScriptLayer(r);
            if (w.Version > 10)
            {
                int groups = r.S32();
                for (int i = 0; i < groups; i++)
                {
                    uint gid = r.U32();
                    uint agsc = r.U32();
                    w.AudioGroups.Add(new KeyValuePair<uint, uint>(gid, agsc));
                }
            }

            if (w.Version > 12) w.DefaultTrack = r.CString();
            if (w.Version > 14 && r.Remaining > 4)
            {
                int n = r.S32();
                for (int i = 0; i < n && i < w.Areas.Count; i++)
                {
                    w.Areas[i].LayerCount = r.U32();
                    w.Areas[i].LayerBits = r.U64();
                }

                int names = r.S32();
                var all = new List<string>();
                for (int i = 0; i < names; i++) all.Add(r.CString());
                int offs = r.S32();
                var starts = new int[offs];
                for (int i = 0; i < offs; i++) starts[i] = r.S32();
                for (int i = 0; i < w.Areas.Count && i < offs; i++)
                {
                    Area a = w.Areas[i];
                    for (int k = 0; k < a.LayerCount && starts[i] + k < all.Count; k++) a.LayerNames.Add(all[starts[i] + k]);
                }
            }

            return w;
        }

        private static void SkipScriptLayer(MpReader r)
        {
            r.U8();
            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                r.U8();
                int size = r.S32();
                r.Skip(size);
            }
        }

        public static float[] Apply(float[] m, float x, float y, float z)
        {
            return new[]
            {
                m[0] * x + m[1] * y + m[2] * z + m[3],
                m[4] * x + m[5] * y + m[6] * z + m[7],
                m[8] * x + m[9] * y + m[10] * z + m[11],
            };
        }

        public static float[] TransformBox(float[] m, float[] b)
        {
            var o = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < 8; i++)
            {
                float[] p = Apply(m, (i & 1) != 0 ? b[3] : b[0], (i & 2) != 0 ? b[4] : b[1], (i & 4) != 0 ? b[5] : b[2]);
                for (int c = 0; c < 3; c++)
                {
                    o[c] = Math.Min(o[c], p[c]);
                    o[c + 3] = Math.Max(o[c + 3], p[c]);
                }
            }

            return o;
        }
    }

    /// <summary>
    /// MREA (decomp CGameArea::PostConstructArea, version 15): 0x60-byte header (magic 0xDEADBEEF, version, 3x4 transform, model
    /// count, section count, geometry / script / collision / unknown / lights / visibility / path / render-octree section indices),
    /// the section size table (padded to 32), the sections. Geometry = material set, then per world model 7 sections + its surfaces.
    /// </summary>
    public sealed class MpArea
    {
        public int Version, ModelCount, SectionCount;
        public int GeometrySection, ScriptSection, CollisionSection, UnknownSection, LightSection, VisibilitySection, PathSection, OctreeSection;
        public float[] Transform = new float[12];
        public byte[] Data;
        public int[] SecAt, SecLen;
        public MpMaterialSet Materials;
        public readonly List<MpGeometry> Models = new List<MpGeometry>();

        public static MpArea Read(byte[] b, bool geometry)
        {
            var r = new MpReader(b);
            var a = new MpArea { Data = b };
            if (r.U32() != 0xDEADBEEF) throw new FormatException("not an MREA");
            a.Version = r.S32();
            for (int i = 0; i < 12; i++) a.Transform[i] = r.F32();
            a.ModelCount = r.S32();
            a.SectionCount = r.S32();
            a.GeometrySection = r.S32();
            a.ScriptSection = r.S32();
            a.CollisionSection = r.S32();
            a.UnknownSection = r.S32();
            a.LightSection = r.S32();
            a.VisibilitySection = r.S32();
            a.PathSection = r.S32();
            a.OctreeSection = r.S32();
            int table = r.Pos;
            a.SecAt = new int[a.SectionCount];
            a.SecLen = new int[a.SectionCount];
            int cur = (table + a.SectionCount * 4 + 31) & ~31;
            for (int i = 0; i < a.SectionCount; i++)
            {
                a.SecAt[i] = cur;
                a.SecLen[i] = (int)MpReader.U32At(b, table + i * 4);
                cur += a.SecLen[i];
            }

            if (geometry) a.ReadGeometry();
            return a;
        }

        public void ReadGeometry()
        {
            int s = GeometrySection;
            Materials = MpMaterialSet.Read(Data, SecAt[s], SecLen[s]);
            s++;
            for (int i = 0; i < ModelCount; i++) Models.Add(MpGeometry.ReadWorldModel(Data, SecAt, SecLen, ref s, Materials));
        }

        public MpReader Section(int index) { return new MpReader(Data, SecAt[index], SecLen[index]); }
    }
}
