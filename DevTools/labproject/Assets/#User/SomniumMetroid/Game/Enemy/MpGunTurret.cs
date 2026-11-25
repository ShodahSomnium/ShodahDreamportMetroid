using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The frigate's ceiling gun turret (decomp CScriptGunTurret + ScriptLoader::LoadGunTurret): two script objects, the base
    /// ("Rotate" component) and the gun ("Lower Gun" component). The base finds its gun by its Play -> Activate link, hangs the gun
    /// from its Gun_SDK locator every frame and runs the turret's state machine: Inactive (idle light; wakes when Samus is in its
    /// detection range and sight, when started by a script, or when the gun is hurt) -> Ready -> Panning A / B (sweeping; panning
    /// light) -> Targeting (lock-on sound, the base turns to Samus at its turn speed and the gun pitches, down to its limit) ->
    /// Firing (the charge: charging sound and effect at the muzzle) -> a burst (CBurstFire tables, the first burst from the script
    /// data) whose shots are the base's Attack state, which the gun takes as Action and fires its own WPSC from Blast_LCTR (aimed at
    /// Samus with the burst's aim error, at most 20 degrees off the barrel). Out of sight 10 s -> Exit Targeting (back to the front)
    /// -> Ready. Stop (the deactivation scan) -> Deactivating -> Deactivate. The base drops on its extension model while active.
    /// The gun takes the damage (its vulnerability); every hit is its Damage state (the base flinches); at 0 it sends Dead and is
    /// deleted (the frigate's own links then play the explosion, debris, sounds, shake and pickups), the base goes Destroyed. A
    /// Wave Beam kill sends the turret into its Frenzy first (spinning, firing every 0.15 s for the frenzy time).
    ///
    /// Left out: additive animations (the charge and flinch poses, the gun's pump), the projectile's intercept prediction, the
    /// collision-actor spheres on the barrel (one shot box along the barrel instead), the tracking sound's pitch bend.
    /// </summary>
    public sealed class MpGunTurret : MpEnemy
    {
        public enum St { Invalid = -1, Destroyed, Deactivate, DeactivateFromReady, Deactivating, DeactivatingFromReady, Inactive, Ready, PanningA, PanningB, Targeting, Firing, ExitTargeting, Frenzy }

        private static readonly int[] StateToLoco = { 5, 7, 9, 0, 1, 0, 1, 2, 3, 1, 1, 1, 1 };

        private static MpBurstFire.Burst B(int w, params int[] a) { return MpBurstFire.B(w, 0.15f, 0.05f, a); }

        private static readonly MpBurstFire.Burst[][] Bursts =
        {
            new[] { B(3, 1, 2), B(3, 7, 6), B(4, 3, 5), B(60, 16, 4), B(30, 4, 4) },
            new[] { B(30, 4, 5, 4), B(30, 2, 3, 4), B(30, 3, 4, 5), B(5, 16, 1, 2), B(5, 8, 7, 6) },
            new[] { B(5, 16, 1, 2, 3), B(5, 9, 8, 7, 6), B(15, 2, 3, 4, 5), B(15, 5, 4, 3, 2), B(15, 10, 11, 4, 13), B(15, 14, 13, 4, 11), B(30, 2, 4, 4, 6) },
            new[] { B(20, 16, 15), B(20, 8, 9), B(20, 13, 11), B(20, 2, 6), B(20, 3, 4) },
            new[] { B(10, 14, 4, 10), B(10, 15, 13, 4), B(10, 9, 11, 4), B(35, 15, 13, 11), B(35, 9, 11, 13) },
            new[] { B(10, 14, 13, 4, 11), B(30, 1, 15, 13, 11), B(20, 16, 15, 14, 13), B(10, 8, 9, 11, 4), B(10, 1, 15, 13, 4), B(20, 8, 9, 10, 11) },
        };

        public MpTurretData D;
        public St State = St.Invalid;
        public MpGunTurret Gun, Base;
        public float StateTime;
        public bool HasExtension { get { return _ext != null; } }
        public float Pitch { get { return _pitch; } }
        public float ExtensionT { get { return _extT; } }
        public static int Launched;

        private float _inactiveT, _activeT, _panT, _fireCycle, _halfFire, _lastGunHp, _pitch, _extT, _extRange, _lastYaw, _soundT;
        private bool _hasBeenActivated, _scriptedStart, _needsClank = true, _frenzyReverse, _firedWithSetBurst, _burstSet, _frozen;
        private bool _gunFrenzy;
        private float _origYaw, _panYaw;
        private Vector3 _targetPos, _extOffset;
        private MpBurstFire _burst;
        private GameObject _ext;
        private MpEffects.Effect _light, _charge;
        private St _lightFor = St.Invalid;
        private object _trackSfx;
        private int _nextAnim = -1;

        // ------------------------------------------------------------------ loading

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpTurretData d = MpTurretData.Read(o);
            MpEnemyFactory.CollectCharacter(assets, d.Anim.Ancs, d.Anim.Character, tex);
            MpEnemyShots.PrepareWeapon(assets, d.Projectile);
            foreach (uint p in new[] { d.IdleLight, d.DeactivateLight, d.TargettingLight, d.FrozenEffect, d.ChargingEffect, d.PanningEffect })
                MpEnemyShots.PreparePart(assets, p);
            if (!d.IsGun && MpAssets.Valid(d.ExtensionModel))
            {
                MpModel m = MpModels.Load(assets, d.ExtensionModel);
                if (m != null) MpModels.TexturesOf(m, tex);
            }
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpTurretData d = MpTurretData.Read(o);
            var e = new MpGunTurret { D = d };
            e.Head = new MpScriptRead.Head { Name = d.Name, Pos = d.Pos, Rot = d.Rot, Scale = d.Scale };
            MpModelRef anim = d.Anim;
            if (d.IsGun) anim.Animation = -1;      // the gun's one animation is an additive pump: it stays in its bind pose
            Vector3 ext = new Vector3(d.CollisionExtent.X, d.CollisionExtent.Y, d.CollisionExtent.Z);
            e.Info = new MpPatternedInfo
            {
                Anim = anim, Health = d.Health, Vuln = d.Vuln, Active = true, TurnSpeed = d.TurnSpeed,
                DetectionRange = d.DetectionRange, DetectionHeight = d.DetectionZRange,
                HalfExtent = Mathf.Max(0.3f, ext.x * 0.5f), Height = Mathf.Max(0.3f, ext.z),
            };
            e.Build(view, (d.IsGun ? "GunTurret gun " : "GunTurret base ") + o.Name);
            if (e.ShotBox != null)
            {
                if (d.IsGun)
                {
                    // the gun's box (CScriptGunTurret: its AABB at the pivot) plus the barrel to Blast_LCTR, turning with the gun
                    e.ShotBox.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                    e.ShotBox.size = new Vector3(Mathf.Max(0.9f, ext.x), 3.2f, Mathf.Max(0.9f, ext.z));
                }
                else
                {
                    e.ShotBox.transform.localPosition = new Vector3(d.CollisionOffset.X, d.CollisionOffset.Y, d.CollisionOffset.Z);
                    e.ShotBox.size = new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z));
                }
            }

            e._origYaw = e._panYaw = e._lastYaw = e.Yaw;
            e._extOffset = e.Pos;
            if (!d.IsGun)
            {
                e._burst = new MpBurstFire(Bursts, 1, (int)o.Id);
                MpModel m = MpAssets.Valid(d.ExtensionModel) ? MpModels.Get(d.ExtensionModel) : null;
                if (m == null && MpAssets.Valid(d.ExtensionModel)) MpLog.Warn("GunTurret: extension model " + d.ExtensionModel.ToString("X8") + " of '" + o.Name + "' not loaded");
                if (m != null)
                {
                    m.Build(true);
                    e._ext = m.Instantiate("GunTurret extension " + o.Name, view.Root.transform);
                    e._ext.transform.localRotation = Quaternion.AngleAxis(e.Yaw, Vector3.forward);
                    e._ext.transform.localScale = new Vector3(d.Scale.X, d.Scale.Y, d.Scale.Z);
                    e._extRange = (m.Max.z - m.Min.z) * Mathf.Abs(d.Scale.Z);
                    e._ext.transform.localPosition = e._extOffset + new Vector3(0f, 0f, -e._extRange * 0.5f);
                    e._ext.SetActive(false);
                }
            }

            e.SetActive(true);
            return e;
        }

        public override void OnLoaded()
        {
            if (D.IsGun || Links == null) return;
            // kSM_InitializedInArea: the gun is the GunTurret this base activates on Play
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != MpScriptMsg.StPlay || l.Message != MpScriptMsg.Activate) continue;
                var g = World != null ? World.Find(l.Target) as MpGunTurret : null;
                if (g == null || !g.D.IsGun) continue;
                Gun = g;
                g.Base = this;
                _lastGunHp = g.HealthNow;
                break;
            }

            SetState(St.Inactive);
        }

        public override void SetActive(bool on)
        {
            base.SetActive(on);
            if (_ext != null && !on) _ext.SetActive(false);
            if (!on) { StopLights(); StopTrack(); }
        }

        // ------------------------------------------------------------------ messages

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Start:
                    if (!D.IsGun && State == St.Inactive) _scriptedStart = true;
                    return;
                case MpScriptMsg.Stop:
                    if (!D.IsGun && State != St.Deactivate && State != St.DeactivateFromReady && State != St.Deactivating)
                        SetState(_hasBeenActivated ? St.DeactivatingFromReady : St.Deactivating);
                    return;
                case MpScriptMsg.Action:
                    if (D.IsGun) LaunchProjectile();
                    return;                      // the base: an additive flinch (not played)
                case MpScriptMsg.SetToMax:
                    _frozen = false;
                    return;
                case MpScriptMsg.SetToZero:
                    _frozen = true;
                    StopTrack();
                    return;
            }

            base.OnMessage(msg, from);
        }

        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            if (!D.IsGun) return Active && !Gone;            // the base stops shots but its health does nothing
            if (State == St.Frenzy || _gunFrenzy) return true;
            return base.TakeDamage(weapon, charged, damage);
        }

        public override void Die(float lastBlow)
        {
            if (!D.IsGun) return;
            if (_lastWeapon == 2 && !_gunFrenzy && Base != null && !Base.Gone)
            {
                // a Wave Beam kill: the turret goes into its frenzy first (CScriptGunTurret kSM_Damage)
                _gunFrenzy = true;
                HealthNow = 0f;
                State = St.Frenzy;
                if (ShotBox != null) ShotBox.enabled = false;
                return;
            }

            if (Dead) return;
            Dead = true;
            HealthNow = 0f;
            StopLights();
            Live.Remove(this);
            Send(MpScriptMsg.StDead);
            MpLog.Info("GunTurret: gun '" + Name + "' destroyed");
            Remove();
        }

        private int _lastWeapon = -1;

        protected override void OnHurt(int weapon, bool charged, float damage) { _lastWeapon = weapon; }

        // ------------------------------------------------------------------ think

        protected override void Behave(float dt)
        {
            if (D.IsGun) { GunThink(dt); return; }
            if (!_frozen)
            {
                ProcessStateMachine(dt);
                UpdateAnimation();
                UpdateGunOrientation(dt);
                UpdateExtension(dt);
            }

            UpdateTargettingSound(dt);
            UpdateHealthInfo();
        }

        private void GunThink(float dt)
        {
            if (_gunFrenzy && State == St.Destroyed && !Dead) { Die(0f); return; }
            if (Base == null || Base.Gone) { StopLights(); return; }
            UpdateGunParticles();
        }

        protected override float DeathLinger { get { return 0f; } }

        // ------------------------------------------------------------------ the state machine (the base)

        private void SetState(St s)
        {
            if (s < St.Destroyed || s > St.Frenzy) return;
            if (State != St.Invalid) StateMsg(2, 0f);
            St was = State;
            State = s;
            StateTime = 0f;
            StateMsg(0, 0f);
            if (MpScripts.Trace) MpLog.Info("GunTurret: " + Name + " " + was + " -> " + s);
        }

        /// <summary>msg 0 = activate, 1 = update, 2 = deactivate (EStateMsg).</summary>
        private void StateMsg(int msg, float dt)
        {
            switch (State)
            {
                case St.Deactivating:
                case St.DeactivatingFromReady:
                    if (msg == 1 && StateTime >= D.IntoDeactivateDelay) SetState(_hasBeenActivated ? St.DeactivateFromReady : St.Deactivate);
                    break;
                case St.Inactive: Inactive(msg, dt); break;
                case St.Ready:
                    if (msg == 0) _activeT = 0f;
                    else if (msg == 1)
                    {
                        _activeT += dt;
                        if (_activeT >= D.PanStartTime)
                        {
                            if (PlayerInFiringRange() && InDetectionRange()) StartTargeting();
                            else { SetState(St.PanningA); _panT = 0f; }
                        }
                    }

                    break;
                case St.PanningA:
                case St.PanningB: Panning(msg, dt); break;
                case St.Targeting:
                case St.Firing: Targeting(msg, dt); break;
                case St.ExitTargeting:
                    if (msg == 1 && Mathf.Abs(Mathf.DeltaAngle(Yaw, _origYaw)) < 1f) SetState(St.Ready);
                    break;
                case St.Frenzy: Frenzy(msg, dt); break;
            }
        }

        private void ProcessStateMachine(float dt)
        {
            StateMsg(1, dt);
            StateTime += dt;
            if (Gun == null || Gun.Gone) return;
            if (Gun.State != St.Frenzy) { Gun.State = State; return; }
            if (State == St.Frenzy) return;
            SetState(St.Frenzy);
        }

        private void Inactive(int msg, float dt)
        {
            if (msg == 0)
            {
                _inactiveT = 0f;
                _burstSet = false;
                if (Gun != null) _lastGunHp = Gun.HealthNow;
            }
            else if (msg == 1)
            {
                bool force = Gun != null && Gun.HealthNow < _lastGunHp;
                bool wake = D.ScriptedStartOnly ? (force || _scriptedStart) : (force || _scriptedStart || InDetectionRange());
                if (wake)
                {
                    _inactiveT += dt;
                    if (force || _inactiveT >= D.IntoActivateDelay) SetState(St.Ready);
                }
            }
            else
            {
                _hasBeenActivated = true;
                if (Gun != null) _lastGunHp = Gun.HealthNow;
            }
        }

        private void StartTargeting()
        {
            SetState(St.Targeting);
            _targetPos = PlayerChest;
            PlayDataSfx(D.SfxLockOn, 80f);
        }

        private void Panning(int msg, float dt)
        {
            if (msg == 0) { _activeT = 0f; return; }
            if (msg != 1) return;
            if (PlayerInFiringRange() && InDetectionRange()) { StartTargeting(); return; }
            _activeT += dt;
            _panT += dt;
            // the sweep (the game's pan animations turn the turret by root motion): 45 degrees to one side, then the other
            float want = _origYaw + (State == St.PanningA ? 45f : -45f);
            Yaw = Mathf.MoveTowardsAngle(Yaw, want, Mathf.Max(20f, D.TurnSpeed * 0.2f) * dt);
            ApplyYaw();
            if (_panT >= D.PanTotalSearchTime && _ext == null && !D.ScriptedStartOnly)
            {
                SetState(St.Inactive);
                _scriptedStart = _hasBeenActivated = false;
            }
            else if (_activeT >= D.PanHoldTime) SetState(State == St.PanningA ? St.PanningB : St.PanningA);
        }

        private void Targeting(int msg, float dt)
        {
            if (msg == 0) { _activeT = 0f; return; }
            if (msg == 2) { _needsClank = true; return; }
            if (_firedWithSetBurst || InDetectionRange())
            {
                UpdateTargettingMode(dt);
                if (Gun != null && !Gun.Gone)
                {
                    if (PlayerInFiringRange()) _targetPos = PlayerChest;
                    Vector3 delta = _targetPos - Gun.Pos;
                    float ex, ez;
                    _burst.GetDistanceCompensatedError(delta.magnitude, 20f, out ex, out ez);
                    Quaternion gr = Gun.Body.localRotation;
                    Gun._targetPos = _targetPos + gr * new Vector3(ex, 0f, ez);
                }

                TurnTowards(_targetPos, dt, D.TurnSpeed);
                if (ShouldFire())
                {
                    Send(MpScriptMsg.StAttack);
                    _firedWithSetBurst = true;
                }

                _activeT = 0f;
            }
            else
            {
                _activeT += dt;
                if (_activeT >= 10f) SetState(St.ExitTargeting);
            }
        }

        private void UpdateTargettingMode(float dt)
        {
            if (_fireCycle > 0f)
            {
                _fireCycle -= dt;
                if (_fireCycle < _halfFire && State != St.Firing)
                {
                    PlayDataSfx(D.SfxCharging, 80f);
                    SetState(St.Firing);
                }
            }
            else
            {
                if (State != St.Targeting) SetState(St.Targeting);
                if (!_burst.IsBurstSet)
                {
                    UpdateBurstType();
                    _fireCycle = _burst.Rand() * D.ReloadTimeVariance + D.ReloadTime;
                    _halfFire = 0.5f * _fireCycle;
                }
                else _burst.Update(dt);
            }
        }

        private void UpdateBurstType()
        {
            if (_burstSet)
            {
                int view = SamusLooksAt(Pos) ? 0 : 3;
                int shots = (int)(_burst.Rand() * 3.999f) + 2;
                int type = shots <= 2 || D.NumSubsequentShots < 3 ? 0 : shots >= 5 && D.NumSubsequentShots > 3 ? 2 : 1;
                _burst.BurstType = type + view;
            }
            else
            {
                _burst.BurstType = Mathf.Clamp(D.NumInitialShots - 2, 0, 2);
                _burst.FirstBurstIndex = D.InitialShotTableIndex;
            }

            _burst.Start();
            _firedWithSetBurst = false;
            _burstSet = true;
        }

        private bool ShouldFire()
        {
            return State == St.Targeting && _fireCycle <= 0f && _burst.ShouldFire && PlayerInFiringRange();
        }

        private void Frenzy(int msg, float dt)
        {
            if (msg == 0)
            {
                _frenzyReverse = Random.value < 0.5f;
                _fireCycle = 0.15f;
                return;
            }

            if (msg != 1) return;
            if (StateTime >= D.FrenzyDuration)
            {
                SetState(St.Destroyed);
                if (Gun != null) Gun.State = St.Destroyed;
                return;
            }

            float off = Mathf.DeltaAngle(_origYaw, Yaw);
            if (_frenzyReverse) { if (off > 45f) _frenzyReverse = false; }
            else if (off < -45f) _frenzyReverse = true;
            Yaw += (_frenzyReverse ? 1f : -1f) * D.TurnSpeed * dt;
            ApplyYaw();
            if (Gun != null && !Gun.Gone)
            {
                _fireCycle -= dt;
                if (_fireCycle < 0f)
                {
                    Gun._targetPos = Gun.Pos + Gun.Body.localRotation * Vector3.up * 100f;
                    Send(MpScriptMsg.StAttack);
                    _fireCycle = 0.15f;
                }
            }
        }

        private void UpdateHealthInfo()
        {
            if (State == St.Destroyed) return;
            if (Gun == null || Gun.Gone)
            {
                SetState(St.Destroyed);
                _frozen = false;
                StopTrack();
                Gun = null;
            }
        }

        // ------------------------------------------------------------------ senses

        /// <summary>CScriptGunTurret::InDetectionRange: Samus below the base or within 20 degrees of its front, in range and height, in sight of the muzzle.</summary>
        public bool InDetectionRange()
        {
            if (G == null || G.State == null || G.State.Energy <= 0f) return false;
            Vector3 delta = PlayerPos - Pos;
            bool where = delta.z <= 0f || Vector3.Angle(Facing, delta) <= 20f;
            if (!where || delta.sqrMagnitude > D.DetectionRange * D.DetectionRange) return false;
            if (D.DetectionZRange != 0f && Mathf.Abs(delta.z) >= D.DetectionZRange) return false;
            return SightTest();
        }

        private bool SightTest()
        {
            if (Gun == null || Gun.Gone) return false;
            if (_burstSet || (State == St.Inactive && _ext != null)) return true;
            return LineOfSight(Gun.Muzzle, PlayerChest);
        }

        /// <summary>CScriptGunTurret::IsPlayerInFiringRange: within the left / right limits of the original front, not more than 20 degrees above, not below the down limit.</summary>
        public bool PlayerInFiringRange()
        {
            Vector3 delta = PlayerPos - Pos;
            Vector3 flat = new Vector3(delta.x, delta.y, 0f);
            float yawR = _origYaw * Mathf.Deg2Rad;
            Vector3 front = new Vector3(-Mathf.Sin(yawR), Mathf.Cos(yawR), 0f), right = new Vector3(front.y, -front.x, 0f);
            float lim = Vector3.Dot(right, delta) >= 0f ? D.RightMaxAngle : D.LeftMaxAngle;
            if (flat.sqrMagnitude > 1e-6f && Vector3.Angle(front, flat) > lim) return false;
            float pitch = Vector3.Angle(Vector3.forward, delta) - 90f;      // + below
            return pitch >= -20f && pitch <= D.DownMaxAngle;
        }

        /// <summary>Whether Samus is looking toward a game point (the game's "in view" choice of burst tables).</summary>
        public static bool SamusLooksAt(Vector3 gamePos)
        {
            Camera c = Camera.main;
            if (c == null) return true;
            Vector3 to = MpSpace.ToUnity(gamePos) - c.transform.position;
            return Vector3.Angle(c.transform.forward, to) < 45f;
        }

        // ------------------------------------------------------------------ the gun

        public Vector3 Muzzle { get { return Anim != null && Anim.Bone("Blast_LCTR") != null ? BonePos("Blast_LCTR") : Pos + Body.localRotation * new Vector3(0f, 2.6f, 0f); } }

        /// <summary>CScriptGunTurret::LaunchProjectile: the gun's WPSC from Blast_LCTR toward the target the base gave it (at most 20 degrees off the barrel).</summary>
        private void LaunchProjectile()
        {
            if (Dead || Gone || !Active) return;
            Vector3 from = Muzzle;
            Vector3 front = Body.localRotation * Vector3.up;
            Vector3 delta = _targetPos - from;
            Vector3 dir = delta.sqrMagnitude > 1e-6f ? delta.normalized : front;
            if (Vector3.Angle(front, dir) > 20f) dir = Vector3.RotateTowards(front, dir, 20f * Mathf.Deg2Rad, 0f);
            if (MpEnemyShots.Fire(D.Projectile, from, dir, D.ProjectileDamage.Damage, this))
            {
                Launched++;
                PlayWeaponSound(D.Projectile, from);
            }
        }

        /// <summary>The flying sound of a WPSC (PJFX, CEnergyProjectile) once at the muzzle.</summary>
        public static void PlayWeaponSound(uint wpsc, Vector3 at)
        {
            if (MpEffects.Lib == null || G == null) return;
            MpWeaponDescFull w = MpEffects.Lib.Weapon(wpsc);
            if (w != null && w.PJFX > 0 && w.PJFX < 0x10000) G.PlayDataSfxAt((int)w.PJFX, at, 1f, false, 60f);
        }

        /// <summary>CScriptGunTurret::UpdateGunOrientation: the gun hangs from the base's Gun_SDK; while targeting it pitches to the target at the turn speed (not below the down limit).</summary>
        private void UpdateGunOrientation(float dt)
        {
            if (Gun == null || Gun.Gone) return;
            Vector3 pos = Anim != null && Anim.Bone("Gun_SDK") != null ? BonePos("Gun_SDK") : Pos + new Vector3(0f, 0f, -2.95f);
            Gun.Pos = pos;
            float gunYaw = Yaw;
            Transform sdk = Anim != null ? Anim.Bone("Gun_SDK") : null;
            if (sdk != null)
            {
                Vector3 f = Body.localRotation * (sdk.localRotation * Vector3.up);
                if (new Vector2(f.x, f.y).sqrMagnitude > 1e-6f) gunYaw = Mathf.Atan2(-f.x, f.y) * Mathf.Rad2Deg;
            }

            float want = 0f;
            switch (State)
            {
                case St.Targeting:
                case St.Firing:
                {
                    Vector3 d = Gun._targetPos - pos;
                    if (d.sqrMagnitude > 1e-6f) want = Mathf.Atan2(d.z, new Vector2(d.x, d.y).magnitude) * Mathf.Rad2Deg;
                    break;
                }

                case St.Frenzy:
                    want = -0.5f * D.DownMaxAngle * (1f - Mathf.Cos(2f * StateTime * D.TurnSpeed * Mathf.Deg2Rad));
                    break;
            }

            float rate = State == St.ExitTargeting ? 0.3f * D.TurnSpeed : D.TurnSpeed;
            _pitch = Mathf.Max(Mathf.MoveTowards(_pitch, want, rate * dt), -D.DownMaxAngle);
            if (State == St.ExitTargeting) { Yaw = Mathf.MoveTowardsAngle(Yaw, _origYaw, 0.3f * D.TurnSpeed * dt); ApplyYaw(); }
            Gun.Yaw = gunYaw;
            Gun._pitch = _pitch;
            Gun.Body.localRotation = Quaternion.AngleAxis(gunYaw, Vector3.forward) * Quaternion.AngleAxis(_pitch, Vector3.right);
        }

        /// <summary>CScriptGunTurret::UpdateExtensionModelState: the base drops on its extension while searching or targeting, back up when idle.</summary>
        private void UpdateExtension(float dt)
        {
            if (_ext == null) return;
            float change = 1.5f * dt;
            switch (State)
            {
                case St.PanningA:
                case St.PanningB:
                case St.Targeting:
                case St.Firing:
                case St.ExitTargeting:
                    _extT = Mathf.Min(_extT + change, 0.9f);
                    break;
                case St.Ready:
                case St.Deactivating:
                case St.DeactivatingFromReady:
                case St.Frenzy:
                    break;
                default:
                    _extT = Mathf.Max(0f, _extT - change);
                    break;
            }

            Pos = _extOffset + new Vector3(0f, 0f, -D.ExtensionDropDownDist * _extT);
            _ext.transform.localRotation = Quaternion.AngleAxis(Yaw, Vector3.forward);
            if (_ext.activeSelf != (_extT > 0f && Active)) _ext.SetActive(_extT > 0f && Active);
        }

        // ------------------------------------------------------------------ animation, lights, sounds

        private void UpdateAnimation()
        {
            if (Anim == null || State < St.Destroyed) return;
            int loco = StateToLoco[(int)State];
            int a;
            switch (loco)
            {
                case 0: a = FindAnim("B_inactiveReady"); break;
                case 1: a = FindAnim("B_Ready_"); break;
                case 2: a = FindAnim("B_panLeft_", "B_panLeftHold"); break;
                case 3: a = FindAnim("B_panRight_", "B_panRightHold"); break;
                case 5: a = FindAnim("destroyed"); break;
                case 7: a = FindAnim("R_deactive_POSE", "R_deactivate_"); break;
                default: a = FindAnim("deactiveFromReady", "deactivateFromReady"); break;
            }

            if (a < 0 || a == _nextAnim) return;
            int was = _nextAnim;
            _nextAnim = a;
            // the transitions the PAS plays on the way: into the ready stance, into the deactivated poses
            int into = -1;
            if (loco == 1 && was == FindAnim("B_inactiveReady")) into = FindAnim("B_intoactiveReady");
            else if (loco == 7) into = FindAnim("R_deactivate_gunturret");
            else if (loco == 9) into = FindAnim("R_deactivateFromReady");
            if (into >= 0) { PlayAnim(into, false, 0.15f); _pendingLoop = a; }
            else { PlayAnim(a, true, 0.25f); _pendingLoop = -1; }
        }

        private int _pendingLoop = -1;

        public override void Think(float dt)
        {
            base.Think(dt);
            if (_pendingLoop >= 0 && Anim != null && Anim.Done && !Gone)
            {
                PlayAnim(_pendingLoop, true, 0.1f);
                _pendingLoop = -1;
            }
        }

        /// <summary>CScriptGunTurret::UpdateGunParticles: one light system at light_LCTR by state (idle, deactivated, panning, targeting), the charge at Blast_LCTR while firing.</summary>
        private void UpdateGunParticles()
        {
            St s = State;
            uint want;
            switch (s)
            {
                case St.Deactivate:
                case St.DeactivateFromReady:
                case St.Deactivating:
                case St.DeactivatingFromReady: want = D.DeactivateLight; break;
                case St.Inactive: want = D.IdleLight; break;
                case St.PanningA:
                case St.PanningB: want = D.PanningEffect; break;
                case St.Ready:
                case St.Targeting:
                case St.Firing:
                case St.ExitTargeting:
                case St.Frenzy: want = D.TargettingLight; break;
                default: want = 0xFFFFFFFF; break;
            }

            if (Base != null && Base._frozen) want = D.FrozenEffect;
            St key = Base != null && Base._frozen ? St.Invalid : s;
            if (key != _lightFor || (_light == null && MpAssets.Valid(want)))
            {
                if (_light != null) _light.Finish();
                _light = MpAssets.Valid(want) ? MpEffects.Spawn(want, Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), true) : null;
                _lightFor = key;
            }

            Transform lb = Anim != null ? Anim.Bone("light_LCTR") : null;
            if (_light != null)
            {
                Vector3 at = lb != null ? MpSpace.ToGame(lb.position) : Pos;
                _light.SetGlobal(at, MpEffects.GameOrientation(lb != null ? lb.rotation : Body.rotation), 1f);
            }

            bool charging = (s == St.Firing || s == St.Frenzy) && !(Base != null && Base._frozen);
            if (charging && _charge == null) _charge = MpEffects.Spawn(D.ChargingEffect, Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), true);
            else if (!charging && _charge != null) { _charge.Finish(); _charge = null; }
            if (_charge != null)
            {
                Transform bb = Anim != null ? Anim.Bone("Blast_LCTR") : null;
                _charge.SetGlobal(Muzzle, MpEffects.GameOrientation(bb != null ? bb.rotation : Body.rotation), 1f);
            }
        }

        private void StopLights()
        {
            if (_light != null) { _light.Finish(); _light = null; }
            if (_charge != null) { _charge.Finish(); _charge = null; }
            _lightFor = St.Invalid;
        }

        /// <summary>CScriptGunTurret::UpdateTargettingSound: the stop clank on settling at a target, the tracking loop while turning.</summary>
        private void UpdateTargettingSound(float dt)
        {
            _soundT += dt;
            float turned = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, Yaw));
            bool targeting = State == St.Targeting || State == St.Firing;
            if (_needsClank && targeting && turned < 20f)
            {
                if (!_frozen) PlayDataSfx(D.SfxStopClank, 60f);
                _needsClank = false;
            }

            if (_soundT >= 0.5f && !_frozen)
            {
                if (targeting || State == St.Frenzy)
                {
                    bool stopped = turned < dt * 2f;
                    if (!stopped && _trackSfx == null) { _trackSfx = G != null && D.SfxTracking >= 0 ? G.PlayDataSfxAt(D.SfxTracking, Pos, 1f, true, 60f) : null; _soundT = 0f; }
                    else if (stopped && _trackSfx != null) { StopTrack(); _soundT = 0f; }
                }
                else if (_trackSfx != null) { StopTrack(); _soundT = 0f; }
            }
            else if (_frozen) StopTrack();

            _lastYaw = Yaw;
        }

        private void StopTrack()
        {
            if (_trackSfx != null && G != null) G.StopSfx(_trackSfx);
            _trackSfx = null;
        }

        public override void OnRemoved()
        {
            StopLights();
            StopTrack();
            if (_ext != null) Object.Destroy(_ext);
            base.OnRemoved();
        }
    }
}
