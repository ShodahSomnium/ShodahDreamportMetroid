using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl11(string cmd, string[] args, ref int result);
    static partial void ExtraImpl11(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "sound":
            {
                var bank = MpSoundBank.Load(Assets);
                Console.WriteLine(bank.Report);
                foreach (var g in bank.Groups) Console.WriteLine("  " + g.BaseDir + " " + g.Name + ": fx " + g.Fx.Count + " macros " + g.Macros.Count + " tables " + g.Tables.Count + " keymaps " + g.Keymaps.Count + " layers " + g.Layers.Count + " samples " + g.SampleDir.Count + " groups " + string.Join(",", g.GroupIds));
                result = 0; break;
            }
            case "gunres": GunRes(); result = 0; break;
            case "partsim": PartSim(args.Skip(1).ToArray()); result = 0; break;
            case "partdump": PartDump(args.Skip(1).ToArray()); result = 0; break;
            case "partuse": PartUse(args.Skip(1).ToArray()); result = 0; break;
            case "swoosh": Swooshes(args.Skip(1).ToArray()); result = 0; break;
            case "partcensus": PartCensus(); result = 0; break;
            case "targeting": Targeting(); result = 0; break;
            case "pal": Pal(args[1], args); result = 0; break;
            case "props":
            {
                var w = LoadWorld(args[1]);
                var a = MpArea.Read(Assets.Read(w.Areas[int.Parse(args[2])].Mrea), false);
                foreach (var o in MpScriptLayers.Read(a))
                    if (o.Id.ToString("X8") == args[3].ToUpperInvariant()) Console.WriteLine(BitConverter.ToString(o.Data, o.PropAt, o.PropEnd - o.PropAt));
                result = 0; break;
            }
            case "sfx":
            {
                var bank = MpSoundBank.Load(Assets);
                for (int a = 1; a < args.Length; a++)
                {
                    int game = int.Parse(args[a].TrimStart('d'));
                    int fx = args[a].StartsWith("d") ? bank.Translate(game) : game;
                    MpSoundGroup g;
                    if (fx < 0 || !bank.FxGroup.TryGetValue(fx, out g)) { Console.WriteLine(game + ": no fx (" + fx + ")"); continue; }
                    var f = g.Fx[fx];
                    Console.WriteLine(game + " -> fx " + fx + " in " + g.Name + ": macro " + f.Macro.ToString("X4") + " key " + f.Key + " vol " + f.Volume + " pan " + f.Pan + " prio " + f.Priority + " max " + f.MaxVoices);
                    DumpMacro(g, f.Macro, 0);
                }
                result = 0; break;
            }
            case "actorhp": ActorHp(args); result = 0; break;
            default: ExtraImpl12(cmd, args, ref result); break;
        }
    }

    static void DumpMacro(MpSoundGroup g, int id, int depth)
    {
        if (depth > 2) return;
        if ((id & 0xC000) == 0x8000)
        {
            int o;
            if (!g.Layers.TryGetValue(id & 0x3FFF, out o) && !g.Layers.TryGetValue(id, out o)) { Console.WriteLine("  layer " + id.ToString("X4") + " missing"); return; }
            int n = (int)MpReader.U32At(g.Pool, o);
            for (int i = 0; i < n; i++)
            {
                int e = o + 4 + i * 12;
                int mid = MpReader.U16At(g.Pool, e);
                Console.WriteLine("  layer " + i + ": id " + mid.ToString("X4") + " keys " + g.Pool[e + 2] + "-" + g.Pool[e + 3] + " transpose " + (sbyte)g.Pool[e + 4] + " vol " + g.Pool[e + 5] + " pan " + g.Pool[e + 8]);
                DumpMacro(g, mid, depth + 1);
            }
            return;
        }
        if ((id & 0xC000) == 0x4000) { Console.WriteLine("  keymap " + id.ToString("X4")); return; }
        int steps;
        if (!g.MacroSteps.TryGetValue(id, out steps)) { Console.WriteLine("  macro " + id.ToString("X4") + " missing"); return; }
        Console.WriteLine("  macro " + id.ToString("X4") + " (" + steps + " steps)");
        for (int s = 0; s < steps && s < 40; s++)
        {
            uint p0, p1;
            g.Step(id, s, out p0, out p1);
            int op = (int)(p0 & 0x7F);
            string extra = "";
            if (op == 0x10)
            {
                int smp = (int)((p0 >> 8) & 0xFFFF);
                MpSoundSample sm;
                extra = g.SampleDir.TryGetValue(smp, out sm) ? " sample " + smp + " rate " + sm.Rate + " root " + sm.RootKey + " len " + sm.Length + " type " + sm.Type + " loop " + sm.LoopStart + "+" + sm.LoopLength : " sample " + smp + " MISSING";
            }
            Console.WriteLine("    " + s + ": " + MpSoundGroup.OpName(op) + " " + p0.ToString("X8") + " " + p1.ToString("X8") + extra);
        }
    }
}

