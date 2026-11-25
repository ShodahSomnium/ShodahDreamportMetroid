using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumMetroid.Data;

/// <summary>
/// "tweak &lt;resource name&gt; &lt;Class&gt;": walks the decompiled constructor src/MetroidPrime/Tweaks/&lt;Class&gt;.cpp statement by statement
/// (ReadFloat / ReadBool / ReadLong, for-loops with literal or ARRAY_SIZE bounds from the header) over the resource from the disc and
/// prints every field with its byte offset - so gameplay numbers come from the file, read in the game's own order.
/// </summary>
static partial class Probe
{
    static readonly string Decomp = @"D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\reference\metaforce";

    static partial void ExtraImpl4(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "tweak": Tweak(args[1], args[2], args.Length > 3 ? args[3] : null); result = 0; break;
            default: ExtraImpl5(cmd, args, ref result); break;
        }
    }

    static partial void ExtraImpl5(string cmd, string[] args, ref int result);

    static void Tweak(string resource, string cls, string filter)
    {
        byte[] b = Assets.Read(ParseId(resource));
        string cpp = File.ReadAllText(Path.Combine(Decomp, "src/MetroidPrime/Tweaks/" + cls + ".cpp"));
        string hpp = File.ReadAllText(Path.Combine(Decomp, "include/MetroidPrime/Tweaks/" + cls + ".hpp"));
        int ctor = cpp.IndexOf(cls + "::" + cls + "(CInputStream");
        int open = cpp.IndexOf('{', cpp.IndexOf(')', ctor));
        int depth = 0, end = open;
        for (int i = open; i < cpp.Length; i++) { if (cpp[i] == '{') depth++; else if (cpp[i] == '}') { depth--; if (depth == 0) { end = i; break; } } }
        string body = cpp.Substring(open + 1, end - open - 1);
        var r = new MpReader(b);
        Run(body, r, hpp, filter, new Dictionary<string, int>());
        Console.WriteLine("read " + r.Pos + " of " + b.Length + " bytes");
    }

    static void Run(string body, MpReader r, string hpp, string filter, Dictionary<string, int> vars)
    {
        int i = 0;
        while (i < body.Length)
        {
            var m = Regex.Match(body.Substring(i), @"^\s*for\s*\(\s*(?:int\s+)?(\w+)\s*=\s*0\s*;\s*\w+\s*<\s*([^;]+);[^)]*\)\s*\{");
            if (m.Success)
            {
                string bound = m.Groups[2].Value.Trim();
                int n = 0;
                var am = Regex.Match(bound, @"ARRAY_SIZE\((\w+)\)");
                if (am.Success) { var hm = Regex.Match(hpp, am.Groups[1].Value + @"\[(\d+)\]"); n = hm.Success ? int.Parse(hm.Groups[1].Value) : 0; }
                else if (!int.TryParse(bound, out n)) { Console.WriteLine("?? loop bound " + bound); n = 0; }
                int start = i + m.Length;
                int d = 1, j = start;
                for (; j < body.Length && d > 0; j++) { if (body[j] == '{') d++; else if (body[j] == '}') d--; }
                string inner = body.Substring(start, j - 1 - start);
                for (int k = 0; k < n; k++) { vars[m.Groups[1].Value] = k; Run(inner, r, hpp, filter, vars); }
                i = j;
                continue;
            }

            int semi = body.IndexOf(';', i);
            if (semi < 0) break;
            string st = body.Substring(i, semi - i).Trim();
            i = semi + 1;
            var calls = Regex.Matches(st, @"in\.(ReadFloat|ReadBool|ReadLong|ReadInt32|ReadUint32|Get< float >|Get< int >|Get< uint >|Get< bool >)\(\)");
            if (calls.Count == 0 && st.Contains("CVector3f(in)"))
            {
                string vn = st.Contains("=") ? st.Substring(0, st.IndexOf('=')).Trim() : st;
                int vat = r.Pos;
                float vx = r.F32(), vy = r.F32(), vz = r.F32();
                if (filter == null || vn.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) Console.WriteLine("  @" + vat.ToString("X3") + " " + vn + " = (" + vx + ", " + vy + ", " + vz + ")");
                continue;
            }

            if (calls.Count == 0)
            {
                if (st.Contains("(in)") || st.Contains("in)")) Console.WriteLine("?? unhandled: " + st);
                continue;
            }

            string name = st.Contains("=") ? st.Substring(0, st.IndexOf('=')).Trim() : st;
            foreach (var kv in vars) name = name.Replace("[" + kv.Key + "]", "[" + kv.Value + "]");
            foreach (Match c in calls)
            {
                int at = r.Pos;
                string v;
                switch (c.Groups[1].Value)
                {
                    case "ReadFloat": case "Get< float >": v = r.F32().ToString("0.####"); break;
                    case "ReadBool": case "Get< bool >": v = r.U8() != 0 ? "true" : "false"; break;
                    default: v = r.S32().ToString(); break;
                }
                if (filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  @" + at.ToString("X3") + " " + name + " = " + v + (st.Contains("M_PIF / 180") ? " (deg)" : ""));
            }
        }
    }
}
