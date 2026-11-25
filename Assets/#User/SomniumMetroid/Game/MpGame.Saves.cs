using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Saving, loading, respawning and moving between rooms. A save (station or tablet) and a checkpoint (each new room entered)
    /// hold the player's state and the world's script state (MpSaveGame). Loading or respawning drops every loaded room, puts the
    /// layers, mailbox and inventory back, loads the target room and only then moves the player there (so they never fall
    /// through a room that is not built yet). Death (energy gone): the HUD says so, then Samus comes back at the last checkpoint
    /// (or save) with full energy. The tablet reaches all of this through MpGameActions.
    /// </summary>
    public sealed partial class MpGame
    {
        public double PlaySeconds;
        private bool _warping;
        private int _checkpointArea = -1;
        private float _deadT = -1f;
        public bool Warping { get { return _warping; } }

        public const string SaveSlot = "save", CheckpointSlot = "checkpoint";

        private void InstallActions()
        {
            MpGameActions.SaveGame = () => Save(SaveSlot, "tablet");
            MpGameActions.LoadGame = () => LoadSlot(SaveSlot);
            MpGameActions.Respawn = () => { Respawn("tablet"); return "Back at the last checkpoint"; };
            MpGameActions.SaveInfo = () =>
            {
                MpSaveGame s = SafeRead(SaveSlot);
                return s != null ? "Save: " + s.Describe() : "No save yet";
            };
            MpGameActions.Rooms = () =>
            {
                var list = new string[World.Areas.Count];
                for (int i = 0; i < list.Length; i++) list[i] = World.Areas[i].Name;
                return list;
            };
            MpGameActions.WarpTo = i =>
            {
                if (i < 0 || i >= World.Areas.Count) return "No such room";
                Vector3 p;
                float yaw;
                RoomEntrance(i, out p, out yaw);
                StartCoroutine(Warp(i, p, yaw, null));
                return "Going to " + World.Areas[i].Name;
            };
            MpGameActions.Status = () =>
            {
                string room = WorldView != null && WorldView.CurrentArea >= 0 ? World.Areas[WorldView.CurrentArea].Name : "-";
                System.TimeSpan t = System.TimeSpan.FromSeconds(PlaySeconds);
                return room + " | Energy " + Mathf.CeilToInt(State.Energy) + "/" + Mathf.RoundToInt(State.MaxEnergy) + " | Missiles " + State.Missiles + "/" + State.MissileCapacity
                    + " | " + BeamNames[State.Beam] + " Beam | " + VisorNames[State.Visor] + " Visor | " + ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00");
            };
            MpGameActions.About = () => "Metroid Prime Dreamport " + MpVersion.Label + "\n" + MpVersion.Summary + "\nDisc: " + (Assets != null && Assets.Disc != null ? Assets.Disc.GameId : "?")
                + "\nEverything you see and hear is read from your own disc image.";
        }

        private static readonly string[] BeamNames = { "Power", "Ice", "Wave", "Plasma" };
        private static readonly string[] VisorNames = { "Combat", "X-Ray", "Scan", "Thermal" };

        private static void ClearActions()
        {
            MpGameActions.SaveGame = MpGameActions.LoadGame = MpGameActions.Respawn = MpGameActions.SaveInfo = MpGameActions.Status = MpGameActions.About = null;
            MpGameActions.Rooms = null;
            MpGameActions.WarpTo = null;
        }

        private static MpSaveGame SafeRead(string slot)
        {
            try { return MpSaveGame.Read(slot); }
            catch (System.Exception e) { MpLog.Warn("Save: " + slot + " not read: " + e.Message); return null; }
        }

        /// <summary>The state now as a save.</summary>
        private MpSaveGame Capture(string where)
        {
            var s = new MpSaveGame { World = CurrentPak, Area = Mathf.Max(0, WorldView.CurrentArea), Energy = State.Energy, Missiles = State.Missiles, MissileCapacity = State.MissileCapacity, Beam = State.Beam, Visor = State.Visor, PlaySeconds = PlaySeconds };
            s.X = PlayerGame.x; s.Y = PlayerGame.y; s.Z = PlayerGame.z;
            float yaw;
            string src;
            s.Yaw = MpPlayer.TryFacingYaw(out yaw, out src) ? -yaw : _spawnYaw;
            System.Array.Copy(State.Items, s.Items, s.Items.Length);
            foreach (MpWorld.Area a in World.Areas) s.Layers[a.Index] = a.LayerBits;
            s.Mailbox.AddRange(Scripts.Mailbox);
            s.Scans.AddRange(_scanned);
            s.Where = World.Areas[s.Area].Name + (where != null ? " (" + where + ")" : "");
            return s;
        }

        public string Save(string slot, string where)
        {
            // co-op: the master's save is the game; a helper plays in it and keeps no save or checkpoint of its own
            if (MpCoop.Helper) return slot == SaveSlot ? "The master (" + MpCoop.MasterName + ") saves this game" : "Checkpoint (the master's)";
            try
            {
                Capture(where).Write(slot);
                if (slot == SaveSlot) MpLog.Info("Save: written (" + where + ") in " + World.Areas[Mathf.Max(0, WorldView.CurrentArea)].Name);
                return slot == SaveSlot ? "Saved" : "Checkpoint";
            }
            catch (System.Exception e)
            {
                MpLog.Warn("Save: " + slot + " not written: " + e.Message);
                return "Could not save: " + e.Message;
            }
        }

        public string LoadSlot(string slot)
        {
            if (MpCoop.Helper) return "You are playing in " + MpCoop.MasterName + "'s game: only the master loads";
            MpSaveGame s = SafeRead(slot);
            if (s == null) return "Nothing saved yet";
            Apply(s, false);
            return "Loading " + s.Where;
        }

        /// <summary>A save's state back, then the player to its place (the rooms are rebuilt with its layers).</summary>
        private void Apply(MpSaveGame s, bool fullEnergy)
        {
            // a save in another world: that world first (its rooms at the save's area), then the save on top
            if (!string.IsNullOrEmpty(s.World) && s.World != CurrentPak && Assets.FindPak(s.World) != null)
            {
                StartCoroutine(SwitchWorld(s.World, 0, Mathf.Max(0, s.Area), false, () => Apply(s, fullEnergy)));
                return;
            }

            EscapeTimer = EscapeTotal = 0f;
            if (!MpCoop.Helper) CoopGameChanged();      // co-op: the master loaded: the helpers take the game again
            MpCinematics.Skip();
            System.Array.Copy(s.Items, State.Items, State.Items.Length);
            if (MpPlayerState.UnlockAll) State.UnlockForTesting();
            State.MissileCapacity = s.MissileCapacity;
            State.Missiles = Mathf.Clamp(s.Missiles, 0, s.MissileCapacity);
            State.Energy = fullEnergy ? State.MaxEnergy : Mathf.Clamp(s.Energy, 1f, State.MaxEnergy);
            foreach (var kv in s.Layers) if (kv.Key >= 0 && kv.Key < World.Areas.Count) World.Areas[kv.Key].LayerBits = kv.Value;
            Scripts.Mailbox.Clear();
            foreach (uint id in s.Mailbox) Scripts.Mailbox.Add(id);
            _scanned.Clear();
            foreach (uint id in s.Scans) _scanned.Add(id);
            PlaySeconds = s.PlaySeconds;
            if (Cannon != null && State.HasBeam(s.Beam) && s.Beam != State.Beam) Cannon.ChangeBeam(s.Beam);
            if (State.HasVisor(s.Visor)) SetVisor(s.Visor);
            StartCoroutine(Warp(Mathf.Clamp(s.Area, 0, World.Areas.Count - 1), new Vector3(s.X, s.Y, s.Z), s.Yaw, null, true));
        }

        /// <summary>Death or "unstuck": the last checkpoint (or save, or the frigate's start) with full energy.</summary>
        public void Respawn(string why)
        {
            // co-op: with other players in the game, Samus comes back beside one of them (nobody's game is reloaded)
            if (CoopRespawn(why)) return;
            // dying in the escape is a game over: back to the save (the station before the Parasite Queen), not a checkpoint mid-escape
            bool escaping = EscapeTotal > 0f;
            MpSaveGame s = escaping ? (SafeRead(SaveSlot) ?? SafeRead(CheckpointSlot)) : (SafeRead(CheckpointSlot) ?? SafeRead(SaveSlot));
            EscapeTimer = EscapeTotal = 0f;
            MpLog.Info("Samus: respawn (" + why + ") " + (s != null ? "at " + s.Where : "at the start"));
            if (s != null) { Apply(s, true); return; }
            State.Energy = State.MaxEnergy;
            int spawnArea = WorldView.AreaAt(_spawnGame);
            StartCoroutine(Warp(spawnArea >= 0 ? spawnArea : Mathf.Max(0, StartAreaOverride), _spawnGame, _spawnYaw, null, true));
        }

        /// <summary>Where a room is entered for a warp: its default spawn point, else just inside its first dock, else its centre.</summary>
        private void RoomEntrance(int area, out Vector3 pos, out float yaw)
        {
            MpWorld.Area a = World.Areas[area];
            yaw = 0f;
            if (a.Docks.Count > 0 && a.Docks[0].Plane.Count >= 4)
            {
                Vector3 c = Vector3.zero;
                foreach (float[] v in a.Docks[0].Plane) c += new Vector3(v[0], v[1], v[2]);
                c /= a.Docks[0].Plane.Count;
                Vector3 centre = new Vector3((a.Bounds[0] + a.Bounds[3]) * 0.5f, (a.Bounds[1] + a.Bounds[4]) * 0.5f, c.z);
                Vector3 inward = centre - c;
                inward.z = 0f;
                inward = inward.sqrMagnitude > 1e-4f ? inward.normalized : Vector3.forward;
                float bottom = Mathf.Min(a.Docks[0].Plane[0][2], Mathf.Min(a.Docks[0].Plane[1][2], Mathf.Min(a.Docks[0].Plane[2][2], a.Docks[0].Plane[3][2])));
                pos = new Vector3(c.x, c.y, bottom) + inward * 3f + Vector3.forward * 0.3f;
                yaw = Mathf.Atan2(-inward.x, inward.y) * Mathf.Rad2Deg;
                return;
            }

            pos = new Vector3((a.Bounds[0] + a.Bounds[3]) * 0.5f, (a.Bounds[1] + a.Bounds[4]) * 0.5f, a.Bounds[2] + 1f);
        }

        /// <summary>
        /// Moves Samus to a room: optionally drops every loaded room first (a load: their script state must be rebuilt), loads the
        /// target and its neighbours, then places the player (the client applies a teleport a few frames later).
        /// </summary>
        public IEnumerator Warp(int area, Vector3 gamePos, float yawGame, System.Action done, bool rebuild = false)
        {
            if (_warping) yield break;
            MpCinemaView.End();
            _warping = true;
            if (Cannon != null) Cannon.CancelCharge();
            if (rebuild) UnloadAllAreas();
            WorldView.CurrentArea = area;
            WorldView.Request(area);
            float t0 = Time.realtimeSinceStartup;
            while (!WorldView.IsLoaded(area) && Time.realtimeSinceStartup - t0 < 60f) { Loader.Drain(8); yield return null; }
            foreach (int n in World.Areas[area].Attached) WorldView.Request(n);
            _checkpointArea = area;
            string how;
            Vector3 unity = MpSpace.ToUnity(gamePos) + Vector3.up * 0.05f;
            bool placed = MpPlayer.Teleport(unity, -yawGame, out how);
            if (LabFocus.HasValue) LabFocus = gamePos;
            MpLog.Info("World: moved to area " + area + " '" + World.Areas[area].Name + "' at game " + gamePos + (placed ? "" : " (not placed: " + how + ")"));
            for (int i = 0; i < 10; i++) yield return null;
            _warping = false;
            if (done != null) done();
        }

        private void UnloadAllAreas()
        {
            var keys = new List<int>(WorldView.Areas.Keys);
            foreach (int a in keys)
            {
                Scripts.RemoveArea(a);
                WorldView.Areas[a].Destroy();
                WorldView.Areas.Remove(a);
            }

            ClearScan();
            if (Shots != null) Shots.Clear();
            Bombs.Clear();
        }

        /// <summary>Every frame: play time, a checkpoint in each newly entered room, death.</summary>
        private void TickProgress(float dt)
        {
            PlaySeconds += Time.unscaledDeltaTime;
            if (_warping) return;
            int cur = WorldView.CurrentArea;
            if (cur >= 0 && cur != _checkpointArea && WorldView.IsLoaded(cur) && State.Energy > 0f && !InCinematic)
            {
                _checkpointArea = cur;
                Save(CheckpointSlot, "checkpoint");
            }

            if (State.Energy <= 0f)
            {
                if (_deadT < 0f)
                {
                    _deadT = 0f;
                    if (Cannon != null) Cannon.CancelCharge();
                    if (Hud != null) Hud.ShowMessage("SAMUS HAS FALLEN", 3f);
                    PlayUiSfx(MpSfx.PlayerDeath);
                    MpLog.Info("Samus: energy gone in " + World.Areas[Mathf.Max(0, cur)].Name);
                }

                _deadT += dt;
                if (_deadT > 3f) { _deadT = -1f; Respawn("death"); }
            }
            else _deadT = -1f;
        }
    }
}
