using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "coop": two Unity processes play together over MpLabLink (DevTools/coop.sh starts both: -mpcoop master | helper,
    /// -mpport). The master boots the frigate; the helper boots, asks for the game and joins it. Then, in step through lab notes:
    /// enemy health x1.25 on both; the master's shot is seen by the helper (Fire), an enemy's hit on the helper goes to it (Hurt), an
    /// unlock the master gets is the helper's (Unlocks); the master breaks a door lock - the helper sees the lock go and the door open
    /// (Script, State); the helper breaks another lock - its hit goes to the master (Damage), the master's world answers; the master
    /// goes to the Chozo Ruins (Ruins Entrance) and the helper follows; a beetle woken by the master is puppeted on the helper (EnemyState)
    /// and the helper kills it (Damage -> the master's death -> EnemyDie); the master goes to Tallon IV and the helper follows; the
    /// master's process quits and the helper becomes the master (the hand-over), its script world running on its own.
    /// "LAB: CHECK name ok|FAIL" lines are the verdicts.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private MpLabLink _link;
        private int _checks, _checkFails;

        partial void ExtraScenarioCoop(string scenario, ref IEnumerator run)
        {
            if (scenario != "coop") return;
            run = MpLabBoot.Arg("-mpcoop", "master") == "helper" ? CoopHelper() : CoopMaster();
        }

        /// <summary>Called from Awake: the lab link is in place before the game's first co-op tick.</summary>
        private void CoopLinkUp()
        {
            string role = MpLabBoot.Arg("-mpcoop", null);
            if (role == null) return;
            int port = int.Parse(MpLabBoot.Arg("-mpport", "47611"));
            _link = new MpLabLink(role != "helper", port);
            MpCoop.LabLink = _link;
            Log("coop: lab link as " + role + " on port " + port);
        }

        private void Check(string name, bool ok, string detail)
        {
            _checks++;
            if (!ok) { _checkFails++; _failed = true; }
            Log("CHECK " + name + " " + (ok ? "ok" : "FAIL") + (detail != null ? " (" + detail + ")" : ""));
        }

        private IEnumerator Note(string want, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                string s;
                while (_link != null && _link.TryNote(out s))
                {
                    _lastNote = s;
                    if (s.StartsWith(want, System.StringComparison.Ordinal)) yield break;
                }

                yield return null;
            }

            Log("TIMEOUT waiting for the note '" + want + "'");
            _lastNote = null;
        }

        private string _lastNote;

        private uint NoteId() { string[] w = _lastNote != null ? _lastNote.Split(' ') : new string[0]; return w.Length > 1 ? uint.Parse(w[1], System.Globalization.NumberStyles.HexNumber) : 0u; }

        private int NoteNumber() { string[] w = _lastNote != null ? _lastNote.Split(' ') : new string[0]; return w.Length > 2 ? int.Parse(w[2]) : -1; }

        private int _lockOpen;

        private IEnumerator SkipCinema()
        {
            for (int k = 0; k < 20 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(3); }
        }

        private IEnumerator SettleRooms()
        {
            float t0 = Time.realtimeSinceStartup;
            while ((G.WorldView.IsBusy || G.Warping || G.SwitchingWorld) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            yield return Frames(30);
        }

        private MpRemote Peer() { return G.Net != null && G.Net.Remotes.Count > 0 ? G.Net.Remotes[0] : null; }

        private bool PeerJoinedIn(string pak)
        {
            MpRemote p = Peer();
            return p != null && (p.Ints[MpNetWire.PlayerSlot.AtFlags] & MpNetWire.PlayerSlot.FlagJoined) != 0 && p.Ints[MpNetWire.PlayerSlot.AtPak] == MpNetWire.PakHash(pak);
        }

        /// <summary>An active door lock (DamageableTrigger) in the rooms loaded here, not the one given.</summary>
        private MpDamageableTriggerEntity FindLock(uint not)
        {
            MpDamageableTriggerEntity any = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                var dt = e as MpDamageableTriggerEntity;
                if (dt == null || !dt.Active || dt.NetId == not || dt.Vuln == null || dt.Vuln.Damage(0, false, 100f) <= 0f) continue;
                // a plain door lock (not one of several "component" switches) next to a closed door
                if (dt.Name.IndexOf("component", System.StringComparison.OrdinalIgnoreCase) < 0 && DoorsNear(G.Scripts, dt).Contains("closed")) return dt;
                if (any == null) any = dt;
            }

            return any;
        }

        private static int OpenNear(MpScripts s, MpDamageableTriggerEntity dt)
        {
            Vector3 c = (dt.Min + dt.Max) * 0.5f;
            int n = 0;
            foreach (MpEntity e in s.All) { var d = e as MpDoorEntity; if (d != null && d.IsOpen && (d.GamePos - c).sqrMagnitude < 36f) n++; }
            return n;
        }

        private static string DoorsNear(MpScripts s, MpDamageableTriggerEntity dt)
        {
            Vector3 c = (dt.Min + dt.Max) * 0.5f;
            var parts = new List<string>();
            foreach (MpEntity e in s.All)
            {
                var d = e as MpDoorEntity;
                if (d != null && (d.GamePos - c).sqrMagnitude < 36f) parts.Add(d.Name + (d.IsOpen ? " open" : " closed"));
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no door within 6";
        }

        private static bool AnyDoorOpenNear(MpScripts s, MpDamageableTriggerEntity dt)
        {
            Vector3 c = (dt.Min + dt.Max) * 0.5f;
            foreach (MpEntity e in s.All)
            {
                var d = e as MpDoorEntity;
                if (d != null && d.IsOpen && (d.GamePos - c).sqrMagnitude < 36f) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the master

        private IEnumerator CoopMaster()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFocus = G.PlayerGame;
            yield return SkipCinema();
            yield return SettleRooms();
            Log("coop master: waiting for the helper to join");
            yield return Wait(() => PeerJoinedIn(G.CurrentPak), 600f, "the helper to join");
            if (_failed) yield break;
            yield return Seconds(3f);
            Check("master-players", MpCoop.Players == 2, "players " + MpCoop.Players);
            Check("master-health-scale", Mathf.Abs(MpEnemy.HealthScale - 1.25f) < 1e-3f, "x" + MpEnemy.HealthScale);
            MpEnemy anyEnemy = null;
            foreach (MpEntity e in G.Scripts.All) { var en = e as MpEnemy; if (en != null && en.BaseMaxHealth > 0f) { anyEnemy = en; break; } }
            if (anyEnemy != null) Check("master-enemy-health", Mathf.Abs(anyEnemy.MaxHealth - anyEnemy.BaseMaxHealth * 1.25f) < 0.01f, anyEnemy + " " + anyEnemy.BaseMaxHealth + " -> " + anyEnemy.MaxHealth);
            else Log("coop master: no enemy loaded to check the health on");

            // a shot, a hit on the helper, an unlock
            Vector3 eye = G.PlayerGame + new Vector3(0f, 0f, MpSpace.SamusEyeHeight);
            G.Shots.FireBeam(0, false, MpSpace.ToUnity(eye), MpSpace.DirToUnity(new Vector3(0f, 1f, 0f)));
            MpEnemy.FocusPlayer = 2;
            bool sent = G.DamagePlayer(3f, G.PlayerGame);
            MpEnemy.FocusPlayer = -1;
            Check("master-hurt-sent", sent, null);
            G.State.Items[MpPlayerState.ItemSpaceJump] = 1;
            G.State.Items[MpPlayerState.ItemVaria] = 1;
            yield return Seconds(1f);
            _link.Note("basics");

            // a door lock broken here: the helper sees it
            MpDamageableTriggerEntity dt = FindLock(0u);
            if (dt == null) { Check("master-lock-found", false, "no active lock loaded"); yield break; }
            Log("coop master: breaking " + dt + " (" + DoorsNear(G.Scripts, dt) + ")");
            dt.TakeDamage(0, false, 1000f);
            yield return Seconds(3f);
            Check("master-lock-broken", !dt.Active, DoorsNear(G.Scripts, dt));
            _lockOpen = OpenNear(G.Scripts, dt);
            _link.Note("lock1 " + dt.NetId.ToString("X8") + " " + _lockOpen);
            yield return Note("lock1-seen", 30f);

            // the helper breaks another
            int before = MpCoop.LabDamageIn;
            _link.Note("lock2 " + dt.NetId.ToString("X8"));
            yield return Wait(() => MpCoop.LabDamageIn > before, 60f, "the helper's hit");
            Check("master-helper-hit", MpCoop.LabDamageIn > before, "damage messages " + (MpCoop.LabDamageIn - before));
            yield return Note("lock2-done", 30f);

            // the Chozo Ruins: the helper follows
            Log("coop master: off to the Chozo Ruins (Ruins Entrance)");
            yield return G.SwitchWorld("Metroid2.pak", 0, 1, false, null);
            MpGame.LabFocus = G.PlayerGame;
            yield return SettleRooms();
            yield return Wait(() => PeerJoinedIn("Metroid2.pak"), 240f, "the helper in the Chozo Ruins");
            Check("master-helper-followed-chozo", PeerJoinedIn("Metroid2.pak"), null);
            yield return Seconds(4f);
            MpBeetle beetle = null;
            foreach (MpEntity e in G.Scripts.All) if (e.Area == 1 && e is MpBeetle && !((MpBeetle)e).Dead) { beetle = (MpBeetle)e; break; }
            if (beetle == null) { Check("master-beetle-found", false, null); yield break; }
            beetle.OnMessage(MpScriptMsg.Activate, null);
            Log("coop master: woke " + beetle + " health " + beetle.HealthNow + "/" + beetle.MaxHealth + " (base " + beetle.BaseMaxHealth + ")");
            Check("master-beetle-health", Mathf.Abs(beetle.MaxHealth - beetle.BaseMaxHealth * 1.25f) < 0.01f, beetle.BaseMaxHealth + " -> " + beetle.MaxHealth);
            yield return Seconds(2f);
            _link.Note("beetle " + beetle.NetId.ToString("X8"));
            yield return Wait(() => beetle.Dead || beetle.Gone, 60f, "the helper to kill the beetle");
            Check("master-beetle-killed-by-helper", beetle.Dead || beetle.Gone, "health " + beetle.HealthNow);
            yield return Note("beetle-done", 30f);

            // Tallon IV
            Log("coop master: off to Tallon IV (Landing Site)");
            yield return G.SwitchWorld(MpGame.TallonPak, 0, 0, false, null);
            MpGame.LabFocus = G.PlayerGame;
            yield return SettleRooms();
            yield return Wait(() => PeerJoinedIn(MpGame.TallonPak), 240f, "the helper on Tallon IV");
            Check("master-helper-followed-tallon", PeerJoinedIn(MpGame.TallonPak), null);
            yield return Seconds(3f);
            Log("coop master: script deliveries sent " + MpCoop.LabScriptOut + ", damage in " + MpCoop.LabDamageIn + "; checks " + _checks + ", failed " + _checkFails);
            _link.Note("bye");
            yield return Seconds(2f);
        }

        // ------------------------------------------------------------------ the helper

        private IEnumerator CoopHelper()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            MpGame.LabFocus = G.PlayerGame;
            yield return SkipCinema();
            Log("coop helper: waiting to join the master's game");
            yield return Wait(() => MpCoop.Helper && MpScripts.Follower && !G.Warping, 600f, "the join");
            if (_failed) yield break;
            yield return SettleRooms();
            Check("helper-joined", MpCoop.Helper && G.CurrentPak == MpGame.StartPak, "'" + G.CurrentPak + "' room " + G.WorldView.CurrentArea + " at " + G.PlayerGame);
            yield return Note("basics", 120f);
            yield return Seconds(1.5f);
            Check("helper-players", MpCoop.Players == 2, "players " + MpCoop.Players);
            Check("helper-health-scale", Mathf.Abs(MpEnemy.HealthScale - 1.25f) < 1e-3f, "x" + MpEnemy.HealthScale);
            Check("helper-saw-shot", MpCoop.LabFireIn > 0, "shots " + MpCoop.LabFireIn);
            Check("helper-got-hurt", MpCoop.LabHurtIn > 0, "hurts " + MpCoop.LabHurtIn);
            Check("helper-unlocks", G.State.Items[MpPlayerState.ItemSpaceJump] > 0 && G.State.Suit == MpPlayerState.SuitVaria, "space jump " + G.State.Items[MpPlayerState.ItemSpaceJump] + ", suit " + G.State.Suit);
            Check("helper-scripts-mirrored", MpCoop.LabScriptIn > 0, "script deliveries " + MpCoop.LabScriptIn);
            Check("helper-no-save", G.Save(MpGame.SaveSlot, "lab") != "Saved", null);

            yield return Note("lock1", 120f);
            uint id = NoteId();
            int masterOpen = NoteNumber();
            var dt = G.Scripts.FindNet(id) as MpDamageableTriggerEntity;
            if (dt == null) { Check("helper-lock1-loaded", false, id.ToString("X8") + " not loaded here"); }
            else
            {
                yield return Wait(() => !dt.Active && OpenNear(G.Scripts, dt) == masterOpen, 15f, "the master's lock and doors");
                Check("helper-lock1-mirrored", !dt.Active && OpenNear(G.Scripts, dt) == masterOpen, "doors open here " + OpenNear(G.Scripts, dt) + ", at the master " + masterOpen + ": " + DoorsNear(G.Scripts, dt));
            }

            _link.Note("lock1-seen");
            yield return Note("lock2", 60f);
            MpDamageableTriggerEntity mine = FindLock(id);
            if (mine == null) Check("helper-lock2-found", false, "no other active lock loaded");
            else
            {
                Log("coop helper: breaking " + mine + " (" + DoorsNear(G.Scripts, mine) + ")");
                MpProjectiles.HitHook(mine, 0, false, 1000f);
                Check("helper-lock2-not-local", mine.Active, "a helper's hit waits for the master");
                yield return Wait(() => !mine.Active, 15f, "the master to break the helper's lock");
                Check("helper-lock2-by-master", !mine.Active, DoorsNear(G.Scripts, mine));
            }

            _link.Note("lock2-done");

            // following into the Chozo Ruins
            yield return Wait(() => G.CurrentPak == "Metroid2.pak" && MpScripts.Follower && !G.SwitchingWorld && !G.Warping, 240f, "following to the Chozo Ruins");
            yield return SettleRooms();
            Check("helper-followed-chozo", G.CurrentPak == "Metroid2.pak", "room " + G.WorldView.CurrentArea);
            yield return Note("beetle", 120f);
            var beetle = G.Scripts.FindNet(NoteId()) as MpBeetle;
            if (beetle == null) Check("helper-beetle-loaded", false, NoteId().ToString("X8"));
            else
            {
                yield return Wait(() => beetle.HasNet && beetle.Active, 15f, "the master's beetle state");
                Check("helper-beetle-puppet", beetle.HasNet && beetle.Active, "at " + beetle.Pos + " (master " + beetle.NetPos + "), anim " + beetle.NetAnim + ", health " + beetle.HealthNow + "/" + beetle.MaxHealth);
                MpProjectiles.HitHook(beetle, 0, false, beetle.MaxHealth * 4f);
                yield return Wait(() => beetle.Dead || beetle.Gone, 20f, "the beetle's death from the master");
                Check("helper-beetle-died", beetle.Dead || beetle.Gone, null);
            }

            _link.Note("beetle-done");

            // following to Tallon IV
            yield return Wait(() => G.CurrentPak == MpGame.TallonPak && MpScripts.Follower && !G.SwitchingWorld && !G.Warping, 240f, "following to Tallon IV");
            yield return SettleRooms();
            Check("helper-followed-tallon", G.CurrentPak == MpGame.TallonPak, "room " + G.WorldView.CurrentArea);
            Log("coop helper: remote cannons shown " + MpRemoteCannons.LabShown + " (this scenario has no stand-in avatar to carry one: see coopcannon)");
            Shoot(_cam, "coop_helper_tallon.png", 960, 540);

            // the master leaves: this client goes on as the master
            yield return Note("bye", 120f);
            yield return Wait(() => MpCoop.IsMaster && !MpScripts.Follower, 60f, "the hand-over");
            Check("helper-became-master", MpCoop.IsMaster && !MpScripts.Follower && !MpCoop.Helper, null);
            Log("coop helper: script deliveries in " + MpCoop.LabScriptIn + ", states " + MpCoop.LabStateIn + ", enemy states " + MpCoop.LabEnemyIn + ", shots " + MpCoop.LabFireIn + ", joins " + MpCoop.LabJoins + "; checks " + _checks + ", failed " + _checkFails);
        }
    }
}
