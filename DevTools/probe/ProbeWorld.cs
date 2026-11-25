using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl2(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "worlds": Worlds(); result = 0; break;
            case "world": World(args.Length > 1 ? args[1] : "Metroid1.pak"); result = 0; break;
            case "area": Area(args[1], args.Length > 2 ? int.Parse(args[2]) : 0); result = 0; break;
            case "mats": Mats(args[1], args.Length > 2 ? int.Parse(args[2]) : 0); result = 0; break;
            case "strg": foreach (var s in MpStrings.Read(Assets.Read(ParseId(args[1]))).Strings) Console.WriteLine("  " + s); result = 0; break;
            default: ExtraImpl3(cmd, args, ref result); break;
        }
    }

    static partial void ExtraImpl3(string cmd, string[] args, ref int result);

    public static MpWorld LoadWorld(string pak)
    {
        var p = Assets.FindPak(pak);
        var e = p.Entries.First(x => x.Type == "MLVL");
        var w = MpWorld.Read(p.Read(e), e.Id);
        foreach (var a in w.Areas)
        {
            try { a.Name = MpStrings.Read(Assets.Read(a.NameStrg))[0]; } catch { a.Name = "?"; }
        }
        return w;
    }

    static void Worlds()
    {
        foreach (var p in Assets.Paks)
        {
            var e = p.Entries.FirstOrDefault(x => x.Type == "MLVL");
            if (e == null) continue;
            var w = LoadWorld(p.Name);
            string name = "?";
            try { name = MpStrings.Read(Assets.Read(w.NameStrg))[0]; } catch { }
            Console.WriteLine(p.Name + ": MLVL " + e.Id.ToString("X8") + " v" + w.Version + " '" + name + "' areas " + w.Areas.Count + " sky " + w.Skybox.ToString("X8") + " track '" + w.DefaultTrack + "' audio groups " + w.AudioGroups.Count);
        }
    }

    static void World(string pak)
    {
        var w = LoadWorld(pak);
        Console.WriteLine("MLVL " + w.Id.ToString("X8") + " v" + w.Version + " sky " + w.Skybox.ToString("X8") + " track '" + w.DefaultTrack + "'");
        foreach (var g in w.AudioGroups) Console.WriteLine("  audio group " + g.Key + " AGSC " + g.Value.ToString("X8"));
        foreach (var a in w.Areas)
        {
            Console.WriteLine(a.Index + " '" + a.Name + "' MREA " + a.Mrea.ToString("X8") + " id " + a.AreaId.ToString("X8") + " pos (" + a.Transform[3] + ", " + a.Transform[7] + ", " + a.Transform[11] + ") box " + string.Join(" ", a.Bounds.Select(v => v.ToString("0.#"))) + " attached [" + string.Join(",", a.Attached) + "] docks " + a.Docks.Count + " layers " + a.LayerCount + " [" + string.Join("|", a.LayerNames) + "] bits " + a.LayerBits.ToString("X"));
            for (int d = 0; d < a.Docks.Count; d++)
                Console.WriteLine("    dock " + d + " -> " + string.Join(",", a.Docks[d].Refs.Select(r => "area " + r.Key + " dock " + r.Value)) + " plane " + string.Join(" ", a.Docks[d].Plane.Select(p => "(" + p[0].ToString("0.0") + "," + p[1].ToString("0.0") + "," + p[2].ToString("0.0") + ")")));
        }
    }

    static void Area(string pak, int index)
    {
        var w = LoadWorld(pak);
        var wa = w.Areas[index];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = MpArea.Read(Assets.Read(wa.Mrea), true);
        Console.WriteLine("area '" + wa.Name + "' MREA v" + a.Version + " models " + a.ModelCount + " sections " + a.SectionCount + " geo " + a.GeometrySection + " scly " + a.ScriptSection + " coll " + a.CollisionSection + " unk " + a.UnknownSection + " light " + a.LightSection + " vis " + a.VisibilitySection + " path " + a.PathSection + " oct " + a.OctreeSection + " parsed in " + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine("  transform " + string.Join(" ", a.Transform.Select(v => v.ToString("0.##"))));
        Console.WriteLine("  materials " + a.Materials.Materials.Length + " textures " + a.Materials.TextureIds.Length);
        int tris = 0, verts = 0, surfs = 0;
        for (int i = 0; i < a.Models.Count; i++)
        {
            var m = a.Models[i];
            int t = m.Surfaces.Sum(s => s.Tris.Length / 3), v = m.Surfaces.Sum(s => s.VertexCount);
            tris += t; verts += v; surfs += m.Surfaces.Count;
            if (i < 6) Console.WriteLine("  model " + i + " visor " + m.VisorFlags.ToString("X") + " surfaces " + m.Surfaces.Count + " tris " + t + " verts " + v + " bounds " + string.Join(" ", m.Bounds.Select(x => x.ToString("0.#"))) + " packed " + m.Surfaces.Count(s => s.PackedUv0));
        }
        Console.WriteLine("  total surfaces " + surfs + " tris " + tris + " verts " + verts);
    }

    static string ArgName(int c, bool alpha)
    {
        string[] cn = { "CPREV", "APREV", "C0", "A0", "C1", "A1", "C2", "A2", "TEXC", "TEXA", "RASC", "RASA", "ONE", "HALF", "KONST", "ZERO" };
        string[] an = { "APREV", "A0", "A1", "A2", "TEXA", "RASA", "KONST", "ZERO" };
        return alpha ? (c < an.Length ? an[c] : "?" + c) : (c < cn.Length ? cn[c] : "?" + c);
    }

    static string OpName(uint ops)
    {
        int op = MpMaterial.Stage.Op(ops);
        string[] reg = { "PREV", "R0", "R1", "R2" };
        return (op == 0 ? "add" : op == 1 ? "sub" : "op" + op) + " b" + MpMaterial.Stage.Bias(ops) + " s" + MpMaterial.Stage.Scale(ops) + (MpMaterial.Stage.Clamp(ops) ? " clamp" : "") + " ->" + reg[MpMaterial.Stage.OutReg(ops)];
    }

    static void Mats(string pak, int index)
    {
        var w = LoadWorld(pak);
        var a = MpArea.Read(Assets.Read(w.Areas[index].Mrea), true);
        PrintMats(a.Materials);
    }

    public static void PrintMats(MpMaterialSet set)
    {
        for (int i = 0; i < set.Materials.Length; i++)
        {
            var m = set.Materials[i];
            Console.WriteLine("mat " + i + " flags " + m.Flags.ToString("X") + " tex [" + string.Join(",", m.Textures.Select(t => t < set.TextureIds.Length ? set.TextureIds[t].ToString("X8") : "?")) + "] vtx " + m.VtxDesc.ToString("X") + " konst [" + string.Join(",", m.Konst.Select(k => k.ToString("X8"))) + "] blend src " + m.BlendSrc + " dst " + m.BlendDst + " chans [" + string.Join(",", m.Channels.Select(c => c.ToString("X"))) + "]" + (m.IndirectSlot >= 0 ? " ind " + m.IndirectSlot : ""));
            for (int s = 0; s < m.Stages.Length; s++)
            {
                var st = m.Stages[s];
                Console.WriteLine("   tev " + s + " C(" + string.Join(",", Enumerable.Range(0, 4).Select(k => ArgName(st.ColorIn(k), false))) + ") " + OpName(st.ColorOps) + " | A(" + string.Join(",", Enumerable.Range(0, 4).Select(k => ArgName(st.AlphaIn(k), true))) + ") " + OpName(st.AlphaOps) + " | ras " + st.RasChannel + " kc " + st.KColorSel + " ka " + st.KAlphaSel + " map " + st.TexMap + " coord " + st.TexCoord);
            }
            for (int g = 0; g < m.TexGens.Length; g++)
            {
                uint tg = m.TexGens[g];
                Console.WriteLine("   tcg " + g + " type " + MpMaterial.TexGenType(tg) + " src " + MpMaterial.TexGenSource(tg) + " mtx " + MpMaterial.TexGenMatrix(tg) + " norm " + MpMaterial.TexGenNormalize(tg) + " post " + MpMaterial.TexGenPost(tg));
            }
            foreach (var an in m.Anims) Console.WriteLine("   uvanim mode " + an.Mode + " " + an.P0 + " " + an.P1 + " " + an.P2 + " " + an.P3);
        }
    }
}

static partial class Probe
{
    static partial void ExtraImpl5(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "scly":
            {
                var w = LoadWorld(args[1]);
                var a = MpArea.Read(Assets.Read(w.Areas[int.Parse(args[2])].Mrea), false);
                var objs = MpScriptLayers.Read(a);
                string filter = args.Length > 3 ? args[3] : null;
                Console.WriteLine(objs.Count + " objects; by type: " + string.Join(", ", objs.GroupBy(o => o.Type).OrderByDescending(g => g.Count()).Select(g => g.Key.ToString("X2") + " x" + g.Count())));
                foreach (var o in objs)
                {
                    if (filter != null && o.Type.ToString("X2") != filter && o.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("  L" + o.Layer + " " + o.Type.ToString("X2") + " " + o.Id.ToString("X8") + " '" + o.Name + "' props " + o.PropCount + " bytes " + (o.PropEnd - o.PropAt) + " links " + string.Join(" ", o.Links.Select(l => l.State + ">" + l.Message + ">" + l.Target.ToString("X8"))));
                }
                result = 0;
                break;
            }
            default: ExtraImpl6(cmd, args, ref result); break;
        }
    }

    static partial void ExtraImpl6(string cmd, string[] args, ref int result);
}
