using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// The co-op lab's network: two Unity processes (the master in one project, the helper in the private lab copy) joined by a
    /// localhost TCP socket instead of Fusion. Same contract as MpNet: the master writes the world record, each side its slot, events
    /// go to everyone (the sender included). The master's player id is 1, the helper's 2. When the master's process goes, the helper
    /// becomes the master (Fusion's hand-over, as the lab can show it). Lab notes (strings) let the two scenarios keep in step.
    /// Frames: u8 kind (1 world ints, 2 slot ints, 3 event, 4 lab note), i32 length, payload.
    /// </summary>
    public sealed class MpLabLink : IMpNetLink
    {
        private readonly bool _host;
        private readonly int _port;
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _reader;
        private volatile bool _connected, _lost, _stop;
        private readonly object _lock = new object();
        private readonly Queue<byte[]> _events = new Queue<byte[]>();
        private readonly Queue<string> _notes = new Queue<string>();
        private readonly int[] _world = new int[MpNetWire.World.Used];
        private readonly int[] _peerSlot = new int[MpNetWire.PlayerSlot.Used];
        private readonly int[] _sentSlot = new int[MpNetWire.PlayerSlot.Used];
        private readonly int[] _sentWorld = new int[MpNetWire.World.Used];
        private readonly List<MpRemote> _remotes = new List<MpRemote>();
        private readonly MpRemote _peer;
        private bool _promoted, _peerSeen;
        private float _nextSlot;
        public int Sent, Received;
        private readonly float[] _pose = new float[7];
        public bool PeerPoseSeen;

        /// <summary>The peer's head as its client has it (Unity): what Somnium would show as their avatar's head.</summary>
        public void PeerHead(out Vector3 pos, out Quaternion rot)
        {
            lock (_lock) { pos = new Vector3(_pose[0], _pose[1], _pose[2]); rot = new Quaternion(_pose[3], _pose[4], _pose[5], _pose[6]); }
        }

        /// <summary>Lab: this client's head pose for the peer's stand-in avatar (the Somnium avatar's own sync, as the lab can have it).</summary>
        public void SendHead(Vector3 pos, Quaternion rot)
        {
            var b = new byte[28];
            float[] v = { pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w };
            for (int i = 0; i < 7; i++) Array.Copy(BitConverter.GetBytes(v[i]), 0, b, i * 4, 4);
            Write(5, b);
        }

        public MpLabLink(bool host, int port)
        {
            _host = host;
            _port = port;
            _peer = new MpRemote { Slot = host ? 1 : 0, PlayerId = host ? 2 : 1, Name = host ? "Lab Helper" : "Lab Master" };
            _reader = new Thread(Run) { IsBackground = true, Name = "MpLabLink" };
            _reader.Start();
        }

        public bool Online { get { return _connected || _promoted; } }
        public bool IsMaster { get { return _host || _promoted; } }
        public int LocalId { get { return _host ? 1 : 2; } }
        public int[] World { get { return _world; } }
        public bool WorldValid { get { lock (_lock) return !_promoted && _world[MpNetWire.World.AtMagic] == MpNetWire.Magic; } }
        public IReadOnlyList<MpRemote> Remotes { get { return _remotes; } }
        public bool Connected { get { return _connected; } }

        private void Run()
        {
            try
            {
                if (_host)
                {
                    _listener = new TcpListener(IPAddress.Loopback, _port);
                    _listener.Start();
                    _client = _listener.AcceptTcpClient();
                }
                else
                {
                    while (!_stop)
                    {
                        try { _client = new TcpClient(); _client.Connect(IPAddress.Loopback, _port); break; }
                        catch (Exception) { Thread.Sleep(500); }
                    }
                }

                if (_stop) return;
                _client.NoDelay = true;
                _stream = _client.GetStream();
                _connected = true;
                var head = new byte[5];
                while (!_stop)
                {
                    if (!ReadAll(head, 5)) break;
                    int len = BitConverter.ToInt32(head, 1);
                    var body = new byte[len];
                    if (!ReadAll(body, len)) break;
                    lock (_lock)
                    {
                        Received++;
                        switch (head[0])
                        {
                            case 1: for (int i = 0; i < _world.Length && i * 4 < len; i++) _world[i] = BitConverter.ToInt32(body, i * 4); break;
                            case 2: for (int i = 0; i < _peerSlot.Length && i * 4 < len; i++) _peerSlot[i] = BitConverter.ToInt32(body, i * 4); _peerSeen = true; break;
                            case 3: _events.Enqueue(body); break;
                            case 4: _notes.Enqueue(System.Text.Encoding.UTF8.GetString(body)); break;
                            case 5: for (int i = 0; i < 7 && i * 4 < len; i++) _pose[i] = BitConverter.ToSingle(body, i * 4); PeerPoseSeen = true; break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!_stop) Debug.Log("LAB: link: " + e.Message);
            }

            _connected = false;
            _lost = true;
        }

        private bool ReadAll(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int k = _stream.Read(b, got, n - got);
                if (k <= 0) return false;
                got += k;
            }

            return true;
        }

        private void Write(byte kind, byte[] body)
        {
            if (!_connected || _stream == null) return;
            var frame = new byte[5 + body.Length];
            frame[0] = kind;
            Array.Copy(BitConverter.GetBytes(body.Length), 0, frame, 1, 4);
            Array.Copy(body, 0, frame, 5, body.Length);
            try { _stream.Write(frame, 0, frame.Length); Sent++; }
            catch (Exception e) { Debug.Log("LAB: link write: " + e.Message); _connected = false; _lost = true; }
        }

        private static byte[] Ints(int[] v)
        {
            var b = new byte[v.Length * 4];
            for (int i = 0; i < v.Length; i++) Array.Copy(BitConverter.GetBytes(v[i]), 0, b, i * 4, 4);
            return b;
        }

        public void Tick(int[] ownState)
        {
            if (_lost && !_host && !_promoted)
            {
                _promoted = true;
                Debug.Log("LAB: link: the master's process is gone - this client is the master now (the hand-over)");
            }

            _remotes.Clear();
            lock (_lock)
            {
                if (_connected && _peerSeen && MpNetWire.OwnerOf(_peerSlot) == _peer.PlayerId)
                {
                    Array.Copy(_peerSlot, _peer.Ints, _peer.Ints.Length);
                    _remotes.Add(_peer);
                }
            }

            if (ownState == null || !_connected || Time.unscaledTime < _nextSlot) return;
            ownState[MpNetWire.PlayerSlot.AtMagic] = MpNetWire.PlayerMagic;
            ownState[MpNetWire.PlayerSlot.AtOwner] = LocalId;
            bool changed = false;
            for (int i = 0; i < ownState.Length; i++) if (ownState[i] != _sentSlot[i]) { changed = true; _sentSlot[i] = ownState[i]; }
            if (!changed) return;
            _nextSlot = Time.unscaledTime + 0.1f;
            Write(2, Ints(ownState));
        }

        public void WriteWorld(int[] rec)
        {
            if (!IsMaster) return;
            rec[MpNetWire.World.AtMagic] = MpNetWire.Magic;
            rec[MpNetWire.World.AtProtocol] = MpNetWire.Protocol;
            rec[MpNetWire.World.AtMaster] = LocalId;
            lock (_lock) Array.Copy(rec, _world, Math.Min(rec.Length, _world.Length));
            bool changed = false;
            for (int i = 0; i < rec.Length && i < _sentWorld.Length; i++) if (rec[i] != _sentWorld[i]) { changed = true; _sentWorld[i] = rec[i]; }
            if (changed) Write(1, Ints(rec));
        }

        public void Send(byte[] msg)
        {
            if (msg == null || msg.Length == 0) return;
            if (msg.Length > MpNetWire.MaxRpc) { Debug.Log("LAB: link: a " + msg.Length + "-byte message is over the RPC size; not sent"); return; }
            lock (_lock) _events.Enqueue(msg);
            Write(3, msg);
        }

        public bool TryReceive(out byte[] msg)
        {
            lock (_lock)
            {
                if (_events.Count > 0) { msg = _events.Dequeue(); return true; }
            }

            msg = null;
            return false;
        }

        public void Note(string s) { Write(4, System.Text.Encoding.UTF8.GetBytes(s)); }

        public bool TryNote(out string s)
        {
            lock (_lock)
            {
                if (_notes.Count > 0) { s = _notes.Dequeue(); return true; }
            }

            s = null;
            return false;
        }

        public void Dispose()
        {
            _stop = true;
            try { if (_client != null) _client.Close(); } catch (Exception) { }
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
        }
    }
}
