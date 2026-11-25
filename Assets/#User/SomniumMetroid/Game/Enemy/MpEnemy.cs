using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The base of every enemy (decomp CPatterned, simplified to what the Dreamport's enemies need): the patterned info (speed,
    /// detection, attack ranges, contact damage, health, vulnerability, death sound and explosion), an animated model
    /// (<see cref="MpAnimator"/> on the ANCS character, its sound and effect events played), a body in game space under its area,
    /// a shot box that follows the body (shots hit it through <see cref="MpHitTarget"/>; pass-through vulnerabilities let shots
    /// by), contact damage to Samus every damage-wait time while she touches the body, and the script states the game sends
    /// (Damage, Dead / DeathRattle, MassiveDeath). Subclasses write the behaviour in <see cref="Behave"/>.
    ///
    /// Helpers for behaviours: where Samus is and whether she can be seen (detection range / height / angle, a world line of sight),
    /// turning and moving on the ground (the area's collision under the feet, walls stop the body), flying, playing animations by
    /// name or number, firing the game's own projectiles at Samus (<see cref="MpEnemyShots"/>).
    /// </summary>
    public abstract class MpEnemy : MpActorEntity
    {
        public MpPatternedInfo Info;
        public MpScriptRead.Head Head;
        public MpActorParams Params;
        public MpCharacter Char;
        public MpAnimator Anim;
        public Transform Body;              // game space under the area root: localPosition = the game position
        public BoxCollider ShotBox;
        public float HealthNow, MaxHealth;
        public bool Dead;
        public float Yaw;                   // degrees about game Z (0 = facing +Y)
        protected float _contactT, _deadT = -1f, _hurtT, _frozenT;
        private float _lastHp;

        public static readonly List<MpEnemy> Live = new List<MpEnemy>();

        // ------------------------------------------------------------------ co-op (MpCoop)

        /// <summary>The master: the Samus this enemy is thinking about this frame (the nearest one; null = this client's own).</summary>
        public static Vector3? FocusFeet;
        /// <summary>The focused player's id (-1 = this client's Samus): damage the behaviour deals goes to them (MpGame.DamagePlayer).</summary>
        public static int FocusPlayer = -1;
        public static bool FocusDead;
        /// <summary>The master: picks <see cref="FocusFeet"/> / <see cref="FocusPlayer"/> for an enemy before it thinks.</summary>
        public static System.Action<MpEnemy> PickFocus;
        /// <summary>The master: an enemy died (last blow, frozen) - the helpers play the same death.</summary>
        public static System.Action<MpEnemy, float, bool> Died;
        /// <summary>Every enemy's health times this (1 + 25 % for each player beyond the first; MpCoop sets it and calls ScaleHealth).</summary>
        public static float HealthScale = 1f;
        /// <summary>The health the game gives it (before the co-op scale).</summary>
        public float BaseMaxHealth;
        /// <summary>A helper: the master's view of this enemy (where, facing, the animation playing, frozen).</summary>
        public Vector3 NetPos;
        public float NetYaw;
        public int NetAnim = -1;
        public bool NetLoop, NetFrozen, HasNet;
        public bool Frozen { get { return _frozenT > 0f; } }

        /// <summary>The co-op scale changed: max health to the new scale, health in proportion.</summary>
        public virtual void ScaleHealth(float scale)
        {
            if (BaseMaxHealth <= 0f) return;
            float old = MaxHealth;
            MaxHealth = BaseMaxHealth * scale;
            if (old > 0f && !Dead) HealthNow = HealthNow * MaxHealth / old;
        }

        public override int NetWord { get { return (Dead ? 1 : 0) | (Gone ? 2 : 0); } }

        public override void ApplyNetWord(int word)
        {
            if ((word & 3) != 0 && !Gone && !(Dead && (word & 2) == 0)) Remove();
        }

        /// <summary>A helper: the master's enemy died - the same death here (its messages stay quiet; the master's follow).</summary>
        public void NetDie(float lastBlow, bool frozen)
        {
            if (Dead || Gone) return;
            if (frozen && _frozenT <= 0f) _frozenT = 0.1f;
            Die(lastBlow);
        }

        /// <summary>A helper: no behaviour of its own - it goes where the master has it and plays what the master plays.</summary>
        protected virtual void Puppet(float dt)
        {
            if (HasNet)
            {
                Vector3 to = NetPos - Pos;
                float d = to.magnitude;
                Pos = d > 8f ? NetPos : Vector3.MoveTowards(Pos, NetPos, Mathf.Max(d * 8f, 0.5f) * dt);
                Yaw = Mathf.MoveTowardsAngle(Yaw, NetYaw, 720f * dt);
                ApplyYaw();
            }

            if (Anim == null) return;
            if (NetAnim >= 0 && NetAnim != Anim.Anim && Anim.Has(NetAnim)) Anim.Play(NetAnim, NetLoop, 0.15f);
            Anim.Speed = NetFrozen ? 0f : 1f;
            Anim.Tick(dt);
        }

        public Vector3 Pos
        {
            get { return Body != null ? Body.localPosition : Vector3.zero; }
            set { if (Body != null) Body.localPosition = value; }
        }

        /// <summary>The game-space direction the body faces (yaw about Z, +Y at 0).</summary>
        public Vector3 Facing { get { float r = Yaw * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f); } }

        public static MpGame G { get { return MpGame.Instance; } }

        /// <summary>
        /// Sets up the body: the area-local game transform from the script head, the animated model (or the static one), the shot
        /// box from the patterned half extent and height. Call from the subclass's factory after <see cref="Info"/> and <see cref="Head"/>.
        /// </summary>
        public virtual void Build(MpAreaView view, string name)
        {
            Body = new GameObject(name).transform;
            Body.SetParent(view.Root.transform, false);
            Body.localPosition = new Vector3(Head.Pos.X, Head.Pos.Y, Head.Pos.Z);
            Yaw = Head.Rot.Z;
            ApplyYaw();
            Vector3 scale = new Vector3(Head.Scale.X, Head.Scale.Y, Head.Scale.Z);
            MpModelRef mr = Info != null ? Info.Anim : default(MpModelRef);
            Char = MpAssets.Valid(mr.Ancs) ? MpCharacter.Get(mr.Ancs, mr.Character) : null;
            if (Char != null && Char.Model != null)
            {
                Char.Upload(true);
                Anim = new MpAnimator(Char, name + " model", Body);
                Anim.Go.transform.localScale = scale;
                Anim.SoundEvent = s => PlayDataSfx(s.SfxId, s.MaxDist > 0f ? s.MaxDist : 50f);
                Anim.ParticleEvent = OnParticleEvent;
                Anim.NamedEvent = OnNamedEvent;
                if (mr.Animation >= 0) Anim.Play(mr.Animation, true, 0f);
                Anim.Tick(0f);
                Go = Anim.Go;
            }

            BaseMaxHealth = Info != null ? Info.Health : 10f;
            MaxHealth = HealthNow = _lastHp = BaseMaxHealth * HealthScale;
            Vuln = Info != null ? Info.Vuln : MpVulnerability.Immune();
            float he = Info != null && Info.HalfExtent > 0f ? Info.HalfExtent : 0.5f;
            float h = Info != null && Info.Height > 0f ? Info.Height : 1f;
            var box = new GameObject(name + " shot box");
            box.layer = MpAreaView.ShotOnlyLayer;
            box.transform.SetParent(Body, false);
            box.transform.localPosition = new Vector3(0f, 0f, h * 0.5f);
            ShotBox = box.AddComponent<BoxCollider>();
            ShotBox.size = new Vector3(he * 2f * Mathf.Abs(scale.x), he * 2f * Mathf.Abs(scale.y), h * Mathf.Abs(scale.z));
            box.AddComponent<MpHitTarget>().Entity = this;
            Live.Add(this);
        }

        public void ApplyYaw()
        {
            if (Body != null) Body.localRotation = Quaternion.AngleAxis(Yaw, Vector3.forward);
        }

        public override void SetActive(bool on)
        {
            Active = on;
            if (Body != null && Body.gameObject.activeSelf != (on && !Gone)) Body.gameObject.SetActive(on && !Gone);
        }

        /// <summary>Removed from the world (dead and faded, or deleted by its generator).</summary>
        public bool Gone;

        public override void Think(float dt)
        {
            if (Gone || Body == null) return;
            if (!Active) return;
            if (MpScripts.Follower && !Dead) { Puppet(dt); return; }
            if (Dead)
            {
                if (Anim != null) Anim.Tick(dt);
                _deadT += dt;
                // the death animation's end (its Delete event): the body fades out over 3 s, then DeathDelete (CPatterned kUE_Delete, UpdateAlphaDelta)
                if (_deadT > DeathLinger)
                {
                    float a = 1f - (_deadT - DeathLinger) / FadeSeconds;
                    if (a <= 0f) DeathDelete();
                    else SetAlpha(a);
                }

                return;
            }

            _hurtT = Mathf.Max(0f, _hurtT - dt);
            if (_frozenT > 0f)
            {
                _frozenT -= dt;
                if (_frozenT <= 0f) Thaw();
                return;
            }

            // co-op, the master: the nearest Samus is the one this enemy hunts and hurts this frame
            if (PickFocus != null) PickFocus(this);
            try
            {
                Behave(dt);
                if (Anim != null) Anim.Tick(dt);
                ContactDamage(dt);
            }
            finally
            {
                FocusFeet = null;
                FocusPlayer = -1;
                FocusDead = false;
            }
        }

        /// <summary>The enemy's own behaviour each frame while alive and active (the animation is ticked after it).</summary>
        protected abstract void Behave(float dt);

        /// <summary>Seconds a dead body stays (its death animation) before it goes.</summary>
        protected virtual float DeathLinger { get { return Anim != null ? Mathf.Clamp(Anim.Duration, 0.5f, 4f) : 0.5f; } }

        // ------------------------------------------------------------------ Samus

        public static Vector3 PlayerPos { get { return FocusFeet.HasValue ? FocusFeet.Value : G != null ? G.PlayerGame : Vector3.zero; } }
        public static Vector3 PlayerEye { get { return PlayerPos + new Vector3(0f, 0f, MpSpace.SamusEyeHeight); } }
        public static Vector3 PlayerChest { get { return PlayerPos + new Vector3(0f, 0f, 1.6f); } }
        public float DistToPlayer { get { return (PlayerPos - Pos).magnitude; } }

        /// <summary>CPatterned::InDetectionRange + line of sight: within range and height, inside the detection cone, nothing solid between.</summary>
        public bool CanSeePlayer(bool needCone = true)
        {
            if (G == null || G.State == null || (FocusFeet.HasValue ? FocusDead : G.State.Energy <= 0f)) return false;
            Vector3 d = PlayerPos - Pos;
            // a detection range of 0 means the enemy never notices Samus by sight (only its scripts wake it)
            if (Info != null && Info.DetectionRange <= 0f) return false;
            float range = Info != null ? Info.DetectionRange : 20f;
            if (new Vector2(d.x, d.y).magnitude > range) return false;
            if (Info != null && Info.DetectionHeight > 0f && Mathf.Abs(d.z) > Info.DetectionHeight) return false;
            if (needCone && Info != null && Info.DetectionAngle > 0f && Info.DetectionAngle < 180f)
            {
                Vector2 f = new Vector2(Facing.x, Facing.y), to = new Vector2(d.x, d.y);
                if (to.sqrMagnitude > 0.01f && Vector2.Angle(f, to) > Info.DetectionAngle) return false;
            }

            return LineOfSight(Pos + new Vector3(0f, 0f, Mathf.Max(0.5f, (Info != null ? Info.Height : 1f) * 0.8f)), PlayerEye);
        }

        /// <summary>No world collision between two game points.</summary>
        public static bool LineOfSight(Vector3 fromGame, Vector3 toGame)
        {
            Vector3 a = MpSpace.ToUnity(fromGame), b = MpSpace.ToUnity(toGame);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return true;
            return !ShotWorldRay(a, d / len, len * 0.98f, out RaycastHit unused);
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>The nearest hit on what stops shots and sight: the areas' shot collision (not their walk collision: glass and grates
        /// let shots through), doors, platforms - not enemies' or actors' shot boxes, not the player.</summary>
        public static bool ShotWorldRay(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(from, dir, Hits, len, (1 << 0) | (1 << MpAreaView.ShotOnlyLayer), QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (Hits[i].distance >= bd || MpSpace.Root == null || !c.transform.IsChildOf(MpSpace.Root)) continue;
                if (MpAreaView.WalkColliders.Contains(c) || c.GetComponent<MpHitTarget>() != null || c.GetComponent<MpShotVulnerability>() != null) continue;
                MpDoorShotGate gate = c.GetComponent<MpDoorShotGate>();
                if (gate != null && !gate.Blocks) continue;
                best = Hits[i];
                bd = Hits[i].distance;
                any = true;
            }

            return any;
        }

        /// <summary>The nearest hit on the game world's own collision along a Unity ray (the player's avatar and anything outside the world ignored).</summary>
        public static bool WorldRay(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(from, dir, Hits, len, WorldMask, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].distance >= bd || MpSpace.Root == null || !Hits[i].collider.transform.IsChildOf(MpSpace.Root)) continue;
                best = Hits[i];
                bd = Hits[i].distance;
                any = true;
            }

            return any;
        }

        /// <summary>The world's own collision (the areas' meshes, doors and platforms), not shot-only boxes.</summary>
        public const int WorldMask = 1 << 0;

        // ------------------------------------------------------------------ moving

        /// <summary>Turns the body toward a game point at the turn speed (degrees per second; 0 = at once).</summary>
        public void TurnTowards(Vector3 target, float dt, float degPerSec = -1f)
        {
            Vector3 d = target - Pos;
            if (new Vector2(d.x, d.y).sqrMagnitude < 1e-4f) return;
            float want = Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
            float rate = degPerSec >= 0f ? degPerSec : (Info != null && Info.TurnSpeed > 0f ? Info.TurnSpeed : 360f);
            Yaw = rate <= 0f ? want : Mathf.MoveTowardsAngle(Yaw, want, rate * dt);
            ApplyYaw();
        }

        /// <summary>
        /// Walks toward a game point at <paramref name="speed"/> units/s on the ground: a wall in the way stops the step, the feet
        /// follow the floor (up to the step-up height, down to a short drop). Returns the distance left.
        /// </summary>
        public float WalkTowards(Vector3 target, float speed, float dt)
        {
            Vector3 d = target - Pos;
            d.z = 0f;
            float dist = d.magnitude;
            if (dist < 1e-3f) return 0f;
            float step = Mathf.Min(dist, speed * dt);
            Vector3 dir = d / dist;
            Vector3 next = Pos + dir * step;
            float he = Info != null && Info.HalfExtent > 0f ? Info.HalfExtent : 0.5f;
            Vector3 chest = Pos + new Vector3(0f, 0f, Mathf.Max(0.4f, StepUp + 0.1f));
            if (!WalkClear(chest, chest + dir * (step + he))) return dist;
            float ground;
            if (GroundAt(next, out ground)) next.z = ground;
            Pos = next;
            return dist - step;
        }

        /// <summary>Nothing of the walk collision (walls, railings, glass) between two game points: bodies move by it, shots and sight do not.</summary>
        public static bool WalkClear(Vector3 fromGame, Vector3 toGame)
        {
            Vector3 a = MpSpace.ToUnity(fromGame), d = MpSpace.ToUnity(toGame) - a;
            float len = d.magnitude;
            return len < 1e-4f || !WorldRay(a, d / len, len, out RaycastHit unused);
        }

        public float StepUp { get { return Info != null && Info.StepUpHeight > 0f ? Info.StepUpHeight : 0.5f; } }

        /// <summary>The floor under a game point (searching from the step-up height above it down 4 units).</summary>
        public bool GroundAt(Vector3 p, out float z)
        {
            Vector3 from = MpSpace.ToUnity(p + new Vector3(0f, 0f, StepUp + 0.2f));
            RaycastHit hit;
            if (WorldRay(from, Vector3.down, (StepUp + 4f) * MpSpace.Scale, out hit))
            {
                z = MpSpace.ToGame(hit.point).z;
                return true;
            }

            z = p.z;
            return false;
        }

        /// <summary>Flies toward a game point (no floor), stopped by walls. Returns the distance left.</summary>
        public float FlyTowards(Vector3 target, float speed, float dt)
        {
            Vector3 d = target - Pos;
            float dist = d.magnitude;
            if (dist < 1e-3f) return 0f;
            float step = Mathf.Min(dist, speed * dt);
            Vector3 dir = d / dist;
            if (!WalkClear(Pos, Pos + dir * (step + 0.3f))) return dist;
            Pos += dir * step;
            return dist - step;
        }

        // ------------------------------------------------------------------ animation, sound, effects

        public bool PlayAnim(string name, bool loop, float blend = 0.15f, bool restart = true)
        {
            return Anim != null && Anim.Play(name, loop, blend, restart);
        }

        public bool PlayAnim(int anim, bool loop, float blend = 0.15f, bool restart = true)
        {
            return Anim != null && Anim.Play(anim, loop, blend, restart);
        }

        /// <summary>The first of the character's animations whose name contains one of the words (case-insensitive), or -1.</summary>
        public int FindAnim(params string[] words)
        {
            if (Char == null) return -1;
            foreach (string w in words)
                foreach (var kv in Char.Info.AnimNames)
                    if (kv.Value.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0) return kv.Key;
            return -1;
        }

        public object PlayDataSfx(int dataId, float maxDist = 50f)
        {
            if (G == null || dataId < 0 || dataId == 0xFFFF) return null;
            return G.PlayDataSfxAt(dataId, Pos + new Vector3(0f, 0f, 1f), 1f, false, maxDist);
        }

        public object PlaySfx(int id, float maxDist = 50f)
        {
            return G != null && id >= 0 ? G.PlaySfxAt(id, Pos + new Vector3(0f, 0f, 1f), 1f, false, maxDist) : null;
        }

        /// <summary>A world point of a bone (game space), or the body's centre.</summary>
        public Vector3 BonePos(string bone)
        {
            Transform t = Anim != null ? Anim.Bone(bone) : null;
            if (t != null) return MpSpace.ToGame(t.position);
            return Pos + new Vector3(0f, 0f, (Info != null ? Info.Height : 1f) * 0.5f);
        }

        protected virtual void OnParticleEvent(MpAnimEvents.Particle p)
        {
            if (p.Kind != "PART" || !MpAssets.Valid(p.Id)) return;
            Transform b = Anim != null && !string.IsNullOrEmpty(p.Bone) ? Anim.Bone(p.Bone) : null;
            Vector3 at = b != null ? MpSpace.ToGame(b.position) : Pos;
            // placed once: in the system's global frame at the bone (scaled), not again as its local translation
            if (Anim != null) Anim.EventEffect(p, at, MpEffects.GameOrientation(b != null ? b.rotation : Quaternion.identity));
        }

        protected virtual void OnNamedEvent(string name, int value) { }

        // ------------------------------------------------------------------ damage

        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            if (!Active || Dead || Gone) return false;
            float d = Vuln != null ? Vuln.Damage(weapon, charged, damage) : damage;
            if (d <= 0f)
            {
                if (G != null) G.OnWeaponIneffective(this);
                OnDeflected(weapon);
                return true;
            }

            _lastHp = HealthNow;
            HealthNow -= d;
            _hurtT = 0.2f;
            Send(MpScriptMsg.StDamage);
            OnHurt(weapon, charged, d);
            if (weapon == 1 && Info != null && Info.FreezeDur > 0f && HealthNow > 0f) Freeze(Info.FreezeDur);
            if (HealthNow <= 0f) Die(_lastHp - HealthNow);
            return true;
        }

        /// <summary>A hit that did damage (before death is checked): flinch animations, alerting, knockback.</summary>
        protected virtual void OnHurt(int weapon, bool charged, float damage) { }

        protected virtual void OnDeflected(int weapon) { }

        /// <summary>Lab: deaths by kind (normal, massive, frozen).</summary>
        public static int LabNormalDeaths, LabMassiveDeaths, LabFrozenDeaths, LabDeathFx;

        /// <summary>
        /// CPatterned::Death + KnockBack: DeathRattle to the links. A last blow of at least the frozen x-damage on a frozen body with an
        /// ice death explosion shatters it (MassiveFrozenDeath: the ice shatter sound, a camera shake within 40 units, the ice explosion);
        /// one of at least the x-damage is a massive death (the death sound, MassiveDeath, the death explosion PART and ELSC at the
        /// scaled offset); either way DeathDelete follows at once (Dead, gone). Otherwise the body plays its death animation (state
        /// "Dead"), fades out over 3 s and only then sends Dead (the links that drop pickups or count kills fire then).
        /// </summary>
        public virtual void Die(float lastBlow)
        {
            if (Dead) return;
            bool frozen = _frozenT > 0f;
            if (Died != null && !MpScripts.Follower) Died(this, lastBlow, frozen);
            Dead = true;
            _deadT = 0f;
            HealthNow = 0f;
            if (ShotBox != null) ShotBox.enabled = false;
            Live.Remove(this);
            if (frozen) { _frozenT = 0f; if (Anim != null) Anim.Speed = 1f; }
            Send(20);           // DeathRattle
            if (Info != null && frozen && MpAssets.Valid(Info.Particle2) && lastBlow >= Info.FrozenXDamage)
            {
                LabFrozenDeaths++;
                PlayDataSfx(Info.IceShatterSfx >= 0 ? Info.IceShatterSfx : Info.DeathSfx, 80f);
                Send(24);       // MassiveFrozenDeath
                if (G != null && DistToPlayer < 40f) G.FlashLight(Pos, new Color(0.6f, 0.8f, 1f), 0.3f, 6f);
                DeathExplosion(Info.Particle2, Info.Particle2Scale, 0xFFFFFFFF);
                DeathDelete();
                return;
            }

            if (Info != null && lastBlow >= Info.XDamage)
            {
                LabMassiveDeaths++;
                if (Info.DeathSfx >= 0) PlayDataSfx(Info.DeathSfx, 80f);
                Send(19);       // MassiveDeath
                DeathExplosion(Info.Particle1, Info.Particle1Scale, Info.Electric);
                DeathDelete();
                return;
            }

            LabNormalDeaths++;
            PlayDeathAnim();
            if (!HasDeathAnim) _deadT = DeathLinger;
        }

        /// <summary>CPatterned::GenerateDeathExplosion: the PART (and ELSC) at the body's transform times the offset scaled by the model's scale.</summary>
        protected void DeathExplosion(uint part, MpVec3 offset, uint electric)
        {
            if (Body == null) return;
            Vector3 s = Anim != null && Anim.Go != null ? Anim.Go.transform.localScale : new Vector3(Head.Scale.X, Head.Scale.Y, Head.Scale.Z);
            Vector3 off = Vector3.Scale(new Vector3(offset.X, offset.Y, offset.Z), s);
            Vector3 at = Pos + Body.localRotation * off;
            var rot = MpEffects.GameOrientation(Quaternion.identity);      // as the Parasite: the explosions are drawn unturned
            if (MpAssets.Valid(part) && MpEffects.Spawn(part, at, rot) != null) LabDeathFx++;
            if (MpAssets.Valid(electric))
            {
                MpEffects.Effect el = MpEffects.SpawnElectric(electric);
                if (el != null) { el.SetPlace(at, rot); el.Persistent = false; LabDeathFx++; }
            }
        }

        /// <summary>CPatterned::DeathDelete: Dead to the links, the body gone.</summary>
        protected void DeathDelete()
        {
            if (Gone) return;
            Send(MpScriptMsg.StDead);
            Remove();
        }

        private const float FadeSeconds = 3f;
        private MaterialPropertyBlock _fadeBlock;
        private bool _fadeMats;

        /// <summary>The body's colour alpha (CModelFlags colour): the TEV shader multiplies by _ModColor; blended from the first fade frame.</summary>
        protected void SetAlpha(float a)
        {
            if (Body == null) return;
            if (!_fadeMats)
            {
                _fadeMats = true;
                foreach (Renderer r in Body.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.materials;
                    foreach (Material m in mats)
                    {
                        if (m == null || !m.HasProperty("_ModColor")) continue;
                        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        m.SetFloat("_ZWrite", 0f);
                        m.renderQueue = 3000;
                    }

                    r.materials = mats;
                }
            }

            if (_fadeBlock == null) _fadeBlock = new MaterialPropertyBlock();
            foreach (Renderer r in Body.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(_fadeBlock);
                _fadeBlock.SetColor("_ModColor", new Color(1f, 1f, 1f, a));
                r.SetPropertyBlock(_fadeBlock);
            }
        }

        protected virtual bool HasDeathAnim { get { return FindAnim("death", "die") >= 0; } }

        protected virtual void PlayDeathAnim()
        {
            int a = FindAnim("death", "die");
            if (a >= 0) PlayAnim(a, false, 0.1f);
        }

        /// <summary>Ice: frozen still for the freeze time (CPatterned::Freeze, simplified: no ice shell).</summary>
        public virtual void Freeze(float seconds)
        {
            _frozenT = seconds;
            if (Anim != null) Anim.Speed = 0f;
        }

        protected virtual void Thaw()
        {
            _frozenT = 0f;
            if (Anim != null) Anim.Speed = 1f;
        }

        /// <summary>CPatterned contact damage: Samus touching the body's box takes the contact damage, at most every damage-wait time.</summary>
        protected virtual void ContactDamage(float dt)
        {
            _contactT -= dt;
            if (Info == null || Info.Contact.Damage <= 0f || _contactT > 0f || G == null) return;
            Vector3 d = PlayerPos - Pos;
            float he = (Info.HalfExtent > 0f ? Info.HalfExtent : 0.5f) * Mathf.Max(Mathf.Abs(Head.Scale.X), Mathf.Abs(Head.Scale.Y)) + 0.5f;
            float h = Info.Height > 0f ? Info.Height * Mathf.Abs(Head.Scale.Z) : 1f;
            if (new Vector2(d.x, d.y).magnitude > he || d.z > h || d.z < -2.7f) return;
            if (G.DamagePlayer(Info.Contact.Damage, Pos)) _contactT = Mathf.Max(0.25f, Info.DamageWaitTime);
        }

        /// <summary>Gone for good (deleted, or dead and faded).</summary>
        public void Remove()
        {
            Gone = true;
            if (Anim != null) Anim.StopEffects();
            Live.Remove(this);
            if (Body != null) Body.gameObject.SetActive(false);
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Activate:
                    if (!Active) { SetActive(true); Send(MpScriptMsg.StActive); }
                    return;
                case MpScriptMsg.Deactivate:
                    if (Active) { SetActive(false); Send(MpScriptMsg.StInactive); }
                    return;
                case 34:        // Deleted (a generator takes its copy back)
                    Remove();
                    return;
            }

            base.OnMessage(msg, from);
        }

        public override void OnRemoved()
        {
            Live.Remove(this);
            if (Anim != null) Anim.StopEffects();
            if (Body != null) Object.Destroy(Body.gameObject);
        }
    }
}
