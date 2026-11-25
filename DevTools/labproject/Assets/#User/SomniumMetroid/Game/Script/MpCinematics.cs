using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CCinematicCamera: a scripted camera shot. Activate starts it (CalculateWaypoints: the camera path from its CameraPath link
    /// and the target path from its CameraTarget link, each following Arrived -> Next); the shot runs its duration, moving along a
    /// Catmull-Rom spline through the path points (orientation slerped), looking at the target path (or at Samus with flag 1),
    /// sending Arrived to each waypoint passed; at the end the remaining waypoints get Arrived and the camera deactivates itself
    /// with Inactive (which is what the frigate's scripts wait for). Deactivate ends it quietly.
    /// </summary>
    public sealed class MpCinematicCamera : MpEntity
    {
        public Vector3 GamePos;
        public Quaternion GameRot = Quaternion.identity;
        public float Duration, Fov = 55f, T;
        public uint Flags;
        public bool Running;
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<Quaternion> _rots = new List<Quaternion>();
        private readonly List<float> _fovs = new List<float>();
        private readonly List<MpEntity> _pointArrivals = new List<MpEntity>();
        private readonly List<Vector3> _targets = new List<Vector3>();
        private readonly List<MpEntity> _targetArrivals = new List<MpEntity>();
        private int _passedPoint, _passedTarget;

        // the shot now (game space)
        public Vector3 ViewPos;
        public Vector3 ViewForward = Vector3.up, ViewUp = Vector3.forward;
        public float ViewFov = 55f;

        public bool DisablesInput { get { return (Flags & 0x20) != 0; } }

        public static MpEntity Make(MpScriptObject o)
        {
            if (o.PropCount < 14 || o.PropCount > 15) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, false);
            var c = new MpCinematicCamera { GamePos = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z), GameRot = MpSpace.GameRotation(new Vector3(h.Rot.X, h.Rot.Y, h.Rot.Z)) };
            c.Active = r.Bool();
            c.Duration = r.F32();
            bool lookAtPlayer = r.Bool(), outOf = r.Bool(), into = r.Bool(), drawPlayer = r.Bool(), disableInput = r.Bool(), b7 = r.Bool(), finishSkip = r.Bool();
            c.Fov = r.F32();
            bool failsafe = r.Bool();
            bool disOutInto = o.PropCount > 14 && r.Bool();
            c.Flags = (lookAtPlayer ? 1u : 0u) | (outOf ? 2u : 0u) | (into ? 4u : 0u) | (b7 ? 8u : 0u) | (finishSkip ? 0x10u : 0u) | (disableInput ? 0x20u : 0u) | (drawPlayer ? 0x40u : 0u) | (failsafe ? 0x80u : 0u) | (disOutInto ? 0x200u : 0u);
            c.ViewPos = c.GamePos;
            return c;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            bool wasActive = Active;
            base.OnMessage(msg, from);
            if (msg == MpScriptMsg.Activate && (Active || !wasActive)) Begin();
            else if (msg == MpScriptMsg.Deactivate) End(false);
        }

        private void Begin()
        {
            CalculateWaypoints();
            T = 0f;
            Running = true;
            _passedPoint = 0;
            _passedTarget = 0;
            Pose();
            MpCinematics.Add(this);
            if (_pointArrivals.Count > 0) Arrive(_pointArrivals[0]);
            if (_targetArrivals.Count > 0) Arrive(_targetArrivals[0]);
            if (MpScripts.Trace) MpLog.Info("Cinema: '" + Name + "' " + Duration.ToString("0.0") + " s, " + _points.Count + " path points, " + _targets.Count + " targets");
        }

        private void Arrive(MpEntity e)
        {
            if (e != null) e.OnMessage(MpScriptMsg.Arrived, this);
        }

        private void CalculateWaypoints()
        {
            _points.Clear(); _rots.Clear(); _fovs.Clear(); _pointArrivals.Clear(); _targets.Clear(); _targetArrivals.Clear();
            MpWaypointEntity firstVp = null, firstTarget = null;
            if (Links != null)
                foreach (MpScriptObject.Link l in Links)
                {
                    if (l.Message != MpScriptMsg.Activate) continue;
                    if (l.State == 15) firstVp = World.Find(l.Target) as MpWaypointEntity;          // CameraPath
                    else if (l.State == 16) firstTarget = World.Find(l.Target) as MpWaypointEntity; // CameraTarget
                }

            for (MpWaypointEntity w = firstVp; w != null && !_pointArrivals.Contains(w); w = w.Next())
            {
                _points.Add(w.GamePos);
                _rots.Add(w.Rot);
                if (w.IsCamera) _fovs.Add(w.HFov);
                _pointArrivals.Add(w);
            }

            for (MpWaypointEntity w = firstTarget; w != null && !_targetArrivals.Contains(w); w = w.Next())
            {
                _targets.Add(w.GamePos);
                _targetArrivals.Add(w);
            }
        }

        public override void Think(float dt)
        {
            if (!Running) return;
            Pose();
            T += dt;
            if (T > Duration)
            {
                for (int i = _passedPoint + 1; i < _pointArrivals.Count; i++) Arrive(_pointArrivals[i]);
                for (int i = _passedTarget + 1; i < _targetArrivals.Count; i++) Arrive(_targetArrivals[i]);
                Active = false;
                End(true);
            }
        }

        private void End(bool sendInactive)
        {
            if (!Running) return;
            Running = false;
            MpCinematics.Remove(this);
            if (sendInactive) Send(MpScriptMsg.StInactive);
        }

        private void Pose()
        {
            Vector3 view = GamePos;
            int idx;
            if (_points.Count > 0)
            {
                view = Spline(_points, T, out idx);
                if (idx > _passedPoint && idx < _pointArrivals.Count) { _passedPoint = idx; Arrive(_pointArrivals[idx]); }
            }

            Quaternion orient = Orientation();
            Vector3 up = orient * Vector3.forward;       // game up (Z) turned
            Vector3 fwd;
            if ((Flags & 1) != 0) fwd = MpEnemy.PlayerEye - view;
            else if (_targets.Count > 0)
            {
                Vector3 target = Spline(_targets, T, out idx);
                if (idx > _passedTarget && idx < _targetArrivals.Count) { _passedTarget = idx; Arrive(_targetArrivals[idx]); }
                fwd = target - view;
            }
            else fwd = orient * Vector3.up;              // game forward (Y) turned

            if (fwd.sqrMagnitude < 1e-6f) fwd = orient * Vector3.up;
            ViewPos = view;
            ViewForward = fwd.normalized;
            ViewUp = up;
            ViewFov = HFov();
        }

        private float Cycle(int count, out int idx, out float t)
        {
            float cycle = Duration > 0f ? Mathf.Repeat(T, Duration) : 0f;
            if (T >= Duration) cycle = Duration * 0.9999f;
            float per = count > 1 ? Duration / (count - 1) : Duration;
            idx = per > 0f ? Mathf.Clamp((int)(cycle / per), 0, Mathf.Max(0, count - 2)) : 0;
            t = per > 0f ? Mathf.Clamp01((cycle - idx * per) / per) : 0f;
            return cycle;
        }

        /// <summary>CCinematicCamera::GetInterpolatedSplinePoint (Catmull-Rom through the points, ends mirrored).</summary>
        private Vector3 Spline(List<Vector3> pts, float time, out int idxOut)
        {
            int n = pts.Count;
            int idx;
            float t;
            Cycle(n, out idx, out t);
            idxOut = idx;
            if (n == 1) return pts[0];
            if (n == 2) return pts[0] + (pts[1] - pts[0]) * t;
            Vector3 a = idx > 0 ? pts[idx - 1] : pts[0] - (pts[1] - pts[0]);
            Vector3 b = pts[idx];
            Vector3 c = idx + 1 < n ? pts[idx + 1] : pts[n - 1] - (pts[n - 2] - pts[n - 1]);
            Vector3 d = idx + 2 < n ? pts[idx + 2] : pts[n - 1] - (pts[n - 2] - pts[n - 1]);
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
        }

        private Quaternion Orientation()
        {
            if (_rots.Count == 0) return GameRot;
            if (_rots.Count == 1) return _rots[0];
            int idx;
            float t;
            Cycle(_rots.Count, out idx, out t);
            return Quaternion.Slerp(_rots[idx], _rots[Mathf.Min(idx + 1, _rots.Count - 1)], t);
        }

        private float HFov()
        {
            if (_fovs.Count == 0) return Fov;
            if (_fovs.Count == 1) return _fovs[0];
            int idx;
            float t;
            Cycle(_fovs.Count, out idx, out t);
            return Mathf.Lerp(_fovs[idx], _fovs[Mathf.Min(idx + 1, _fovs.Count - 1)], t);
        }

        public override void OnRemoved() { End(false); }
    }

    /// <summary>
    /// The cinematics, shown the VR-friendly way: the game's own cutscene camera renders to a "cinema screen" that appears in front
    /// of the player while a shot runs (the player's own view is never moved or turned) - the same on a monitor. Samus's cinematic
    /// body (PlayerActor) is drawn only on that screen. The game's scripts run on the shots' real timing either way; Skip ends the
    /// running cutscene as the game's skip does (CScriptSpecialFunction CinematicSkip sends Zero).
    /// </summary>
    public static class MpCinematics
    {
        private static readonly List<MpCinematicCamera> Running = new List<MpCinematicCamera>();
        public static readonly List<MpPlayerActorEntity> CinemaActors = new List<MpPlayerActorEntity>();
        public static MpSpecialFunctionEntity SkipObject;
        /// <summary>The morph ball: its trailing camera is shown on the screen while Samus is a ball (and no cutscene runs).</summary>
        public static MpMorphBall Ball;

        /// <summary>The screen is put in front of the player again on the next frame it shows (a new morph).</summary>
        public static void PlaceAgain() { _placed = false; }
        private static Camera _cam;
        private static RenderTexture _rt;
        private static Transform _screen;
        private static Material _screenMat;
        private static float _fade;
        private static bool _placed;
        public static bool ShowScreen = true;
        public static int Shots;

        public static MpCinematicCamera Current { get { return Running.Count > 0 ? Running[Running.Count - 1] : null; } }
        public static bool Active { get { return Running.Count > 0; } }
        public static bool InputDisabled { get { MpCinematicCamera c = Current; return c != null && c.DisablesInput; } }

        public static void Add(MpCinematicCamera c)
        {
            Running.Remove(c);
            Running.Add(c);
            Shots++;
            if (Running.Count == 1 && !(Ball != null && Ball.Morphed)) _placed = false;
        }

        public static void Remove(MpCinematicCamera c) { Running.Remove(c); }

        /// <summary>The player's skip (key / tablet): the cutscene's skip function sends Zero, every running shot stops.</summary>
        /// <summary>Co-op, the master: the cutscene was skipped (MpCoop skips it for the helpers too).</summary>
        public static System.Action Skipped;

        public static bool Skip()
        {
            if (!Active) return false;
            if (Skipped != null && !MpScripts.Follower) Skipped();
            MpSpecialFunctionEntity s = SkipObject;
            foreach (MpCinematicCamera c in Running.ToArray()) c.OnMessage(MpScriptMsg.Deactivate, null);
            Running.Clear();
            if (s != null) { s.Send(MpScriptMsg.StZero); SkipObject = null; }
            foreach (MpPlayerActorEntity a in CinemaActors) a.SetCinemaVisible(false);      // nothing of the cutscene stays in view
            MpCinemaAvatar.HideAll();
            MpLog.Info("Cinema: skipped");
            return true;
        }

        /// <summary>The screen appears 2.2 m in front of the player's eyes, level, facing them.</summary>
        private static void Place()
        {
            Camera head = MpPlayer.Head();
            if (head == null) return;
            Build();
            if (_screen == null) return;
            _placed = true;
            Vector3 f = head.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            float dist = MpXr.Present ? 2.2f : 1.3f;
            _screen.position = head.transform.position + f * dist;
            _screen.rotation = Quaternion.LookRotation(f, Vector3.up);
            float w = MpXr.Present ? 2.4f : 1.6f;
            _screen.localScale = new Vector3(w, w * 9f / 16f, 1f);
            _fade = 0f;
        }

        private static void Build()
        {
            if (_cam != null) return;
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return;
            _rt = new RenderTexture(1280, 720, 24) { name = "Metroid cinema" };
            var go = new GameObject("Metroid cinema camera");
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 2500f * MpSpace.Scale;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = ~(1 << ScreenLayer);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "Metroid cinema screen";
            q.layer = ScreenLayer;
            _screen = q.transform;
            _screenMat = new Material(carrier) { name = "Metroid cinema screen" };
            _screenMat.SetTexture("_MainTex", _rt);
            // opaque: the render texture's alpha is not the picture's (cleared sky leaves it 0)
            _screenMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _screenMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            _screenMat.SetFloat("_ZWrite", 1f);
            _screenMat.renderQueue = 2502;      // past 2500: URP depth priming would hide an effects-shader draw in the opaque range
            q.GetComponent<MeshRenderer>().sharedMaterial = _screenMat;
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.SetActive(false);
        }

        public const int ScreenLayer = 31;

        /// <summary>Late in the frame, before the player's camera draws: the shot renders to the screen (call before MpEffects.Render(main)).</summary>
        public static void Render(Camera main, MpWorldView world)
        {
            MpCinematicCamera c = Current;
            bool ball = c == null && Ball != null && Ball.Morphed && !Ball.ThirdPerson;
            // first-person cutscenes (MpCinemaView): no screen; Samus (or the avatar playing her) stands in the world
            if (MpCinemaView.Active) foreach (MpPlayerActorEntity a in CinemaActors) a.SetCinemaVisible(true);
            bool on = ((c != null && !MpCinemaView.Active) || ball) && ShowScreen;
            if (_screen != null)
            {
                // the screen opens and closes by its height (an opaque picture cannot fade)
                _fade = Mathf.MoveTowards(_fade, on ? 1f : 0f, Time.deltaTime * 5f);
                bool vis = _fade > 0.001f;
                if (_screen.gameObject.activeSelf != vis) _screen.gameObject.SetActive(vis);
                float w = MpXr.Present ? 2.4f : 1.6f;
                if (vis) _screen.localScale = new Vector3(w, w * 9f / 16f * Mathf.SmoothStep(0f, 1f, _fade), 1f);
            }

            if (!on) return;
            Build();
            if (_cam == null) return;
            // placed once a head camera exists; again when the player has turned well away from it
            if (!_placed || (main != null && Vector3.Angle(main.transform.forward, _screen.position - main.transform.position) > 75f)) Place();
            Vector3 fwd = MpSpace.DirToUnity(ball ? Ball.ViewForward : c.ViewForward), up = MpSpace.DirToUnity(ball ? Ball.ViewUp : c.ViewUp);
            if (Vector3.Cross(fwd, up).sqrMagnitude < 1e-6f) up = Vector3.up;
            _cam.transform.SetPositionAndRotation(MpSpace.ToUnity(ball ? Ball.ViewPos : c.ViewPos), Quaternion.LookRotation(fwd, up));
            _cam.fieldOfView = ball ? 60f : Mathf.Clamp(c.ViewFov / 1.42f, 10f, 100f);
            if (!ball) foreach (MpPlayerActorEntity a in CinemaActors) a.SetCinemaVisible(true);
            if (world != null) world.TickSky(_cam);
            MpShaderGlobals.Apply(_cam);
            MpEffects.Render(_cam);
            try { _cam.Render(); }
            catch (System.Exception e) { MpLog.Warn("Cinema: the shot did not render: " + e.Message); ShowScreen = false; }
            foreach (MpPlayerActorEntity a in CinemaActors) a.SetCinemaVisible(false);
            if (world != null) world.TickSky(main);
            MpShaderGlobals.Apply(main);
        }

        public static void Clear()
        {
            Running.Clear();
            CinemaActors.Clear();
            SkipObject = null;
            MpCinemaAvatar.Clear();
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_screen != null) Object.Destroy(_screen.gameObject);
            if (_rt != null) _rt.Release();
            _cam = null; _screen = null; _rt = null;
        }
    }

    /// <summary>
    /// CScriptPlayerActor: Samus's own body for cutscenes (the suit model, animated). It is drawn only on the cinema screen - the
    /// player's own avatar is Samus in the world.
    /// </summary>
    public sealed class MpPlayerActorEntity : MpAnimActorEntity
    {
        private bool _cinemaVisible;

        public static MpModelRef ReadModel(MpScriptObject o)
        {
            if (o.PropCount != 19) return default(MpModelRef);
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.ReadHead(r, true);
            MpVec3.Read(r); MpVec3.Read(r);
            r.F32(); r.F32();
            r.U32(); r.F32(); r.F32();
            MpScriptRead.ReadVulnerability(r);
            r.U32();
            return MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            if (o.PropCount != 19) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, true);
            MpVec3.Read(r); MpVec3.Read(r);
            r.F32(); r.F32();
            r.U32(); r.F32(); r.F32();
            MpScriptRead.ReadVulnerability(r);
            r.U32();
            MpModelRef m = MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
            MpScriptRead.ReadActorParams(r);
            bool loop = r.Bool();
            r.Bool(); r.Bool();
            bool active = r.Bool();
            MpCharacter ch = MpAssets.Valid(m.Ancs) ? MpCharacter.Get(m.Ancs, m.Character) : null;
            if (ch == null || ch.Model == null) return null;
            ch.Upload(true);
            var e = new MpPlayerActorEntity { LoopDefault = loop };
            var body = new GameObject("PlayerActor " + o.Name);
            body.transform.SetParent(view.Root.transform, false);
            body.transform.localPosition = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z);
            body.transform.localRotation = MpSpace.GameRotation(new Vector3(h.Rot.X, h.Rot.Y, h.Rot.Z));
            body.transform.localScale = new Vector3(h.Scale.X, h.Scale.Y, h.Scale.Z);
            e.Anim = new MpAnimator(ch, "PlayerActor model", body.transform);
            if (m.Animation >= 0) e.Anim.Play(m.Animation, loop, 0f);
            e.Anim.Tick(0f);
            e.Go = body;
            e.SetActive(active);
            e.SetCinemaVisible(false);
            MpCinematics.CinemaActors.Add(e);
            return e;
        }

        public void SetCinemaVisible(bool on)
        {
            _cinemaVisible = on;
            // the instance master's avatar plays Samus when it can (MpCinemaAvatar); else her own suit
            bool avatar = MpCinemaAvatar.Replaces;
            if (Anim != null) Anim.SetVisible(on && Active && !avatar);
            MpCinemaAvatar.Show(this, on && Active && avatar);
        }

        public override void OnRemoved()
        {
            MpCinematics.CinemaActors.Remove(this);
            MpCinemaAvatar.Remove(this);
            base.OnRemoved();
        }
    }

    /// <summary>
    /// CScriptSpecialFunction, the functions the frigate uses: ScriptLayerController (Increment / Decrement switch a room's script
    /// layer on / off for its next load: CWorldLayerState), CinematicSkip (the running cutscene's skip object), SaveStation (Action:
    /// full energy, the game saved, then Zero), MapStation (Action: the map downloaded), EscapeSequence (Action: the countdown),
    /// BossEnergyBar (Increment: the boss bar for the sender, Decrement: off), EndGame (Action: the end), HUDFadeIn,
    /// IntroBossRingController (the Parasite Queen's reactor rings, MpIntroBossRings), and quietly the visual-only ones.
    /// </summary>
    public sealed class MpSpecialFunctionEntity : MpEntity
    {
        public const int What = 0, ObjectFollowLocator = 3, MapStation = 6, SaveStation = 7, IntroBossRingController = 8, EscapeSequence = 11,
            BossEnergyBar = 12, EndGame = 13, HUDFadeIn = 14, CinematicSkip = 15, ScriptLayerController = 16, ScaleActor = 22, Billboard = 24;

        public int Function;
        public string Text;
        public float F1, F2, F3, F4;
        public int W2, W3, Item;
        public Vector3 GamePos;
        private float _saveDoneT = -1f;
        private bool _saveOk;

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            if (o.PropCount != 15) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, false);
            int fn = r.S32();
            if (fn == IntroBossRingController)
            {
                // the boss's rings belong to the Parasite Queen's code (Game/Enemy/MpIntroBossRings), found by name so neither side waits on the other
                System.Type t = typeof(MpSpecialFunctionEntity).Assembly.GetType("SomniumSpace.Worlds.SomniumMetroid.Game.MpIntroBossRings");
                System.Reflection.MethodInfo mk = t != null ? t.GetMethod("Make", new[] { typeof(MpScriptObject), typeof(MpAreaView) }) : null;
                if (mk != null) return mk.Invoke(null, new object[] { o, view }) as MpEntity;
            }

            var e = new MpSpecialFunctionEntity { Function = fn, GamePos = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z) };
            e.Text = r.CString();
            e.F1 = r.F32(); e.F2 = r.F32(); e.F3 = r.F32();
            e.W2 = r.S32(); e.W3 = r.S32(); e.Item = r.S32();
            e.Active = r.Bool();
            e.F4 = r.F32();
            return e;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if (!Active) return;
            MpGame g = MpGame.Instance;
            if (g == null) return;
            switch (Function)
            {
                case ScriptLayerController:
                    if (msg == MpScriptMsg.Increment || msg == MpScriptMsg.Decrement) g.SetLayer((uint)W2, W3, msg == MpScriptMsg.Increment, Name);
                    break;
                case CinematicSkip:
                    if (msg == MpScriptMsg.Increment) MpCinematics.SkipObject = this;
                    else if (msg == MpScriptMsg.Decrement && MpCinematics.SkipObject == this) MpCinematics.SkipObject = null;
                    break;
                case SaveStation:
                    if (msg == MpScriptMsg.Action)
                    {
                        g.State.Energy = g.State.MaxEnergy;
                        string said = g.Save(MpGame.SaveSlot, "save station");
                        if (g.Hud != null) g.Hud.ShowMessage(said == "Saved" ? "Game saved. Energy restored." : said, 4f);
                        _saveOk = said == "Saved";
                        _saveDoneT = 0.05f;
                    }

                    break;
                case MapStation:
                    if (msg == MpScriptMsg.Action && g.Hud != null) g.Hud.ShowMessage("Map data downloaded.", 3f);
                    break;
                case EscapeSequence:
                    if (msg == MpScriptMsg.Action && F1 >= 0f) g.StartEscape(F1);
                    break;
                case BossEnergyBar:
                    if (msg == MpScriptMsg.Increment) g.SetBoss(from, (int)F2 + 86);
                    else if (msg == MpScriptMsg.Decrement) g.SetBoss(null, 0);
                    break;
                case EndGame:
                    if (msg == MpScriptMsg.Action) g.OnWorldTeleport(0, 0);
                    break;
            }
        }

        public override void Think(float dt)
        {
            if (_saveDoneT < 0f) return;
            _saveDoneT -= dt;
            // the game's save screen answers MaxReached when saved (the save camera plays out) and Zero when not (the "not saving" camera)
            if (_saveDoneT < 0f) Send(_saveOk ? MpScriptMsg.StMaxReached : MpScriptMsg.StZero);
        }
    }
}
