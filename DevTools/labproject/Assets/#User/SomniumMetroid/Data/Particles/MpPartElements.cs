using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    // The particle elements (decomp Kyoto/Particles CIntElement / CRealElement / CVectorElement / CColorElement / CModVectorElement /
    // CEmitterElement / CUVElement), one class per FourCC, evaluated exactly as the game does: values are written into the caller's
    // storage (an "initial" element leaves it untouched after frame 0), and a true result means "dead" where the game uses it.

    public abstract class IntEl
    {
        public abstract bool Get(int frame, ref int v);
        public virtual bool IsConstant { get { return false; } }
    }

    public abstract class RealEl
    {
        public abstract bool Get(int frame, ref float v);
        public virtual bool IsConstant { get { return false; } }
    }

    public abstract class VecEl
    {
        public abstract bool Get(int frame, ref V3 v);
        public virtual bool IsFastConstant { get { return false; } }
    }

    public abstract class ColorEl
    {
        public abstract bool Get(int frame, ref C4 v);
    }

    public abstract class ModVecEl
    {
        public abstract bool Get(int frame, ref V3 vel, ref V3 pos);
    }

    public abstract class EmitterEl
    {
        public abstract bool Get(int frame, ref V3 pos, ref V3 vel);
    }

    public struct UVSet { public float XMin, YMin, XMax, YMax; }

    /// <summary>TEXR / TIND: a constant texture, or an animated sheet (CUVEAnimTexture: tiles listed from the last row up).</summary>
    public sealed class UVEl
    {
        public uint Texture;              // 0 = the game's generated blank texture
        public bool Animated;
        public IntEl TileW, TileH, StrideW, StrideH, CycleFrames;
        public bool Loop;
        private int _w = -1, _h, _tiles = 1;
        private UVSet[] _sets;

        public bool HasConstantUV { get { return !Animated; } }

        /// <summary>The texture's size (needed for the tile layout; known once the texture is decoded).</summary>
        public void SetTextureSize(int w, int h)
        {
            if (w == _w && h == _h) return;
            _w = w; _h = h;
            if (!Animated) return;
            int tw = 0, th = 0, sw = 1, sh = 1;
            TileW.Get(0, ref tw); TileH.Get(0, ref th); StrideW.Get(0, ref sw); StrideH.Get(0, ref sh);
            sw = Math.Max(1, sw); sh = Math.Max(1, sh);
            int xTiles = Math.Max(1, w / sw), yTiles = Math.Max(1, h / sh);
            _tiles = xTiles * yTiles;
            var list = new List<UVSet>();
            for (int y = yTiles - 1; y >= 0; --y)
                for (int x = 0; x < xTiles; ++x)
                    list.Add(new UVSet { XMin = (float)(sw * x) / w, YMin = (float)(sh * y) / h, XMax = (float)(sw * x + tw) / w, YMax = (float)(sh * y + th) / h });
            _sets = list.ToArray();
        }

        public void GetUV(int frame, ref UVSet uv)
        {
            if (!Animated || _sets == null || _sets.Length == 0) { uv = new UVSet { XMin = 0, YMin = 0, XMax = 1, YMax = 1 }; return; }
            int cv = 1;
            CycleFrames.Get(frame, ref cv);
            float cvf = frame / ((float)cv / _tiles);
            int tile;
            if (Loop)
            {
                tile = Math.Max((int)cvf, 0);
                if (tile >= _tiles) tile %= _tiles;
            }
            else
            {
                tile = (int)cvf;
                if ((int)cvf >= _tiles) tile = _tiles - 1;
                if (tile < 0) tile = 0;
            }

            uv = _sets[tile];
        }
    }

    // ------------------------------------------------------------------ ints

    sealed class IECnst : IntEl { public int V; public override bool Get(int f, ref int v) { v = V; return false; } public override bool IsConstant { get { return true; } } }

    sealed class IEKey : IntEl
    {
        public int Percent, LoopEnd, LoopStart; public bool Loop; public int[] Keys;
        public override bool Get(int f, ref int v)
        {
            if (Keys == null || Keys.Length == 0) return false;
            if (Percent == 0)
            {
                int t = MpPartGlobals.EmitterTime;
                if (Loop) { if (t >= LoopEnd && LoopEnd > LoopStart) { t -= LoopStart; t %= LoopEnd - LoopStart; t += LoopStart; } }
                else t = Math.Min(t, LoopEnd - 1);
                v = Keys[Clamp(t)];
                return false;
            }

            int p = MpPartGlobals.LifetimePercent;
            if (p == 100) v = Keys[Clamp(p)];
            else { float r = MpPartGlobals.LifetimePercentRemainder; v = (int)((1f - r) * Keys[Clamp(p)] + r * Keys[Clamp(p + 1)]); }
            return false;
        }

        private int Clamp(int i) { return i < 0 ? 0 : i >= Keys.Length ? Keys.Length - 1 : i; }
    }

    sealed class IETscl : IntEl { public RealEl A; public override bool Get(int f, ref int v) { float a = 0; A.Get(f, ref a); v = (int)(f * a); return false; } }
    sealed class IEDeath : IntEl { public IntEl A, B; public override bool Get(int f, ref int v) { int b = 0; A.Get(f, ref v); B.Get(f, ref b); return f >= b; } }
    sealed class IEChain : IntEl { public IntEl A, B, S; public override bool Get(int f, ref int v) { int s = 0; S.Get(f, ref s); return f < s ? A.Get(f, ref v) : B.Get(f - s, ref v); } }
    sealed class IEAdd : IntEl { public IntEl A, B; public override bool Get(int f, ref int v) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a + b; return false; } }
    sealed class IEMul : IntEl { public IntEl A, B; public override bool Get(int f, ref int v) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a * b; return false; } }
    sealed class IEMod : IntEl { public IntEl A, B; public override bool Get(int f, ref int v) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = b != 0 ? a % b : a; return false; } }
    sealed class IERand : IntEl
    {
        public IntEl A, B;
        public override bool Get(int f, ref int v) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a > 0 ? MpRandom16.Current.Range(a, b) : MpRandom16.Current.Next(); return false; }
    }

    sealed class IEImpulse : IntEl { public IntEl A; public override bool Get(int f, ref int v) { if (f == 0) A.Get(f, ref v); else v = 0; return false; } }
    sealed class IELtPercent : IntEl
    {
        public IntEl A;
        public override bool Get(int f, ref int v) { int a = 0; A.Get(f, ref a); if (a < 0) a = 0; v = (int)(a / 100f * MpPartGlobals.ParticleLifetimeReal + 0.5f); return false; }
    }

    sealed class IESampleHold : IntEl
    {
        public IntEl Source, Min, Max; private int _next, _hold;
        public override bool Get(int f, ref int v)
        {
            if (_next < f)
            {
                int b = 0, c = 0; Min.Get(f, ref b); Max.Get(f, ref c);
                _next = MpRandom16.Current.Range(b, c) + f;
                bool r = Source.Get(f, ref v); _hold = v; return r;
            }

            v = _hold; return false;
        }
    }

    sealed class IEInitRand : IntEl
    {
        public IntEl A, B;
        public override bool Get(int f, ref int v) { if (f == 0) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = MpRandom16.Current.Range(a, b); } return false; }
    }

    sealed class IEClamp : IntEl
    {
        public IntEl Min, Max, Val;
        public override bool Get(int f, ref int v) { int a = 0, b = 0; Min.Get(f, ref a); Max.Get(f, ref b); Val.Get(f, ref v); if (v > b) v = b; if (v < a) v = a; return false; }
    }

    sealed class IEPulse : IntEl
    {
        public IntEl Ad, Bd, A, B;
        public override bool Get(int f, ref int v)
        {
            int a = 0, b = 0; Ad.Get(f, ref a); Bd.Get(f, ref b);
            int cv = a + b + 1; if (cv < 0) cv = 1;
            if (b >= 1) { if (f % cv > a) B.Get(f, ref v); else A.Get(f, ref v); } else A.Get(f, ref v);
            return false;
        }
    }

    sealed class IERealToInt : IntEl { public RealEl A, B; public override bool Get(int f, ref int v) { float a = 0, b = 1; B.Get(f, ref b); A.Get(f, ref a); v = (int)(a * b); return false; } }
    sealed class IESub : IntEl { public IntEl A, B; public override bool Get(int f, ref int v) { int a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a - b; return false; } }
    sealed class IECumulative : IntEl { public override bool Get(int f, ref int v) { v = MpPartGlobals.CurrentSystem != null ? MpPartGlobals.CurrentSystem.CumulativeParticleCount : 0; return false; } }
    sealed class IEActive : IntEl { public override bool Get(int f, ref int v) { v = MpPartGlobals.CurrentSystem != null ? MpPartGlobals.CurrentSystem.ParticleCount : 0; return false; } }
    sealed class IEEmitterTime : IntEl { public override bool Get(int f, ref int v) { v = MpPartGlobals.CurrentSystem != null ? MpPartGlobals.CurrentSystem.EmitterTime : 0; return false; } }

    // ------------------------------------------------------------------ reals

    sealed class RECnst : RealEl { public float V; public override bool Get(int f, ref float v) { v = V; return false; } public override bool IsConstant { get { return true; } } }

    sealed class REKey : RealEl
    {
        public int Percent, LoopEnd, LoopStart; public bool Loop; public float[] Keys;
        public override bool Get(int f, ref float v)
        {
            if (Keys == null || Keys.Length == 0) return false;
            if (Percent == 0)
            {
                int t = MpPartGlobals.EmitterTime;
                if (Loop) { if (t >= LoopEnd && LoopEnd > LoopStart) { t -= LoopStart; t %= LoopEnd - LoopStart; t += LoopStart; } }
                else t = Math.Min(t, LoopEnd - 1);
                v = Keys[Clamp(t)];
                return false;
            }

            int p = MpPartGlobals.LifetimePercent;
            if (p == 100) v = Keys[Clamp(p)];
            else { float r = MpPartGlobals.LifetimePercentRemainder; v = (1f - r) * Keys[Clamp(p)] + r * Keys[Clamp(p + 1)]; }
            return false;
        }

        private int Clamp(int i) { return i < 0 ? 0 : i >= Keys.Length ? Keys.Length - 1 : i; }
    }

    sealed class RETscl : RealEl { public RealEl A; public override bool Get(int f, ref float v) { float a = 0; A.Get(f, ref a); v = f * a; return false; } }
    sealed class RESine : RealEl
    {
        public RealEl Freq, Amp, Phase;
        public override bool Get(int f, ref float v)
        {
            float amp = 0, freq = 0, phase = 0; Amp.Get(f, ref amp); Freq.Get(f, ref freq); Phase.Get(f, ref phase);
            v = (float)Math.Sin((f * freq + phase) * Math.PI / 180.0) * amp; return false;
        }
    }

    sealed class REAdd : RealEl { public RealEl A, B; public override bool Get(int f, ref float v) { float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a + b; return false; } }
    sealed class REMul : RealEl { public RealEl A, B; public override bool Get(int f, ref float v) { float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a * b; return false; } }
    sealed class REDot : RealEl { public VecEl A, B; public override bool Get(int f, ref float v) { V3 a = V3.Zero, b = V3.Zero; A.Get(f, ref a); B.Get(f, ref b); v = V3.Dot(a, b); return false; } }
    sealed class RERand : RealEl { public RealEl Min, Max; public override bool Get(int f, ref float v) { float a = 0, b = 0; Min.Get(f, ref a); Max.Get(f, ref b); v = (b - a) * MpRandom16.Current.Float() + a; return false; } }
    sealed class REInitRand : RealEl
    {
        public RealEl Min, Max;
        public override bool Get(int f, ref float v) { if (f == 0) { float a = 0, b = 0; Min.Get(f, ref a); Max.Get(f, ref b); v = (b - a) * MpRandom16.Current.Float() + a; } return false; }
        public override bool IsConstant { get { return true; } }
    }

    sealed class REChain : RealEl { public RealEl A, B; public IntEl S; public override bool Get(int f, ref float v) { int s = 0; S.Get(f, ref s); return f < s ? A.Get(f, ref v) : B.Get(f - s, ref v); } }
    sealed class REClamp : RealEl
    {
        public RealEl Min, Max, Val;
        public override bool Get(int f, ref float v) { float a = 0, b = 0; Min.Get(f, ref a); Max.Get(f, ref b); Val.Get(f, ref v); if (v > b) v = b; if (v < a) v = a; return false; }
    }

    sealed class REPulse : RealEl
    {
        public IntEl Ad, Bd; public RealEl A, B;
        public override bool Get(int f, ref float v)
        {
            int a = 0, b = 0; Ad.Get(f, ref a); Bd.Get(f, ref b);
            int cv = a + b + 1; if (cv < 0) cv = 1;
            if (b >= 1) { if (f % cv >= a) B.Get(f, ref v); else A.Get(f, ref v); } else A.Get(f, ref v);
            return false;
        }
    }

    sealed class RELtPercent : RealEl { public RealEl A; public override bool Get(int f, ref float v) { float a = 0; A.Get(f, ref a); if (a < 0) a = 0; v = a / 100f * MpPartGlobals.ParticleLifetimeReal; return false; } }
    sealed class RELtTween : RealEl
    {
        public RealEl A, B;
        public override bool Get(int f, ref float v) { float t = f / MpPartGlobals.ParticleLifetimeReal; float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = b * t + a * (1f - t); return false; }
    }

    sealed class REAccess : RealEl { public int I; public override bool Get(int f, ref float v) { v = MpPartGlobals.Access(I); return false; } }
    sealed class REPartSize : RealEl { public override bool Get(int f, ref float v) { v = MpPartGlobals.Particle.LineLengthOrSize; return false; } }
    sealed class REPartRota : RealEl { public override bool Get(int f, ref float v) { v = MpPartGlobals.Particle.LineWidthOrRota; return false; } }
    sealed class REVecComp : RealEl
    {
        public VecEl A; public int C;
        public override bool Get(int f, ref float v) { V3 a = V3.Zero; A.Get(f, ref a); v = C == 0 ? a.X : C == 1 ? a.Y : C == 2 ? a.Z : a.Magnitude; return false; }
    }

    sealed class REInitSwitch : RealEl { public RealEl A, B; public override bool Get(int f, ref float v) { if (f == 0) A.Get(0, ref v); else B.Get(f - 1, ref v); return false; } }
    sealed class RECompLess : RealEl
    {
        public RealEl A, B, C, D;
        public override bool Get(int f, ref float v) { float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); if (a < b) C.Get(f, ref v); else D.Get(f, ref v); return false; }
    }

    sealed class RECompEq : RealEl
    {
        public RealEl A, B, C, D;
        public override bool Get(int f, ref float v) { float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); if (Math.Abs(a - b) < 1.1920929e-7f) C.Get(f, ref v); else D.Get(f, ref v); return false; }
    }

    sealed class RERange : RealEl
    {
        public RealEl Val, Min, Max, In, Out;
        public override bool Get(int f, ref float v)
        {
            float x = 0, a = 0, b = 0; Val.Get(f, ref x); Min.Get(f, ref a); Max.Get(f, ref b);
            if (x > a && x < b) In.Get(f, ref v); else Out.Get(f, ref v); return false;
        }
    }

    sealed class REExternal : RealEl
    {
        public IntEl A;
        public override bool Get(int f, ref float v) { int a = 0; A.Get(f, ref a); a = Math.Max(0, a) % 16; v = MpPartGlobals.CurrentSystem != null ? MpPartGlobals.CurrentSystem.ExternalVar(a) : 0f; return false; }
    }

    sealed class RESub : RealEl { public RealEl A, B; public override bool Get(int f, ref float v) { float a = 0, b = 0; A.Get(f, ref a); B.Get(f, ref b); v = a - b; return false; } }
    sealed class REColorComp : RealEl
    {
        public ColorEl A; public int C;
        public override bool Get(int f, ref float v) { C4 c = C4.Black; A.Get(f, ref c); v = C == 0 ? c.R : C == 1 ? c.G : C == 2 ? c.B : c.A; return false; }
    }

    sealed class REIntTimesReal : RealEl { public IntEl A; public RealEl B; public override bool Get(int f, ref float v) { int a = 0; float b = 1; B.Get(f, ref b); A.Get(f, ref a); v = b * a; return false; } }

    // ------------------------------------------------------------------ vectors

    sealed class VEFast : VecEl { public V3 V; public override bool Get(int f, ref V3 v) { v = V; return false; } public override bool IsFastConstant { get { return true; } } }
    sealed class VECnst : VecEl { public RealEl X, Y, Z; public override bool Get(int f, ref V3 v) { float x = 0, y = 0, z = 0; X.Get(f, ref x); Y.Get(f, ref y); Z.Get(f, ref z); v = new V3(x, y, z); return false; } }

    sealed class VEKey : VecEl
    {
        public int Percent, LoopEnd, LoopStart; public bool Loop; public V3[] Keys;
        public override bool Get(int f, ref V3 v)
        {
            if (Keys == null || Keys.Length == 0) return false;
            if (Percent == 0)
            {
                int t = MpPartGlobals.EmitterTime;
                if (Loop) { if (t >= LoopEnd && LoopEnd > LoopStart) { t -= LoopStart; t %= LoopEnd - LoopStart; t += LoopStart; } }
                else t = Math.Min(t, LoopEnd - 1);
                v = Keys[Clamp(t)];
                return false;
            }

            int p = MpPartGlobals.LifetimePercent;
            if (p == 100) v = Keys[Clamp(p)];
            else { float r = MpPartGlobals.LifetimePercentRemainder; v = Keys[Clamp(p)] * (1f - r) + Keys[Clamp(p + 1)] * r; }
            return false;
        }

        private int Clamp(int i) { return i < 0 ? 0 : i >= Keys.Length ? Keys.Length - 1 : i; }
    }

    sealed class VECone : VecEl
    {
        public VecEl Dir; public RealEl Mag; public V3 XVec, YVec;
        public void Init()
        {
            V3 av = V3.Zero; Dir.Get(0, ref av);
            V3 n = av.Normalized;
            XVec = n.X > 0.8f ? V3.Cross(av, new V3(0, 1, 0)) : V3.Cross(av, new V3(1, 0, 0));
            YVec = V3.Cross(n, XVec);
        }

        public override bool Get(int f, ref V3 v)
        {
            float b = 0; V3 dir = V3.Zero; Mag.Get(f, ref b); Dir.Get(f, ref dir);
            b = Math.Min(1f, b);
            float rx, ry;
            int guard = 0;
            do
            {
                rx = 2f * b * (MpRandom16.Current.Float() - 0.5f);
                ry = 2f * b * (MpRandom16.Current.Float() - 0.5f);
            }
            while (rx * rx + ry * ry > 1f && ++guard < 64);
            v = dir + XVec * rx + YVec * ry;
            return false;
        }
    }

    sealed class VEAngleCone : VecEl
    {
        public RealEl XC, YC, XR, YR, Mag;
        public override bool Get(int f, ref V3 v)
        {
            float xc = 0, xr = 0, yc = 0, yr = 0; XC.Get(f, ref xc); YC.Get(f, ref yc); XR.Get(f, ref xr); YR.Get(f, ref yr);
            xc += xr * 0.5f - xr * MpRandom16.Current.Float(); xc *= (float)Math.PI / 180f;
            yc += yr * 0.5f - yr * MpRandom16.Current.Float(); yc *= (float)Math.PI / 180f;
            var vec = new V3(-(float)Math.Sin(yc) * (float)Math.Cos(xc), (float)Math.Sin(xc), (float)Math.Cos(xc) * (float)Math.Cos(yc));
            float mag = 0; Mag.Get(f, ref mag);
            v = vec * mag; return false;
        }
    }

    sealed class VECircle : VecEl
    {
        public VecEl Offset, Normal; public RealEl AngleC, AngleL, Radius; public V3 XVec, YVec;
        public void Init()
        {
            V3 d = V3.Zero; Normal.Get(0, ref d); V3 n = d.Normalized;
            XVec = n.X > 0.8f ? V3.Cross(n, new V3(0, 1, 0)) : V3.Cross(n, new V3(1, 0, 0));
            YVec = V3.Cross(n, XVec);
        }

        public override bool Get(int f, ref V3 v)
        {
            float r = 0, al = 0, ac = 0; AngleL.Get(f, ref al); Radius.Get(f, ref r); AngleC.Get(f, ref ac);
            float a = (al * f + ac) * (float)Math.PI / 180f;
            V3 o = V3.Zero; Offset.Get(f, ref o);
            v = o + XVec * (r * (float)Math.Cos(a)) + YVec * (r * (float)Math.Sin(a)); return false;
        }
    }

    sealed class VECluster : VecEl
    {
        public VecEl Offset; public RealEl RandomFactor; public V3 XVec, YVec; public float Step;
        public void Init(VecEl normalEl, IntEl cycle)
        {
            int c = 1; cycle.Get(0, ref c);
            Step = (float)Math.PI / 180f * (360f / Math.Max(1, c));
            V3 n = V3.Zero; normalEl.Get(0, ref n);
            V3 tmp = n;
            n = n.CanBeNormalized ? n.Normalized : new V3(0, 0, 1);
            XVec = n.X > 0.8f ? V3.Cross(tmp, new V3(0, 1, 0)) : V3.Cross(tmp, new V3(1, 0, 0));
            YVec = V3.Cross(n, XVec);
        }

        public override bool Get(int f, ref V3 v)
        {
            float a = Step * f;
            V3 o = V3.Zero; Offset.Get(f, ref o);
            V3 tv = o + XVec * (float)Math.Cos(a) + YVec * (float)Math.Sin(a);
            float dv = 0; RandomFactor.Get(f, ref dv);
            float m = dv * tv.Magnitude;
            float x = m * MpRandom16.Current.Float(), y = m * MpRandom16.Current.Float(), z = m * MpRandom16.Current.Float();
            v = new V3(x + tv.X, y + tv.Y, z + tv.Z); return false;
        }
    }

    sealed class VEAdd : VecEl { public VecEl A, B; public override bool Get(int f, ref V3 v) { V3 a = V3.Zero, b = V3.Zero; A.Get(f, ref a); B.Get(f, ref b); v = a + b; return false; } }
    sealed class VEMul : VecEl { public VecEl A, B; public override bool Get(int f, ref V3 v) { V3 a = V3.Zero, b = V3.Zero; A.Get(f, ref a); B.Get(f, ref b); v = V3.Mul(a, b); return false; } }
    sealed class VESub : VecEl { public VecEl A, B; public override bool Get(int f, ref V3 v) { V3 a = V3.Zero, b = V3.Zero; A.Get(f, ref a); B.Get(f, ref b); v = a - b; return false; } }
    sealed class VEChain : VecEl { public VecEl A, B; public IntEl S; public override bool Get(int f, ref V3 v) { int s = 0; S.Get(f, ref s); return f < s ? A.Get(f, ref v) : B.Get(f - s, ref v); } }
    sealed class VEPulse : VecEl
    {
        public IntEl Ad, Bd; public VecEl A, B;
        public override bool Get(int f, ref V3 v)
        {
            int a = 0, b = 0; Ad.Get(f, ref a); Bd.Get(f, ref b);
            int cv = a + b + 1; if (cv < 0) cv = 1;
            if (b >= 1) { if (f % cv > a) B.Get(f, ref v); else A.Get(f, ref v); } else A.Get(f, ref v);
            return false;
        }
    }

    sealed class VERealToVec : VecEl { public RealEl A; public override bool Get(int f, ref V3 v) { float a = 0; A.Get(f, ref a); v = new V3(a, a, a); return false; } }
    sealed class VEPartLoc : VecEl { public override bool Get(int f, ref V3 v) { v = MpPartGlobals.Particle.Pos; return false; } }
    sealed class VEPartPrev : VecEl { public override bool Get(int f, ref V3 v) { v = MpPartGlobals.Particle.PrevPos; return false; } }
    sealed class VEPartVel : VecEl { public override bool Get(int f, ref V3 v) { v = MpPartGlobals.Particle.Vel; return false; } }
    sealed class VESysOrient : VecEl
    {
        public int Axis;
        public override bool Get(int f, ref V3 v)
        {
            IPartSystem s = MpPartGlobals.CurrentSystem;
            M3 m = s != null ? s.Orientation : M3.Identity;
            v = Axis == 0 ? m.Forward : Axis == 1 ? m.Up : m.Right; return false;
        }
    }

    sealed class VESysTrans : VecEl { public override bool Get(int f, ref V3 v) { v = MpPartGlobals.CurrentSystem != null ? MpPartGlobals.CurrentSystem.Translation : V3.Zero; return false; } }
    sealed class VEColorToVec : VecEl { public ColorEl A; public override bool Get(int f, ref V3 v) { C4 c = C4.Black; A.Get(f, ref c); v = new V3(c.R, c.G, c.B); return false; } }

    // ------------------------------------------------------------------ colours

    static class Col { public static float Sat(float x) { return x < 0 ? 0 : x > 1 ? 1 : x; } }

    sealed class CEFast : ColorEl { public C4 V; public override bool Get(int f, ref C4 v) { v = V; return false; } }
    sealed class CECnst : ColorEl
    {
        public RealEl R, G, B, A;
        public override bool Get(int f, ref C4 v)
        {
            float r = 0, g = 0, b = 0, a = 0; R.Get(f, ref r); G.Get(f, ref g); B.Get(f, ref b); A.Get(f, ref a);
            v = new C4(Col.Sat(r), Col.Sat(g), Col.Sat(b), Col.Sat(a)); return false;
        }
    }

    sealed class CEKey : ColorEl
    {
        public int Percent, LoopEnd, LoopStart; public bool Loop; public C4[] Keys;
        public override bool Get(int f, ref C4 v)
        {
            if (Keys == null || Keys.Length == 0) return false;
            if (Percent == 0)
            {
                int t = MpPartGlobals.EmitterTime;
                if (Loop) { if (t >= LoopEnd && LoopEnd > LoopStart) { t -= LoopStart; t %= LoopEnd - LoopStart; t += LoopStart; } }
                else t = Math.Min(t, LoopEnd - 1);
                v = Keys[Clamp(t)];
                return false;
            }

            int p = MpPartGlobals.LifetimePercent;
            if (p == 100) v = Keys[Clamp(p)];
            else v = C4.Lerp(Keys[Clamp(p)], Keys[Clamp(p + 1)], MpPartGlobals.LifetimePercentRemainder);
            return false;
        }

        private int Clamp(int i) { return i < 0 ? 0 : i >= Keys.Length ? Keys.Length - 1 : i; }
    }

    sealed class CEFade : ColorEl
    {
        public ColorEl A, B; public RealEl End;
        public override bool Get(int f, ref C4 v)
        {
            float c = 1; End.Get(f, ref c);
            float t = f * (1f / c);
            if (t >= 1f) B.Get(f, ref v);
            else { C4 a = C4.Black, b = C4.Black; A.Get(f, ref a); B.Get(f, ref b); v = C4.Lerp(a, b, t); }
            return false;
        }
    }

    sealed class CEFadeEnd : ColorEl
    {
        public ColorEl A, B; public RealEl Start, End;
        public override bool Get(int f, ref C4 v)
        {
            float s = 0; Start.Get(f, ref s);
            if (f < s) { A.Get(f, ref v); return false; }
            float e = 1; End.Get(f, ref e);
            C4 a = C4.Black, b = C4.Black; A.Get(f, ref a); B.Get(f, ref b);
            v = C4.Lerp(a, b, (f - s) / (e - s)); return false;
        }
    }

    sealed class CEChain : ColorEl { public ColorEl A, B; public IntEl S; public override bool Get(int f, ref C4 v) { int s = 0; S.Get(f, ref s); return f < s ? A.Get(f, ref v) : B.Get(f - s, ref v); } }
    sealed class CEPulse : ColorEl
    {
        public IntEl Ad, Bd; public ColorEl A, B;
        public override bool Get(int f, ref C4 v)
        {
            int a = 0, b = 0; Ad.Get(f, ref a); Bd.Get(f, ref b);
            int cv = a + b + 1; if (cv < 0) cv = 1;
            if (b >= 1) { if (f % cv > a) B.Get(f, ref v); else A.Get(f, ref v); } else A.Get(f, ref v);
            return false;
        }
    }

    sealed class CEPartColor : ColorEl { public override bool Get(int f, ref C4 v) { v = MpPartGlobals.Particle.Color; return false; } }

    // ------------------------------------------------------------------ mod vectors

    sealed class MVEFast : ModVecEl { public V3 V; public override bool Get(int f, ref V3 vel, ref V3 pos) { vel = V; return false; } }
    sealed class MVECnst : ModVecEl { public RealEl X, Y, Z; public override bool Get(int f, ref V3 vel, ref V3 pos) { float x = 0, y = 0, z = 0; X.Get(f, ref x); Y.Get(f, ref y); Z.Get(f, ref z); vel = new V3(x, y, z); return false; } }
    sealed class MVEGrav : ModVecEl { public VecEl A; public override bool Get(int f, ref V3 vel, ref V3 pos) { V3 g = V3.Zero; A.Get(f, ref g); vel = vel + g; return false; } }
    sealed class MVEWind : ModVecEl
    {
        public VecEl Vel; public RealEl Factor;
        public override bool Get(int f, ref V3 vel, ref V3 pos) { V3 w = V3.Zero; Vel.Get(f, ref w); float k = 0; Factor.Get(f, ref k); vel = vel + (w - vel) * k; return false; }
    }

    sealed class MVEExplode : ModVecEl
    {
        public RealEl A, B;
        public override bool Get(int f, ref V3 vel, ref V3 pos)
        {
            if (f == 0)
            {
                float x, y, z; int guard = 0;
                do { x = MpRandom16.Current.Float() - 0.5f; y = MpRandom16.Current.Float() - 0.5f; z = MpRandom16.Current.Float() - 0.5f; }
                while (x * x + y * y + z * z > 1f && ++guard < 64);
                float a = 0; A.Get(f, ref a);
                vel = new V3(x, y, z).Normalized * a;
            }
            else { float b = 0; B.Get(f, ref b); vel = vel * b; }
            return false;
        }
    }

    sealed class MVEChain : ModVecEl { public ModVecEl A, B; public IntEl S; public override bool Get(int f, ref V3 vel, ref V3 pos) { int s = 0; S.Get(f, ref s); return f < s ? A.Get(f, ref vel, ref pos) : B.Get(f - s, ref vel, ref pos); } }
    sealed class MVEPulse : ModVecEl
    {
        public IntEl Ad, Bd; public ModVecEl A, B;
        public override bool Get(int f, ref V3 vel, ref V3 pos)
        {
            int a = 0, b = 0; Ad.Get(f, ref a); Bd.Get(f, ref b);
            int cv = a + b + 1; if (cv < 0) cv = 1;
            if (b >= 1) { if (f % cv > a) B.Get(f, ref vel, ref pos); else A.Get(f, ref vel, ref pos); } else A.Get(f, ref vel, ref pos);
            return false;
        }
    }

    /// <summary>IMPL / LMPL / EMPL: pull toward a point (add, set, or add scaled by distance).</summary>
    sealed class MVEImplosion : ModVecEl
    {
        public VecEl Point; public RealEl MagScale, MaxMag, MinMag; public bool EnableMin; public int Kind;
        public override bool Get(int f, ref V3 vel, ref V3 pos)
        {
            V3 av = V3.Zero; Point.Get(f, ref av);
            V3 dv = av - pos; float dvm = dv.Magnitude;
            float c = 0; MaxMag.Get(f, ref c); if (dvm > c) return false;
            float d = 0; MinMag.Get(f, ref d); if (EnableMin && dvm < d) return true;
            if (dvm == 0f) return false;
            V3 dvs = dv / dvm; float b = 0; MagScale.Get(f, ref b);
            if (Kind == 0) vel = vel + dvs * b;
            else if (Kind == 1) vel = dvs * b;
            else vel = vel + dvs * (b * dvm);
            return false;
        }
    }

    sealed class MVESwirl : ModVecEl
    {
        public VecEl Helix, Binormal; public RealEl Gain, Tangential;
        public override bool Get(int f, ref V3 vel, ref V3 pos)
        {
            V3 a = V3.Zero, b = V3.Zero; Helix.Get(f, ref a); Binormal.Get(f, ref b);
            V3 po = a - pos; V3 ph = po - b * V3.Dot(po, b);
            float c = 0, d = 0; Gain.Get(f, ref c); Tangential.Get(f, ref d);
            V3 tangent = V3.Cross(b, ph) * d;
            vel = (b * V3.Dot(b, vel) + tangent) * c + vel * (1f - c);
            return false;
        }
    }

    sealed class MVEBounce : ModVecEl
    {
        public VecEl Point, Normal; public RealEl Friction, Restitution; public bool Die; private bool _pre; private V3 _n; private float _d;
        public void Init()
        {
            if (Point != null && Normal != null && Point.IsFastConstant && Normal.IsFastConstant)
            {
                _pre = true; Normal.Get(0, ref _n); if (_n.MagSquared > 0f) _n = _n.Normalized;
                V3 a = V3.Zero; Point.Get(0, ref a); _d = V3.Dot(_n, a);
            }
        }

        public override bool Get(int f, ref V3 vel, ref V3 pos)
        {
            if (!_pre) { Normal.Get(f, ref _n); _n = _n.Normalized; V3 a = V3.Zero; Point.Get(f, ref a); _d = V3.Dot(_n, a); }
            float dot = V3.Dot(_n, pos);
            if (dot - _d > 0f) return false;
            if (Die) return true;
            if (V3.Dot(_n, vel) < 0f)
            {
                float pd = V3.Dot(pos - vel, _n) - _d, pn = V3.Dot(vel, _n);
                pos = pos + vel * (-pd / pn - 1f);
                float d = 0; Restitution.Get(f, ref d); vel = vel - vel * d;
                float c = 0; Friction.Get(f, ref c); vel = vel - _n * ((c + 1f) * V3.Dot(_n, vel));
            }

            return false;
        }
    }

    sealed class MVESetPos : ModVecEl { public VecEl A; public override bool Get(int f, ref V3 vel, ref V3 pos) { A.Get(f, ref pos); return false; } }

    // ------------------------------------------------------------------ emitters

    sealed class EESimple : EmitterEl
    {
        public VecEl Pos, Vel;
        public override bool Get(int f, ref V3 pos, ref V3 vel) { Pos.Get(f, ref pos); if (Vel != null) Vel.Get(f, ref vel); else vel = V3.Zero; return false; }
    }

    sealed class EESphere : EmitterEl
    {
        public VecEl Origin; public RealEl Radius, VelMag;
        public override bool Get(int f, ref V3 pos, ref V3 vel)
        {
            V3 o = V3.Zero; float r = 0, m = 0; Origin.Get(f, ref o); Radius.Get(f, ref r);
            var rnd = MpRandom16.Current;
            V3 n = new V3(rnd.Range(-100, 100), rnd.Range(-100, 100), rnd.Range(-100, 100));
            n = n.CanBeNormalized ? (n * 0.01f).Normalized : n * 0.01f;
            pos = o + n * r;
            V3 off = pos - o; V3 dir = off.CanBeNormalized ? off.Normalized : off;
            VelMag.Get(f, ref m); vel = dir * m; return false;
        }
    }

    sealed class EEAngleSphere : EmitterEl
    {
        public VecEl Origin; public RealEl Radius, VelMag, XB, YB, XR, YR;
        public override bool Get(int f, ref V3 pos, ref V3 vel)
        {
            V3 o = V3.Zero; float r = 0, m = 0, xb = 0, xr = 0, yb = 0, yr = 0;
            Origin.Get(f, ref o); Radius.Get(f, ref r); XB.Get(f, ref xb); YB.Get(f, ref yb); XR.Get(f, ref xr); YR.Get(f, ref yr);
            xb += 0.5f * xr - MpRandom16.Current.Float() * xr; xb *= (float)Math.PI / 180f;
            yb += 0.5f * yr - MpRandom16.Current.Float() * yr; yb *= (float)Math.PI / 180f;
            var vec = new V3(-(float)Math.Sin(yb) * (float)Math.Cos(xb), (float)Math.Sin(xb), (float)Math.Cos(xb) * (float)Math.Cos(yb));
            pos = o + vec * r;
            V3 dir = (pos - o).Normalized;
            VelMag.Get(f, ref m); vel = dir * m; return false;
        }
    }
}
