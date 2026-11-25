using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>Lab scenario "skipintro" (-mparea 0): with the option on, a new game's opening cutscene must be gone at once.</summary>
    public sealed partial class MpLabRunner
    {
        private static string FloorName(Vector3 feet)
        {
            RaycastHit h;
            return Physics.Raycast(MpSpace.ToUnity(feet + new Vector3(0, 0, 1f)), Vector3.down, out h, 5f) ? h.collider.name + " " + h.distance.ToString("0.00") + " m down" : "none";
        }

        private IEnumerator SkipIntroScenario()
        {
            bool was = MpSettings.SkipIntro;
            MpSettings.SkipIntro = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            Vector3 before = G.PlayerGame;
            int seen = 0;
            for (int i = 0; i < 20; i++)
            {
                yield return Seconds(0.5f);
                if (MpCinematics.Active) seen++;
            }

            Log("skip the intro: a cutscene was running in " + seen + " of 20 half-second checks over 10 s (should be 0), input " + (MpCinematics.InputDisabled ? "OFF" : "on"));
            Log("Samus at " + G.PlayerGame.ToString("F1") + " in room " + G.WorldView.CurrentArea + " (was " + before.ToString("F1") + "), the floor under her: " + FloorName(G.PlayerGame));
            Shoot(_cam, "skipintro_after.png", 960, 540);
            MpSettings.SkipIntro = was;
        }
    }
}
