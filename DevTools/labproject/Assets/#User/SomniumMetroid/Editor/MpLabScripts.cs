using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "scripts": the frigate's scripting runs on its own from the start area (or -mparea): which entity kinds were
    /// made, the cinematic shots as they play (pictures of the cinema screen's camera and of the player's view), effects live,
    /// generators' copies, enemies alive, the script message trace for the first seconds (-mptrace 1).
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioScripts(string scenario, ref IEnumerator run)
        {
            if (scenario == "scripts") run = ScriptsScenario();
            if (scenario == "ball") run = BallScenario();
            if (scenario == "lift") run = LiftScenario();
            if (scenario == "rooms") run = RoomsScenario();
            if (scenario == "blockage") run = BlockageScenario();
            if (scenario == "save") run = SaveScenario();
            if (scenario == "bomb") run = BombScenario();
            if (scenario == "clamp") run = ClampScenario();
            if (scenario == "cinema") run = CinemaScenario();
            if (scenario == "skipintro") run = SkipIntroScenario();
            if (scenario == "fusionsolo") run = FusionSoloScenario();
            if (scenario == "settle") run = SettleScenario();
            if (scenario == "ballhold") run = BallHoldScenario();
            if (scenario == "cineavatar") run = CineAvatarScenario();
        }

        private void Census(string when)
        {
            var count = new Dictionary<string, int>();
            foreach (MpEntity e in G.Scripts.All)
            {
                string k = e.GetType().Name;
                int n;
                count.TryGetValue(k, out n);
                count[k] = n + 1;
            }

            var parts = new List<string>();
            foreach (var kv in count) parts.Add(kv.Key + " " + kv.Value);
            parts.Sort();
            Log(when + ": entities " + G.Scripts.All.Count + " - " + string.Join(", ", parts.ToArray()));
            Log(when + ": effects " + MpEffects.Census() + "; live " + MpEffects.LiveCount + ", enemies alive " + MpEnemy.Live.Count + ", generator copies " + MpGeneratorEntity.Copies + ", cinematic shots " + MpCinematics.Shots + (MpCinematics.Active ? " (one running: '" + MpCinematics.Current.Name + "' " + MpCinematics.Current.T.ToString("0.0") + "/" + MpCinematics.Current.Duration.ToString("0.0") + " s)" : ""));
        }

        private IEnumerator ScriptsScenario()
        {
            MpScripts.Trace = MpLabBoot.Arg("-mptrace", "0") == "1";
            Vector3 spawn = G.PlayerGame;
            MpGame.LabFocus = spawn;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            G.God = true;
            Vector3 eye = spawn + new Vector3(0, 0, MpSpace.SamusEyeHeight);
            Aim(eye, 0f, 3f);
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            yield return Frames(10);
            Census("start");
            int seconds = int.Parse(MpLabBoot.Arg("-mpseconds", "60"));
            int pic = 0;
            int lastShots = 0;
            for (int s = 0; s < seconds; s++)
            {
                yield return Seconds(1f);
                if (MpCinematics.Shots != lastShots || (MpCinematics.Active && s % 3 == 0))
                {
                    lastShots = MpCinematics.Shots;
                    Shoot(_cam, "scripts_view_" + (pic++).ToString("00") + ".png", 960, 540);
                }

                if (s % 10 == 9) Census("after " + (s + 1) + " s");
            }

            Census("end");
            Shoot(_cam, "scripts_end.png", 1280, 720);
            MpScripts.Trace = false;
        }
    }
}

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    public sealed partial class MpLabRunner
    {
        private bool _ballToggle;
        private Vector3? _ballGoal;

        /// <summary>
        /// Lab scenario "ball" (-mparea 12): morph, roll the ball to the room's morphed-only trigger ("into ball Trigger" by default,
        /// -mptarget another name), log the triggers it sets off and the ball camera on the cinema screen, unmorph.
        /// </summary>
        private IEnumerator BallScenario()
        {
            MpScripts.Trace = MpLabBoot.Arg("-mptrace", "0") == "1";
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            string target = MpLabBoot.Arg("-mptarget", "into ball Trigger");
            MpTriggerEntity goal = null;
            int targetArea = int.Parse(MpLabBoot.Arg("-mptargetarea", "-1"));
            foreach (MpEntity e in G.Scripts.All) { var t = e as MpTriggerEntity; if (t != null && e.Name == target && (targetArea < 0 || e.Area == targetArea)) goal = t; }
            if (goal == null) { Log("no trigger '" + target + "' here"); yield break; }
            Vector3 gc = (goal.Min + goal.Max) * 0.5f;
            Log("goal '" + target + "' at " + gc + " flags " + goal.Flags.ToString("X") + (goal.Active ? "" : " (inactive)"));
            // stand a few units from it on the floor, morph, roll in
            Vector3 start = gc + new Vector3(0f, -4f, 0f);
            if (MpEnemy.WorldRay(MpSpace.ToUnity(start + new Vector3(0, 0, 3f)), Vector3.down, 10f * MpSpace.Scale, out RaycastHit h)) start = MpSpace.ToGame(h.point);
            MpGame.LabFocus = start;
            Aim(start + new Vector3(0, 0, MpSpace.SamusEyeHeight), 0f, 0f);
            yield return Frames(10);
            _ballGoal = gc;
            MpGame.LabBall = (MpGame g, out Vector2 stick) =>
            {
                stick = Vector2.zero;
                if (g.Ball.Morphed && _ballGoal.HasValue)
                {
                    Vector3 d = _ballGoal.Value - g.Ball.Pos;
                    float yr = g.Ball.CamYaw * Mathf.Deg2Rad;
                    Vector3 f = new Vector3(-Mathf.Sin(yr), Mathf.Cos(yr), 0f), r = new Vector3(f.y, -f.x, 0f);
                    Vector2 s = new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f));
                    stick = s.magnitude > 1f ? s.normalized : s;
                }

                bool tg = _ballToggle;
                _ballToggle = false;
                return tg;
            };
            _ballToggle = true;
            yield return Frames(3);
            Log("morphed " + G.Ball.Morphed + " at " + G.Ball.Pos);
            int pic = 0;
            bool entered = false;
            for (int i = 0; i < 20 * 10 && !entered; i++)
            {
                yield return Seconds(0.1f);
                entered = goal.Inside || !goal.Active;      // (it may switch itself off on entry)
                if (i % 20 == 0) { Log("ball " + G.Ball.Pos.ToString("F2") + " speed " + G.Ball.Vel.magnitude.ToString("0.0") + " to goal " + (gc - G.Ball.Pos).magnitude.ToString("0.0")); Shoot(_cam, "ball_" + (pic++).ToString("00") + ".png", 960, 540); }
            }

            Log(entered ? "the ball set off '" + target + "'" : "the ball did NOT reach '" + target + "'");
            Shoot(_cam, "ball_goal.png", 960, 540);
            _ballGoal = null;
            yield return Seconds(1f);
            _ballToggle = true;
            yield return Frames(3);
            Log("unmorphed: " + !G.Ball.Morphed + (G.Ball.Morphed ? " (no headroom here)" : ""));
            MpGame.LabBall = null;
            MpScripts.Trace = false;
        }

    }
}
