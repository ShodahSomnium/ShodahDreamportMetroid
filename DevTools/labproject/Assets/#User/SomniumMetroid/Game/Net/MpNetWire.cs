using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// What travels over the world's Fusion session (the Skyrim Dreamport's SkyNetWire, cut to Metroid Prime): Community Modules
    /// objects placed in the scene by hand -
    /// - "Mp Net World" (NetworkBridgeData64), written only by the instance master: the game everyone plays in (<see cref="World"/>).
    /// - "Mp Net Player 0..15" (NetworkBridgeData64), one per player, claimed by the WoW / Skyrim slot rules (<see cref="MpSlotClaim"/>):
    ///   where that Samus stands and what she does (<see cref="PlayerSlot"/>).
    /// - "Mp Net Events" (NetworkBridgeEvents): byte[] messages for things that happen once (<see cref="Msg"/>): the script world's
    ///   messages, enemy states and deaths, shots, hits, the census a joiner asks for. Payloads stay under Fusion's 512-byte RPC limit.
    /// Game positions travel in GAME space (MpSpace.ToGame): each client slides and turns its own world (rides, the ball, cutscenes),
    /// so Unity positions differ between clients while game positions do not.
    /// </summary>
    public static class MpNetWire
    {
        public const int Magic = 0x4D505257;       // "MPRW"
        public const int PlayerMagic = 0x4D505250; // "MPRP"
        public const int Protocol = 1;
        public const string WorldObject = "Mp Net World";
        public const string EventsObject = "Mp Net Events";
        public const string PlayerPrefix = "Mp Net Player ";
        public const int MaxPlayers = 16;
        public const int MaxRpc = 480;

        /// <summary>The master's world record (int indexes).</summary>
        public static class World
        {
            public const int AtMagic = 0, AtProtocol = 1, AtMaster = 2, AtPhase = 3, AtPak = 4, AtGame = 5, AtArea = 6,
                AtX = 7, AtY = 8, AtZ = 9, AtYaw = 10, AtEscape = 11, AtEscapeTotal = 12, AtUnlocks = 13, AtPlayers = 14, AtFlags = 15,
                AtStamp = 16;
            public const int Used = 17;
            public const int PhaseBooting = 0, PhasePlaying = 2;
            public const int FlagUnlockAll = 1, FlagCinema = 2, FlagSwitching = 4;
        }

        /// <summary>A player's slot (int indexes).</summary>
        public static class PlayerSlot
        {
            public const int AtMagic = 0, AtOwner = 1, AtGeneration = 2, AtStatus = 3, AtStamp = 4, AtSeq = 5,
                AtPak = 6, AtArea = 7, AtX = 8, AtY = 9, AtZ = 10, AtYaw = 11, AtFlags = 12, AtBeam = 13, AtVisor = 14, AtEnergy = 15,
                AtSuit = 16,
                // 1.6: the arm cannon on the avatar (MpRemoteCannons) - see WriteCannon
                AtCannon = 17, AtPresses = 18, AtRelA = 19, AtRelB = 20, AtRotA = 21, AtRotB = 22,
                // 1.6: where this client's Metroid world sits in Unity (MpSpace.Origin, millimetres): its game positions as its avatar sees them
                AtOriginX = 23, AtOriginY = 24, AtOriginZ = 25;
            public const int Used = 26;
            // AtCannon bits
            public const int CannonPosed = 1, CannonOnHead = 2, CannonShown = 4, CannonHolstered = 8, CannonFireHeld = 16, CannonCanFire = 32,
                CannonForearm = 64, CannonSuitArm = 128, CannonBodyHidden = 256;   // bits 16-23: the cannon scale x 100
            public const int StatusFree = 0, StatusRoom = 1, StatusPlaying = 2;
            public const int FlagMorphed = 1, FlagDead = 2, FlagVr = 4, FlagCinema = 8, FlagMaster = 16, FlagJoined = 32, FlagCharging = 64;
        }

        /// <summary>A stable number for a pak name (FNV-1a), for the records.</summary>
        public static int PakHash(string pak)
        {
            if (string.IsNullOrEmpty(pak)) return 0;
            uint h = 2166136261u;
            foreach (char c in pak.ToLowerInvariant()) { h ^= c; h *= 16777619u; }
            return (int)(h & 0x7FFFFFFF);
        }

        /// <summary>Game units to the record's fixed point (centimetre-ish: x 100).</summary>
        public static int Fix(float v) { return (int)Math.Round(v * 100.0); }
        public static float Unfix(int v) { return v / 100f; }

        // ------------------------------------------------------------------ the cannon record (ints 17-22)

        /// <summary>
        /// The cannon as its owner holds it, relative to their avatar's right hand (VR) or head (a monitor) - every client puts it
        /// back on that avatar as IT draws it, so the avatar's own network delay cancels (the ETS / Skyrim rider record's rule): position
        /// in millimetres as S16 pairs, rotation as a unit quaternion's x, y, z (x 32767, w kept positive). Presses: fire (low 16 bits)
        /// and missile (high 16) counters - a client replays each new press on its copy of the cannon.
        /// </summary>
        public static void WriteCannon(int[] slot, int flags, float scale, int firePresses, int missilePresses, Vector3 relPos, Quaternion relRot)
        {
            slot[PlayerSlot.AtCannon] = (flags & 0xFFFF) | ((Math.Max(0, Math.Min(255, (int)Math.Round(scale * 100.0))) & 0xFF) << 16);
            slot[PlayerSlot.AtPresses] = (firePresses & 0xFFFF) | ((missilePresses & 0xFFFF) << 16);
            slot[PlayerSlot.AtRelA] = Pair(S16(relPos.x * 1000.0), S16(relPos.y * 1000.0));
            if (relRot.w < 0f) relRot = new Quaternion(-relRot.x, -relRot.y, -relRot.z, -relRot.w);
            slot[PlayerSlot.AtRelB] = Pair(S16(relPos.z * 1000.0), S16(relRot.x * 32767.0));
            slot[PlayerSlot.AtRotA] = Pair(S16(relRot.y * 32767.0), S16(relRot.z * 32767.0));
            slot[PlayerSlot.AtRotB] = 0;
        }

        public static void ReadCannon(int[] slot, out int flags, out float scale, out int firePresses, out int missilePresses, out Vector3 relPos, out Quaternion relRot)
        {
            int c = slot[PlayerSlot.AtCannon];
            flags = c & 0xFFFF;
            scale = ((c >> 16) & 0xFF) / 100f;
            if (scale <= 0f) scale = 1f;
            firePresses = slot[PlayerSlot.AtPresses] & 0xFFFF;
            missilePresses = (slot[PlayerSlot.AtPresses] >> 16) & 0xFFFF;
            relPos = new Vector3(Lo(slot[PlayerSlot.AtRelA]) / 1000f, Hi(slot[PlayerSlot.AtRelA]) / 1000f, Lo(slot[PlayerSlot.AtRelB]) / 1000f);
            float x = Hi(slot[PlayerSlot.AtRelB]) / 32767f, y = Lo(slot[PlayerSlot.AtRotA]) / 32767f, z = Hi(slot[PlayerSlot.AtRotA]) / 32767f;
            float w = (float)Math.Sqrt(Math.Max(0.0, 1.0 - x * x - y * y - z * z));
            relRot = new Quaternion(x, y, z, w);
        }

        /// <summary>This client's Metroid world origin into the slot (millimetres).</summary>
        public static void WriteOrigin(int[] slot, Vector3 origin)
        {
            slot[PlayerSlot.AtOriginX] = (int)Math.Round(origin.x * 1000.0);
            slot[PlayerSlot.AtOriginY] = (int)Math.Round(origin.y * 1000.0);
            slot[PlayerSlot.AtOriginZ] = (int)Math.Round(origin.z * 1000.0);
        }

        public static Vector3 OriginOf(int[] slot)
        {
            return new Vector3(slot[PlayerSlot.AtOriginX] / 1000f, slot[PlayerSlot.AtOriginY] / 1000f, slot[PlayerSlot.AtOriginZ] / 1000f);
        }

        // the small encodings (the ETS / Skyrim Dreamports')
        public static int S16(double value)
        {
            double r = Math.Round(value);
            int v = r < -32768.0 ? -32768 : r > 32767.0 ? 32767 : (int)r;
            return v & 0xFFFF;
        }

        public static int Pair(int lo, int hi) { return (lo & 0xFFFF) | (hi << 16); }
        public static short Lo(int v) { return (short)(v & 0xFFFF); }
        public static short Hi(int v) { return (short)((v >> 16) & 0xFFFF); }

        public static int OwnerOf(int[] slot)
        {
            return slot == null || slot.Length < PlayerSlot.Used || slot[PlayerSlot.AtMagic] != PlayerMagic ? -1 : slot[PlayerSlot.AtOwner];
        }

        public static int GenerationOf(int[] slot)
        {
            return slot == null || slot.Length < PlayerSlot.Used ? 0 : slot[PlayerSlot.AtGeneration];
        }

        public static void WriteClaim(int[] own, int localId, int generation, int stamp)
        {
            own[PlayerSlot.AtMagic] = PlayerMagic;
            own[PlayerSlot.AtOwner] = localId;
            own[PlayerSlot.AtGeneration] = generation;
            own[PlayerSlot.AtStatus] = PlayerSlot.StatusRoom;
            own[PlayerSlot.AtStamp] = stamp;
        }

        public static void WriteFree(int[] slot, int stamp)
        {
            Array.Clear(slot, 0, slot.Length);
            slot[PlayerSlot.AtMagic] = PlayerMagic;
            slot[PlayerSlot.AtOwner] = -1;
            slot[PlayerSlot.AtStamp] = stamp;
        }

        // ------------------------------------------------------------------ messages

        public enum Msg : byte
        {
            None = 0,
            Hello = 1,        // i32 player: a joiner wants the game (the master answers the snapshot to it)
            SnapBegin = 2,    // i32 to, i32 game, str pak, i16 area, 4 x f32 (x, y, z, yaw), f32 escape, f32 escape total
            SnapLayers = 3,   // i32 to, u8 n, (u16 area, u32 lo, u32 hi)...
            SnapMail = 4,     // i32 to, u8 n, u32 ids...
            SnapEnd = 5,      // i32 to
            Unlocks = 6,      // the master: u8 unlock-all, u16 missile capacity, u8 n, u16 items...
            Script = 7,       // the master: u8 n, (u32 from, u32 target, u8 message)... - the messages its script world delivered
            State = 8,        // the master: i32 to (-1 all), u8 n, (u32 net id, u8 active, i32 word)... - entity state (census and changes)
            Gen = 9,          // the master: u32 generator, u32 template, u32 net id, 3 x f32 position, f32 yaw, f32 scale, u8 kind
            EnemyState = 10,  // the master: u8 n, (u32 net id, 3 x f32 position, i16 yaw x 100, i16 anim, u8 flags, f32 health, f32 max)...
            EnemyDie = 11,    // the master: u32 net id, f32 last blow, u8 frozen
            Damage = 12,      // a helper's shot / bomb hit: i32 from, u32 net id, u8 weapon, u8 charged, f32 damage
            Hurt = 13,        // the master's enemy hit a player: i32 to, f32 damage, 3 x f32 from, u8 ignore immunity
            Fire = 14,        // a player fired: i32 from, u8 beam (4 missile), u8 charged, f32 charge, 3 x f32 muzzle, 3 x f32 direction (game)
            Bomb = 15,        // a player dropped a bomb: i32 from, 3 x f32 position
            TakeAsk = 16,     // a helper touched a pickup: i32 from, u32 net id
            Give = 17,        // the master: i32 to, i32 item, i32 capacity, i32 amount (the pickup a helper took)
            SkipAsk = 18,     // a helper wants the cutscene skipped: i32 from
            Skipped = 19,     // the master skipped the cutscene
            AreaAsk = 20,     // a helper built an area: i32 from, u16 area - the master answers its entities' state
            Platform = 21,    // the master: u8 n, (u32 net id, 3 x f32 position, u32 current waypoint, u32 target waypoint, f32 speed)...
            EnemyShot = 22,   // the master: u32 weapon, 3 x f32 from, 3 x f32 direction, f32 damage, f32 scale
            Notice = 23,      // str text (everyone shows it)
        }

        public sealed class Writer
        {
            private readonly List<byte> _b = new List<byte>(64);

            public Writer(Msg type)
            {
                _b.Add((byte)type);
            }

            public int Length { get { return _b.Count; } }
            public Writer U8(int v) { _b.Add((byte)v); return this; }
            public Writer U16(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); return this; }
            public Writer I32(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 24)); return this; }
            public Writer U32(uint v) { return I32((int)v); }
            public Writer F32(float f) { return I32(BitConverter.ToInt32(BitConverter.GetBytes(f), 0)); }

            public Writer Str(string s)
            {
                byte[] t = Encoding.UTF8.GetBytes(s ?? "");
                int n = Math.Min(t.Length, 200);
                U8(n);
                for (int i = 0; i < n; i++) _b.Add(t[i]);
                return this;
            }

            /// <summary>Overwrites one byte already written (a count filled in at the end).</summary>
            public void Patch(int at, int v) { if (at >= 0 && at < _b.Count) _b[at] = (byte)v; }

            public byte[] Bytes() { return _b.ToArray(); }
        }

        public sealed class Reader
        {
            private readonly byte[] _b;
            private int _p;

            public Reader(byte[] b)
            {
                _b = b ?? new byte[0];
                _p = 1;
            }

            public Msg Type { get { return _b.Length > 0 ? (Msg)_b[0] : Msg.None; } }
            public bool More { get { return _p < _b.Length; } }
            public int U8() { return _p < _b.Length ? _b[_p++] : 0; }
            public int U16() { int v = U8(); return v | (U8() << 8); }
            public int S16() { return (short)U16(); }

            public int I32()
            {
                if (_p + 4 > _b.Length)
                {
                    _p = _b.Length;
                    return 0;
                }

                int v = _b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24);
                _p += 4;
                return v;
            }

            public uint U32() { return (uint)I32(); }
            public float F32() { return BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0); }

            public string Str()
            {
                int n = U8();
                if (_p + n > _b.Length) n = Math.Max(0, _b.Length - _p);
                string s = Encoding.UTF8.GetString(_b, _p, n);
                _p += n;
                return s;
            }
        }
    }

    /// <summary>Which slots count (the WoW Dreamport's WowSlotRules, as the Skyrim Dreamport's SkySlotRules).</summary>
    public static class MpSlotRules
    {
        public static bool Contains(IReadOnlyList<int> present, int playerId)
        {
            if (present == null || playerId < 0) return false;
            for (int i = 0; i < present.Count; i++) if (present[i] == playerId) return true;
            return false;
        }

        public static bool FreeFor(int[] slot, int localId, IReadOnlyList<int> present)
        {
            if (slot == null || slot.Length < MpNetWire.PlayerSlot.Used || slot[MpNetWire.PlayerSlot.AtMagic] != MpNetWire.PlayerMagic) return true;
            int owner = MpNetWire.OwnerOf(slot);
            return owner < 0 || owner == localId || !Contains(present, owner);
        }

        public static bool Listed(int[] slot, int localId, IReadOnlyList<int> present)
        {
            int owner = MpNetWire.OwnerOf(slot);
            return owner >= 0 && owner != localId && Contains(present, owner);
        }

        public static bool ToFree(int[] slot, bool haveAuthority, bool claimedHere, int localId, IReadOnlyList<int> present)
        {
            if (!haveAuthority || claimedHere || slot == null || slot.Length < MpNetWire.PlayerSlot.Used || slot[MpNetWire.PlayerSlot.AtMagic] != MpNetWire.PlayerMagic) return false;
            int owner = MpNetWire.OwnerOf(slot);
            return owner >= 0 && (owner == localId || !Contains(present, owner));
        }
    }

    /// <summary>
    /// This client's claim on one player slot: the Skyrim Dreamport's SkySlotClaim (the WoW Dreamport's WowSlotClaim, from EtsSlotClaim,
    /// which the ETS probe room ran against a simulated Fusion through joins, leaves, simultaneous claims and a master hand-over),
    /// same states and timings.
    /// </summary>
    public sealed class MpSlotClaim
    {
        public const int Slots = MpNetWire.MaxPlayers;
        public const double AuthorityWait = 3.0, ConfirmAfter = 1.0, AskEvery = 1.0, LostPatience = 1.5, RoundPause = 5.0;

        public enum Stage { Idle, Asking, Confirming, Owned }

        public struct Inputs
        {
            public double Now;
            public int LocalId;
            public bool Allowed;
            public int SlotCount;
        }

        public Stage Current { get; private set; }
        public int Slot { get; private set; } = -1;
        public int Generation { get; private set; }
        public int AskAuthority { get; private set; } = -1;
        public bool WriteClaim { get; private set; }
        public bool Holding { get { return Slot >= 0 && (Current == Stage.Confirming || Current == Stage.Owned); } }

        private readonly bool[] _tried = new bool[Slots];
        private double _since, _nextAsk, _lostAt = -1.0, _roundAt = double.NegativeInfinity;
        private int _localId = -1;
        private string _note;

        public string TakeNote()
        {
            string n = _note;
            _note = null;
            return n;
        }

        public void Reset()
        {
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            Array.Clear(_tried, 0, _tried.Length);
            _roundAt = double.NegativeInfinity;
        }

        public void Step(Inputs input, int[][] slots, bool[] valid, bool[] authority, IReadOnlyList<int> present)
        {
            AskAuthority = -1;
            WriteClaim = false;
            if (!input.Allowed || input.LocalId < 0 || input.LocalId != _localId)
            {
                if (Current != Stage.Idle || Slot >= 0) _note = "claim dropped (" + (!input.Allowed ? "the room is not up" : "a new local player id") + ")";
                Reset();
                _localId = input.LocalId;
                if (!input.Allowed || input.LocalId < 0) return;
            }

            double now = input.Now;
            int count = Math.Min(Math.Min(input.SlotCount, Slots), slots.Length);
            switch (Current)
            {
                case Stage.Idle:
                {
                    if (now < _roundAt) return;
                    int k = FindFree(input.LocalId, count, slots, valid, present);
                    if (k < 0)
                    {
                        Array.Clear(_tried, 0, _tried.Length);
                        _roundAt = now + RoundPause;
                        _note = "no free player slot (" + count + " slots)";
                        return;
                    }

                    Slot = k;
                    Current = Stage.Asking;
                    _since = now;
                    _nextAsk = now + AskEvery;
                    AskAuthority = k;
                    return;
                }

                case Stage.Asking:
                {
                    if (!valid[Slot] || !MpSlotRules.FreeFor(slots[Slot], input.LocalId, present))
                    {
                        Give("slot " + Slot + " taken before the claim; trying the next");
                        return;
                    }

                    if (authority[Slot])
                    {
                        Generation = (MpNetWire.GenerationOf(slots[Slot]) + 1) & 0xFF;
                        WriteClaim = true;
                        Current = Stage.Confirming;
                        _since = now;
                        return;
                    }

                    if (now - _since >= AuthorityWait)
                    {
                        Give("no authority over slot " + Slot + " in " + AuthorityWait + " s; trying the next");
                        return;
                    }

                    if (now >= _nextAsk)
                    {
                        AskAuthority = Slot;
                        _nextAsk = now + AskEvery;
                    }

                    return;
                }

                case Stage.Confirming:
                {
                    int owner = MpNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && MpSlotRules.Contains(present, owner))
                    {
                        Give("slot " + Slot + " lost to player " + owner);
                        return;
                    }

                    if (!authority[Slot])
                    {
                        if (owner == input.LocalId)
                        {
                            if (_lostAt < 0.0)
                            {
                                _lostAt = now;
                                _nextAsk = now + LostPatience;
                            }
                            else if (now >= _nextAsk)
                            {
                                AskAuthority = Slot;
                                _nextAsk = now + AskEvery;
                            }

                            return;
                        }

                        if (now - _since >= AuthorityWait) Give("authority over slot " + Slot + " went elsewhere; trying the next");
                        return;
                    }

                    if (_lostAt >= 0.0)
                    {
                        _lostAt = -1.0;
                        _since = now;
                        return;
                    }

                    if (owner != input.LocalId)
                    {
                        if (now - _since >= ConfirmAfter)
                        {
                            WriteClaim = true;
                            _since = now;
                        }

                        return;
                    }

                    if (now - _since >= ConfirmAfter)
                    {
                        Current = Stage.Owned;
                        _lostAt = -1.0;
                        Array.Clear(_tried, 0, _tried.Length);
                        _note = "slot " + Slot + " claimed (generation " + Generation + ")";
                    }

                    return;
                }

                case Stage.Owned:
                {
                    int owner = MpNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && MpSlotRules.Contains(present, owner))
                    {
                        int lost = Slot;
                        Reset();
                        _note = "slot " + lost + " was taken over by player " + owner + "; claiming again";
                        return;
                    }

                    if (!authority[Slot])
                    {
                        if (_lostAt < 0.0)
                        {
                            _lostAt = now;
                            _nextAsk = now + LostPatience;
                        }
                        else if (now >= _nextAsk)
                        {
                            AskAuthority = Slot;
                            _nextAsk = now + AskEvery;
                        }

                        return;
                    }

                    _lostAt = -1.0;
                    if (owner != input.LocalId)
                    {
                        Generation = (Generation + 1) & 0xFF;
                        WriteClaim = true;
                        _note = "slot " + Slot + " read as someone else's while held; claimed again";
                    }

                    return;
                }
            }
        }

        private void Give(string why)
        {
            if (Slot >= 0 && Slot < _tried.Length) _tried[Slot] = true;
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            _note = why;
        }

        private int FindFree(int localId, int count, int[][] slots, bool[] valid, IReadOnlyList<int> present)
        {
            if (count <= 0) return -1;
            int k0 = localId % count;
            for (int i = 0; i < count; i++)
            {
                int k = (k0 + i) % count;
                if (valid[k] && !_tried[k] && MpSlotRules.FreeFor(slots[k], localId, present)) return k;
            }

            return -1;
        }
    }
}
