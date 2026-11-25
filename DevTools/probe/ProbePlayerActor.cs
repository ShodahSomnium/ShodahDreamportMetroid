using System;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    // playeractor <pak> <area>: each CScriptPlayerActor's model (ANCS / character) and its skeleton's bones
    static partial void ExtraImpl17(string cmd, string[] args, ref int result);
    static partial void ExtraImpl17(string cmd, string[] args, ref int result)
    {
        if (cmd != "playeractor") { ExtraImpl15b(cmd, args, ref result); return; }
        var w = LoadWorld(args[1]);
        var a = MpArea.Read(Assets.Read(w.Areas[int.Parse(args[2])].Mrea), false);
        foreach (var o in MpScriptLayers.Read(a))
        {
            if (o.Type != 0x4C || o.PropCount != 19) continue;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.ReadHead(r, true);
            MpVec3.Read(r); MpVec3.Read(r);
            r.F32(); r.F32();
            r.U32(); r.F32(); r.F32();
            MpScriptRead.ReadVulnerability(r);
            r.U32();
            MpModelRef m = MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
            Console.WriteLine(o.Id.ToString("X8") + " '" + o.Name + "' ANCS " + m.Ancs.ToString("X8") + " char " + m.Character + " anim " + m.Animation);
            var ancs = MpAncs.Read(Assets.Read(m.Ancs));
            var ch = ancs.Characters[m.Character];
            var s = MpSkeleton.Read(Assets.Read(ch.Cinf));
            foreach (var id in s.BuildOrder) { var b = s.Bones[id]; Console.WriteLine("  bone " + b.Id + " '" + b.Name + "' parent " + b.Parent + " at (" + b.X.ToString("0.##") + ", " + b.Y.ToString("0.##") + ", " + b.Z.ToString("0.##") + ")"); }
            break;
        }

        result = 0;
    }
}
