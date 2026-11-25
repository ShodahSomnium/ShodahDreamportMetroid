using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "enemyb" (run with -mparea 20, the frigate room with a gun turret and four pirates): for the turret and for
    /// each kind of Space Pirate in the start area - a picture while idle, the lab player moved in front of it (its energy,
    /// the enemy shots fired and MpEnemyShots.HitPlayer logged twice a second with the enemy's state), pictures of the wake /
    /// alert and of the attack, then the lab player's own Power Beam shots until it dies (pictures of the hit and of the death,
    /// the Dead links traced). Pictures are from a third camera to the side so the enemy and the shots show.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        partial void ExtraScenarioEnemyB(string scenario, ref IEnumerator run)
        {
            if (scenario == "enemyb") run = EnemyBScenario();
        }

        private Camera _ebCam;

        private void EbSnap(string file, Vector3 fromGame, Vector3 atGame)
        {
            if (_ebCam == null)
            {
                _ebCam = new GameObject("Lab EnemyB Camera").AddComponent<Camera>();
                _ebCam.nearClipPlane = 0.05f;
                _ebCam.farClipPlane = 2000f;
                _ebCam.fieldOfView = 60f;
                _ebCam.enabled = false;
                _ebCam.clearFlags = CameraClearFlags.SolidColor;
                _ebCam.backgroundColor = Color.black;
                _ebCam.cullingMask = ~(1 << 5);
            }

            _ebCam.transform.position = MpSpace.ToUnity(fromGame);
            _ebCam.transform.LookAt(MpSpace.ToUnity(atGame), Vector3.up);
            MpShaderGlobals.Apply(_ebCam);
            Shoot(_ebCam, file, 1280, 720);
        }

        /// <summary>A side view of the player and the enemy: off the line between them, a little up.</summary>
        private void EbSide(string file, Vector3 enemyGame, float up = 1.5f)
        {
            Vector3 player = G.PlayerGame;
            Vector3 mid = (player + enemyGame) * 0.5f + new Vector3(0f, 0f, up);
            Vector3 line = enemyGame - player;
            line.z = 0f;
            Vector3 side = line.sqrMagnitude > 1e-4f ? new Vector3(line.y, -line.x, 0f).normalized : Vector3.right;
            float dist = Mathf.Max(5f, line.magnitude * 0.8f);
            Vector3 from = mid + side * dist + new Vector3(0f, 0f, 1.5f);
            if (!MpEnemy.LineOfSight(mid, from)) from = mid - side * dist + new Vector3(0f, 0f, 1.5f);
            if (!MpEnemy.LineOfSight(mid, from)) from = player + new Vector3(0f, 0f, 3.5f) - line.normalized * 2f;
            EbHidePlayer(true);
            EbSnap(file, from, mid);
            EbHidePlayer(false);
        }

        private readonly List<Renderer> _ebHidden = new List<Renderer>();

        /// <summary>The lab player's own cannon and helmet (near its camera, outside the world root) off for a third-person picture.</summary>
        private void EbHidePlayer(bool hide)
        {
            if (!hide) { foreach (Renderer r in _ebHidden) if (r != null) r.enabled = true; _ebHidden.Clear(); return; }
            Vector3 c = _cam.transform.position;
            float near = 3f * MpSpace.Scale;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || (MpSpace.Root != null && r.transform.IsChildOf(MpSpace.Root))) continue;
                if ((r.bounds.center - c).magnitude > near) continue;
                r.enabled = false;
                _ebHidden.Add(r);
            }
        }

        /// <summary>A close picture of an enemy: from about <paramref name="dist"/> away beside the line to the player, where nothing is in the way.</summary>
        private void EbClose(string file, Vector3 focusGame, float dist = 5f, float up = 0.8f)
        {
            Vector3 toPlayer = G.PlayerGame - focusGame;
            toPlayer.z = 0f;
            float baseYaw = toPlayer.sqrMagnitude > 1e-4f ? Mathf.Atan2(-toPlayer.x, toPlayer.y) * Mathf.Rad2Deg : 0f;
            float[] offs = { 40f, -40f, 75f, -75f, 15f, -15f, 110f, -110f, 150f, -150f, 180f };
            Vector3 from = focusGame + new Vector3(0f, 0f, 2f);
            foreach (float o in offs)
            {
                float r = (baseYaw + o) * Mathf.Deg2Rad;
                Vector3 p = focusGame + new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f) * dist + new Vector3(0f, 0f, up);
                if (MpEnemy.LineOfSight(focusGame, p) && MpEnemy.LineOfSight(p, p + new Vector3(0f, 0f, -1.5f))) { from = p; break; }
            }

            EbHidePlayer(true);
            EbSnap(file, from, focusGame);
            EbHidePlayer(false);
        }

        private float EbFloor(Vector3 p)
        {
            RaycastHit hit;
            if (MpEnemy.WorldRay(MpSpace.ToUnity(p + new Vector3(0f, 0f, 1f)), Vector3.down, 40f * MpSpace.Scale, out hit)) return MpSpace.ToGame(hit.point).z;
            return p.z;
        }

        /// <summary>A floor spot about <paramref name="dist"/> from the enemy, in its sight; the front first, then around.</summary>
        private Vector3 EbSpot(Vector3 enemy, float faceYaw, float dist, float eyeZ)
        {
            for (int k = 0; k < 16; k++)
            {
                float yaw = faceYaw + (k % 2 == 0 ? 1 : -1) * (k / 2) * 30f;
                float r = yaw * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f);
                Vector3 p = enemy + dir * dist;
                p.z = EbFloor(new Vector3(p.x, p.y, enemy.z + 1f));
                if (Mathf.Abs(p.z - EbFloor(enemy + new Vector3(0f, 0f, 1f))) > 3f && k < 15) continue;
                if (MpEnemy.LineOfSight(enemy + new Vector3(0f, 0f, eyeZ), p + new Vector3(0f, 0f, MpSpace.SamusEyeHeight))) return p;
            }

            Vector3 f = enemy + new Vector3(0f, -dist, 0f);
            f.z = EbFloor(f);
            return f;
        }

        private void EbAim(Vector3 targetGame)
        {
            Vector3 eye = G.PlayerGame + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
            _cam.transform.position = MpSpace.ToUnity(eye);
            _cam.transform.rotation = Quaternion.LookRotation(MpSpace.ToUnity(targetGame) - _cam.transform.position, Vector3.up);
        }

        private void EbKeepAlive()
        {
            if (G.State.Energy < 30f)
            {
                Log("  (lab: energy " + G.State.Energy + " refilled to 99)");
                G.State.Energy = 99f;
            }
        }

        private IEnumerator EnemyBScenario()
        {
            Vector3 spawn = G.PlayerGame;
            _ebSpawn = spawn;
            MpGame.LabFocus = spawn;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFire = g => _labFire;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            Time.captureDeltaTime = 1f / 30f;
            yield return Frames(20);
            int area = G.WorldView.CurrentArea;
            _ebTag = "eb" + area;
            Log("enemyb: area " + area + " '" + G.World.Areas[area].Name + "', spawn " + spawn.ToString("F1") + ", energy " + G.State.Energy);
            var turrets = new List<MpGunTurret>();
            var pirates = new List<MpSpacePirate>();
            foreach (MpEntity e in G.Scripts.All)
            {
                var t = e as MpGunTurret;
                if (t != null)
                {
                    Log("  turret " + t + " " + (t.D.IsGun ? "gun" : "base") + " at " + t.Pos.ToString("F1") + " active " + t.Active + " state " + t.State + " hp " + t.HealthNow + (t.Gun != null ? " gun " + t.Gun.Id.ToString("X8") : "") + " char " + (t.Char != null ? t.Char.Report : "NONE"));
                    if (!t.D.IsGun && t.Gun != null && t.Area == area) turrets.Add(t);
                }

                var p = e as MpSpacePirate;
                if (p != null)
                {
                    Log("  pirate " + p + " at " + p.Pos.ToString("F1") + " active " + p.Active + " flags 0x" + p.P.Flags.ToString("X") + " hp " + p.HealthNow + " mode " + p.ModeName + " char " + (p.Char != null ? p.Char.Report : "NONE"));
                    if (p.Area == area) pirates.Add(p);
                }
            }

            foreach (MpGunTurret t in turrets) yield return EbTurret(t);

            // one pirate of each kind: a fighter, an injured walker, a seated one
            var done = new HashSet<string>();
            foreach (MpSpacePirate p in pirates)
            {
                string kind = p.Seated ? "seated" : p.Injured ? "injured" : p.Ceiling ? "ceiling" : "fighter";
                if (!done.Add(kind)) continue;
                yield return EbPirate(p, kind);
            }

            Log("enemyb: totals - enemy shots fired " + MpEnemyShots.Fired + ", hit the player " + MpEnemyShots.HitPlayer + ", turret launches " + MpGunTurret.Launched + ", pirate shots " + MpSpacePirate.ShotsFired + ", blade hits " + MpSpacePirate.BladeHits + ", energy " + G.State.Energy);
            Time.captureDeltaTime = 0f;
            MpGame.LabFire = null;
        }

        private IEnumerator EbTurret(MpGunTurret t)
        {
            MpGunTurret gun = t.Gun;
            Log("turret '" + t.Name + "': det " + t.D.DetectionRange + " turn " + t.D.TurnSpeed + " reload " + t.D.ReloadTime + "+" + t.D.ReloadTimeVariance + " shots " + t.D.NumInitialShots + "/" + t.D.InitialShotTableIndex + "/" + t.D.NumSubsequentShots + ", gun hp " + gun.HealthNow + " wpsc " + gun.D.Projectile.ToString("X8") + " dmg " + gun.D.ProjectileDamage.Damage);
            Vector3 spot = EbSpot(gun.Pos, t.Yaw, 9f, 0f);
            Vector3 view = spot + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
            Log("  idle: state " + t.State + ", player at spawn " + G.PlayerGame.ToString("F1") + " (" + (G.PlayerGame - t.Pos).magnitude.ToString("0.0") + " away), in detection " + t.InDetectionRange());
            EbSnap(_ebTag + "_turret_idle.png", view, gun.Pos + new Vector3(0f, 0f, 0.5f));
            EbClose(_ebTag + "_turret_idle_close.png", gun.Pos, 5f, -1f);
            Log("  extension model " + t.HasExtension + ", base z " + t.Pos.z.ToString("0.00"));

            MpGame.LabFocus = spot;
            yield return Frames(2);
            float e0 = G.State.Energy;
            int hit0 = MpEnemyShots.HitPlayer, fired0 = MpEnemyShots.Fired;
            Log("  player moved to " + spot.ToString("F1") + " (" + (spot - gun.Pos).magnitude.ToString("0.0") + " from the gun), energy " + e0);
            EbAim(gun.Pos);
            var seen = new HashSet<MpGunTurret.St>();
            float t1 = Time.time, lastLog = -1f;
            int pics = 0, lastLaunch = MpGunTurret.Launched;
            while (Time.time - t1 < 9f)
            {
                EbAim(gun.Pos);
                if (seen.Add(t.State)) Log("  state " + t.State + " at " + (Time.time - t1).ToString("0.00") + " s");
                if (Time.time - lastLog >= 0.5f)
                {
                    lastLog = Time.time;
                    Log("  t " + (Time.time - t1).ToString("0.0") + " state " + t.State + " yaw " + t.Yaw.ToString("0") + " base z " + t.Pos.z.ToString("0.00") + " gun pitch " + t.Pitch.ToString("0") + " ext " + t.ExtensionT.ToString("0.00") + " energy " + G.State.Energy + " shots " + (MpEnemyShots.Fired - fired0) + " hit player " + (MpEnemyShots.HitPlayer - hit0));
                    EbKeepAlive();
                }

                if (pics == 0 && (t.State == MpGunTurret.St.Ready || t.State == MpGunTurret.St.PanningA)) { EbSnap(_ebTag + "_turret_wake.png", view, gun.Pos); EbClose(_ebTag + "_turret_wake_close.png", gun.Pos, 5f, -1f); pics++; }
                if (pics <= 1 && t.State == MpGunTurret.St.Firing) { EbClose(_ebTag + "_turret_charge.png", gun.Pos, 5f, -1f); pics = 2; }
                if (pics <= 2 && MpGunTurret.Launched > lastLaunch) { yield return Frames(3); EbSide(_ebTag + "_turret_fire.png", gun.Pos, -1f); EbClose(_ebTag + "_turret_fire_close.png", gun.Pos, 5f, -1f); EbSnap(_ebTag + "_turret_fire_pov.png", view, gun.Pos); pics = 3; }
                if (pics == 3 && MpEnemyShots.HitPlayer > hit0) { EbSnap(_ebTag + "_turret_hit_player.png", view, gun.Pos); pics = 4; }
                yield return null;
            }

            Log("  attack: energy " + e0 + " -> " + G.State.Energy + ", turret launches " + (MpGunTurret.Launched - lastLaunch) + ", enemy shots " + (MpEnemyShots.Fired - fired0) + ", hit the player " + (MpEnemyShots.HitPlayer - hit0));

            // the lab player shoots the gun (Power Beam presses) until it is destroyed
            MpScripts.Trace = true;
            yield return EbShootDown(gun, () => gun.Pos + gun.Body.localRotation * new Vector3(0f, 0.8f, 0f), _ebTag + "_turret", 14f);
            yield return Frames(4);
            MpScripts.Trace = false;
            Log("  after: base state " + t.State + ", base active " + t.Active + ", gun gone " + gun.Gone + ", gun dead " + gun.Dead);
            EbSnap(_ebTag + "_turret_destroyed.png", view, t.Pos);
            EbClose(_ebTag + "_turret_destroyed_close.png", t.Pos, 6f, -1.5f);
            yield return Seconds(1.5f);
            EbSnap(_ebTag + "_turret_destroyed_2.png", view, t.Pos);
        }

        /// <summary>Fires the lab player's beam at an enemy (presses every few frames) until it dies; a direct hit after the time limit if not.</summary>
        private IEnumerator EbShootDown(MpEnemy e, System.Func<Vector3> aim, string tag, float limit)
        {
            float t1 = Time.time, lastLog = -1f;
            int shots0 = G.Shots.Fired, hits0 = G.Shots.Hits, frame = 0;
            bool hitPic = false;
            float hp0 = e.HealthNow;
            while (!e.Dead && !e.Gone && Time.time - t1 < limit)
            {
                EbAim(aim());
                _labFire = frame % 8 < 2;
                frame++;
                if (Time.time - lastLog >= 0.5f)
                {
                    lastLog = Time.time;
                    var p = e as MpSpacePirate;
                    Log("  shooting: t " + (Time.time - t1).ToString("0.0") + " hp " + e.HealthNow + " player shots " + (G.Shots.Fired - shots0) + " hits " + (G.Shots.Hits - hits0) + (p != null ? " mode " + p.ModeName : "") + " energy " + G.State.Energy);
                    EbKeepAlive();
                }

                if (!hitPic && e.HealthNow < hp0)
                {
                    hitPic = true;
                    yield return Frames(2);
                    EbClose(tag + "_hurt.png", e.Pos + new Vector3(0f, 0f, e is MpGunTurret ? 0f : 1.2f));
                    Shoot(_cam, tag + "_hurt_pov.png", 1280, 720);
                }

                yield return null;
            }

            _labFire = false;
            if (!e.Dead && !e.Gone)
            {
                Log("  FALLBACK: not dead after " + limit + " s of shots (hp " + e.HealthNow + ") - a direct 200 damage hit");
                e.TakeDamage(0, true, 200f);
            }

            Log("  killed at " + (Time.time - t1).ToString("0.0") + " s by " + (G.Shots.Fired - shots0) + " player shots (" + (G.Shots.Hits - hits0) + " hits)");
        }

        private IEnumerator EbPirate(MpSpacePirate p, string kind)
        {
            Log("pirate '" + p.Name + "' (" + kind + "): hp " + p.HealthNow + " det " + p.Info.DetectionRange + "/" + p.Info.DetectionAngle + " attack " + p.Info.AverageAttackTime + "+" + p.Info.AttackTimeVariation + " wpsc " + p.P.Projectile.ToString("X8") + " dmg " + p.P.ProjectileDamage.Damage + " blade " + p.P.BladeDamage.Damage + " flags 0x" + p.P.Flags.ToString("X"));
            MpGame.LabFocus = _ebSpawn;
            if (!p.Active)
            {
                p.OnMessage(MpScriptMsg.Activate, null);
                Log("  activated (as its trigger does): active " + p.Active);
            }

            yield return Frames(10);
            Vector3 spot = EbSpot(p.Pos, p.Yaw, p.Seated ? 7f : 10f, 2f);
            Vector3 chest = p.Pos + new Vector3(0f, 0f, p.Seated ? 0.8f : 1.6f);
            EbSnap(_ebTag + "_" + kind + "_idle.png", spot + new Vector3(0f, 0f, MpSpace.SamusEyeHeight), chest);
            EbClose(_ebTag + "_" + kind + "_idle_close.png", chest);
            Log("  idle: mode " + p.ModeName + " at " + p.Pos.ToString("F1") + " yaw " + p.Yaw.ToString("0"));

            float e0 = G.State.Energy;
            int hit0 = MpEnemyShots.HitPlayer, shots0 = MpSpacePirate.ShotsFired;
            MpGame.LabFocus = spot;
            Log("  player moved to " + spot.ToString("F1") + " (" + (spot - p.Pos).magnitude.ToString("0.0") + " away)");
            float t1 = Time.time, lastLog = -1f;
            string lastMode = "";
            bool alertPic = false, firePic = false, prodded = false;
            while (Time.time - t1 < 10f && !p.Dead)
            {
                EbAim(p.Pos + new Vector3(0f, 0f, 1.5f));
                if (p.ModeName != lastMode) { lastMode = p.ModeName; Log("  mode " + lastMode + " at " + (Time.time - t1).ToString("0.00") + " s"); }
                if (!prodded && Time.time - t1 > 3f && (p.ModeName == "Idle" || p.ModeName == "Patrol" || p.ModeName == "Hang"))
                {
                    prodded = true;
                    Log("  not alerted by sight / hearing in 3 s: Activate sent (its trigger)");
                    p.OnMessage(MpScriptMsg.Activate, null);
                }

                if (Time.time - lastLog >= 0.5f)
                {
                    lastLog = Time.time;
                    Log("  t " + (Time.time - t1).ToString("0.0") + " mode " + p.ModeName + " pos " + p.Pos.ToString("F1") + " yaw " + p.Yaw.ToString("0") + " anim " + (p.Anim != null ? p.Anim.Anim.ToString() : "-") + " energy " + G.State.Energy + " shots " + (MpSpacePirate.ShotsFired - shots0) + " hit player " + (MpEnemyShots.HitPlayer - hit0));
                    EbKeepAlive();
                }

                if (!alertPic && (p.ModeName == "Alert" || p.ModeName == "SitUp" || p.ModeName == "Drop" || p.ModeName == "Fall") && Time.time - t1 > 0.1f)
                {
                    alertPic = true;
                    yield return Frames(8);
                    EbClose(_ebTag + "_" + kind + "_alert.png", p.Pos + new Vector3(0f, 0f, 1.2f));
                }

                if (!firePic && MpSpacePirate.ShotsFired > shots0)
                {
                    firePic = true;
                    yield return Frames(4);
                    EbSide(_ebTag + "_" + kind + "_attack.png", p.Pos);
                    EbClose(_ebTag + "_" + kind + "_attack_close.png", p.Pos + new Vector3(0f, 0f, 1.2f));
                    Shoot(_cam, _ebTag + "_" + kind + "_attack_pov.png", 1280, 720);
                }

                yield return null;
            }

            Log("  attack: energy " + e0 + " -> " + G.State.Energy + ", pirate shots " + (MpSpacePirate.ShotsFired - shots0) + ", hit the player " + (MpEnemyShots.HitPlayer - hit0) + ", blade hits " + MpSpacePirate.BladeHits);
            EbClose(_ebTag + "_" + kind + "_combat.png", p.Pos + new Vector3(0f, 0f, 1.2f));
            if (p.CanMelee)
            {
                // close in: the arm blade
                int blade0 = MpSpacePirate.BladeHits;
                float eb = G.State.Energy;
                Vector3 near = p.Pos + p.Facing * 2.5f;
                near.z = EbFloor(near + new Vector3(0f, 0f, 1f));
                MpGame.LabFocus = near;
                bool meleePic = false;
                float t2 = Time.time;
                while (Time.time - t2 < 4f && !p.Dead)
                {
                    EbAim(p.Pos + new Vector3(0f, 0f, 1.5f));
                    if (!meleePic && p.ModeName == "Melee" && p.Anim != null && p.Anim.Time > 0.3f) { meleePic = true; EbClose(_ebTag + "_" + kind + "_melee.png", p.Pos + new Vector3(0f, 0f, 1.2f)); }
                    yield return null;
                }

                Log("  melee: player 2.5 in front for 4 s - blade hits " + (MpSpacePirate.BladeHits - blade0) + ", energy " + eb + " -> " + G.State.Energy + ", mode " + p.ModeName);
                EbKeepAlive();
                MpGame.LabFocus = spot;
            }


            MpScripts.Trace = true;
            yield return EbShootDown(p, () => p.Pos + new Vector3(0f, 0f, p.Seated ? 0.8f : 1.5f), _ebTag + "_" + kind, 15f);
            yield return Frames(3);
            MpScripts.Trace = false;
            Log("  dead " + p.Dead + ", anim " + (p.Anim != null ? p.Anim.Anim + " " + p.Anim.Duration.ToString("0.00") + " s" : "-"));
            EbClose(_ebTag + "_" + kind + "_death.png", p.Pos + new Vector3(0f, 0f, 1f));
            yield return Seconds(1f);
            EbClose(_ebTag + "_" + kind + "_death_2.png", p.Pos + new Vector3(0f, 0f, 1f));
            yield return Seconds(2f);
            EbClose(_ebTag + "_" + kind + "_dead.png", p.Pos + new Vector3(0f, 0f, 0.8f));
        }

        private Vector3 _ebSpawn;
        private string _ebTag = "eb";
    }
}
