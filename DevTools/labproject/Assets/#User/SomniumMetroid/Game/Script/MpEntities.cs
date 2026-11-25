using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>An entity with a model in the world (CActor): the model's GameObject, shown while active.</summary>
    public class MpActorEntity : MpEntity
    {
        public GameObject Go;
        /// <summary>CModelData's x-ray model: shown instead of <see cref="Go"/> while the X-Ray visor is on.</summary>
        public GameObject XrayGo;
        /// <summary>The visor every actor draws for (EPlayerVisor; MpGame.SetVisor sets it and calls ApplyVisor).</summary>
        public static int Visor;
        public static int LabXraySwaps;
        public Collider Solid;
        public float Health;
        public MpVulnerability Vuln;
        /// <summary>A CScriptActor or CScriptPlatform (not a door / enemy): Increment and Decrement fade it in and out. The Reactor Core's
        /// "Platform - Door Block" barrier drops this way after the Parasite Queen dies.</summary>
        public bool ScriptActor;

        public override void OnMessage(int msg, MpEntity from)
        {
            // CScriptActor::AcceptScriptMsg: Increment = Activate (if inactive) with a fade in; Decrement = a fade out that
            // deactivates it at its end (CScriptColorModulate::FadeOutHelper). The frigate's freight clamp drops its orange energy
            // field this way - ignored, the field stayed up and kept blocking the doorway.
            if (ScriptActor)
            {
                if (msg == MpScriptMsg.Increment) { if (!Active) OnMessage(MpScriptMsg.Activate, from); return; }
                if (msg == MpScriptMsg.Decrement) { if (Active) OnMessage(MpScriptMsg.Deactivate, from); return; }
            }

            base.OnMessage(msg, from);
        }

        public override void SetActive(bool on)
        {
            base.SetActive(on);
            bool xr = XrayGo != null && Visor == 1;
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
            if (Go != null && XrayGo != null) foreach (Renderer r in Go.GetComponentsInChildren<Renderer>(true)) r.enabled = !xr;
            if (XrayGo != null && XrayGo.activeSelf != (on && xr)) XrayGo.SetActive(on && xr);
            if (Solid != null) Solid.enabled = on;
        }

        public override void OnRemoved()
        {
            if (Go != null) Object.Destroy(Go);
            if (XrayGo != null) Object.Destroy(XrayGo);
            if (Solid != null) Object.Destroy(Solid.gameObject);
        }

        /// <summary>The visor changed: an actor with an x-ray model swaps it in (X-Ray) or back (CModelData::GetRenderingModel).</summary>
        public void ApplyVisor()
        {
            if (XrayGo == null) return;
            SetActive(Active);
            LabXraySwaps++;
        }
    }

    /// <summary>
    /// CScriptDoor. A doorway shared by two areas has a door in each; the door that opens first (by a trigger or a script) opens
    /// the other one (Partner2), and the other one remembers it as its opener (Partner1). Open (unless inactive) -> OpenDoor: sends
    /// Open, or MaxReached when a partner opened it. Close is taken only from nobody's-opened doors or from the opener: it closes
    /// the door it opened too, so a partner's own close timer cannot shut the doorway. Action closes the pair. Closing sends Closed.
    /// No door animation yet: an open door is not drawn and does not block.
    /// </summary>
    public sealed class MpDoorEntity : MpActorEntity
    {
        public bool IsOpen;
        /// <summary>CScriptDoor::GetProjectileBounds: shots hit the door only while this is set (SetToZero sets it, SetToMax and closing clear it); otherwise they pass through to its lock (a DamageableTrigger) and beyond.</summary>
        public bool ProjectilesCollide;
        public Vector3 GamePos;
        public MpDoorEntity Partner1, Partner2;     // mPartner1: the door that opened this one; mPartner2: the door this one opened

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Open:
                    if (!Active || IsOpen) return;
                    OpenDoor(from as MpDoorEntity);
                    return;
                case MpScriptMsg.Close:
                    if (!Active) return;
                    if ((Partner1 == null || Partner1 == from) && IsOpen)
                    {
                        if (Partner2 != null) Partner2.OnMessage(MpScriptMsg.Close, this);
                        CloseDoor();
                    }

                    return;
                case MpScriptMsg.Action:
                    if (Partner1 != null)
                    {
                        if (Partner1.IsOpen) Partner1.OnMessage(MpScriptMsg.Close, this);
                    }
                    else if (IsOpen)
                    {
                        if (Partner2 != null) Partner2.OnMessage(MpScriptMsg.Close, this);
                        CloseDoor();
                    }

                    return;
                case MpScriptMsg.SetToZero:
                    ProjectilesCollide = true;
                    return;
                case MpScriptMsg.SetToMax:
                    ProjectilesCollide = false;
                    return;
            }

            base.OnMessage(msg, from);
        }

        /// <summary>CScriptDoor::OpenDoor (the other area is always "ready": its areas stream in around the player).</summary>
        private void OpenDoor(MpDoorEntity opener)
        {
            IsOpen = true;
            Partner1 = opener;
            Partner2 = null;
            Show();
            Send(Partner1 != null ? MpScriptMsg.StMaxReached : MpScriptMsg.StOpen);

            // the other area's door at this doorway (the game pairs them through their docks)
            foreach (MpEntity e in World.All)
            {
                var d = e as MpDoorEntity;
                if (d == null || d == this || d == opener || d.Area == Area) continue;
                if ((d.GamePos - GamePos).sqrMagnitude >= 4f) continue;
                Partner2 = d;
                d.OnMessage(MpScriptMsg.Open, this);
                break;
            }

            // else the first door this one sends Open to
            if (Partner1 == null && Partner2 == null && Links != null)
                foreach (var l in Links)
                {
                    if (l.Message != MpScriptMsg.Open) continue;
                    var d = World.Find(l.Target) as MpDoorEntity;
                    if (d != null) { Partner2 = d; break; }
                }

            MpGame g = MpGame.Instance;
            if (g != null && Partner1 == null) g.OnDoor(this, true);
        }

        private void CloseDoor()
        {
            IsOpen = false;
            Show();
            ProjectilesCollide = false;
            Send(MpScriptMsg.StClosed);
            MpGame g = MpGame.Instance;
            if (g != null && Partner1 == null) g.OnDoor(this, false);
        }

        /// <summary>The door's own animations (CScriptDoor::SetDoorAnimation: 0 open, 1 close, 2 ready), when its model is a character.</summary>
        public MpAnimator Anim;
        private int _shownOpen = -1;

        private void Show()
        {
            if (Anim != null)
            {
                if (Go != null) Go.SetActive(Active);
                int want = IsOpen ? 1 : 0;
                if (want != _shownOpen)
                {
                    bool first = _shownOpen < 0;
                    _shownOpen = want;
                    Anim.Play(IsOpen ? 0 : (first ? 2 : 1), false, 0.05f);
                    if (first && IsOpen) Anim.Time = Anim.Duration;
                    Anim.Tick(0f);
                }
            }
            else if (Go != null) Go.SetActive(Active && !IsOpen);
            if (Solid != null) Solid.enabled = Active && !IsOpen;
        }

        public override int NetWord { get { return IsOpen ? 1 : 0; } }

        public override void ApplyNetWord(int word)
        {
            bool open = (word & 1) != 0;
            if (open == IsOpen) return;
            IsOpen = open;
            if (!open) ProjectilesCollide = false;
            Show();
        }

        public override void SetActive(bool on)
        {
            Active = on;
            Show();
        }

        public override void Think(float dt)
        {
            if (Anim != null && Active) Anim.Tick(dt);
        }
    }

    /// <summary>CScriptPlatform: a model with its own collision (DCLN, else its box) that moves with it.</summary>
    public class MpPlatformEntity : MpActorEntity
    {
        public bool Dead;

        /// <summary>
        /// CScriptPlatform health: a shot hurts it through its vulnerability (Damage to the links; at no health Dead) - the frigate's
        /// Deck Alpha Umbilical Hall blockage is such a platform, deflecting normal shots and broken by a charged one.
        /// </summary>
        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            if (!Active || Dead || Vuln == null) return false;
            float d = Vuln.Damage(weapon, charged, damage);
            if (d <= 0f)
            {
                MpGame g = MpGame.Instance;
                if (g != null) g.OnWeaponIneffective(this);
                return true;
            }

            Health -= d;
            Send(MpScriptMsg.StDamage);
            if (Health <= 0f) { Dead = true; Send(MpScriptMsg.StDead); }
            return true;
        }

        public override int NetWord { get { return Dead ? 1 : 0; } }

        public override void ApplyNetWord(int word) { Dead = (word & 1) != 0; }
    }

    /// <summary>
    /// CScriptDamageableTrigger: a box that takes damage by the game's vulnerability table; at no health it sends Dead and
    /// deactivates (the shootable part of every coloured door). Increment / Decrement = invulnerable on / off.
    /// </summary>
    public sealed class MpDamageableTriggerEntity : MpEntity
    {
        public Vector3 Min, Max;      // game space
        public float MaxHealth, HealthNow;
        public MpVulnerability Vuln;
        public bool Invulnerable;
        public BoxCollider Box;

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Activate:
                    HealthNow = MaxHealth;
                    break;
                case MpScriptMsg.Increment: Invulnerable = true; break;
                case MpScriptMsg.Decrement: Invulnerable = false; break;
            }

            base.OnMessage(msg, from);
            if (Box != null) Box.enabled = Active;
        }

        public override void SetActive(bool on)
        {
            base.SetActive(on);
            if (Box != null) Box.enabled = on;
        }

        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            if (!Active || Vuln == null) return false;
            float d = Vuln.Damage(weapon, charged, damage);
            if (d <= 0f)
            {
                MpGame g = MpGame.Instance;
                if (g != null) g.OnWeaponIneffective(this);
                return true;
            }

            if (Invulnerable) return true;
            HealthNow -= d;
            Send(MpScriptMsg.StDamage);
            if (HealthNow <= 0f)
            {
                SetActive(false);
                Send(MpScriptMsg.StDead);
            }

            return true;
        }

        public override void OnRemoved()
        {
            if (Box != null) Object.Destroy(Box.gameObject);
        }
    }

    /// <summary>CScriptTrigger: the player entering / inside / leaving the box (Entered, Inside every frame, Exited).</summary>
    public sealed class MpTriggerEntity : MpEntity
    {
        public Vector3 Min, Max;
        public uint Flags;
        public bool DeactivateOnEnter, DeactivateOnExit, Inside;
        /// <summary>CScriptTrigger damage: per second to Samus while she is inside (fire, steam, electricity), applied every half second.</summary>
        public float Damage;
        private float _hurt, _hurtT;

        public const uint DetectPlayer = 1, DetectMorphed = 1 << 12, DetectUnmorphed = 1 << 16;

        public override void Think(float dt)
        {
            if (!Active)
            {
                if (Inside) Inside = false;
                return;
            }

            // CScriptTrigger::Touch: the player counts as "player" plus "morphed" or "unmorphed" by her state. Co-op: the master's
            // triggers count every Samus (World.Others); a helper's only work out its own Samus's damage (the master's messages do the rest).
            bool mine = Touches(World.PlayerFeet, World.PlayerMorphed);
            if (MpScripts.Follower)
            {
                Inside = mine;
                Hurt(mine, dt);
                return;
            }

            bool inside = mine;
            for (int i = 0; i < World.Others.Count && !inside; i++) inside = Touches(World.Others[i].Key, World.Others[i].Value);
            if (!inside && (Flags & (DetectPlayer | DetectMorphed | DetectUnmorphed)) == 0)
            {
                if (Inside) { Inside = false; Send(MpScriptMsg.StExited); }
                return;
            }
            if (inside && !Inside)
            {
                Inside = true;
                Send(MpScriptMsg.StEntered);
                if (DeactivateOnEnter) { SetActive(false); return; }
            }
            else if (!inside && Inside)
            {
                Inside = false;
                Send(MpScriptMsg.StExited);
                if (DeactivateOnExit) { SetActive(false); return; }
            }

            if (Inside) Send(MpScriptMsg.StInside);
            Hurt(mine, dt);
        }

        /// <summary>A Samus (feet, morphed) in the box, by the trigger's detect flags.</summary>
        private bool Touches(Vector3 f, bool morphed)
        {
            uint test = DetectPlayer | (morphed ? DetectMorphed : DetectUnmorphed);
            if ((Flags & test) == 0) return false;
            float r = World.PlayerRadius;
            return f.x + r >= Min.x && f.x - r <= Max.x && f.y + r >= Min.y && f.y - r <= Max.y && f.z + World.PlayerHeight >= Min.z && f.z <= Max.z;
        }

        /// <summary>The trigger's damage to this client's own Samus while she is inside.</summary>
        private void Hurt(bool mine, float dt)
        {
            if (!mine || Damage <= 0f) { _hurt = 0f; _hurtT = 0f; return; }
            _hurt += Damage * dt;
            _hurtT += dt;
            if (_hurtT >= 0.5f && MpGame.Instance != null) { MpGame.Instance.DamagePlayer(_hurt, (Min + Max) * 0.5f, true); _hurt = 0f; _hurtT = 0f; }
        }
    }

    /// <summary>CScriptTimer: counts down while timing; at zero sends Zero, loops if set.</summary>
    public sealed class MpTimerEntity : MpEntity
    {
        public float Start, MaxRandom, TimeLeft;
        public bool Loop, AutoStart, Timing;

        public override int NetWord { get { return Timing ? 1 : 0; } }

        public override void ApplyNetWord(int word) { Timing = (word & 1) != 0; }

        private void Reset() { TimeLeft = Start + MaxRandom * Random.value; }

        public override void OnLoaded()
        {
            Reset();
            Timing = AutoStart;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            if (Active)
            {
                switch (msg)
                {
                    case MpScriptMsg.Start: Timing = true; break;
                    case MpScriptMsg.Stop: Timing = false; break;
                    case MpScriptMsg.Reset: Reset(); if (AutoStart) Timing = true; break;
                    case MpScriptMsg.StopAndReset: Reset(); Timing = false; break;
                    case MpScriptMsg.ResetAndStart: Reset(); Timing = true; break;
                }
            }

            base.OnMessage(msg, from);
        }

        public override void Think(float dt)
        {
            if (!Active || !Timing || TimeLeft <= 0f) return;
            TimeLeft -= dt;
            if (TimeLeft > 0f) return;
            Send(MpScriptMsg.StZero);
            Timing = false;
            if (!Loop) return;
            Reset();
            if (AutoStart) Timing = true;
        }
    }

    /// <summary>CScriptCounter: Increment / Decrement / SetToZero / SetToMax / Reset; MaxReached at max, Zero at zero.</summary>
    public sealed class MpCounterEntity : MpEntity
    {
        public int Initial, Max, Current;
        public bool AutoReset;

        public override int NetWord { get { return Current; } }

        public override void ApplyNetWord(int word) { Current = word; }

        public override void OnMessage(int msg, MpEntity from)
        {
            if (Active)
            {
                switch (msg)
                {
                    case MpScriptMsg.SetToZero:
                        Current = 0; Send(MpScriptMsg.StZero); if (AutoReset) Current = Initial;
                        break;
                    case MpScriptMsg.SetToMax:
                        Current = Max; Send(MpScriptMsg.StMaxReached); if (AutoReset) Current = Initial;
                        break;
                    case MpScriptMsg.Decrement:
                        if (Current != 0)
                        {
                            Current--;
                            if (Current == 0) { Send(MpScriptMsg.StZero); if (AutoReset) Current = Initial; }
                        }

                        break;
                    case MpScriptMsg.Increment:
                        if (Current < Max)
                        {
                            Current++;
                            if (Current == Max) { Send(MpScriptMsg.StMaxReached); if (AutoReset) Current = Initial; }
                        }

                        break;
                    case MpScriptMsg.Reset: Current = Initial; break;
                }
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>CScriptRelay (type 0x15): SetToZero while active -> Zero on the next think (one per message received).</summary>
    public sealed class MpRelayEntity : MpEntity
    {
        private int _pending;

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if (msg == MpScriptMsg.SetToZero && Active) _pending++;
        }

        public override void Think(float dt)
        {
            while (_pending > 0)
            {
                _pending--;
                Send(MpScriptMsg.StZero);
            }
        }
    }

    /// <summary>CScriptMemoryRelay: Activate is remembered (the mailbox, kept across area loads) and sends Active; Deactivate forgets.</summary>
    public sealed class MpMemoryRelayEntity : MpEntity
    {
        public bool SkipSendActive, IgnoreMessages;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (IgnoreMessages) return;
            switch (msg)
            {
                case MpScriptMsg.Activate:
                    World.Mailbox.Add(Id);
                    if (!SkipSendActive) Send(MpScriptMsg.StActive);
                    return;
                case MpScriptMsg.Deactivate:
                    World.Mailbox.Remove(Id);
                    return;
            }

            base.OnMessage(msg, from);
        }

        public override void OnLoaded()
        {
            // CScriptMailbox: a relay activated on an earlier visit replays its Active state when its area loads again
            if (World.Mailbox.Contains(Id)) Send(MpScriptMsg.StActive);
        }
    }

    /// <summary>CScriptHUDMemo: SetToZero while active shows its text (the HUD's message line).</summary>
    public sealed class MpHudMemoEntity : MpEntity
    {
        public uint Strg;
        public float DisplayTime;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.SetToZero && Active)
            {
                MpGame g = MpGame.Instance;
                if (g != null) g.ShowHudMemo(Strg, DisplayTime);
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>CScriptActorRotate: Action -> its Play-linked actors turn by the rotation over the time, then it sends Play.</summary>
    public sealed class MpActorRotateEntity : MpEntity
    {
        public Vector3 Rotation;
        public float MaxTime, Current;
        public bool UpdateActors, Running;
        private readonly List<KeyValuePair<MpActorEntity, Quaternion>> _actors = new List<KeyValuePair<MpActorEntity, Quaternion>>();

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if ((msg == MpScriptMsg.Action || msg == MpScriptMsg.Next) && Active && !Running) Begin(msg == MpScriptMsg.Next);
        }

        private void Begin(bool skip)
        {
            _actors.Clear();
            if (Links != null)
                foreach (MpScriptObject.Link l in Links)
                {
                    if (l.State != MpScriptMsg.StPlay || l.Message != MpScriptMsg.Play) continue;
                    List<MpEntity> list;
                    if (!World.ById.TryGetValue(l.Target, out list)) continue;
                    foreach (MpEntity e in list)
                    {
                        var a = e as MpActorEntity;
                        if (a != null && a.Go != null) _actors.Add(new KeyValuePair<MpActorEntity, Quaternion>(a, a.Go.transform.localRotation));
                    }
                }

            Send(MpScriptMsg.StPlay);
            if (_actors.Count == 0) return;
            Running = true;
            Current = skip ? MaxTime : 0f;
        }

        public override void Think(float dt)
        {
            if (!Running || !Active) return;
            Current += dt;
            if (Current >= MaxTime) { Current = MaxTime; Running = false; }
            float t = MaxTime > 0f ? Current / MaxTime : 1f;
            Quaternion xf = Quaternion.AngleAxis(t * Rotation.x, Vector3.right) * Quaternion.AngleAxis(t * Rotation.y, Vector3.up) * Quaternion.AngleAxis(t * Rotation.z, Vector3.forward);
            foreach (var kv in _actors)
                if (kv.Key.Go != null) kv.Key.Go.transform.localRotation = kv.Value * xf;
        }
    }

    /// <summary>A point the scan visor can scan (CScriptPointOfInterest, and actors with a scan): its SCAN asset and where it is.</summary>
    public sealed class MpScanPointEntity : MpEntity
    {
        public uint Scan;
        public Vector3 GamePos;
        public bool Scanned;
        public Transform Follow;      // an actor's scan follows the actor
    }

    /// <summary>CScriptSpawnPoint (kept for the player's respawn and the debug start).</summary>
    public sealed class MpSpawnEntity : MpEntity
    {
        public Vector3 GamePos;
        public float Yaw;
        public bool Default;

        /// <summary>CScriptSpawnPoint SetToZero (Reset too): Samus is put here (the scripts move her after cutscenes and lifts), then Zero.</summary>
        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if ((msg == MpScriptMsg.SetToZero || msg == MpScriptMsg.Reset) && Active)
            {
                MpGame g = MpGame.Instance;
                if (g != null) g.TeleportPlayer(GamePos, Yaw, Name);
                Send(MpScriptMsg.StZero);
            }
        }
    }

    /// <summary>CScriptStreamedMusic: Play while active starts the track (the disc's Audio/ .dsp pair), Stop / Deactivate stop it.</summary>
    public sealed class MpMusicEntity : MpEntity
    {
        public string File;
        public float FadeIn, FadeOut, Volume;
        public bool Loop, Music;

        public override void OnMessage(int msg, MpEntity from)
        {
            MpGame g = MpGame.Instance;
            if (g != null)
            {
                if (msg == MpScriptMsg.Play && Active) g.PlayMusic(File, FadeIn, Volume, Loop);
                else if ((msg == MpScriptMsg.Stop && Active) || msg == MpScriptMsg.Deactivate) g.StopMusic(File, FadeOut);
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>CScriptSound: Play while active plays the sound effect (by its SFX id) at its place; Stop stops a looping one.</summary>
    public sealed class MpSoundEntity : MpEntity
    {
        public int SoundId;
        public Vector3 GamePos;
        public bool Looped, NonEmitter, AutoStart;
        public float Volume, MaxDist;
        public object Playing;

        public override void OnLoaded()
        {
            if (AutoStart && Active) Play();
        }

        private void Play()
        {
            MpGame g = MpGame.Instance;
            if (g != null) Playing = g.PlayDataSfxAt(SoundId, NonEmitter ? (Vector3?)null : GamePos, Volume, Looped, MaxDist);
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Play && Active) Play();
            else if (msg == MpScriptMsg.Stop || msg == MpScriptMsg.Deactivate) { MpGame g = MpGame.Instance; if (g != null && Playing != null) g.StopSfx(Playing); Playing = null; }
            base.OnMessage(msg, from);
        }

        public override void OnRemoved()
        {
            MpGame g = MpGame.Instance;
            if (g != null && Playing != null) g.StopSfx(Playing);
        }
    }
}
