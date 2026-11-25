using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// One Metroid Prime material, decoded exactly as the game's CCubeMaterial::SetCurrent walks it (decomp
    /// src/Kyoto/Graphics/CCubeMaterial.cpp): u32 flags, u32 texture count + model texture indices, u32 vertex description
    /// (2 bits per attribute from POS: 0 none, 1 direct, 2 index8, 3 index16), u32 group index, [flags bit 3: u32 count + konst
    /// colours RGBA8], u32 blend (high 16 = destination, low 16 = source factor), [flags bit 10: u32 indirect texture slot],
    /// u32 channel count + channel control words (bit 0 lighting, 1 ambient from vertex, 2 material from vertex, 3..10 light mask,
    /// 11 diffuse fn, 13 attenuation fn), u32 TEV stage count + 5 words each (colour args a,b,c,d 5 bits each; alpha args; colour op
    /// (bits 0-3 op, 4-5 bias, 6-7 scale, 8 clamp, 9-10 out register); alpha op; byte 3 ras channel, byte 2 konst colour select,
    /// byte 1 konst alpha select), one u32 per stage (low byte texcoord, next byte texmap; 0xFF = none), u32 texgen count + words
    /// (bits 0-3 type, 4-8 source, 9-13 matrix (30 + 3 n; 60 identity), 14 normalize, 15-20 post matrix (64 + 3 n; 125 identity)),
    /// u32 UV animation byte size, u32 count + animations (u32 mode, 0/2/4 floats by mode).
    /// </summary>
    public sealed class MpMaterial
    {
        // flags (CCubeMaterial kStateFlag_*)
        public const uint FlagKonst = 1u << 3, FlagDepthSort = 1u << 4, FlagAlphaTest = 1u << 5, FlagReflection = 1u << 6,
            FlagDepthWrite = 1u << 7, FlagReflectionEye = 1u << 8, FlagOccluder = 1u << 9, FlagIndirect = 1u << 10,
            FlagLightmap = 1u << 11, FlagLightmapUvArray = 1u << 13;

        public struct Stage
        {
            public uint ColorArgs, AlphaArgs, ColorOps, AlphaOps, Extra, TexMapCoord;
            public int ColorIn(int i) { return (int)((ColorArgs >> (i * 5)) & 31); }
            public int AlphaIn(int i) { return (int)((AlphaArgs >> (i * 5)) & 31); }
            public int RasChannel { get { return (int)(Extra & 0xFF); } }
            public int KColorSel { get { return (int)((Extra >> 8) & 0xFF); } }
            public int KAlphaSel { get { return (int)((Extra >> 16) & 0xFF); } }
            public int TexCoord { get { return (int)(TexMapCoord & 0xFF); } }
            public int TexMap { get { return (int)((TexMapCoord >> 8) & 0xFF); } }
            public static int Op(uint ops) { return (int)(ops & 15); }
            public static int Bias(uint ops) { return (int)((ops >> 4) & 3); }
            public static int Scale(uint ops) { return (int)((ops >> 6) & 3); }
            public static bool Clamp(uint ops) { return ((ops >> 8) & 1) != 0; }
            public static int OutReg(uint ops) { return (int)((ops >> 9) & 3); }
        }

        public struct UvAnim
        {
            public int Mode;
            public float P0, P1, P2, P3;
        }

        public uint Flags;
        public int[] Textures;       // indices into the set's texture id list, by texmap slot
        public uint VtxDesc;
        public uint GroupIndex;
        public uint[] Konst = new uint[0];     // RGBA8
        public int BlendSrc, BlendDst;         // GX blend factors (0 zero, 1 one, 2 src colour, 3 inv src colour, 4 src alpha, 5 inv src alpha, 6 dst alpha, 7 inv dst alpha)
        public int IndirectSlot = -1;
        public uint[] Channels = new uint[0];
        public Stage[] Stages = new Stage[0];
        public uint[] TexGens = new uint[0];
        public UvAnim[] Anims = new UvAnim[0];

        public bool Has(uint flag) { return (Flags & flag) != 0; }

        /// <summary>The vertex attribute type (0 none, 1 direct, 2 index8, 3 index16) of attribute n from POS (0 POS, 1 NRM, 2 CLR0, 3 CLR1, 4.. TEX0..).</summary>
        public int Attr(int n) { return (int)((VtxDesc >> (n * 2)) & 3); }

        public static int TexGenType(uint g) { return (int)(g & 15); }
        public static int TexGenSource(uint g) { return (int)((g >> 4) & 31); }
        /// <summary>The UV animation driving this texgen's matrix, or -1 for the identity matrix (GX_IDENTITY = 60).</summary>
        public static int TexGenMatrix(uint g) { int m = (int)((g >> 9) & 31) + 30; return m >= 60 ? -1 : (m - 30) / 3; }
        public static bool TexGenNormalize(uint g) { return ((g >> 14) & 1) != 0; }
        /// <summary>The post-transform matrix index (0..19 for PTTEXMTX0..19), or -1 for PTIDENTITY (125).</summary>
        public static int TexGenPost(uint g) { int m = (int)((g >> 15) & 63) + 64; return m >= 125 ? -1 : (m - 64) / 3; }

        public static MpMaterial Read(MpReader r)
        {
            var m = new MpMaterial();
            m.Flags = r.U32();
            int texCount = r.S32();
            m.Textures = new int[texCount];
            for (int i = 0; i < texCount; i++) m.Textures[i] = r.S32();
            m.VtxDesc = r.U32();
            m.GroupIndex = r.U32();
            if (m.Has(FlagKonst))
            {
                int n = r.S32();
                m.Konst = new uint[n];
                for (int i = 0; i < n; i++) m.Konst[i] = r.U32();
            }

            uint blend = r.U32();
            m.BlendSrc = (int)(blend & 0xFFFF);
            m.BlendDst = (int)(blend >> 16);
            if (m.Has(FlagIndirect)) m.IndirectSlot = r.S32();
            int chans = r.S32();
            m.Channels = new uint[chans];
            for (int i = 0; i < chans; i++) m.Channels[i] = r.U32();
            int tevs = r.S32();
            m.Stages = new Stage[tevs];
            for (int i = 0; i < tevs; i++)
            {
                m.Stages[i].ColorArgs = r.U32();
                m.Stages[i].AlphaArgs = r.U32();
                m.Stages[i].ColorOps = r.U32();
                m.Stages[i].AlphaOps = r.U32();
                m.Stages[i].Extra = r.U32();
            }

            for (int i = 0; i < tevs; i++) m.Stages[i].TexMapCoord = r.U32();
            int tcgs = r.S32();
            m.TexGens = new uint[tcgs];
            for (int i = 0; i < tcgs; i++) m.TexGens[i] = r.U32();
            r.U32();     // uv animation byte size
            int anims = r.S32();
            var list = new List<UvAnim>();
            for (int i = 0; i < anims; i++)
            {
                var a = new UvAnim { Mode = r.S32() };
                switch (a.Mode)
                {
                    case 2: case 4: case 5:
                        a.P0 = r.F32(); a.P1 = r.F32(); a.P2 = r.F32(); a.P3 = r.F32();
                        break;
                    case 3: case 7:
                        a.P0 = r.F32(); a.P1 = r.F32();
                        break;
                    case 0: case 1: case 6:
                        break;
                    default:
                        // the game stops at an unknown mode (HandleAnimatedUV returns 0)
                        i = anims;
                        continue;
                }

                list.Add(a);
            }

            m.Anims = list.ToArray();
            return m;
        }
    }

    /// <summary>
    /// A material set (one per CMDL "shader set", one per MREA): u32 texture count + TXTR ids, u32 material count + end offsets
    /// (relative to the first material), the materials.
    /// </summary>
    public sealed class MpMaterialSet
    {
        public uint[] TextureIds = new uint[0];
        public MpMaterial[] Materials = new MpMaterial[0];

        public static MpMaterialSet Read(byte[] data, int offset, int length)
        {
            var r = new MpReader(data, offset, length);
            var s = new MpMaterialSet();
            int texCount = r.S32();
            s.TextureIds = new uint[texCount];
            for (int i = 0; i < texCount; i++) s.TextureIds[i] = r.U32();
            int matCount = r.S32();
            var ends = new int[matCount];
            for (int i = 0; i < matCount; i++) ends[i] = r.S32();
            int start = r.Pos;
            s.Materials = new MpMaterial[matCount];
            for (int i = 0; i < matCount; i++)
            {
                r.Pos = start + (i == 0 ? 0 : ends[i - 1]);
                s.Materials[i] = MpMaterial.Read(r);
            }

            return s;
        }
    }
}
