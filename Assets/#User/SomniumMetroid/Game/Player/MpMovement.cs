using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Samus's walking speed, jump and gravity on the player's own avatar (tablet: "Samus speed & jump", on by default), through the
    /// client's motion multipliers (ISomniumPlayerMotion SetMovementSpeed / SetJump / SetGravity: 1 = the client's own, 0..10).
    /// The game's numbers (CTweakPlayer on the disc): top speed 16.5 units/s, jump = +50 units/s^2 for the 0.25 s the button may be
    /// held (12.5 units/s at the top of the push, 1.56 units risen), then gravity 35 units/s^2: apex 3.8 units. At our scale (0.66 m a
    /// unit) that is 10.9 m/s, 23.1 m/s^2 and a 2.5 m apex (takeoff 10.7 m/s).
    /// The client never says what its own speeds are, so they are MEASURED from the avatar as it moves: its steady top speed on the
    /// ground while the stick / WASD is held fully (per mode: VR locomotion, monitor walk, monitor sprint), and per jump the fall's
    /// acceleration and the apex. Each measurement corrects the multiplier (speed and gravity in proportion, the jump by the takeoff
    /// speed it implies); the client's base values found are kept in options.txt, so the next visit starts right. Everything goes
    /// back to 1 when the option is switched off or the world stops.
    /// </summary>
    public static class MpMovement
    {
        public const float UnitM = MpSpace.PlayerEyeHeight / MpSpace.SamusEyeHeight;
        public static readonly float TargetSpeed = 16.5f * UnitM;                  // m/s
        public static readonly float TargetGravity = 35f * UnitM;                  // m/s^2
        public static readonly float TargetApex = (1.5625f + 12.5f * 12.5f / 70f) * UnitM;   // m
        public static float TargetTakeoff { get { return Mathf.Sqrt(2f * TargetGravity * TargetApex); } }

        private static float _walk = 1f, _loco = 1f, _sprint = 1f, _jumpDesk = 1f, _jumpXr = 1f, _gravDesk = 1f, _gravXr = 1f;
        private static bool _applied, _wantOn;
        private static float _changedAt = -10f, _nextApply;
        private static Vector3 _prev;
        private static bool _havePrev;
        // steady speed window
        private static float _winT, _winMin, _winMax, _winSum;
        private static int _winN;
        // the jump being watched
        private static bool _air;
        private static float _airStart, _airY0, _airTop, _fallT0 = -1f, _fallV0, _fallT1, _fallV1;
        private static int _logs;

        /// <summary>Every frame of play.</summary>
        public static void Tick(bool vr, bool fullStick, bool sprintHeld, bool held)
        {
            // the morph ball in third person: the hidden avatar must not walk, jump or fall while the world slides under it
            if (Frozen)
            {
                if (!_frozenSent || Time.unscaledTime >= _nextApply) Freeze();
                _havePrev = false; ResetWindow(); _air = false;
                return;
            }

            if (_frozenSent)
            {
                _frozenSent = false;
                _sentWalk = -1f;
                _applied = false;
                if (!MpSettings.SamusMovement) Restore();
            }

            _wantOn = MpSettings.SamusMovement;
            if (!_wantOn) { if (_applied) Restore(); _havePrev = false; return; }
            ComputeFromBases();
            if (!_applied || Time.unscaledTime >= _nextApply) Apply();
            Transform root = MpPlayer.Root;
            float dt = Time.unscaledDeltaTime;
            if (root == null || dt <= 0f || held) { _havePrev = false; ResetWindow(); _air = false; return; }
            Vector3 p = root.position;
            if (!_havePrev) { _prev = p; _havePrev = true; return; }
            Vector3 v = (p - _prev) / dt;
            _prev = p;
            if (v.magnitude > 40f || dt > 0.1f) { ResetWindow(); _air = false; return; }   // a teleport, a hitch
            if (Time.unscaledTime - _changedAt < 0.6f) { ResetWindow(); return; }        // the multipliers just changed
            bool grounded = Grounded(p);
            Speed(vr, grounded, fullStick, sprintHeld, new Vector2(v.x, v.z).magnitude, dt);
            Jump(vr, grounded, p.y, v.y);
        }

        private static bool Grounded(Vector3 feet)
        {
            return Physics.Raycast(feet + Vector3.up * 0.15f, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void ResetWindow() { _winT = 0f; _winN = 0; _winSum = 0f; _winMin = float.MaxValue; _winMax = 0f; }

        /// <summary>A steady top speed (0.4 s within 8 %) while the stick / keys are fully held on the ground: the client's base = it / the multiplier.</summary>
        private static void Speed(bool vr, bool grounded, bool fullStick, bool sprint, float h, float dt)
        {
            if (!grounded || !fullStick || h < 0.3f) { ResetWindow(); return; }
            _winT += dt; _winN++; _winSum += h;
            _winMin = Mathf.Min(_winMin, h); _winMax = Mathf.Max(_winMax, h);
            if (_winT < 0.4f) return;
            float mean = _winSum / _winN;
            bool steady = (_winMax - _winMin) <= 0.08f * mean;
            ResetWindow();
            if (!steady) return;
            float mult = vr ? _loco : sprint ? _sprint : _walk;
            float b = mean / Mathf.Max(0.01f, mult);
            float baseNow = vr ? MpSettings.BaseLocoSpeed : sprint ? MpSettings.BaseSprintSpeed : MpSettings.BaseWalkSpeed;
            if (baseNow > 0f && Mathf.Abs(b - baseNow) < 0.03f * baseNow) return;
            float was = baseNow;
            baseNow = Blend(baseNow, b);
            if (vr) MpSettings.BaseLocoSpeed = baseNow;
            else if (sprint) MpSettings.BaseSprintSpeed = baseNow;
            else MpSettings.BaseWalkSpeed = baseNow;
            Log("the client's " + (vr ? "VR locomotion" : sprint ? "sprint" : "walk") + " speed measured " + b.ToString("0.00") + " m/s at x" + mult.ToString("0.00") + " (base " + (was > 0f ? was.ToString("0.00") : "unknown") + " -> " + baseNow.ToString("0.00") + ")");
            MpSettings.Save();
            ComputeFromBases();
            Apply();
        }

        /// <summary>Per jump: gravity from the free fall (the first and last falling samples), the takeoff speed from the apex under it.</summary>
        private static void Jump(bool vr, bool grounded, float y, float vy)
        {
            float t = Time.unscaledTime;
            if (!_air)
            {
                if (!grounded && vy > 1f) { _air = true; _airStart = t; _airY0 = y - vy * Time.unscaledDeltaTime; _airTop = y; _fallT0 = -1f; }
                return;
            }

            _airTop = Mathf.Max(_airTop, y);
            if (vy < -0.3f)
            {
                if (_fallT0 < 0f) { _fallT0 = t; _fallV0 = vy; }
                _fallT1 = t; _fallV1 = vy;
            }

            if (!grounded && t - _airStart < 4f) return;
            _air = false;
            if (!grounded || _fallT0 < 0f || _fallT1 - _fallT0 < 0.15f || Mathf.Abs(y - _airY0) > 0.3f) return;   // landed elsewhere: not a clean jump
            float g = (_fallV0 - _fallV1) / (_fallT1 - _fallT0), apex = _airTop - _airY0;
            if (g > 1f && apex > 0.1f) Measured(vr, g, apex);
        }

        private static void Measured(bool vr, float g, float apex)
        {
            float gm = vr ? _gravXr : _gravDesk, jm = vr ? _jumpXr : _jumpDesk;
            float gBase = g / Mathf.Max(0.01f, gm), takeoff = Mathf.Sqrt(2f * g * apex), jBase = takeoff / Mathf.Max(0.01f, jm);
            if (vr) { MpSettings.BaseGravityXr = Blend(MpSettings.BaseGravityXr, gBase); MpSettings.BaseJumpXr = Blend(MpSettings.BaseJumpXr, jBase); }
            else { MpSettings.BaseGravityDesk = Blend(MpSettings.BaseGravityDesk, gBase); MpSettings.BaseJumpDesk = Blend(MpSettings.BaseJumpDesk, jBase); }
            Log("a jump measured (" + (vr ? "VR" : "monitor") + "): apex " + apex.ToString("0.00") + " m, fall " + g.ToString("0.0") + " m/s^2 at gravity x" + gm.ToString("0.00") + ", jump x" + jm.ToString("0.00") + " - the client's own: gravity " + gBase.ToString("0.0") + " m/s^2, takeoff " + jBase.ToString("0.00") + " m/s (Samus: apex " + TargetApex.ToString("0.00") + " m, " + TargetGravity.ToString("0.0") + " m/s^2)");
            MpSettings.Save();
            ComputeFromBases();
            Apply();
        }

        private static float Blend(float was, float now) { return was > 0f ? Mathf.Lerp(was, now, 0.6f) : now; }

        private static float Mult(float target, float measuredBase) { return measuredBase > 0f ? Mathf.Clamp(target / measuredBase, 0.05f, 10f) : 1f; }

        private static void ComputeFromBases()
        {
            _walk = Mult(TargetSpeed, MpSettings.BaseWalkSpeed);
            _loco = Mult(TargetSpeed, MpSettings.BaseLocoSpeed);
            // sprint: Samus has one speed; until the sprint is measured it gets the walk's multiplier
            _sprint = MpSettings.BaseSprintSpeed > 0f ? Mult(TargetSpeed, MpSettings.BaseSprintSpeed) : _walk;
            _gravDesk = Mult(TargetGravity, MpSettings.BaseGravityDesk);
            _gravXr = Mult(TargetGravity, MpSettings.BaseGravityXr);
            _jumpDesk = Mult(TargetTakeoff, MpSettings.BaseJumpDesk);
            _jumpXr = Mult(TargetTakeoff, MpSettings.BaseJumpXr);
        }

        private static float _sentWalk = -1f, _sentLoco, _sentSprint, _sentJd, _sentJx, _sentGd, _sentGx;

        private static void Apply()
        {
            _nextApply = Time.unscaledTime + 2f;    // again now and then: a teleport / avatar change may reset the client's values
            var m = MpPlayer.Motion;
            if (m == null) return;
            try
            {
                bool changed = _walk != _sentWalk || _loco != _sentLoco || _sprint != _sentSprint || _jumpDesk != _sentJd || _jumpXr != _sentJx || _gravDesk != _sentGd || _gravXr != _sentGx;
                m.SetMovementSpeed(_walk, _loco, _sprint);
                m.SetJump(_jumpDesk, _jumpXr);
                m.SetGravity(_gravDesk, _gravXr);
                _applied = true;
                if (changed)
                {
                    _changedAt = Time.unscaledTime;
                    _sentWalk = _walk; _sentLoco = _loco; _sentSprint = _sprint; _sentJd = _jumpDesk; _sentJx = _jumpXr; _sentGd = _gravDesk; _sentGx = _gravXr;
                    Log("Samus movement: speed x" + _walk.ToString("0.00") + " (VR x" + _loco.ToString("0.00") + ", sprint x" + _sprint.ToString("0.00") + "), jump x" + _jumpDesk.ToString("0.00") + " (VR x" + _jumpXr.ToString("0.00") + "), gravity x" + _gravDesk.ToString("0.00") + " (VR x" + _gravXr.ToString("0.00") + ")");
                }
            }
            catch (Exception e) { if (_logs++ < 3) MpLog.Warn("Movement: the client's speeds could not be set: " + e.Message); }
        }

        /// <summary>Set by the game each frame: the morph ball rolls in third person (the avatar is held still, no gravity).</summary>
        public static bool Frozen;
        private static bool _frozenSent;

        private static void Freeze()
        {
            _nextApply = Time.unscaledTime + 2f;
            var m = MpPlayer.Motion;
            if (m == null) return;
            try
            {
                m.SetMovementSpeed(0f, 0f, 0f);
                m.SetJump(0f, 0f);
                m.SetGravity(0f, 0f);
                if (!_frozenSent) Log("the avatar held still while the morph ball rolls (speed, jump and gravity x0)");
                _frozenSent = true;
            }
            catch (Exception e) { if (_logs++ < 3) MpLog.Warn("Movement: the avatar could not be held still: " + e.Message); }
        }

        /// <summary>The client's own speeds back (the option off, the world stopping).</summary>
        public static void Restore()
        {
            _applied = false;
            Frozen = false;
            _frozenSent = false;
            _sentWalk = -1f;
            var m = MpPlayer.Motion;
            if (m == null) return;
            try
            {
                m.SetMovementSpeed(1f, 1f, 1f);
                m.SetJump(1f, 1f);
                m.SetGravity(1f, 1f);
                Log("Samus movement off: the client's own speed, jump and gravity");
            }
            catch (Exception e) { if (_logs++ < 3) MpLog.Warn("Movement: the client's speeds could not be restored: " + e.Message); }
        }

        private static int _infoLogs;
        private static void Log(string s) { if (_infoLogs++ < 60) MpLog.Info("Movement: " + s); }
    }
}
