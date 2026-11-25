using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// The Hive Totem fight in the "tallon" lab: Samus steps into Trigger_startcinema (the intro: the totem sinks, steam, the
    /// bridge drops), then the lab plays the fight - each War Wasp the hive spits out is shot down, each time the target opens
    /// in the totem's mouth it is shot until its hit counter runs out - and logs the stages (wasps made and killed, the stage
    /// counters, the target's hits) until the head stage counter is full (the outro) or time runs out.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator TotemFight()
        {
            MpTriggerEntity start = null;
            MpDamageableTriggerEntity target = null;
            MpCounterEntity head = null, hits = null;
            MpPickupEntity prize = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e.Area != 36) continue;
                if (e.Name == "Trigger_startcinema") start = e as MpTriggerEntity;
                if (e.Name == "DamageableTrigger - Target") target = e as MpDamageableTriggerEntity;
                if (e.Name == "Counter - Head Stage") head = e as MpCounterEntity;
                if (e.Name == "Counter - Times Hit") hits = e as MpCounterEntity;
                if (e.Name == "P_Missile_Launcher") prize = e as MpPickupEntity;
            }

            foreach (MpEntity e in G.Scripts.All)
                if (e.Area == 36 && e.Name == "Actor - Totem")
                {
                    var act = e as MpActorEntity;
                    Renderer rd = act != null && act.Go != null ? act.Go.GetComponentInChildren<Renderer>(true) : null;
                    Log("totem: actor '" + e.Name + "' " + e.GetType().Name + " active " + e.Active + ", model " + (rd != null ? rd.GetType().Name + " at " + MpSpace.ToGame(rd.bounds.center) + " size " + (rd.bounds.size / MpSpace.Scale) + " visible " + rd.gameObject.activeInHierarchy : "NONE"));
                    if (rd != null) { _totemAt = MpSpace.ToGame(rd.bounds.center); }
                }

            Log("totem: start trigger " + (start != null) + ", target " + (target != null) + ", head counter " + (head != null ? head.Current + "/" + head.Max : "MISSING") + ", hits counter " + (hits != null) + ", prize " + (prize != null));
            if (start == null) { _failed = true; yield break; }
            Vector3 c = (start.Min + start.Max) * 0.5f;
            Vector3 feet = new Vector3(c.x, c.y, start.Min.z + 0.1f);
            MpGame.LabFocus = feet;
            string tpHow;
            MpPlayer.Teleport(MpSpace.ToUnity(feet) + Vector3.up * 0.05f, -YawTo(_totemAt != Vector3.zero ? _totemAt : feet + Vector3.up), out tpHow);
            yield return Frames(5);
            Log("totem: standing in the start trigger at " + feet + " (cinematic " + (MpCinematics.Active ? MpCinematics.Current.Name : "none") + ")");
            Aim(feet + new Vector3(0, 0, MpSpace.SamusEyeHeight), 0f, 0f);
            yield return Seconds(1f);
            Shoot(_cam, "totem_intro.png", 960, 540);
            ViewCheck("intro");
            if (_totemAt != Vector3.zero) { Aim(PlayerEye(), YawTo(_totemAt), PitchTo(_totemAt)); Shoot(_cam, "totem_actor.png", 960, 540); }

            int made0 = MpGeneratorEntity.Copies, killed = 0, targetHits = 0;
            float t0 = Time.time, lastLog = -10f;
            bool shotWasp = false, shotTarget = false;
            while (Time.time - t0 < 300f)
            {
                for (int k = 0; k < 3 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(1); }
                int alive = 0;
                foreach (MpEnemy e in MpEnemy.Live.ToArray())
                {
                    if (e.Area != 36 || e.Dead || !e.Active || !(e is MpWarWasp)) continue;
                    alive++;
                    // let each wasp fly a little before it is shot down (its attack runs show in the log)
                    if (((MpWarWasp)e).AliveFor > 3f)
                    {
                        if (!shotWasp) { Aim(PlayerEye(), YawTo(e.Pos), PitchTo(e.Pos)); Shoot(_cam, "totem_wasp.png", 960, 540); shotWasp = true; }
                        e.TakeDamage(0, false, (killed & 1) == 0 ? e.HealthNow + 0.1f : 1000f);
                        if (e.Dead) killed++;
                    }
                }

                if (target != null && target.Active && !target.Invulnerable)
                {
                    if (!shotTarget) { Vector3 tc = (target.Min + target.Max) * 0.5f; Aim(PlayerEye(), YawTo(tc), PitchTo(tc)); Shoot(_cam, "totem_target.png", 960, 540); shotTarget = true; }
                    if (target.TakeDamage(0, false, 50f)) targetHits++;
                    yield return Seconds(0.3f);
                }

                if (Time.time - lastLog > 5f)
                {
                    if (((int)(Time.time - t0)) % 15 < 5) ViewCheck((Time.time - t0).ToString("0") + " s");
                    Camera headCam = MpPlayer.Head();
                    if (headCam != null) Shoot(headCam, "totem_head_" + ((int)(Time.time - t0)).ToString("000") + ".png", 960, 540);
                    if (headCam != null) { NoFx = true; Shoot(headCam, "totem_head_" + ((int)(Time.time - t0)).ToString("000") + "_nofx.png", 960, 540); NoFx = false; }
                    Log("totem particles: " + MpEffects.Census(12) + " | near the head: " + MpEffects.Near(MpSpace.ToGame(headCam != null ? headCam.transform.position : Vector3.zero), 12f));
                    lastLog = Time.time;
                    Log("totem " + (Time.time - t0).ToString("0") + " s: wasps made " + (MpGeneratorEntity.Copies - made0) + ", alive " + alive + ", killed " + killed + "; target " + (target != null && target.Active ? "OPEN" : "closed") + ", hits " + targetHits + (hits != null ? " (counter " + hits.Current + ")" : "") + "; head stage " + (head != null ? head.Current + "/" + head.Max : "?")  + "; wasp bursts " + MpWarWasp.LabBursts + " dashes " + MpWarWasp.LabDashes + " stings " + MpWarWasp.LabStings + "; deaths normal " + MpEnemy.LabNormalDeaths + " massive " + MpEnemy.LabMassiveDeaths + " frozen " + MpEnemy.LabFrozenDeaths + " death fx " + MpEnemy.LabDeathFx + "; debris thrown " + MpDebrisEntity.LabThrown + " bounces " + MpDebrisEntity.LabBounces+ "; energy " + G.State.Energy + "; boss bar " + (G.Hud != null ? "" : "-"));
                }

                if (head != null && head.Max > 0 && head.Current >= head.Max) break;
                yield return Seconds(0.25f);
            }

            ViewCheck("end of fight");
            bool won = head != null && head.Max > 0 && head.Current >= head.Max;
            Log("totem: " + (won ? "DEFEATED" : "NOT defeated") + " after " + (Time.time - t0).ToString("0") + " s - wasps killed " + killed + ", target hits " + targetHits);
            yield return Seconds(8f);
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            Log("totem: prize " + (prize != null ? "'" + prize.Name + "' active " + prize.Active : "none") + "; missiles " + G.State.Missiles + "/" + G.State.MissileCapacity);
            if (prize != null && prize.Active && prize.Body != null)
            {
                // walk onto the Missile Launcher (the pickup's touch)
                MpGame.LabFocus = prize.Body.localPosition - new Vector3(0f, 0f, 0.5f);
                yield return Seconds(3f);
                for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
                Log("totem: after touching the prize - missiles " + G.State.Missiles + "/" + G.State.MissileCapacity + ", prize active " + prize.Active);
                if (G.State.MissileCapacity <= 0) _failed = true;
            }

            Aim(PlayerEye(), 0f, 0f);
            Shoot(_cam, "totem_after.png", 960, 540);
            if (!won) _failed = true;
        }

        private Vector3 _totemAt;

        private float PitchTo(Vector3 game)
        {
            Vector3 d = game - PlayerEye();
            return -Mathf.Atan2(d.z, new Vector2(d.x, d.y).magnitude) * Mathf.Rad2Deg;
        }

        private Vector3 PlayerEye() { return G.PlayerGame + new Vector3(0, 0, MpSpace.SamusEyeHeight); }

        private float YawTo(Vector3 game)
        {
            Vector3 d = game - G.PlayerGame;
            return Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
        }
    }
}

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    public sealed partial class MpLabRunner
    {
        /// <summary>What could sit in the player's view: every drawn renderer whose bounds come within 2 m of the eye (with its path),
        /// the live particle systems within 3 units, the cinema state and the room's player actors (cinematic Samus models).</summary>
        private void ViewCheck(string when)
        {
            Vector3 eye = MpSpace.ToUnity(PlayerEye());
            var hits = new System.Collections.Generic.List<string>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.bounds.SqrDistance(eye) > 4f) continue;
                string path = r.name;
                for (Transform t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                hits.Add(path + " (" + r.GetType().Name + ", size " + r.bounds.size.magnitude.ToString("0.0") + " m)");
            }

            var actors = new System.Collections.Generic.List<string>();
            foreach (MpEntity e in G.Scripts.All)
                if (e.Area == G.WorldView.CurrentArea && e.Type == 0x4C && e.Active) actors.Add("'" + e.Name + "'");
            Log("view " + when + ": cinema " + (MpCinematics.Active ? MpCinematics.Current.Name : "none") + "; active player actors " + string.Join(", ", actors.ToArray()) + "; effects near the eye: " + MpEffects.Near(PlayerEye(), 3f));
            hits.Clear();
            foreach (string h in hits) Log("view " + when + ":   near the eye: " + h);
            Camera hc = MpPlayer.Head();
            if (hc != null)
            {
                var along = new System.Collections.Generic.List<KeyValuePair<float, string>>();
                foreach (Vector2 vp in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.65f, 0.6f), new Vector2(0.35f, 0.6f) })
                {
                    Ray ray = hc.ViewportPointToRay(vp);
                    foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy || r.transform.root.name.Contains("HUD") || r.transform.root.name.Contains("Cannon")) continue;
                        float dist;
                        if (!r.bounds.IntersectRay(ray, out dist) || dist > 12f) continue;
                        string path = r.name;
                        for (Transform t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                        along.Add(new KeyValuePair<float, string>(dist, vp + " " + path + " (bounds " + r.bounds.size.ToString("0.0") + ", " + (r.sharedMaterial != null ? r.sharedMaterial.name : "-") + ")"));
                    }
                }

                along.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 0; i < along.Count && i < 12; i++) Log("view " + when + ":   in view at " + along[i].Key.ToString("0.0") + " m: " + along[i].Value);
            }

        }
    }
}
