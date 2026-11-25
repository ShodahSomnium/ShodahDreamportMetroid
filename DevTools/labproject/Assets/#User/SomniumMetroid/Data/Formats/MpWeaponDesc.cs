using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// WPSC projectile description ("WPSM", then key FourCC + element until "_END"). Measured on the disc (probe dump PowerBeam):
    /// constant elements are "CNST" + the value - a bool is one byte, an int / asset id four, a real a float, a vector three real
    /// elements, a colour four. Only the constant forms are read; at the first element of another kind (keyframes, random...) the
    /// rest is left at its default. Keys: IVEC initial velocity (game units per frame at 60 Hz), PSLT lifetime (frames), APSM / APS2
    /// main and second particle (PART), ASW1..3 swooshes (SWHC), OHEF projectile model (CMDL), COLR collision response (CRSC),
    /// PJFX flying sound, PSCL scale, PCOL colour, POFS offset, the booleans.
    /// </summary>
    public sealed class MpWeaponDesc
    {
        public float VelX, VelY = 2f, VelZ;
        public int LifetimeFrames = 120;
        public uint Particle = 0xFFFFFFFF, Particle2 = 0xFFFFFFFF, Swoosh1 = 0xFFFFFFFF, Swoosh2 = 0xFFFFFFFF, Swoosh3 = 0xFFFFFFFF, Model = 0xFFFFFFFF, Collision = 0xFFFFFFFF;
        public int FlyingSfx = -1;
        public float Scale = 1f;
        public bool Homing;
        public readonly List<string> Keys = new List<string>();
        public string StoppedAt;

        private static readonly HashSet<string> Bools = new HashSet<string> { "SWTR", "LWTR", "EWTR", "AS13", "AS12", "AS11", "HOMG", "APSO", "AP21", "AP11", "VMD2", "SPS1", "SPS2", "FC60" };
        private static readonly HashSet<string> Assets = new HashSet<string> { "COLR", "APSM", "APS2", "ASW1", "ASW2", "ASW3", "OHEF" };
        private static readonly HashSet<string> Ints = new HashSet<string> { "PSLT", "PJFX" };
        private static readonly HashSet<string> Reals = new HashSet<string> { "TRAT", "RNGE", "FOFF" };
        private static readonly HashSet<string> Vectors = new HashSet<string> { "IVEC", "IORN", "PSOV", "PSCL", "POFS", "OFST" };
        private static readonly HashSet<string> Colors = new HashSet<string> { "PCOL" };

        public static MpWeaponDesc Read(byte[] b)
        {
            var d = new MpWeaponDesc();
            var r = new MpReader(b);
            if (r.FourCC() != "WPSM") return d;
            while (r.Remaining >= 8)
            {
                string key = r.FourCC();
                if (key == "_END") break;
                d.Keys.Add(key);
                string kind = r.FourCC();
                if (kind == "NONE") continue;
                if (kind != "CNST") { d.StoppedAt = key + ":" + kind; break; }
                if (Bools.Contains(key))
                {
                    bool v = r.Bool();
                    if (key == "HOMG") d.Homing = v;
                }
                else if (Assets.Contains(key))
                {
                    uint id = r.U32();
                    switch (key)
                    {
                        case "APSM": d.Particle = id; break;
                        case "APS2": d.Particle2 = id; break;
                        case "ASW1": d.Swoosh1 = id; break;
                        case "ASW2": d.Swoosh2 = id; break;
                        case "ASW3": d.Swoosh3 = id; break;
                        case "OHEF": d.Model = id; break;
                        case "COLR": d.Collision = id; break;
                    }
                }
                else if (Ints.Contains(key))
                {
                    int v = r.S32();
                    if (key == "PSLT") d.LifetimeFrames = v;
                    else d.FlyingSfx = v;
                }
                else if (Reals.Contains(key)) r.F32();
                else if (Vectors.Contains(key))
                {
                    float[] v = new float[3];
                    for (int i = 0; i < 3; i++)
                    {
                        if (r.FourCC() != "CNST") { d.StoppedAt = key + ":vector part"; return d; }
                        v[i] = r.F32();
                    }

                    if (key == "IVEC") { d.VelX = v[0]; d.VelY = v[1]; d.VelZ = v[2]; }
                    else if (key == "PSCL") d.Scale = v[1];
                }
                else if (Colors.Contains(key))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (r.FourCC() != "CNST") { d.StoppedAt = key + ":colour part"; return d; }
                        r.F32();
                    }
                }
                else { d.StoppedAt = key + " (unknown key)"; break; }
            }

            return d;
        }

        public float Speed { get { return (float)System.Math.Sqrt(VelX * VelX + VelY * VelY + VelZ * VelZ) * 60f; } }
    }
}
