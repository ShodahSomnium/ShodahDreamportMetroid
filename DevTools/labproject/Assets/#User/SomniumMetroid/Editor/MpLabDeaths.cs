using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenarios "deaths" (Chozo Ruins, Ruins Entrance: two beetles woken; one killed by an exactly lethal shot - the normal death:
    /// DeathRattle, the death animation, a 3 s fade, then Dead; the other by a huge one - the massive death: the death sound,
    /// MassiveDeath, the explosion PART, Dead at once; the times between the states are logged with pictures) and "xray" (Tallon
    /// Overworld, Root Cave: the actors with an x-ray model swap it in with the X-Ray visor and back).
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioDeaths(string scenario, ref IEnumerator run)
        {
            if (scenario == "deaths") run = DeathsScenario();
            if (scenario == "xray") run = XrayScenario();
        }

        private IEnumerator DeathsScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            yield return G.SwitchWorld("Metroid2.pak", 0, 1, false, null);
            yield return Settle();
            MpGame.LabFocus = G.PlayerGame;
            var beetles = new System.Collections.Generic.List<MpBeetle>();
            foreach (MpEntity e in G.Scripts.All) if (e.Area == 1 && e is MpBeetle) beetles.Add((MpBeetle)e);
            Log("deaths: " + beetles.Count + " beetles in the Ruins Entrance");
            if (beetles.Count < 2) { _failed = true; yield break; }
            int i = 0;
            foreach (MpBeetle b in beetles.GetRange(0, 2))
            {
                bool massive = i++ == 1;
                b.OnMessage(MpScriptMsg.Activate, null);
                yield return Seconds(3f);
                MpGame.LabFocus = b.Pos + new Vector3(0f, -8f, 0f);
                Aim(b.Pos + new Vector3(0f, -8f, MpSpace.SamusEyeHeight), YawFrom(b.Pos + new Vector3(0f, -8f, 0f), b.Pos), 10f);
                int states = 0;
                string seen = "";
                float t0 = Time.time;
                b.StateHook = (s) => { seen += s + "@" + (Time.time - t0).ToString("0.0") + " "; states++; };
                int fx0 = MpEnemy.LabDeathFx;
                Log("deaths: beetle '" + b.Name + "' xdamage " + b.Info.XDamage + ", health " + b.HealthNow + ", death PART " + b.Info.Particle1.ToString("X8") + ", death sfx " + b.Info.DeathSfx + " - killing it " + (massive ? "with a huge blow" : "with an exactly lethal blow"));
                if (!massive) { b.TakeDamage(MpPlayerState.BeamPower, false, b.HealthNow - 0.5f); yield return Seconds(0.5f); seen = ""; }
                b.TakeDamage(MpPlayerState.BeamPower, false, massive ? 1000f : 1f);      // the normal kill: a last blow under the x-damage
                yield return Seconds(0.3f);
                Shoot(_cam, "deaths_" + (massive ? "massive" : "normal") + "_0.png", 960, 540);
                yield return Seconds(1.5f);
                Shoot(_cam, "deaths_" + (massive ? "massive" : "normal") + "_1.png", 960, 540);
                while (!b.Gone && Time.time - t0 < 15f) yield return null;
                if (!massive) Shoot(_cam, "deaths_normal_2.png", 960, 540);
                Log("deaths: " + (massive ? "massive" : "normal") + " - states " + seen + "; gone after " + (Time.time - t0).ToString("0.0") + " s; death effects spawned " + (MpEnemy.LabDeathFx - fx0));
            }

            Log("deaths: totals normal " + MpEnemy.LabNormalDeaths + ", massive " + MpEnemy.LabMassiveDeaths + ", frozen " + MpEnemy.LabFrozenDeaths);
        }

        private static float YawFrom(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
        }

        private IEnumerator XrayScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            yield return G.SwitchWorld(MpGame.TallonPak, 0, 15, false, null);
            yield return Settle();
            MpActorEntity found = null;
            int withXray = 0;
            foreach (MpEntity e in G.Scripts.All) { var a = e as MpActorEntity; if (a != null && e.Area == 15 && a.XrayGo != null) { withXray++; if (found == null || (a.Active && !found.Active)) found = a; } }
            Log("xray: Root Cave actors with an x-ray model: " + withXray);
            if (found == null) { _failed = true; yield break; }
            Vector3 at = found.Go.transform.localPosition;
            MpGame.LabFocus = at + new Vector3(0f, -10f, 0f);
            Aim(at + new Vector3(0f, -10f, 2f), 0f, 5f);
            yield return Frames(5);
            Shoot(_cam, "xray_combat.png", 960, 540);
            Log("xray: '" + found.Name + "' active " + found.Active + " - combat: model drawn " + Drawn(found.Go) + ", x-ray model drawn " + Drawn(found.XrayGo));
            G.SetVisor(MpPlayerState.VisorXRay);
            yield return Frames(5);
            Shoot(_cam, "xray_xray.png", 960, 540);
            Log("xray: x-ray visor: model drawn " + Drawn(found.Go) + ", x-ray model drawn " + Drawn(found.XrayGo) + " (swaps " + MpActorEntity.LabXraySwaps + ")");
            G.SetVisor(MpPlayerState.VisorCombat);
            yield return Frames(5);
            Log("xray: back to combat: model drawn " + Drawn(found.Go) + ", x-ray model drawn " + Drawn(found.XrayGo));
        }

        private static bool Drawn(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) if (r.enabled) return true;
            return false;
        }
    }
}
