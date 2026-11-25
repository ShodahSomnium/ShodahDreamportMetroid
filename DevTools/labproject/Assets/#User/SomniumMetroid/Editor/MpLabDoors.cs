using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "doors": every active DamageableTrigger in the loaded areas - the camera stands 6 units in front of its face,
    /// a ray from the eye lists what it passes (nearest first), one power beam shot is fired at it, and its health / state is
    /// logged before and after, with a picture.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioDoors(string scenario, ref IEnumerator run)
        {
            if (scenario == "doors") run = DoorsScenario();
            if (scenario == "gate") run = GateScenario();
        }

        private IEnumerator DoorsScenario()
        {
            MpGame.LabFocus = G.PlayerGame;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            yield return Frames(20);
            int n = 0;
            foreach (MpEntity e in G.Scripts.All.ToArray())
            {
                var dt = e as MpDamageableTriggerEntity;
                if (dt == null) continue;
                Vector3 size = dt.Max - dt.Min, c = (dt.Max + dt.Min) * 0.5f;
                string vuln = dt.Vuln != null ? string.Join(",", System.Array.ConvertAll(dt.Vuln.Normal, v => v.ToString())) : "none";
                Log("DT " + dt + " active " + dt.Active + " health " + dt.HealthNow + "/" + dt.MaxHealth + " box " + c + " size " + size + " vuln " + vuln + " collider " + (dt.Box != null ? (dt.Box.enabled + " layer " + dt.Box.gameObject.layer + " active " + dt.Box.gameObject.activeInHierarchy) : "none"));
                if (!dt.Active || n >= 6) continue;
                n++;
                Log("  invulnerable " + dt.Invulnerable);
                // its script wiring: what it sends, and what sends to it (state -> message)
                if (dt.Links != null)
                    foreach (var l in dt.Links)
                    {
                        MpEntity t = G.Scripts.Find(l.Target);
                        Log("    sends state " + l.State + " -> msg " + l.Message + " to " + (t != null ? t.ToString() : l.Target.ToString("X8") + " (not loaded)"));
                    }

                foreach (MpEntity src in G.Scripts.All)
                    if (src.Links != null)
                        foreach (var l in src.Links)
                            if (l.Target == dt.Id) Log("    gets msg " + l.Message + " on state " + l.State + " from " + src + (src.Active ? "" : " (inactive)"));
                // stand 6 units out along the thinnest axis: the side with a clear line to the box (else the player's side)
                Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                float side = Mathf.Sign(Vector3.Dot(G.PlayerGame - c, axis));
                if (side == 0f) side = 1f;
                foreach (float s in new[] { side, -side })
                {
                    Vector3 e2 = MpSpace.ToUnity(c + axis * s * 6f), dir = MpSpace.ToUnity(c) - e2;
                    RaycastHit first;
                    bool blocked = MpProjectiles.ShotRay(e2, dir.normalized, dir.magnitude, out first) && first.collider.GetComponentInParent<MpHitTarget>() == null;
                    Log("  side " + s + ": " + (blocked ? "blocked by " + first.collider.name + " @" + first.distance.ToString("0.00") + " material " + MpAreaView.MaterialAt(first).ToString("X8") : "clear"));
                    if (!blocked) { side = s; break; }
                }

                Vector3 eye = c + axis * side * 6f;
                MpGame.LabFocus = eye - new Vector3(0, 0, MpSpace.SamusEyeHeight);
                _cam.transform.position = MpSpace.ToUnity(eye);
                _cam.transform.rotation = Quaternion.LookRotation(MpSpace.ToUnity(c) - _cam.transform.position);
                yield return Frames(3);
                RaycastHit[] hits = Physics.RaycastAll(_cam.transform.position, _cam.transform.forward, 30f, MpProjectiles.HitMask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                string list = "";
                foreach (RaycastHit h in hits)
                {
                    var sv = h.collider.GetComponent<MpShotVulnerability>();
                    list += h.collider.name + " @" + h.distance.ToString("0.00") + (sv != null && sv.Entity.Vuln != null ? " (power beam vulnerability " + sv.Entity.Vuln.Of(0, false) + ")" : "") + "; ";
                }
                Log("  ray from the eye: " + (list.Length > 0 ? list : "nothing"));
                MpScripts.Trace = n == 1;
                _labFire = true;
                yield return Frames(2);
                _labFire = false;
                yield return Seconds(0.8f);
                MpScripts.Trace = false;
                Log("  after a shot: health " + dt.HealthNow + " active " + dt.Active + " (shots " + G.Shots.Fired + ", hits " + G.Shots.Hits + ", last hit " + G.Shots.LastHit + ")");
                foreach (MpEntity d in G.Scripts.All) { var door = d as MpDoorEntity; if (door != null && (door.GamePos - c).magnitude < 8f) Log("  door " + door + " open " + door.IsOpen + " projectiles collide " + door.ProjectilesCollide); }
                Shoot(_cam, "doors_" + n + ".png", 960, 540);
                yield return Seconds(2.5f);
                Shoot(_cam, "doors_" + n + "_late.png", 960, 540);
            }

            MpGame.LabFire = null;
        }
    }
}
