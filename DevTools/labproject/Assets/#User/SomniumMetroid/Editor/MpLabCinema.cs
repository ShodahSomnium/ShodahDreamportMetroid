using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "cinema" (-mparea 0): the intro cutscene seen first person (MpCinemaView) - the lab camera is the player's head,
    /// no stand-in feet: the world must slide so each shot's camera is at the eyes, Samus drawn in the world, then the world must
    /// come back to where it was when the cutscene ends.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator CinemaScenario()
        {
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            G.God = true;
            MpCinemaView.NoTurn = true;
            Vector3 origin0 = MpSpace.Origin;
            Vector3 before = Vector3.zero;
            bool haveBefore = false;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            Log("cutscene running " + MpCinematics.Active + (MpCinematics.Active ? " '" + MpCinematics.Current.Name + "'" : "") + ", first person " + MpCinemaView.Active);
            int pic = 0;
            Vector3 samus0 = G.PlayerGame;
            float samusDrift = 0f;
            int area0 = G.WorldView.CurrentArea;
            for (int s = 0; s < 6 && MpCinematics.Active; s++)
            {
                yield return Seconds(0.5f);
                MpCinematicCamera c = MpCinematics.Current;
                if (c == null) break;
                samusDrift = Mathf.Max(samusDrift, (G.PlayerGame - samus0).magnitude);
                if (!haveBefore && MpCinemaView.Active) { before = MpSpace.Origin - MpCinemaView.Applied; haveBefore = true; }
                Camera head = MpPlayer.Head() != null ? MpPlayer.Head() : _cam;
                float err = (MpSpace.ToUnity(c.ViewPos) - MpCinemaView.Anchor).magnitude;
                int shownActors = 0;
                foreach (MpPlayerActorEntity a in MpCinematics.CinemaActors) if (a.Active) shownActors++;
                if (s % 4 == 0)
                {
                    Log("  '" + c.Name + "' first person " + MpCinemaView.Active + ", camera-to-eyes " + err.ToString("0.000") + " m, world moved " + (MpSpace.Origin - origin0).magnitude.ToString("0.0") + " m, Samus actors active " + shownActors);
                    // look the way the shot looks
                    head.transform.SetPositionAndRotation(MpCinemaView.Anchor, MpCinemaView.Ahead);      // the player looks straight ahead
                    Log("  (the head camera: '" + head.name + "')");
                    Shoot(head, "cinema_" + (pic++).ToString("00") + ".png", 960, 540);
                }
            }

            // frame by frame over the whole cutscene: how close the shot's camera comes to the world (a 0.25 m ball at the eyes)
            int frames = 0, touching = 0, under = 0;
            float maxAngle = 0f, maxEye = 0f;
            float maxJerk = 0f;
            Vector3 lastPush = Vector3.zero;
            float maxStep = 0f, minClear = 999f;
            string worst = "";
            Vector3 prevEye = Vector3.zero;
            bool havePrev = false;
            float f0 = Time.realtimeSinceStartup;
            while (MpCinematics.Active && Time.realtimeSinceStartup - f0 < 150f)
            {
                yield return null;
                MpCinematicCamera fc = MpCinematics.Current;
                if (fc == null || !MpCinemaView.Active) continue;
                frames++;
                Vector3 eye = MpSpace.ToUnity(fc.ViewPos);
                maxAngle = Mathf.Max(maxAngle, Vector3.Angle(MpSpace.DirToUnity(fc.ViewForward), MpCinemaView.Ahead * Vector3.forward));
                maxEye = Mathf.Max(maxEye, (eye - MpCinemaView.Anchor).magnitude);

                bool touch = false;
                foreach (Collider oc in Physics.OverlapSphere(eye, 0.25f, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore)) if (MpSpace.Root != null && oc.transform.IsChildOf(MpSpace.Root)) { touch = true; if (touching < 3 || touching % 200 == 0) Log("    touching " + oc.name + " (" + oc.GetType().Name + ") in " + fc.Name + " t " + fc.T.ToString("0.0")); }
                if (touch) { touching++; worst = fc.Name + " t " + fc.T.ToString("0.0"); }
                RaycastHit dh;
                if (Physics.Raycast(eye, Vector3.down, out dh, 30f, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore)) minClear = Mathf.Min(minClear, dh.distance);
                else if (Physics.Raycast(eye, Vector3.up, out dh, 30f, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore)) under++;


                prevEye = eye; havePrev = true;
            }

            Log("exactness: the shot's view off straight ahead by at most " + maxAngle.ToString("0.000") + " deg, its camera off the eyes by at most " + maxEye.ToString("0.000") + " m");
            Log("frame check: " + frames + " frames, the eyes within 0.25 m of the world in " + touching + (touching > 0 ? " (last: " + worst + ")" : "") + ", under a surface in " + under + ", lowest " + minClear.ToString("0.00") + " m above a floor, the clearance push changed at most " + maxJerk.ToString("0.0") + " m/s");
            Log("during the cutscene Samus moved at most " + samusDrift.ToString("0.00") + " units (should stay put), room " + area0 + " -> " + G.WorldView.CurrentArea);
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            yield return Seconds(1f);      // past the view's 0.4 s hold between shots
            Log("after: world turn " + Quaternion.Angle(MpSpace.Turn, Quaternion.identity).ToString("0.000") + " deg (should be 0)");
            Log("after: first person " + MpCinemaView.Active + ", the world against where it stood before the cutscene view: " + (MpSpace.Origin - before).ToString("F3") + " (should be 0)");
            MpCinemaView.NoTurn = false;
        }
    }
}
