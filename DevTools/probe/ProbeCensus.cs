using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl12(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "sclycensus":
            {
                var w = LoadWorld(args.Length > 1 ? args[1] : "Metroid1.pak");
                var total = new Dictionary<int, int>();
                var where = new Dictionary<int, SortedSet<int>>();
                for (int i = 0; i < w.Areas.Count; i++)
                {
                    var a = MpArea.Read(Assets.Read(w.Areas[i].Mrea), false);
                    foreach (var o in MpScriptLayers.Read(a))
                    {
                        total.TryGetValue(o.Type, out int n); total[o.Type] = n + 1;
                        if (!where.ContainsKey(o.Type)) where[o.Type] = new SortedSet<int>();
                        where[o.Type].Add(i);
                    }
                }
                foreach (var kv in total.OrderByDescending(k => k.Value)) Console.WriteLine(kv.Key.ToString("X2") + " x" + kv.Value + " areas " + string.Join(",", where[kv.Key]));
                result = 0; break;
            }
            default: ExtraImpl13(cmd, args, ref result); break;
        }
    }
    static partial void ExtraImpl12(string cmd, string[] args, ref int result);
    static partial void ExtraImpl13(string cmd, string[] args, ref int result);
}
