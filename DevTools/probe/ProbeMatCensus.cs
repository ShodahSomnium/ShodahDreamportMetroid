using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl3(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "matcensus": MatCensus(); result = 0; break;
            default: ExtraImpl4(cmd, args, ref result); break;
        }
    }

    static partial void ExtraImpl4(string cmd, string[] args, ref int result);

    static void Count(Dictionary<string, int> d, string k) { d[k] = d.TryGetValue(k, out int c) ? c + 1 : 1; }

    /// <summary>Every material of every MREA and CMDL on the disc: what the TEV interpreter has to cover.</summary>
    static void MatCensus()
    {
        var stats = new Dictionary<string, Dictionary<string, int>>();
        Dictionary<string, int> S(string n) { if (!stats.TryGetValue(n, out var d)) stats[n] = d = new Dictionary<string, int>(); return d; }
        int mats = 0, fails = 0;
        var seen = new HashSet<uint>();
        void Take(MpMaterialSet set, string kind)
        {
            foreach (var m in set.Materials)
            {
                mats++;
                Count(S("stages " + kind), m.Stages.Length.ToString());
                Count(S("textures"), m.Textures.Length.ToString());
                Count(S("texgens"), m.TexGens.Length.ToString());
                Count(S("konst"), m.Konst.Length.ToString());
                Count(S("blend"), m.BlendSrc + "/" + m.BlendDst);
                Count(S("chans"), m.Channels.Length + ":" + string.Join(",", m.Channels.Select(c => (c & 7).ToString("X"))));
                for (int bit = 0; bit < 20; bit++) if ((m.Flags & (1u << bit)) != 0) Count(S("flag bits"), bit.ToString());
                foreach (var a in m.Anims) Count(S("uv anim"), a.Mode.ToString());
                foreach (var g in m.TexGens) Count(S("texgen"), "type " + MpMaterial.TexGenType(g) + " src " + MpMaterial.TexGenSource(g) + " mtx " + (MpMaterial.TexGenMatrix(g) >= 0 ? "anim" : "id") + " norm " + MpMaterial.TexGenNormalize(g) + " post " + (MpMaterial.TexGenPost(g) >= 0 ? "anim" : "id"));
                foreach (var st in m.Stages)
                {
                    Count(S("cop"), MpMaterial.Stage.Op(st.ColorOps) + " b" + MpMaterial.Stage.Bias(st.ColorOps) + " s" + MpMaterial.Stage.Scale(st.ColorOps) + " c" + (MpMaterial.Stage.Clamp(st.ColorOps) ? 1 : 0));
                    Count(S("aop"), MpMaterial.Stage.Op(st.AlphaOps) + " b" + MpMaterial.Stage.Bias(st.AlphaOps) + " s" + MpMaterial.Stage.Scale(st.AlphaOps) + " c" + (MpMaterial.Stage.Clamp(st.AlphaOps) ? 1 : 0));
                    Count(S("cout/aout"), MpMaterial.Stage.OutReg(st.ColorOps) + "/" + MpMaterial.Stage.OutReg(st.AlphaOps));
                    for (int k = 0; k < 4; k++) { Count(S("cin"), ArgName(st.ColorIn(k), false)); Count(S("ain"), ArgName(st.AlphaIn(k), true)); }
                    Count(S("ras"), st.RasChannel.ToString());
                    Count(S("kcsel"), st.KColorSel.ToString("X"));
                    Count(S("kasel"), st.KAlphaSel.ToString("X"));
                    Count(S("map/coord"), (st.TexMap == 0xFF ? "none" : "map") + "/" + (st.TexCoord == 0xFF ? "none" : "coord"));
                }
            }
        }
        foreach (var p in Assets.Paks)
            foreach (var e in p.Entries)
            {
                if ((e.Type != "MREA" && e.Type != "CMDL") || !seen.Add(e.Id)) continue;
                try
                {
                    byte[] b = p.Read(e);
                    if (e.Type == "MREA") { var a = MpArea.Read(b, false); Take(MpMaterialSet.Read(b, a.SecAt[a.GeometrySection], a.SecLen[a.GeometrySection]), "MREA"); }
                    else { MpGeometry.ReadCmdl(b, out var sets); foreach (var s in sets) Take(s, "CMDL"); }
                }
                catch (Exception ex) { fails++; if (fails < 5) Console.WriteLine("FAIL " + e + " " + ex.Message); }
            }
        Console.WriteLine(mats + " materials, " + fails + " failures");
        foreach (var kv in stats.OrderBy(k => k.Key))
            Console.WriteLine(kv.Key + ": " + string.Join("; ", kv.Value.OrderByDescending(x => x.Value).Select(x => x.Key + " x" + x.Value)));
    }
}
