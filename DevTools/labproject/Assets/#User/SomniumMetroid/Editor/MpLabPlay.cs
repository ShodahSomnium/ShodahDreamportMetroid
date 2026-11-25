using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "play": the monitor game as a player sees it - the lab camera is the head (MainCamera), the cannon by the eye,
    /// the helmet HUD. Fires a shot, charges and lets go, fires a missile, cycles the beams and the visors (scan: looks for scan
    /// points), and writes a picture of each moment plus the sound counters (effects started / missing, music).
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private bool _labFire, _labMissile;

        partial void ExtraScenarioDoors(string scenario, ref IEnumerator run);
        partial void ExtraScenarioGun(string scenario, ref IEnumerator run);
        partial void ExtraScenarioEnemyA(string scenario, ref IEnumerator run);
        partial void ExtraScenarioEnemyB(string scenario, ref IEnumerator run);
        partial void ExtraScenarioEnemyC(string scenario, ref IEnumerator run);
        partial void ExtraScenarioScripts(string scenario, ref IEnumerator run);
        partial void ExtraScenarioTallon(string scenario, ref IEnumerator run);
        partial void ExtraScenarioEscape(string scenario, ref IEnumerator run);
        partial void ExtraScenarioDeaths(string scenario, ref IEnumerator run);
        partial void ExtraScenarioCoop(string scenario, ref IEnumerator run);
        partial void ExtraScenarioCoopCannon(string scenario, ref IEnumerator run);

        partial void ExtraScenarioImpl(string scenario, ref IEnumerator run)
        {
            if (scenario == "play") run = PlayScenario();
            ExtraScenarioDoors(scenario, ref run);
            ExtraScenarioGun(scenario, ref run);
            ExtraScenarioEnemyA(scenario, ref run);
            ExtraScenarioEnemyB(scenario, ref run);
            ExtraScenarioEnemyC(scenario, ref run);
            ExtraScenarioScripts(scenario, ref run);
            ExtraScenarioTallon(scenario, ref run);
            ExtraScenarioEscape(scenario, ref run);
            ExtraScenarioDeaths(scenario, ref run);
            ExtraScenarioCoop(scenario, ref run);
            ExtraScenarioCoopCannon(scenario, ref run);
            if (scenario == "tablet") run = MpLabTablet.Run(this);
        }

        private IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private IEnumerator Seconds(float s) { float t0 = Time.time; while (Time.time - t0 < s) yield return null; }

        /// <summary>The reticle's report, and a picture of it alone on black (from the lab camera).</summary>
        private void ReticleShot(string file)
        {
            if (G.Reticle == null) { Log("reticle MISSING"); return; }
            Log(G.Reticle.Report(_cam));
            const int layer = 30;
            G.Reticle.SetLayer(layer);
            var only = new GameObject("Lab Reticle Camera").AddComponent<Camera>();
            only.CopyFrom(_cam);
            only.enabled = false;
            only.clearFlags = CameraClearFlags.SolidColor;
            only.backgroundColor = Color.black;
            only.cullingMask = 1 << layer;
            Shoot(only, file, 1280, 720);
            Object.Destroy(only.gameObject);
            G.Reticle.SetLayer(0);
        }

        /// <summary>A picture of the cannon from its left and a little above (a second camera; the HUD is not in its view).</summary>
        private void GunShot(string file)
        {
            if (G.Cannon == null || G.Cannon.Root == null) return;
            var side = new GameObject("Lab Gun Camera").AddComponent<Camera>();
            side.nearClipPlane = 0.01f;
            side.fieldOfView = 40f;
            side.enabled = false;
            Transform gun = G.Cannon.Root;
            side.transform.position = gun.position - gun.right * 0.45f + gun.up * 0.12f + gun.forward * 0.15f;
            side.transform.LookAt(gun.position + gun.forward * 0.15f);
            side.cullingMask = ~(1 << 5);
            Shoot(side, file, 960, 720);
            Object.Destroy(side.gameObject);
        }

        private IEnumerator PlayScenario()
        {
            Vector3 spawn = G.PlayerGame;
            MpGame.LabFocus = spawn;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            MpGame.LabMissile = g => { bool m = _labMissile; _labMissile = false; return m; };
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Vector3 eye = spawn + new Vector3(0, 0, MpSpace.SamusEyeHeight);
            // the start area opens with the intro cutscene (input off, as in the game): skipped, as a player may
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            Aim(eye, 0f, 3f);
            yield return Frames(30);
            Log("cannon " + (G.Cannon != null ? "built" : "MISSING") + ", HUD " + (G.Hud != null && G.Hud.Built ? "built" : "MISSING") + ", sound " + (G.Audio != null ? "ready" : "MISSING") + ", energy " + G.State.Energy + ", missiles " + G.State.Missiles);
            Shoot(_cam, "play_idle.png", 1280, 720);
            ReticleShot("play_reticle_alone.png");

            // the cannon close up, from its left and a little above (a second camera; the HUD is not in its view)
            GunShot("play_gun_close.png");

            // one power beam shot: the muzzle flash shows for 1/16 s (the lab clock runs at 60 Hz for it)
            float dtWas = Time.deltaTime;
            Time.captureDeltaTime = 1f / 60f;
            _labFire = true;
            yield return null;
            _labFire = false;
            yield return null;
            GunShot("play_gun_muzzle.png");
            Log("frame time before the 60 Hz clock " + dtWas.ToString("0.000") + " s");
            Time.captureDeltaTime = 0f;
            yield return Frames(3);
            Shoot(_cam, "play_fire.png", 1280, 720);
            yield return Seconds(0.6f);
            Log("shots fired " + G.Shots.Fired + ", hits " + G.Shots.Hits + ", effects live " + MpEffects.LiveCount + ", shots drawn " + MpEffects.Projectiles.Count);

            // charge (the charge effect from 1 s, full at 1.8 s with the aux muzzle) and release
            _labFire = true;
            yield return Seconds(0.5f);
            Log("charge at 0.5 s " + G.Cannon.Charge.ToString("0.00"));
            GunShot("play_charge_05.png");
            yield return Seconds(0.75f);
            Log("charge at 1.25 s " + G.Cannon.Charge.ToString("0.00"));
            GunShot("play_charge_12.png");
            Shoot(_cam, "play_charge_12_view.png", 1280, 720);
            yield return Seconds(0.85f);
            Log("charge at 2.1 s " + G.Cannon.Charge.ToString("0.00") + ", effects live " + MpEffects.LiveCount);
            GunShot("play_charge_full.png");
            ReticleShot("play_reticle_charged.png");
            Shoot(_cam, "play_charge.png", 1280, 720);
            _labFire = false;
            yield return Frames(4);
            Shoot(_cam, "play_charged_shot.png", 1280, 720);
            yield return Frames(8);
            Shoot(_cam, "play_charged_shot_2.png", 1280, 720);
            yield return Seconds(0.8f);

            // a missile
            _labMissile = true;
            yield return Frames(6);
            Shoot(_cam, "play_missile.png", 1280, 720);
            yield return Seconds(1f);
            Log("shots fired " + G.Shots.Fired + ", hits " + G.Shots.Hits + ", missiles left " + G.State.Missiles);

            // beams
            int[] beams = { MpPlayerState.BeamWave, MpPlayerState.BeamIce, MpPlayerState.BeamPlasma, MpPlayerState.BeamPower };
            string[] names = { "wave", "ice", "plasma", "power" };
            for (int i = 0; i < beams.Length; i++)
            {
                G.Cannon.ChangeBeam(beams[i]);
                yield return Seconds(0.5f);
                _labFire = true;
                yield return Frames(2);
                _labFire = false;
                yield return Frames(5);
                Shoot(_cam, "play_beam_" + names[i] + ".png", 1280, 720);
                yield return Seconds(0.3f);
            }

            // visors
            G.SetVisor(MpPlayerState.VisorThermal);
            yield return Seconds(0.5f);
            Shoot(_cam, "play_visor_thermal.png", 1280, 720);
            G.SetVisor(MpPlayerState.VisorXRay);
            yield return Seconds(0.5f);
            Shoot(_cam, "play_visor_xray.png", 1280, 720);
            G.SetVisor(MpPlayerState.VisorScan);
            yield return Seconds(0.5f);
            int points = 0;
            foreach (MpEntity e in G.Scripts.All) if (e is MpScanPointEntity && e.Active) points++;
            Log("scan points active in the loaded areas: " + points);
            Shoot(_cam, "play_visor_scan.png", 1280, 720);
            // look at the nearest scan point and hold fire for the scan time
            MpScanPointEntity near = null;
            float nearD = float.MaxValue;
            foreach (MpEntity e in G.Scripts.All)
            {
                var sp = e as MpScanPointEntity;
                if (sp == null || !sp.Active) continue;
                Vector3 p = sp.Follow != null ? sp.Follow.position : MpSpace.ToUnity(sp.GamePos);
                float d = (p - _cam.transform.position).magnitude;
                if (d < nearD) { nearD = d; near = sp; }
            }

            if (near != null)
            {
                Vector3 p = near.Follow != null ? near.Follow.position : MpSpace.ToUnity(near.GamePos);
                _cam.transform.rotation = Quaternion.LookRotation(p - _cam.transform.position);
                Log("scanning '" + near.Name + "' " + nearD.ToString("0.0") + " m away");
            }

            yield return Frames(3);
            _labFire = true;
            yield return Seconds(3.4f);
            _labFire = false;
            yield return Frames(3);
            Shoot(_cam, "play_scan_result.png", 1280, 720);
            G.SetVisor(MpPlayerState.VisorCombat);
            yield return Seconds(0.5f);
            G.Hud.ShowMessage("LAB MESSAGE LINE", 5f);
            yield return Frames(3);
            Shoot(_cam, "play_message.png", 1280, 720);

            // look around once more with the HUD on
            for (int d = 1; d < 4; d++)
            {
                Aim(eye, d * 90f, 3f);
                yield return Frames(3);
                Shoot(_cam, "play_look_" + d + ".png", 1280, 720);
            }

            yield return Seconds(1f);
            Log("sound: effects started " + G.Audio.Started + ", missing " + G.Audio.Missing + ", dsp time " + AudioSettings.dspTime.ToString("0.00") + ", output rate " + AudioSettings.outputSampleRate);
            MpGame.LabFire = null;
            MpGame.LabMissile = null;
        }
    }
}
