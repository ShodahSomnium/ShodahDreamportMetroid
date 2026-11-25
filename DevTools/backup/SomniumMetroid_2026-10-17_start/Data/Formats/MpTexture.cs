using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// TXTR: u32 format, u16 width, u16 height, u32 mip count, [palette: u32 format (0 IA8, 1 RGB565, 2 RGB5A3), u16 w, u16 h,
    /// w*h u16 entries] for C4/C8/C14X2, then the mips in GameCube tile order (DolphinCTexture.cpp reads them in one run).
    /// Decoded for Unity: CMPR goes to DXT1 blocks as they are (colours byte-swapped, the 2-bit indices mirrored per row) so it stays
    /// compressed on the GPU; everything else to RGBA32. Rows are kept in the GameCube order (row 0 = t 0): the shaders sample with
    /// the game's own texture coordinates, so nothing is flipped.
    /// </summary>
    public sealed class MpTexture
    {
        public enum GxFormat { I4 = 0, I8 = 1, IA4 = 2, IA8 = 3, C4 = 4, C8 = 5, C14X2 = 6, RGB565 = 7, RGB5A3 = 8, RGBA8 = 9, CMPR = 10 }

        public GxFormat Format;
        public int Width, Height, Mips;
        public bool Dxt1;          // true: Data is DXT1 blocks (8 bytes per 4x4), else RGBA32
        public byte[] Data;        // all mips, Unity's layout for the format
        public bool HasAlpha;      // any texel below 255 alpha (RGBA32 only; DXT1 = 1-bit alpha possible)

        public static MpTexture Read(byte[] b)
        {
            var r = new MpReader(b);
            var t = new MpTexture();
            t.Format = (GxFormat)r.U32();
            t.Width = r.U16();
            t.Height = r.U16();
            t.Mips = Math.Max(1, r.S32());
            ushort[] palette = null;
            int palFormat = 0;
            if (t.Format == GxFormat.C4 || t.Format == GxFormat.C8 || t.Format == GxFormat.C14X2)
            {
                palFormat = r.S32();
                int pw = r.U16(), ph = r.U16();
                palette = new ushort[pw * ph];
                for (int i = 0; i < palette.Length; i++) palette[i] = r.U16();
            }

            // Unity needs whole DXT1 blocks: the game's CMPR mips below 4 texels are dropped (Unity samples the last mip kept)
            if (t.Format == GxFormat.CMPR)
            {
                int keep = 0;
                for (int i = 0; i < t.Mips; i++) if ((t.Width >> i) >= 4 && (t.Height >> i) >= 4) keep = i + 1;
                t.Dxt1 = keep > 0 && t.Width % 4 == 0 && t.Height % 4 == 0;
                if (t.Dxt1)
                {
                    int total = 0;
                    for (int i = 0; i < keep; i++) total += ((t.Width >> i) / 4) * ((t.Height >> i) / 4) * 8;
                    t.Data = new byte[total];
                    int src = r.Pos, dst = 0;
                    for (int i = 0; i < t.Mips; i++)
                    {
                        int w = Math.Max(1, t.Width >> i), h = Math.Max(1, t.Height >> i);
                        if (i < keep) dst = CmprToDxt1(b, src, w, h, t.Data, dst);
                        src += MipBytes(t.Format, w, h);
                    }

                    t.Mips = keep;
                    return t;
                }
            }

            int rgbaTotal = 0;
            for (int i = 0; i < t.Mips; i++) rgbaTotal += Math.Max(1, t.Width >> i) * Math.Max(1, t.Height >> i) * 4;
            t.Data = new byte[rgbaTotal];
            int at = r.Pos, outAt = 0;
            for (int i = 0; i < t.Mips; i++)
            {
                int w = Math.Max(1, t.Width >> i), h = Math.Max(1, t.Height >> i);
                if (at + MipBytes(t.Format, w, h) > b.Length) { t.Mips = Math.Max(1, i); break; }
                DecodeRgba(t.Format, b, at, w, h, palette, palFormat, t.Data, outAt);
                at += MipBytes(t.Format, w, h);
                outAt += w * h * 4;
            }

            for (int i = 3; i < t.Data.Length; i += 4) if (t.Data[i] != 255) { t.HasAlpha = true; break; }
            return t;
        }

        public static int BitsPerPixel(GxFormat f)
        {
            switch (f)
            {
                case GxFormat.I4: case GxFormat.C4: case GxFormat.CMPR: return 4;
                case GxFormat.I8: case GxFormat.IA4: case GxFormat.C8: return 8;
                case GxFormat.RGBA8: return 32;
                default: return 16;
            }
        }

        private static void TileSize(GxFormat f, out int tw, out int th)
        {
            switch (f)
            {
                case GxFormat.I4: case GxFormat.C4: case GxFormat.CMPR: tw = 8; th = 8; break;
                case GxFormat.I8: case GxFormat.IA4: case GxFormat.C8: tw = 8; th = 4; break;
                default: tw = 4; th = 4; break;
            }
        }

        /// <summary>Bytes of one mip as the hardware lays it out (whole tiles).</summary>
        public static int MipBytes(GxFormat f, int w, int h)
        {
            int tw, th;
            TileSize(f, out tw, out th);
            int tx = (w + tw - 1) / tw, ty = (h + th - 1) / th;
            return tx * ty * (f == GxFormat.RGBA8 ? 64 : 32);
        }

        private static int CmprToDxt1(byte[] src, int at, int w, int h, byte[] dst, int dstAt)
        {
            int bw = w / 4, bh = h / 4;     // DXT1 blocks across / down
            int tilesX = (w + 7) / 8;
            int tilesY = (h + 7) / 8;
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                    for (int sub = 0; sub < 4; sub++)
                    {
                        int bx = tx * 2 + (sub & 1), by = ty * 2 + (sub >> 1);
                        int s = at + ((ty * tilesX + tx) * 4 + sub) * 8;
                        if (bx >= bw || by >= bh) continue;
                        int d = dstAt + (by * bw + bx) * 8;
                        dst[d + 0] = src[s + 1];
                        dst[d + 1] = src[s + 0];
                        dst[d + 2] = src[s + 3];
                        dst[d + 3] = src[s + 2];
                        for (int row = 0; row < 4; row++)
                        {
                            int v = src[s + 4 + row];
                            dst[d + 4 + row] = (byte)(((v >> 6) & 3) | (((v >> 4) & 3) << 2) | (((v >> 2) & 3) << 4) | ((v & 3) << 6));
                        }
                    }

            return dstAt + bw * bh * 8;
        }

        private static void Put(byte[] o, int at, int r, int g, int b, int a)
        {
            o[at] = (byte)r;
            o[at + 1] = (byte)g;
            o[at + 2] = (byte)b;
            o[at + 3] = (byte)a;
        }

        private static void Color565(int v, out int r, out int g, out int b)
        {
            r = ((v >> 11) & 31) * 255 / 31;
            g = ((v >> 5) & 63) * 255 / 63;
            b = (v & 31) * 255 / 31;
        }

        private static void Color5A3(int v, out int r, out int g, out int b, out int a)
        {
            if ((v & 0x8000) != 0)
            {
                r = ((v >> 10) & 31) * 255 / 31;
                g = ((v >> 5) & 31) * 255 / 31;
                b = (v & 31) * 255 / 31;
                a = 255;
            }
            else
            {
                a = ((v >> 12) & 7) * 255 / 7;
                r = ((v >> 8) & 15) * 17;
                g = ((v >> 4) & 15) * 17;
                b = (v & 15) * 17;
            }
        }

        private static void Palette(ushort[] pal, int fmt, int index, byte[] o, int at)
        {
            int v = pal != null && index < pal.Length ? pal[index] : 0;
            int r, g, b, a;
            if (fmt == 0) { int i = v & 0xFF; Put(o, at, i, i, i, v >> 8); return; }
            if (fmt == 1) { Color565(v, out r, out g, out b); Put(o, at, r, g, b, 255); return; }
            Color5A3(v, out r, out g, out b, out a);
            Put(o, at, r, g, b, a);
        }

        private static void DecodeRgba(GxFormat f, byte[] s, int at, int w, int h, ushort[] pal, int palFmt, byte[] o, int outAt)
        {
            int tw, th;
            TileSize(f, out tw, out th);
            int tilesX = (w + tw - 1) / tw, tilesY = (h + th - 1) / th;
            int tileBytes = f == GxFormat.RGBA8 ? 64 : 32;
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int t = at + (ty * tilesX + tx) * tileBytes;
                    for (int py = 0; py < th; py++)
                        for (int px = 0; px < tw; px++)
                        {
                            int x = tx * tw + px, y = ty * th + py;
                            int i = py * tw + px;
                            if (x >= w || y >= h) continue;
                            int d = outAt + (y * w + x) * 4;
                            int r, g, b, a, v;
                            switch (f)
                            {
                                case GxFormat.I4:
                                    v = s[t + i / 2];
                                    v = (i & 1) == 0 ? v >> 4 : v & 15;
                                    v *= 17;
                                    Put(o, d, v, v, v, v);
                                    break;
                                case GxFormat.I8:
                                    v = s[t + i];
                                    Put(o, d, v, v, v, v);
                                    break;
                                case GxFormat.IA4:
                                    v = s[t + i];
                                    Put(o, d, (v & 15) * 17, (v & 15) * 17, (v & 15) * 17, (v >> 4) * 17);
                                    break;
                                case GxFormat.IA8:
                                    a = s[t + i * 2];
                                    v = s[t + i * 2 + 1];
                                    Put(o, d, v, v, v, a);
                                    break;
                                case GxFormat.C4:
                                    v = s[t + i / 2];
                                    v = (i & 1) == 0 ? v >> 4 : v & 15;
                                    Palette(pal, palFmt, v, o, d);
                                    break;
                                case GxFormat.C8:
                                    Palette(pal, palFmt, s[t + i], o, d);
                                    break;
                                case GxFormat.C14X2:
                                    Palette(pal, palFmt, ((s[t + i * 2] << 8) | s[t + i * 2 + 1]) & 0x3FFF, o, d);
                                    break;
                                case GxFormat.RGB565:
                                    Color565((s[t + i * 2] << 8) | s[t + i * 2 + 1], out r, out g, out b);
                                    Put(o, d, r, g, b, 255);
                                    break;
                                case GxFormat.RGB5A3:
                                    Color5A3((s[t + i * 2] << 8) | s[t + i * 2 + 1], out r, out g, out b, out a);
                                    Put(o, d, r, g, b, a);
                                    break;
                                case GxFormat.RGBA8:
                                    Put(o, d, s[t + i * 2 + 1], s[t + 32 + i * 2], s[t + 32 + i * 2 + 1], s[t + i * 2]);
                                    break;
                                case GxFormat.CMPR:
                                {
                                    // 8x8 tile of four 4x4 DXT-like blocks
                                    int sub = (py / 4) * 2 + (px / 4);
                                    int blk = t + sub * 8;
                                    int c0 = (s[blk] << 8) | s[blk + 1], c1 = (s[blk + 2] << 8) | s[blk + 3];
                                    int idx = (s[blk + 4 + (py & 3)] >> (6 - (px & 3) * 2)) & 3;
                                    int r0, g0, b0, r1, g1, b1;
                                    Color565(c0, out r0, out g0, out b0);
                                    Color565(c1, out r1, out g1, out b1);
                                    if (idx == 0) Put(o, d, r0, g0, b0, 255);
                                    else if (idx == 1) Put(o, d, r1, g1, b1, 255);
                                    else if (c0 > c1)
                                    {
                                        if (idx == 2) Put(o, d, (2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
                                        else Put(o, d, (r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
                                    }
                                    else
                                    {
                                        if (idx == 2) Put(o, d, (r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
                                        else Put(o, d, 0, 0, 0, 0);
                                    }

                                    break;
                                }
                            }
                        }
                }
        }

        /// <summary>
        /// The palette (TLUT) of a C4 / C8 texture as RGBA bytes, in index order (null otherwise) - the visor palettes
        /// (TXTR_ThermoPalette, TXTR_XRayPalette) are used for their TLUT, not their texels.
        /// </summary>
        public static byte[] PaletteRgba(byte[] b, out int count)
        {
            count = 0;
            var r = new MpReader(b);
            var f = (GxFormat)r.U32();
            r.U16();
            r.U16();
            r.S32();
            if (f != GxFormat.C4 && f != GxFormat.C8 && f != GxFormat.C14X2) return null;
            int fmt = r.S32();
            int pw = r.U16(), ph = r.U16();
            count = pw * ph;
            var pal = new ushort[count];
            for (int i = 0; i < count; i++) pal[i] = r.U16();
            var o = new byte[count * 4];
            for (int i = 0; i < count; i++) Palette(pal, fmt, i, o, i * 4);
            return o;
        }

        /// <summary>
        /// The top mip's raw palette indices (C4 / C8 only; null otherwise) - font textures keep glyph layers in the index bits
        /// (CTextRenderBuffer SetFontPalette), so the indices, not the colours, are what text needs.
        /// </summary>
        public static byte[] RawIndices(byte[] b, out int width, out int height)
        {
            var r = new MpReader(b);
            var f = (GxFormat)r.U32();
            width = r.U16();
            height = r.U16();
            r.S32();
            if (f != GxFormat.C4 && f != GxFormat.C8) return null;
            r.U32();
            int pw = r.U16(), ph = r.U16();
            r.Skip(pw * ph * 2);
            int tw = 8, th = f == GxFormat.C4 ? 8 : 4;
            int tilesX = (width + tw - 1) / tw, tilesY = (height + th - 1) / th;
            var o = new byte[width * height];
            int at = r.Pos;
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int t = at + (ty * tilesX + tx) * 32;
                    for (int py = 0; py < th; py++)
                        for (int px = 0; px < tw; px++)
                        {
                            int x = tx * tw + px, y = ty * th + py, i = py * tw + px;
                            if (x >= width || y >= height || t + i / 2 >= b.Length) continue;
                            int v;
                            if (f == GxFormat.C4) { v = b[t + i / 2]; v = (i & 1) == 0 ? v >> 4 : v & 15; }
                            else v = b[t + i];
                            o[y * width + x] = (byte)v;
                        }
                }

            return o;
        }

        /// <summary>The top mip as RGBA32 rows (GameCube order), for the probe's pictures and for code that needs texels.</summary>
        public byte[] TopRgba()
        {
            if (!Dxt1)
            {
                var o = new byte[Width * Height * 4];
                Buffer.BlockCopy(Data, 0, o, 0, o.Length);
                return o;
            }

            var rgba = new byte[Width * Height * 4];
            int bw = Width / 4;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int blk = ((y / 4) * bw + x / 4) * 8;
                    int c0 = Data[blk] | (Data[blk + 1] << 8), c1 = Data[blk + 2] | (Data[blk + 3] << 8);
                    int idx = (Data[blk + 4 + (y & 3)] >> ((x & 3) * 2)) & 3;
                    int r0, g0, b0, r1, g1, b1;
                    Color565(c0, out r0, out g0, out b0);
                    Color565(c1, out r1, out g1, out b1);
                    int d = (y * Width + x) * 4;
                    if (idx == 0) Put(rgba, d, r0, g0, b0, 255);
                    else if (idx == 1) Put(rgba, d, r1, g1, b1, 255);
                    else if (c0 > c1) Put(rgba, d, idx == 2 ? (2 * r0 + r1) / 3 : (r0 + 2 * r1) / 3, idx == 2 ? (2 * g0 + g1) / 3 : (g0 + 2 * g1) / 3, idx == 2 ? (2 * b0 + b1) / 3 : (b0 + 2 * b1) / 3, 255);
                    else if (idx == 2) Put(rgba, d, (r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
                    else Put(rgba, d, 0, 0, 0, 0);
                }

            return rgba;
        }
    }
}
