using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "lift" (-mparea 10, the Connection Elevator to Deck Beta; -mpplatform another name): the lab player stands on the
    /// elevator platform, the platform is started as its scripts start it, and the ride is logged - the platform's game position,
    /// the player's, the world's shift - until it stops at its last waypoint.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator LiftScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            string name = MpLabBoot.Arg("-mpplatform", "Platform Elevator");
            MpMovingPlatform p = null;
            foreach (MpEntity e in G.Scripts.All) { var m = e as MpMovingPlatform; if (m != null && e.Name == name) p = m; }
            if (p == null) { Log("no platform '" + name + "'"); yield break; }
            Bounds b = p.Solid != null ? p.Solid.bounds : new Bounds(MpSpace.ToUnity(p.GamePos), Vector3.one);
            Vector3 top = MpSpace.ToGame(new Vector3(b.center.x, b.max.y, b.center.z));
            MpGame.LabFocus = top;
            Aim(top + new Vector3(0, 0, MpSpace.SamusEyeHeight), 0f, 10f);
            yield return Frames(10);
            Log("platform '" + name + "' at " + p.GamePos + ", player " + G.PlayerGame + ", carried " + p.Carries(G.PlayerGame) + ", origin " + MpSpace.Origin);
            Shoot(_cam, "lift_0.png", 960, 540);
            p.OnMessage(MpScriptMsg.Start, null);
            Vector3 startPlat = p.GamePos;
            for (int s = 1; s <= 40; s++)
            {
                yield return Seconds(0.5f);
                if (s % 4 == 0) { Log("t " + (s * 0.5f) + " s: platform " + p.GamePos.ToString("F2") + " player " + G.PlayerGame.ToString("F2") + " rides " + G.RideCount + " origin " + MpSpace.Origin.ToString("F2")); Shoot(_cam, "lift_" + (s / 4) + ".png", 960, 540); }
                if (s > 4 && (p.GamePos - startPlat).magnitude > 0.1f && p.LastDelta == Vector3.zero && G.RideCount > 0 && s % 4 == 0 && !IsMoving(p)) break;
            }

            Log("ride over: the platform moved " + (p.GamePos - startPlat).magnitude.ToString("0.0") + " units, the player is " + (G.PlayerGame - p.GamePos).magnitude.ToString("0.0") + " from it, area " + G.WorldView.CurrentArea);
        }

        private Vector3 _liftLast;

        private bool IsMoving(MpMovingPlatform p)
        {
            bool moving = (p.GamePos - _liftLast).sqrMagnitude > 1e-6f;
            _liftLast = p.GamePos;
            return moving;
        }
    }
}
