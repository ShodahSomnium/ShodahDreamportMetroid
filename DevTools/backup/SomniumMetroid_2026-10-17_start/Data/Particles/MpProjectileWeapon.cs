using System;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>
    /// A shot in flight (decomp Weapons/CProjectileWeapon, PAL rules): an offset in the firing frame moved by IVEC each 60 Hz frame
    /// (PSVM may change velocity and offset; VMD2 turns the velocity by the projectile's own orientation; gravity), turned by IORN /
    /// PSOV, offset by OFST / POFS, living PSLT frames; its APSM / APS2 systems and up to three swooshes follow its translation
    /// (APSO turns the systems with it) and fade out when it ends or hits.
    /// </summary>
    public sealed class MpProjectileWeapon
    {
        public readonly MpWeaponDescFull Desc;
        public readonly MpPartLibrary Lib;
        private readonly MpRandom16 _rand = new MpRandom16(99);
        public M3 LocalToWorldRot;              // the firing frame (CTransform4f rotation)
        public V3 LocalToWorldPos, WorldOffset;
        public M3 LocalRot = M3.Identity;       // mLocalXf
        public V3 LocalOffset, PreviousLocalOffset, InterpolationOffset, ProjOffset, LocalOffset2, Velocity, Gravity;
        public V3 Scale = new V3(1, 1, 1);
        public C4 AmbientLight = C4.White;
        public int Lifetime = 0x7FFFFF, CurFrame, LastParticleFrame = -1, ChildUpdates;
        private double _curTime;
        public bool Active = true;
        public MpElementGen APSM, APS2;
        public MpSwooshGen Swoosh1, Swoosh2, Swoosh3;

        public MpProjectileWeapon(MpWeaponDescFull desc, MpPartLibrary lib, M3 localToWorldRot, V3 localToWorldPos, V3 worldOffset, V3 scale, bool particleOpts = false)
        {
            Desc = desc;
            Lib = lib;
            LocalToWorldRot = localToWorldRot;
            LocalToWorldPos = localToWorldPos;
            WorldOffset = worldOffset;
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                MpGenDesc a = lib.Part(desc.APSM), b = lib.Part(desc.APS2);
                if (a != null) { APSM = new MpElementGen(a, lib, particleOpts); APSM.SetGlobalScale(scale); }
                if (b != null) { APS2 = new MpElementGen(b, lib, particleOpts); APS2.SetGlobalScale(scale); }
                MpSwooshDesc s1 = lib.Swoosh(desc.ASW1), s2 = lib.Swoosh(desc.ASW2), s3 = lib.Swoosh(desc.ASW3);
                if (s1 != null) { Swoosh1 = new MpSwooshGen(s1, 0); Swoosh1.SetGlobalScale(scale); }
                if (s2 != null) { Swoosh2 = new MpSwooshGen(s2, 0); Swoosh2.SetGlobalScale(scale); }
                if (s3 != null) { Swoosh3 = new MpSwooshGen(s3, 0); Swoosh3.SetGlobalScale(scale); }
                if (desc.PSLT != null) desc.PSLT.Get(0, ref Lifetime);
                if (desc.IVEC != null) desc.IVEC.Get(0, ref Velocity);
                if (desc.IORN != null)
                {
                    V3 ang = V3.Zero;
                    desc.IORN.Get(0, ref ang);
                    LocalRot = RotateLocal(RotateLocal(RotateLocal(M3.Identity, 0, ang.X), 1, ang.Y), 2, ang.Z);
                }

                UpdateChildParticleSystems(1.0 / 60.0);
            }
            finally { MpRandom16.Current = prev; }
        }

        private static M3 Mul(M3 a, M3 b) { return new M3 { C0 = a.Mul(b.C0), C1 = a.Mul(b.C1), C2 = a.Mul(b.C2) }; }

        /// <summary>CTransform4f::RotateLocalX/Y/Z by degrees.</summary>
        public static M3 RotateLocal(M3 m, int axis, float deg)
        {
            if (deg == 0f) return m;
            float r = deg * (float)Math.PI / 180f, c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            M3 rot;
            if (axis == 0) rot = new M3 { C0 = new V3(1, 0, 0), C1 = new V3(0, c, s), C2 = new V3(0, -s, c) };
            else if (axis == 1) rot = new M3 { C0 = new V3(c, 0, -s), C1 = new V3(0, 1, 0), C2 = new V3(s, 0, c) };
            else rot = new M3 { C0 = new V3(c, s, 0), C1 = new V3(-s, c, 0), C2 = new V3(0, 0, 1) };
            return Mul(m, rot);
        }

        /// <summary>GetTransform: the projectile's orientation in the world.</summary>
        public M3 Transform { get { return Mul(LocalToWorldRot, LocalRot); } }

        /// <summary>GetTranslation (PAL: with the sub-frame interpolation).</summary>
        public V3 Translation
        {
            get { return LocalToWorldRot.Mul(LocalOffset + InterpolationOffset + LocalRot.Mul(ProjOffset)) + LocalToWorldPos + WorldOffset; }
        }

        private static bool Close(double a, double b, double eps) { return Math.Abs(a - b) < eps; }

        public bool Update(float dt)
        {
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                double t = CurFrame * (1.0 / 60.0);
                ChildUpdates = 0;
                double useDt = Close(dt, 1.0 / 60.0, 1.6666666851961054e-5) ? 1.0 / 60.0 : dt;
                if (useDt < 0) useDt = 0;
                _curTime += useDt;
                while (t < _curTime && !Close(t, _curTime, 1.6666666666666667e-5))
                {
                    if (CurFrame < Lifetime)
                    {
                        MpPartGlobals.SetEmitterTime(CurFrame);
                        MpPartGlobals.SetParticleLifetime(Lifetime);
                        MpPartGlobals.UpdateParticleLifetimeTweenValues(CurFrame);
                        UpdatePSTranslationAndOrientation();
                    }

                    t += 1.0 / 60.0;
                    ++CurFrame;
                    ++ChildUpdates;
                }

                if (Close(t, _curTime, 1.6666666666666667e-5)) _curTime = t;
                float remainder = (float)((t - _curTime) / (1.0 / 60.0));
                InterpolationOffset = (PreviousLocalOffset - LocalOffset) * remainder;
                return false;
            }
            finally { MpRandom16.Current = prev; }
        }

        private void UpdatePSTranslationAndOrientation()
        {
            if (Lifetime < CurFrame || !Active) return;
            PreviousLocalOffset = LocalOffset;
            if (Desc.PSVM != null) Desc.PSVM.Get(CurFrame, ref Velocity, ref LocalOffset);
            LocalOffset = LocalOffset + (Desc.VMD2 ? LocalRot.Mul(Velocity) : Velocity);
            Velocity = Velocity + Gravity / 60f;
            if (Desc.PSOV != null)
            {
                V3 o = V3.Zero;
                Desc.PSOV.Get(CurFrame, ref o);
                LocalRot = RotateLocal(RotateLocal(RotateLocal(LocalRot, 0, o.X), 1, o.Y), 2, o.Z);
            }

            if (Desc.PSCL != null) Desc.PSCL.Get(CurFrame, ref Scale);
            if (Desc.PCOL != null) Desc.PCOL.Get(CurFrame, ref AmbientLight);
            if (Desc.POFS != null) Desc.POFS.Get(CurFrame, ref LocalOffset2);
            if (Desc.OFST != null) Desc.OFST.Get(CurFrame, ref ProjOffset);
        }

        /// <summary>UpdateParticleFX: the systems step as many 60 Hz frames as the projectile did (SPS1 / SPS2: once).</summary>
        public void UpdateParticleFX()
        {
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                if (APSM != null && Desc.SPS1) APSM.Update(1.0 / 60.0);
                if (APS2 != null && Desc.SPS2) APS2.Update(1.0 / 60.0);
                for (int i = 0; i < ChildUpdates; i++) UpdateChildParticleSystems(1.0 / 60.0);
            }
            finally { MpRandom16.Current = prev; }
        }

        private void UpdateChildParticleSystems(double dt)
        {
            V3 trans = Translation;
            M3 xf = Transform;
            if (APSM != null)
            {
                if (LastParticleFrame != CurFrame)
                {
                    if (CurFrame > Lifetime) { APSM.SetParticleEmission(false); APSM.EndLifetime(); }
                    else
                    {
                        if (Desc.AP11) APSM.SetGlobalTranslation(trans); else APSM.SetTranslation(trans);
                        if (Desc.APSO) APSM.SetOrientation(xf);
                    }
                }

                if (!Desc.SPS1) APSM.Update(dt);
                if (APSM.IsSystemDeletable) { APSM.DestroyParticles(); APSM = null; }
            }

            if (APS2 != null)
            {
                if (LastParticleFrame != CurFrame)
                {
                    if (CurFrame > Lifetime) { APS2.SetParticleEmission(false); APS2.EndLifetime(); }
                    else
                    {
                        if (Desc.AP21) APS2.SetGlobalTranslation(trans); else APS2.SetTranslation(trans);
                        if (Desc.APSO) APS2.SetOrientation(xf);
                    }
                }

                if (!Desc.SPS2) APS2.Update(dt);
                if (APS2.IsSystemDeletable) { APS2.DestroyParticles(); APS2 = null; }
            }

            Swoosh1 = Feed(Swoosh1, Desc.AS11, trans, xf);
            Swoosh2 = Feed(Swoosh2, Desc.AS12, trans, xf);
            Swoosh3 = Feed(Swoosh3, Desc.AS13, trans, xf);
            LastParticleFrame = CurFrame;
        }

        private MpSwooshGen Feed(MpSwooshGen s, bool global, V3 trans, M3 xf)
        {
            if (s == null) return null;
            if (LastParticleFrame != CurFrame)
            {
                if (CurFrame > Lifetime) s.SetParticleEmission(false);
                else
                {
                    if (global) s.SetGlobalTranslation(trans); else s.SetTranslation(trans);
                    s.SetOrientation(xf);
                }
            }

            s.ForceOneUpdate = true;
            s.Update(0.0);
            return s.IsSystemDeletable ? null : s;
        }

        /// <summary>CollisionOccured (not deflected): the projectile stops at the hit and its systems stop emitting.</summary>
        public void Hit(V3 worldPos)
        {
            LocalOffset = LocalToWorldRot.Transposed.Mul(worldPos - WorldOffset - LocalToWorldPos) - ProjOffset;
            InterpolationOffset = V3.Zero;
            Active = false;
            // (the hit projectile is removed by its owner once its systems finish: end their lifetimes so they can)
            if (APSM != null) { APSM.SetParticleEmission(false); APSM.EndLifetime(); }
            if (APS2 != null) { APS2.SetParticleEmission(false); APS2.EndLifetime(); }
            if (Swoosh1 != null) Swoosh1.SetParticleEmission(false);
            if (Swoosh2 != null) Swoosh2.SetParticleEmission(false);
            if (Swoosh3 != null) Swoosh3.SetParticleEmission(false);
        }

        public bool IsSystemDeletable
        {
            get
            {
                if (APSM != null && !APSM.IsSystemDeletable) return false;
                if (APS2 != null && !APS2.IsSystemDeletable) return false;
                if (Swoosh1 != null && !Swoosh1.IsSystemDeletable) return false;
                if (Swoosh2 != null && !Swoosh2.IsSystemDeletable) return false;
                if (Swoosh3 != null && !Swoosh3.IsSystemDeletable) return false;
                if (Active) return CurFrame >= Lifetime;
                return true;
            }
        }
    }
}
