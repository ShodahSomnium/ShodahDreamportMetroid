using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// First-person cutscenes (tablet: "Cutscenes: you are the camera", on by default): while a cinematic camera runs, the WORLD slides
    /// so the camera's place is at the player's own eyes (MpSpace.Shift, the elevators' and the morph ball's way); the player looks
    /// wherever they like and sees the scene around them instead of on the cinema screen. As the cutscene starts the player is turned
    /// once to face the camera's view. Meanwhile their avatar is hidden and held still (no walking, gravity), the cannon and HUD are
    /// put away, and Samus's body is drawn in the world (played by the master's avatar: MpCinemaAvatar). Only this view's own slide is
    /// undone at the end, so an elevator ride or a teleport during the cutscene stands.
    /// </summary>
    public static class MpCinemaView
    {
        public static bool Active { get; private set; }
        /// <summary>The slide this view has applied so far (undone at the end).</summary>
        public static Vector3 Applied { get { return _applied; } }
        /// <summary>Where the eyes were when the cutscene started (Unity): each shot's camera is kept here.</summary>
        public static Vector3 Anchor { get { return _anchor; } }
        /// <summary>Labs: no turn to face the shot (the editor's stand-in teleport moves the lab camera itself).</summary>
        public static bool NoTurn;
        private static Vector3 _anchor, _applied;
        private static MpCinematicCamera _cam;
        private static int _logs;

        private static Vector3 _feet0, _gameFeet;
        private static bool _haveFeet;
        private static float _gapT, _backAt;
        private static GameObject _stand;
        private static float _reanchorT;
        private static Quaternion _ahead = Quaternion.identity;
        /// <summary>"Straight ahead" for this cutscene (each shot's view is turned to it).</summary>
        public static Quaternion Ahead { get { return _ahead; } }

        /// <summary>Where Samus stands for the game while the world slides (the player's place when the cutscene began): the
        /// moving world must not carry "Samus" through walls, triggers and room loads along the camera path.</summary>
        public static Vector3 GameFeet { get { return _gameFeet; } }

        /// <summary>Every frame of play, after the scripts ran (the cinematic cameras have moved).</summary>
        public static void Tick(MpGame g)
        {
            MpCinematicCamera c = MpCinematics.Current;
            bool want = MpSettings.CinemaFirstPerson && c != null && g != null && !g.Ball.Morphed && MpGame.LabFocus == null && MpPlayer.Head() != null;
            if (!want)
            {
                // between two shots a cutscene may have no camera for a frame or two: the view holds (no snap back, no re-turn)
                if (Active && c == null && g != null && !g.Ball.Morphed && MpSettings.CinemaFirstPerson && (_gapT += Time.deltaTime) < 0.4f) { Guard(); return; }
                if (Active) End();
                return;
            }

            _gapT = 0f;
            if (!Active) Begin(c);
            else if (c != _cam) _cam = c;     // the next shot: the world turns to it (below), the eyes stay where they are
            if (_reanchorT > 0f && (_reanchorT -= Time.deltaTime) <= 0f && MpPlayer.Head() != null) _anchor = MpPlayer.Head().transform.position;

            // the shot exactly as the game frames it (yaw, pitch, roll): the world is turned so its view is straight ahead of the
            // player, then slid so its camera is at the eyes. The path is the game's own: through terrain and objects as on the TV.
            Vector3 f0 = new Vector3(c.ViewForward.x, c.ViewForward.z, c.ViewForward.y), u0 = new Vector3(c.ViewUp.x, c.ViewUp.z, c.ViewUp.y);
            if (f0.sqrMagnitude > 1e-8f && Vector3.Cross(f0, u0).sqrMagnitude > 1e-8f) MpSpace.SetTurn(_ahead * Quaternion.Inverse(Quaternion.LookRotation(f0, u0)));
            Vector3 d = _anchor - MpSpace.ToUnity(c.ViewPos);
            if (d.sqrMagnitude > 1e-10f)
            {
                MpSpace.Shift(d);
                _applied += d;
            }

            Guard();
        }

        /// <summary>The hidden avatar stays where it stood (no gravity, no collision); if it was moved anyway it is put back.</summary>
        private static void Guard()
        {
            Vector3 f;
            if (!_haveFeet || !MpPlayer.TryFeet(out f) || (f - _feet0).magnitude < 0.5f || Time.unscaledTime < _backAt) return;
            _backAt = Time.unscaledTime + 0.3f;
            PutBack("drifted " + (f - _feet0).magnitude.ToString("0.0") + " m");
        }

        private static void PutBack(string why)
        {
            float yaw;
            string src, how;
            MpPlayer.Teleport(_feet0 + Vector3.up * 0.02f, MpPlayer.TryFacingYaw(out yaw, out src) ? yaw : 0f, out how);
            if (_logs++ < 30) MpLog.Warn("Cinema: the avatar " + why + " - put back where it stood (" + how + ")");
        }

        private static void Collision(bool off)
        {
            try { var m = MpPlayer.Motion; if (m != null) m.SetCollisionDisableState(off); }
            catch (System.Exception e) { if (_logs++ < 30) MpLog.Warn("Cinema: collision " + (off ? "off" : "on") + " failed: " + e.Message); }
        }

        /// <summary>Turns the player to face the shot's view (the yaw only; the headset keeps its own) - at the start and at every new
        /// shot. The turn moves the head about the feet, so the eye anchor is taken again a moment later.</summary>
        private static void FaceShot(MpCinematicCamera c)
        {
            Camera head = MpPlayer.Head();
            if (head == null || !_haveFeet || NoTurn) return;
            Vector3 f = MpSpace.DirToUnity(c.ViewForward);
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            Vector3 hf = head.transform.forward;
            hf.y = 0f;
            float turn = hf.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(hf, f, Vector3.up) : 0f;
            float yaw;
            string src, how;
            if (Mathf.Abs(turn) <= 20f || !MpPlayer.TryFacingYaw(out yaw, out src)) return;
            MpPlayer.Teleport(_feet0, yaw + turn, out how);
            _reanchorT = 0.2f;
        }

        private static void Begin(MpCinematicCamera c)
        {
            Active = true;
            _cam = c;
            _gapT = 0f;
            _applied = Vector3.zero;
            Camera head = MpPlayer.Head();
            _anchor = head.transform.position;
            _haveFeet = MpPlayer.TryFeet(out _feet0);
            _gameFeet = _haveFeet ? MpSpace.ToGame(_feet0) : (MpGame.Instance != null ? MpGame.Instance.PlayerGame : Vector3.zero);
            // the moving world must not push or drop the avatar: no collision with it, no gravity (MpMovement.Frozen), and a floor of
            // its own that stays put (outside the game world; the editor's stand-in controller needs it, the client keeps it as a net)
            if (_haveFeet)
            {
                if (_stand == null) { _stand = new GameObject("Cinema stand"); _stand.AddComponent<BoxCollider>().size = new Vector3(3f, 0.2f, 3f); }
                _stand.transform.position = _feet0 - new Vector3(0f, 0.1f, 0f);
                _stand.SetActive(true);
            }

            Collision(true);
            // the way the player faced as it began is "straight ahead": every shot is turned to it (MpSpace.Turn)
            Vector3 hf = head.transform.forward;
            hf.y = 0f;
            _ahead = Quaternion.LookRotation(hf.sqrMagnitude > 1e-4f ? hf.normalized : Vector3.forward, Vector3.up);

            if (_logs++ < 20) MpLog.Info("Cinema: '" + c.Name + "' seen first person (the world moves around the player)");
        }

        /// <summary>The cutscene is over (or a teleport needs the world back): only this view's own slide is undone, the avatar is
        /// where it stood when the cutscene began, collision on again.</summary>
        public static void End()
        {
            if (!Active) return;
            Active = false;
            MpSpace.SetTurn(Quaternion.identity);
            if (_applied.sqrMagnitude > 0f) MpSpace.Shift(-_applied);
            _applied = Vector3.zero;
            _cam = null;
            Collision(false);
            if (_stand != null) _stand.SetActive(false);
            Vector3 f;
            if (_haveFeet && MpPlayer.TryFeet(out f) && (f - _feet0).magnitude > 0.3f) PutBack("was moved during the cutscene");
            _haveFeet = false;
            foreach (MpPlayerActorEntity a in MpCinematics.CinemaActors) a.SetCinemaVisible(false);
            MpCinemaAvatar.HideAll();
        }
    }
}
