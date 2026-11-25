using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>What a running particle system offers its owner and its parent (CParticleGen).</summary>
    public interface IPartGen
    {
        string Kind { get; }                 // PART, SWHC
        bool Update(double dt);
        bool IsSystemDeletable { get; }
        void SetTranslation(V3 t);
        void SetOrientation(M3 o);
        void SetGlobalTranslation(V3 t);
        void SetGlobalOrientation(M3 o);
        void SetGlobalScale(V3 s);
        void SetLocalScale(V3 s);
        void SetParticleEmission(bool on);
        void SetModulationColor(C4 c);
        void EndLifetime();
        void DestroyParticles();
    }

    /// <summary>
    /// A running PART (decomp Kyoto/Particles CElementGen): fixed 60 Hz steps (kTickTime), each step ages and moves the particles
    /// (velocity modifiers VEL1-4, size / rotation / line / colour per frame), emits GRTE particles (MAXP cap, the global 2560 cap),
    /// spawns its children (ICTS at CSSD, IITS every PISY from SISY, KSSM keyframes, IDTS at death, the SSWH swoosh at SSSD).
    /// Positions are in the system's space: world = global translation + global orientation x (position x scales); see
    /// <see cref="WorldOf"/>. Rendering is the Unity side's (MpParticleView); this class holds what it needs.
    /// </summary>
    public sealed class MpElementGen : IPartGen, IPartSystem
    {
        public const double TickTime = 1.0 / 60.0;
        public const int GlobalMaxParticles = 0xA00;
        [ThreadStatic] private static int _aliveCount;
        public static ushort Seed = 99;

        public readonly MpGenDesc Desc;
        public readonly MpPartLibrary Lib;
        public string Kind { get { return "PART"; } }

        public int CurFrame, PrevFrame = -1, PSLT, MAXP, MBSP, CSSD, PISY = 16, SISY = 16, SSSD, SESD;
        private double _curSeconds;
        public bool Emission = true;
        private float _genRemainder;
        public float GeneratorRate = 1f;
        public V3 Translation, GlobalTranslation, POFS, SSPO, SEPO;
        public V3 GlobalScale = new V3(1, 1, 1), LocalScale = new V3(1, 1, 1);
        public M3 Orientation = M3.Identity, OrientationInverse = M3.Identity, GlobalOrientation = M3.Identity;
        public readonly List<MpParticle> Particles = new List<MpParticle>();
        private readonly List<float[]> _adv = new List<float[]>();
        public int ActiveCount, Cumulative;
        public bool ZTest = true, LIT, AAPH, ZBUF, ORNT, MBLR, LINE, EnableADV;
        private bool _warmed, _translationDirty;
        private readonly ModVecEl[] _vel = new ModVecEl[4];
        private readonly bool[] _vmd = new bool[4];
        private readonly MpRandom16 _rand;
        private readonly float[] _external = new float[16];
        public float TimeDeltaScale = 1f;
        public C4 ModulationColor = C4.White;
        public readonly List<IPartGen> Children = new List<IPartGen>();

        public M3 Orientation_ { get { return Orientation; } }
        M3 IPartSystem.Orientation { get { return Orientation; } }
        V3 IPartSystem.Translation { get { return Translation; } }
        public float ExternalVar(int i) { return _external[i & 15]; }
        public void SetExternalVar(int i, float v) { _external[i & 15] = v; }
        public int CumulativeParticleCount { get { return Cumulative; } }
        public int ParticleCount { get { return Particles.Count; } }
        public int EmitterTime { get { return CurFrame; } }

        /// <summary>kOSF_Two (a partly charged shot): child systems whose description has OPTS are not started.</summary>
        public bool EnableOPTS;

        public MpElementGen(MpGenDesc desc, MpPartLibrary lib, bool enableOpts = false)
        {
            Desc = desc;
            EnableOPTS = enableOpts;
            Lib = lib;
            uint seed = Seed;
            _rand = new MpRandom16(seed);
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                if (desc.SEED != null) { int s = 0; desc.SEED.Get(CurFrame, ref s); seed = (uint)s; }
                _rand.Seed = seed;
                LIT = desc.LIT_;
                AAPH = desc.AAPH;
                ZBUF = desc.ZBUF;
                ORNT = desc.ORNT;
                MBLR = ORNT ? false : desc.MBLR;
                if (desc.MBSP != null) desc.MBSP.Get(CurFrame, ref MBSP);
                int idx = 0;
                ModVecEl[] v = { desc.VEL1, desc.VEL2, desc.VEL3, desc.VEL4 };
                bool[] m = { desc.VMD1, desc.VMD2, desc.VMD3, desc.VMD4 };
                for (int i = 0; i < 4; i++) if (v[i] != null) { _vel[idx] = v[i]; _vmd[idx] = m[i]; idx++; }
                foreach (RealEl a in desc.ADV) if (a != null) EnableADV = true;
                if (desc.CSSD != null) desc.CSSD.Get(0, ref CSSD);
                if (desc.PISY != null) { desc.PISY.Get(0, ref PISY); if (PISY <= 0) PISY = 1; }
                if (desc.SISY != null) desc.SISY.Get(0, ref SISY);
                if (desc.SSSD != null) desc.SSSD.Get(0, ref SSSD);
                if (desc.SSPO != null) { desc.SSPO.Get(0, ref SSPO); if (!desc.SSPO.IsFastConstant) _translationDirty = true; }
                if (desc.SESD != null) desc.SESD.Get(0, ref SESD);
                if (desc.SEPO != null) { desc.SEPO.Get(0, ref SEPO); if (!desc.SEPO.IsFastConstant) _translationDirty = true; }
                if (desc.POFS != null) { desc.POFS.Get(CurFrame, ref POFS); if (!desc.POFS.IsFastConstant) _translationDirty = true; }
                if (desc.PSLT != null) desc.PSLT.Get(0, ref PSLT); else PSLT = 0x7FFFFF;
                if (desc.MAXP != null) desc.MAXP.Get(CurFrame, ref MAXP);
                LINE = desc.LINE;
                InitLight();
            }
            finally { MpRandom16.Current = prev; }
        }


        // ------------------------------------------------------------------ the system's light (CElementGen::GetLight)

        public int LightType;              // 0 none, 1 custom (point), 2 directional, 3 spot
        public int FalloffType = 1;        // 0 constant, 1 linear, 2 quadratic
        public C4 LCLR = C4.White;
        public float LINT = 1f, LFOR = 1f, LSLA = 45f;
        public V3 LOFF, LDIR = new V3(1, 0, 0);

        private void InitLight()
        {
            if (Desc.LTYP == null) return;
            int t = 0;
            Desc.LTYP.Get(CurFrame, ref t);
            LightType = t >= 0 && t <= 3 ? t : 0;
            if (Desc.LFOT != null)
            {
                int f = 1;
                Desc.LFOT.Get(CurFrame, ref f);
                FalloffType = f >= 0 && f <= 2 ? f : 1;
            }
        }

        private void UpdateLightParameters()
        {
            if (Desc.LCLR != null) Desc.LCLR.Get(CurFrame, ref LCLR);
            if (Desc.LINT != null) Desc.LINT.Get(CurFrame, ref LINT);
            if (LightType != 2)
            {
                if (Desc.LOFF != null) Desc.LOFF.Get(CurFrame, ref LOFF);
                if (Desc.LFOR != null) Desc.LFOR.Get(CurFrame, ref LFOR);
                if (LightType == 3 && Desc.LSLA != null) Desc.LSLA.Get(CurFrame, ref LSLA);
            }

            if (LightType != 1 && Desc.LDIR != null) Desc.LDIR.Get(CurFrame, ref LDIR);
        }

        /// <summary>The system's point light (custom and spot treated as points): world position, colour x intensity, attenuation.</summary>
        public bool TryGetLight(out V3 worldPos, out C4 color, out float k0, out float k1, out float k2)
        {
            worldPos = WorldOf(Translation + LOFF);
            color = new C4(LCLR.R * LINT, LCLR.G * LINT, LCLR.B * LINT, LCLR.A);
            k0 = FalloffType == 0 ? 1f : 0f;
            k1 = FalloffType == 1 ? LFOR : 0f;
            k2 = FalloffType == 2 ? LFOR : 0f;
            return LightType == 1 || LightType == 3;
        }
        // ------------------------------------------------------------------ transforms (children follow)

        public void SetTranslation(V3 t)
        {
            Translation = t;
            foreach (IPartGen c in Children) c.SetTranslation(c.Kind == "SWHC" ? t + SSPO : t);
        }

        public void SetGlobalTranslation(V3 t) { GlobalTranslation = t; foreach (IPartGen c in Children) c.SetGlobalTranslation(t); }
        public void SetGlobalScale(V3 s) { GlobalScale = s; foreach (IPartGen c in Children) c.SetGlobalScale(s); }
        public void SetLocalScale(V3 s) { LocalScale = s; foreach (IPartGen c in Children) c.SetLocalScale(s); }
        public void SetModulationColor(C4 c) { ModulationColor = c; foreach (IPartGen ch in Children) ch.SetModulationColor(c); }

        public void SetOrientation(M3 o)
        {
            Orientation = o;
            OrientationInverse = o.Transposed;
            foreach (IPartGen c in Children) c.SetOrientation(o);
        }

        public void SetGlobalOrientation(M3 o) { GlobalOrientation = o; foreach (IPartGen c in Children) c.SetGlobalOrientation(o); }

        public void SetParticleEmission(bool on) { Emission = on; foreach (IPartGen c in Children) c.SetParticleEmission(on); }

        public void EndLifetime()
        {
            PSLT = 0;
            foreach (IPartGen c in Children)
            {
                if (c.Kind == "PART") c.EndLifetime();
                else c.SetParticleEmission(false);
            }
        }

        public void DestroyParticles()
        {
            _aliveCount -= Particles.Count;
            Particles.Clear();
            foreach (IPartGen c in Children) c.DestroyParticles();
        }

        public bool IsSystemDeletable
        {
            get
            {
                foreach (IPartGen c in Children) if (!c.IsSystemDeletable) return false;
                return PSLT < CurFrame && ActiveCount == 0;
            }
        }

        /// <summary>A particle position in game world space (the renderer's system matrix with uniform scales).</summary>
        public V3 WorldOf(V3 p)
        {
            return GlobalTranslation + GlobalOrientation.Mul(V3.Mul(V3.Mul(p, GlobalScale), LocalScale));
        }

        public float ScaleFactor { get { return GlobalScale.X * LocalScale.X; } }

        // ------------------------------------------------------------------ update

        public bool Update(double dt)
        {
            IPartSystem prevSys = MpPartGlobals.CurrentSystem;
            MpPartGlobals.CurrentSystem = this;
            try
            {
                if (Desc.PSWT != null && !_warmed)
                {
                    int pswt = 0;
                    Desc.PSWT.Get(CurFrame, ref pswt);
                    InternalUpdate(TickTime * pswt);
                    _warmed = true;
                }

                return InternalUpdate(dt);
            }
            finally { MpPartGlobals.CurrentSystem = prevSys; }
        }

        private static bool Close(double a, double b, double eps = 1.1920929e-7) { return Math.Abs(a - b) < eps; }

        private bool InternalUpdate(double dt)
        {
            MpRandom16 prevRand = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                int steps = 0;
                double t = CurFrame * TickTime;
                double dt1 = Close(dt, TickTime) ? TickTime : dt;
                MpPartGlobals.SetEmitterTime(CurFrame);
                if (Desc.PSTS != null)
                {
                    float psts = 1f;
                    Desc.PSTS.Get(CurFrame, ref psts);
                    dt1 = Math.Max(0.0, dt1 * psts);
                }

                _curSeconds += dt1;
                if (MBLR && dt > 0.0 && Desc.MBSP != null) Desc.MBSP.Get(CurFrame, ref MBSP);

                while (t < _curSeconds && !Close(t, _curSeconds))
                {
                    MpPartGlobals.SetEmitterTime(CurFrame);
                    UpdateExistingParticles();
                    MpPartGlobals.SetParticleLifetime(PSLT);
                    if (CurFrame < PSLT && Emission && Desc.GRTE != null)
                    {
                        float grte = 0f;
                        if (Desc.GRTE.Get(CurFrame, ref grte))
                        {
                            _aliveCount -= Particles.Count;
                            Particles.Clear();
                            return true;
                        }

                        grte = Math.Max(0f, grte * GeneratorRate);
                        _genRemainder += grte;
                        int gen = (int)Math.Floor(_genRemainder);
                        _genRemainder -= gen;
                        if (Desc.MAXP != null) Desc.MAXP.Get(CurFrame, ref MAXP);
                        CreateNewParticles(gen);
                    }

                    if (_translationDirty) UpdatePSTranslationAndOrientation();
                    if (LightType != 0) UpdateLightParameters();
                    UpdateChildParticleSystems(TickTime);
                    ++steps;
                    t += TickTime;
                    ++CurFrame;
                }

                if (Close(t, _curSeconds))
                {
                    _curSeconds = t;
                    TimeDeltaScale = 1f;
                }
                else
                {
                    UpdateChildParticleSystems(dt1 - steps * TickTime);
                    TimeDeltaScale = 1f - (float)((t - _curSeconds) / TickTime);
                }

                return false;
            }
            finally { MpRandom16.Current = prevRand; }
        }

        private void UpdateAdvance(int particleIndex, int particleFrame)
        {
            while (_adv.Count <= particleIndex) _adv.Add(new float[8]);
            float[] p = _adv[particleIndex];
            MpPartGlobals.AccessParameters = p;
            for (int i = 0; i < 8; i++) if (Desc.ADV[i] != null) Desc.ADV[i].Get(particleFrame, ref p[i]);
        }

        private void UpdateVelocitySource(int i, int frame, MpParticle p)
        {
            bool reset;
            if (_vmd[i])
            {
                V3 lv = OrientationInverse.Mul(p.Vel);
                V3 lp = OrientationInverse.Mul(p.Pos - Translation);
                reset = _vel[i].Get(frame, ref lv, ref lp);
                p.Vel = Orientation.Mul(lv);
                p.Pos = Orientation.Mul(lp) + Translation;
            }
            else
            {
                V3 v = p.Vel, pos = p.Pos;
                reset = _vel[i].Get(frame, ref v, ref pos);
                p.Vel = v;
                p.Pos = pos;
            }

            if (reset) p.EndFrame = -1;
        }

        private void UpdateExistingParticles()
        {
            ActiveCount = 0;
            MpPartGlobals.SetEmitterTime(CurFrame);
            MpPartGlobals.AccessParameters = null;
            int i = 0;
            while (i < Particles.Count)
            {
                MpParticle p = Particles[i];
                if (p.EndFrame < CurFrame)
                {
                    // the game swaps the last particle into this slot
                    --_aliveCount;
                    int last = Particles.Count - 1;
                    if (i != last)
                    {
                        p.CopyFrom(Particles[last]);
                        if (EnableADV && last < _adv.Count && ActiveCount < _adv.Count) Array.Copy(_adv[last], _adv[ActiveCount], 8);
                    }

                    Particles.RemoveAt(last);
                    if (i == last) break;
                    if (p.EndFrame < CurFrame) continue;
                }

                p.PrevPos = p.Pos;
                p.Pos = p.Pos + p.Vel;
                int pf = CurFrame - p.StartFrame;
                MpPartGlobals.CurrentParticle = p;
                MpPartGlobals.SetParticleLifetime(p.EndFrame - p.StartFrame);
                MpPartGlobals.UpdateParticleLifetimeTweenValues(pf);
                if (EnableADV) UpdateAdvance(ActiveCount, pf);
                ++ActiveCount;
                for (int k = 0; k < 4 && _vel[k] != null; k++) UpdateVelocitySource(k, pf, p);
                if (LINE)
                {
                    if (Desc.LENG != null) Desc.LENG.Get(pf, ref p.LineLengthOrSize);
                    if (Desc.WIDT != null) Desc.WIDT.Get(pf, ref p.LineWidthOrRota);
                }
                else
                {
                    if (Desc.ROTA != null) Desc.ROTA.Get(pf, ref p.LineWidthOrRota);
                    if (Desc.SIZE != null) Desc.SIZE.Get(pf, ref p.LineLengthOrSize);
                }

                if (Desc.COLR != null) Desc.COLR.Get(pf, ref p.Color);
                ++i;
            }
        }

        private readonly Stack<MpParticle> _pool = new Stack<MpParticle>();

        private void CreateNewParticles(int count)
        {
            if (Particles.Count >= MAXP) return;
            if (count + Particles.Count > MAXP) count = MAXP - Particles.Count;
            if (count + _aliveCount > GlobalMaxParticles) count = GlobalMaxParticles - _aliveCount;
            MpPartGlobals.AccessParameters = null;
            for (int i = 0; i < count; i++)
            {
                MpParticle p = _pool.Count > 0 ? _pool.Pop() : new MpParticle();
                p.EndFrame = 0;
                p.Pos = p.PrevPos = p.Vel = V3.Zero;
                p.Color = new C4(1f, 0f, 1f, 1f);
                Particles.Add(p);
                int index = Particles.Count - 1;
                ++_aliveCount;
                ++ActiveCount;
                ++Cumulative;
                p.StartFrame = CurFrame;
                if (Desc.LTME != null) Desc.LTME.Get(0, ref p.EndFrame);
                MpPartGlobals.SetParticleLifetime(p.EndFrame);
                MpPartGlobals.UpdateParticleLifetimeTweenValues(0);
                MpPartGlobals.CurrentParticle = p;
                if (EnableADV) UpdateAdvance(index, 0);
                p.EndFrame += CurFrame;
                if (Desc.COLR != null) Desc.COLR.Get(0, ref p.Color);
                else p.Color = C4.White;
                V3 trans = new V3(Translation.X / (GlobalScale.X * LocalScale.X), Translation.Y / (GlobalScale.Y * LocalScale.Y), Translation.Z / (GlobalScale.Z * LocalScale.Z));
                if (Desc.EMTR != null)
                {
                    V3 pos = V3.Zero, vel = V3.Zero;
                    Desc.EMTR.Get(CurFrame, ref pos, ref vel);
                    p.Pos = Orientation.Mul(pos) + trans + POFS;
                    p.Vel = Orientation.Mul(vel);
                }
                else
                {
                    p.Pos = trans + POFS;
                    p.Vel = V3.Zero;
                }

                p.PrevPos = p.Pos;
                if (LINE)
                {
                    p.LineLengthOrSize = 1f; p.LineWidthOrRota = 1f;
                    if (Desc.LENG != null) Desc.LENG.Get(0, ref p.LineLengthOrSize);
                    if (Desc.WIDT != null) Desc.WIDT.Get(0, ref p.LineWidthOrRota);
                }
                else
                {
                    p.LineWidthOrRota = 0f; p.LineLengthOrSize = 0.1f;
                    if (Desc.ROTA != null) Desc.ROTA.Get(0, ref p.LineWidthOrRota);
                    if (Desc.SIZE != null) Desc.SIZE.Get(0, ref p.LineLengthOrSize);
                }
            }
        }

        private void UpdatePSTranslationAndOrientation()
        {
            if (PSLT < CurFrame) return;
            if (Desc.POFS != null) Desc.POFS.Get(CurFrame, ref POFS);
            if (Desc.SSPO != null) Desc.SSPO.Get(CurFrame, ref SSPO);
            if (Desc.SEPO != null) Desc.SEPO.Get(CurFrame, ref SEPO);
        }

        private MpElementGen Child(uint id)
        {
            MpGenDesc d = Lib != null ? Lib.Part(id) : null;
            if (d == null) return null;
            var c = new MpElementGen(d, Lib, EnableOPTS);
            c.SetGlobalTranslation(GlobalTranslation);
            c.SetGlobalOrientation(GlobalOrientation);
            c.SetGlobalScale(GlobalScale);
            c.SetLocalScale(LocalScale);
            c.SetTranslation(Translation);
            c.SetOrientation(Orientation);
            c.SetParticleEmission(Emission);
            c.SetModulationColor(ModulationColor);
            return c;
        }

        private bool IsOpts(uint id)
        {
            MpGenDesc d = Lib != null ? Lib.Part(id) : null;
            return d != null && d.OPTS;
        }

        private void UpdateChildParticleSystems(double dt)
        {
            if (Math.Abs(dt) < 1e-7) return;
            if (MpAssets.Valid(Desc.ICTS) && PrevFrame != CurFrame && CurFrame == CSSD)
            {
                int n = 1;
                if (Desc.NCSY != null) Desc.NCSY.Get(CurFrame, ref n);
                for (int i = 0; i < n; i++) { if (EnableOPTS && IsOpts(Desc.ICTS)) break; MpElementGen c = Child(Desc.ICTS); if (c != null) Children.Add(c); }
            }

            if (MpAssets.Valid(Desc.IITS) && PrevFrame != CurFrame && CurFrame < PSLT && Emission && CurFrame >= SISY && (CurFrame - SISY) % PISY == 0)
            {
                MpElementGen c = EnableOPTS && IsOpts(Desc.IITS) ? null : Child(Desc.IITS);
                if (c != null) Children.Add(c);
            }

            if (Desc.KSSM != null && PrevFrame != CurFrame && CurFrame < PSLT && CurFrame < Desc.KSSMEnd)
            {
                ushort backup = Seed;
                foreach (var kv in Desc.KSSM)
                {
                    if (kv.Key != CurFrame) continue;
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        if (EnableOPTS && IsOpts(kv.Value[i])) continue;
                        Seed = (ushort)(backup + i);
                        MpElementGen c = Child(kv.Value[i]);
                        if (c != null) Children.Add(c);
                    }

                    break;
                }

                Seed = backup;
            }

            if (CurFrame == PSLT && PrevFrame != CurFrame && MpAssets.Valid(Desc.IDTS))
            {
                int n = 1;
                if (Desc.NDSY != null) Desc.NDSY.Get(0, ref n);
                for (int i = 0; i < n; i++) { if (EnableOPTS && IsOpts(Desc.IDTS)) break; MpElementGen c = Child(Desc.IDTS); if (c != null) Children.Add(c); }
            }

            if (MpAssets.Valid(Desc.SSWH) && PrevFrame != CurFrame && CurFrame == SSSD && Lib != null)
            {
                MpSwooshDesc sd = Lib.Swoosh(Desc.SSWH);
                if (sd != null)
                {
                    var s = new MpSwooshGen(sd, 0);
                    s.SetGlobalTranslation(GlobalTranslation);
                    s.SetGlobalScale(GlobalScale);
                    s.SetLocalScale(LocalScale);
                    s.SetTranslation(Translation + SSPO);
                    s.SetOrientation(Orientation);
                    s.SetParticleEmission(Emission);
                    Children.Add(s);
                }
            }

            for (int i = 0; i < Children.Count; i++)
            {
                IPartGen c = Children[i];
                c.Update(dt);
                if (c.IsSystemDeletable)
                {
                    c.DestroyParticles();
                    Children.RemoveAt(i);
                    i--;
                }
            }

            PrevFrame = CurFrame;
        }

        /// <summary>Every system in the tree (this one first), for drawing.</summary>
        public void Collect(List<IPartGen> into)
        {
            into.Add(this);
            foreach (IPartGen c in Children)
            {
                var e = c as MpElementGen;
                if (e != null) e.Collect(into);
                else into.Add(c);
            }
        }

        public int ParticleCountAll
        {
            get
            {
                int n = Particles.Count;
                foreach (IPartGen c in Children) { var e = c as MpElementGen; if (e != null) n += e.ParticleCountAll; }
                return n;
            }
        }
    }
}
