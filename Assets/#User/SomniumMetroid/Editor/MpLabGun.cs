using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "gun": the animated arm cannon close up. For every beam: its base pose from the left, from behind (the open back
    /// the suit forearm closes) and from above; then on the Power Beam a shot, the charge-up, the charge loop, the charged shot, a
    /// missile (shoot, reload, the hatch closing on a fire press), a beam change half way through, and a fidget. Logs every beam's
    /// character report and where its LBEAM / elbow locators are.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioGun(string scenario, ref IEnumerator run)
        {
            if (scenario == "gun") run = GunScenario();
        }

        private void GunCam(string file, Vector3 fromLocal, Vector3 lookLocal, float fov = 40f)
        {
            if (G.Cannon == null || G.Cannon.Root == null) return;
            var c = new GameObject("Lab Gun Camera").AddComponent<Camera>();
            c.nearClipPlane = 0.01f;
            c.fieldOfView = fov;
            c.enabled = false;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
            Transform gun = G.Cannon.Root;
            c.transform.position = gun.TransformPoint(fromLocal);
            c.transform.LookAt(gun.TransformPoint(lookLocal), gun.up);
            c.cullingMask = ~(1 << 5);
            Shoot(c, file, 960, 720);
            Object.Destroy(c.gameObject);
        }

        private void GunViews(string tag)
        {
            GunCam("gun_" + tag + "_side.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            GunCam("gun_" + tag + "_back.png", new Vector3(0.08f, 0.12f, -0.45f), new Vector3(0f, 0f, 0.05f));
            GunCam("gun_" + tag + "_top.png", new Vector3(0.02f, 0.5f, 0.1f), new Vector3(0f, 0f, 0.1f));
        }

        private IEnumerator GunScenario()
        {
            Vector3 spawn = G.PlayerGame;
            MpGame.LabFocus = spawn;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            MpGame.LabMissile = g => { bool m = _labMissile; _labMissile = false; return m; };
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            Vector3 eye = spawn + new Vector3(0, 0, MpSpace.SamusEyeHeight);
            Aim(eye, 0f, 3f);
            yield return Frames(30);
            if (G.Cannon == null) { Log("cannon MISSING"); yield break; }
            for (int b = 0; b < 4; b++)
            {
                MpGunAssets.Beam beam = G.GunAssets.Beams[b];
                Log("beam " + b + " " + (beam != null ? beam.Name + ": " + (beam.Char != null ? beam.Char.Report : "NO CHARACTER") : "missing"));
            }

            for (int i = 0; i < 4; i++) Log("suit arm " + MpGunAssets.SuitArmNames[i] + " " + (G.GunAssets.SuitArms[i] != null ? "loaded" : "MISSING"));
            Log("suit " + G.State.Suit);

            string[] names = { "power", "ice", "wave", "plasma" };
            int[] order = { 0, 2, 1, 3 };
            foreach (int b in order)
            {
                if (b != G.State.Beam) { G.Cannon.ChangeBeam(b); yield return Seconds(1.2f); }
                yield return Frames(4);
                Log(names[b] + ": muzzle " + G.Cannon.Muzzle.ToString("F3") + " (root " + G.Cannon.Root.position.ToString("F3") + ")");
                GunViews(names[b]);
                // the material's animated textures (the plasma's flowing lava): two pictures 0.1 s apart must differ
                Time.captureDeltaTime = 1f / 60f;
                GunCam("gun_" + names[b] + "_uv0.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
                yield return Frames(6);
                GunCam("gun_" + names[b] + "_uv1.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
                Time.captureDeltaTime = 0f;
                // the beam's own effects: charging (Power2nd / Ice2nd / Wave2nd arcs / Plasma2nd, the ice smoke), then a normal shot
                Time.captureDeltaTime = 1f / 60f;
                _labFire = true;
                yield return Frames(110);
                GunCam("gun_" + names[b] + "_charging.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
                _labFire = false;
                yield return Frames(70);
                int before = G.Shots.Fired;
                _labFire = true;
                yield return null;
                _labFire = false;
                yield return Frames(10);
                Log(names[b] + ": a normal shot made " + (G.Shots.Fired - before) + " projectile(s)");
                Shoot(_cam, "gun_" + names[b] + "_shot.png", 960, 540);
                yield return Frames(40);
                Time.captureDeltaTime = 0f;
            }

            G.Cannon.ChangeBeam(0);
            yield return Seconds(1.2f);

            Time.captureDeltaTime = 1f / 60f;
            // a shot: the shoot animation
            _labFire = true;
            yield return null;
            _labFire = false;
            yield return Frames(6);
            GunCam("gun_anim_shoot.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(40);

            // the charge: charge-up from 0.14, the loop from 0.56 of the factor
            _labFire = true;
            yield return Frames(45);
            Log("charge " + G.Cannon.Charge.ToString("0.00") + " (charge-up)");
            GunCam("gun_anim_chargeup.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(80);
            Log("charge " + G.Cannon.Charge.ToString("0.00") + " (loop)");
            GunCam("gun_anim_chargeloop.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            GunViews("charged");
            _labFire = false;
            yield return Frames(8);
            GunCam("gun_anim_chargeshoot.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(60);

            // a missile: shoot, reload, then a fire press closes the hatch
            _labMissile = true;
            yield return Frames(12);
            GunCam("gun_anim_missile.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(45);
            GunCam("gun_anim_reload.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(60);
            _labFire = true;
            yield return null;
            _labFire = false;
            yield return Frames(10);
            GunCam("gun_anim_missileaway.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(40);
            Log("missiles left " + G.State.Missiles + ", shots fired " + G.Shots.Fired);

            // a beam change half way
            G.Cannon.ChangeBeam(2);
            yield return Frames(12);
            GunCam("gun_anim_change.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            yield return Frames(60);
            Time.captureDeltaTime = 0f;

            // idle until a fidget plays
            yield return Seconds(11.5f);
            GunCam("gun_anim_fidget.png", new Vector3(-0.5f, 0.1f, 0.12f), new Vector3(0f, 0f, 0.12f));
            Shoot(_cam, "gun_view.png", 1280, 720);
            MpGame.LabFire = null;
            MpGame.LabMissile = null;
        }
    }
}
