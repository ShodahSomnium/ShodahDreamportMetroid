using System;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>A game-space vector (CVector3f) for the particle code (pure C#: the probe runs it too).</summary>
    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly V3 Zero = new V3(0, 0, 0);
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V3 operator -(V3 a) { return new V3(-a.X, -a.Y, -a.Z); }
        public static V3 operator *(V3 a, float s) { return new V3(a.X * s, a.Y * s, a.Z * s); }
        public static V3 operator *(float s, V3 a) { return new V3(a.X * s, a.Y * s, a.Z * s); }
        public static V3 operator /(V3 a, float s) { return new V3(a.X / s, a.Y / s, a.Z / s); }
        public static float Dot(V3 a, V3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static V3 Cross(V3 a, V3 b) { return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        public static V3 Mul(V3 a, V3 b) { return new V3(a.X * b.X, a.Y * b.Y, a.Z * b.Z); }
        public float MagSquared { get { return X * X + Y * Y + Z * Z; } }
        public float Magnitude { get { return (float)Math.Sqrt(MagSquared); } }
        public bool CanBeNormalized { get { return MagSquared > 1.1920929e-7f; } }
        public V3 Normalized { get { float m = Magnitude; return m > 0f ? this / m : this; } }
        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ", " + Z.ToString("0.###") + ")"; }
    }

    /// <summary>A colour (CColor), 0..1 floats, gamma space as the game keeps them.</summary>
    public struct C4
    {
        public float R, G, B, A;
        public C4(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
        public static readonly C4 White = new C4(1, 1, 1, 1);
        public static readonly C4 Black = new C4(0, 0, 0, 1);
        public static C4 Lerp(C4 a, C4 b, float t) { float n = 1f - t; return new C4(a.R * n + b.R * t, a.G * n + b.G * t, a.B * n + b.B * t, a.A * n + b.A * t); }
    }

    /// <summary>A 3x3 rotation (CMatrix3f) in columns: right (x), forward (y), up (z).</summary>
    public struct M3
    {
        public V3 C0, C1, C2;
        public static readonly M3 Identity = new M3 { C0 = new V3(1, 0, 0), C1 = new V3(0, 1, 0), C2 = new V3(0, 0, 1) };
        public V3 Mul(V3 v) { return C0 * v.X + C1 * v.Y + C2 * v.Z; }
        public M3 Transposed
        {
            get
            {
                return new M3
                {
                    C0 = new V3(C0.X, C1.X, C2.X),
                    C1 = new V3(C0.Y, C1.Y, C2.Y),
                    C2 = new V3(C0.Z, C1.Z, C2.Z),
                };
            }
        }

        public V3 Right { get { return C0; } }
        public V3 Forward { get { return C1; } }
        public V3 Up { get { return C2; } }
    }

    /// <summary>The game's random numbers (CRandom16: an LCG, 16-bit results) and the current one (CGlobalRandom).</summary>
    public sealed class MpRandom16
    {
        public uint Seed;
        public MpRandom16(uint seed) { Seed = seed; }
        public int Next() { Seed = Seed * 0x41c64e6du + 0x3039u; return (int)((Seed >> 16) & 0xffff); }
        public float Float() { return 1.52590222E-5f * Next(); }
        public int Range(int min, int max) { return min + Next() % (max - min + 1); }
        public float Range(float min, float max) { return (max - min) * Float() + min; }

        [ThreadStatic] private static MpRandom16 _current;
        private static readonly MpRandom16 Fallback = new MpRandom16(99);
        public static MpRandom16 Current { get { return _current ?? Fallback; } set { _current = value; } }
    }

    /// <summary>What an element may ask of the system it belongs to (CElementGen for PART, CParticleSwoosh for SWHC).</summary>
    public interface IPartSystem
    {
        M3 Orientation { get; }
        V3 Translation { get; }
        float ExternalVar(int i);
        int CumulativeParticleCount { get; }
        int ParticleCount { get; }
        int EmitterTime { get; }
    }

    /// <summary>One particle (CElementGen::CParticle).</summary>
    public sealed class MpParticle
    {
        public int EndFrame, StartFrame;
        public V3 Pos, PrevPos, Vel;
        public float LineLengthOrSize, LineWidthOrRota;
        public C4 Color = new C4(1f, 0f, 1f, 1f);

        public void CopyFrom(MpParticle o)
        {
            EndFrame = o.EndFrame; StartFrame = o.StartFrame; Pos = o.Pos; PrevPos = o.PrevPos; Vel = o.Vel;
            LineLengthOrSize = o.LineLengthOrSize; LineWidthOrRota = o.LineWidthOrRota; Color = o.Color;
        }
    }

    /// <summary>CParticleGlobals: the state elements read while a system updates (one thread at a time).</summary>
    public static class MpPartGlobals
    {
        [ThreadStatic] public static int EmitterTime;
        [ThreadStatic] public static int ParticleLifetime;
        [ThreadStatic] public static float ParticleLifetimeReal;
        [ThreadStatic] public static int LifetimePercent;
        [ThreadStatic] public static float LifetimePercentReal, LifetimePercentRemainder;
        [ThreadStatic] public static MpParticle CurrentParticle;
        [ThreadStatic] public static float[] AccessParameters;
        [ThreadStatic] public static IPartSystem CurrentSystem;

        public static void SetEmitterTime(int t) { EmitterTime = t; }

        public static void SetParticleLifetime(int lifetime)
        {
            ParticleLifetime = lifetime;
            ParticleLifetimeReal = lifetime;
        }

        public static void UpdateParticleLifetimeTweenValues(int time)
        {
            float d = ParticleLifetime != 0 ? ParticleLifetime : 1f;
            LifetimePercentReal = time * 100f / d;
            LifetimePercent = (int)LifetimePercentReal;
            LifetimePercentRemainder = LifetimePercentReal - LifetimePercent;
            if (LifetimePercent < 0) LifetimePercent = 0;
            else if (LifetimePercent > 100) LifetimePercent = 100;
        }

        public static MpParticle Particle { get { return CurrentParticle ?? Dummy; } }
        private static readonly MpParticle Dummy = new MpParticle();
        public static float Access(int i) { return AccessParameters != null && i < AccessParameters.Length ? AccessParameters[i] : 0f; }
    }

    public static class MpFourCC
    {
        public static uint Of(string s) { return ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3]; }
        public static string Name(uint v) { return new string(new[] { (char)(v >> 24), (char)((v >> 16) & 255), (char)((v >> 8) & 255), (char)(v & 255) }); }
    }
}
