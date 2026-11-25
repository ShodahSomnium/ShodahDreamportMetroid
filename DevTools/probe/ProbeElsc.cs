using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;

static partial class Probe
{
    // elsc <id|name> [frames]: an ELSC's fields as read (element trees), then the arc generator run for N frames at 60 Hz
    static partial void ExtraImpl16(string cmd, string[] args, ref int result);
    static partial void ExtraImpl16(string cmd, string[] args, ref int result)
    {
        if (cmd != "elsc") { ExtraImpl17(cmd, args, ref result); return; }
        var lib = new MpPartLibrary(Assets);
        uint id = ParseId(args[1]);
        int frames = args.Length > 2 ? int.Parse(args[2]) : 60;
        MpElectricDesc d = lib.Electric(id);
        if (d == null) { Console.WriteLine(id.ToString("X8") + ": not an ELSC (" + Assets.TypeOf(id) + ")"); result = 0; return; }
        Console.WriteLine("ELSC " + id.ToString("X8") + " complete " + d.Complete + (d.Stop != null ? " stop " + d.Stop : ""));
        foreach (string f in new[] { "LIFE", "SLIF", "GRAT", "SCNT", "SSEG", "COLR", "IEMT", "FEMT", "AMPL", "AMPD", "LWD1", "LWD2", "LWD3", "LCL1", "LCL2", "LCL3" })
        {
            object v = typeof(MpElectricDesc).GetField(f).GetValue(d);
            Console.WriteLine("  " + f + " = " + (v == null ? "-" : El(v, 0)));
        }

        Console.WriteLine("  SSWH = " + d.SSWH.ToString("X8") + "  GPSM = " + d.GPSM.ToString("X8") + "  EPSM = " + d.EPSM.ToString("X8") + "  ZERY = " + d.ZERY);
        ElscSim(lib, d, frames);
        result = 0;
    }

    /// <summary>An element tree as text: class name and its constant / child fields.</summary>
    static string El(object o, int depth)
    {
        if (o == null) return "null";
        Type t = o.GetType();
        if (t.IsPrimitive || o is string) return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
        if (o is V3 || o is C4) return Fmt(o);
        if (depth > 6) return t.Name + "{..}";
        if (o is IList list)
        {
            var parts = new StringBuilder("[");
            for (int i = 0; i < list.Count && i < 12; i++) { if (i > 0) parts.Append(", "); parts.Append(El(list[i], depth + 1)); }
            if (list.Count > 12) parts.Append(", ... " + list.Count);
            return parts.Append("]").ToString();
        }

        var sb = new StringBuilder(t.Name);
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fields.Length == 0) return sb.ToString();
        sb.Append("{");
        bool first = true;
        foreach (FieldInfo f in fields)
        {
            if (!first) sb.Append(" ");
            first = false;
            sb.Append(f.Name).Append("=").Append(El(f.GetValue(o), depth + 1));
        }

        return sb.Append("}").ToString();
    }

    static string Fmt(object o)
    {
        if (o is V3 v) return v.ToString();
        var c = (C4)o;
        return "rgba(" + c.R.ToString("0.###") + ", " + c.G.ToString("0.###") + ", " + c.B.ToString("0.###") + ", " + c.A.ToString("0.###") + ")";
    }

    /// <summary>The arc generator for N frames (scale 1, identity placement): live arcs, their extent and lines, the child systems.</summary>
    static void ElscSim(MpPartLibrary lib, MpElectricDesc d, int frames)
    {
        var g = new MpElectricGen(d, lib);
        Console.WriteLine("sim: SSEG+1 " + g.SSEG + " SCNT " + g.SCNT + " LIFE " + g.LIFE + " lines " + (g.Lines != null) + " swooshes " + (g.Swooshes != null) + " gpsm " + (g.Gpsm != null) + " epsm " + (g.Epsm != null));
        int made = 0, prevArcs = 0;
        for (int f = 1; f <= frames; f++)
        {
            g.Update(1.0 / 60.0);
            if (g.Arcs.Count > prevArcs) made += g.Arcs.Count - prevArcs;
            prevArcs = g.Arcs.Count;
            if (f % Math.Max(1, frames / 6) != 0 && f != frames) continue;
            string s = "  frame " + g.CurFrame + ": arcs " + g.Arcs.Count;
            if (g.Arcs.Count > 0 && g.Lines != null)
            {
                MpElectricGen.Arc a = g.Arcs[g.Arcs.Count - 1];
                MpElectricGen.Line l = g.Lines[a.Idx];
                V3 first = l.Verts[0], end = l.Verts[l.Verts.Length - 1];
                float dev = 0f;
                V3 axis = end - first;
                float len = axis.Magnitude;
                if (len > 0f) axis = axis * (1f / len);
                foreach (V3 v in l.Verts) { V3 rel = v - first; dev = Math.Max(dev, (rel - axis * V3.Dot(rel, axis)).Magnitude); }
                s += " | last arc slot " + a.Idx + " slif " + a.Slif + " from " + first + " to " + end + " length " + len.ToString("0.000") + " max off-axis " + dev.ToString("0.000")
                    + " widths " + l.Width1 + "/" + l.Width2 + "/" + l.Width3;
            }

            if (g.Gpsm != null) s += " | gpsm particles " + g.Gpsm.Sum(x => x.ParticleCountAll) + " emitting " + g.Gpsm.Count(x => x.Emission);
            Console.WriteLine(s);
        }

        Console.WriteLine("  arcs started (approx) " + made);
        g.EndLifetime();
        int n = 0;
        while (!g.IsSystemDeletable && n < 600) { g.Update(1.0 / 60.0); n++; }
        Console.WriteLine("  after emission stops: deletable in " + n + " frames");
        if (MpSomniumValid(d.GPSM)) Console.WriteLine("  GPSM " + d.GPSM.ToString("X8") + ": " + (lib.Part(d.GPSM) != null ? "complete " + lib.Part(d.GPSM).Complete : "not a PART"));
    }
}
