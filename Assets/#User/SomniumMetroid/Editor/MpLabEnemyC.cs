using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "enemyc" (run with -mparea 25, the Reactor Core): the Parasite Queen and its shield rings. Samus (god mode)
    /// stands where the intro cinematic leaves her; if the scripts have not woken the queen, the intro's end is done by hand (the
    /// rings activated, Relay Skip Rings, Relay End Intro Cinematic). The fight is watched for a while (state, beam, rings,
    /// pictures), then the queen is shot with the arm cannon from Samus's place until it dies (aiming at the head, else the
    /// pelvis; if the rings keep the shots off for too long, hits are dealt through whichever queen box a clear line reaches).
    /// The script messages of its death (DeathRattle, then Dead) are traced and the escape chain's key objects checked.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioEnemyC(string scenario, ref IEnumerator run)
        {
            if (scenario == "enemyc") run = QueenScenario();
        }

        private void QueenLook(Vector3 eye, Vector3 at)
        {
            _cam.transform.position = MpSpace.ToUnity(eye);
            Vector3 d = MpSpace.ToUnity(at) - _cam.transform.position;
            if (d.sqrMagnitude > 1e-6f) _cam.transform.rotation = Quaternion.LookRotation(d);
        }

        private void QueenPicture(string file)
        {
            MpShaderGlobals.Apply(_cam);
            Shoot(_cam, file, 1280, 720);
        }

        private MpEntity QueenFind(string name)
        {
            foreach (MpEntity e in G.Scripts.All) if (e.Name == name) return e;
            return null;
        }

        private void QueenSend(string name, int msg)
        {
            int n = 0;
            foreach (MpEntity e in G.Scripts.All.ToArray())
                if (e.Name == name) { G.Scripts.Deliver(null, e.Id, msg, -1); n++; break; }
            Log("sent msg " + msg + " to '" + name + "'" + (n == 0 ? " - NOT FOUND" : ""));
        }

        /// <summary>Every collider on a line (world and shot layers), nearest first, with the game point.</summary>
        private void QueenRays(string tag, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            RaycastHit[] hits = Physics.RaycastAll(from, d.normalized, d.magnitude + 2f, (1 << 0) | (1 << MpAreaView.ShotOnlyLayer), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            string s = "";
            foreach (RaycastHit h in hits) s += h.collider.name + " (layer " + h.collider.gameObject.layer + ") at " + MpSpace.ToGame(h.point).ToString("0.0") + "; ";
            RaycastHit first;
            bool shot = MpProjectiles.ShotRay(from, d.normalized, d.magnitude + 2f, out first);
            Log("ray " + tag + " -> head (" + (d.magnitude / MpSpace.Scale).ToString("0.0") + " units): " + (s.Length > 0 ? s : "nothing") + " | ShotRay stops at " + (shot ? first.collider.name : "nothing"));
        }

        private IEnumerator QueenScenario()
        {
            MpGame.LabFocus = G.PlayerGame;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            G.God = true;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            yield return Frames(20);

            MpParasiteQueen q = null;
            MpIntroBossRings rings = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e is MpParasiteQueen) q = (MpParasiteQueen)e;
                if (e is MpIntroBossRings) rings = (MpIntroBossRings)e;
            }

            Log("queen " + (q != null ? q.ToString() + ": " + q.Report() : "MISSING") + "; ring controller " + (rings != null ? rings.Report() : "MISSING (the lead's factory does not make it yet?)"));
            if (q == null) { _failed = true; yield break; }
            Log("queen '" + q.BossName + "' bar max " + q.BarMax + " health " + q.MaxHealth + " at " + q.Pos.ToString("0.0") + " head " + q.HeadPoint.ToString("0.0") + " pelvis " + q.PelvisPoint.ToString("0.0"));

            // Samus where the intro cinematic leaves her
            Vector3 feet = G.PlayerGame;
            foreach (MpEntity e in G.Scripts.All)
            {
                var sp = e as MpSpawnEntity;
                if (sp != null) Log("spawn '" + sp.Name + "' " + sp.GamePos.ToString("0.0") + (sp.Default ? " (default)" : ""));
                if (sp != null && sp.Name.Contains("End of Queen Intro")) feet = sp.GamePos;
            }

            MpGame.LabFocus = feet;
            Vector3 eye = feet + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
            yield return Frames(10);
            QueenLook(eye, q.HeadPoint);
            QueenPicture("enemyc_0_start.png");
            QueenLook(eye + (eye - q.HeadPoint).normalized * 12f + new Vector3(0f, 0f, 8f), q.Pos + new Vector3(0f, 0f, 8f));
            QueenPicture("enemyc_0_wide.png");

            // wake it as the end of the intro cinematic (or its skip) does, if the scripts have not
            float w0 = Time.time;
            while (!q.Active && Time.time - w0 < 8f) yield return null;
            if (!q.Active)
            {
                Log("queen inactive after 8 s: doing the intro's end by hand");
                for (int i = 0; i < 5; i++) QueenSend("Platform_" + i, 1);
                QueenSend("Relay Skip Rings", 13);
                QueenSend("Relay End Intro Cinematic", 13);
                yield return Frames(5);
            }

            Log("queen active " + q.Active + ": " + q.Report());
            if (rings != null) Log(rings.Report());

            // the fight without shooting: its turns, beams and the rings
            bool beamPic = false, alignedPic = false;
            int aligns0 = rings != null ? rings.Aligns : 0;
            float f0 = Time.time, lastLog = -10f;
            while (Time.time - f0 < 35f)
            {
                QueenLook(eye, q.HeadPoint);
                if (Time.time - lastLog > 2.5f)
                {
                    lastLog = Time.time;
                    Log("t " + (Time.time - f0).ToString("0.0") + " queen " + q.Report());
                    if (rings != null) Log("    " + rings.Report());
                }

                string r = q.Report();
                if (!beamPic && r.Contains("beam on state 2"))
                {
                    beamPic = true;
                    QueenPicture("enemyc_1_beam.png");
                    QueenLook(eye + new Vector3(0f, 0f, 6f) + (eye - q.HeadPoint).normalized * 10f, (q.HeadPoint + eye) * 0.5f);
                    QueenPicture("enemyc_1_beam_side.png");
                    QueenLook(eye, q.HeadPoint);
                }

                if (!alignedPic && rings != null && rings.Aligns > aligns0 && rings.Report().Contains("Stopped"))
                {
                    alignedPic = true;
                    QueenPicture("enemyc_2_rings_aligned.png");
                }

                yield return null;
            }

            Log("watched: beams fired " + q.BeamsFired + ", frames on Samus " + q.BeamTicksOnSamus + ", damage dealt " + q.BeamDamageDealt.ToString("0.0") + " (god mode), energy " + G.State.Energy);

            // the queen's model: its textures there? a close look
            var tex = new System.Collections.Generic.HashSet<uint>();
            if (q.Char != null) q.Char.TexturesInto(tex);
            int missing = 0;
            foreach (uint t in tex) if (MpTextures.Get(t) == null) missing++;
            Log("queen model: " + tex.Count + " textures, " + missing + " not uploaded; renderers " + (q.Anim != null ? q.Anim.Renderers.Count.ToString() : "none"));
            if (q.Anim != null)
                foreach (SkinnedMeshRenderer r in q.Anim.Renderers)
                {
                    if (r == null) continue;
                    string ms = "";
                    foreach (Material m in r.sharedMaterials)
                        if (m != null) ms += m.name + " (" + m.shader.name + " lit " + (m.HasProperty("_Lit") ? m.GetFloat("_Lit").ToString() : "-") + " amb " + (m.HasProperty("_Ambient") ? m.GetColor("_Ambient").ToString() : "-") + " tex0 " + (m.HasProperty("_Tex0") && m.GetTexture("_Tex0") != null ? m.GetTexture("_Tex0").name : "none") + "); ";
                    Log("queen renderer " + r.name + " enabled " + r.enabled + " bounds " + r.bounds.size.ToString("0.0") + " mesh " + (r.sharedMesh != null ? r.sharedMesh.vertexCount + " verts, normals " + (r.sharedMesh.normals.Length) + ", colours " + r.sharedMesh.colors.Length : "none") + " | " + ms);
                }

            Log("light count " + Shader.GetGlobalFloat("_MpLightCount") + ", visor " + Shader.GetGlobalFloat("_MpVisor"));
            Vector3 headNow = q.HeadPoint;
            QueenLook(headNow + new Vector3(0f, 12f, 1f), headNow);
            QueenPicture("enemyc_2_close.png");
            {
                Vector4[] lp = Shader.GetGlobalVectorArray("_MpLightPos"), lc = Shader.GetGlobalVectorArray("_MpLightColor"), la = Shader.GetGlobalVectorArray("_MpLightAtten");
                string ls = "lights at the close picture: count " + Shader.GetGlobalFloat("_MpLightCount");
                for (int i = 0; lp != null && i < lp.Length; i++) ls += "; pos " + lp[i].ToString("0.0") + " colour " + (lc != null && i < lc.Length ? lc[i].ToString("0.00") : "-") + " atten " + (la != null && i < la.Length ? la[i].ToString("0.000") : "-");
                Log(ls + " | head at " + q.HeadPoint.ToString("0.0"));
            }
            // diagnosis of the dark model: the same view with the queen unlit (white ambient, no lights)
            if (q.Anim != null)
            {
                var block = new MaterialPropertyBlock();
                foreach (SkinnedMeshRenderer r in q.Anim.Renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(block);
                    block.SetFloat("_Lit", 2f);
                    block.SetColor("_Ambient", Color.white);
                    r.SetPropertyBlock(block);
                }

                QueenPicture("enemyc_2_close_unlit.png");
                foreach (SkinnedMeshRenderer r in q.Anim.Renderers) if (r != null) r.SetPropertyBlock(null);
            }

            // what lies between Samus and the queen's head (both collision layers)
            QueenRays("eye", MpSpace.ToUnity(eye), MpSpace.ToUnity(q.HeadPoint));
            if (G.Cannon != null) QueenRays("muzzle", G.Cannon.Muzzle, MpSpace.ToUnity(q.HeadPoint));

            // shoot it: power beam from Samus's place at the head (else the pelvis); after 20 s without a hit, from just behind the railing
            float s0 = Time.time;
            lastLog = -10f;
            int fired0 = G.Shots.Fired, hits0 = G.Shots.Hits;
            bool stage2Pic = false, moved = false;
            float hp0 = q.HealthNow;
            while (!q.Dead && Time.time - s0 < 45f)
            {
                if (q.HealthNow <= 60f) MpScripts.Trace = true;
                if (!moved && Time.time - s0 > 20f && q.HealthNow >= hp0)
                {
                    moved = true;
                    Vector3 flat = q.Pos - feet;
                    flat.z = 0f;
                    flat.Normalize();
                    RaycastHit wall;
                    Vector3 chest = MpSpace.ToUnity(feet + new Vector3(0f, 0f, 1f));
                    float k = Physics.Raycast(chest, MpSpace.DirToUnity(flat), out wall, 40f * MpSpace.Scale, 1 << 0) ? wall.distance / MpSpace.Scale - 1.2f : 0f;
                    feet += flat * Mathf.Max(0f, k);
                    eye = feet + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
                    MpGame.LabFocus = feet;
                    Log("no hit in 20 s: Samus moves " + k.ToString("0.0") + " toward the queen to " + feet.ToString("0.0") + (wall.collider != null ? " (before " + wall.collider.name + ")" : ""));
                    yield return Frames(3);
                    QueenRays("eye2", MpSpace.ToUnity(eye), MpSpace.ToUnity(q.HeadPoint));
                    if (G.Cannon != null) QueenRays("muzzle2", G.Cannon.Muzzle, MpSpace.ToUnity(q.HeadPoint));
                }

                Vector3 aim = q.HeadPoint;
                RaycastHit hit;
                Vector3 from = MpSpace.ToUnity(eye), to = MpSpace.ToUnity(aim);
                bool clearHead = MpProjectiles.ShotRay(from, (to - from).normalized, (to - from).magnitude + 1f, out hit) && hit.collider.GetComponentInParent<MpHitTarget>() != null;
                if (!clearHead) aim = q.PelvisPoint;
                QueenLook(eye, aim);
                _labFire = !_labFire;
                if (Time.time - lastLog > 3f)
                {
                    lastLog = Time.time;
                    Log("shooting t " + (Time.time - s0).ToString("0.0") + " shots " + (G.Shots.Fired - fired0) + " hits " + (G.Shots.Hits - hits0) + " last " + G.Shots.LastHit + " | " + q.Report());
                    if (rings != null) Log("    " + rings.Report());
                }

                if (!stage2Pic && q.HealthNow < 0.6f * q.MaxHealth) { stage2Pic = true; QueenPicture("enemyc_3_stage2.png"); }
                yield return Frames(2);
            }

            _labFire = false;
            if (!q.Dead)
            {
                Log("not dead after 45 s of cannon fire (" + q.HealthNow + " left): hitting a queen box whenever nothing that stops shots (the rings, the area's shot collision) is in the way");
                float d0 = Time.time;
                int direct = 0, blocked = 0;
                while (!q.Dead && Time.time - d0 < 120f)
                {
                    if (q.HealthNow <= 60f) MpScripts.Trace = true;
                    foreach (Vector3 aim in new[] { q.HeadPoint, q.PelvisPoint })
                    {
                        RaycastHit hit;
                        Vector3 from = MpSpace.ToUnity(eye), to = MpSpace.ToUnity(aim);
                        if (MpParasiteQueen.BeamRay(from, (to - from).normalized, (to - from).magnitude * 0.98f, out hit)) { blocked++; continue; }
                        // the line is clear up to the box: the box itself takes the shot
                        RaycastHit[] all = Physics.RaycastAll(from, (to - from).normalized, (to - from).magnitude + 2f, 1 << MpAreaView.ShotOnlyLayer, QueryTriggerInteraction.Ignore);
                        foreach (RaycastHit h in all)
                        {
                            MpHitTarget t = h.collider.GetComponentInParent<MpHitTarget>();
                            if (t == null || t.Entity == null || !t.Entity.ToString().StartsWith("Parasite Queen")) continue;
                            t.Entity.TakeDamage(0, true, 25f);
                            direct++;
                            break;
                        }

                        break;
                    }

                    yield return Seconds(0.3f);
                }

                Log("direct hits " + direct + ", checks blocked by the rings / shot collision " + blocked);
            }

            MpScripts.Trace = true;
            if (!q.Dead)
            {
                Log("still alive: forcing damage through TakeDamage");
                while (!q.Dead) { q.TakeDamage(6, false, 50f); yield return Frames(2); }
            }

            // the death: trace every script message until Dead and the escape chain after
            MpScripts.Trace = true;
            float k0 = Time.time;
            Log("DEAD: " + q.Report() + "; death animation " + q.DeathAnimLength.ToString("0.0") + " s");
            yield return Frames(3);
            MpEntity tele = QueenFind("Timer - Parasite Queen Death Teleport Delay") as MpTimerEntity;
            Log("teleport delay timer " + (tele != null ? "timing " + ((MpTimerEntity)tele).Timing + " left " + ((MpTimerEntity)tele).TimeLeft.ToString("0.00") : "MISSING"));
            if (rings != null) Log(rings.Report());
            yield return Seconds(1f);
            QueenLook(eye, q.HeadPoint);
            QueenPicture("enemyc_4_death_1s.png");
            yield return Seconds(1f);
            if (rings != null) Log("after 2 s: " + rings.Report());
            QueenLook(eye + new Vector3(0f, 0f, 6f), q.Pos + new Vector3(0f, 0f, 6f));
            QueenPicture("enemyc_4_death_rings.png");
            yield return Seconds(4f);
            QueenLook(eye, q.HeadPoint);
            QueenPicture("enemyc_4_death_6s.png");
            while (!q.Gone && Time.time - k0 < q.DeathAnimLength + 10f) yield return null;
            Log("queen gone " + q.Gone + " after " + (Time.time - k0).ToString("0.0") + " s");
            MpEntity relay = QueenFind("Relay - Activate Teleport Trigger In Hangar");
            Log("memory relay 'Relay - Activate Teleport Trigger In Hangar' " + (relay != null ? "in mailbox " + G.Scripts.Mailbox.Contains(relay.Id) : "MISSING"));
            // the death cinematic chain ends in the escape sequence's Action (cameras -> Relay End Boss Death Cinematic -> 2 s timer)
            float e0 = Time.time;
            MpTimerEntity countdown = QueenFind("Timer Delay to Start Countdown") as MpTimerEntity;
            bool started = false;
            while (Time.time - e0 < 30f)
            {
                if (countdown != null && countdown.Timing) started = true;
                yield return null;
            }

            MpScripts.Trace = false;
            Log("escape countdown timer " + (countdown == null ? "MISSING" : started ? "started (the escape sequence's Action follows 2 s later)" : "never started (the death cinematic's cameras did not end?)"));
            QueenLook(eye, q.Pos + new Vector3(0f, 0f, 6f));
            QueenPicture("enemyc_5_after.png");
            var exitBlock = QueenFind("Platform - Door Block") as MpActorEntity;
            Log("the exit barrier 'Platform - Door Block' " + (exitBlock == null ? "MISSING" : exitBlock.Active ? "STILL UP (the way out stays blocked)" : "down (the way out is open)"));
            MpGame.LabFire = null;
        }
    }
}
