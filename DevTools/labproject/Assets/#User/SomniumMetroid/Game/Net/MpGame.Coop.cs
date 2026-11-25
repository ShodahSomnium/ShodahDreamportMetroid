using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>The co-op state other classes ask about (MpGame.Coop keeps it).</summary>
    public static class MpCoop
    {
        /// <summary>In the instance's Fusion room with the Dreamport's scene objects.</summary>
        public static bool Online;
        /// <summary>This client runs the game (alone, offline, or the instance master).</summary>
        public static bool IsMaster = true;
        /// <summary>This client plays in the master's game (joined: its script world mirrors the master's).</summary>
        public static bool Helper;
        public static int LocalId = -1, MasterId = -1;
        /// <summary>Players in the game (this one included).</summary>
        public static int Players = 1;
        public static string MasterName = "the master";
        /// <summary>Lab: a loopback network between two Unity processes instead of Fusion.</summary>
        public static IMpNetLink LabLink;
        /// <summary>Lab counters.</summary>
        public static int LabScriptIn, LabScriptOut, LabStateIn, LabEnemyIn, LabDamageIn, LabFireIn, LabGenIn, LabHurtIn, LabJoins;
    }

    /// <summary>
    /// Co-op, the Skyrim Dreamport's way: the instance master (Fusion's shared-mode master; whoever is alone) runs THE game - its
    /// script world, its enemies, its save. Anyone else in the instance joins it:
    /// - Joining: the helper says Hello; the master answers its game (world, the rooms' script layers, the memory relays, its
    ///   unlocks) and the helper loads it like a save (no file written) and stands next to the master. Each room the helper
    ///   builds, it asks the master for that room's entities that are not as the disc has them (doors open, pickups taken,
    ///   counters, the copies generators made).
    /// - The script world: every message the master's scripts deliver goes to the helpers (Script), whose own script worlds stay
    ///   quiet and only react (MpScripts.Follower): doors open, cutscenes roll, music and sounds play, layers switch, the world
    ///   teleporter takes everyone. What changes outside messages (an active flag, a door, a taken pickup) follows at 5 Hz (State);
    ///   moving platforms at 4 Hz (Platform), enemies at 10 Hz (EnemyState: where, facing, animation, health), deaths at once.
    /// - The master's triggers count every Samus; its enemies hunt the nearest one, and what they do to another player goes to
    ///   that player (Hurt); its enemy shots fly on every client and hurt that client's Samus.
    /// - Each Samus is her own: her cannon, beam, visor, morph ball, bombs, cutscene view and energy. Her shots and bombs are seen
    ///   and heard by everyone (Fire, Bomb); the hits are hers to report - a helper's go to the master (Damage), who applies them.
    /// - Pickups: a helper touching one asks the master (TakeAsk); the master gives the unlock to the game (its own state) and the
    ///   refill to the taker (Give). Everyone has the master's unlocks (Unlocks): beams, visors, morph ball, bombs, suits, tanks,
    ///   missile capacity.
    /// - Enemies and bosses have 25 % more health for every player beyond the first (max and current), again whenever someone
    ///   joins or leaves.
    /// - Saves: the master's save is the game; helpers do not save. Dying with others around, Samus comes back beside them.
    /// - The master leaving: Fusion makes another player master; that one's game (as mirrored) simply goes on running.
    /// </summary>
    public sealed partial class MpGame
    {
        private IMpNetLink _net;
        private readonly int[] _rec = new int[MpNetWire.World.Used];
        private readonly int[] _own = new int[MpNetWire.PlayerSlot.Used];
        private bool _coopInstalled, _joined, _wasFollower;
        private int _gameStamp = 1, _seenGame = -1;
        private float _helloAt = -100f, _pakWrongT, _nextDiff, _nextEnemy, _nextPlatform, _nextRemoteLog;
        private int _lastPlayers = 1, _unlockSent = int.MinValue, _unlockKey;
        private uint _nextGenId = 1;
        private bool _masterUnlockAll = true, _haveMasterUnlocks;
        private readonly int[] _masterItems = new int[41];
        private int _masterMissileCap;

        // the joiner's snapshot as it arrives
        private MpSaveGame _snap;
        private int _snapStamp;

        // the master's batch of script deliveries (sent at the start of the next frame, in order with everything else)
        private MpNetWire.Writer _scriptBatch;
        private int _scriptCount, _scriptCountAt;
        private readonly Dictionary<long, float> _insideSent = new Dictionary<long, float>();
        private readonly Dictionary<MpEntity, int> _stateSent = new Dictionary<MpEntity, int>();
        private readonly Dictionary<MpEnemy, Vector4> _enemySent = new Dictionary<MpEnemy, Vector4>();
        private readonly Dictionary<MpEnemy, float> _enemySentT = new Dictionary<MpEnemy, float>();
        private readonly Dictionary<int, float> _remoteImmune = new Dictionary<int, float>();
        private readonly List<MpEntity> _scratch = new List<MpEntity>();
        private MpRemoteCannons _remoteView;
        // this player's cannon as the others draw it (CoopCannon, written into the slot by OwnSlot)
        private int _cannonFlags, _firePresses, _missilePresses;
        private Vector3 _cannonRelPos;
        private Quaternion _cannonRelRot = Quaternion.identity;
        private MpWorldView _askView;

        /// <summary>Linked with others: online and somebody else is here.</summary>
        private bool Linked { get { return _net != null && _net.Online && _net.Remotes.Count > 0; } }

        public IMpNetLink Net { get { return _net; } }

        /// <summary>The world's own SceneNetworking (Net/SceneNetworking.cs) on this object, unless one already runs: live the Community
        /// Modules' one is a missing script, so this is the one that registers the scene's NetworkObjects (the Annihilator world's way).</summary>
        private void EnsureSceneNetworking()
        {
            if (global::SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking.Instance != null) return;
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (b != null && b.isActiveAndEnabled && b.GetType().Name == "SceneNetworking") { MpLog.Info("Net: the scene's own SceneNetworking runs (" + b.GetType().FullName + ")"); return; }
            gameObject.AddComponent<global::SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking>();
            MpLog.Info("Net: SceneNetworking added (the world's own copy: registers the scene's network objects when we join)");
        }

        private void InstallCoop()
        {
            if (_coopInstalled) return;
            _coopInstalled = true;
            if (MpCoop.LabLink == null) EnsureSceneNetworking();
            _net = MpCoop.LabLink ?? new MpNet();
            MpScripts.Delivered = OnDelivered;
            MpEnemy.PickFocus = PickFocus;
            MpEnemy.Died = OnEnemyDied;
            RemoteHurt = SendHurt;
            MpProjectiles.OnFired = OnOwnFire;
            MpBombs.OnDropped = OnOwnBomb;
            MpPickupEntity.TakeAsk = p => SendIf(true, new MpNetWire.Writer(MpNetWire.Msg.TakeAsk).I32(_net.LocalId).U32(p.NetId));
            MpPickupEntity.GivenTo = (player, p) => SendIf(true, new MpNetWire.Writer(MpNetWire.Msg.Give).I32(player).I32(p.Item).I32(p.Capacity).I32(p.Amount));
            MpGeneratorEntity.OnGenerated = OnGenerated;
            MpEnemyShots.OnFire = (w, from, dir, dmg, scale) =>
            {
                if (!Linked || MpScripts.Follower || !_net.IsMaster) return;
                FlushScripts();
                Send(new MpNetWire.Writer(MpNetWire.Msg.EnemyShot).U32(w).F32(from.x).F32(from.y).F32(from.z).F32(dir.x).F32(dir.y).F32(dir.z).F32(dmg).F32(scale));
            };
            MpCinematics.Skipped = () => { if (Linked && _net.IsMaster) { FlushScripts(); Send(new MpNetWire.Writer(MpNetWire.Msg.Skipped)); } };
            _remoteView = new MpRemoteCannons(this);
            MpLog.Info("Co-op: ready (" + (MpCoop.LabLink != null ? "lab link" : "Fusion shared session") + "); alone you are the master of your own game");
        }

        private void ClearCoop()
        {
            MpScripts.Delivered = null;
            MpScripts.Follower = false;
            MpEnemy.PickFocus = null;
            MpEnemy.Died = null;
            RemoteHurt = null;
            MpProjectiles.OnFired = null;
            MpProjectiles.HitHook = null;
            MpBombs.OnDropped = null;
            MpPickupEntity.TakeAsk = null;
            MpPickupEntity.GivenTo = null;
            MpGeneratorEntity.OnGenerated = null;
            MpEnemyShots.OnFire = null;
            MpCinematics.Skipped = null;
            MpCoop.Helper = false;
            MpCoop.IsMaster = true;
            MpCoop.Online = false;
            MpEnemy.HealthScale = 1f;
            if (_remoteView != null) _remoteView.Destroy();
            if (_net != null) _net.Dispose();
            _net = null;
            _coopInstalled = false;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Once a frame, before the world and the scripts tick.</summary>
        private void TickCoop(float dt)
        {
            InstallCoop();
            try { _net.Tick(OwnSlot()); }
            catch (System.Exception e) { MpLog.Warn("Net: " + e.Message); }

            bool master = _net.IsMaster;
            MpCoop.Online = _net.Online;
            MpCoop.IsMaster = master;
            MpCoop.LocalId = _net.LocalId;
            MpCoop.MasterId = master ? _net.LocalId : (_net.WorldValid ? _net.World[MpNetWire.World.AtMaster] : -1);
            MpCoop.MasterName = master ? "you" : NameOf(MpCoop.MasterId);
            int players = 1;
            foreach (MpRemote r in _net.Remotes) if (r.Ints[MpNetWire.PlayerSlot.AtStatus] == MpNetWire.PlayerSlot.StatusPlaying) players++;
            MpCoop.Players = players;

            // the master went away and this client is the master now: its game (as mirrored) runs on from here
            if (master && MpScripts.Follower)
            {
                MpScripts.Follower = false;
                MpCoop.Helper = false;
                MpProjectiles.HitHook = null;
                if (_net.WorldValid) _gameStamp = _net.World[MpNetWire.World.AtGame] + 1;
                MpLog.Info("Co-op: the master left - this client is the master now; the game goes on here");
                if (Hud != null) Hud.ShowMessage("YOU ARE NOW THE MASTER", 3f);
            }

            Receive();
            if (master) TickMaster(dt);
            else TickHelper(dt);
            if (_wasFollower != MpScripts.Follower) { _wasFollower = MpScripts.Follower; MpLog.Info("Co-op: script world " + (MpScripts.Follower ? "mirrors the master's" : "runs here")); }
            if (_remoteView != null) _remoteView.Tick(dt, _net.Remotes);
            if (Time.unscaledTime >= _nextRemoteLog && _net.Online)
            {
                _nextRemoteLog = Time.unscaledTime + 30f;
                MpLog.Info("Co-op: " + (master ? "master" : "helper") + ", " + players + " player(s), " + _net.Remotes.Count + " remote slot(s), enemy health x" + MpEnemy.HealthScale.ToString("0.00"));
            }
        }

        private void TickMaster(float dt)
        {
            FlushScripts();
            // the other players as the triggers and the rooms see them
            Scripts.Others.Clear();
            WorldView.KeepAlso.Clear();
            int pak = MpNetWire.PakHash(CurrentPak);
            foreach (MpRemote r in _net.Remotes)
            {
                Vector3 feet;
                bool morphed, dead;
                int area;
                if (!RemoteIn(r, pak, out feet, out morphed, out dead, out area)) continue;
                if (!dead) Scripts.Others.Add(new KeyValuePair<Vector3, bool>(feet, morphed));
                if (area >= 0) WorldView.KeepAlso.Add(area);
            }

            // enemy health: +25 % per player beyond the first
            if (MpCoop.Players != _lastPlayers)
            {
                _lastPlayers = MpCoop.Players;
                ApplyHealthScale(1f + 0.25f * (MpCoop.Players - 1));
            }

            WriteRecord();
            if (!Linked) return;
            SendUnlocksIfChanged(false);
            float now = Time.unscaledTime;
            if (now >= _nextDiff) { _nextDiff = now + 0.2f; SendStateDiffs(); }
            if (now >= _nextPlatform) { _nextPlatform = now + 0.25f; SendPlatforms(-1, false); }
            if (now >= _nextEnemy) { _nextEnemy = now + 0.1f; SendEnemies(); }
        }

        private void TickHelper(float dt)
        {
            Scripts.Others.Clear();
            WorldView.KeepAlso.Clear();
            if (!_net.Online) return;
            if (_lastPlayers != MpCoop.Players)
            {
                _lastPlayers = MpCoop.Players;
                MpEnemy.HealthScale = 1f + 0.25f * (MpCoop.Players - 1);
            }

            if (!_net.WorldValid || _net.World[MpNetWire.World.AtPhase] != MpNetWire.World.PhasePlaying) return;
            int[] w = _net.World;
            bool wantGame = !_joined || w[MpNetWire.World.AtGame] != _seenGame;
            // the master's world changed and the teleporter did not bring us along (we were loading, or missed it): take the game again
            if (_joined && !SwitchingWorld && !_warping && w[MpNetWire.World.AtPak] != MpNetWire.PakHash(CurrentPak) && (w[MpNetWire.World.AtFlags] & MpNetWire.World.FlagSwitching) == 0)
            {
                _pakWrongT += dt;
                if (_pakWrongT > 8f) { wantGame = true; _pakWrongT = 0f; MpLog.Info("Co-op: the master is in another world - joining it again"); }
            }
            else _pakWrongT = 0f;

            if (wantGame && _snap == null && Time.unscaledTime - _helloAt > 6f && !SwitchingWorld && !_warping)
            {
                _helloAt = Time.unscaledTime;
                MpLog.Info("Co-op: asking " + MpCoop.MasterName + " (player " + MpCoop.MasterId + ") for the game");
                Send(new MpNetWire.Writer(MpNetWire.Msg.Hello).I32(_net.LocalId));
            }

            // the escape countdown runs here too; the master's is the one
            float esc = MpNetWire.Unfix(w[MpNetWire.World.AtEscape]);
            if (_joined && (Mathf.Abs(esc - EscapeTimer) > 2f || (esc <= 0f) != (EscapeTimer <= 0f)))
            {
                EscapeTimer = esc;
                EscapeTotal = MpNetWire.Unfix(w[MpNetWire.World.AtEscapeTotal]);
            }

            // the master's testing unlocks
            if (_haveMasterUnlocks && MpPlayerState.UnlockAll != _masterUnlockAll) ApplyUnlocks();
        }

        // ------------------------------------------------------------------ records

        private int[] OwnSlot()
        {
            System.Array.Clear(_own, 0, _own.Length);
            _own[MpNetWire.PlayerSlot.AtStatus] = Running ? MpNetWire.PlayerSlot.StatusPlaying : MpNetWire.PlayerSlot.StatusRoom;
            _own[MpNetWire.PlayerSlot.AtPak] = MpNetWire.PakHash(CurrentPak);
            _own[MpNetWire.PlayerSlot.AtArea] = WorldView != null ? WorldView.CurrentArea : -1;
            Vector3 feet = Ball.Morphed ? Ball.Pos - new Vector3(0f, 0f, MpMorphBall.Radius) : PlayerGame;
            _own[MpNetWire.PlayerSlot.AtX] = MpNetWire.Fix(feet.x);
            _own[MpNetWire.PlayerSlot.AtY] = MpNetWire.Fix(feet.y);
            _own[MpNetWire.PlayerSlot.AtZ] = MpNetWire.Fix(feet.z);
            float yaw;
            string src;
            float gameYaw = Ball.Morphed ? Ball.CamYaw : (MpPlayer.TryFacingYaw(out yaw, out src) ? -yaw : _spawnYaw);
            if (MpSpace.Turned) gameYaw = _spawnYaw;
            _own[MpNetWire.PlayerSlot.AtYaw] = MpNetWire.Fix(gameYaw);
            int flags = 0;
            if (Ball.Morphed) flags |= MpNetWire.PlayerSlot.FlagMorphed;
            if (State != null && State.Energy <= 0f) flags |= MpNetWire.PlayerSlot.FlagDead;
            if (MpXr.Present) flags |= MpNetWire.PlayerSlot.FlagVr;
            if (MpCinemaView.Active) flags |= MpNetWire.PlayerSlot.FlagCinema;
            if (_net != null && _net.IsMaster) flags |= MpNetWire.PlayerSlot.FlagMaster;
            if (_joined || (_net != null && _net.IsMaster)) flags |= MpNetWire.PlayerSlot.FlagJoined;      // the master is in its own game
            if (Cannon != null && Cannon.Charge > 0f) flags |= MpNetWire.PlayerSlot.FlagCharging;
            _own[MpNetWire.PlayerSlot.AtFlags] = flags;
            _own[MpNetWire.PlayerSlot.AtBeam] = State != null ? State.Beam : 0;
            _own[MpNetWire.PlayerSlot.AtVisor] = State != null ? State.Visor : 0;
            _own[MpNetWire.PlayerSlot.AtEnergy] = State != null ? Mathf.CeilToInt(State.Energy) : 0;
            _own[MpNetWire.PlayerSlot.AtSuit] = State != null ? State.Suit : 0;
            MpNetWire.WriteCannon(_own, _cannonFlags, MpSettings.CannonScale, _firePresses, _missilePresses, _cannonRelPos, _cannonRelRot);
            MpNetWire.WriteOrigin(_own, MpSpace.Origin);
            return _own;
        }

        /// <summary>
        /// The frame's cannon for the others: where it is relative to this avatar's right hand (VR) or head (a monitor; the head bone,
        /// else the camera), whether it shows, the scan visor's holster, the fire hold and the new presses (each one replayed on their
        /// copy of the cannon, MpRemoteCannons), the fit (scale, forearm mode, the suit arm), the body hidden by the third-person ball.
        /// </summary>
        private void CoopCannon(bool posed, Vector3 pos, Quaternion rot, bool vrHand, Transform cam, bool shown, bool holstered, bool pressed, bool held, bool missile, bool canFire)
        {
            Transform anchor = vrHand ? MpPlayer.RightHand : (MpPlayer.HeadBone ?? cam);
            if (LabAnchor != null) anchor = LabAnchor;
            if (LabHandMode) vrHand = true;
            int f = 0;
            if (posed && anchor != null)
            {
                Quaternion inv = Quaternion.Inverse(anchor.rotation);
                _cannonRelPos = inv * (pos - anchor.position);
                _cannonRelRot = inv * rot;
                f |= MpNetWire.PlayerSlot.CannonPosed;
            }

            if (!vrHand) f |= MpNetWire.PlayerSlot.CannonOnHead;
            if (shown) f |= MpNetWire.PlayerSlot.CannonShown;
            if (holstered) f |= MpNetWire.PlayerSlot.CannonHolstered;
            if (held) f |= MpNetWire.PlayerSlot.CannonFireHeld;
            if (canFire) f |= MpNetWire.PlayerSlot.CannonCanFire;
            if (MpSettings.CannonOnForearm) f |= MpNetWire.PlayerSlot.CannonForearm;
            if (MpSettings.SuitArm) f |= MpNetWire.PlayerSlot.CannonSuitArm;
            if (Ball.Morphed && Ball.ThirdPerson) f |= MpNetWire.PlayerSlot.CannonBodyHidden;
            _cannonFlags = f;
            if (pressed) _firePresses = (_firePresses + 1) & 0xFFFF;
            if (missile) _missilePresses = (_missilePresses + 1) & 0xFFFF;
        }

        /// <summary>Lab: what this client's cannon is measured against (its stand-in avatar's head), instead of the Somnium avatar.</summary>
        public static Transform LabAnchor;
        /// <summary>Lab: the anchor is a hand (as in VR), not the head.</summary>
        public static bool LabHandMode;
        public int[] LabOwnSlot { get { return _own; } }

        private void WriteRecord()
        {
            if (!_net.Online) return;
            _rec[MpNetWire.World.AtPhase] = Running ? MpNetWire.World.PhasePlaying : MpNetWire.World.PhaseBooting;
            _rec[MpNetWire.World.AtPak] = MpNetWire.PakHash(CurrentPak);
            _rec[MpNetWire.World.AtGame] = _gameStamp;
            _rec[MpNetWire.World.AtArea] = WorldView != null ? WorldView.CurrentArea : 0;
            _rec[MpNetWire.World.AtX] = MpNetWire.Fix(PlayerGame.x);
            _rec[MpNetWire.World.AtY] = MpNetWire.Fix(PlayerGame.y);
            _rec[MpNetWire.World.AtZ] = MpNetWire.Fix(PlayerGame.z);
            _rec[MpNetWire.World.AtYaw] = _own[MpNetWire.PlayerSlot.AtYaw];
            _rec[MpNetWire.World.AtEscape] = MpNetWire.Fix(EscapeTimer);
            _rec[MpNetWire.World.AtEscapeTotal] = MpNetWire.Fix(EscapeTotal);
            _rec[MpNetWire.World.AtUnlocks] = _unlockKey;
            _rec[MpNetWire.World.AtPlayers] = MpCoop.Players;
            _rec[MpNetWire.World.AtFlags] = (MpPlayerState.UnlockAll ? MpNetWire.World.FlagUnlockAll : 0) | (MpCinematics.Active ? MpNetWire.World.FlagCinema : 0) | (SwitchingWorld ? MpNetWire.World.FlagSwitching : 0);
            _net.WriteWorld(_rec);
        }

        /// <summary>A remote's place in this world (game feet, morphed, dead, the area it is in); false when it is elsewhere or not playing.</summary>
        private static bool RemoteIn(MpRemote r, int pak, out Vector3 feet, out bool morphed, out bool dead, out int area)
        {
            int[] s = r.Ints;
            feet = new Vector3(MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtX]), MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtY]), MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtZ]));
            int f = s[MpNetWire.PlayerSlot.AtFlags];
            morphed = (f & MpNetWire.PlayerSlot.FlagMorphed) != 0;
            dead = (f & MpNetWire.PlayerSlot.FlagDead) != 0;
            area = s[MpNetWire.PlayerSlot.AtArea];
            return s[MpNetWire.PlayerSlot.AtStatus] == MpNetWire.PlayerSlot.StatusPlaying && s[MpNetWire.PlayerSlot.AtPak] == pak && (f & MpNetWire.PlayerSlot.FlagJoined) != 0;
        }

        private string NameOf(int playerId)
        {
            if (_net != null)
                foreach (MpRemote r in _net.Remotes)
                    if (r.PlayerId == playerId) return r.Name;
            return "Player " + playerId;
        }

        // ------------------------------------------------------------------ sending

        private void Send(MpNetWire.Writer w)
        {
            if (_net == null || w == null) return;
            _net.Send(w.Bytes());
        }

        private void SendIf(bool linked, MpNetWire.Writer w)
        {
            if (!linked || Linked) Send(w);
        }

        /// <summary>The master's script deliveries so far, out (they always go before anything else the master sends).</summary>
        private void FlushScripts()
        {
            if (_scriptBatch == null || _scriptCount == 0) return;
            _scriptBatch.Patch(_scriptCountAt, _scriptCount);
            Send(_scriptBatch);
            MpCoop.LabScriptOut += _scriptCount;
            _scriptBatch = null;
            _scriptCount = 0;
        }

        private void OnDelivered(MpEntity from, uint target, int msg, int state)
        {
            if (!Linked || MpScripts.Follower || !_net.IsMaster) return;
            uint fromId = from != null ? from.NetId : 0u;
            if (state == MpScriptMsg.StInside)
            {
                // "inside" goes every frame: one in half a second is plenty for the helpers
                long key = ((long)fromId << 32) ^ ((long)target << 8) ^ msg;
                float t;
                if (_insideSent.TryGetValue(key, out t) && Time.unscaledTime - t < 0.5f) return;
                _insideSent[key] = Time.unscaledTime;
            }

            if (_scriptBatch == null)
            {
                _scriptBatch = new MpNetWire.Writer(MpNetWire.Msg.Script);
                _scriptCountAt = _scriptBatch.Length;
                _scriptBatch.U8(0);
                _scriptCount = 0;
            }

            _scriptBatch.U32(fromId).U32(target).U8(msg);
            _scriptCount++;
            if (_scriptCount >= 50) FlushScripts();
        }

        private void OnOwnFire(int beam, bool charged, float factor, Vector3 muzzle, Vector3 dir)
        {
            if (!Linked) return;
            Vector3 m = MpSpace.ToGame(muzzle), d = MpSpace.DirToGame(dir);
            Send(new MpNetWire.Writer(MpNetWire.Msg.Fire).I32(_net.LocalId).U8(beam).U8(charged ? 1 : 0).F32(factor).F32(m.x).F32(m.y).F32(m.z).F32(d.x).F32(d.y).F32(d.z));
        }

        private void OnOwnBomb(Vector3 at)
        {
            if (Linked) Send(new MpNetWire.Writer(MpNetWire.Msg.Bomb).I32(_net.LocalId).F32(at.x).F32(at.y).F32(at.z));
        }

        /// <summary>A helper's own hit: to the master (the master applies it and its world answers); not-shared things are hit here.</summary>
        private bool HelperHit(MpEntity e, int weapon, bool charged, float damage)
        {
            if (!MpScripts.Follower || Scripts.FindNet(e.NetId) != e) return e.TakeDamage(weapon, charged, damage);
            Send(new MpNetWire.Writer(MpNetWire.Msg.Damage).I32(_net.LocalId).U32(e.NetId).U8(weapon).U8(charged ? 1 : 0).F32(damage));
            var en = e as MpEnemy;
            return en == null || (!en.Dead && en.Active);
        }

        private bool SendHurt(int player, float damage, Vector3? from, bool ignoreImmunity)
        {
            if (!Linked) return false;
            float until;
            if (!ignoreImmunity && _remoteImmune.TryGetValue(player, out until) && Time.time < until) return false;
            _remoteImmune[player] = Time.time + 0.5f;
            Vector3 f = from ?? Vector3.zero;
            FlushScripts();
            Send(new MpNetWire.Writer(MpNetWire.Msg.Hurt).I32(player).F32(damage).F32(f.x).F32(f.y).F32(f.z).U8((ignoreImmunity ? 1 : 0) | (from.HasValue ? 2 : 0)));
            return true;
        }

        private void PickFocus(MpEnemy e)
        {
            if (_net == null || !_net.IsMaster || _net.Remotes.Count == 0) return;
            Vector3 p = e.Pos;
            bool localOk = State != null && State.Energy > 0f && !_warping;
            float best = localOk ? (PlayerGame - p).sqrMagnitude : float.MaxValue;
            int pak = MpNetWire.PakHash(CurrentPak);
            foreach (MpRemote r in _net.Remotes)
            {
                Vector3 feet;
                bool morphed, dead;
                int area;
                if (!RemoteIn(r, pak, out feet, out morphed, out dead, out area) || dead) continue;
                float d = (feet - p).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                MpEnemy.FocusFeet = feet;
                MpEnemy.FocusPlayer = r.PlayerId;
                MpEnemy.FocusDead = false;
            }
        }

        private void OnEnemyDied(MpEnemy e, float lastBlow, bool frozen)
        {
            if (!Linked || !_net.IsMaster) return;
            FlushScripts();
            Send(new MpNetWire.Writer(MpNetWire.Msg.EnemyDie).U32(e.NetId).F32(lastBlow).U8(frozen ? 1 : 0));
        }

        private void OnGenerated(MpEntity generator, MpEntity template, MpEntity copy, Vector3 at, float yaw, float scale)
        {
            // the master names every copy (its helpers make theirs under the same id)
            Scripts.SetNetId(copy, 0xC0000000u | (_nextGenId++ & 0x3FFFFFFFu));
            if (!Linked || !_net.IsMaster) return;
            FlushScripts();
            Send(new MpNetWire.Writer(MpNetWire.Msg.Gen).U32(generator != null ? generator.NetId : 0u).U32(template.Id).U32(copy.NetId).F32(at.x).F32(at.y).F32(at.z).F32(yaw).F32(scale).U8(0));
        }

        private void CoopAskSkip()
        {
            Send(new MpNetWire.Writer(MpNetWire.Msg.SkipAsk).I32(_net.LocalId));
            if (Hud != null) Hud.ShowMessage("Asked " + MpCoop.MasterName + " to skip", 2f);
        }

        /// <summary>The master loaded a save (or the game changed under everyone): the helpers take it again.</summary>
        private void CoopGameChanged()
        {
            _gameStamp++;
            _stateSent.Clear();
        }

        // ------------------------------------------------------------------ the master: state out

        private int StateKey(MpEntity e) { return (e.Active ? 1 : 0) | (e.NetWord << 1); }

        private void SendStateDiffs()
        {
            MpNetWire.Writer w = null;
            int n = 0, countAt = 0;
            foreach (MpEntity e in Scripts.All)
            {
                int key = StateKey(e);
                int was;
                if (!_stateSent.TryGetValue(e, out was)) { _stateSent[e] = e.NetInitial; was = e.NetInitial; }
                if (was == key) continue;
                _stateSent[e] = key;
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.State).I32(-1); countAt = w.Length; w.U8(0); n = 0; }
                w.U32(e.NetId).U8(key & 1).I32(key >> 1);
                if (++n >= 50) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
            // entities gone with their rooms
            if (_stateSent.Count > Scripts.All.Count * 2 + 64)
            {
                _scratch.Clear();
                foreach (var kv in _stateSent) if (kv.Key.World == null || !Scripts.ByArea.ContainsKey(kv.Key.Area)) _scratch.Add(kv.Key);
                foreach (MpEntity e in _scratch) _stateSent.Remove(e);
            }
        }

        /// <summary>A room's entities that are not as the disc has them (and the copies generators made), to one player or all.</summary>
        private void SendAreaCensus(int area, int to)
        {
            List<MpEntity> list;
            if (!Scripts.ByArea.TryGetValue(area, out list)) return;
            FlushScripts();
            foreach (MpEntity e in list)
            {
                if (!e.Generated) continue;
                var en = e as MpEnemy;
                var pk = e as MpPickupEntity;
                if ((en == null && pk == null) || (en != null && en.Gone) || (pk != null && pk.Taken)) continue;
                Vector3 at;
                MpGeneratorEntity.PositionOf(e, out at);
                if (pk != null && pk.Body != null) at = pk.Body.localPosition;
                Send(new MpNetWire.Writer(MpNetWire.Msg.Gen).U32(0u).U32(e.Id).U32(e.NetId).F32(at.x).F32(at.y).F32(at.z).F32(en != null ? en.Yaw : 0f).F32(1f).U8(1));
            }

            MpNetWire.Writer w = null;
            int n = 0, countAt = 0, sent = 0;
            foreach (MpEntity e in list)
            {
                int key = StateKey(e);
                if (key == e.NetInitial && !e.Generated) continue;
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.State).I32(to); countAt = w.Length; w.U8(0); n = 0; }
                w.U32(e.NetId).U8(key & 1).I32(key >> 1);
                sent++;
                if (++n >= 50) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
            SendPlatforms(area, true);
            MpLog.Info("Co-op: room " + area + " '" + World.Areas[area].Name + "' census to " + (to < 0 ? "everyone" : "player " + to) + ": " + sent + " entities changed");
        }

        private readonly Dictionary<MpMovingPlatform, Vector3> _platformSent = new Dictionary<MpMovingPlatform, Vector3>();

        private void SendPlatforms(int area, bool all)
        {
            MpNetWire.Writer w = null;
            int n = 0, countAt = 0;
            foreach (MpEntity e in Scripts.All)
            {
                var p = e as MpMovingPlatform;
                if (p == null || (area >= 0 && p.Area != area)) continue;
                Vector3 was;
                bool known = _platformSent.TryGetValue(p, out was);
                if (!all && known && (was - p.GamePos).sqrMagnitude < 1e-4f && Mathf.Abs(p.CurSpeed) < 1e-4f) continue;
                if (!all && !known && Mathf.Abs(p.CurSpeed) < 1e-4f) { _platformSent[p] = p.GamePos; continue; }
                _platformSent[p] = p.GamePos;
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.Platform); countAt = w.Length; w.U8(0); n = 0; }
                w.U32(p.NetId).F32(p.GamePos.x).F32(p.GamePos.y).F32(p.GamePos.z).U32(p.Current != null ? p.Current.NetId : 0u).U32(p.Target != null ? p.Target.NetId : 0u).F32(p.CurSpeed);
                if (++n >= 15) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
        }

        private void SendEnemies()
        {
            MpNetWire.Writer w = null;
            int n = 0, countAt = 0;
            float now = Time.unscaledTime;
            foreach (MpEntity ent in Scripts.All)
            {
                var e = ent as MpEnemy;
                if (e == null || !e.Active || e.Dead || e.Gone || e.Body == null) continue;
                int anim = e.Anim != null ? e.Anim.Anim : -1;
                var sig = new Vector4(e.Pos.x + e.Yaw * 0.001f, e.Pos.y, e.Pos.z, anim * 1000f + e.HealthNow);
                Vector4 was;
                float at;
                if (_enemySent.TryGetValue(e, out was) && (was - sig).sqrMagnitude < 1e-5f && _enemySentT.TryGetValue(e, out at) && now - at < 1f) continue;
                _enemySent[e] = sig;
                _enemySentT[e] = now;
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.EnemyState); countAt = w.Length; w.U8(0); n = 0; }
                int flags = (e.Anim != null && e.Anim.Loop ? 1 : 0) | (e.Frozen ? 2 : 0);
                w.U32(e.NetId).F32(e.Pos.x).F32(e.Pos.y).F32(e.Pos.z).U16(Mathf.RoundToInt(Mathf.Repeat(e.Yaw, 360f) * 100f) & 0xFFFF).U16(anim & 0xFFFF).U8(flags).F32(e.HealthNow).F32(e.MaxHealth);
                if (++n >= 15) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
            if (_enemySent.Count > 512)
            {
                var gone = new List<MpEnemy>();
                foreach (var kv in _enemySent) if (kv.Key.Gone || kv.Key.Body == null) gone.Add(kv.Key);
                foreach (MpEnemy e in gone) { _enemySent.Remove(e); _enemySentT.Remove(e); }
            }
        }

        private int UnlockKey()
        {
            int h = MpPlayerState.UnlockAll ? 7 : 3;
            for (int i = 0; i < State.Items.Length; i++) h = h * 31 + State.Items[i];
            return h * 31 + State.MissileCapacity;
        }

        private void SendUnlocksIfChanged(bool force)
        {
            _unlockKey = UnlockKey();
            if (!force && _unlockKey == _unlockSent) return;
            _unlockSent = _unlockKey;
            var w = new MpNetWire.Writer(MpNetWire.Msg.Unlocks).U8(MpPlayerState.UnlockAll ? 1 : 0).U16(State.MissileCapacity).U8(State.Items.Length);
            for (int i = 0; i < State.Items.Length; i++) w.U16(State.Items[i]);
            FlushScripts();
            Send(w);
        }

        private void SendSnapshot(int to)
        {
            FlushScripts();
            float yaw = MpNetWire.Unfix(_own[MpNetWire.PlayerSlot.AtYaw]);
            Send(new MpNetWire.Writer(MpNetWire.Msg.SnapBegin).I32(to).I32(_gameStamp).Str(CurrentPak).U16(Mathf.Max(0, WorldView.CurrentArea)).F32(PlayerGame.x).F32(PlayerGame.y).F32(PlayerGame.z).F32(yaw).F32(EscapeTimer).F32(EscapeTotal));
            MpNetWire.Writer w = null;
            int n = 0, countAt = 0;
            foreach (MpWorld.Area a in World.Areas)
            {
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.SnapLayers).I32(to); countAt = w.Length; w.U8(0); n = 0; }
                w.U16(a.Index).U32((uint)(a.LayerBits & 0xFFFFFFFFu)).U32((uint)(a.LayerBits >> 32));
                if (++n >= 40) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
            w = null;
            foreach (uint id in Scripts.Mailbox)
            {
                if (w == null) { w = new MpNetWire.Writer(MpNetWire.Msg.SnapMail).I32(to); countAt = w.Length; w.U8(0); n = 0; }
                w.U32(id);
                if (++n >= 100) { w.Patch(countAt, n); Send(w); w = null; }
            }

            if (w != null) { w.Patch(countAt, n); Send(w); }
            SendUnlocksIfChanged(true);
            Send(new MpNetWire.Writer(MpNetWire.Msg.SnapEnd).I32(to));
            MpLog.Info("Co-op: the game sent to " + NameOf(to) + " (player " + to + "): '" + CurrentPak + "' room " + WorldView.CurrentArea + ", " + World.Areas.Count + " rooms' layers, " + Scripts.Mailbox.Count + " memory relays");
        }

        private void ApplyHealthScale(float scale)
        {
            MpEnemy.HealthScale = scale;
            int n = 0;
            foreach (MpEntity e in Scripts.All)
            {
                var en = e as MpEnemy;
                if (en == null || en.Gone) continue;
                en.ScaleHealth(scale);
                n++;
            }

            MpLog.Info("Co-op: " + MpCoop.Players + " player(s) - enemy and boss health x" + scale.ToString("0.00") + " (" + n + " enemies now)");
        }

        // ------------------------------------------------------------------ receiving

        private void Receive()
        {
            byte[] msg;
            int guard = 400;
            while (guard-- > 0 && _net.TryReceive(out msg))
            {
                try { Handle(new MpNetWire.Reader(msg)); }
                catch (System.Exception e) { MpLog.Warn("Co-op: message " + (msg.Length > 0 ? ((MpNetWire.Msg)msg[0]).ToString() : "?") + " not handled: " + e.Message); }
            }
        }

        private void Handle(MpNetWire.Reader r)
        {
            bool master = _net.IsMaster;
            int me = _net.LocalId;
            switch (r.Type)
            {
                case MpNetWire.Msg.Hello:
                {
                    int from = r.I32();
                    if (master && from != me && Running) SendSnapshot(from);
                    return;
                }

                case MpNetWire.Msg.SnapBegin:
                {
                    if (master || r.I32() != me) return;
                    int stamp = r.I32();
                    string pak = r.Str();
                    int area = r.U16();
                    var s = new MpSaveGame { World = pak, Area = area, X = r.F32(), Y = r.F32(), Z = r.F32(), Yaw = r.F32(), PlaySeconds = PlaySeconds, Where = "co-op" };
                    _snapEscape = r.F32();
                    _snapEscapeTotal = r.F32();
                    _snap = s;
                    _snapStamp = stamp;
                    return;
                }

                case MpNetWire.Msg.SnapLayers:
                {
                    if (master || r.I32() != me || _snap == null) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++) { int a = r.U16(); ulong lo = r.U32(), hi = r.U32(); _snap.Layers[a] = lo | (hi << 32); }
                    return;
                }

                case MpNetWire.Msg.SnapMail:
                {
                    if (master || r.I32() != me || _snap == null) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++) _snap.Mailbox.Add(r.U32());
                    return;
                }

                case MpNetWire.Msg.SnapEnd:
                {
                    if (master || r.I32() != me || _snap == null) return;
                    JoinMaster(_snap, _snapStamp);
                    _snap = null;
                    return;
                }

                case MpNetWire.Msg.Unlocks:
                {
                    if (master) return;
                    _masterUnlockAll = r.U8() != 0;
                    _masterMissileCap = r.U16();
                    int n = r.U8();
                    for (int i = 0; i < n && i < _masterItems.Length; i++) _masterItems[i] = r.U16();
                    _haveMasterUnlocks = true;
                    if (_joined) ApplyUnlocks();
                    return;
                }

                case MpNetWire.Msg.Script:
                {
                    if (master || !MpScripts.Follower) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        uint from = r.U32(), target = r.U32();
                        int m = r.U8();
                        Scripts.DeliverRemote(from, target, m);
                    }

                    MpCoop.LabScriptIn += n;
                    return;
                }

                case MpNetWire.Msg.State:
                {
                    if (master || !MpScripts.Follower) return;
                    int to = r.I32();
                    if (to >= 0 && to != me) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        uint id = r.U32();
                        bool active = r.U8() != 0;
                        int word = r.I32();
                        MpEntity e = Scripts.FindNet(id);
                        if (e == null) continue;
                        if (e.Active != active) e.SetActive(active);
                        if (e.NetWord != word) e.ApplyNetWord(word);
                        MpCoop.LabStateIn++;
                    }

                    return;
                }

                case MpNetWire.Msg.Gen:
                {
                    if (master || !MpScripts.Follower) return;
                    uint gen = r.U32(), templ = r.U32(), id = r.U32();
                    var at = new Vector3(r.F32(), r.F32(), r.F32());
                    float yaw = r.F32(), scale = r.F32();
                    if (Scripts.FindNet(id) != null) return;
                    MpEntity template = null;
                    List<MpEntity> list;
                    if (Scripts.ById.TryGetValue(templ, out list)) foreach (MpEntity e in list) if (!e.Generated) { template = e; break; }
                    if (template == null) return;
                    MpGeneratorEntity.Replay(Scripts, gen != 0 ? Scripts.FindNet(gen) : null, template, id, at, yaw, scale);
                    MpCoop.LabGenIn++;
                    return;
                }

                case MpNetWire.Msg.EnemyState:
                {
                    if (master || !MpScripts.Follower) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        uint id = r.U32();
                        var pos = new Vector3(r.F32(), r.F32(), r.F32());
                        float yaw = r.U16() / 100f;
                        int anim = r.S16();
                        int flags = r.U8();
                        float hp = r.F32(), max = r.F32();
                        var e = Scripts.FindNet(id) as MpEnemy;
                        if (e == null || e.Dead || e.Gone) continue;
                        if (!e.HasNet) { e.Pos = pos; e.Yaw = yaw; e.ApplyYaw(); }
                        e.HasNet = true;
                        e.NetPos = pos;
                        e.NetYaw = yaw;
                        e.NetAnim = anim;
                        e.NetLoop = (flags & 1) != 0;
                        e.NetFrozen = (flags & 2) != 0;
                        e.HealthNow = hp;
                        e.MaxHealth = max;
                        MpCoop.LabEnemyIn++;
                    }

                    return;
                }

                case MpNetWire.Msg.EnemyDie:
                {
                    if (master || !MpScripts.Follower) return;
                    var e = Scripts.FindNet(r.U32()) as MpEnemy;
                    float blow = r.F32();
                    bool frozen = r.U8() != 0;
                    if (e != null) e.NetDie(blow, frozen);
                    return;
                }

                case MpNetWire.Msg.Damage:
                {
                    int from = r.I32();
                    uint id = r.U32();
                    int weapon = r.U8();
                    bool charged = r.U8() != 0;
                    float dmg = r.F32();
                    if (!master || from == me) return;
                    MpEntity e = Scripts.FindNet(id);
                    if (e != null) { e.TakeDamage(weapon, charged, dmg); MpCoop.LabDamageIn++; }
                    return;
                }

                case MpNetWire.Msg.Hurt:
                {
                    int to = r.I32();
                    float dmg = r.F32();
                    var from = new Vector3(r.F32(), r.F32(), r.F32());
                    int f = r.U8();
                    if (to != me) return;
                    MpCoop.LabHurtIn++;
                    DamagePlayer(dmg, (f & 2) != 0 ? (Vector3?)from : null, (f & 1) != 0);
                    return;
                }

                case MpNetWire.Msg.Fire:
                {
                    int from = r.I32();
                    if (from == me || Shots == null) return;
                    int beam = r.U8();
                    bool charged = r.U8() != 0;
                    float factor = r.F32();
                    var m = new Vector3(r.F32(), r.F32(), r.F32());
                    var d = new Vector3(r.F32(), r.F32(), r.F32());
                    if (!RemoteHere(from)) return;
                    MpCoop.LabFireIn++;
                    // their cannon on their avatar fires its own copy of the shot (from its muzzle, with its animation and effects)
                    if (_remoteView != null && _remoteView.HasCannon(from)) return;
                    MpRemote who = RemoteOf(from);
                    Shots.FireGhost(beam, charged, factor, who != null ? TheirsToUnity(who, m) : MpSpace.ToUnity(m), MpSpace.DirToUnity(d));
                    return;
                }

                case MpNetWire.Msg.Bomb:
                {
                    int from = r.I32();
                    if (from == me || !RemoteHere(from)) return;
                    var at = new Vector3(r.F32(), r.F32(), r.F32());
                    MpRemote who = RemoteOf(from);
                    Bombs.DropGhost(who != null ? MpSpace.ToGame(TheirsToUnity(who, at)) : at, GunAssets);      // by their ball, as their avatar sees it
                    return;
                }

                case MpNetWire.Msg.TakeAsk:
                {
                    int from = r.I32();
                    var p = Scripts.FindNet(r.U32()) as MpPickupEntity;
                    if (master && from != me && p != null && p.Active && !p.Taken) p.Take(from);
                    return;
                }

                case MpNetWire.Msg.Give:
                {
                    if (r.I32() != me || master) return;
                    int item = r.I32(), cap = r.I32(), amount = r.I32();
                    GiveItem(item, cap, amount);
                    PlayUiSfx(MpSfx.ScanDone);
                    return;
                }

                case MpNetWire.Msg.SkipAsk:
                {
                    int from = r.I32();
                    if (master && from != me)
                    {
                        MpLog.Info("Co-op: " + NameOf(from) + " skipped the cutscene");
                        MpCinematics.Skip();
                    }

                    return;
                }

                case MpNetWire.Msg.Skipped:
                    if (!master && MpScripts.Follower) MpCinematics.Skip();
                    return;

                case MpNetWire.Msg.AreaAsk:
                {
                    int from = r.I32();
                    int area = r.U16();
                    if (master && from != me && WorldView.IsLoaded(area)) SendAreaCensus(area, from);
                    return;
                }

                case MpNetWire.Msg.Platform:
                {
                    if (master || !MpScripts.Follower) return;
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        var p = Scripts.FindNet(r.U32()) as MpMovingPlatform;
                        var pos = new Vector3(r.F32(), r.F32(), r.F32());
                        uint cur = r.U32(), target = r.U32();
                        float speed = r.F32();
                        if (p != null) p.Correct(pos, cur, target, speed);
                    }

                    return;
                }

                case MpNetWire.Msg.EnemyShot:
                {
                    if (master || !MpScripts.Follower) return;
                    uint wpsc = r.U32();
                    var from = new Vector3(r.F32(), r.F32(), r.F32());
                    var dir = new Vector3(r.F32(), r.F32(), r.F32());
                    float dmg = r.F32(), scale = r.F32();
                    MpEnemyShots.Fire(wpsc, from, dir, dmg, null, scale);
                    return;
                }

                case MpNetWire.Msg.Notice:
                    if (Hud != null) Hud.ShowMessage(r.Str(), 3f);
                    return;
            }
        }

        public MpRemote RemoteOf(int playerId)
        {
            if (_net != null) foreach (MpRemote r in _net.Remotes) if (r.PlayerId == playerId) return r;
            return null;
        }

        /// <summary>
        /// A game position of another player's, in Unity where THEIR client draws it - their world's origin, not ours (each client slides
        /// its own world): that is where it stands next to their avatar. (Their cutscene turn is not sent; their things are hidden then.)
        /// </summary>
        public static Vector3 TheirsToUnity(MpRemote r, Vector3 game)
        {
            Vector3 o = MpNetWire.OriginOf(r.Ints);
            if (o == Vector3.zero) return MpSpace.ToUnity(game);
            return o + new Vector3(game.x, game.z, game.y) * MpSpace.Scale;
        }

        /// <summary>The player is in this world (their shots and bombs are drawn only then).</summary>
        private bool RemoteHere(int playerId)
        {
            int pak = MpNetWire.PakHash(CurrentPak);
            foreach (MpRemote r in _net.Remotes)
                if (r.PlayerId == playerId) return r.Ints[MpNetWire.PlayerSlot.AtPak] == pak;
            return true;
        }

        // ------------------------------------------------------------------ the helper: joining

        private float _snapEscape, _snapEscapeTotal;

        /// <summary>The master's game, loaded here like a save (nothing written): its world, layers, memory relays, unlocks; Samus beside the master.</summary>
        private void JoinMaster(MpSaveGame s, int stamp)
        {
            MpCoop.LabJoins++;
            _joined = true;
            _seenGame = stamp;
            MpScripts.Follower = true;
            MpCoop.Helper = true;
            MpProjectiles.HitHook = HelperHit;
            _stateSent.Clear();
            // beside the master, not inside them
            float a = (_net.LocalId % 8) * 0.785f;
            Vector3 beside = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 1.6f;
            Vector3 at = new Vector3(s.X, s.Y, s.Z) + beside;
            if (!MpEnemy.WalkClear(new Vector3(s.X, s.Y, s.Z + 1f), at + new Vector3(0f, 0f, 1f))) at = new Vector3(s.X, s.Y, s.Z);
            s.X = at.x; s.Y = at.y; s.Z = at.z + 0.2f;
            if (_haveMasterUnlocks)
            {
                System.Array.Copy(_masterItems, s.Items, s.Items.Length);
                s.MissileCapacity = _masterMissileCap;
            }
            else
            {
                System.Array.Copy(State.Items, s.Items, s.Items.Length);
                s.MissileCapacity = State.MissileCapacity;
            }

            s.Missiles = s.MissileCapacity;
            s.Beam = State.Beam;
            s.Visor = State.Visor;
            s.Scans.AddRange(_scanned);
            MpLog.Info("Co-op: joining " + MpCoop.MasterName + "'s game: '" + s.World + "' room " + s.Area + ", " + s.Layers.Count + " rooms' layers, " + s.Mailbox.Count + " memory relays");
            if (_haveMasterUnlocks) MpPlayerState.UnlockAll = _masterUnlockAll;
            Apply(s, true);
            EscapeTimer = _snapEscape;
            EscapeTotal = _snapEscapeTotal;
            if (Hud != null) Hud.ShowMessage("JOINED " + MpCoop.MasterName.ToUpperInvariant() + "'S GAME", 4f);
        }

        /// <summary>A room was built here: a helper asks the master for its state; the master tells everyone what a room it just built holds.</summary>
        private void OnAreaReady(MpAreaView view)
        {
            if (_net == null || !_net.Online) return;
            if (MpScripts.Follower) Send(new MpNetWire.Writer(MpNetWire.Msg.AreaAsk).I32(_net.LocalId).U16(view.Index));
            else if (_net.IsMaster && Linked) SendAreaCensus(view.Index, -1);
        }

        /// <summary>Every frame: the world view's ready event follows the current world view (a world switch makes a new one).</summary>
        private void WatchAreas()
        {
            if (WorldView == null || WorldView == _askView) return;
            if (_askView != null) _askView.AreaReady -= OnAreaReady;
            _askView = WorldView;
            _askView.AreaReady += OnAreaReady;
        }

        /// <summary>The master's unlocks are this Samus's (beams, visors, morph ball, bombs, suits, tanks, missile capacity).</summary>
        private void ApplyUnlocks()
        {
            int tanks = State.EnergyTanks, cap = State.MissileCapacity;
            MpPlayerState.UnlockAll = _masterUnlockAll;
            System.Array.Copy(_masterItems, State.Items, State.Items.Length);
            State.MissileCapacity = _masterMissileCap;
            State.Missiles = Mathf.Clamp(State.Missiles + Mathf.Max(0, _masterMissileCap - cap), 0, _masterMissileCap);
            if (State.EnergyTanks > tanks) State.Energy = State.MaxEnergy;
            State.Energy = Mathf.Min(State.Energy, State.MaxEnergy);
            if (Cannon != null && !State.HasBeam(State.Beam)) Cannon.ChangeBeam(MpPlayerState.BeamPower);
            if (!State.HasVisor(State.Visor)) SetVisor(MpPlayerState.VisorCombat);
        }

        // ------------------------------------------------------------------ dying with others around

        /// <summary>Energy gone (or "unstuck") with other players in this world: back beside one of them, full energy, nothing reloaded.</summary>
        private bool CoopRespawn(string why)
        {
            if (_net == null || !_net.Online || _net.Remotes.Count == 0) return false;
            Vector3 best = Vector3.zero;
            int bestArea = -1;
            string who = null;
            int pak = MpNetWire.PakHash(CurrentPak);
            if (MpScripts.Follower && _net.WorldValid && _net.World[MpNetWire.World.AtPak] == pak)
            {
                int[] w = _net.World;
                best = new Vector3(MpNetWire.Unfix(w[MpNetWire.World.AtX]), MpNetWire.Unfix(w[MpNetWire.World.AtY]), MpNetWire.Unfix(w[MpNetWire.World.AtZ]));
                bestArea = w[MpNetWire.World.AtArea];
                who = MpCoop.MasterName;
            }
            else
            {
                foreach (MpRemote r in _net.Remotes)
                {
                    Vector3 feet;
                    bool morphed, dead;
                    int area;
                    if (!RemoteIn(r, pak, out feet, out morphed, out dead, out area) || dead || area < 0) continue;
                    best = feet;
                    bestArea = area;
                    who = r.Name;
                    break;
                }
            }

            if (bestArea < 0 || bestArea >= World.Areas.Count) return false;
            if (EscapeTotal > 0f && EscapeTimer <= 0f && !MpScripts.Follower) return false;      // the escape ran out: game over for all
            State.Energy = State.MaxEnergy;
            MpLog.Info("Samus: respawn (" + why + ") beside " + who);
            if (Ball.Morphed) Ball.Unmorph();
            StartCoroutine(Warp(bestArea, best + new Vector3(1.2f, 0f, 0.3f), _spawnYaw, null));
            return true;
        }
    }
}
