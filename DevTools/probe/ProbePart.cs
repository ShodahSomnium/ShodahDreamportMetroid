using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;

static partial class Probe
{
    /// <summary>partdump id [frames]: a PART's drawing flags, then after N frames at 60 Hz each live system's particle spread (distance from the origin, size, velocity).</summary>
    public static void PartDump(string[] args)
    {
        var lib = new MpPartLibrary(Assets);
        uint id = ParseId(args[0]);
        int frames = args.Length > 1 ? int.Parse(args[1]) : 60;
        var g = new MpElementGen(lib.Part(id), lib);
        g.SetGlobalScale(new V3(2, 2, 2));
        for (int f = 0; f < frames; f++) g.Update(1.0 / 60.0);
        var all = new List<IPartGen>();
        g.Collect(all);
        foreach (IPartGen s in all)
        {
            var e = s as MpElementGen;
            if (e == null) { Console.WriteLine("  " + s.Kind); continue; }
            MpGenDesc d = e.Desc;
            string flags = (d.LINE ? " LINE" : "") + (d.ORNT ? " ORNT" : "") + (d.RSOP ? " RSOP" : "") + (d.MBLR ? " MBLR" : "") + (d.AAPH ? " AAPH" : "") + (d.ZBUF ? " ZBUF" : "") + (d.VMD1 ? " VMD1" : "") + (d.VMD2 ? " VMD2" : "") + (MpSomniumValid(d.PMDL) ? " PMDL" : "") + (d.LIT_ ? " LIT" : "");
            Console.WriteLine("  PART " + d.Id.ToString("X8") + flags + " particles " + e.Particles.Count + " scale " + e.ScaleFactor.ToString("0.00") + " global " + e.GlobalScale.X.ToString("0.00"));
            if (e.Particles.Count == 0) continue;
            float dMin = float.MaxValue, dMax = 0, sMin = float.MaxValue, sMax = 0, vMax = 0, wMax = 0, stepMax = 0, lineMax = 0;
            foreach (MpParticle p in e.Particles)
            {
                float dist = (float)Math.Sqrt(p.Pos.MagSquared);
                dMin = Math.Min(dMin, dist); dMax = Math.Max(dMax, dist);
                sMin = Math.Min(sMin, p.LineLengthOrSize); sMax = Math.Max(sMax, p.LineLengthOrSize);
                vMax = Math.Max(vMax, (float)Math.Sqrt(p.Vel.MagSquared));
                wMax = Math.Max(wMax, p.LineWidthOrRota);
                float st = (float)Math.Sqrt((p.Pos - p.PrevPos).MagSquared);
                stepMax = Math.Max(stepMax, st);
                lineMax = Math.Max(lineMax, st * p.LineLengthOrSize);
            }

            Console.WriteLine("    distance " + dMin.ToString("0.000") + ".." + dMax.ToString("0.000") + " size/length " + sMin.ToString("0.000") + ".." + sMax.ToString("0.000") + " width/rota max " + wMax.ToString("0.000") + " speed max " + vMax.ToString("0.000") + " step max " + stepMax.ToString("0.000") + " line max " + lineMax.ToString("0.000") + (d.FXLL ? " FXLL" : ""));
        }
    }

    private static bool MpSomniumValid(uint id) { return id != 0 && id != 0xFFFFFFFF; }
}
