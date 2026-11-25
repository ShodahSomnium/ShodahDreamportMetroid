using System;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>
    /// A running SWHC (decomp CParticleSwoosh): a ring of LENG+1 points laid down at 60 Hz along the moving translation (while
    /// emitting and before PSLT), each with its orientation, velocity (VELM / VLM2), offsets, colour and left / right radius; the
    /// renderer joins them into a ribbon (2 sides) or a tube (3+ sides) from the newest point back.
    /// </summary>
    public sealed class MpSwooshGen : IPartGen
    {
        public sealed class Point
        {
            public bool Active;
            public int StartFrame, Frame;
            public float InitialRot, Rotm, LeftRad, RightRad;
            public V3 Translation, Offset, UseOffset, Velocity;
            public M3 Orientation = M3.Identity;
            public C4 Color = C4.White;
        }

        public const double FrameTime = 1.0 / 60.0;
        public readonly MpSwooshDesc Desc;
        public string Kind { get { return "SWHC"; } }
        public readonly Point[] Points;
        public int CurParticle, ParticleCount, CurFrame, PSLT, LENG, SIDE, SPLN, TSPN;
        private double _curTime;
        public V3 Translation, GlobalTranslation, GlobalScale = new V3(1, 1, 1), LocalScale = new V3(1, 1, 1);
        public M3 Orientation = M3.Identity, InvOrientation = M3.Identity, GlobalOrientation = M3.Identity;
        public bool Emitting = true, AALP, LLRD, VLS1, VLS2;
        /// <summary>SetWarmUp: the next Update lays down one point even with no time passing (projectiles feed their swooshes so).</summary>
        public bool ForceOneUpdate;
        public C4 ModulationColor = C4.White;
        private readonly MpRandom16 _rand;

        public MpSwooshGen(MpSwooshDesc desc, int leng)
        {
            Desc = desc;
            _rand = new MpRandom16(99);
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                if (leng > 0) LENG = leng;
                else if (desc.LENG != null) desc.LENG.Get(0, ref LENG);
                ++LENG;
                if (desc.SIDE != null) desc.SIDE.Get(0, ref SIDE);
                LLRD = desc.LLRD;
                VLS1 = desc.VLS1;
                VLS2 = desc.VLS2;
                if (IsLargeEnough)
                {
                    PSLT = 0x7fffff;
                    if (desc.PSLT != null) desc.PSLT.Get(0, ref PSLT);
                    AALP = desc.AALP;
                    if (desc.SPLN != null) desc.SPLN.Get(0, ref SPLN);
                    if (SPLN < 0) SPLN = 0;
                    Points = new Point[LENG];
                    for (int i = 0; i < LENG; i++) Points[i] = new Point();
                }
                else Points = new Point[0];
            }
            finally { MpRandom16.Current = prev; }
        }

        public bool IsLargeEnough { get { return LENG >= 2 && SIDE >= 2; } }

        public void SetOrientation(M3 o)
        {
            Orientation = o;
            InvOrientation = o.Transposed;
            if (Points.Length > 0) Points[CurParticle].Orientation = o;
        }

        public void SetTranslation(V3 t)
        {
            Translation = t;
            if (Points.Length > 0) Points[CurParticle].Translation = new V3(t.X / GlobalScale.X, t.Y / GlobalScale.Y, t.Z / GlobalScale.Z);
        }

        public void SetGlobalTranslation(V3 t) { GlobalTranslation = t; }
        public void SetGlobalOrientation(M3 o) { GlobalOrientation = o; }
        public void SetGlobalScale(V3 s) { GlobalScale = s; }
        public void SetLocalScale(V3 s) { LocalScale = s; }
        public void SetParticleEmission(bool on) { Emitting = on; }
        public void SetModulationColor(C4 c) { ModulationColor = c; }
        public void EndLifetime() { Emitting = false; }
        public void DestroyParticles() { }

        public bool IsSystemDeletable
        {
            get
            {
                if (Emitting && CurFrame < PSLT) return false;
                return ParticleCount < 2;
            }
        }

        public bool Update(double dt)
        {
            if (!IsLargeEnough) return false;
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                MpPartGlobals.SetParticleLifetime(LENG);
                MpPartGlobals.SetEmitterTime(CurFrame);
                MpPartGlobals.UpdateParticleLifetimeTweenValues(0);
                double eval = CurFrame * FrameTime;
                float timeScale = 1f;
                if (Desc.TIME != null) Desc.TIME.Get(CurFrame, ref timeScale);
                double advance = Math.Max(0.0, dt * timeScale);
                _curTime += advance;
                while (ForceOneUpdate || eval < _curTime)
                {
                    ForceOneUpdate = false;
                    if (++CurParticle >= Points.Length) CurParticle = 0;
                    Point p = Points[CurParticle];
                    if (Emitting && CurFrame < PSLT)
                    {
                        p.Translation = new V3(Translation.X / GlobalScale.X, Translation.Y / GlobalScale.Y, Translation.Z / GlobalScale.Z);
                        p.InitialRot = 0f;
                        if (Desc.IROT != null) Desc.IROT.Get(CurFrame, ref p.InitialRot);
                        p.Rotm = 0f;
                        p.StartFrame = CurFrame;
                        if (!p.Active) { ++ParticleCount; p.Active = true; }
                        p.Orientation = Orientation;
                        if (Desc.IVEL != null) { Desc.IVEL.Get(CurFrame, ref p.Velocity); p.Velocity = Orientation.Mul(p.Velocity); }
                        if (Desc.POFS != null) Desc.POFS.Get(CurFrame, ref p.Offset);
                        p.UseOffset = p.Offset;
                        if (Desc.COLR != null) Desc.COLR.Get(CurFrame, ref p.Color);
                        else p.Color = C4.White;
                        if (Desc.TSPN != null) Desc.TSPN.Get(CurFrame, ref TSPN);
                    }
                    else if (p.Active)
                    {
                        ParticleCount = Math.Max(0, ParticleCount - 1);
                        p.Active = false;
                    }

                    UpdateTranslationAndOrientation();
                    eval += FrameTime;
                    ++CurFrame;
                }

                return false;
            }
            finally { MpRandom16.Current = prev; }
        }

        private void UpdateTranslationAndOrientation()
        {
            if (ParticleCount <= 0) return;
            MpPartGlobals.SetParticleLifetime(LENG);
            MpPartGlobals.SetEmitterTime(CurFrame);
            V3 offset = V3.Zero;
            for (int i = 0; i < Points.Length; i++)
            {
                Point s = Points[i];
                if (!s.Active) continue;
                s.Frame = CurFrame - s.StartFrame;
                MpPartGlobals.UpdateParticleLifetimeTweenValues(s.Frame);
                if (Desc.SROT)
                {
                    if (Desc.IROT != null) Desc.IROT.Get(CurFrame, ref s.InitialRot);
                    s.Rotm = 0f;
                }
                else
                {
                    s.Rotm = 0f;
                    if (Desc.ROTM != null) Desc.ROTM.Get(CurFrame, ref s.Rotm);
                }

                Modify(Desc.VELM, VLS1, s);
                Modify(Desc.VLM2, VLS2, s);
                if (s.Frame > 0) s.Translation = s.Translation + s.Velocity;
                if (Desc.NPOS != null) { Desc.NPOS.Get(s.Frame, ref offset); s.UseOffset = s.Offset + offset; }
                if (Desc.COLR != null) Desc.COLR.Get(s.Frame, ref s.Color);
                s.LeftRad = 0f;
                if (Desc.LRAD != null) Desc.LRAD.Get(s.Frame, ref s.LeftRad);
                if (LLRD) s.RightRad = s.LeftRad;
                else { s.RightRad = 0f; if (Desc.RRAD != null) Desc.RRAD.Get(s.Frame, ref s.RightRad); }
            }
        }

        private void Modify(ModVecEl el, bool local, Point s)
        {
            if (el == null) return;
            if (local)
            {
                V3 lv = InvOrientation.Mul(s.Velocity), lt = InvOrientation.Mul(s.Translation - Translation);
                el.Get(s.Frame, ref lv, ref lt);
                s.Velocity = Orientation.Mul(lv);
                s.Translation = Orientation.Mul(lt) + Translation;
            }
            else el.Get(s.Frame, ref s.Velocity, ref s.Translation);
        }

        /// <summary>A point in game world space (the render matrix: global translation, orientation, scale, local scale).</summary>
        public V3 WorldOf(V3 p)
        {
            return GlobalTranslation + GlobalOrientation.Mul(V3.Mul(V3.Mul(p, GlobalScale), LocalScale));
        }

        public static V3 Spline(V3 p0, V3 p1, V3 p2, V3 p3, float t)
        {
            if (t <= 0f) return p1;
            if (t >= 1f) return p2;
            float t2 = t * t, t3 = t2 * t, h = 0.5f * t3;
            return p0 * (-h + t2 - 0.5f * t) + p1 * (1.5f * t3 - 2.5f * t2 + 1f) + p2 * (-1.5f * t3 + 2f * t2 + 0.5f * t) + p3 * (h - 0.5f * t2);
        }
    }
}
