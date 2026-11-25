using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "gate": the hangar's force-field gate played the game's way. Shoots the four green switches
    /// (DamageableTrigger1-4: their counter switches on the "Hanger panel to open gate" scan point), scans that panel with the scan
    /// visor (hold fire), waits for the timers to switch DamageableTrigger2_1-6 on, shoots those, and logs the force field
    /// ('shield') and its trigger before and after.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private MpEntity Named(string name)
        {
            foreach (MpEntity e in G.Scripts.All) if (e.Name == name) return e;
            return null;
        }

        private void GateState(string when)
        {
            MpEntity panel = null, shield = Named("shield"), trig = Named("shield_trigger");
            foreach (MpEntity e in G.Scripts.All) if (e is MpScanPointEntity && e.Name.IndexOf("panel to open gate", System.StringComparison.OrdinalIgnoreCase) >= 0) panel = e;
            var counter = G.Scripts.Find(0x27) as MpCounterEntity;
            Log(when + ": panel " + (panel != null ? panel.Active.ToString() : "missing") + ", counter " + (counter != null ? counter.Current.ToString() : "missing")
                + ", shield " + (shield != null ? shield.Active.ToString() : "missing") + ", shield_trigger " + (trig != null ? trig.Active.ToString() : "missing"));
        }

        private IEnumerator ShootSwitch(MpDamageableTriggerEntity dt, string picture)
        {
            Vector3 size = dt.Max - dt.Min, c = (dt.Max + dt.Min) * 0.5f;
            Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            float side = 1f;
            foreach (float s in new[] { 1f, -1f })
            {
                Vector3 e2 = MpSpace.ToUnity(c + axis * s * 6f), dir = MpSpace.ToUnity(c) - e2;
                RaycastHit first;
                bool blocked = MpProjectiles.ShotRay(e2, dir.normalized, dir.magnitude, out first) && first.collider.GetComponentInParent<MpHitTarget>() == null;
                if (!blocked) { side = s; break; }
            }

            Vector3 eye = c + axis * side * 6f;
            MpGame.LabFocus = eye - new Vector3(0, 0, MpSpace.SamusEyeHeight);
            _cam.transform.position = MpSpace.ToUnity(eye);
            _cam.transform.rotation = Quaternion.LookRotation(MpSpace.ToUnity(c) - _cam.transform.position);
            yield return Frames(3);
            _labFire = true;
            yield return Frames(2);
            _labFire = false;
            yield return Seconds(0.8f);
            Log("  " + dt + ": health " + dt.HealthNow + " active " + dt.Active + " (last hit " + G.Shots.LastHit + ")");
            if (picture != null) Shoot(_cam, picture, 960, 540);
        }

        private IEnumerator GateScenario()
        {
            MpGame.LabFocus = G.PlayerGame;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            yield return Frames(20);
            GateState("start");

            // 1. the four green switches
            for (int i = 1; i <= 4; i++)
            {
                var dt = Named("DamageableTrigger" + i) as MpDamageableTriggerEntity;
                if (dt == null || !dt.Active) { Log("DamageableTrigger" + i + " " + (dt == null ? "missing" : "inactive")); continue; }
                yield return ShootSwitch(dt, null);
            }

            yield return Seconds(1f);
            GateState("after the four green switches");

            // 2. scan the panel with the scan visor
            MpScanPointEntity panel = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                var s = e as MpScanPointEntity;
                if (s != null && s.Name.IndexOf("panel to open gate", System.StringComparison.OrdinalIgnoreCase) >= 0) panel = s;
            }

            if (panel != null && panel.Active)
            {
                Vector3 p = panel.Follow != null ? panel.Follow.position : MpSpace.ToUnity(panel.GamePos);
                Vector3 from = p + (MpSpace.ToUnity(G.PlayerGame) - p).normalized * 4f;
                from.y = p.y;
                MpGame.LabFocus = MpSpace.ToGame(from) - new Vector3(0, 0, MpSpace.SamusEyeHeight);
                _cam.transform.position = from;
                _cam.transform.rotation = Quaternion.LookRotation(p - from);
                G.SetVisor(MpPlayerState.VisorScan);
                yield return Seconds(0.5f);
                _labFire = true;
                yield return Seconds(4f);
                _labFire = false;
                yield return Frames(3);
                Log("panel scanned " + panel.Scanned);
                Shoot(_cam, "gate_scan.png", 960, 540);
                G.SetVisor(MpPlayerState.VisorCombat);
                yield return Seconds(8f);
            }

            GateState("after the scan");

            // 3. the six switches the scan switches on (they come on one after another)
            for (int round = 0; round < 3; round++)
            {
                for (int i = 1; i <= 6; i++)
                {
                    var dt = Named("DamageableTrigger2_" + i) as MpDamageableTriggerEntity;
                    if (dt == null || !dt.Active) continue;
                    yield return ShootSwitch(dt, "gate_switch_" + i + ".png");
                }

                yield return Seconds(3f);
            }

            GateState("end");
            MpGame.LabFire = null;
        }
    }
}
