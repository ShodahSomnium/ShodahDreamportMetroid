using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl10(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "frme":
            {
                var f = MpFrame.Read(Assets.Read(ParseId(args[1])));
                Console.WriteLine("version " + f.Version + " widgets " + f.Widgets.Count);
                foreach (var w in f.Widgets.Where(x => x.ParentWidget == null)) Dump(w, 0);
                result = 0; break;
            }
            case "frmeall":
            {
                foreach (var kv in Assets.AllNamed.Where(k => k.Key.StartsWith("FRME_")))
                {
                    try { var f = MpFrame.Read(Assets.Read(kv.Value.Id)); Console.WriteLine(kv.Key + ": " + f.Widgets.Count + " widgets ok"); }
                    catch (Exception e) { Console.WriteLine(kv.Key + ": FAIL " + e.Message); }
                }
                result = 0; break;
            }
            case "dclncensus":
            {
                int ok = 0, fail = 0; var seen = new System.Collections.Generic.HashSet<uint>();
                foreach (var p in Assets.Paks) foreach (var e in p.Entries)
                {
                    if (e.Type != "DCLN" || !seen.Add(e.Id)) continue;
                    try { var c = MpCollision.ReadDcln(p.Read(e)); if (c.TriangleCount == 0) throw new Exception("no triangles"); ok++; }
                    catch (Exception ex) { fail++; if (fail < 5) Console.WriteLine("FAIL " + e + " " + ex.Message); }
                }
                Console.WriteLine("DCLN ok " + ok + " fail " + fail);
                result = 0; break;
            }
            case "wpsc": for (int i = 1; i < args.Length; i++) Wpsc(args[i]); result = 0; break;
            case "font": for (int i = 1; i < args.Length; i++) Font(args[i]); result = 0; break;
            case "spawns": Spawns(args[1], int.Parse(args[2])); result = 0; break;
            default: ExtraImpl11(cmd, args, ref result); break;
        }
    }

    static void Dump(MpFrame.Widget w, int depth)
    {
        string extra = w.Type == "MODL" ? " model " + w.ModelId.ToString("X8") : w.Type == "TXPN" ? " font " + w.FontId.ToString("X8") + " dims " + string.Join(",", w.TextDims.Select(v => v.ToString("0.##"))) + " col " + string.Join(",", w.FontColor.Select(v => v.ToString("0.##"))) : w.Type == "CAMR" ? " cam " + w.Projection + " " + string.Join(",", w.Camera.Select(v => v.ToString("0.##"))) : w.Type == "ENRG" ? " tex " + w.TextureId.ToString("X8") : w.Type == "METR" ? " cap " + w.MeterCapacity + " workers " + w.MeterWorkers : w.Type == "IMGP" ? " coords " + w.ImageCoords.Count : "";
        Console.WriteLine(new string(' ', depth * 2) + w.Type + " '" + w.Name + "'" + (w.Visible ? "" : " (hidden)") + (w.WorkerId >= 0 ? " worker " + w.WorkerId : "") + " at (" + string.Join(", ", w.Translation.Select(v => v.ToString("0.###"))) + ") rgba " + w.R.ToString("0.##") + "," + w.G.ToString("0.##") + "," + w.B.ToString("0.##") + "," + w.A.ToString("0.##") + extra);
        foreach (var c in w.Children) Dump(c, depth + 1);
    }
}
