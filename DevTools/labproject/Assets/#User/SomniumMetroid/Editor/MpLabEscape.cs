using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "escape" (-mparea 5): the frigate's last stretch walked, never warped. Samus starts at the bottom of the
    /// Connection Elevator to Deck Alpha, steps onto its trigger and rides it up, walks through the Deck Alpha Mech Shaft (whose
    /// trigger switches on the hangar's ending layer and the Air Lock's flames), the Air Lock (its doors shot open) into the
    /// Exterior Docking Hangar and into 'Trigger - Start Closing Cinema'. The ending's cameras play on the cinema screen
    /// (not skipped) until the world teleporter lands her on Tallon IV.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioEscape(string scenario, ref IEnumerator run)
        {
            if (scenario == "escape") run = EscapeScenario();
        }

        private T FindNamed<T>(int area, string name) where T : MpEntity
        {
            foreach (MpEntity e in G.Scripts.All) if (e.Area == area && e.Name == name && e is T) return (T)e;
            return null;
        }

        private IEnumerator EscapeScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            yield return Settle();
            MpGame.LabFocus = G.PlayerGame;
            Log("escape: start in '" + G.World.Areas[G.WorldView.CurrentArea].Name + "' at " + G.PlayerGame);

            // the elevator: onto its trigger (Timer1 switches it on a second after the room loads)
            MpTriggerEntity lift = FindNamed<MpTriggerEntity>(5, "Trigger Elevator");
            if (lift == null) { Log("escape: no elevator trigger"); _failed = true; yield break; }
            // the panel's scan starts the suit-loss cutscene; its end arms the elevator (Timer1)
            MpScanPointEntity panel = null;
            foreach (MpEntity e in G.Scripts.All) { var s = e as MpScanPointEntity; if (s != null && e.Area == 5 && e.Name.Contains("elev access")) panel = s; }
            Log("escape: elevator panel scan " + (panel != null ? "'" + panel.Name + "'" : "MISSING"));
            if (panel != null)
            {
                G.SendScanState(panel, MpScriptMsg.StScanDone);
                float tc = Time.time;
                string lastCam = null;
                while (Time.time - tc < 60f && (MpCinematics.Active || Time.time - tc < 1f))
                {
                    string now = MpCinematics.Active ? MpCinematics.Current.Name : null;
                    if (now != lastCam) { lastCam = now; Log("escape: suit-loss camera " + (now ?? "none")); }
                    yield return null;
                }

                yield return Seconds(1.5f);
                Log("escape: suit-loss cutscene over after " + (Time.time - tc).ToString("0") + " s; elevator trigger active " + lift.Active);
            }

            Vector3 c = (lift.Min + lift.Max) * 0.5f;
            yield return WalkTo(new Vector3(c.x, c.y, lift.Min.z + 0.1f), "elevator");
            yield return Seconds(1.5f);
            Log("escape: on the elevator trigger (active " + lift.Active + "), rides so far " + G.RideCount);
            float t0 = Time.time;
            int rides = G.RideCount;
            float last = Time.time;
            while (Time.time - t0 < 60f)
            {
                if (G.RideCount != rides) { rides = G.RideCount; last = Time.time; }
                if (rides > 0 && Time.time - last > 2f) break;
                yield return null;
            }

            Log("escape: elevator stopped after " + (Time.time - t0).ToString("0") + " s, " + G.RideCount + " ride frames, Samus at " + G.PlayerGame + " in '" + G.World.Areas[Mathf.Max(0, G.WorldView.CurrentArea)].Name + "'");

            // through the docks: the elevator room's top dock, the Mech Shaft's dock to the Air Lock, the Air Lock's dock to the hangar
            yield return WalkTo(DockFoot(5, 1), "Mech Shaft door");
            foreach (MpEntity e in G.Scripts.All) { var tr = e as MpTriggerEntity; if (tr != null && e.Area == 3) Log("escape: Mech Shaft trigger '" + e.Name + "' active " + e.Active + " box " + tr.Min + ".." + tr.Max); }
            MpTriggerEntity shaft = FindNamed<MpTriggerEntity>(3, "Trigger - Load/Unload Areas");
            if (shaft != null) { Vector3 sc = (shaft.Min + shaft.Max) * 0.5f; yield return WalkTo(new Vector3(sc.x, sc.y, shaft.Min.z + 0.1f), "the Mech Shaft's load/unload trigger"); yield return Seconds(0.5f); }
            yield return Seconds(2f);
            MpEntity hatch3 = FindNamed<MpEntity>(3, "Platform - 00h Hatch Closed"), hatch1 = FindNamed<MpEntity>(1, "Platform - Hatch Closed");
            Log("escape: hatches - Mech Shaft 'Platform - 00h Hatch Closed' " + (hatch3 == null ? "not there" : hatch3.Active ? "STILL SOLID" : "open") + ", Air Lock 'Platform - Hatch Closed' " + (hatch1 == null ? "not there" : hatch1.Active ? "STILL SOLID" : "open"));
            if (hatch3 != null && hatch3.Active) { Log("escape: STUCK - the Mech Shaft hatch stays shut"); _failed = true; }
            yield return WalkTo(DockFoot(3, 1) + new Vector3(-2f, 0f, 0f), "the Air Lock hatch");
            yield return Seconds(1f);
            hatch1 = FindNamed<MpEntity>(1, "Platform - Hatch Closed");
            Log("escape: Air Lock hatch " + (hatch1 == null ? "not there" : hatch1.Active ? "STILL SOLID" : "open"));
            if (hatch1 != null && hatch1.Active) { Log("escape: STUCK - the Air Lock hatch stays shut"); _failed = true; }
            yield return WalkTo(DockFoot(3, 1), "Air Lock door");
            yield return Seconds(2f);
            Log("escape: layers now - hangar '" + LayerList(0) + "', Air Lock '" + LayerList(1) + "'");
            yield return WalkTo(DockFoot(1, 1), "hangar door");

            // the hangar, rebuilt with its 'Intro Closing' layer: the ending's trigger
            MpTriggerEntity end = null;
            t0 = Time.time;
            while (end == null && Time.time - t0 < 30f) { end = FindNamed<MpTriggerEntity>(0, "Trigger - Start Closing Cinema"); yield return null; }
            if (end == null) { Log("escape: STUCK - the hangar has no 'Trigger - Start Closing Cinema' (layer 2 never built)"); _failed = true; yield break; }
            c = (end.Min + end.Max) * 0.5f;
            yield return WalkTo(new Vector3(c.x, c.y, end.Min.z + 0.1f), "closing cinema trigger");
            Log("escape: in the closing trigger (active " + end.Active + ")");

            // the ending plays out (no skip)
            string cam = null;
            int pic = 0;
            t0 = Time.time;
            while (G.CurrentPak != MpGame.TallonPak && Time.time - t0 < 240f)
            {
                string now = MpCinematics.Active ? MpCinematics.Current.Name : null;
                if (now != cam)
                {
                    cam = now;
                    Log("escape " + (Time.time - t0).ToString("0.0") + " s: camera " + (cam ?? "none"));
                    if (cam != null && pic < 6) { yield return Frames(20); Aim(PlayerEye(), 0f, 0f); Shoot(_cam, "escape_cine_" + pic++ + ".png", 960, 540); }
                }

                yield return null;
            }

            yield return Wait(() => G.CurrentPak == MpGame.TallonPak && !G.SwitchingWorld, 120f, "Tallon IV");
            if (_failed) { Log("escape: STUCK - no world switch (cinematic " + (MpCinematics.Active ? MpCinematics.Current.Name : "none") + ")"); yield break; }
            yield return Settle();
            Log("escape: ARRIVED in '" + G.World.Areas[G.WorldView.CurrentArea].Name + "' after the ending (" + (Time.time - t0).ToString("0") + " s), energy " + G.State.Energy + ", missiles " + G.State.Missiles + "/" + G.State.MissileCapacity);
            Aim(PlayerEye(), 0f, 5f);
            Shoot(_cam, "escape_landing.png", 960, 540);
        }

        private string LayerList(int area)
        {
            MpWorld.Area a = G.World.Areas[area];
            var on = new List<string>();
            for (int i = 0; i < a.LayerNames.Count; i++) if ((a.LayerBits & (1UL << i)) != 0) on.Add(a.LayerNames[i]);
            return string.Join("|", on.ToArray());
        }

        /// <summary>The floor point in the middle of a dock (the door between two rooms).</summary>
        private Vector3 DockFoot(int area, int dock)
        {
            MpWorld.Area a = G.World.Areas[area];
            Vector3 s = Vector3.zero;
            float bottom = float.MaxValue;
            foreach (float[] v in a.Docks[dock].Plane) { s += new Vector3(v[0], v[1], v[2]); bottom = Mathf.Min(bottom, v[2]); }
            s /= a.Docks[dock].Plane.Count;
            return new Vector3(s.x, s.y, bottom + 0.1f);
        }

        /// <summary>Walks the lab's stand-in player in a straight line at running speed (rooms stream as she goes); a closed door
        /// within reach ahead is shot open first (its lock, the DamageableTrigger beside it, takes a Power Beam shot).</summary>
        private IEnumerator WalkTo(Vector3 goal, string what)
        {
            const float speed = 8f;
            float t0 = Time.time;
            while (Time.time - t0 < 40f)
            {
                Vector3 p = MpGame.LabFocus ?? G.PlayerGame;
                Vector3 d = goal - p;
                if (d.magnitude < 0.2f) break;
                ShootDoorsNear(p, 8f);
                Vector3 stepTo = p + d.normalized * Mathf.Min(d.magnitude, 1f);
                foreach (float hgt in new[] { 0.6f, 1.6f })
                {
                    Vector3 a = MpSpace.ToUnity(p + new Vector3(0f, 0f, hgt)), b = MpSpace.ToUnity(stepTo + new Vector3(0f, 0f, hgt));
                    RaycastHit hit;
                    if (MpEnemy.WorldRay(a, (b - a).normalized, (b - a).magnitude, out hit) && _blockers.Add(hit.collider))
                        Log("escape: BLOCKED walking to " + what + " at " + p + " by '" + hit.collider.name + "' (parent '" + (hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "-") + "', enabled " + hit.collider.enabled + ")");
                }
                MpGame.LabFocus = p + d.normalized * Mathf.Min(d.magnitude, speed * Time.deltaTime);
                Aim(MpGame.LabFocus.Value + new Vector3(0, 0, MpSpace.SamusEyeHeight), Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg, 0f);
                yield return null;
            }

            Log("escape: reached " + what + " at " + MpGame.LabFocus + " in '" + G.World.Areas[Mathf.Max(0, G.WorldView.CurrentArea)].Name + "'");
        }

        private readonly HashSet<Collider> _blockers = new HashSet<Collider>();

        private readonly HashSet<MpEntity> _shotLocks = new HashSet<MpEntity>();

        private void ShootDoorsNear(Vector3 p, float reach)
        {
            foreach (MpEntity e in G.Scripts.All.ToArray())
            {
                var lk = e as MpDamageableTriggerEntity;
                if (lk == null || !lk.Active || _shotLocks.Contains(lk)) continue;
                Vector3 c = (lk.Min + lk.Max) * 0.5f;
                if ((c - p).magnitude > reach) continue;
                _shotLocks.Add(lk);
                bool hit = lk.TakeDamage(MpPlayerState.BeamPower, false, 10f);
                Log("escape: shot '" + lk.Name + "' in area " + lk.Area + " (" + (hit ? "hit" : "no effect") + ")");
            }
        }
    }
}
