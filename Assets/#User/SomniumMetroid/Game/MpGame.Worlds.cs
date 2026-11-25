using System;
using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Moving between worlds (CWorldTransManager / CMain world change, without the ship and elevator movies): every room of the
    /// world is dropped, the next world's pak and MLVL are read on the loader, its rooms stream around the teleporter's target
    /// area, and Samus stands at that area's spawn point. The layer state of a world left behind is kept (CWorldLayerState per
    /// world) so coming back finds the rooms as the scripts left them. Leaving the frigate is the game's one item loss: Samus
    /// arrives on Tallon IV with only what the Landing Site's spawn point gives (CPlayerState reset by the frigate's ending).
    /// </summary>
    public sealed partial class MpGame
    {
        /// <summary>The pak of the world being played (StartPak for a new game).</summary>
        public string CurrentPak = StartPak;

        /// <summary>Lab: start in this world's pak instead of the frigate.</summary>
        public static string StartPakOverride;

        /// <summary>Tallon Overworld (the frigate's world teleporter leads to its Landing Site).</summary>
        public const string TallonPak = "Metroid4.pak";

        public bool SwitchingWorld { get; private set; }

        private readonly Dictionary<uint, Dictionary<int, ulong>> _worldLayers = new Dictionary<uint, Dictionary<int, ulong>>();

        /// <summary>Loader thread: a world's MLVL from its pak, with the room names.</summary>
        private static MpWorld ReadWorld(MpAssets assets, string pakName)
        {
            MpPak pak = assets.FindPak(pakName);
            if (pak == null) throw new Exception("no " + pakName + " on the disc");
            MpPak.Entry mlvl = null;
            foreach (MpPak.Entry e in pak.Entries) if (e.Type == "MLVL") mlvl = e;
            if (mlvl == null) throw new Exception(pakName + " has no MLVL");
            var world = MpWorld.Read(pak.Read(mlvl), mlvl.Id);
            foreach (MpWorld.Area a in world.Areas)
            {
                try { a.Name = MpStrings.Read(assets.Read(a.NameStrg))[0]; }
                catch (Exception) { a.Name = "Area " + a.Index; }
            }

            return world;
        }

        /// <summary>The pak holding a world (MLVL id), or null.</summary>
        public string PakOfWorld(uint mlvl)
        {
            foreach (MpPak p in Assets.Paks)
                foreach (MpPak.Entry e in p.Entries)
                    if (e.Type == "MLVL" && e.Id == mlvl) return p.Name;
            return null;
        }

        /// <summary>The area index of an MREA asset id or an MLVL area id, or -1.</summary>
        private static int AreaIndexOf(MpWorld w, uint id)
        {
            foreach (MpWorld.Area a in w.Areas) if (a.Mrea == id || a.AreaId == id) return a.Index;
            return -1;
        }

        /// <summary>CScriptWorldTeleporter: off to another world (the frigate's end, the elevators). World 0 = the frigate's way out (Tallon IV).</summary>
        public void OnWorldTeleport(uint world, uint area)
        {
            if (SwitchingWorld) return;
            string pak = world != 0 ? PakOfWorld(world) : TallonPak;
            bool fromFrigate = CurrentPak == StartPak;
            if (pak == null) { MpLog.Warn("World: teleporter to unknown world " + world.ToString("X8")); return; }
            if (pak == CurrentPak) { MpLog.Info("World: teleporter to this same world - ignored"); return; }
            if (fromFrigate)
            {
                FrigateDone = true;
                EscapeTimer = EscapeTotal = 0f;
                State.Energy = Mathf.Max(State.Energy, 1f);
                MpLog.Info("World: the frigate is done (teleporter to world " + world.ToString("X8") + " area " + area.ToString("X8") + ")");
            }

            StartCoroutine(SwitchWorld(pak, area, -1, fromFrigate, null));
        }

        /// <summary>
        /// Drops this world and starts another at an area (an MREA / area id, or an area index when <paramref name="areaIndex"/> is set);
        /// <paramref name="stripItems"/> gives Samus only what the arrival spawn point holds. <paramref name="then"/> runs once she stands there.
        /// </summary>
        public IEnumerator SwitchWorld(string pak, uint areaId, int areaIndex, bool stripItems, Action then)
        {
            if (SwitchingWorld) yield break;
            SwitchingWorld = true;
            while (_warping) yield return null;
            _warping = true;
            string reason = "World: leaving '" + CurrentPak + "' for '" + pak + "'";
            MpLog.Info(reason);
            if (Hud != null) { Hud.SetBoss(false, null, 0f); Hud.SetCounter(null); Hud.ShowMessage("Loading...", 3f); }
            _boss = null;
            if (Cannon != null) Cannon.CancelCharge();
            if (Ball.Morphed) Ball.Unmorph();
            MpCinematics.Skip();
            MpCinemaView.End();
            MpCinemaAvatar.Clear();
            MpCinematics.Clear();
            EscapeTimer = EscapeTotal = 0f;

            // this world's layers kept for a return
            var keep = new Dictionary<int, ulong>();
            foreach (MpWorld.Area a in World.Areas) keep[a.Index] = a.LayerBits;
            _worldLayers[World.Id] = keep;

            UnloadAllAreas();
            WorldView.Destroy();
            MpEnemyShots.Clear();
            MpEffects.Clear();
            MpSprites.Clear();

            MpWorld next = null;
            Exception error = null;
            bool done = false;
            MpAssets assets = Assets;
            Loader.Add(() => ReadWorld(assets, pak), (r, e) => { next = r as MpWorld; error = e; done = true; });
            while (!done) { Loader.Drain(4); yield return null; }
            if (next == null)
            {
                MpLog.Error("World: '" + pak + "' could not be read: " + error);
                _warping = false;
                SwitchingWorld = false;
                yield break;
            }

            Dictionary<int, ulong> back;
            if (_worldLayers.TryGetValue(next.Id, out back))
                foreach (var kv in back) if (kv.Key >= 0 && kv.Key < next.Areas.Count) next.Areas[kv.Key].LayerBits = kv.Value;

            World = next;
            CurrentPak = pak;
            WorldView = new MpWorldView(this, Assets, World, Loader, WorldRoot);
            WorldView.Visor = State.Visor;
            int area = areaIndex >= 0 ? areaIndex : AreaIndexOf(World, areaId);
            if (area < 0) { MpLog.Warn("World: area " + areaId.ToString("X8") + " not in '" + pak + "' - its first room instead"); area = 0; }
            MpWorldView view = WorldView;
            bool sky = false;
            Loader.Add(() => { view.PrepareSky(); return null; }, (r, e) => { sky = true; if (e != null) MpLog.Warn("World: sky not read: " + e.Message); });
            WorldView.CurrentArea = area;
            WorldView.Request(area);
            float t0 = Time.realtimeSinceStartup;
            while ((!WorldView.IsLoaded(area) || !sky) && Time.realtimeSinceStartup - t0 < 120f) { Loader.Drain(8); yield return null; }
            foreach (int n in World.Areas[area].Attached) WorldView.Request(n);

            MpAreaView start;
            Vector3 pos;
            float yaw;
            int[] items = null;
            if (WorldView.Areas.TryGetValue(area, out start) && start.Payload != null) PickSpawn(start, out pos, out yaw, out items);
            else RoomEntrance(area, out pos, out yaw);
            if (stripItems)
            {
                // the frigate's end: Samus keeps nothing but what the arrival gives (Power Beam, Combat and Scan visors, 99 energy)
                State.Give(items);
                if (Cannon != null && State.Beam != MpPlayerState.BeamPower) Cannon.ChangeBeam(MpPlayerState.BeamPower);
                if (State.Visor != MpPlayerState.VisorCombat) SetVisor(MpPlayerState.VisorCombat);
                MpLog.Info("Samus: upgrades lost - energy " + State.Energy + ", missiles " + State.Missiles + "/" + State.MissileCapacity + ", morph ball " + (State.Items[MpPlayerState.ItemMorphBall] > 0) + (MpPlayerState.UnlockAll ? " (testing unlocks stay on)" : ""));
            }

            _spawnGame = pos;
            _spawnYaw = yaw;
            _checkpointArea = area;
            string how;
            bool placed = MpPlayer.Teleport(MpSpace.ToUnity(pos) + Vector3.up * 0.05f, -yaw, out how);
            if (LabFocus.HasValue) LabFocus = pos;
            MpLog.Info("World: now in '" + pak + "' (" + World.Areas.Count + " rooms), area " + area + " '" + World.Areas[area].Name + "' at game " + pos + (placed ? "" : " (not placed: " + how + ")"));
            for (int i = 0; i < 10; i++) yield return null;
            _warping = false;
            SwitchingWorld = false;
            if (Hud != null) Hud.ShowMessage(World.Areas[area].Name.ToUpperInvariant(), 3f);
            Save(CheckpointSlot, "arrived");
            if (then != null) then();
        }
    }
}
