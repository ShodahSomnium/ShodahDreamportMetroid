using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl6(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "ancscensus":
            {
                int ok = 0, fail = 0; var seen = new HashSet<uint>();
                foreach (var p in Assets.Paks) foreach (var e in p.Entries)
                {
                    if (e.Type != "ANCS" || !seen.Add(e.Id)) continue;
                    try { var a = MpAncs.Read(p.Read(e)); if (a.Characters.Any(c => Assets.TypeOf(c.Cmdl) != "CMDL")) throw new Exception("bad cmdl " + string.Join(",", a.Characters.Select(c => c.Cmdl.ToString("X8")))); ok++; }
                    catch (Exception ex) { fail++; if (fail < 6) Console.WriteLine("FAIL " + e + " " + ex.Message); }
                }
                Console.WriteLine("ANCS ok " + ok + " fail " + fail);
                result = 0; break;
            }
            case "ancs":
            {
                var a = MpAncs.Read(Assets.Read(ParseId(args[1])));
                foreach (var c in a.Characters)
                {
                    Console.WriteLine("char " + c.Id + " '" + c.Name + "' CMDL " + c.Cmdl.ToString("X8") + " CSKR " + c.Cskr.ToString("X8") + " CINF " + c.Cinf.ToString("X8") + " overlay " + c.OverlayCmdl.ToString("X8") + " anims " + c.AnimNames.Count + " parts " + c.Particles.Count + " effects " + c.Effects.Count);
                    foreach (var an in c.AnimNames.Take(40)) Console.WriteLine("   anim " + an.Key + " " + an.Value);
                    foreach (var ef in c.Effects) Console.WriteLine("   effect " + ef.Name + ": " + string.Join(", ", ef.Components.Select(x => x.Type + " " + x.Id.ToString("X8") + " @" + x.Bone)));
                }
                result = 0; break;
            }
            default: ExtraImpl7(cmd, args, ref result); break;
        }
    }
    static partial void ExtraImpl7(string cmd, string[] args, ref int result);
}
