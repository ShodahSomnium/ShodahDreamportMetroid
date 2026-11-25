using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CScriptDebris: the gibs and chunks the scripts throw (an enemy's Dead -> a generator copies these at the body, an explosion
    /// scatters rock). Debris (0x1B, 18 properties) and DebrisExtended (0x45, 39 properties), read as ScriptLoader reads them.
    /// Activate throws it: an extended one gets a speed in a cone about its up axis (between the min and max magnitudes), a random
    /// spin and a lifetime between its min and max durations; a plain one its Z impulse plus a random part of its velocity. It falls
    /// (a physics actor under gravity), bounces off the world with its restitution (or stops dead: no bounce), shrinks to its end
    /// scale after the scale-out start, trails its first two PARTs and bursts its third where its life ends; then it is gone.
    /// Templates (inactive at load) draw nothing until a generator copies and activates them.
    /// </summary>
    public sealed class MpDebrisEntity : MpActorEntity
    {
        public bool Extended;
        public float ConeAngle, LinMin, LinMax, AngMin, AngMax, MinDur, MaxDur = 1f, ScaleOutStart = 1f, Restitution, Downward;
        public Vector3 EndScale = Vector3.one, LocalOffset, Velocity;
        public float ZImpulse, Duration = 0.5f;
        public bool RandomSpin, Solid2, DieOnProjectile, NoBounce;
        public uint Model = 0xFFFFFFFF, Part0 = 0xFFFFFFFF, Part1 = 0xFFFFFFFF, Part2 = 0xFFFFFFFF;
        public Vector3 Part0Scale = Vector3.one, Part1Scale = Vector3.one, Part2Scale = Vector3.one;

        private bool _thrown, _ended;
        private float _t, _endLinger;
        private Vector3 _vel, _spinAxis, _baseScale = Vector3.one;
        private float _spinDeg;
        private MpEffects.Effect _fx0, _fx1;
        public const float Gravity = 24.5f;
        public static int LabThrown, LabBounces;

        private static Vector3 V(MpReader r) { return new Vector3(r.F32(), r.F32(), r.F32()); }

        /// <summary>The model and the three PARTs of a debris object (loader thread: what to prepare).</summary>
        public static bool Parts(MpScriptObject o, out uint model, out uint p0, out uint p1, out uint p2)
        {
            model = p0 = p1 = p2 = 0xFFFFFFFF;
            MpDebrisEntity d = Read(o);
            if (d == null) return false;
            model = d.Model; p0 = d.Part0; p1 = d.Part1; p2 = d.Part2;
            return true;
        }

        private static MpDebrisEntity Read(MpScriptObject o)
        {
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            if (o.Type == 0x45 && o.PropCount == 0x27)
            {
                var d = new MpDebrisEntity { Extended = true };
                d.H = MpScriptRead.ReadHead(r, true);
                d.ConeAngle = r.F32(); d.LinMin = r.F32(); d.LinMax = r.F32(); d.AngMin = r.F32(); d.AngMax = r.F32();
                d.MinDur = r.F32(); d.MaxDur = r.F32();
                r.F32(); r.F32();                       // colour in / out times
                r.Skip(32);                             // colour, ends colour
                d.ScaleOutStart = r.F32() / 100f;
                d.EndScale = V(r);
                d.Restitution = r.F32();
                d.Downward = r.F32();
                d.LocalOffset = V(r);
                d.Model = r.U32();
                MpScriptRead.ReadActorParams(r);
                d.Part0 = r.U32(); d.Part0Scale = V(r); r.Bool(); r.Bool(); r.S32();
                d.Part1 = r.U32(); d.Part1Scale = V(r); r.Bool(); r.Bool(); r.S32();
                d.Part2 = r.U32(); d.Part2Scale = d.Part1Scale; V(r); r.S32();     // retail uses the second scale for the third
                d.Solid2 = r.Bool(); d.DieOnProjectile = r.Bool(); d.NoBounce = r.Bool();
                d.Active = r.Bool();
                return d;
            }

            if (o.Type == 0x1B && o.PropCount == 0x12)
            {
                var d = new MpDebrisEntity();
                d.H = MpScriptRead.ReadHead(r, true);
                d.ZImpulse = r.F32();
                d.Velocity = V(r);
                r.Skip(16);
                r.F32();                                // mass
                d.Restitution = r.F32();
                d.Duration = r.F32();
                if (d.Duration < 0f) d.Duration = 0.5f;
                r.S32();
                d.RandomSpin = r.Bool();
                d.Model = r.U32();
                MpScriptRead.ReadActorParams(r);
                d.Part0 = r.U32(); d.Part0Scale = V(r);
                r.Bool();
                d.Active = r.Bool();
                return d;
            }

            return null;
        }

        public MpScriptRead.Head H;

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpDebrisEntity d = Read(o);
            if (d == null) return null;
            MpModel m = MpAssets.Valid(d.Model) ? MpModels.Get(d.Model) : null;
            if (m != null)
            {
                m.Build(true);
                d.Go = m.Instantiate("Debris " + o.Name, view.Root.transform);
                d._baseScale = new Vector3(d.H.Scale.X, d.H.Scale.Y, d.H.Scale.Z);
                Quaternion rot = MpSpace.GameRotation(new Vector3(d.H.Rot.X, d.H.Rot.Y, d.H.Rot.Z));
                d.Go.transform.localPosition = new Vector3(d.H.Pos.X, d.H.Pos.Y, d.H.Pos.Z) + rot * d.LocalOffset;
                d.Go.transform.localRotation = rot;
                d.Go.transform.localScale = d._baseScale;
                foreach (Collider c in d.Go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            else
            {
                d.Go = new GameObject("Debris " + o.Name + " (no model)");
                d.Go.transform.SetParent(view.Root.transform, false);
                d.Go.transform.localPosition = new Vector3(d.H.Pos.X, d.H.Pos.Y, d.H.Pos.Z);
            }

            bool on = d.Active;
            d.Active = false;
            d.Go.SetActive(false);
            // active at load: no Activate comes - an extended one (lifetime 0) is gone at once, a plain one sits for its duration
            if (on && !d.Extended) { d._thrown = true; d.Active = true; d.Go.SetActive(true); d._vel = Vector3.zero; }
            return d;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Activate) { Throw(); return; }
            if (msg == MpScriptMsg.Deactivate) { End(true); return; }
            base.OnMessage(msg, from);
        }

        public override void SetActive(bool on)
        {
            Active = on;
            if (Go != null && Go.activeSelf != (on && !_ended)) Go.SetActive(on && !_ended);
        }

        private static float Fr() { return Random.value * 2f - 1f; }

        private static float Range(float a, float b) { return a + (b - a) * Random.value; }

        /// <summary>debris_cone: a vector of a random magnitude within a cone about +Z (the cone angle is the full angle in degrees).</summary>
        private static Vector3 Cone(float angle, float min, float max)
        {
            float mag = Range(min, max);
            float cos = Mathf.Cos(Mathf.PI / 360f * angle);
            float side = 1f - (1f - cos) * Random.value;
            float hyp = mag * Mathf.Sqrt(Mathf.Max(0f, 1f - side * side));
            float a = 2f * Mathf.PI * Random.value;
            return new Vector3(hyp * Mathf.Cos(a), hyp * Mathf.Sin(a), mag * side);
        }

        /// <summary>CScriptDebris::AcceptScriptMsg Activate: the throw.</summary>
        private void Throw()
        {
            if (Go == null) return;
            _thrown = true;
            _ended = false;
            _t = 0f;
            Active = true;
            Go.SetActive(true);
            Quaternion rot = Go.transform.localRotation;
            if (Extended)
            {
                _vel = rot * Cone(ConeAngle, LinMin, LinMax) + new Vector3(0f, 0f, -Downward);
                Vector3 spin = Cone(360f, AngMin, AngMax);
                _spinDeg = spin.magnitude;
                _spinAxis = spin.sqrMagnitude > 1e-6f ? spin.normalized : Vector3.forward;
                Duration = Range(MinDur, MaxDur);
            }
            else
            {
                _vel = rot * Vector3.forward + new Vector3(Velocity.x * Fr(), Velocity.y * Fr(), Velocity.z * Mathf.Abs(Fr()) + ZImpulse);
                if (RandomSpin && Random.value < 0.5f) { _spinAxis = new Vector3(45f * Fr(), 15f * Fr(), 35f * Fr()); _spinDeg = _spinAxis.magnitude; _spinAxis.Normalize(); }
            }

            Vector3 at = Go.transform.localPosition;
            if (MpAssets.Valid(Part0)) { _fx0 = MpEffects.Spawn(Part0, at, MpEffects.GameOrientation(Quaternion.identity), true); if (_fx0 != null) _fx0.SetGlobal(Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), Part0Scale.x); }
            if (MpAssets.Valid(Part1)) { _fx1 = MpEffects.Spawn(Part1, at, MpEffects.GameOrientation(Quaternion.identity), true); if (_fx1 != null) _fx1.SetGlobal(Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), Part1Scale.x); }
            LabThrown++;
        }

        public override void Think(float dt)
        {
            if (!_thrown || Go == null) return;
            if (_ended)
            {
                _endLinger -= dt;
                if (_endLinger <= 0f) { _thrown = false; Active = false; }
                return;
            }

            _t += dt;
            Transform tr = Go.transform;
            Vector3 p = tr.localPosition;
            _vel.z -= Gravity * dt;
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            RaycastHit hit;
            if (len > 1e-4f && MpEnemy.WorldRay(MpSpace.ToUnity(p), MpSpace.DirToUnity(step / len), len * MpSpace.Scale + 0.01f, out hit))
            {
                Vector3 n = MpSpace.DirToGame(hit.normal).normalized;
                p = MpSpace.ToGame(hit.point) + n * 0.05f;
                if (NoBounce) { _vel = Vector3.zero; Duration = Mathf.Min(Duration, _t); }
                else { _vel = Vector3.Reflect(_vel, n) * Mathf.Clamp01(Restitution > 0f ? Restitution : 0.3f); _spinDeg *= 0.6f; LabBounces++; }
            }
            else p += step;

            tr.localPosition = p;
            if (_spinDeg > 0f) tr.localRotation = Quaternion.AngleAxis(_spinDeg * dt, _spinAxis) * tr.localRotation;
            float lt = Duration > 0f ? _t / Duration : 1f;
            float s = lt > ScaleOutStart && ScaleOutStart < 1f ? (_t - Duration * ScaleOutStart) / (Duration * (1f - ScaleOutStart)) : 0f;
            tr.localScale = Vector3.Lerp(_baseScale, Vector3.Scale(_baseScale, EndScale), Mathf.Clamp01(s));
            M3Follow(_fx0, p);
            M3Follow(_fx1, p);
            if (_t >= Duration) End(false);
        }

        private static void M3Follow(MpEffects.Effect fx, Vector3 p)
        {
            if (fx != null) fx.SetPlace(p, MpEffects.GameOrientation(Quaternion.identity));
        }

        /// <summary>The end of its life: the trails stop emitting (their particles finish), the third PART bursts here, the model goes.</summary>
        private void End(bool quiet)
        {
            if (_ended || Go == null) return;
            _ended = true;
            _endLinger = 0.1f;
            if (_fx0 != null) { _fx0.Finish(); _fx0 = null; }
            if (_fx1 != null) { _fx1.Finish(); _fx1 = null; }
            if (!quiet && MpAssets.Valid(Part2))
            {
                MpEffects.Effect e = MpEffects.Spawn(Part2, Go.transform.localPosition, MpEffects.GameOrientation(Quaternion.identity));
                if (e != null) e.SetGlobal(Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), Part2Scale.x);
            }

            Go.SetActive(false);
        }

        public override void OnRemoved()
        {
            if (_fx0 != null) _fx0.Finish();
            if (_fx1 != null) _fx1.Finish();
            base.OnRemoved();
        }
    }
}
