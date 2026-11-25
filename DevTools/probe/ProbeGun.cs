using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    static partial void ExtraImpl8(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "cinf":
            {
                var s = MpSkeleton.Read(Assets.Read(ParseId(args[1])));
                foreach (var id in s.BuildOrder) { var b = s.Bones[id]; Console.WriteLine("  bone " + b.Id + " '" + b.Name + "' parent " + b.Parent + " at (" + b.X.ToString("0.###") + ", " + b.Y.ToString("0.###") + ", " + b.Z.ToString("0.###") + ")"); }
                result = 0; break;
            }
            case "cmdl":
            {
                var g = MpGeometry.ReadCmdl(Assets.Read(ParseId(args[1])), out var sets);
                Console.WriteLine("flags " + g.Flags.ToString("X") + " bounds " + string.Join(" ", g.Bounds.Select(v => v.ToString("0.###"))) + " surfaces " + g.Surfaces.Count + " tris " + g.Surfaces.Sum(x => x.Tris.Length / 3) + " sets " + sets.Count + " textures " + string.Join(",", sets[0].TextureIds.Select(t => t.ToString("X8"))));
                if (args.Length > 2) PrintMats(sets[int.Parse(args[2])]);
                result = 0; break;
            }
            case "animset":
            {
                uint ancsId = ParseId(args[1]);
                byte[] ab = Assets.Read(ancsId);
                var a = MpAncs.Read(ab);
                var set = MpAnimSet.Read(ab, a.AnimSetOffset);
                var c = a.Characters[0];
                Console.WriteLine("set " + set.Entries.Count + " entries, char index list " + string.Join(",", c.AnimIndex)); for (int i = 0; i < set.Entries.Count; i++) Console.WriteLine("  entry " + i + " " + set.Entries[i].Name + " type " + set.Entries[i].Type + " " + string.Join(",", set.Entries[i].Anims.Select(x => x.ToString("X8"))));
                for (int i = 0; i < c.AnimNames.Count; i++)
                {
                    uint anim = set.AnimOf(c, c.AnimNames[i].Key);
                    byte[] d = MpAssets.Valid(anim) ? Assets.Read(anim) : null;
                    string info = d == null ? "missing" : "fmt " + MpReader.U32At(d, 0) + " len " + d.Length;
                    if (d != null && MpReader.U32At(d, 0) == 2) info += " " + BitConverter.ToString(d, 4, 64);
                    if (d != null) { var m = MpAnimation.Read(d); info += " dur " + m.Duration.ToString("0.###") + " keys " + m.KeyCount + " bones " + m.BoneCount; }
                    Console.WriteLine("  " + c.AnimNames[i].Key + " " + c.AnimNames[i].Value + " -> ANIM " + anim.ToString("X8") + " " + info);
                }
                result = 0; break;
            }
            case "cskr":
            {
                var k = MpSkin.Read(Assets.Read(ParseId(args[1])));
                Console.WriteLine("vbones " + k.Groups.Count + " points " + k.PointCount + " normals " + k.NormalCount + " sum " + k.Groups.Sum(x => x.Count));
                foreach (var gr in k.Groups.Take(int.Parse(args.Length > 2 ? args[2] : "12"))) Console.WriteLine("  " + gr.Count + ": " + string.Join(" ", gr.Bones.Select((bn, i) => bn + "x" + gr.Weights[i].ToString("0.##"))));
                result = 0; break;
            }
            default: ExtraImpl9(cmd, args, ref result); break;
        }
    }
    static partial void ExtraImpl9(string cmd, string[] args, ref int result);
}
