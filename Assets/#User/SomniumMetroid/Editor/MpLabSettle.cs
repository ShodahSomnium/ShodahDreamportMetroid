using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "settle" (-mparea 0): the world moved under the player (as a ride or the third-person morph ball leaves it) must go
    /// back to MpSpace.Home - where every client's world sits - with the avatar moved along, so Samus keeps her place in the game world.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator SettleScenario()
        {
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            yield return Seconds(1.5f);
            // a stand-in body for the teleport (the editor has no avatar; MpPlayer finds a CharacterController), on the deck where Samus is
            var body = new GameObject("Lab Player Body").AddComponent<CharacterController>();
            body.transform.position = MpSpace.ToUnity(G.PlayerGame) + Vector3.up * 0.05f;
            yield return Seconds(2.5f);      // MpPlayer looks for a controller once a second
            Vector3 game0 = G.PlayerGame;
            int s0 = MpGame.Settles;
            MpSpace.Shift(new Vector3(4f, 0.5f, -7f));          // as a ride / a roll leaves it
            yield return null;
            yield return null;
            game0 = G.PlayerGame;      // where the moved world put her (a ride carries her through the game world: this is her place now)
            Log("world moved " + (MpSpace.Origin - MpSpace.Home).ToString("F2") + "; Samus now at " + game0.ToString("F2"));
            yield return Seconds(2.5f);
            Vector3 off = MpSpace.Origin - MpSpace.Home;
            float drift = (G.PlayerGame - game0).magnitude;
            Log("CHECK settle-home " + (off.magnitude < 0.01f ? "ok" : "FAILED") + " (world off home by " + off.magnitude.ToString("0.000") + " m)");
            Log("CHECK settle-place " + (drift < 0.3f ? "ok" : "FAILED") + " (Samus moved " + drift.ToString("0.00") + " game units)");
            Log("CHECK settle-once " + (MpGame.Settles - s0 == 1 ? "ok" : "FAILED") + " (" + (MpGame.Settles - s0) + " settles)");
        }
    }
}
