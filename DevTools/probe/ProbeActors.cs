using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    /// <summary>actorhp pak area: every Actor with its health, solid flag, power beam vulnerability, place and links (state>msg>target).</summary>
    public static void ActorHp(string[] args)
    {
        var w = LoadWorld(args[1]);
        var a = MpArea.Read(Assets.Read(w.Areas[int.Parse(args[2])].Mrea), false);
        var objs = MpScriptLayers.Read(a);
        foreach (var o in objs)
        {
            if (o.Type != 0) continue;
            var ac = MpScriptRead.ReadActor(o);
            if (ac == null) continue;
            bool dies = o.Links.Any(l => l.State == MpScriptMsg.StDead);
            if (!dies && args.Length < 4) continue;
            Console.WriteLine("L" + o.Layer + " " + o.Id.ToString("X8") + " '" + o.Name + "' hp " + ac.Health + " solid " + ac.Solid + " active " + ac.Active
                + " power " + (ac.Vuln != null ? ac.Vuln.Of(0, false) : -1) + " pos " + ac.Head.Pos.X.ToString("0.0") + "," + ac.Head.Pos.Y.ToString("0.0") + "," + ac.Head.Pos.Z.ToString("0.0")
                + " ext " + ac.CollisionExtent.X.ToString("0.0") + "," + ac.CollisionExtent.Y.ToString("0.0") + "," + ac.CollisionExtent.Z.ToString("0.0")
                + " links " + string.Join(" ", o.Links.Select(l => l.State + ">" + l.Message + ">" + l.Target.ToString("X8"))));
        }
    }
}
