using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// An area's light list (decomp CGameArea post-construct + CWorldLight): the MREA light section starts with 0xBABEDEAD then
    /// u32 count and lights (layer A), then u32 count and lights (layer B, else A); each light: u32 type (0 local ambient,
    /// 1 directional, 2 custom, 3 spot, 4 spot2, 5 local ambient2), colour, position, direction (3 floats each), q, cutoff angle,
    /// float, bool cast shadows, float, u32 falloff (0 constant, 1 linear, 2 quadratic), float. The game lights actors with
    /// these (CActorLights): ambient lights add to the actor's ambient, the others as GX lights with
    /// CWorldLight::GetAsCGraphicsLight's attenuation (constant 2/q, linear 250/q or quadratic 25000/q by the falloff).
    /// </summary>
    public sealed class MpWorldLight
    {
        public int Type, Falloff;
        public float R, G, B, X, Y, Z, DX, DY, DZ, Q, Cutoff;

        public bool IsAmbient { get { return Type == 0 || Type == 5; } }
        public bool IsDirectional { get { return Type == 1; } }

        public float K0 { get { float q = Q < 1e-6f ? 1e-5f : Q; return Falloff == 0 ? 2f / q : 0f; } }
        public float K1 { get { float q = Q < 1e-6f ? 1e-5f : Q; return Falloff == 1 ? 250f / q : 0f; } }
        public float K2 { get { float q = Q < 1e-6f ? 1e-5f : Q; return Falloff == 2 ? 25000f / q : 0f; } }

        /// <summary>The ambient colour an ambient light gives (its colour times q, clamped).</summary>
        public float[] AmbientColor()
        {
            float q = Q < 1e-6f ? 1e-5f : Q;
            return new[] { System.Math.Min(1f, R * q), System.Math.Min(1f, G * q), System.Math.Min(1f, B * q) };
        }

        public static List<MpWorldLight> ReadLayerA(MpArea a)
        {
            var list = new List<MpWorldLight>();
            if (a.Version <= 6 || a.LightSection < 0 || a.LightSection >= a.SectionCount) return list;
            MpReader r = a.Section(a.LightSection);
            if (r.Remaining < 4) return list;
            uint magic = r.U32();
            int count = magic == 0xBABEDEAD ? r.S32() : (int)magic;
            for (int i = 0; i < count && r.Remaining >= 65; i++)
            {
                var l = new MpWorldLight { Type = r.S32() };
                l.R = r.F32(); l.G = r.F32(); l.B = r.F32();
                l.X = r.F32(); l.Y = r.F32(); l.Z = r.F32();
                l.DX = r.F32(); l.DY = r.F32(); l.DZ = r.F32();
                l.Q = r.F32(); l.Cutoff = r.F32();
                r.F32(); r.U8(); r.F32();
                l.Falloff = r.S32();
                r.F32();
                list.Add(l);
            }

            return list;
        }
    }
}
