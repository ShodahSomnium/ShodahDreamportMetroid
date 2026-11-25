using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenarios "save" (-mparea 27, the frigate's save station: step in, wait for the save cutscene to end, then fire - the
    /// controls must come back) and "bomb" (any area: morph, drop a bomb, log its explosion and the bomb jump).
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator SaveScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            MpTriggerEntity into = null;
            foreach (MpEntity e in G.Scripts.All) { var t = e as MpTriggerEntity; if (t != null && e.Name == "Trigger Save Station In") into = t; }
            if (into == null) { Log("no save station here"); yield break; }
            if (!into.Active) into.SetActive(true);      // the game turns it on as the player walks out of "Trigger Save Station Out"
            Vector3 c = (into.Min + into.Max) * 0.5f;
            Vector3 feet = new Vector3(c.x, c.y, into.Min.z + 0.1f);
            MpGame.LabFocus = feet;
            Aim(feet + new Vector3(0, 0, MpSpace.SamusEyeHeight), 0f, 0f);
            Log("standing in the save station at " + feet + " (trigger active " + into.Active + ")");
            for (int s = 0; s < 20; s++)
            {
                yield return Seconds(1f);
                Log("  " + (s + 1) + " s: cutscene " + (MpCinematics.Active ? "'" + MpCinematics.Current.Name + "'" : "none") + ", input " + (MpCinematics.InputDisabled ? "OFF" : "on") + ", energy " + G.State.Energy);
                if (s == 2) Shoot(_cam, "save_0.png", 960, 540);
            }

            int before = G.Shots.Fired;
            MpGame.LabFire = g => _labFire;
            _labFire = true; yield return Frames(2); _labFire = false; yield return Seconds(0.5f);
            MpGame.LabFire = null;
            Log("after the save: a shot " + (G.Shots.Fired > before ? "FIRED" : "did NOT fire") + " (input " + (MpCinematics.InputDisabled ? "OFF" : "on") + ")");
            Shoot(_cam, "save_1.png", 960, 540);
        }

        private IEnumerator BombScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            Vector3 start = G.PlayerGame;
            MpGame.LabFocus = start;
            bool morph = true;
            MpGame.LabBall = (MpGame g, out Vector2 stick) => { stick = Vector2.zero; bool m = morph; morph = false; return m; };
            yield return Frames(5);
            Log("morphed " + G.Ball.Morphed + " at " + G.Ball.Pos + ", bombs " + G.Bombs.Count + ", fuse " + (G.GunAssets != null && G.GunAssets.Tweak != null ? G.GunAssets.Tweak.BombFuseTime + " s, refill " + G.GunAssets.Tweak.BombDropDelayTime + " s, damage " + G.GunAssets.Tweak.Bomb.Damage + " radius " + G.GunAssets.Tweak.Bomb.Radius : "?"));
            Aim(G.Ball.Pos + new Vector3(0f, -5f, 2.5f), 0f, -20f);
            yield return Seconds(0.5f);
            float z0 = G.Ball.Pos.z, top = z0;
            MpGame.LabFire = g => _labFire;
            _labFire = true; yield return Frames(2); _labFire = false;
            Log("dropped: bombs left " + G.Bombs.Count + ", dropped " + G.Bombs.Dropped);
            yield return Seconds(0.6f);
            Shoot(_cam, "bomb_0.png", 960, 540);
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 3f) { top = Mathf.Max(top, G.Ball.Pos.z); yield return null; }
            Log("exploded " + G.Bombs.Exploded + ", bomb jumps " + G.Bombs.Jumps + ", the ball rose " + (top - z0).ToString("0.00") + " units, bombs back " + G.Bombs.Count);
            Shoot(_cam, "bomb_1.png", 960, 540);
            MpGame.LabFire = null;
            MpGame.LabBall = null;
        }
    }
}
