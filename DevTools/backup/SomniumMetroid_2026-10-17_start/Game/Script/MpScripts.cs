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

        public MpScripts(MpGame game) { Game = game; }

        /// <summary>The player as the triggers see them: game-space feet and height.</summary>
        public Vector3 PlayerFeet;
        public float PlayerHeight = 2.7f, PlayerRadius = 0.5f;

        public void Add(MpEntity e)
        {
            List<MpEntity> list;
            if (!ById.TryGetValue(e.Id, out list)) ById[e.Id] = list = new List<MpEntity>(1);
            list.Add(e);
            if (!ByArea.TryGetValue(e.Area, out list)) ByArea[e.Area] = list = new List<MpEntity>();
            list.Add(e);
            All.Add(e);
            e.World = this;
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
            }

            ByArea.Remove(area);
        }

        public MpEntity Find(uint id)
        {
            List<MpEntity> list;
            return ById.TryGetValue(id, out list) && list.Count > 0 ? list[0] : null;
        }

        public void Deliver(MpEntity from, uint target, int msg, int state)
        {
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

        public void Tick(float dt)
        {
            for (int i = 0; i < All.Count; i++) All[i].Think(dt);
        }
    }

    /// <summary>A script object at run time (CEntity): active flag, links, the base messages Activate / Deactivate / ToggleActive.</summary>
    public class MpEntity
    {
        public uint Id;
        public int Area, Type;
        public string Name;
        public bool Active;
        public List<MpScriptObject.Link> Links;
        public MpScripts World;
        public MpAreaView View;

        public override string ToString() { return Type.ToString("X2") + " " + Id.ToString("X8") + " '" + Name + "'"; }

        public void Send(int state)
        {
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
        public virtual void Think(float dt) { }
        public virtual void OnLoaded() { }
        public virtual void OnRemoved() { }

        /// <summary>Gameplay: a shot or blast reached this entity (DamageableTrigger, shootable actors). Returns true if it took the hit.</summary>
        public virtual bool TakeDamage(int weapon, bool charged, float damage) { return false; }
    }
}
