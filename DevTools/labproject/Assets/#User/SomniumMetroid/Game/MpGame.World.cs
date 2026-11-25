using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// World-level game state the scripts drive: script layers (CWorldLayerState), the escape countdown (CStateManager escape timer:
    /// the HUD counter, the controller rumble growing as it runs out, death at zero), the boss bar, items given by pickups
    /// (CPlayerState::InitializePowerUp / IncrPickUp), riding moving platforms (the world slides under the player, who is never
    /// moved), the cinema screen and the skip key, and the frigate's end (the world teleporter).
    /// </summary>
    public sealed partial class MpGame
    {
        public float EscapeTimer, EscapeTotal;
        private float _escapeShake;
        private MpEnemy _boss;
        private string _bossName;
        public bool FrigateDone;
        private bool _rideBody;
        public int RideCount;

        /// <summary>ScriptLayerController: a room's layer on or off for its next load (the room is found by its MREA area id).</summary>
        public void SetLayer(uint areaId, int layer, bool on, string why)
        {
            if (layer < 0 || layer >= 64) return;
            foreach (MpWorld.Area a in World.Areas)
            {
                if (a.AreaId != areaId) continue;
                ulong bit = 1UL << layer;
                a.LayerBits = on ? a.LayerBits | bit : a.LayerBits & ~bit;
                MpLog.Info("Scripts: layer " + layer + (layer < a.LayerNames.Count ? " '" + a.LayerNames[layer] + "'" : "") + " of '" + a.Name + "' " + (on ? "on" : "off") + " (" + why + ")");
                // already built with the old layers (a room next door, kept loaded): it is rebuilt with the new ones (its objects' assets are
                // only prepared for active layers). The room Samus stands in waits until she has left it.
                if (WorldView != null) WorldView.LayersChanged(a.Index);
                return;
            }
        }

        public void StartEscape(float seconds)
        {
            EscapeTimer = EscapeTotal = seconds;
            _escapeShake = 0f;
            MpLog.Info("Escape: " + seconds.ToString("0") + " s to get off the frigate");
        }

        private void TickEscape(float dt)
        {
            if (EscapeTimer <= 0f) { if (Hud != null) Hud.SetCounter(null); return; }
            EscapeTimer = Mathf.Max(0f, EscapeTimer - dt);
            if (Hud != null)
            {
                int s = Mathf.CeilToInt(EscapeTimer);
                Hud.SetCounter((s / 60).ToString("00") + ":" + (s % 60).ToString("00"));
            }

            _escapeShake -= dt;
            if (_escapeShake < 0f)
            {
                float f = 1f - EscapeTimer / Mathf.Max(1f, EscapeTotal);
                _escapeShake = -12f * f * f + 15f;
                if (MpXr.Present && MpSettings.Haptics > 0f) { MpXr.Pulse(true, 0.5f * MpSettings.Haptics, 0.4f); MpXr.Pulse(false, 0.5f * MpSettings.Haptics, 0.4f); }
            }

            if (EscapeTimer <= 0f)
            {
                MpLog.Info("Escape: time ran out");
                State.Energy = 0f;
            }
        }

        /// <summary>BossEnergyBar Increment: the bar follows this boss's health (its name: the main string table at the index).</summary>
        public void SetBoss(MpEntity boss, int stringIndex)
        {
            _boss = boss as MpEnemy;
            _bossName = Hud != null && stringIndex > 0 ? Hud.MainString(stringIndex) : null;
            if (_boss == null && Hud != null) Hud.SetBoss(false, null, 0f);
        }

        private void TickBoss()
        {
            if (_boss == null || Hud == null) return;
            if (_boss.Dead || _boss.Gone) { Hud.SetBoss(false, null, 0f); _boss = null; return; }
            if (_boss.Active) Hud.SetBoss(true, string.IsNullOrEmpty(_bossName) ? "PARASITE QUEEN" : _bossName, _boss.MaxHealth > 0f ? _boss.HealthNow / _boss.MaxHealth : 1f);
        }

        /// <summary>CScriptPickup::Touch: the item's capacity, then its amount (energy refills, missiles, tanks, upgrades).</summary>
        public void GiveItem(int item, int capacity, int amount)
        {
            if (item < 0 || item >= State.Items.Length) return;
            switch (item)
            {
                case MpPlayerState.ItemHealthRefill:
                    State.Energy = Mathf.Min(State.MaxEnergy, State.Energy + amount);
                    break;
                case MpPlayerState.ItemMissiles:
                    State.MissileCapacity += Mathf.Max(0, capacity);
                    State.Items[item] = State.MissileCapacity;
                    State.Missiles = Mathf.Min(State.MissileCapacity, State.Missiles + amount);
                    break;
                case MpPlayerState.ItemTanks:
                    State.Items[item] += Mathf.Max(0, capacity);
                    State.Energy = State.MaxEnergy;
                    break;
                default:
                    State.Items[item] = Mathf.Max(State.Items[item], capacity > 0 ? capacity : 1);
                    break;
            }

            MpLog.Info("Samus: item " + item + " (+" + capacity + " capacity, +" + amount + ") - energy " + Mathf.CeilToInt(State.Energy) + ", missiles " + State.Missiles + "/" + State.MissileCapacity);
        }

        /// <summary>
        /// Co-op, the master: another player took a pickup - what it unlocks (a beam, a visor, the morph ball, a tank, missile capacity)
        /// is the game's and so the master's (MpCoop gives the master's unlocks to everyone); refills stay with the player who took it.
        /// </summary>
        public void GiveUnlock(int item, int capacity)
        {
            if (item < 0 || item >= State.Items.Length || item == MpPlayerState.ItemHealthRefill) return;
            if (item == MpPlayerState.ItemMissiles)
            {
                if (capacity <= 0) return;
                State.MissileCapacity += capacity;
                State.Items[item] = State.MissileCapacity;
            }
            else if (item == MpPlayerState.ItemTanks) State.Items[item] += Mathf.Max(0, capacity);
            else State.Items[item] = Mathf.Max(State.Items[item], capacity > 0 ? capacity : 1);
            MpLog.Info("Samus: item " + item + " (+" + capacity + " capacity) unlocked by another player");
        }

        /// <summary>
        /// Riding: when Samus's feet are on a moving platform (an elevator), her avatar is carried by the platform's motion
        /// the platform stays where it is under the player, everything else goes by (the client's avatar is never moved by a world).
        /// </summary>
        private void TickRide()
        {
            Vector3 carried = Vector3.zero;
            bool on = false;
            foreach (MpEntity e in Scripts.All)
            {
                var p = e as MpMovingPlatform;
                if (p == null) continue;
                if (!on && p.LastDelta != Vector3.zero && p.Carries(PlayerGame)) { carried = p.LastDelta; on = true; }
                p.LastDelta = Vector3.zero;
            }

            if (!on) return;
            // the AVATAR rides the platform (Somnium's own elevator way: its body moved by the platform's motion); the world stays
            // where it is for everyone (co-op: "make the CHARACTER move, not the world")
            if (LabFocus.HasValue) LabFocus = LabFocus.Value + carried;      // the lab's stand-in player rides along
            else MpPlayer.Carry(MpSpace.DirToUnity(carried) * MpSpace.Scale);
            RideCount++;
            _lastRideT = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ one world for everyone
        // Rides and the third-person morph ball move THIS client's world under its avatar (MpSpace.Shift); the Somnium client places
        // every avatar in the same Unity space for everyone, so a world left moved puts the other players out of place in it (the
        // live co-op test: "the remote players are in a different offset to me - the morph ball caused it"). So when nothing is
        // moving it any more, the world goes back home (MpSpace.Home: the same for every client) and the avatar moves by the same
        // amount in one teleport - it keeps its place in the game world, and everyone's worlds line up again. While the teleport
        // lands, Samus's game position is held where she was.

        private float _lastRideT = -10f, _settleHoldUntil, _nextSettle;
        private Vector3 _settleGame;
        public static int Settles;     // labs

        private void SettleWorld()
        {
            if (MpSpace.Turned || Time.unscaledTime < _nextSettle) return;
            Vector3 off = MpSpace.Origin - MpSpace.Home;
            if (off.sqrMagnitude < 1e-4f) return;
            if (Ball.Holding || MpCinemaView.Active || _warping || LabFocus.HasValue || Time.unscaledTime - _lastRideT < 0.75f) return;
            Vector3 feet;
            if (!MpPlayer.TryFeet(out feet)) return;
            _nextSettle = Time.unscaledTime + 1f;
            float yaw;
            string src, how;
            if (!MpPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            Vector3 game = MpSpace.ToGame(feet);
            if (!MpPlayer.Teleport(feet - off + Vector3.up * 0.02f, yaw, out how)) return;
            MpSpace.Shift(-off);
            _settleGame = game;
            _settleHoldUntil = Time.unscaledTime + 0.6f;
            Settles++;
            MpLog.Info("World: back to the shared place (it had moved " + off.magnitude.ToString("0.0") + " m under this player: rides / morph ball); the avatar moved with it (" + how + ")");
        }

        /// <summary>CScriptSpawnPoint SetToZero / CPlayer::Teleport: Samus to a place (not when she already stands within 3 units of it).</summary>
        public void TeleportPlayer(Vector3 gamePos, float yawGame, string why)
        {
            if ((PlayerGame - gamePos).magnitude < 3f || _warping) return;
            MpCinemaView.End();      // the world back where the player stands before the scripts move them
            int area = WorldView.AreaAt(gamePos);
            MpLog.Info("World: the scripts move Samus (" + why + ")");
            StartCoroutine(Warp(area >= 0 ? area : Mathf.Max(0, WorldView.CurrentArea), gamePos, yawGame, null));
        }

        /// <summary>The player's skip: Enter on a monitor, both grips together in VR (or the tablet).</summary>
        private float _introUntil;
        private bool _introSkipped;

        private void TickCinemaInput()
        {
            InCinematic = MpCinematics.Active;
            // the option "Skip the intro": a new game's opening cutscene on the frigate's deck is skipped the moment each of its shots
            // begins (the master's choice in co-op: a helper's cutscenes follow the master's skip)
            if (_introUntil > 0f)
            {
                if (Time.unscaledTime > _introUntil || CurrentPak != StartPak || (WorldView != null && WorldView.CurrentArea > 0)) _introUntil = 0f;
                else if (MpSettings.SkipIntro && MpCinematics.Active && !MpCoop.Helper)
                {
                    if (!_introSkipped) MpLog.Info("Start: the intro skipped (option)");
                    _introSkipped = true;
                    MpCinematics.Skip();
                    return;
                }
            }

            if (!MpCinematics.Active) return;
            bool skip = false;
            if (MpXr.Present)
            {
                MpHand l = MpXr.Hand(false), r = MpXr.Hand(true);
                skip = (l.GripHeld && r.GripPressed) || (r.GripHeld && l.GripPressed);
            }
            else skip = SafeKey(() => Input.GetKeyDown(KeyCode.Return));
            if (!skip) return;
            if (MpCoop.Helper) CoopAskSkip();      // co-op: the master skips it for everyone
            else MpCinematics.Skip();
        }
    }
}
