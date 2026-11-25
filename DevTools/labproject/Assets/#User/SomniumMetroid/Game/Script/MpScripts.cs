using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The game's script entities of the loaded areas (decomp CStateManager + CEntity): every object keeps its links (state ->
    /// message -> target editor id); <see cref="MpEntity.Send"/> delivers a state's messages at once (CStateManager::SendScriptMsg),
    /// relays defer to the next think as the game's do. Entities exist while their area is loaded; messages to unloaded targets are
    /// dropped (memory relays keep their "mailbox" here across loads, CScriptMailbox).
    /// </summary>
    public sealed class MpScripts
    {
        public readonly Dictionary<uint, List<MpEntity>> ById = new Dictionary<uint, List<MpEntity>>();
        public readonly Dictionary<int, List<MpEntity>> ByArea = new Dictionary<int, List<MpEntity>>();
        public readonly List<MpEntity> All = new List<MpEntity>();
        public readonly HashSet<uint> Mailbox = new HashSet<uint>();
        public readonly MpGame Game;
        private int _depth;
        public static bool Trace;

        /// <summary>
        /// Co-op: this client is a helper in the master's game (MpCoop). Its script world only mirrors the master's: no message of
        /// its own is delivered (links, timers, triggers, relays all stay quiet); the master's deliveries arrive through
        /// <see cref="DeliverRemote"/> and the entities react to them as the master's did (doors open, cameras roll, sounds play).
        /// </summary>
        public static bool Follower;

        /// <summary>Co-op, the master: every message its script world delivers (from, target id, message, the sender's state), for the helpers.</summary>
        public static System.Action<MpEntity, uint, int, int> Delivered;

        /// <summary>Entities by their co-op id (the editor id; a generated copy gets its own from the master).</summary>
        public readonly Dictionary<uint, MpEntity> ByNet = new Dictionary<uint, MpEntity>();

        /// <summary>The other players as the master's triggers see them (game feet, morphed), refreshed each frame by MpCoop.</summary>
        public readonly List<KeyValuePair<Vector3, bool>> Others = new List<KeyValuePair<Vector3, bool>>();

        public MpScripts(MpGame game) { Game = game; }

        /// <summary>The player as the triggers see them: game-space feet and height.</summary>
        public Vector3 PlayerFeet;
        public float PlayerHeight = 2.7f, PlayerRadius = 0.5f;
        public bool PlayerMorphed;

        public void Add(MpEntity e)
        {
            List<MpEntity> list;
            if (!ById.TryGetValue(e.Id, out list)) ById[e.Id] = list = new List<MpEntity>(1);
            list.Add(e);
            if (!ByArea.TryGetValue(e.Area, out list)) ByArea[e.Area] = list = new List<MpEntity>();
            list.Add(e);
            All.Add(e);
            e.World = this;
            if (e.NetId == 0) e.NetId = e.Id;
            e.NetInitial = (e.Active ? 1 : 0) | (e.NetWord << 1);
            if (!ByNet.ContainsKey(e.NetId)) ByNet[e.NetId] = e;
        }

        /// <summary>Co-op: a generated copy's own id (the master hands them out; the helpers make the same copy under it).</summary>
        public void SetNetId(MpEntity e, uint netId)
        {
            MpEntity was;
            if (ByNet.TryGetValue(e.NetId, out was) && was == e) ByNet.Remove(e.NetId);
            e.NetId = netId;
            ByNet[netId] = e;
        }

        public MpEntity FindNet(uint netId)
        {
            MpEntity e;
            return ByNet.TryGetValue(netId, out e) ? e : null;
        }

        /// <summary>After an area's entities are made: the game's initialisation messages (InitializedInArea; timers' auto start).</summary>
        public void AreaLoaded(int area)
        {
            List<MpEntity> list;
            if (!ByArea.TryGetValue(area, out list)) return;
            foreach (MpEntity e in list) e.OnLoaded();
        }

        public void RemoveArea(int area)
        {
            List<MpEntity> list;
            if (!ByArea.TryGetValue(area, out list)) return;
            foreach (MpEntity e in list)
            {
                e.OnRemoved();
                List<MpEntity> same;
                if (ById.TryGetValue(e.Id, out same))
                {
                    same.Remove(e);
                    if (same.Count == 0) ById.Remove(e.Id);
                }

                All.Remove(e);
                MpEntity was;
                if (ByNet.TryGetValue(e.NetId, out was) && was == e) ByNet.Remove(e.NetId);
            }

            ByArea.Remove(area);
            // an entity of another area under the same id takes the slot again
            foreach (MpEntity e in All) if (!ByNet.ContainsKey(e.NetId)) ByNet[e.NetId] = e;
        }

        public MpEntity Find(uint id)
        {
            List<MpEntity> list;
            return ById.TryGetValue(id, out list) && list.Count > 0 ? list[0] : null;
        }

        public void Deliver(MpEntity from, uint target, int msg, int state)
        {
            if (Follower) return;
            if (Delivered != null && _depth <= 48) Delivered(from, target, msg, state);
            List<MpEntity> list;
            if (!ById.TryGetValue(target, out list)) return;
            if (_depth > 48)
            {
                MpLog.Warn("Scripts: message loop cut at " + from + " -> " + target.ToString("X8") + " msg " + msg);
                return;
            }

            _depth++;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (Trace) MpLog.Info("Scripts: " + from + " [" + state + "] -> " + list[i] + " msg " + msg);
                    list[i].OnMessage(msg, from);
                }
            }
            finally { _depth--; }
        }

        /// <summary>Co-op, a helper: one of the master's deliveries (its own messages stay quiet; the master's follow-ups arrive next).</summary>
        public void DeliverRemote(uint fromId, uint target, int msg)
        {
            List<MpEntity> list;
            if (!ById.TryGetValue(target, out list)) return;
            MpEntity from = fromId != 0 ? FindNet(fromId) ?? Find(fromId) : null;
            MpEntity[] copy = list.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                if (Trace) MpLog.Info("Scripts (master): " + from + " -> " + copy[i] + " msg " + msg);
                copy[i].OnMessage(msg, from);
            }
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < All.Count; i++) All[i].Think(dt);
        }
    }

    /// <summary>A script object at run time (CEntity): active flag, links, the base messages Activate / Deactivate / ToggleActive.</summary>
    public class MpEntity
    {
        public uint Id;
        /// <summary>Co-op: the id the master and its helpers know this entity by (MpScripts.ByNet).</summary>
        public uint NetId;
        /// <summary>Co-op: the state as the disc made it (active, NetWord) - a room's census only carries what differs.</summary>
        public int NetInitial;
        public int Area, Type;
        public string Name;
        public bool Active;
        public List<MpScriptObject.Link> Links;
        public MpScripts World;
        public MpAreaView View;
        public MpScriptObject Source;      // the script object it was made from (generators copy it)
        public bool Generated;             // a generator's copy (CStateManager::GenerateObject)

        public override string ToString() { return Type.ToString("X2") + " " + Id.ToString("X8") + " '" + Name + "'"; }

        /// <summary>Lab: sees every state this entity sends.</summary>
        public System.Action<int> StateHook;

        public void Send(int state)
        {
            if (StateHook != null) StateHook(state);
            if (Links == null) return;
            for (int i = 0; i < Links.Count; i++)
                if (Links[i].State == state) World.Deliver(this, Links[i].Target, Links[i].Message, state);
        }

        public virtual void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Activate:
                    if (!Active) { SetActive(true); Send(MpScriptMsg.StActive); }
                    break;
                case MpScriptMsg.Deactivate:
                    if (Active) { SetActive(false); Send(MpScriptMsg.StInactive); }
                    break;
                case MpScriptMsg.ToggleActive:
                    OnMessage(Active ? MpScriptMsg.Deactivate : MpScriptMsg.Activate, from);
                    break;
            }
        }

        public virtual void SetActive(bool on) { Active = on; }

        /// <summary>Co-op: the one number of this entity's state the master keeps its helpers in step with (a door open, a pickup taken,
        /// a counter's count), besides its active flag. 0 for most.</summary>
        public virtual int NetWord { get { return 0; } }

        /// <summary>Co-op, a helper: the master's <see cref="NetWord"/> (no messages are sent).</summary>
        public virtual void ApplyNetWord(int word) { }
        public virtual void Think(float dt) { }
        public virtual void OnLoaded() { }
        public virtual void OnRemoved() { }

        /// <summary>Gameplay: a shot or blast reached this entity (DamageableTrigger, shootable actors). Returns true if it took the hit.</summary>
        public virtual bool TakeDamage(int weapon, bool charged, float damage) { return false; }
    }
}
