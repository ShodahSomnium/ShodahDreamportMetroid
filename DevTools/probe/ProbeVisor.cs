using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    // visormask <pak> <area|all>: actors / platforms whose visor mask is not all visors, or that have x-ray / thermal models
    static partial void ExtraImpl15b(string cmd, string[] args, ref int result)
    {
        if (cmd != "visormask") { result = 2; return; }
        var w = LoadWorld(args[1]);
        var areas = args[2] == "all" ? Enumerable.Range(0, w.Areas.Count) : new[] { int.Parse(args[2]) };
        int masked = 0, xray = 0, thermal = 0, total = 0;
        foreach (int i in areas)
        {
            var a = MpArea.Read(Assets.Read(w.Areas[i].Mrea), false);
            foreach (var o in MpScriptLayers.Read(a))
            {
                MpActorParams p; MpModelRef m; string kind;
                if (o.Type == 0x00) { var x = MpScriptRead.ReadActor(o); if (x == null) continue; p = x.Params; m = x.Model; kind = "Actor"; }
                else if (o.Type == 0x08) { var x = MpScriptRead.ReadPlatform(o); if (x == null) continue; p = x.Params; m = x.Model; kind = "Platform"; }
                else continue;
                total++;
                bool vm = (p.VisorMask & 0xF) != 0xF, xr = MpAssets.Valid(p.XrayModel), th = MpAssets.Valid(p.ThermalModel);
                if (vm) masked++; if (xr) xray++; if (th) thermal++;
                if ((vm || xr || th) && args[2] != "all")
                    Console.WriteLine("L" + o.Layer + " " + kind + " " + o.Id.ToString("X8") + " '" + o.Name + "' model " + (MpAssets.Valid(m.Cmdl) ? "CMDL " + m.Cmdl.ToString("X8") : MpAssets.Valid(m.Ancs) ? "ANCS " + m.Ancs.ToString("X8") : "none") + " visors " + Convert.ToString(p.VisorMask & 0xF, 2).PadLeft(4, '0') + " (xray|thermal|scan|combat) xray " + p.XrayModel.ToString("X8") + " thermal " + p.ThermalModel.ToString("X8"));
            }
        }
        Console.WriteLine("actors+platforms " + total + ": visor-masked " + masked + ", x-ray models " + xray + ", thermal models " + thermal);
        result = 0;
    }
    static partial void ExtraImpl15b(string cmd, string[] args, ref int result);
}
