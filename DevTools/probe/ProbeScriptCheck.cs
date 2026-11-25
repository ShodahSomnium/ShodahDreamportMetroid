using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl7(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "sclycheck":
            {
                var ok = new Dictionary<int, int>(); var bad = new Dictionary<int, int>(); int shown = 0;
                foreach (var p in Assets.Paks)
                {
                    var me = p.Entries.FirstOrDefault(x => x.Type == "MLVL"); if (me == null) continue;
                    var w = MpWorld.Read(p.Read(me), me.Id);
                    foreach (var wa in w.Areas)
                    {
                        var a = MpArea.Read(Assets.Read(wa.Mrea), false);
                        foreach (var o in MpScriptLayers.Read(a))
                        {
                            object d = null;
                            switch (o.Type)
                            {
                                case MpScriptType.Actor: d = MpScriptRead.ReadActor(o); break;
                                case MpScriptType.Door: d = MpScriptRead.ReadDoor(o); break;
                                case MpScriptType.SpawnPoint: d = MpScriptRead.ReadSpawnPoint(o); break;
                                case MpScriptType.Trigger: d = MpScriptRead.ReadTrigger(o); break;
                                case MpScriptType.PointOfInterest: d = MpScriptRead.ReadPointOfInterest(o); break;
                                default: continue;
                            }
                            int size = o.PropEnd - o.PropAt;
                            bool good = d != null && MpScriptRead.LastRead == size;
                            var dict = good ? ok : bad; dict[o.Type] = dict.TryGetValue(o.Type, out int c) ? c + 1 : 1;
                            if (!good && shown++ < 10) Console.WriteLine("BAD " + p.Name + " " + o.Type.ToString("X2") + " '" + o.Name + "' props " + o.PropCount + " size " + size + " read " + MpScriptRead.LastRead);
                        }
                    }
                }
                Console.WriteLine("ok: " + string.Join(", ", ok.Select(k => k.Key.ToString("X2") + " " + k.Value)) + " | bad: " + string.Join(", ", bad.Select(k => k.Key.ToString("X2") + " " + k.Value)));
                result = 0; break;
            }
            default: ExtraImpl8(cmd, args, ref result); break;
        }
    }
    static partial void ExtraImpl8(string cmd, string[] args, ref int result);
}
