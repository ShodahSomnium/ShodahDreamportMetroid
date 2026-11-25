using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// FONT raster font (decomp CRasterFont, PAL reader): "FONT", version (0..4), mono width / height, [v1: baseline], [v2: line
    /// margin], 2 bools, u32, font size, name, TXTR id, mode (0 one layer, 1 one layer + outline, 2 four layers, 3 two layers +
    /// outline, 4 two layers), glyphs (u16 char, start u/v, end u/v floats, [v3: u8 layer], A / B / C spacing, cell width / height,
    /// baseline, kern start - bytes in v4, ints before), kerning pairs (u16, u16, s32).
    /// </summary>
    public sealed class MpFont
    {
        public struct Glyph
        {
            public char Char;
            public float U0, V0, U1, V1;
            public int Layer, A, B, C, CellW, CellH, Baseline, KernStart;
        }

        public int Version, MonoW, MonoH, Baseline, LineMargin, Size, Mode;
        public string Name;
        public uint Texture;
        public readonly Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>();
        public readonly Dictionary<int, int> Kerning = new Dictionary<int, int>();

        public static MpFont Read(byte[] b)
        {
            var f = new MpFont();
            var r = new MpReader(b);
            if (r.FourCC() != "FONT") return f;
            f.Version = r.S32();
            f.MonoW = r.S32();
            f.MonoH = r.S32();
            f.Baseline = f.Version >= 1 ? r.S32() : f.MonoH;
            if (f.Version >= 2) f.LineMargin = r.S32();
            r.Bool(); r.Bool(); r.S32();
            f.Size = r.S32();
            f.Name = r.CString();
            f.Texture = r.U32();
            f.Mode = r.S32();
            int glyphs = r.S32();
            for (int i = 0; i < glyphs; i++)
            {
                var g = new Glyph { Char = (char)r.U16(), U0 = r.F32(), V0 = r.F32(), U1 = r.F32(), V1 = r.F32() };
                g.Layer = f.Version >= 3 ? r.U8() : 0;
                if (f.Version >= 4)
                {
                    g.A = r.S8(); g.B = r.S8(); g.C = r.S8();
                    g.CellW = r.U8(); g.CellH = r.U8(); g.Baseline = r.U8();
                    g.KernStart = r.S16();
                }
                else
                {
                    g.A = (sbyte)r.S32(); g.B = (sbyte)r.S32(); g.C = (sbyte)r.S32();
                    g.CellW = (byte)r.S32(); g.CellH = (byte)r.S32(); g.Baseline = (byte)r.S32();
                    g.KernStart = (short)r.S32();
                }

                f.Glyphs[g.Char] = g;
            }

            int kerns = r.S32();
            for (int i = 0; i < kerns; i++)
            {
                int a = r.U16(), c = r.U16();
                int how = r.S32();
                f.Kerning[(a << 16) | c] = how;
            }

            return f;
        }

        public int Kern(char a, char b)
        {
            int v;
            return Kerning.TryGetValue((a << 16) | b, out v) ? v : 0;
        }
    }
}
