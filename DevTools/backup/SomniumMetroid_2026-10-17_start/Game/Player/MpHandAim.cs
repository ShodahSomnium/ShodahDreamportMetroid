using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The direction a hand POINTS, taken from the Somnium client's own laser. The Bridge's hand anchors are the controller's grip pose, whose axes
    /// are turned an unknown amount from the pointing direction (a gun that follows them points 90 degrees up), so the turn is MEASURED instead of
    /// assumed (the Rec Room Dreamport's way): the client's laser is the Near-Far interactor on the hand (its far caster, castDistance 5 m, from the
    /// stabilized cast origin; the grab interactor on the same hand reaches 1 m), and the rotation between the hand anchor and the laser's origin
    /// is a constant of the controller. It is smoothed while the hand is slow (the origin is stabilized, so it lags in fast turns) and applied to the
    /// live anchor, so the gun follows the hand without the laser's lag. Reads only: nothing of the client is changed (the interactors are found
    /// by type, the way the WoW Dreamport finds its laser).
    /// </summary>
    public sealed class MpLaserAim
    {
        private sealed class Side
        {
            public NearFarInteractor Laser;
            public Quaternion Offset = Quaternion.identity;
            public bool Have;
            public int Samples;
            public bool Logged;
            public Quaternion LastHand = Quaternion.identity;
            public bool HaveLast;
            public float NextSearch;
        }

        private const float NearHand = 0.5f;             // an interactor never sits further than this from its own hand
        private const float SlowDegPerSec = 70f;         // measure only while the hand turns slower than this
        private const float Blend = 0.06f;
        private const int FirstSamples = 20;
        private readonly Side[] _s = { new Side(), new Side() };

        /// <summary>True when the hand's pointing direction is measured from the laser.</summary>
        public bool Has(bool right) { return _s[right ? 1 : 0].Have; }

        /// <summary>The hand anchor's rotation turned to where the laser points (forward = the laser, up = the controller's top, right = to the right). False until the laser has been measured.</summary>
        public bool TryAim(bool right, Transform hand, out Quaternion aim)
        {
            aim = Quaternion.identity;
            if (hand == null) return false;
            Side s = _s[right ? 1 : 0];
            aim = hand.rotation;
            Measure(right, hand, s);
            if (!s.Have) return false;
            aim = hand.rotation * s.Offset;
            return true;
        }

        private void Measure(bool right, Transform hand, Side s)
        {
            try
            {
                // the hand's turning speed (the laser's origin is stabilized and lags: only a slow hand gives a clean reading)
                bool slow = true;
                if (s.HaveLast)
                {
                    float dt = Mathf.Max(0.0005f, Time.deltaTime);
                    slow = Quaternion.Angle(s.LastHand, hand.rotation) / dt < SlowDegPerSec;
                }

                s.LastHand = hand.rotation;
                s.HaveLast = true;
                if (s.Laser != null && (!s.Laser.isActiveAndEnabled || (s.Laser.transform.position - hand.position).sqrMagnitude > NearHand * NearHand)) s.Laser = null;
                if (s.Laser == null)
                {
                    if (Time.unscaledTime >= s.NextSearch) Search(right, hand, s);
                    if (s.Laser == null) return;
                }

                Transform o = null;
                ICurveInteractionCaster far = s.Laser.farInteractionCaster;
                if (far != null) o = far.effectiveCastOrigin != null ? far.effectiveCastOrigin : far.castOrigin;
                if (o == null) o = s.Laser.transform;
                if (!slow && s.Have) return;
                Quaternion cur = Quaternion.Inverse(hand.rotation) * o.rotation;
                if (!s.Have) { s.Offset = cur; s.Have = true; }
                else s.Offset = Quaternion.Slerp(s.Offset, cur, Blend);
                s.Samples++;
                if (!s.Logged && s.Samples >= FirstSamples)
                {
                    s.Logged = true;
                    Vector3 e = s.Offset.eulerAngles;
                    MpLog.Info("VR: the " + (right ? "right" : "left") + " hand points along the client's laser ('" + s.Laser.name + "'): the anchor is turned (" + e.x.ToString("0") + ", " + e.y.ToString("0") + ", " + e.z.ToString("0") + ") degrees from it");
                }
            }
            catch (Exception e)
            {
                s.Laser = null;
                s.NextSearch = Time.unscaledTime + 5f;
                if (!s.Logged) { s.Logged = true; MpLog.Warn("VR: reading the client's laser failed (" + e.GetType().Name + ": " + e.Message + "): the hand frame is estimated"); }
            }
        }

        /// <summary>
        /// The laser among the Near-Far interactors near the hand: far casting on, named for the hand's own side when the names say so ("Near-Far Interactor Left" /
        /// "... Right": the other side's interactor sits within half a metre of a hand held beside it, as in a two-handed grip), else the one nearest the hand,
        /// with the longest far reach first (the Somnium pointer reaches 5 m, the grab interactor 1 m).
        /// </summary>
        private void Search(bool right, Transform hand, Side s)
        {
            s.NextSearch = Time.unscaledTime + 2f;
            NearFarInteractor best = null;
            float bestReach = 0f, bestDist = float.MaxValue;
            bool bestSide = false;
            string mine = right ? "right" : "left", other = right ? "left" : "right";
            NearFarInteractor[] all = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; all != null && i < all.Length; i++)
            {
                NearFarInteractor c = all[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                string nm = (c.name ?? "").ToLowerInvariant();
                if (nm.Contains(other) && !nm.Contains(mine)) continue;            // the other hand's
                float d = (c.transform.position - hand.position).magnitude;
                if (d > NearHand) continue;
                CurveInteractionCaster far = c.farInteractionCaster as CurveInteractionCaster;
                if (far == null || !c.enableFarCasting) continue;
                float reach = far.castDistance;
                bool side = nm.Contains(mine);
                bool better = best == null || (side && !bestSide) || (side == bestSide && (reach > bestReach + 0.01f || (Mathf.Abs(reach - bestReach) <= 0.01f && d < bestDist)));
                if (!better) continue;
                best = c; bestReach = reach; bestDist = d; bestSide = side;
            }

            s.Laser = best;
        }
    }
}
