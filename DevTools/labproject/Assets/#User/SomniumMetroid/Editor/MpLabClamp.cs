using SomniumSpace.Worlds.SomniumMetroid.Data;
using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "clamp" (-mparea 27 -mptarget padtrigger, Reactor Core Entrance): the freight clamp puzzle that drops the orange
    /// energy field ("Actor_Energy") over the way on: the panel is scanned (ScanDone), the ball rolls into the clamp, and after the
    /// clamp's timers the field must be gone and the door open.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator ClampScenario()
        {
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            MpActorEntity energy = null;
            MpDoorEntity door = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e.Name == "Actor_Energy" && e is MpActorEntity && e.Area == 27) energy = (MpActorEntity)e;
                if (e.Name == "Door_Area-component" && e is MpDoorEntity && e.Area == 27) door = (MpDoorEntity)e;
            }

            Log("field " + (energy != null ? (energy.Active ? "UP" : "down") : "missing") + ", door " + (door != null ? (door.IsOpen ? "open" : "closed") : "missing"));
            MpScripts.Trace = true;
            int scanned = 0;
            foreach (MpEntity e in G.Scripts.All)
                if (e.Name != null && e.Name.Contains("Freight clamp panel") && e.Area == 27) { e.Send(MpScriptMsg.StScanDone); scanned++; }
            Log("scanned the clamp panel (" + scanned + ")");
            yield return Seconds(1f);
            MpTriggerEntity pad = null;
            foreach (MpEntity e in G.Scripts.All) if (e.Name == "padtrigger" && e is MpTriggerEntity && e.Area == 27) pad = (MpTriggerEntity)e;
            Log("after the scan: padtrigger " + (pad != null ? (pad.Active ? "active" : "INACTIVE") : "missing") + ", field " + (energy != null && energy.Active ? "UP" : "down"));
            yield return BallScenario();
            MpScripts.Trace = true;
            for (int s = 0; s < 14; s++)
            {
                yield return Seconds(1f);
                if (s % 2 == 1) Log("  " + (s + 1) + " s after the clamp: field " + (energy != null ? (energy.Active ? "UP" : "down") : "missing") + ", door " + (door != null ? (door.IsOpen ? "open" : "closed") : "missing"));
            }

            Shoot(_cam, "clamp_after.png", 960, 540);
        }
    }
}
