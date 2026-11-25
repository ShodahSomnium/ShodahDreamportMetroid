using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "texcensus": TexCensus(); result = 0; break;
            case "tex": for (int i = 1; i < args.Length; i++) TexPng(ParseId(args[i])); result = 0; break;
            default: ExtraImpl2(cmd, args, ref result); break;
        }
    }

    static partial void ExtraImpl2(string cmd, string[] args, ref int result);

    /// <summary>Every TXTR: formats, and whether the data length matches whole-tile mips or the game's round-to-4 mips.</summary>
    static void TexCensus()
    {
        var fmt = new Dictionary<string, int>();
        int tileOk = 0, round4Ok = 0, neither = 0, fails = 0, n = 0;
        var seen = new HashSet<uint>();
        foreach (var p in Assets.Paks)
            foreach (var e in p.Entries)
            {
                if (e.Type != "TXTR" || !seen.Add(e.Id)) continue;
                byte[] b = p.Read(e);
                n++;
                try
                {
                    var r = new MpReader(b);
                    var f = (MpTexture.GxFormat)r.U32();
                    int w = r.U16(), h = r.U16(), mips = r.S32();
                    if (f == MpTexture.GxFormat.C4 || f == MpTexture.GxFormat.C8 || f == MpTexture.GxFormat.C14X2) { r.U32(); int pw = r.U16(), ph = r.U16(); r.Skip(pw * ph * 2); }
                    int tile = 0, round4 = 0;
                    for (int i = 0; i < mips; i++)
                    {
                        int mw = Math.Max(1, w >> i), mh = Math.Max(1, h >> i);
                        tile += MpTexture.MipBytes(f, mw, mh);
                        round4 += ((mw + 3) & ~3) * ((mh + 3) & ~3) * MpTexture.BitsPerPixel(f) / 8;
                    }
                    int left = b.Length - r.Pos;
                    if (left == tile) tileOk++; else if (left == round4) round4Ok++; else { neither++; if (neither < 8) Console.WriteLine("  size? " + e + " " + f + " " + w + "x" + h + " mips " + mips + " left " + left + " tile " + tile + " round4 " + round4); }
                    string k = f + (mips > 1 ? " mipped" : "");
                    fmt[k] = fmt.TryGetValue(k, out int c) ? c + 1 : 1;
                    MpTexture.Read(b);
                }
                catch (Exception ex) { fails++; if (fails < 5) Console.WriteLine("  FAIL " + e + ": " + ex.Message); }
            }
        Console.WriteLine(n + " textures; sizes: whole tiles " + tileOk + ", round-4 " + round4Ok + ", neither " + neither + "; decode failures " + fails);
        foreach (var kv in fmt.OrderByDescending(k => k.Value)) Console.WriteLine("  " + kv.Key + ": " + kv.Value);
    }

    static void TexPng(uint id)
    {
        var t = MpTexture.Read(Assets.Read(id));
        string path = Path.Combine(Out, id.ToString("X8") + ".png");
        WritePng(path, t.Width, t.Height, t.TopRgba());
        Console.WriteLine(path + " " + t.Format + " " + t.Width + "x" + t.Height + " mips " + t.Mips + (t.Dxt1 ? " DXT1" : " RGBA") + (t.HasAlpha ? " alpha" : ""));
    }

    public static void WritePng(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4); }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", new byte[0]);
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        uint crc = Crc(td);
        var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
    }

    static uint[] _crc;
    static uint Crc(byte[] b)
    {
        if (_crc == null) { _crc = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crc[n] = c; } }
        uint crc = 0xFFFFFFFF;
        foreach (byte x in b) crc = _crc[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
