using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid;
using SomniumSpace.Worlds.SomniumMetroid.Data;

/// <summary>
/// Offline probe over the pure Data code and the user's own disc image (MP_ISO overrides the path). Usage:
/// dotnet run -c Release -- &lt;command&gt; [args]. Output pictures go to MP_OUT (default the session scratchpad given by the caller).
/// </summary>
static partial class Probe
{
    public static readonly string Iso = Environment.GetEnvironmentVariable("MP_ISO") ?? @"D:\Dreamport\Metroid Prime\ISO\Metroid Prime (Europe) (En,Fr,De,Es,It).ciso";
    public static readonly string Out = Environment.GetEnvironmentVariable("MP_OUT") ?? @"D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\probe\out";

    static MpAssets _assets;
    public static MpAssets Assets { get { return _assets ?? (_assets = new MpAssets(Iso)); } }

    static int Main(string[] args)
    {
        MpLog.Sink = Console.WriteLine;
        Directory.CreateDirectory(Out);
        string cmd = args.Length > 0 ? args[0] : "disc";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            switch (cmd)
            {
                case "disc":
                {
                    using var d = new MpDisc(Iso);
                    Console.WriteLine("id " + d.GameId + " v" + d.DiscVersion + " '" + d.GameName + "' pal " + d.IsPal + ", " + d.Files.Count + " files");
                    foreach (var f in d.Files) Console.WriteLine("  " + f.Path + "  @" + f.Offset.ToString("X") + "  " + f.Size);
                    break;
                }
                case "paks":
                {
                    var a = Assets;
                    Console.WriteLine(a.Count + " resources in " + a.Paks.Count + " paks");
                    foreach (var p in a.Paks)
                    {
                        var types = p.Entries.GroupBy(e => e.Type).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count());
                        Console.WriteLine(p.Name + ": " + p.Entries.Count + " (" + p.Named.Count + " named) " + string.Join(", ", types));
                    }
                    break;
                }
                case "named":
                {
                    string filter = args.Length > 1 ? args[1] : "";
                    foreach (var kv in Assets.AllNamed.OrderBy(k => k.Key))
                        if (kv.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                            Console.WriteLine(kv.Key + " = " + kv.Value);
                    break;
                }
                case "dump":
                {
                    for (int i = 1; i < args.Length; i++)
                    {
                        uint id = ParseId(args[i]);
                        byte[] b = Assets.Read(id);
                        if (b == null) { Console.WriteLine(args[i] + " missing"); continue; }
                        string path = Path.Combine(Out, id.ToString("X8") + "." + Assets.TypeOf(id).ToLowerInvariant());
                        File.WriteAllBytes(path, b);
                        Console.WriteLine(path + " " + b.Length + " bytes");
                    }
                    break;
                }
                default:
                    return Extra(cmd, args);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e);
            return 1;
        }

        Console.WriteLine("done in " + sw.ElapsedMilliseconds + " ms");
        return 0;
    }

    public static uint ParseId(string s)
    {
        if (s.StartsWith("0x")) s = s.Substring(2);
        uint v;
        if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return v;
        var e = Assets.Named(s);
        if (e != null) return e.Id;
        throw new ArgumentException("not an id or a name: " + s);
    }

    static partial void ExtraImpl(string cmd, string[] args, ref int result);
    static int Extra(string cmd, string[] args) { int r = 2; ExtraImpl(cmd, args, ref r); if (r == 2) Console.WriteLine("unknown command " + cmd); return r; }
}
