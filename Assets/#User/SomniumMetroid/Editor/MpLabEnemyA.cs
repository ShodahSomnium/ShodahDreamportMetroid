using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "enemya": the Parasites and the Wall Crawler Swarms of the start area (run it with -mparea 4 for the
    /// Emergency Evacuation Area's munching parasites, -mparea 23 for a ventilation shaft swarm). Parasites: pictures of them
    /// still, then the scurry triggers fired (their links delivered), pictures as they run their paths, Samus stood on one
    /// (contact damage), one shot with the cannon (camera aimed at it) and one hit directly. Swarm: activated, Samus by its
    /// waypoint, pictures as it breeds and comes at her, its boids shot. Logs states, hits, deaths and the damage done to Samus.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioEnemyA(string scenario, ref IEnumerator run)
        {
            if (scenario == "enemya") run = EnemyAScenario();
        }

        /// <summary>The lab camera at a game-space eye looking at a game-space point.</summary>
        private void LookAtGame(Vector3 eye, Vector3 at)
        {
            Vector3 d = at - eye;
            float yaw = Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.z, new Vector2(d.x, d.y).magnitude) * Mathf.Rad2Deg;
            Aim(eye, yaw, pitch);
        }

        private IEnumerator EnemyAScenario()
        {
            G.God = true;
            Vector3 spawn = G.PlayerGame;
            MpGame.LabFocus = spawn;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            yield return Frames(20);

            var ps = new List<MpParasite>();
            var ss = new List<MpWallCrawlerSwarm>();
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e is MpParasite) ps.Add((MpParasite)e);
                if (e is MpWallCrawlerSwarm) ss.Add((MpWallCrawlerSwarm)e);
            }

            Log("enemya: " + ps.Count + " parasites, " + ss.Count + " swarms in the loaded areas (player at " + spawn.ToString("0.0") + ")");
            foreach (MpParasite p in ps) Log("  " + p.Report());
            foreach (MpWallCrawlerSwarm s in ss) Log("  " + s.Report());
            if (ps.Count > 0) yield return ParasiteLab(ps);
            if (ss.Count > 0) yield return SwarmLab(ss);
            MpGame.LabFire = null;
            MpGame.LabFocus = null;
        }

        private MpParasite NearestLive(List<MpParasite> ps, Vector3 to)
        {
            MpParasite best = null;
            float bd = float.MaxValue;
            foreach (MpParasite p in ps)
            {
                if (p.Dead || p.Gone || !p.Active || p.Area != G.WorldView.CurrentArea) continue;
                float d = (p.Pos - to).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }

            return best;
        }

        /// <summary>A view of a parasite from 3 units off its side and a little above.</summary>
        private void ParasitePicture(MpParasite p, string file)
        {
            if (p == null || p.Gone) return;
            Vector3 eye = p.Pos + new Vector3(2.2f, -2.2f, 1.8f);
            LookAtGame(eye, p.Pos);
            Shoot(_cam, file, 1280, 720);
        }

        private IEnumerator ParasiteLab(List<MpParasite> ps)
        {
            MpParasite first = NearestLive(ps, G.PlayerGame);
            if (first == null) { Log("no live parasite in the current area"); yield break; }
            MpGame.LabFocus = first.Pos + new Vector3(0f, 12f, 0f);
            yield return Frames(10);
            ParasitePicture(first, "enemya_parasite_idle.png");
            Log("idle: " + first.Report());

            // the scurry triggers / activation: deliver the Entered links of every trigger that activates a parasite
            int fired = 0;
            foreach (MpEntity e in G.Scripts.All.ToArray())
            {
                if (e.Links == null || e.Type != MpScriptType.Trigger) continue;
                bool toParasite = false;
                foreach (MpScriptObject.Link l in e.Links)
                    if (l.State == MpScriptMsg.StEntered && l.Message == MpScriptMsg.Activate && G.Scripts.Find(l.Target) is MpParasite) toParasite = true;
                if (!toParasite) continue;
                e.Send(MpScriptMsg.StEntered);
                fired++;
            }

            foreach (MpParasite p in ps) if (!p.Active) p.OnMessage(MpScriptMsg.Activate, null);
            Log("fired " + fired + " parasite triggers");
            float[] marks = { 1f, 3f, 6f, 10f };
            float waited = 0f;
            for (int i = 0; i < marks.Length; i++)
            {
                yield return Seconds(marks[i] - waited);
                waited = marks[i];
                foreach (MpParasite p in ps) Log("t=" + marks[i] + " " + p.Report());
                MpParasite near = first.Gone ? NearestLive(ps, first.Pos) : first;
                ParasitePicture(near, "enemya_parasite_run_" + i + ".png");
            }

            // Samus standing on a parasite: its contact damage
            MpParasite victim = NearestLive(ps, G.PlayerGame);
            if (victim != null)
            {
                int c0 = MpParasite.LabContacts;
                float e0 = G.State.Energy;
                for (int f = 0; f < 90 && !victim.Gone; f++)
                {
                    MpGame.LabFocus = victim.Pos - new Vector3(0f, 0f, 0.3f);
                    yield return null;
                }

                Log("contact: " + (MpParasite.LabContacts - c0) + " touches, damage " + MpParasite.LabDamage.ToString("0.#") + " (God mode: energy " + e0 + " -> " + G.State.Energy + "), last hit at " + G.LastDamageT.ToString("0.00") + " from " + G.LastDamageFrom.ToString("0.0"));
            }

            // a shot with the cannon: stand 5 units off, the camera on it
            MpParasite target = NearestLive(ps, G.PlayerGame);
            for (int attempt = 0; attempt < 4 && target != null && !target.Dead; attempt++)
            {
                Vector3 off = new Vector3(Mathf.Cos(attempt * 1.3f), Mathf.Sin(attempt * 1.3f), 0f) * 5f;
                MpGame.LabFocus = target.Pos + off - new Vector3(0f, 0f, 0.3f);
                yield return Frames(3);
                Vector3 eye = MpGame.LabFocus.Value + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
                LookAtGame(eye, target.Pos);
                int h0 = MpParasite.LabHits;
                _labFire = true;
                yield return null;
                _labFire = false;
                for (int f = 0; f < 3; f++) { LookAtGame(eye, target.Pos); yield return null; }
                Shoot(_cam, "enemya_parasite_shot_" + attempt + ".png", 1280, 720);
                yield return Seconds(0.6f);
                Log("shot " + attempt + ": fired " + G.Shots.Fired + ", hits " + G.Shots.Hits + " (last: " + G.Shots.LastHit + "), parasite hits " + (MpParasite.LabHits - h0) + ", target " + target.Report() + (target.Dead ? " DEAD" : ""));
            }

            if (target != null && target.Dead) Shoot(_cam, "enemya_parasite_killed.png", 1280, 720);

            // a direct hit on another (the power beam's damage)
            MpParasite second = NearestLive(ps, G.PlayerGame);
            if (second != null)
            {
                bool took = second.TakeDamage(0, false, 3f);
                Log("direct hit on " + second.Name + ": took " + took + ", " + (second.Dead ? "DEAD" : "hp " + second.HealthNow));
            }

            Log("parasites: hits " + MpParasite.LabHits + ", deaths " + MpParasite.LabDeaths + ", contacts " + MpParasite.LabContacts + ", damage to Samus " + MpParasite.LabDamage);
        }

        private IEnumerator SwarmLab(List<MpWallCrawlerSwarm> ss)
        {
            MpWallCrawlerSwarm s = null;
            foreach (MpWallCrawlerSwarm x in ss) if (x.Area == G.WorldView.CurrentArea && x.PatrolWaypoint() != null) { s = x; break; }
            if (s == null) { Log("no swarm with a patrol path in the current area"); yield break; }
            MpWaypointEntity w = s.PatrolWaypoint();
            Log("swarm " + s.Name + " waypoint " + w.Id.ToString("X8") + " at " + w.GamePos.ToString("0.0") + ", boids " + s.NumBoids + ", attraction " + s.AttractionRadius + ", launches " + s.MaxLaunches);

            var chain = new List<string>();
            var seenWp = new HashSet<MpWaypointEntity>();
            for (MpWaypointEntity x = w; x != null && seenWp.Add(x) && chain.Count < 40; x = x.Next()) chain.Add(x.GamePos.ToString("0.0"));
            Log("swarm path (" + chain.Count + "): " + string.Join(" ", chain));
            foreach (Vector3 dir in new[] { Vector3.back, Vector3.forward, Vector3.up, Vector3.down, Vector3.right, Vector3.left })
            {
                RaycastHit[] hits = Physics.RaycastAll(MpSpace.ToUnity(w.GamePos), MpSpace.DirToUnity(dir), 6f * MpSpace.Scale, ~0, QueryTriggerInteraction.Collide);
                var names = new List<string>();
                foreach (RaycastHit h in hits) names.Add(h.collider.name + " (layer " + h.collider.gameObject.layer + ") at " + (h.distance / MpSpace.Scale).ToString("0.00"));
                Log("  from the waypoint along " + dir + ": " + string.Join("; ", names));
            }

            // Samus where the game wakes the swarm: in the trigger that activates it (on the floor), else by the waypoint
            Vector3 stand = w.GamePos + new Vector3(4f, 4f, 0f);
            foreach (MpEntity e in G.Scripts.All)
            {
                var tr = e as MpTriggerEntity;
                if (tr == null || tr.Links == null) continue;
                foreach (MpScriptObject.Link l in tr.Links)
                    if (l.Message == MpScriptMsg.Activate && G.Scripts.Find(l.Target) == s) stand = (tr.Min + tr.Max) * 0.5f;
            }

            Log("the activating trigger's centre " + stand.ToString("0.0"));
            // stand by the middle of the path (on the floor under it), where the boids come past
            var wps = new List<MpWaypointEntity>();
            seenWp.Clear();
            for (MpWaypointEntity x = w; x != null && seenWp.Add(x) && wps.Count < 40; x = x.Next()) wps.Add(x);
            if (wps.Count > 2) stand = wps[wps.Count / 2].GamePos + new Vector3(1.5f, 0f, 0.5f);
            Vector3 gp, gn;
            if (MpCrawl.Ray(stand + new Vector3(0f, 0f, 1f), Vector3.back, 20f, out gp, out gn)) stand = gp;
            Log("standing at " + stand.ToString("0.0") + ", " + (w.GamePos - stand).magnitude.ToString("0.0") + " from the waypoint");
            MpGame.LabFocus = stand;
            s.OnMessage(MpScriptMsg.Activate, null);
            Vector3 eye = stand + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
            for (int k = 0; k < 6; k++)
            {
                yield return Seconds(0.25f);
                Log("t=" + (k * 0.25f + 0.25f).ToString("0.00") + " " + s.BoidLine(0) + " | " + s.BoidLine(9));
            }

            Shoot(_cam, "enemya_swarm_birth.png", 1280, 720);
            {
                Vector3 b0;
                if (s.NearestBoid(w.GamePos, out b0)) { LookAtGame(w.GamePos + new Vector3(3f, -3f, 2.5f), b0); Shoot(_cam, "enemya_swarm_birth_close.png", 1280, 720); }
            }
            float[] marks = { 0.5f, 2f, 4f, 7f };
            float waited = 0f;
            for (int i = 0; i < marks.Length; i++)
            {
                yield return Seconds(marks[i] - waited);
                waited = marks[i];
                Vector3 c;
                if (!s.NearestBoid(w.GamePos, out c)) c = w.GamePos;
                LookAtGame(eye, c);
                Shoot(_cam, "enemya_swarm_" + i + ".png", 1280, 720);
                Log("t=" + marks[i] + " " + s.Report());
            }

            // a close look
            Vector3 nb;
            if (s.NearestBoid(stand, out nb))
            {
                LookAtGame(nb + new Vector3(1.2f, -1.2f, 1.2f), nb);
                Shoot(_cam, "enemya_swarm_close.png", 1280, 720);
            }

            // Samus in the middle of them
            if (s.NearestBoid(w.GamePos, out nb))
            {
                MpGame.LabFocus = nb - new Vector3(0f, 0f, 0.5f);
                yield return Seconds(3f);
                Log("standing in the swarm: " + s.Report());
                MpGame.LabFocus = stand;
            }

            // shoot boids
            int k0 = s.Kills, h0 = MpWallCrawlerSwarm.LabShotHits;
            for (int i = 0; i < 6; i++)
            {
                Vector3 tgt;
                if (!s.NearestBoid(eye, out tgt)) break;
                LookAtGame(eye, tgt);
                _labFire = true;
                yield return null;
                _labFire = false;
                for (int f = 0; f < 2; f++) yield return null;
                if (i == 0) Shoot(_cam, "enemya_swarm_shot.png", 1280, 720);
                yield return Seconds(0.45f);
                Log("swarm shot " + i + ": fired " + G.Shots.Fired + ", hits " + G.Shots.Hits + " (last: " + G.Shots.LastHit + ")");
            }

            foreach (string d in s.DropLog) Log("dropped: " + d);
            Log("swarm shots: boid hits " + (MpWallCrawlerSwarm.LabShotHits - h0) + ", kills " + (s.Kills - k0) + "; " + s.Report());
            yield return Seconds(0.3f);
            Shoot(_cam, "enemya_swarm_after.png", 1280, 720);
        }
    }
}
