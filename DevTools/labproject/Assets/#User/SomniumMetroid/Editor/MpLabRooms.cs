using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "rooms": every room of the frigate in turn (the tablet's room warp), each built with its scripts, enemies and
    /// effects: per room the entity census, enemies, effects, and a picture from its entrance; any exception in the log is the
    /// lab's failure line. -mpfrom / -mpto limit the rooms.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator RoomsScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            int from = int.Parse(MpLabBoot.Arg("-mpfrom", "0")), to = int.Parse(MpLabBoot.Arg("-mpto", (G.World.Areas.Count - 1).ToString()));
            int errors = 0;
            Application.logMessageReceived += (msg, stack, type) => { if (type == LogType.Exception || type == LogType.Error) errors++; };
            for (int a = from; a <= to; a++)
            {
                int before = errors;
                string said = MpGameActions.WarpTo(a);
                float t0 = Time.realtimeSinceStartup;
                yield return Frames(2);
                while ((G.Warping || G.WorldView.IsBusy) && Time.realtimeSinceStartup - t0 < 90f) yield return null;
                yield return Seconds(1.5f);
                MpGame.LabFocus = G.PlayerGame;
                Aim(G.PlayerGame + new Vector3(0, 0, MpSpace.SamusEyeHeight), 0f, 0f);
                yield return Frames(3);
                Census("room " + a + " '" + G.World.Areas[a].Name + "' (" + said + ")");
                Shoot(_cam, "room_" + a.ToString("00") + ".png", 640, 360);
                if (errors > before) Log("room " + a + ": " + (errors - before) + " errors/exceptions while it loaded");
            }

            Log("rooms done: " + errors + " errors/exceptions in all");
        }
    }
}
