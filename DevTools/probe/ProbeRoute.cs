using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    // routecensus <pak> <area,area,...>: the script types on the start layers of those rooms (all layers with 'all' as a 3rd arg)
    static partial void ExtraImpl14(string cmd, string[] args, ref int result)
    {
        if (cmd != "routecensus") { ExtraImpl15(cmd, args, ref result); return; }
        var w = LoadWorld(args[1]);
        bool all = args.Length > 3 && args[3] == "all";
        var total = new SortedDictionary<int, int>();
        var where = new Dictionary<int, SortedSet<int>>();
        foreach (string s in args[2].Split(','))
        {
            int i = int.Parse(s);
            var a = MpArea.Read(Assets.Read(w.Areas[i].Mrea), false);
            foreach (var o in MpScriptLayers.Read(a))
            {
                if (!all && (w.Areas[i].LayerBits & (1UL << o.Layer)) == 0) continue;
                total.TryGetValue(o.Type, out int n); total[o.Type] = n + 1;
                if (!where.ContainsKey(o.Type)) where[o.Type] = new SortedSet<int>();
                where[o.Type].Add(i);
            }
        }
        foreach (var kv in total) Console.WriteLine(kv.Key.ToString("X2") + " " + TName(kv.Key).PadRight(26) + " x" + kv.Value + "  areas " + string.Join(",", where[kv.Key]));
        result = 0;
    }
    static partial void ExtraImpl14(string cmd, string[] args, ref int result);
}
