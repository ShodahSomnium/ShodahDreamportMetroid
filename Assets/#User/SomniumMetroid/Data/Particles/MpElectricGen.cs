using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>
    /// A running ELSC (decomp Kyoto/Particles CParticleElectric): fixed 60 Hz steps; each step ages the live arcs (SLIF frames,
    /// line widths / colours per arc frame), then while emitting (before LIFE) adds GRAT arcs per frame up to SCNT at a time. An
    /// arc is SSEG+1 points from the initial emitter (IEMT) to the final one (FEMT) - straight with jittered spacing, or a Bezier
    /// when the emitters have velocities - displaced by a midpoint fractal (AMPL / AMPD) turned about each segment around a
    /// common "up". Each arc may carry a swoosh laid along it (SSWH) and start / end systems (GPSM / EPSM). Points are in the
    /// system's space: world = global translation + global orientation x (translation + orientation x (point x scales)), see
    /// <see cref="WorldOf"/>. Rendering is the Unity side's (MpEffects: three additive line layers, LWD1-3 / LCL1-3).
    /// </summary>
    public sealed class MpElectricGen : IPartGen, IPartSystem
    {
        /// <summary>CParticleElectric::CLineManager: one arc slot's points and its three line layers.</summary>
        public sealed class Line
        {
            public V3[] Verts;
            public float Width1 = 1f, Width2 = 2f, Width3 = 3f;
            public C4 Color1 = C4.White, Color2 = C4.White, Color3 = C4.White;
        }

        /// <summary>CParticleElectric::CParticleElectricManager: a live arc (slot, frames left, its start / end systems).</summary>
        public sealed class Arc
        {
            public int Idx, Slif, StartFrame, EndFrame, GpsmIdx = -1, EpsmIdx = -1;
        }

        public const double TickTime = 1.0 / 60.0;
        /// <summary>CParticleElectric::sSeed: each new system takes the next seed.</summary>
        public static ushort Seed = 99;

        public readonly MpElectricDesc Desc;
        public readonly MpPartLibrary Lib;
        public string Kind { get { return "ELSC"; } }

        public int CurFrame, LIFE, SSEG = 8, SCNT = 1;
        private double _curTime;
        private float _genRem;
        public V3 Translation, GlobalTranslation, GlobalScale = new V3(1, 1, 1), LocalScale = new V3(1, 1, 1);
        public M3 Orientation = M3.Identity, GlobalOrientation = M3.Identity;
        public C4 ModulationColor = C4.White;
        public bool Emitting = true;
        /// <summary>The renderer's: draw the swoosh / GPSM / EPSM children (false while their textures are still loading).</summary>
        public bool DrawChildren = true;
        public V3? OverrideIPos, OverrideIVel, OverrideFPos, OverrideFVel;
        private bool _transformDirty = true;
        private M3 _cachedRot = M3.Identity;
        private V3 _cachedTrans;
        private readonly MpRandom16 _rand;

        private readonly bool[] _allocated;
        public readonly List<Arc> Arcs = new List<Arc>();
        /// <summary>Per slot (null when the ELSC has none of them).</summary>
        public readonly Line[] Lines;
        public readonly MpSwooshGen[] Swooshes;
        public readonly MpElementGen[] Gpsm, Epsm;
        private readonly V3[] _verts, _offsets;
        private readonly float[] _mags;

        /// <summary>CParticleElectric::CParticleElectric.</summary>
        public MpElectricGen(MpElectricDesc desc, MpPartLibrary lib)
        {
            Desc = desc;
            Lib = lib;
            _rand = new MpRandom16(Seed++);
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = _rand;
            try
            {
                if (desc.SSEG != null) desc.SSEG.Get(CurFrame, ref SSEG);
                if (desc.SCNT != null) desc.SCNT.Get(CurFrame, ref SCNT);
                if (SCNT > 32) SCNT = 32;
                if (SCNT < 0) SCNT = 0;
                if (SSEG < 1) SSEG = 1;
                if (desc.LIFE != null) desc.LIFE.Get(0, ref LIFE);
                else LIFE = 0x7fffff;
                _allocated = new bool[SCNT];

                MpSwooshDesc sw = lib != null && MpAssets.Valid(desc.SSWH) ? lib.Swoosh(desc.SSWH) : null;
                if (sw != null)
                {
                    Swooshes = new MpSwooshGen[SCNT];
                    for (int i = 0; i < SCNT; i++)
                    {
                        var s = new MpSwooshGen(sw, SSEG);
                        for (int j = 0; j < s.Points.Length; j++)
                        {
                            s.ForceOneUpdate = true;
                            s.Update(0.0);
                        }

                        Swooshes[i] = s;
                    }
                }

                ++SSEG;
                _verts = new V3[SSEG];
                _offsets = new V3[SSEG];
                _mags = new float[SSEG];

                MpGenDesc g = lib != null && MpAssets.Valid(desc.GPSM) ? lib.Part(desc.GPSM) : null;
                if (g != null)
                {
                    Gpsm = new MpElementGen[SCNT];
                    for (int i = 0; i < SCNT; i++) { Gpsm[i] = new MpElementGen(g, lib); Gpsm[i].SetParticleEmission(false); }
                }

                MpGenDesc e = lib != null && MpAssets.Valid(desc.EPSM) ? lib.Part(desc.EPSM) : null;
                if (e != null)
                {
                    Epsm = new MpElementGen[SCNT];
                    for (int i = 0; i < SCNT; i++) { Epsm[i] = new MpElementGen(e, lib); Epsm[i].SetParticleEmission(false); }
                }

                if (desc.HasLines)
                {
                    Lines = new Line[SCNT];
                    for (int i = 0; i < SCNT; i++) Lines[i] = new Line();
                }
            }
            finally { MpRandom16.Current = prev; }
        }

        // ------------------------------------------------------------------ IPartSystem (what elements may ask while it updates)

        M3 IPartSystem.Orientation { get { return Orientation; } }
        V3 IPartSystem.Translation { get { return Translation; } }
        public float ExternalVar(int i) { return 0f; }
        public int CumulativeParticleCount { get { return 0; } }
        public int ParticleCount { get { return Arcs.Count * SSEG; } }
        public int EmitterTime { get { return CurFrame; } }

        // ------------------------------------------------------------------ transforms

        public void SetTranslation(V3 t) { Translation = t; _transformDirty = true; }
        public void SetOrientation(M3 o) { Orientation = o; _transformDirty = true; }

        public void SetGlobalTranslation(V3 t)
        {
            GlobalTranslation = t;
            _transformDirty = true;
            if (Swooshes != null) foreach (Arc a in Arcs) Swooshes[a.Idx].SetGlobalTranslation(t);
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.SetGlobalTranslation(t);
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.SetGlobalTranslation(t);
        }

        public void SetGlobalOrientation(M3 o)
        {
            GlobalOrientation = o;
            _transformDirty = true;
            if (Swooshes != null) foreach (Arc a in Arcs) Swooshes[a.Idx].SetGlobalOrientation(o);
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.SetGlobalOrientation(o);
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.SetGlobalOrientation(o);
        }

        public void SetGlobalScale(V3 s) { GlobalScale = s; _transformDirty = true; }

        public void SetLocalScale(V3 s)
        {
            LocalScale = s;
            _transformDirty = true;
            if (Swooshes != null) foreach (Arc a in Arcs) Swooshes[a.Idx].SetLocalScale(s);
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.SetLocalScale(s);
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.SetLocalScale(s);
        }

        public void SetParticleEmission(bool on) { Emitting = on; }
        public void SetModulationColor(C4 c) { ModulationColor = c; }

        /// <summary>
        /// No EndLifetime on CParticleElectric (the game's owners stop drawing it); here: no new arcs, and the start / end systems
        /// end theirs (as they do at LIFE), so the system becomes deletable once the live arcs and particles are gone.
        /// </summary>
        public void EndLifetime()
        {
            Emitting = false;
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.EndLifetime();
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.EndLifetime();
        }

        /// <summary>CParticleElectric::DestroyParticles is empty in the game; here the live arcs go at once (a killed effect).</summary>
        public void DestroyParticles()
        {
            Arcs.Clear();
            for (int i = 0; i < _allocated.Length; i++) _allocated[i] = false;
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.DestroyParticles();
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.DestroyParticles();
        }

        /// <summary>CParticleElectric::UpdateCachedTransform: global translation x global orientation x translation x orientation.</summary>
        private void UpdateCachedTransform()
        {
            _cachedRot = Mul(GlobalOrientation, Orientation);
            _cachedTrans = GlobalTranslation + GlobalOrientation.Mul(Translation);
            _transformDirty = false;
        }

        private static M3 Mul(M3 a, M3 b) { return new M3 { C0 = a.Mul(b.C0), C1 = a.Mul(b.C1), C2 = a.Mul(b.C2) }; }

        /// <summary>A point in game world space (RenderLines' model matrix: global, local transform, global and local scale).</summary>
        public V3 WorldOf(V3 p)
        {
            return GlobalTranslation + GlobalOrientation.Mul(Translation + Orientation.Mul(V3.Mul(V3.Mul(p, GlobalScale), LocalScale)));
        }

        public float ScaleFactor { get { return GlobalScale.X * LocalScale.X; } }

        /// <summary>CParticleElectric::IsSystemDeletable.</summary>
        public bool IsSystemDeletable
        {
            get
            {
                if (Emitting && CurFrame < LIFE) return false;
                if (Arcs.Count > 0) return false;
                if (Gpsm != null) foreach (MpElementGen g in Gpsm) if (!g.IsSystemDeletable) return false;
                if (Epsm != null) foreach (MpElementGen g in Epsm) if (!g.IsSystemDeletable) return false;
                return true;
            }
        }

        // ------------------------------------------------------------------ update

        /// <summary>CParticleElectric::Update: whole 60 Hz frames up to the accumulated time.</summary>
        public bool Update(double dt)
        {
            MpRandom16 prevRand = MpRandom16.Current;
            IPartSystem prevSys = MpPartGlobals.CurrentSystem;
            MpRandom16.Current = _rand;
            try
            {
                bool updated = false;
                bool emitting = Emitting && CurFrame < LIFE;
                double evalTime = CurFrame * TickTime;
                _curTime += Math.Max(0.0, dt);
                if (_transformDirty)
                {
                    UpdateCachedTransform();
                    if (Swooshes != null)
                        foreach (Arc a in Arcs)
                        {
                            MpSwooshGen s = Swooshes[a.Idx];
                            s.SetGlobalTranslation(_cachedTrans);
                            s.SetGlobalOrientation(_cachedRot);
                            s.SetGlobalScale(GlobalScale);
                            s.SetLocalScale(LocalScale);
                        }

                    PlaceChildren(Gpsm);
                    PlaceChildren(Epsm);
                    updated = true;
                }

                while (evalTime < _curTime)
                {
                    MpPartGlobals.CurrentSystem = this;
                    MpPartGlobals.SetEmitterTime(CurFrame);
                    UpdateElectricalEffects();
                    if (emitting) AddElectricalEffects();
                    StepChildren(Gpsm);
                    StepChildren(Epsm);
                    updated = true;
                    evalTime += TickTime;
                    ++CurFrame;
                }

                return updated;
            }
            finally
            {
                MpRandom16.Current = prevRand;
                MpPartGlobals.CurrentSystem = prevSys;
            }
        }

        private void PlaceChildren(MpElementGen[] gens)
        {
            if (gens == null) return;
            foreach (MpElementGen g in gens)
            {
                g.SetGlobalTranslation(_cachedTrans);
                g.SetGlobalOrientation(_cachedRot);
                g.SetGlobalScale(GlobalScale);
                g.SetLocalScale(LocalScale);
            }
        }

        private void StepChildren(MpElementGen[] gens)
        {
            if (gens == null) return;
            if (CurFrame >= LIFE) foreach (MpElementGen g in gens) g.EndLifetime();
            foreach (MpElementGen g in gens) g.Update(TickTime);
            // the children set their own random while they step; this system's goes back for its next frame
            MpRandom16.Current = _rand;
            MpPartGlobals.CurrentSystem = this;
        }

        /// <summary>CParticleElectric::AddElectricalEffects: GRAT arcs per frame (a GRAT that reports "done" clears every arc).</summary>
        private void AddElectricalEffects()
        {
            float rate = 0f;
            if (Desc.GRAT != null)
            {
                if (Desc.GRAT.Get(CurFrame, ref rate))
                {
                    Arcs.Clear();
                    for (int i = 0; i < _allocated.Length; i++) _allocated[i] = false;
                    return;
                }

                rate = Math.Max(0f, rate);
            }

            _genRem += rate;
            int count = (int)Math.Floor(_genRem);
            _genRem -= count;
            CreateNewParticles(count);
        }

        /// <summary>CParticleElectric::CreateNewParticles: each new arc takes the first free slot, gets its points, line, swoosh and end systems.</summary>
        private void CreateNewParticles(int count)
        {
            int allocIdx = 0;
            for (int i = 0; i < count; i++)
            {
                if (Arcs.Count >= SCNT) return;
                for (; allocIdx < _allocated.Length; ++allocIdx)
                {
                    if (_allocated[allocIdx]) continue;
                    _allocated[allocIdx] = true;
                    int lifetime = 1;
                    if (Desc.SLIF != null) Desc.SLIF.Get(CurFrame, ref lifetime);
                    var arc = new Arc { Idx = allocIdx, Slif = lifetime, StartFrame = CurFrame, EndFrame = CurFrame + lifetime };
                    Arcs.Add(arc);
                    int frame = CurFrame - arc.StartFrame;
                    MpPartGlobals.SetParticleLifetime(arc.EndFrame - arc.StartFrame);
                    MpPartGlobals.UpdateParticleLifetimeTweenValues(frame);
                    CalculatePoints();

                    if (Swooshes != null)
                    {
                        MpSwooshGen s = Swooshes[allocIdx];
                        s.SetParticleEmission(true);
                        s.SetGlobalTranslation(_cachedTrans);
                        s.SetGlobalOrientation(_cachedRot);
                        s.SetGlobalScale(GlobalScale);
                        s.SetLocalScale(LocalScale);
                        C4 color = C4.White;
                        if (Desc.COLR != null) Desc.COLR.Get(frame, ref color);
                        s.SetModulationColor(Modulate(color, ModulationColor));
                        int cur = s.CurParticle;
                        for (int j = 0; j < s.Points.Length; j++)
                        {
                            cur = (cur + 1) % s.Points.Length;
                            s.Points[cur].Translation = _verts[Math.Min(j, _verts.Length - 1)];
                        }
                    }

                    if (Lines != null)
                    {
                        Line line = Lines[allocIdx];
                        if (line.Verts == null || line.Verts.Length != _verts.Length) line.Verts = new V3[_verts.Length];
                        Array.Copy(_verts, line.Verts, _verts.Length);
                        UpdateLine(allocIdx, 0);
                    }

                    V3 scale = V3.Mul(GlobalScale, LocalScale);
                    if (Gpsm != null)
                        for (int j = 0; j < Gpsm.Length; j++)
                        {
                            if (Gpsm[j].Emission) continue;
                            Gpsm[j].SetTranslation(V3.Mul(scale, _verts[0]));
                            Gpsm[j].SetParticleEmission(true);
                            arc.GpsmIdx = j;
                            break;
                        }

                    if (Epsm != null)
                        for (int j = 0; j < Epsm.Length; j++)
                        {
                            if (Epsm[j].Emission) continue;
                            Epsm[j].SetTranslation(V3.Mul(scale, _verts[_verts.Length - 1]));
                            Epsm[j].SetParticleEmission(true);
                            arc.EpsmIdx = j;
                            break;
                        }

                    break;
                }
            }
        }

        /// <summary>CParticleElectric::UpdateElectricalEffects: arcs at the end of their SLIF free their slot; the rest age a frame.</summary>
        private void UpdateElectricalEffects()
        {
            for (int i = 0; i < Arcs.Count;)
            {
                Arc arc = Arcs[i];
                if (arc.Slif <= 1)
                {
                    _allocated[arc.Idx] = false;
                    if (arc.GpsmIdx != -1) Gpsm[arc.GpsmIdx].SetParticleEmission(false);
                    if (arc.EpsmIdx != -1) Epsm[arc.EpsmIdx].SetParticleEmission(false);
                    Arcs.RemoveAt(i);
                    continue;
                }

                int frame = CurFrame - arc.StartFrame;
                MpPartGlobals.SetParticleLifetime(arc.EndFrame - arc.StartFrame);
                MpPartGlobals.UpdateParticleLifetimeTweenValues(frame);
                if (Swooshes != null)
                {
                    C4 color = C4.White;
                    if (Desc.COLR != null) Desc.COLR.Get(frame, ref color);
                    Swooshes[arc.Idx].SetModulationColor(Modulate(color, ModulationColor));
                }

                if (Lines != null) UpdateLine(arc.Idx, frame);
                --arc.Slif;
                ++i;
            }
        }

        /// <summary>CParticleElectric::UpdateLine: the three layers' colours and widths at the arc's frame.</summary>
        private void UpdateLine(int idx, int frame)
        {
            Line line = Lines[idx];
            if (Desc.LCL1 != null) Desc.LCL1.Get(frame, ref line.Color1);
            if (Desc.LCL2 != null) Desc.LCL2.Get(frame, ref line.Color2);
            if (Desc.LCL3 != null) Desc.LCL3.Get(frame, ref line.Color3);
            if (Desc.LWD1 != null) Desc.LWD1.Get(frame, ref line.Width1);
            if (Desc.LWD2 != null) Desc.LWD2.Get(frame, ref line.Width2);
            if (Desc.LWD3 != null) Desc.LWD3.Get(frame, ref line.Width3);
        }

        public static C4 Modulate(C4 a, C4 b) { return new C4(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A); }

        // ------------------------------------------------------------------ the arc's shape

        /// <summary>CParticleElectric::CalculatePoints: the arc's SSEG+1 points between the emitters, then the fractal displacement.</summary>
        private void CalculatePoints()
        {
            var points = new List<V3>(4);
            V3 pos = V3.Zero, vel = V3.Zero;
            if (Desc.IEMT != null) Desc.IEMT.Get(CurFrame, ref pos, ref vel);
            if (OverrideIPos.HasValue) pos = OverrideIPos.Value;
            if (OverrideIVel.HasValue) vel = OverrideIVel.Value;
            if (NonZero(vel))
            {
                points.Add(pos);
                points.Add(pos + vel);
                points.Add(pos + vel * 2f);
            }
            else points.Add(pos);

            V3 fpos = new V3(0f, 1f, 0f), fvel = V3.Zero;
            if (Desc.FEMT != null) Desc.FEMT.Get(CurFrame, ref fpos, ref fvel);
            if (OverrideFPos.HasValue) fpos = OverrideFPos.Value;
            if (OverrideFVel.HasValue) fvel = OverrideFVel.Value;
            if (NonZero(fvel))
            {
                if (points.Count == 3)
                {
                    points[2] = fpos + fvel;
                    points.Add(fpos);
                }
                else
                {
                    points.Add(fpos + fvel * 2f);
                    points.Add(fpos + fvel);
                    points.Add(fpos);
                }
            }
            else points.Add(fpos);

            int segments = SSEG - 1;
            float segDiv = 1f / segments;
            if (points.Count == 4)
            {
                _verts[0] = points[0];
                float curDiv = segDiv;
                for (int i = 1; i < segments; i++)
                {
                    float t = segDiv * _rand.Range(-0.45f, 0.45f) + curDiv;
                    _verts[i] = Bezier(points[0], points[1], points[2], points[3], t);
                    curDiv += segDiv;
                }

                _verts[segments] = points[3];
            }
            else
            {
                // (the game's order: the jitter is added before the running point steps on)
                _verts[0] = pos;
                V3 accum = _verts[0];
                V3 segDelta = (fpos - pos) * segDiv;
                for (int i = 1; i < segments; i++)
                {
                    float r = _rand.Range(-0.45f, 0.45f);
                    _verts[i] = accum + segDelta * r;
                    accum = accum + segDelta;
                }

                _verts[segments] = fpos;
            }

            for (int i = 0; i < SSEG; i++) _mags[i] = 0f;
            float ampl = 1f;
            if (Desc.AMPL != null)
            {
                Desc.AMPL.Get(CurFrame, ref ampl);
                ampl *= 2f;
            }

            float ampd = 0f;
            if (Desc.AMPD != null) Desc.AMPD.Get(CurFrame, ref ampd);
            int last = _verts.Length - 1;
            if (last >= 1) CalculateFractal(0, last, ampl, ampd);

            V3 up = new V3(0, 0, 1);
            if (last >= 1)
            {
                V3 v0 = _verts[0] - _verts[1];
                V3 v1 = _verts[last] - _verts[1];
                if (v0.CanBeNormalized && v1.CanBeNormalized)
                {
                    v0 = v0.Normalized;
                    v1 = v1.Normalized;
                    float dot = Math.Abs(V3.Dot(v0, v1));
                    if (Math.Abs(dot - 1f) < 1e-5f) up = LookAtUp(_verts[0], _verts[1]);
                    else up = V3.Cross(v0, v1).Normalized;
                }
                else if (!Same(_verts[0], _verts[1])) up = LookAtUp(_verts[0], _verts[1]);
            }

            float commonRand = _rand.Range(0f, 360f);
            for (int i = 1; i < last; i++)
            {
                V3 delta = _verts[i] - _verts[i - 1];
                if (!NonZero(delta)) continue;
                float randomAngle = _mags[i] / ampl * 16f * _rand.Range(-1f, 1f);
                float angle = (randomAngle + commonRand) * (float)Math.PI / 180f;
                _offsets[i] = Rotate(up * _mags[i], delta.Normalized, angle);
            }

            for (int i = 1; i < last; i++) _verts[i] = _verts[i] + _offsets[i];
            if (Desc.ZERY) for (int i = 0; i < _verts.Length; i++) _verts[i].Y = 0f;
        }

        /// <summary>CParticleElectric::CalculateFractal: midpoint displacement of the magnitudes between start and end.</summary>
        private void CalculateFractal(int start, int end, float ampl, float ampd)
        {
            float ratio = (float)(end - start) / _mags.Length;
            float startMag = _mags[start];
            int mid = (start + end) / 2;
            float mag = ratio * ampl;
            float endMag = _mags[end];
            float b = (startMag + endMag) / 2f + mag * _rand.Float() - mag / 2f;
            _mags[mid] = b + (ampd * _rand.Float() - ampd / 2f);
            if ((start + end) % 2 == 1) _mags[end - 1] = _mags[end];
            if (mid - start > 1) CalculateFractal(start, mid, ampl, ampd);
            if (end - mid > 1) CalculateFractal(mid, end, ampl, ampd);
        }

        private static bool NonZero(V3 v) { return v.X != 0f || v.Y != 0f || v.Z != 0f; }
        private static bool Same(V3 a, V3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }

        /// <summary>CMath::GetBezierPoint (de Casteljau).</summary>
        public static V3 Bezier(V3 a, V3 b, V3 c, V3 d, float t)
        {
            V3 ab = Lerp(a, b, t), bc = Lerp(b, c, t), cd = Lerp(c, d, t);
            return Lerp(Lerp(ab, bc, t), Lerp(bc, cd, t), t);
        }

        private static V3 Lerp(V3 a, V3 b, float t) { return a * (1f - t) + b * t; }

        /// <summary>CTransform4f::LookAt(pos, look, Up()).GetUp(): world up made perpendicular to the look direction.</summary>
        private static V3 LookAtUp(V3 pos, V3 look)
        {
            const float eps = 1.1920929e-7f;
            V3 vLook = look - pos;
            float mag = vLook.Magnitude;
            vLook = mag <= eps ? new V3(0, 1, 0) : vLook * (1f / mag);
            V3 upIn = new V3(0, 0, 1);
            float d = Math.Max(-1f, Math.Min(1f, V3.Dot(upIn, vLook)));
            V3 vUp = upIn - vLook * d;
            float vMag = vUp.Magnitude;
            if (vMag <= eps)
            {
                vUp = new V3(0, 0, 1) - vLook * vLook.Z;
                vMag = vUp.Magnitude;
                if (vMag <= eps)
                {
                    vUp = new V3(0, 1, 0) - vLook * vLook.Y;
                    vMag = vUp.Magnitude;
                }
            }

            return vMag > 0f ? vUp * (1f / vMag) : new V3(0, 0, 1);
        }

        /// <summary>CQuaternion::AxisAngle(axis, angle).Transform(v) (Rodrigues).</summary>
        private static V3 Rotate(V3 v, V3 axis, float angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return v * c + V3.Cross(axis, v) * s + axis * (V3.Dot(axis, v) * (1f - c));
        }

        // ------------------------------------------------------------------ drawing support

        /// <summary>The child systems to draw (live arcs' swooshes, the start / end systems), for the renderer.</summary>
        public void CollectChildren(List<IPartGen> into)
        {
            if (Swooshes != null) foreach (Arc a in Arcs) into.Add(Swooshes[a.Idx]);
            if (Gpsm != null) foreach (MpElementGen g in Gpsm) g.Collect(into);
            if (Epsm != null) foreach (MpElementGen g in Epsm) g.Collect(into);
        }

        /// <summary>Every texture / model id the children can use (to load before they are drawn).</summary>
        public static void ResourcesOf(MpElectricDesc d, MpPartLibrary lib, HashSet<uint> textures, HashSet<uint> models)
        {
            if (d == null || lib == null) return;
            if (MpAssets.Valid(d.SSWH))
            {
                MpSwooshDesc s = lib.Swoosh(d.SSWH);
                if (s != null && s.TEXR != null && MpAssets.Valid(s.TEXR.Texture)) textures.Add(s.TEXR.Texture);
            }

            var seen = new HashSet<uint>();
            var todo = new Stack<uint>();
            todo.Push(d.GPSM);
            todo.Push(d.EPSM);
            while (todo.Count > 0)
            {
                uint id = todo.Pop();
                if (!MpAssets.Valid(id) || !seen.Add(id)) continue;
                MpGenDesc g = lib.Part(id);
                if (g == null) continue;
                if (g.TEXR != null && MpAssets.Valid(g.TEXR.Texture)) textures.Add(g.TEXR.Texture);
                if (g.TIND != null && MpAssets.Valid(g.TIND.Texture)) textures.Add(g.TIND.Texture);
                if (MpAssets.Valid(g.PMDL)) models.Add(g.PMDL);
                todo.Push(g.ICTS); todo.Push(g.IDTS); todo.Push(g.IITS);
                if (g.KSSM != null) foreach (var kv in g.KSSM) foreach (uint k in kv.Value) todo.Push(k);
                if (MpAssets.Valid(g.SSWH))
                {
                    MpSwooshDesc s = lib.Swoosh(g.SSWH);
                    if (s != null && s.TEXR != null && MpAssets.Valid(s.TEXR.Texture)) textures.Add(s.TEXR.Texture);
                }
            }
        }
    }
}
