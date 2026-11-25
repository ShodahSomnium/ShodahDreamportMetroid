using System;
using System.Collections.Generic;
using Fusion;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Network.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>A player of the instance seen through the net: their slot's ints, their Somnium player when found.</summary>
    public sealed class MpRemote
    {
        public int Slot;
        public int PlayerId;
        public ISomniumPlayer Player;
        public string Name;
        public readonly int[] Ints = new int[MpNetWire.PlayerSlot.Used];
    }

    /// <summary>
    /// What the game needs of a network (MpNet over Fusion; the labs plug in a loopback between two Unity processes).
    /// </summary>
    public interface IMpNetLink
    {
        bool Online { get; }
        bool IsMaster { get; }
        int LocalId { get; }
        int[] World { get; }
        bool WorldValid { get; }
        IReadOnlyList<MpRemote> Remotes { get; }
        void Tick(int[] ownState);
        void WriteWorld(int[] rec);
        void Send(byte[] msg);
        bool TryReceive(out byte[] msg);
        void Dispose();
    }

    /// <summary>
    /// The Metroid Prime Dreamport over the world's Photon Fusion shared-mode session (the Skyrim Dreamport's SkyNet, unchanged in
    /// its rules): scene objects found by name (MpNetWire), no runtime spawns, no networked behaviours of our own.
    /// - The instance master (Fusion's shared-mode master, NetworkRunner.IsSharedModeMasterClient) holds "Mp Net World" and writes the game
    ///   there; everyone reads it. When the master leaves, Fusion makes another player master and that one takes the record.
    /// - Each client claims one "Mp Net Player N" slot (MpSlotClaim) and writes where its Samus is; everyone reads the others.
    /// - Events are RPC byte[] messages to all on "Mp Net Events" (the sender gets its own too).
    /// Alone, in the editor, before the room is joined or without the scene objects: Online is false and the local player is the
    /// master of their own game. Only Fusion / Somnium members the upload server already compiled for sibling worlds are used
    /// (DevTools/fusionapi/check.sh).
    /// </summary>
    public sealed class MpNet : IMpNetLink
    {
        private const float WriteEvery = 0.1f, PresentEvery = 1f, ServeEvery = 0.25f;
        private readonly NetworkBridgeData[] _slotObjects = new NetworkBridgeData[MpNetWire.MaxPlayers];
        private readonly int[][] _slots = new int[MpNetWire.MaxPlayers][];
        private readonly bool[] _valid = new bool[MpNetWire.MaxPlayers];
        private readonly bool[] _authority = new bool[MpNetWire.MaxPlayers];
        private readonly List<int> _present = new List<int>(32);
        private readonly MpSlotClaim _claim = new MpSlotClaim();
        private readonly int[] _own = new int[MpNetWire.PlayerSlot.Used];
        private readonly List<MpRemote> _remotes = new List<MpRemote>();
        private readonly MpRemote[] _bySlot = new MpRemote[MpNetWire.MaxPlayers];
        private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
        private NetworkBridgeData _world;
        private NetworkBridgeEvents _events;
        private bool _subscribed;
        private int _slotCount;
        private bool _found;
        private float _nextFind, _presentAt, _nextServe, _nextWrite, _nextWalk, _nextWorldAsk;
        private readonly float _t0 = Time.realtimeSinceStartup;
        private readonly int[] _worldInts = new int[MpNetWire.World.Used];

        public int[] World { get { return _worldInts; } }
        public bool WorldValid { get; private set; }

        public MpNet()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new int[MpNetWire.PlayerSlot.Used];
        }

        // the world's own SceneNetworking (Net/SceneNetworking.cs: live, the one that registers the scene's objects) when it runs;
        // in the editor's labs the Community Modules' one runs instead (our code must not name it: the upload server skips the
        // Community Modules), so then the running Fusion runner is found through Fusion itself
        private static NetworkRunner Runner
        {
            get
            {
                if (Net.SceneNetworking.Instance != null) return Net.SceneNetworking.NetworkRunnerRef;
                var all = NetworkRunner.Instances;
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].IsRunning) return all[i];
                return null;
            }
        }

        /// <summary>The scene's network objects are registered (our SceneNetworking says so once our own join is in).</summary>
        private static bool Ready { get { return Net.SceneNetworking.Instance == null || Net.SceneNetworking.IsNetworkReady; } }

        private static bool MasterClient { get { NetworkRunner r = Runner; return r != null && r.IsSharedModeMasterClient; } }

        public bool Online { get { return Runner != null && Ready && _found; } }

        /// <summary>The instance master: Fusion's shared-mode master; alone / offline = this client.</summary>
        public bool IsMaster { get { return !Online || MasterClient; } }

        public int LocalId { get { return Runner != null ? Runner.LocalPlayer.PlayerId : -1; } }

        public IReadOnlyList<MpRemote> Remotes { get { return _remotes; } }

        public int MySlot { get { return _claim.Holding ? _claim.Slot : -1; } }

        private static bool Valid(NetworkBehaviour b)
        {
            return b != null && b.Object != null && b.Object.IsValid;
        }

        private int StampMs()
        {
            return (int)((Time.realtimeSinceStartup - _t0) * 1000f);
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Once a frame: find objects, read the world and the slots, step the claim, write our slot.</summary>
        public void Tick(int[] ownState)
        {
            Find();
            bool online = Online;
            if (online && !_subscribed && Valid(_events))
            {
                _events.OnMessageToAll += OnMessage;
                _subscribed = true;
            }

            RefreshPresent(online);
            ReadWorld(online);
            ReadSlots(online);
            StepClaim(online);
            if (Time.unscaledTime >= _nextServe)
            {
                _nextServe = Time.unscaledTime + ServeEvery;
                Housekeeping();
            }

            RefreshRemotes();
            WriteOwn(ownState);
        }

        private void Find()
        {
            if (_found || Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            int slots = 0;
            foreach (NetworkBridgeData d in UnityEngine.Object.FindObjectsByType<NetworkBridgeData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = d.name;
                if (n == MpNetWire.WorldObject) _world = d;
                else if (n.StartsWith(MpNetWire.PlayerPrefix, StringComparison.Ordinal))
                {
                    int k;
                    if (int.TryParse(n.Substring(MpNetWire.PlayerPrefix.Length), out k) && k >= 0 && k < MpNetWire.MaxPlayers)
                    {
                        _slotObjects[k] = d;
                        slots++;
                    }
                }
            }

            foreach (NetworkBridgeEvents e in UnityEngine.Object.FindObjectsByType<NetworkBridgeEvents>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (e.name == MpNetWire.EventsObject && !(e is NetworkBridgeData)) _events = e;
            }

            int count = 0;
            while (count < MpNetWire.MaxPlayers && _slotObjects[count] != null) count++;
            _slotCount = count;
            _found = _world != null && _events != null && count > 0;
            if (_found) MpLog.Info("Net: scene objects found (world record, events, " + count + " player slots)");
            else if (Time.unscaledTime > 20f && Time.unscaledTime < 22f) MpLog.Warn("Net: scene objects missing (world " + (_world != null) + ", events " + (_events != null) + ", slots " + count + ")");
        }

        private void RefreshPresent(bool online)
        {
            if (!online)
            {
                _present.Clear();
                return;
            }

            if (Time.unscaledTime < _presentAt) return;
            _presentAt = Time.unscaledTime + PresentEvery;
            _present.Clear();
            foreach (PlayerRef p in Runner.ActivePlayers) _present.Add(p.PlayerId);
        }

        private void ReadWorld(bool online)
        {
            WorldValid = false;
            if (!online || !Valid(_world)) return;
            NetworkArray<int> ints = _world.IntArray;
            int n = Math.Min(ints.Length, _worldInts.Length);
            for (int i = 0; i < n; i++) _worldInts[i] = ints.Get(i);
            WorldValid = _worldInts[MpNetWire.World.AtMagic] == MpNetWire.Magic && _worldInts[MpNetWire.World.AtProtocol] == MpNetWire.Protocol;
            // the master holds the world record's authority
            if (MasterClient && !_world.Object.HasStateAuthority && Time.unscaledTime >= _nextWorldAsk)
            {
                _nextWorldAsk = Time.unscaledTime + 1f;
                _world.Object.RequestStateAuthority();
            }
        }

        /// <summary>Master: writes the world record (only the ints that changed are sent by Fusion).</summary>
        public void WriteWorld(int[] rec)
        {
            if (!Online || !Valid(_world) || !_world.Object.HasStateAuthority) return;
            rec[MpNetWire.World.AtMagic] = MpNetWire.Magic;
            rec[MpNetWire.World.AtProtocol] = MpNetWire.Protocol;
            rec[MpNetWire.World.AtMaster] = LocalId;
            NetworkArray<int> ints = _world.IntArray;
            int n = Math.Min(ints.Length, rec.Length);
            for (int i = 0; i < n; i++) if (ints.Get(i) != rec[i]) ints.Set(i, rec[i]);
        }

        private void ReadSlots(bool online)
        {
            for (int k = 0; k < _slotCount; k++)
            {
                NetworkBridgeData d = _slotObjects[k];
                bool valid = online && Valid(d);
                _valid[k] = valid;
                _authority[k] = valid && d.Object.HasStateAuthority;
                if (!valid) continue;
                NetworkArray<int> ints = d.IntArray;
                int n = Math.Min(ints.Length, MpNetWire.PlayerSlot.Used);
                for (int i = 0; i < n; i++) _slots[k][i] = ints.Get(i);
            }
        }

        private void StepClaim(bool online)
        {
            var input = new MpSlotClaim.Inputs { Now = Time.realtimeSinceStartupAsDouble, LocalId = LocalId, Allowed = online && _slotCount > 0, SlotCount = _slotCount };
            _claim.Step(input, _slots, _valid, _authority, _present);
            if (_claim.AskAuthority >= 0)
            {
                NetworkBridgeData d = _slotObjects[_claim.AskAuthority];
                if (Valid(d)) d.Object.RequestStateAuthority();
            }

            if (_claim.WriteClaim && _claim.Slot >= 0)
            {
                MpNetWire.WriteClaim(_own, LocalId, _claim.Generation, StampMs());
                Push(_slotObjects[_claim.Slot], _own);
            }

            string note = _claim.TakeNote();
            if (note != null) MpLog.Info("Net: " + note);
        }

        private void Housekeeping()
        {
            int local = LocalId;
            for (int k = 0; k < _slotCount; k++)
            {
                if (!MpSlotRules.ToFree(_slots[k], _authority[k], k == _claim.Slot, local, _present)) continue;
                MpNetWire.WriteFree(_slots[k], StampMs());
                Push(_slotObjects[k], _slots[k]);
            }
        }

        private void Push(NetworkBridgeData d, int[] rec)
        {
            if (!Valid(d) || !d.Object.HasStateAuthority) return;
            NetworkArray<int> ints = d.IntArray;
            int n = Math.Min(ints.Length, rec.Length);
            for (int i = 0; i < n; i++) if (ints.Get(i) != rec[i]) ints.Set(i, rec[i]);
        }

        /// <summary>Our slot: state ints from index AtStatus on (the claim part is ours to keep).</summary>
        private void WriteOwn(int[] state)
        {
            int k = MySlot;
            if (k < 0 || k >= _slotCount || !_authority[k] || MpNetWire.OwnerOf(_own) != LocalId || state == null) return;
            bool changed = false;
            for (int i = MpNetWire.PlayerSlot.AtStatus; i < MpNetWire.PlayerSlot.Used; i++)
            {
                if (i == MpNetWire.PlayerSlot.AtStamp || i == MpNetWire.PlayerSlot.AtSeq) continue;
                if (_own[i] != state[i])
                {
                    _own[i] = state[i];
                    changed = true;
                }
            }

            if (!changed || Time.unscaledTime < _nextWrite) return;
            _nextWrite = Time.unscaledTime + WriteEvery;
            _own[MpNetWire.PlayerSlot.AtSeq]++;
            _own[MpNetWire.PlayerSlot.AtStamp] = StampMs();
            Push(_slotObjects[k], _own);
        }

        private void RefreshRemotes()
        {
            _remotes.Clear();
            int local = LocalId;
            bool missing = false;
            for (int k = 0; k < _slotCount; k++)
            {
                if (!_valid[k] || !MpSlotRules.Listed(_slots[k], local, _present))
                {
                    _bySlot[k] = null;
                    continue;
                }

                int owner = MpNetWire.OwnerOf(_slots[k]);
                MpRemote r = _bySlot[k];
                if (r == null || r.PlayerId != owner)
                {
                    r = new MpRemote { Slot = k, PlayerId = owner, Name = "Player " + owner };
                    _bySlot[k] = r;
                    missing = true;
                }

                Array.Copy(_slots[k], r.Ints, r.Ints.Length);
                if (r.Player == null) missing = true;
                _remotes.Add(r);
            }

            float now = Time.unscaledTime;
            if (now < _nextWalk || (!missing && now < _nextWalk + 4f)) return;
            _nextWalk = now + 1f;
            List<ISomniumPlayer> all = null;
            try { all = SomniumBridge.PlayersContainer?.AllPlayers; }
            catch (Exception) { /* editor */ }
            foreach (MpRemote r in _remotes)
            {
                ISomniumPlayer found = null;
                if (all != null)
                {
                    foreach (ISomniumPlayer p in all)
                    {
                        if (p?.Properties != null && !p.Properties.IsLocal && p.Properties.PhotonId == r.PlayerId)
                        {
                            found = p;
                            break;
                        }
                    }
                }

                r.Player = found;
                string nick = found?.Properties?.NickName;
                if (!string.IsNullOrEmpty(nick)) r.Name = nick;
            }
        }

        // ------------------------------------------------------------------ events

        private void OnMessage(byte[] msg)
        {
            if (msg == null || msg.Length == 0) return;
            lock (_inbox) _inbox.Enqueue(msg);
        }

        /// <summary>Messages received since the last call (including our own, which RPC_SendMessageToAll also delivers here).</summary>
        public bool TryReceive(out byte[] msg)
        {
            lock (_inbox)
            {
                if (_inbox.Count > 0)
                {
                    msg = _inbox.Dequeue();
                    return true;
                }
            }

            msg = null;
            return false;
        }

        /// <summary>To everyone (this client included). Offline: delivered locally only.</summary>
        public void Send(byte[] msg)
        {
            if (msg == null || msg.Length == 0) return;
            if (msg.Length > MpNetWire.MaxRpc)
            {
                MpLog.Warn("Net: a " + msg.Length + "-byte message is over the RPC size; not sent");
                return;
            }

            if (!Online || !Valid(_events))
            {
                OnMessage(msg);
                return;
            }

            try
            {
                _events.RPC_SendMessageToAll(msg);
            }
            catch (Exception e)
            {
                MpLog.Warn("Net: sending failed: " + e.Message);
                OnMessage(msg);
            }
        }

        public void Dispose()
        {
            if (_subscribed && _events != null) _events.OnMessageToAll -= OnMessage;
            _subscribed = false;
        }
    }
}
