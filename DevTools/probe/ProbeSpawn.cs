using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    public static void Spawns(string pak, int area)
    {
        var w = LoadWorld(pak);
        var a = MpArea.Read(Assets.Read(w.Areas[area].Mrea), false);
        foreach (var o in MpScriptLayers.Read(a))
        {
            if (o.Type != MpScriptType.SpawnPoint) continue;
            var s = MpScriptRead.ReadSpawnPoint(o);
            Console.WriteLine("L" + o.Layer + " '" + s.Name + "' default " + s.Default + " active " + s.Active + " pos " + s.Pos + " rot " + s.Rot + " items [" + string.Join(",", s.Items) + "]");
        }
    }
}

static partial class Probe
{
    public static void Wpsc(string name)
    {
        var d = MpWeaponDesc.Read(Assets.Read(ParseId(name)));
        Console.WriteLine(name + ": vel (" + d.VelX + "," + d.VelY + "," + d.VelZ + ") speed " + d.Speed + " u/s, life " + d.LifetimeFrames + " frames, part " + d.Particle.ToString("X8") + " part2 " + d.Particle2.ToString("X8") + " sw " + d.Swoosh1.ToString("X8") + "/" + d.Swoosh2.ToString("X8") + "/" + d.Swoosh3.ToString("X8") + " model " + d.Model.ToString("X8") + " colr " + d.Collision.ToString("X8") + " sfx " + d.FlyingSfx + " scale " + d.Scale + (d.StoppedAt != null ? " STOPPED " + d.StoppedAt : "") + " keys " + string.Join(",", d.Keys));
    }
}

static partial class Probe
{
    public static void Font(string name)
    {
        var f = MpFont.Read(Assets.Read(ParseId(name)));
        Console.WriteLine(name + ": v" + f.Version + " '" + f.Name + "' size " + f.Size + " mono " + f.MonoW + "x" + f.MonoH + " base " + f.Baseline + " mode " + f.Mode + " glyphs " + f.Glyphs.Count + " kerns " + f.Kerning.Count + " TXTR " + f.Texture.ToString("X8"));
        foreach (var g in f.Glyphs.Values.Take(4)) Console.WriteLine("  '" + g.Char + "' uv " + g.U0 + "," + g.V0 + " - " + g.U1 + "," + g.V1 + " layer " + g.Layer + " a/b/c " + g.A + "/" + g.B + "/" + g.C + " cell " + g.CellW + "x" + g.CellH + " base " + g.Baseline);
        TexPng(f.Texture);
    }
}
