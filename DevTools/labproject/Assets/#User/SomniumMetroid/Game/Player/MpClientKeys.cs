using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The client's own buttons and keys that clash with Metroid Prime's, kept quiet while the game runs (ported from the BO2 Dreamport's
    /// Bo2FirstPerson). VR: the right controller's B fires missiles, so the client's actions on it - RightHandTablet (the tablet on a tap) and
    /// RightHandPopUPmenu (map "Interactions", XRI Player Interactions.inputactions) - are held off. Monitor: first person (Metroid Prime is a
    /// first-person game) and the 2DPlayer keys the game uses: F (missile), X (visor), Q/E, R, I.
    ///
    /// The shipped client is obfuscated (its classes and private fields have scrambled names that change with every build), so nothing of it is
    /// reached by name. Everything goes through Unity's Input System instead (reflection: Unity.InputSystem types may not be compiled against), whose
    /// names are never obfuscated, plus the client's GameObject names (prefab names survive):
    /// - First person: the client's camera ("2D Input Actions", map 2DPlayer, action Zoom: scroll up = closer) switches to its first-person rig at its
    ///   closest zoom. While the camera is away from the avatar's head (third person), mouse-wheel notches toward the head are queued on the mouse
    ///   (InputSystem.QueueDeltaStateEvent on Mouse.current.scroll), exactly as if the player scrolled. If the camera moves away instead, the direction flips.
    /// - Keys: the 2DPlayer actions on the game's keys are disabled for the match (R PopUpMenu, Q/E Rotate, F Interact, X HideObject / MuteObject) and
    ///   enabled again afterwards. Tab (the tablet, the game's menu) is left alone. The client enables a whole map again now and then (a map swap), so the
    ///   actions found are held off every frame, early (Update) and late (just before the camera draws), and new ones are looked for every second.
    /// - ESC: if the client's R menu ("PopUpMenu.NonXR(Clone)") is open anyway, it is closed the client's own way: R is let through for a moment and one
    ///   R tap is queued on the keyboard (the menu toggles on R; it blocks the camera while open).
    /// Any failure is logged once and leaves the client alone.
    /// </summary>
    public static class MpClientKeys
    {
        /// <summary>Hold the monitor camera in first person (Metroid Prime is a first-person game).</summary>
        public static bool ForceFirstPerson = true;

        /// <summary>True while a headset runs: only the VR actions are held off and the camera is the client's.</summary>
        public static bool Vr;

        private static readonly string[] BlockedActions = { "PopUpMenu", "Rotate", "Interact", "HideObject", "MuteObject", "Inspector" };
        private static readonly string[] BlockedVrActions = { "RightHandTablet", "RightHandPopUPmenu" };
        private static readonly string[] MenuNames = { "PopUpMenu.NonXR(Clone)", "PopUpMenu(Clone)" };
        private const float ThirdPersonDistance = 0.35f;        // the camera this far from the avatar's head: not first person
        private const int MaxNotches = 200;

        private sealed class Held
        {
            public object Action;
            public string Name;
            public Func<bool> Enabled;
            public Action Disable, Enable;
        }

        private static bool _ready, _failed, _loggedZoom, _loggedFirst, _loggedKeys, _flipped;
        private static MethodInfo _queue, _queueState, _listEnabled;
        private static PropertyInfo _mouseCurrent, _mouseScroll, _keyboardCurrent;
        private static Type _keyboardState, _key;
        private static readonly object[] QueueArgs = new object[3];
        private static float _sign = 1f, _nextKeys, _burstStart = -1f;
        private static int _notches, _closing;      // _closing: frames left of the ESC close (3 R down, 2 R up, 1 the keys off again)
        private static readonly List<Held> Disabled = new List<Held>();

        /// <summary>The morph ball rolls: the client's walking, jumping and turning are held off (the stick and keys steer the ball).</summary>
        public static bool HoldMovement;
        private static readonly string[] MoveActions = { "Move", "Continuous Move", "Jump", "Sprint", "Turn", "Snap Turn", "SnapTurn", "Gamepad Move" };
        private static readonly List<Held> MoveHeld = new List<Held>();
        private static readonly List<Held> MoveOff = new List<Held>();

        /// <summary>Every frame while a monitor player is in a match.</summary>
        public static void Tick()
        {
            if (_failed || Application.isEditor) return;
            try
            {
                if (!_ready) Init();
                if (ForceFirstPerson && !Vr) Zoom();
                StepClose();
                if (_closing == 0 && EscPressed()) TryCloseMenu();
                if (Time.unscaledTime >= _nextKeys) { _nextKeys = Time.unscaledTime + 1f; FindKeys(); }
                HoldKeys();
            }
            catch (Exception e)
            {
                _failed = true;
                MpLog.Warn("View: the client's camera and keys could not be reached (" + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message) + "): left as they are.");
                Release();
            }
        }

        /// <summary>Late in the frame (just before the camera draws): the clashing client keys are off again if the client switched them on during its Update.</summary>
        public static void HoldKeys()
        {
            HoldMove();
            if (_closing > 0) return;
            for (int i = 0; i < Disabled.Count; i++)
            {
                Held h = Disabled[i];
                try { if (h.Enabled()) h.Disable(); } catch (Exception) { /* an action that went away: found again by the next search */ }
            }
        }

        /// <summary>The match is over: the client's keys work again (the camera is left where it is: first person is a normal client view).</summary>
        public static void Release()
        {
            for (int i = 0; i < Disabled.Count; i++)
            {
                try { Disabled[i].Enable(); } catch (Exception) { /* nothing more to do */ }
            }

            Disabled.Clear();
            _burstStart = -1f;
            _notches = 0;
            _closing = 0;
        }

        private static void Init()
        {
            Type input = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            Type mouse = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            Type keyboard = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
            _keyboardState = Type.GetType("UnityEngine.InputSystem.LowLevel.KeyboardState, Unity.InputSystem");
            _key = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
            if (input == null || mouse == null) throw new InvalidOperationException("no Input System");
            foreach (MethodInfo m in input.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (m.Name == "QueueDeltaStateEvent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3) _queue = m.MakeGenericMethod(typeof(Vector2));
                if (m.Name == "QueueStateEvent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3 && _keyboardState != null) _queueState = m.MakeGenericMethod(_keyboardState);
                if (m.Name == "ListEnabledActions" && m.GetParameters().Length == 0) _listEnabled = m;
            }

            _mouseCurrent = mouse.GetProperty("current", BindingFlags.Static | BindingFlags.Public);
            _mouseScroll = mouse.GetProperty("scroll", BindingFlags.Instance | BindingFlags.Public);
            _keyboardCurrent = keyboard != null ? keyboard.GetProperty("current", BindingFlags.Static | BindingFlags.Public) : null;
            if (_queue == null || _listEnabled == null || _mouseCurrent == null || _mouseScroll == null) throw new InvalidOperationException("Input System members missing");
            _ready = true;
        }

        private static void Zoom()
        {
            Camera cam = MpPlayer.Head();
            Transform head = MpPlayer.HeadBone;
            if (cam == null || head == null || _closing > 0) return;
            float d = Vector3.Distance(cam.transform.position, head.position);
            if (d <= ThirdPersonDistance)
            {
                if (_burstStart >= 0f && !_loggedFirst) { _loggedFirst = true; MpLog.Info("View: first person reached after " + _notches + " mouse-wheel notch(es)."); }
                _burstStart = -1f;
                _notches = 0;
                return;
            }

            if (_burstStart < 0f)
            {
                _burstStart = d;
                if (!_loggedZoom) { _loggedZoom = true; MpLog.Info("View: the camera is " + d.ToString("0.00") + " m from the avatar's head (third person): scrolling it in to first person."); }
            }

            // after a dozen notches the camera must have come closer; if it went away, the wheel works the other way round in this client
            if (_notches == 12 && !_flipped && d > _burstStart - 0.1f)
            {
                _flipped = true;
                _sign = -_sign;
                MpLog.Info("View: the camera did not come closer (" + _burstStart.ToString("0.00") + " -> " + d.ToString("0.00") + " m): scrolling the other way.");
            }

            if (_notches > MaxNotches) return;      // it does not respond: stop pushing
            object m = _mouseCurrent.GetValue(null, null);
            if (m == null) return;
            QueueArgs[0] = _mouseScroll.GetValue(m, null);
            QueueArgs[1] = new Vector2(0f, 120f * _sign);
            QueueArgs[2] = -1.0;
            _queue.Invoke(null, QueueArgs);
            _notches++;
        }

        private static bool EscPressed()
        {
            try { return Input.GetKeyDown(KeyCode.Escape); } catch (Exception) { return false; }
        }

        private static void TryCloseMenu()
        {
            GameObject menu = null;
            for (int i = 0; i < MenuNames.Length && menu == null; i++) menu = GameObject.Find(MenuNames[i]);      // (only finds it while it is shown)
            if (menu == null || _queueState == null || _keyboardCurrent == null || _key == null) return;
            Held popup = null;
            for (int i = 0; i < Disabled.Count; i++) if (Disabled[i].Name == "PopUpMenu") popup = Disabled[i];
            if (popup != null) popup.Enable();
            if (!QueueR(true)) return;
            _closing = 3;
            MpLog.Info("View: ESC - the client's R menu was open: closing it with an R tap.");
        }

        private static void StepClose()
        {
            if (_closing <= 0) return;
            _closing--;
            if (_closing == 1) QueueR(false);
        }

        private static bool QueueR(bool down)
        {
            object kb = _keyboardCurrent.GetValue(null, null);
            if (kb == null) return false;
            object state;
            if (down)
            {
                Array keys = Array.CreateInstance(_key, 1);
                keys.SetValue(Enum.Parse(_key, "R"), 0);
                state = Activator.CreateInstance(_keyboardState, new object[] { keys });
            }
            else state = Activator.CreateInstance(_keyboardState);
            _queueState.Invoke(null, new object[] { kb, state, -1.0 });
            return true;
        }

        private static Held MakeHeld(object a, string name, Type t)
        {
            return new Held
            {
                Action = a,
                Name = name,
                Enabled = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), a, t.GetProperty("enabled").GetGetMethod()),
                Disable = (Action)Delegate.CreateDelegate(typeof(Action), a, t.GetMethod("Disable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)),
                Enable = (Action)Delegate.CreateDelegate(typeof(Action), a, t.GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)),
            };
        }

        /// <summary>While the ball rolls the movement actions are kept off (and only the ones switched off here are switched on again).</summary>
        private static void HoldMove()
        {
            try
            {
                if (HoldMovement)
                {
                    foreach (Held h in MoveHeld) if (h.Enabled()) { h.Disable(); if (!MoveOff.Contains(h)) MoveOff.Add(h); }
                }
                else if (MoveOff.Count > 0)
                {
                    foreach (Held h in MoveOff) if (!h.Enabled()) h.Enable();
                    MoveOff.Clear();
                }
            }
            catch (Exception e) { MpLog.Warn("Keys: movement hold failed: " + e.Message); HoldMovement = false; MoveOff.Clear(); }
        }

        private static void FindKeys()
        {
            var list = _listEnabled.Invoke(null, null) as IList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                object a = list[i];
                if (a == null) continue;
                Type t = a.GetType();
                string name = t.GetProperty("name").GetValue(a, null) as string;
                object map = t.GetProperty("actionMap").GetValue(a, null);
                string mapName = map != null ? map.GetType().GetProperty("name").GetValue(map, null) as string : null;
                if (Array.IndexOf(MoveActions, name) >= 0 && mapName != "Interactions" && mapName != "UI")
                {
                    bool seen = false;
                    for (int k = 0; k < MoveHeld.Count; k++) if (ReferenceEquals(MoveHeld[k].Action, a)) seen = true;
                    if (!seen) MoveHeld.Add(MakeHeld(a, name, t));
                }

                bool block = Vr ? mapName == "Interactions" && Array.IndexOf(BlockedVrActions, name) >= 0 : mapName == "2DPlayer" && Array.IndexOf(BlockedActions, name) >= 0;
                if (!block) continue;
                bool known = false;
                for (int k = 0; k < Disabled.Count; k++) if (ReferenceEquals(Disabled[k].Action, a)) known = true;
                if (!known)
                {
                    Disabled.Add(new Held
                    {
                        Action = a,
                        Name = name,
                        Enabled = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), a, t.GetProperty("enabled").GetGetMethod()),
                        Disable = (Action)Delegate.CreateDelegate(typeof(Action), a, t.GetMethod("Disable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)),
                        Enable = (Action)Delegate.CreateDelegate(typeof(Action), a, t.GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)),
                    });
                }

                if (!_loggedKeys) { _loggedKeys = true; MpLog.Info(Vr ? "Keys: the client's tablet and pop-up menu on the right B are off while playing (B fires missiles)." : "Keys: the client's own keys that clash with the game's are off while playing (F missile, X visor, R pop-up menu, Q/E rotate, I inspector; ESC closes the R menu if it is open)."); }
            }
        }
    }
}
