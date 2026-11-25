using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The game moves the player's own avatar (the WoW Dreamport's AvatarHold, ported: its mounts, flights and forced moves): the
    /// morph ball in third person carries the avatar along the ball camera - the AVATAR moves, the world never does, so every player
    /// sees the same world (co-op: "make the CHARACTER move, not the world"). Take-over: the client's collision / fly / teleport off,
    /// speed, gravity and jump 0, the falling animation and VRIK's leg steps off, the avatar's CharacterController disabled. Each frame
    /// the avatar root is written (VR: position only - the player's own turn is kept). Release, in WoW's no-drop order: the root
    /// written on the spot with the controller still off, everything given back, then the client's own teleport there.
    /// All Bridge members are on the proven list (fusionapi/known_good.txt).
    /// </summary>
    public sealed class MpAvatarHold
    {
        public Transform Root { get; private set; }
        public bool Taken { get; private set; }

        private CharacterController _cc;
        private bool _ccWas;
        private ISomniumPlayerMotion _motion;
        private ISomniumPlayerAnimation _animation;
        private ISomniumPlayerAvatar _avatar;

        public bool TakeOver(Transform root, out string why)
        {
            why = null;
            if (Taken) return true;
            if (root == null) { why = "no avatar"; return false; }
            Root = root;
            ISomniumPlayer player = null;
            try { player = SomniumBridge.PlayersContainer?.LocalPlayer; } catch (Exception) { player = null; }
            try
            {
                _motion = player?.Features?.Motion;
                _animation = player?.Features?.Animation;
                _avatar = player?.Features?.Avatar;
            }
            catch (Exception e) { MpLog.Warn("Hold: reading the player's features threw " + e.Message); }
            _cc = root.GetComponentInParent<CharacterController>();
            if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(true);
                    _motion.SetFlyModeDisableState(true);
                    _motion.SetTeleportationDisableState(true);
                    _motion.SetMovementSpeed(0f, 0f, 0f);
                    _motion.SetGravity(0f, 0f);
                    _motion.SetJump(0f, 0f);
                }
                catch (Exception e) { MpLog.Warn("Hold: motion settings threw " + e.Message); }
            }

            try { _animation?.SetFallingAnimationDisabled(true); } catch (Exception) { }
            try { _avatar?.SetVRIKLegMovementDisabled(true); } catch (Exception) { }
            if (_cc != null) { _ccWas = _cc.enabled; _cc.enabled = false; }
            Taken = true;
            return true;
        }

        /// <summary>The held avatar's root this frame (VR: position only).</summary>
        public void Write(Vector3 rootPos)
        {
            if (!Taken || Root == null) return;
            Root.position = rootPos;
        }

        /// <summary>Gives the avatar back standing on <paramref name="spot"/> (its feet), facing <paramref name="yaw"/> (desktop).</summary>
        public void Release(Vector3 spot, float yaw, bool vr, out string how)
        {
            how = "none";
            if (!Taken) return;
            Taken = false;
            if (Root != null)
            {
                if (vr) yaw = Root.eulerAngles.y;
                Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));   // no drop: the controller is still off
            }

            try { _avatar?.SetVRIKLegMovementDisabled(false); } catch (Exception) { }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(false);
                    _motion.SetFlyModeDisableState(false);
                    _motion.SetTeleportationDisableState(false);
                    _motion.SetMovementSpeed(1f, 1f, 1f);
                    _motion.SetGravity(1f, 1f);
                    _motion.SetJump(1f, 1f);
                }
                catch (Exception e) { MpLog.Warn("Hold: restoring the motion settings threw " + e.Message); }
            }

            try { _animation?.SetFallingAnimationDisabled(false); } catch (Exception) { }
            if (_cc != null) _cc.enabled = _ccWas;
            if (Root != null && !MpPlayer.Teleport(spot, yaw, out how))
            {
                bool was = _cc != null && _cc.enabled;
                if (_cc != null) _cc.enabled = false;
                Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                if (_cc != null) _cc.enabled = was;
                how = "direct write";
            }

            _cc = null;
            _motion = null; _animation = null; _avatar = null;
        }
    }
}
