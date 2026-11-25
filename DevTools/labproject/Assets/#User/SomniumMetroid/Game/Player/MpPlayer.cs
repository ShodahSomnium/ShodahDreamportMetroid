using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The local player through the Somnium Bridge (null-safe: the editor has no client), the camera the player looks
    /// through, headset detection and the desktop key. Patterns from EtsVr (Head, Present, Teleport) and AomBridge.
    /// </summary>
    public static class MpPlayer
    {
        /// <summary>
        /// Back to the room on a monitor. Picked after reading the client's bindings (E:\Somnium Space\Main Build):
        /// _SomniumSpace/Inputs/2D + VR *.inputactions bind W A S D, arrows, X, Space, Shift, R, Escape, Tab, Q, M, I, F, E, Alt;
        /// Input.GetKey / Keyboard.current in the client's own scripts use S, LeftControl, F, D, Z, UpArrow, Space, Q,
        /// LeftShift, G, F4-F6, E, DownArrow, A. K appears only in the XR Device Simulator's controls (an editor tool).
        /// The client runs both input back ends (ProjectSettings activeInputHandler: 2), so the legacy Input works.
        /// </summary>
        public const KeyCode BackKey = KeyCode.K;
        public const string BackKeyName = "K";

        private static int _presentFrame = -1;
        private static bool _present;
        private static int _headFrame = -1;
        private static Camera _head;
        private static float _nextHeadSearch;
        private static bool _keyBroken;

        /// <summary>A headset is running (asked of XRSettings once a frame, as EtsVr.Present).</summary>
        public static bool XrPresent
        {
            get
            {
                if (Time.frameCount != _presentFrame)
                {
                    _presentFrame = Time.frameCount;
                    try { _present = XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName); }
                    catch (Exception) { _present = false; }
                }

                return _present;
            }
        }

        /// <summary>
        /// The camera the player looks through: Camera.main when live, otherwise any enabled camera drawing to the screen
        /// (searched at most 4 times a second). Asked again every frame: the client's camera arrives late and changes.
        /// </summary>
        public static Camera Head()
        {
            if (Time.frameCount == _headFrame)
            {
                return _head;
            }

            _headFrame = Time.frameCount;
            Camera main = Camera.main;

            if (main != null && main.isActiveAndEnabled && main.targetTexture == null)
            {
                _head = main;
                return _head;
            }

            if (_head != null && _head.isActiveAndEnabled && _head.targetTexture == null)
            {
                return _head;
            }

            if (Time.unscaledTime < _nextHeadSearch)
            {
                return _head;
            }

            _nextHeadSearch = Time.unscaledTime + 0.25f;
            _head = null;

            foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.isActiveAndEnabled && c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    _head = c;
                    break;
                }
            }

            return _head;
        }

        private static ISomniumPlayer Local
        {
            get
            {
                try { return SomniumBridge.PlayersContainer?.LocalPlayer; }
                catch (Exception) { return null; }
            }
        }

        public static bool HasBridgePlayer { get { return Local != null; } }

        /// <summary>
        /// The instance's master: Fusion's shared-mode master as the co-op knows it (MpCoop.MasterId, the Photon id of the player whose
        /// game everyone plays); before the room is joined, the player who has been here longest (the lowest Photon id). Its avatar's
        /// head (Body.Head), or the local player's when alone / when it cannot be told. <paramref name="isLocal"/>: the master is this player.
        /// </summary>
        public static Transform MasterHead(out bool isLocal)
        {
            isLocal = true;
            try
            {
                var c = SomniumBridge.PlayersContainer;
                ISomniumPlayer local = c != null ? c.LocalPlayer : null, best = null;
                int bestId = int.MaxValue;
                int master = MpCoop.Online ? MpCoop.MasterId : -1;
                if (c != null && c.AllPlayers != null)
                    foreach (ISomniumPlayer p in c.AllPlayers)
                    {
                        if (p == null || p.Properties == null || p.References?.Body?.Head == null) continue;
                        int id = p.Properties.PhotonId;
                        if (master >= 0) { if (id == master) { best = p; break; } continue; }
                        if (id < bestId) { bestId = id; best = p; }
                    }

                if (best == null) best = local;
                isLocal = ReferenceEquals(best, local);
                return best?.References?.Body?.Head;
            }
            catch (Exception) { return HeadBone; }
        }

        /// <summary>The client's motion feature (speed, jump, gravity multipliers; MpMovement), or null.</summary>
        public static ISomniumPlayerMotion Motion
        {
            get { try { return Local?.Features?.Motion; } catch (Exception) { return null; } }
        }

        public static Transform Root
        {
            get { try { return Local?.References?.Body?.Root; } catch (Exception) { return null; } }
        }

        /// <summary>The avatar's head (Body.Head: follows the headset in VR, the avatar's head on a monitor), or null.</summary>
        public static Transform HeadBone
        {
            get { try { return Local?.References?.Body?.Head; } catch (Exception) { return null; } }
        }

        /// <summary>
        /// Where the player faces, as a Unity yaw in degrees: in a headset the headset's yaw (the body may not turn with
        /// room-scale head turns), on a monitor the avatar root's yaw (the avatar turns to where it walks while the orbit
        /// camera looks from behind). Falls back to Body.Head, then the camera. False when nothing is known.
        /// </summary>
        public static bool TryFacingYaw(out float yawDegrees, out string source)
        {
            Transform t = null;
            source = null;

            if (XrPresent)
            {
                Camera c = Head();
                if (c != null) { t = c.transform; source = "headset"; }
            }

            if (t == null && Root != null) { t = Root; source = "avatar root"; }
            if (t != null && !Horizontal(t.forward)) t = null;
            if (t == null && HeadBone != null && Horizontal(HeadBone.forward)) { t = HeadBone; source = "avatar head"; }

            if (t == null)
            {
                Camera c = Head();
                if (c != null && Horizontal(c.transform.forward)) { t = c.transform; source = "camera"; }
            }

            if (t == null)
            {
                yawDegrees = 0f;
                source = "none";
                return false;
            }

            Vector3 f = t.forward;
            yawDegrees = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return true;
        }

        private static bool Horizontal(Vector3 f)
        {
            return f.x * f.x + f.z * f.z > 0.01f;
        }

        public static Transform LeftHand
        {
            get { try { return Local?.References?.Body?.LeftHand; } catch (Exception) { return null; } }
        }

        public static Transform RightHand
        {
            get { try { return Local?.References?.Body?.RightHand; } catch (Exception) { return null; } }
        }


        /// <summary>
        /// Carries the avatar by a Unity offset (riding an elevator): its CharacterController transform moved directly, as Somnium's
        /// own elevators do (MobilePlatform: never CharacterController.Move) and the Euro Truck Simulator / WoW Dreamports' riders, then
        /// Physics.SyncTransforms so the client steps against the platform where it is drawn. The editor: its stand-in controller.
        /// </summary>
        /// <summary>The body the game may hold / carry: the avatar root, or in the editor its stand-in controller.</summary>
        public static Transform BodyRoot { get { Transform r = Root; if (r != null) return r; CharacterController cc = EditorController(); return cc != null ? cc.transform : null; } }

        public static bool Carry(Vector3 delta)
        {
            Transform root = Root;
            if (root == null)
            {
                CharacterController cc = EditorController();
                if (cc == null) return false;
                bool was = cc.enabled;
                cc.enabled = false;
                cc.transform.position += delta;
                cc.enabled = was;
            }
            else root.position += delta;
            Physics.SyncTransforms();
            return true;
        }
        /// <summary>Where the avatar stands (its root), or in the editor the camera's feet; false when neither exists.</summary>
        public static bool TryFeet(out Vector3 feet)
        {
            Transform root = Root;

            if (root != null)
            {
                feet = root.position;
                return true;
            }

            CharacterController cc = EditorController();

            if (cc != null)
            {
                feet = cc.transform.position;
                return true;
            }

            Camera head = Head();

            if (head != null)
            {
                feet = head.transform.position - new Vector3(0f, 1.6f, 0f);
                return true;
            }

            feet = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Moves the player's body and character controller together (the only safe way: PLAN rule 10). In the editor,
        /// with no client, the SDK test harness's CharacterController (WorldTestingManager) or the main camera is moved.
        /// Returns false when nothing could be moved yet.
        /// </summary>
        public static bool Teleport(Vector3 position, float yawDegrees, out string how)
        {
            return Teleport(position, yawDegrees, null, out how);
        }

        /// <summary>
        /// As <see cref="Teleport(Vector3, float, out string)"/>, with the client's "teleport done" callback (DoTeleportToPoint's
        /// third argument; the client applies a teleport asynchronously, a few frames later). In the editor fallbacks the move
        /// is immediate and done is called before this returns. 
        /// </summary>
        public static bool Teleport(Vector3 position, float yawDegrees, Action done, out string how)
        {
            ISomniumPlayerMotion motion = null;

            try { motion = Local?.Features?.Motion; }
            catch (Exception) { motion = null; }

            if (motion != null)
            {
                motion.DoTeleportToPoint(position, new Vector3(0f, yawDegrees, 0f), done);
                how = "DoTeleportToPoint";
                return true;
            }

            if (!Application.isEditor)
            {
                how = "no Bridge player yet";
                return false;
            }

            CharacterController cc = EditorController();

            if (cc != null)
            {
                bool was = cc.enabled;
                cc.enabled = false;
                cc.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
                cc.enabled = was;
                how = "editor: CharacterController '" + cc.name + "' moved";
                if (done != null) done();
                return true;
            }

            Camera head = Camera.main;

            if (head != null)
            {
                head.transform.SetPositionAndRotation(position + new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, yawDegrees, 0f));
                how = "editor: camera '" + head.name + "' moved";
                if (done != null) done();
                return true;
            }

            how = "editor: nothing to move";
            return false;
        }

        private static CharacterController _editorController;
        private static float _nextControllerSearch;

        private static CharacterController EditorController()
        {
            if (!Application.isEditor || HasBridgePlayer)
            {
                return null;
            }

            if (_editorController != null)
            {
                return _editorController;
            }

            if (Time.unscaledTime < _nextControllerSearch)
            {
                return null;
            }

            _nextControllerSearch = Time.unscaledTime + 1f;
            _editorController = UnityEngine.Object.FindFirstObjectByType<CharacterController>();
            return _editorController;
        }

        /// <summary>The back key went down this frame (never while a text field has the keyboard).</summary>
        public static bool BackKeyPressed()
        {
            if (_keyBroken)
            {
                return false;
            }

            try
            {
                if (!Input.GetKeyDown(BackKey))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                _keyBroken = true;
                MpLog.Warn("Back key " + BackKeyName + " unavailable (legacy Input): " + e.Message);
                return false;
            }

            EventSystem es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;

            if (selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null))
            {
                return false;
            }

            return true;
        }

        /// <summary>An EventSystem for the editor only (the client brings its own XRUIInputModule one), as EtsMenu does.</summary>
        public static void EnsureEditorEventSystem()
        {
            if (!Application.isEditor || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var events = new GameObject("Mp EventSystem (editor)");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
            MpLog.Info("Editor: no EventSystem in the scene - added one with a StandaloneInputModule.");
        }
    }
}
