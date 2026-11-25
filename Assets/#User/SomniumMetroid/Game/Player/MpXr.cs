using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>One controller's state for this frame.</summary>
    public struct MpHand
    {
        public float Grip;               // 0..1, with the Index-controller rules of the Skyrim Dreamport.s GearXr
        public float Trigger;            // 0..1
        public bool GripHeld, GripPressed, GripReleased;
        public bool TriggerHeld, TriggerPressed, TriggerReleased;
        public bool Primary, PrimaryPressed;      // A on the right controller, X on the left
        public bool Secondary, SecondaryPressed;  // B on the right controller, Y on the left
        public bool StickClick, StickClickPressed;
        public Vector2 Stick;
    }

    /// <summary>
    /// The controllers through UnityEngine.XR (InputDevices / CommonUsages), as the other Dreamports read them (Unity.InputSystem is not on
    /// the upload server's list): grip and trigger with hysteresis, the face buttons, the thumbstick and haptic impulses. On a Valve Index the
    /// squeeze FORCE is used for the grip when the device offers it (the capacitive touch alone would read as held). Tick() once a frame.
    /// </summary>
    public static class MpXr
    {
        public const float GripOn = 0.55f, GripOff = 0.35f, TriggerOn = 0.5f, TriggerOff = 0.3f;
        public const float IndexForceOn = 0.25f, IndexGripOn = 0.9f;

        public static MpHand Left, Right;

        private static InputDevice _left, _right;
        private static readonly List<InputDevice> Found = new List<InputDevice>();
        private static float _nextScan;
        private static int _frame = -1;
        private static bool _present;
        private static readonly int[] _impulse = new int[2];   // 0 unknown, 1 yes, 2 no
        private static readonly int[] _kind = new int[2];      // 0 not looked, 1 normal, 2 Index with force, 3 Index without force
        private static readonly InputFeatureUsage<float>[] _force = new InputFeatureUsage<float>[2];
        private static readonly List<InputFeatureUsage> Usages = new List<InputFeatureUsage>();
        private static bool _described;

        /// <summary>false = no controller pulses at all (a setting).</summary>
        public static bool HapticsOn = true;

        public static bool Present
        {
            get { Tick(); return _present; }
        }

        public static MpHand Hand(bool right) { Tick(); return right ? Right : Left; }

        public static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            try { _present = XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName); }
            catch (System.Exception) { _present = false; }
            if (Simulated)
            {
                _present = true;
                Left = Build(SimLeft, Left);
                Right = Build(SimRight, Right);
                return;
            }

            if (!_present)
            {
                Left = default(MpHand);
                Right = default(MpHand);
                return;
            }

            if (!_left.isValid || !_right.isValid)
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 1f;
                    _left = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
                    _right = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
                    _impulse[0] = _impulse[1] = 0;
                    _kind[0] = _kind[1] = 0;
                    if (_left.isValid && _right.isValid && !_described)
                    {
                        _described = true;
                        MpLog.Info("VR: controllers " + Describe(_left) + " | " + Describe(_right));
                    }
                }
            }

            Left = Read(_left, 0, Left);
            Right = Read(_right, 1, Right);
        }

        /// <summary>Lab hook: when set, the controllers read from SimLeft / SimRight (only Grip, Trigger, Primary, Secondary, StickClick and Stick are used) instead of the headset.</summary>
        public static bool Simulated;
        public static MpHand SimLeft, SimRight;

        private static MpHand Read(InputDevice d, int h, MpHand was)
        {
            if (!d.isValid) return new MpHand();
            var raw = new MpHand { Grip = Grip(d, h) };
            float tr = 0f;
            d.TryGetFeatureValue(CommonUsages.trigger, out tr);
            bool tbtn;
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out tbtn) && tbtn && tr < 1f) tr = Mathf.Max(tr, TriggerOn + 0.01f);
            raw.Trigger = tr;
            bool bb;
            raw.Primary = d.TryGetFeatureValue(CommonUsages.primaryButton, out bb) && bb;
            raw.Secondary = d.TryGetFeatureValue(CommonUsages.secondaryButton, out bb) && bb;
            raw.StickClick = d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bb) && bb;
            Vector2 st;
            if (d.TryGetFeatureValue(CommonUsages.primary2DAxis, out st)) raw.Stick = st;
            return Build(raw, was);
        }

        /// <summary>The frame state (held with hysteresis, pressed and released edges) from raw values and the last frame.</summary>
        private static MpHand Build(MpHand raw, MpHand was)
        {
            var r = new MpHand();
            r.Grip = raw.Grip;
            r.GripHeld = r.Grip >= (was.GripHeld ? GripOff : GripOn);
            r.GripPressed = r.GripHeld && !was.GripHeld;
            r.GripReleased = !r.GripHeld && was.GripHeld;

            float t = raw.Trigger;
            r.Trigger = t;
            r.TriggerHeld = t >= (was.TriggerHeld ? TriggerOff : TriggerOn);
            r.TriggerPressed = r.TriggerHeld && !was.TriggerHeld;
            r.TriggerReleased = !r.TriggerHeld && was.TriggerHeld;

            r.Primary = raw.Primary;
            r.PrimaryPressed = r.Primary && !was.Primary;
            r.Secondary = raw.Secondary;
            r.SecondaryPressed = r.Secondary && !was.Secondary;
            r.StickClick = raw.StickClick;
            r.StickClickPressed = r.StickClick && !was.StickClick;
            r.Stick = raw.Stick;
            return r;
        }

        private static float Grip(InputDevice device, int h)
        {
            if (_kind[h] == 0) Classify(device, h);
            float value;
            if (!device.TryGetFeatureValue(CommonUsages.grip, out value)) value = 0f;
            if (_kind[h] == 2)
            {
                float f;
                if (!device.TryGetFeatureValue(_force[h], out f)) f = 0f;
                return f >= IndexForceOn ? 1f : f / IndexForceOn * GripOff;
            }

            if (_kind[h] == 3) return value >= IndexGripOn ? 1f : value * GripOff;
            bool down;
            if (device.TryGetFeatureValue(CommonUsages.gripButton, out down) && down) value = Mathf.Max(value, 1f);
            return value;
        }

        private static void Classify(InputDevice device, int h)
        {
            string n = device.name ?? "";
            bool index = n.IndexOf("Index", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Knuckles", System.StringComparison.OrdinalIgnoreCase) >= 0
                         || n.IndexOf("Valve", System.StringComparison.OrdinalIgnoreCase) >= 0;
            _kind[h] = 1;
            if (!index) return;
            _kind[h] = 3;
            Usages.Clear();
            if (device.TryGetFeatureUsages(Usages))
                foreach (InputFeatureUsage u in Usages)
                    if (_kind[h] == 3 && u.type == typeof(float) && u.name.IndexOf("force", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _force[h] = new InputFeatureUsage<float>(u.name);
                        _kind[h] = 2;
                    }

            MpLog.Info("VR: '" + n + "' (" + (h == 1 ? "right" : "left") + ") is a Valve Index - grip from " + (_kind[h] == 2 ? "the squeeze force '" + _force[h].name + "'" : "the grip value only"));
        }

        /// <summary>A short vibration on a hand (nothing when the device has no impulse haptics).</summary>
        public static void Pulse(bool right, float amplitude, float seconds)
        {
            if (!HapticsOn || !Present) return;
            int h = right ? 1 : 0;
            InputDevice device = right ? _right : _left;
            if (!device.isValid) return;
            if (_impulse[h] == 0)
            {
                HapticCapabilities caps;
                _impulse[h] = device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse ? 1 : 2;
            }

            if (_impulse[h] != 1) return;
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), seconds);
        }

        private static InputDevice First(InputDeviceCharacteristics wanted)
        {
            InputDevices.GetDevicesWithCharacteristics(wanted, Found);
            return Found.Count > 0 ? Found[0] : default(InputDevice);
        }

        private static string Describe(InputDevice device)
        {
            float f;
            bool b;
            HapticCapabilities caps;
            bool analog = device.TryGetFeatureValue(CommonUsages.grip, out f);
            bool button = device.TryGetFeatureValue(CommonUsages.gripButton, out b);
            bool trig = device.TryGetFeatureValue(CommonUsages.trigger, out f);
            bool haptics = device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse;
            return "'" + device.name + "' grip(float) " + (analog ? "yes" : "NO") + ", gripButton " + (button ? "yes" : "NO") + ", trigger " + (trig ? "yes" : "NO") + ", haptic impulse " + (haptics ? "yes" : "NO");
        }
    }
}
