using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    // enemyinfo <pak> <area> <type hex> [flavor]: each object's patterned info (flavor = name,u32 flavor,xf,scale head; else name,xf,scale) + its ANCS anims
    static partial void ExtraImpl15(string cmd, string[] args, ref int result)
    {
        if (cmd != "enemyinfo") { ExtraImpl16(cmd, args, ref result); return; }
        var w = LoadWorld(args[1]);
        int type = Convert.ToInt32(args[3], 16);
        bool flavor = args.Length > 4 && args[4] == "flavor";
        var seen = new HashSet<uint>();
        var a = MpArea.Read(Assets.Read(w.Areas[int.Parse(args[2])].Mrea), false);
        foreach (var o in MpScriptLayers.Read(a))
        {
            if (o.Type != type) continue;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            string name = r.CString(); uint fl = flavor ? r.U32() : 0;
            var pos = MpVec3.Read(r); var rot = MpVec3.Read(r); var sc = MpVec3.Read(r);
            var p = MpPatternedInfo.Read(r);
            Console.WriteLine(o.Id.ToString("X8") + " '" + name + "' L" + o.Layer + " props " + o.PropCount + " flavor " + fl + " pos " + pos + " scale " + sc + " hp " + p.Health + " speed " + p.Speed + " turn " + p.TurnSpeed + " detect " + p.DetectionRange + "/" + p.DetectionHeight + "/" + p.DetectionAngle + " attack " + p.MinAttackRange + "-" + p.MaxAttackRange + " contact " + p.Contact.Damage + " he " + p.HalfExtent + " h " + p.Height + " active " + p.Active + " ANCS " + p.Anim.Ancs.ToString("X8") + " char " + p.Anim.Character + " anim " + p.Anim.Animation + " deathSfx " + p.DeathSfx + " part1 " + p.Particle1.ToString("X8"));
            var links = string.Join("; ", o.Links.Select(l => l.State + "->" + l.Message + "->" + l.Target.ToString("X8")));
            if (links.Length > 0) Console.WriteLine("    links " + links);
            if (!seen.Add(p.Anim.Ancs) || !MpAssets.Valid(p.Anim.Ancs)) continue;
            var anc = MpAncs.Read(Assets.Read(p.Anim.Ancs));
            var c = anc.Characters[Math.Max(0, Math.Min(anc.Characters.Count - 1, p.Anim.Character))];
            Console.WriteLine("  char '" + c.Name + "' anims: " + string.Join(", ", c.AnimNames.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value)));
        }
        result = 0;
    }
    static partial void ExtraImpl15(string cmd, string[] args, ref int result);
}