static partial class Probe
{
    public static void GunRes()
    {
        var e = Assets.Named("GunRes");
        var r = new MpReader(e.Pak.Read(e));
        int i = 0;
        while (r.Remaining > 0)
        {
            string s = r.CString();
            if (s.Length == 0) break;
            var n = Assets.Named(s);
            string extra = "";
            if (n != null && n.Type == "ANCS")
            {
                var a = MpAncs.Read(Assets.Read(n.Id));
                extra = " chars: " + string.Join("; ", a.Characters.Select(c => c.Name + " cmdl " + c.Cmdl.ToString("X8") + " anims " + c.AnimNames.Count));
            }
            Console.WriteLine(i++ + " " + s + " = " + (n != null ? n.Type + " " + n.Id.ToString("X8") : "missing") + extra);
        }
    }
}

static partial class Probe
{
    public static void Pal(string name, string[] args)
    {
        int n;
        byte[] p = MpTexture.PaletteRgba(Assets.Read(ParseId(name)), out n);
        int step = args.Length > 2 ? int.Parse(args[2]) : 16;
        for (int i = 0; i < n && i < (args.Length > 3 ? int.Parse(args[3]) : n); i += step) Console.WriteLine(i + ": " + p[i * 4] + "," + p[i * 4 + 1] + "," + p[i * 4 + 2] + "," + p[i * 4 + 3]);
    }
}

static partial class Probe
{
    public static void Targeting()
    {
        var e = Assets.Named("Targeting");
        var t = MpTweakTargeting.Read(e.Pak.Read(e));
        System.Func<float[], string> c = a => string.Join(",", a.Select(v => v.ToString("0.##")));
        Console.WriteLine("lockConfirm scale " + t.LockConfirmScale + " col " + c(t.LockConfirmColor) + " clamp " + t.LockConfirmClampMin + ".." + t.LockConfirmClampMax);
        Console.WriteLine("flower scale " + t.TargetFlowerScale + " col " + c(t.TargetFlowerColor) + " clamp " + t.TargetFlowerClampMin + ".." + t.TargetFlowerClampMax);
        Console.WriteLine("squares scale " + t.OuterBeamSquaresScale + " col " + c(t.OuterBeamSquareColor) + " beams " + t.OuterBeamSquareAngles.Length + " x " + t.OuterBeamSquareAngles[0].Length + " power deg " + c(t.OuterBeamSquareAngles[0].Select(v => v * 57.29578f).ToArray()));
        Console.WriteLine("gauge scale " + t.ChargeGaugeScale + " angles deg " + c(t.ChargeGaugeAngles.Select(v => v * 57.29578f).ToArray()) + " nonfull " + c(t.ChargeGaugeNonFullColor) + " clamp " + t.ChargeGaugeClampMin + ".." + t.ChargeGaugeClampMax + " ticks " + t.ChargeTickCount + " pitch deg " + t.ChargeTickAnglePitch * 57.29578f);
        Console.WriteLine("pulse hi " + c(t.ChargeGaugePulseColorHigh) + " lo " + c(t.ChargeGaugePulseColorLow) + " period " + t.ChargeGaugePulsePeriod + " full fade " + t.FullChargeFadeDuration + " crosshairs " + c(t.CrosshairsColor) + " dur " + t.CrosshairsScaleDur);
    }
}

static partial class Probe
{
    public static void PartCensus()
    {
        var lib = new SomniumSpace.Worlds.SomniumMetroid.Data.Particles.MpPartLibrary(Assets);
        var seen = new System.Collections.Generic.HashSet<uint>();
        var counts = new System.Collections.Generic.Dictionary<string, int[]>();
        foreach (var p in Assets.Paks) foreach (var e in p.Entries)
        {
            if (!seen.Add(e.Id)) continue;
            bool ok;
            switch (e.Type)
            {
                case "PART": { var d = lib.Part(e.Id); ok = d != null && d.Complete; break; }
                case "SWHC": { var d = lib.Swoosh(e.Id); ok = d != null && d.Complete; break; }
                case "WPSC": { var d = lib.Weapon(e.Id); ok = d != null && d.Complete; break; }
                case "CRSC": { var d = lib.Response(e.Id); ok = d != null && d.Complete; break; }
                default: continue;
            }
            int[] c; if (!counts.TryGetValue(e.Type, out c)) counts[e.Type] = c = new int[2];
            c[ok ? 0 : 1]++;
        }
        foreach (var kv in counts) Console.WriteLine(kv.Key + ": ok " + kv.Value[0] + ", incomplete " + kv.Value[1]);
        foreach (var g in lib.Problems.GroupBy(s => s.Substring(s.IndexOf("stopped"))).OrderByDescending(g => g.Count()).Take(15)) Console.WriteLine("  " + g.Count() + " x " + g.Key + " e.g. " + g.First());
    }
}

