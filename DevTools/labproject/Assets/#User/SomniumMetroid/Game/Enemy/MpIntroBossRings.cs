using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The Reactor Core's shield rings around the Parasite Queen (decomp CScriptSpecialFunction, function 8 IntroBossRingController:
    /// AcceptScriptMsg and ThinkIntroBossRingController). On the area's load (InitializedInArea) it takes the actors it sends
    /// Activate on its Play state (the platforms Platform_0..4), lowest first, all turning at its first float (16 degrees a second,
    /// the direction flipping with each scramble). Action scrambles them (Zero; every other ring the other way at a random 350..600
    /// degrees a second); SetToZero lines them up one after another, lowest first, facing away from Samus (their holes toward her)
    /// while the common direction itself keeps turning, then sends MaxReached and they turn on together; Play (the queen's death)
    /// breaks them up: each flies off along its own facing at 50 units a second. The rings turn about their own Z in game space
    /// (their models and DCLN collision move with them: shots and the queen's beam are stopped by the rings).
    /// </summary>
    public sealed class MpIntroBossRings : MpEntity
    {
        private enum RingState { Scramble, Rotate, Stopped, Breakup }

        private sealed class Ring
        {
            public MpActorEntity Actor;
            public float Speed;
            public bool Reached;
            public int Order;
        }

        public Vector3 GamePos;
        public float Float1, Float2, Float3, Float4;
        private readonly List<uint> _targets = new List<uint>();
        private readonly List<Ring> _rings = new List<Ring>();
        private RingState _state = RingState.Stopped;
        private Vector3 _rotateTarget;
        private bool _reverse = true, _init;
        private float _breakT;
        public int Scrambles, Aligns;

        /// <summary>ScriptLoader::LoadSpecialFunction: head, function, string, 3 floats, 3 ints, active, float, 3 sound ids.</summary>
        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            if (o.PropCount != 15) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, false);
            r.S32();               // function (8)
            r.CString();
            var e = new MpIntroBossRings { GamePos = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z) };
            e.Float1 = r.F32();
            e.Float2 = r.F32();
            e.Float3 = r.F32();
            r.S32();
            r.S32();
            r.S32();
            e.Active = r.Bool();
            e.Float4 = r.F32();
            foreach (MpScriptObject.Link l in o.Links)
                if (l.State == MpScriptMsg.StPlay && l.Message == MpScriptMsg.Activate) e._targets.Add(l.Target);
            return e;
        }

        private float Dir { get { return _reverse ? 1f : -1f; } }

        public override void OnLoaded() { Init(); }

        /// <summary>InitializedInArea: the rings (actors among the Play -> Activate targets), sorted by height, turning at the first float.</summary>
        private void Init()
        {
            if (World == null) return;
            _init = true;
            _rings.Clear();
            foreach (uint id in _targets)
            {
                List<MpEntity> list;
                if (!World.ById.TryGetValue(id, out list)) continue;
                foreach (MpEntity en in list)
                {
                    var a = en as MpActorEntity;
                    if (a != null && a.Go != null) _rings.Add(new Ring { Actor = a, Order = _rings.Count });
                }
            }

            _rings.Sort((x, y) =>
            {
                int c = x.Actor.Go.transform.localPosition.z.CompareTo(y.Actor.Go.transform.localPosition.z);
                return c != 0 ? c : x.Order.CompareTo(y.Order);
            });
            foreach (Ring g in _rings) { g.Speed = Float1 * Dir; g.Reached = false; }
            MpLog.Info("Ring controller " + this + ": " + _rings.Count + " rings of " + _targets.Count + " targets, " + Float1 + " deg/s, scramble " + Float2 + ".." + Float3);
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if (!Active || _state == RingState.Breakup) return;
            if (!_init) Init();
            switch (msg)
            {
                case MpScriptMsg.Play:
                    if (_state != RingState.Scramble) Scramble();
                    _state = RingState.Breakup;
                    _breakT = 0f;
                    MpLog.Info("Ring controller: breakup (from " + from + ")");
                    break;
                case MpScriptMsg.Action:
                    Scramble();
                    break;
                case MpScriptMsg.SetToZero:
                {
                    _state = RingState.Rotate;
                    Vector3 p = MpGame.Instance != null ? MpGame.Instance.PlayerGame : (World != null ? World.PlayerFeet : Vector3.zero);
                    _rotateTarget = GamePos - p;
                    _rotateTarget.z = 0f;
                    _rotateTarget = _rotateTarget.sqrMagnitude > 1e-6f ? _rotateTarget.normalized : Vector3.up;
                    Aligns++;
                    MpLog.Info("Ring controller: line up, away from Samus " + _rotateTarget.ToString("0.00"));
                    break;
                }
            }
        }

        /// <summary>CScriptSpecialFunction::RingScramble.</summary>
        private void Scramble()
        {
            Send(MpScriptMsg.StZero);
            _state = RingState.Scramble;
            _reverse = !_reverse;
            float dir = Dir;
            foreach (Ring g in _rings)
            {
                g.Speed = dir * Random.Range(Float2, Float3);
                dir = -dir;
                g.Reached = false;
            }

            Scrambles++;
            MpLog.Info("Ring controller: scramble " + Scrambles);
        }

        private static Vector3 Forward(Transform t) { return t.localRotation * Vector3.up; }

        public override void Think(float dt)
        {
            if (!Active) return;
            if (!_init) Init();
            if (_state != RingState.Breakup)
                foreach (Ring g in _rings)
                    if (g.Actor.Go != null)
                    {
                        Transform t = g.Actor.Go.transform;
                        t.localRotation = t.localRotation * Quaternion.AngleAxis(dt * g.Speed, Vector3.forward);      // RotateLocalZ
                    }

            switch (_state)
            {
                case RingState.Breakup:
                    // each ring flies off along its own facing (the game never stops them: they are hidden once far away)
                    _breakT += dt;
                    foreach (Ring g in _rings)
                    {
                        if (g.Actor.Go == null) continue;
                        Transform t = g.Actor.Go.transform;
                        t.localPosition += Forward(t) * 50f * dt;
                        if (_breakT > 10f && g.Actor.Active) g.Actor.SetActive(false);
                    }

                    break;
                case RingState.Rotate:
                {
                    _rotateTarget = Quaternion.AngleAxis(Float1 * Dir * dt, Vector3.forward) * _rotateTarget;
                    bool all = true;
                    foreach (Ring g in _rings)
                    {
                        if (g.Reached || g.Actor.Go == null) continue;
                        Transform t = g.Actor.Go.transform;
                        Vector3 f = Forward(t);
                        f.z = 0f;
                        f = f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.up;
                        float cos = Mathf.Clamp(Vector3.Dot(f, _rotateTarget), -1f, 1f);
                        float within = (Float1 + Mathf.Abs(g.Speed)) / 30f;
                        if (Mathf.Acos(cos) <= within * Mathf.Deg2Rad)
                        {
                            // LookAt the target with the ring's place kept: a pure turn about Z
                            t.localRotation = Quaternion.AngleAxis(Mathf.Atan2(-_rotateTarget.x, _rotateTarget.y) * Mathf.Rad2Deg, Vector3.forward);
                            g.Speed = Dir * Float1;
                            g.Reached = true;
                        }
                        else
                        {
                            all = false;
                            break;
                        }
                    }

                    if (all)
                    {
                        Send(MpScriptMsg.StMaxReached);
                        _state = RingState.Stopped;
                        foreach (Ring g in _rings) g.Reached = false;
                        MpLog.Info("Ring controller: lined up (MaxReached)");
                    }

                    break;
                }
            }
        }

        /// <summary>For the lab: the state and each ring's facing (degrees about Z) and speed.</summary>
        public string Report()
        {
            string s = "rings " + _state + " reverse " + _reverse;
            foreach (Ring g in _rings)
            {
                Transform t = g.Actor.Go != null ? g.Actor.Go.transform : null;
                if (t == null) continue;
                Vector3 f = Forward(t);
                s += "; " + g.Actor.Name + " z " + t.localPosition.z.ToString("0.0") + " yaw " + (Mathf.Atan2(-f.x, f.y) * Mathf.Rad2Deg).ToString("0") + " speed " + g.Speed.ToString("0") + (g.Actor.Active ? "" : " (inactive)");
            }

            return s;
        }
    }
}