static partial class Probe
{
    public static void Swooshes(string[] ids)
    {
        var lib = new SomniumSpace.Worlds.SomniumMetroid.Data.Particles.MpPartLibrary(Assets);
        foreach (string s in ids)
        {
            var d = lib.Swoosh(ParseId(s));
            if (d == null) { Console.WriteLine(s + " missing"); continue; }
            int side = 0, leng = 0, spln = 0;
            if (d.SIDE != null) d.SIDE.Get(0, ref side);
            if (d.LENG != null) d.LENG.Get(0, ref leng);
            if (d.SPLN != null) d.SPLN.Get(0, ref spln);
            Console.WriteLine(s + ": side " + side + " leng " + leng + " spln " + spln + " ornt " + d.ORNT + " cros " + d.CROS + " aalp " + d.AALP + " zbuf " + d.ZBUF + " texr " + (d.TEXR != null ? d.TEXR.Texture.ToString("X8") + (d.TEXR.Animated ? " anim" : "") : "none") + " llrd " + d.LLRD + " texw " + d.TEXW + " wire " + d.WIRE);
        }
    }
}

static partial class Probe
{
    public static void PartUse(string[] ids)
    {
        var lib = new SomniumSpace.Worlds.SomniumMetroid.Data.Particles.MpPartLibrary(Assets);
        var roots = new System.Collections.Generic.List<uint>();
        foreach (string s in ids)
        {
            uint id = ParseId(s);
            string t = Assets.TypeOf(id);
            if (t == "WPSC") { var w = lib.Weapon(id); roots.Add(w.APSM); roots.Add(w.APS2); var cr = lib.Response(w.COLR); if (cr != null) { roots.Add(cr.ParticleFor(1)); roots.Add(cr.ParticleFor(3)); } }
            else roots.Add(id);
        }
        var seen = new System.Collections.Generic.HashSet<uint>();
        var stack = new System.Collections.Generic.Stack<uint>(roots);
        int line = 0, pmdl = 0, ornt = 0, mblr = 0, sort = 0, tind = 0, selc = 0, lit = 0, n = 0, kssm = 0;
        while (stack.Count > 0)
        {
            uint id = stack.Pop();
            if (id == 0 || id == 0xFFFFFFFF || !seen.Add(id)) continue;
            var d = lib.Part(id); if (d == null) continue;
            n++;
            if (d.LINE) line++; if (d.PMDL != 0 && d.PMDL != 0xFFFFFFFF) pmdl++; if (d.ORNT) ornt++; if (d.MBLR) mblr++; if (d.SORT) sort++; if (d.TIND != null) tind++; if (d.SELC != 0 && d.SELC != 0xFFFFFFFF) selc++; if (d.LTYP != null) lit++; if (d.KSSM != null) kssm++;
            stack.Push(d.ICTS); stack.Push(d.IDTS); stack.Push(d.IITS);
            if (d.KSSM != null) foreach (var kv in d.KSSM) foreach (uint k in kv.Value) stack.Push(k);
        }
        Console.WriteLine(n + " PARTs: LINE " + line + ", PMDL " + pmdl + ", ORNT " + ornt + ", MBLR " + mblr + ", SORT " + sort + ", TIND " + tind + ", SELC " + selc + ", lights " + lit + ", KSSM " + kssm);
    }
}

static partial class Probe
{
    public static void PartSim(string[] ids)
    {
        var lib = new SomniumSpace.Worlds.SomniumMetroid.Data.Particles.MpPartLibrary(Assets);
        foreach (string s in ids)
        {
            uint id = ParseId(s);
            var d = lib.Part(id);
            var g = new SomniumSpace.Worlds.SomniumMetroid.Data.Particles.MpElementGen(d, lib);
            var counts = new System.Collections.Generic.List<string>();
            int f;
            for (f = 0; f < 240; f++)
            {
                g.Update(1.0 / 60.0);
                if (f % 20 == 0) counts.Add(g.ParticleCountAll + "/" + g.Children.Count);
                if (g.IsSystemDeletable) break;
            }
            var all = new System.Collections.Generic.List<SomniumSpace.Worlds.SomniumMetroid.Data.Particles.IPartGen>();
            g.Collect(all);
            Console.WriteLine(s + ": PSLT " + g.PSLT + " MAXP " + g.MAXP + " light " + g.LightType + " -> " + (g.IsSystemDeletable ? "done at frame " + f : "alive at 240") + "; particles/children every 20 frames: " + string.Join(" ", counts));
        }
    }
}
