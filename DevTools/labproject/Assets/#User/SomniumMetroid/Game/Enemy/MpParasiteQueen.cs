using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The Parasite Queen in the frigate's Reactor Core (decomp CNewIntroBoss, script type 0x0E, ScriptLoader::LoadNewIntroBoss).
    /// It hangs in the reactor's tube and never moves; its state machine (AFSM: Start -> Patrol; Patrol -> Generate on AIStage,
    /// -> Attack on ShouldAttack / InAttackPosition; Attack -> Generate on AIStage, -> Patrol on AnimOver; Generate -> Patrol)
    /// runs as the game's: in Patrol it idles in its stage's wall-hang loop and turns 45 degrees at a time toward where Samus will
    /// be (CNewIntroBoss::ShouldTurn, the minimum turn angle from its script data); every 6-8 s (shorter by 41% / 82.5% below two
    /// thirds / one third of its health) it rears up and fires its plasma beam (CPlasmaProjectile, the beam info of
    /// AcceptScriptMsg(Registered): 50 long, radius 1, growing at 150 a second, yellow inside, green outside, the script's
    /// contact effect and textures) from LCTR_INTROMOUTH between the animation's DamageOn and DamageOff events, aimed at where
    /// Samus stood when it started, turned at most 30 degrees off the mouth's own direction; the beam stops on the world (the
    /// reactor's rings too) and burns Samus while it touches her (the script damage x 60 a second, x1.25 in the last stage).
    /// Below two thirds and one third of its health it roars (Generate: Entered / Exited) and changes stage (idle, turn and
    /// attack animations); in the later stages it attacks twice in a row. Shots only hurt it on its head (its own vulnerability:
    /// weak to everything) and pelvis (normal), the game's two damageable collision actors, so the rotating reactor shields
    /// (<see cref="MpIntroBossRings"/>) protect it until their holes line up. Death (CNewIntroBoss::Think): the beam is cut, the
    /// body goes back to its start, DeathRattle goes out (the area's escape chain: ring breakup, explosions, the death cinematic
    /// relay), the death animation plays, then - after the game's 3 s fade - Dead (the hangar's teleport trigger, layer changes).
    /// The boss energy bar (CHudBossEnergyInterface) shows while it is active and alive, over the maximum of the area's
    /// BossEnergyBar special function, named from STRG_Main as the game does.
    /// </summary>
    public sealed class MpParasiteQueen : MpEnemy
    {
        // ScriptLoader::LoadNewIntroBoss after the actor parameters
        public float MinTurnAngle = 45f;
        public uint Projectile;
        public MpDamageInfo BeamDamage;
        public uint BeamContactFx = 0xFFFFFFFF, BeamPulseFx = 0xFFFFFFFF, BeamTexture = 0xFFFFFFFF, BeamGlowTexture = 0xFFFFFFFF;

        public string BossName = "Parasite Queen";
        public float BarMax;

        private const int StEntered = 3, StExited = 4, StDeathRattle = 20;
        private const int Relaxed = 0, Lurk = 1, Combat = 2;
        private const float BeamLength = 50f, BeamRadius = 1f, BeamTravel = 150f, BeamExpansion = 1f, BeamShutdown = 1f, BeamPulseSpeed = 20f;
        private static readonly Color BeamInner = new Color(1f, 1f, 0f, 1f), BeamOuter = new Color(28f / 255f, 147f / 255f, 39f / 255f, 1f);

        private static MpStrings _mainStrings;
        private static readonly object StringsLock = new object();

        // ------------------------------------------------------------------ loading

        private static MpReader ReadAll(MpScriptObject o, MpParasiteQueen e, out MpScriptRead.Head h, out MpPatternedInfo info, out MpActorParams ap)
        {
            MpReader r = MpEnemyFactory.ReadPatterned(o, out h, out info, out ap);
            e.MinTurnAngle = r.F32();
            e.Projectile = r.U32();
            e.BeamDamage = MpDamageInfo.Read(r);
            e.BeamContactFx = r.U32();
            e.BeamPulseFx = r.U32();
            e.BeamTexture = r.U32();
            e.BeamGlowTexture = r.U32();
            return r;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            var e = new MpParasiteQueen();
            MpScriptRead.Head h;
            MpPatternedInfo info;
            MpActorParams ap;
            ReadAll(o, e, out h, out info, out ap);
            MpEnemyFactory.CollectPatterned(assets, info, tex);
            MpEnemyShots.PreparePart(assets, e.BeamContactFx);
            MpEnemyShots.PreparePart(assets, e.BeamPulseFx);
            if (MpAssets.Valid(e.BeamTexture)) tex.Add(e.BeamTexture);
            if (MpAssets.Valid(e.BeamGlowTexture)) tex.Add(e.BeamGlowTexture);
            lock (StringsLock)
            {
                if (_mainStrings != null) return;
                try
                {
                    MpPak.Entry main = assets.Named("STRG_Main");
                    if (main != null) _mainStrings = MpStrings.Read(main.Pak.Read(main));
                }
                catch (System.Exception ex) { MpLog.Warn("Parasite Queen: STRG_Main not read: " + ex.Message); }
            }
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            var e = new MpParasiteQueen();
            ReadAll(o, e, out e.Head, out e.Info, out e.Params);
            e.Build(view, "ParasiteQueen " + o.Name);
            e.Setup(o, view);
            e.SetActive(e.Info.Active);
            return e;
        }

        /// <summary>
        /// The boss bar's maximum and name: the BossEnergyBar special function the queen increments when it becomes active
        /// (CStateManager::SetBossParams: its first float, string index = its second float + 86 in STRG_Main; on the PAL disc's table
        /// the six boss names sit 6 earlier).
        /// </summary>
        private void FindBossBar(MpScriptObject o, MpAreaView view)
        {
            BarMax = MaxHealth;
            float stringFloat = 0f;
            foreach (MpScriptObject.Link l in o.Links)
            {
                if (l.State != MpScriptMsg.StActive || l.Message != MpScriptMsg.Increment) continue;
                foreach (MpScriptObject b in view.Payload.Objects)
                {
                    if (b.Id != l.Target || b.Type != MpScriptType.SpecialFunction || b.PropCount != 15) continue;
                    var r = new MpReader(b.Data, b.PropAt, b.PropEnd - b.PropAt);
                    MpScriptRead.ReadHead(r, false);
                    if (r.S32() != 12) continue;
                    r.CString();
                    float max = r.F32();
                    stringFloat = r.F32();
                    if (max > 0f) BarMax = max * HealthScale;
                }
            }

            MpStrings s;
            lock (StringsLock) s = _mainStrings;
            if (s == null) return;
            int idx = (int)stringFloat + 86;
            string name = MpStrings.Plain(s[idx]).Trim();
            if (name.Length == 0) name = MpStrings.Plain(s[idx - 6]).Trim();
            if (name.Length > 0) BossName = name;
        }

        // ------------------------------------------------------------------ the body

        /// <summary>One of the game's damageable collision actors (CCollisionActor on Head_1 / Pelvis): shots on it hurt the queen with its own vulnerability.</summary>
        private sealed class Part : MpEntity
        {
            public MpParasiteQueen Owner;
            public MpVulnerability Vuln;
            public string Bone, ToBone;
            public BoxCollider Box;
            public Transform From, To;

            public override bool TakeDamage(int weapon, bool charged, float damage)
            {
                return Owner != null && Owner.HitPart(this, weapon, charged, damage);
            }

            public override string ToString() { return "Parasite Queen " + Bone; }
        }

        private Part _head, _pelvis;
        private Transform _root, _headBone;
        private Vector3 _homePos;
        private float _homeYaw;

        private void Setup(MpScriptObject o, MpAreaView view)
        {
            _root = view.Root.transform;
            _homePos = Pos;
            _homeYaw = Yaw;
            if (ShotBox != null) ShotBox.enabled = false;      // the queen itself is not solid or targetable, only its collision actors
            FindBossBar(o, view);
            if (Anim != null)
            {
                Anim.SoundEvent = OnSound;
                _headBone = Anim.Bone("Head_1");
            }

            var normal = new MpVulnerability();
            for (int i = 0; i < 15; i++) normal.Normal[i] = MpVulnerability.NormalV;
            for (int i = 0; i < 4; i++) { normal.Charged[i] = MpVulnerability.NormalV; normal.Combo[i] = MpVulnerability.NormalV; }
            _head = MakePart("Head_1", null, Info.Vuln, new Vector3(3f, 3f, 3f));
            _pelvis = MakePart("Pelvis", "Spine_3", normal, new Vector3(4f, 1f, 4f));
            Vuln = Info.Vuln;
            MpLog.Info("Parasite Queen: health " + MaxHealth + ", bar max " + BarMax + " '" + BossName + "', min turn " + MinTurnAngle + ", beam damage " + BeamDamage.Damage
                + " x60/s, contact fx " + BeamContactFx.ToString("X8") + ", textures " + BeamTexture.ToString("X8") + "/" + BeamGlowTexture.ToString("X8")
                + (Char != null ? ", " + Char.Report : ", NO CHARACTER") + ", head bone " + (_headBone != null) + ", mouth " + (Anim != null && Anim.Bone("LCTR_INTROMOUTH") != null));
        }

        private Part MakePart(string bone, string toBone, MpVulnerability vuln, Vector3 size)
        {
            var p = new Part { Owner = this, Vuln = vuln, Bone = bone, ToBone = toBone, Active = true };
            p.From = Anim != null ? Anim.Bone(bone) : null;
            p.To = Anim != null && toBone != null ? Anim.Bone(toBone) : null;
            var go = new GameObject("ParasiteQueen " + bone + " shot box");
            go.layer = MpAreaView.ShotOnlyLayer;
            go.transform.SetParent(Body, false);
            p.Box = go.AddComponent<BoxCollider>();
            p.Box.size = size;
            go.AddComponent<MpHitTarget>().Entity = p;
            return p;
        }

        /// <summary>The collision actors follow their joints (CCollisionActorManager::Update): a sphere on the head, a box from the pelvis to Spine_3.</summary>
        private void PlaceParts()
        {
            foreach (Part p in new[] { _head, _pelvis })
            {
                if (p == null || p.Box == null) continue;
                Transform t = p.Box.transform;
                if (p.From == null) { t.localPosition = new Vector3(0f, 0f, 3f); continue; }
                Vector3 a = MpSpace.ToGame(p.From.position);
                if (p.To != null)
                {
                    Vector3 b = MpSpace.ToGame(p.To.position);
                    Vector3 d = b - a;
                    t.localPosition = Body.InverseTransformPoint(MpSpace.ToUnity((a + b) * 0.5f));
                    if (d.sqrMagnitude > 1e-4f)
                    {
                        // in the body's own (game) frame: the box's long side along pelvis -> Spine_3
                        Vector3 local = Quaternion.Inverse(Body.localRotation) * d.normalized;
                        t.localRotation = Quaternion.LookRotation(local, Mathf.Abs(local.z) > 0.95f ? Vector3.up : Vector3.forward);
                        p.Box.size = new Vector3(4f, 4f, Mathf.Max(1f, d.magnitude));
                    }
                }
                else t.localPosition = Body.InverseTransformPoint(p.From.position);
            }
        }

        // ------------------------------------------------------------------ state machine (CNewIntroBoss + its AFSM)

        private enum State { Patrol, Attack, Generate }
        private State _state = State.Patrol;
        private float _stateT, _attackT = 8f, _lookBlend = 0.2f;
        private int _prog, _loco = Relaxed;
        private bool _again;                 // x678_: a second attack straight after (later stages)
        private Vector3 _targetPos, _lookPos, _startPlayerPos, _predicted, _lastPlayer, _playerVel;
        private bool _havePlayer;
        private bool _turning;
        private float _turnLeft, _turnRate;

        /// <summary>CNewIntroBoss::GetLocoForHealth.</summary>
        private int LocoForHealth()
        {
            if (HealthNow > 0.66f * MaxHealth) return Relaxed;
            if (HealthNow > 0.33f * MaxHealth) return Lurk;
            return Combat;
        }

        /// <summary>CNewIntroBoss::GetNextAttackTime: 6-8 s, shorter by 41.25% in the second stage and 82.5% in the last.</summary>
        private float NextAttackTime()
        {
            float t = 2f * Random.value + 6f;
            if (HealthNow > 0.66f * MaxHealth) return t;
            if (HealthNow > 0.33f * MaxHealth) return t - 0.4125f * t;
            return t - 0.825f * t;
        }

        /// <summary>CNewIntroBoss::PlayerPos: half the eye height above the floor under Samus, plus 0.2.</summary>
        private static Vector3 BeamTarget()
        {
            Vector3 p = PlayerPos;
            float half = MpSpace.SamusEyeHeight * 0.5f;
            RaycastHit hit;
            if (WorldRay(MpSpace.ToUnity(p + new Vector3(0f, 0f, half)), Vector3.down, 30f * MpSpace.Scale, out hit))
                return MpSpace.ToGame(hit.point) + new Vector3(0f, 0f, half + 0.2f);
            return p + new Vector3(0f, 0f, half + 0.2f);
        }

        /// <summary>CNewIntroBoss::ShouldTurn: where Samus will be in a second is more than the minimum turn angle off the facing.</summary>
        private bool ShouldTurn()
        {
            _predicted = BeamTarget() + _playerVel;
            return Mathf.Abs(TurnAngle(_predicted)) > MinTurnAngle;
        }

        private float TurnAngle(Vector3 to)
        {
            Vector3 d = to - Pos;
            if (new Vector2(d.x, d.y).sqrMagnitude < 1e-4f) return 0f;
            return Mathf.DeltaAngle(Yaw, Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg);
        }

        private void Enter(State s)
        {
            // the old state's Deactivate
            if (_state == State.Attack && s != State.Attack)
            {
                int loco = LocoForHealth();
                _again = (loco == Lurk || loco == Combat) && !_again;
                if (_beamOn && _beamState <= 2) BeamOff();
            }

            _state = s;
            _stateT = 0f;
            _prog = 0;
            MpLog.Info("Parasite Queen: " + s + " (health " + HealthNow.ToString("0") + "/" + MaxHealth + ", stage " + _loco + ")");
            if (s == State.Generate)
            {
                _turning = false;
                _loco = LocoForHealth();
                Send(StEntered);
            }
        }

        protected override void Behave(float dt)
        {
            Vector3 pp = PlayerPos;
            if (_havePlayer && dt > 0f) _playerVel = Vector3.Lerp(_playerVel, (pp - _lastPlayer) / dt, Mathf.Clamp01(dt * 8f));
            _lastPlayer = pp;
            _havePlayer = true;
            if (_lookBlend < 0.2f) _lookBlend += dt;
            if (_attackT > 0f) _attackT -= dt;
            _stateT += dt;
            Turn(dt);

            switch (_state)
            {
                case State.Patrol:
                    if (_loco != LocoForHealth()) { Enter(State.Generate); break; }
                    if (_attackT <= 0f && !_turning && !ShouldTurn()) { Enter(State.Attack); break; }
                    if (_stateT > 0.25f && _again && !_turning && !ShouldTurn()) { Enter(State.Attack); break; }
                    Patrol();
                    break;
                case State.Attack:
                    if (_loco != LocoForHealth()) { Enter(State.Generate); break; }
                    if (_prog == 3) { Enter(State.Patrol); break; }
                    Attack();
                    break;
                case State.Generate:
                    if (_prog == 3) { Enter(State.Patrol); break; }
                    Generate();
                    break;
            }
        }

        private void Patrol()
        {
            Vector3 p = BeamTarget();
            _targetPos = _lookBlend > 0.2f ? p : _lookPos + (_lookBlend / 0.2f) * (p - _lookPos);
            if (!_turning) PlayAnim(IdleAnim(), true, 0.25f, false);
            if (ShouldTurn()) { _prog = 0; StartTurn(); }
        }

        private void Attack()
        {
            if (_prog == 0)
            {
                PlayAnim(AttackAnim(), false, 0.2f);
                _prog = 2;
                return;
            }

            if (_prog == 2)
            {
                if (Anim == null || Anim.Done) { _prog = 3; _lookBlend = 0f; }
                if (!_beamOn) _targetPos = PlayerPos;
            }
        }

        private void Generate()
        {
            if (_prog == 0)
            {
                PlayAnim(HealthNow > 0.33f * MaxHealth ? AnimNo("B_roar1_introboss", 8) : AnimNo("B_roar2_introboss", 9), false, 0.2f);
                _prog = 2;
                return;
            }

            if (_prog == 2 && (Anim == null || Anim.Done))
            {
                _prog = 3;
                Send(StExited);
            }
        }

        /// <summary>The body controller's turn: a 45-degree turn animation of the stage at a time, toward the predicted place.</summary>
        private void StartTurn()
        {
            if (_turning) return;
            float a = TurnAngle(_predicted);
            bool left = a > 0f;
            int anim = _loco == Combat ? (left ? AnimNo("B_45left_3_introboss", 6) : AnimNo("B_45right_3_introboss", 7))
                : _loco == Lurk ? (left ? AnimNo("B_45left_2_introboss", 12) : AnimNo("B_45right_2_introboss", 13))
                : (left ? AnimNo("B_45left_introboss", 0) : AnimNo("B_45right_introboss", 1));
            PlayAnim(anim, false, 0.2f);
            float dur = base.Anim != null && base.Anim.Duration > 0.1f ? base.Anim.Duration : 1f;
            _turnLeft = Mathf.Clamp(a, -45f, 45f);
            _turnRate = 45f / dur;
            _turning = true;
        }

        private void Turn(float dt)
        {
            if (!_turning) return;
            float step = Mathf.Min(Mathf.Abs(_turnLeft), _turnRate * dt) * Mathf.Sign(_turnLeft);
            Yaw += step;
            _turnLeft -= step;
            ApplyYaw();
            if (base.Anim != null && !base.Anim.Done) return;
            _turning = false;
            // keeps turning (another 45-degree step) while still off the predicted place
            _predicted = BeamTarget() + _playerVel;
            if (_state == State.Patrol && Mathf.Abs(TurnAngle(_predicted)) > 5f) StartTurn();
        }

        private int IdleAnim()
        {
            return _loco == Combat ? AnimNo("B_wallhang3_introboss", 11) : _loco == Lurk ? AnimNo("B_wallhang2_introboss", 10) : AnimNo("B_wallhang1_introboss", 2);
        }

        private int AttackAnim()
        {
            int loco = LocoForHealth();
            return loco == Combat ? AnimNo("A_purge3_introboss", 5) : loco == Lurk ? AnimNo("A_purge2_introboss", 4) : AnimNo("A_purge1_mid_introboss2", 3);
        }

        private int AnimNo(string name, int fallback)
        {
            int n = Char != null ? Char.AnimNumber(name) : -1;
            return n >= 0 ? n : fallback;
        }

        // ------------------------------------------------------------------ animation events

        private object _loopSfx;

        /// <summary>CActor::ProcessSoundEvent: the id's top bits say looping (0x80000000) and not positional (0x40000000); the weight is the chance.</summary>
        private void OnSound(MpAnimEvents.Sound s)
        {
            if (G == null) return;
            if (s.N.Weight < 1f && Random.value > s.N.Weight) return;
            int id = s.SfxId & 0xFFFF;
            bool loop = (s.SfxId & unchecked((int)0x80000000)) != 0, nonEmitter = (s.SfxId & 0x40000000) != 0;
            float maxDist = s.MaxDist > 0f ? s.MaxDist : 150f;
            Vector3? at = nonEmitter ? (Vector3?)null : BonePos("Head_1");
            if (loop)
            {
                if (_loopSfx == null) _loopSfx = G.PlayDataSfxAt(id, at, 1f, true, maxDist);
                return;
            }

            G.PlayDataSfxAt(id, at, 1f, false, maxDist);
        }

        private void StopLoop()
        {
            if (_loopSfx != null && G != null) G.StopSfx(_loopSfx);
            _loopSfx = null;
        }

        /// <summary>CNewIntroBoss::DoUserAnimEvent: DamageOn fires the stage's beam from the event's locator, DamageOff lets it go.</summary>
        protected override void OnNamedEvent(string name, int value)
        {
            if (name == "Loop") return;
            if (value == 2) StopLoop();
            else if (value == 8) BeamOn(LocatorOf(8));
            else if (value == 9)
            {
                if (_beamOn) BeamOff();
                _attackT = NextAttackTime();
                Send(MpScriptMsg.StAttack);
            }
        }

        /// <summary>The locator named by the current animation's user event of this type.</summary>
        private string LocatorOf(int type)
        {
            MpAnimSet.Entry e = Char != null && base.Anim != null ? Char.EntryOf(base.Anim.Anim) : null;
            if (e != null)
                foreach (uint id in e.Anims)
                {
                    MpAnimEvents ev;
                    if (!Char.Events.TryGetValue(id, out ev)) continue;
                    foreach (MpAnimEvents.Int32Event i in ev.Ints)
                        if (i.Value == type && !string.IsNullOrEmpty(i.Locator)) return i.Locator;
                }

            return "LCTR_INTROMOUTH";
        }

        // ------------------------------------------------------------------ the beam (CPlasmaProjectile)

        private bool _beamOn;
        private int _beamState;              // 1 attack (widening), 2 sustain, 3 release, 4 done
        private float _expT, _shutT, _beamLen, _firingT, _beamAngle, _pulseY, _contactPulseT, _pendingDamage, _beamPerTick, _rumbleT;
        private Transform _mouth;
        private string _mouthName;
        private Vector3 _beamFrom, _beamDir, _beamEnd;
        private bool _beamOnSamus;
        private MpEffects.Effect _contact;
        private GameObject _beamGo;
        private Mesh _beamMesh;
        public int BeamsFired, BeamTicksOnSamus;
        public float BeamDamageDealt;

        private void BeamOn(string locator)
        {
            _mouthName = locator;
            _mouth = base.Anim != null ? base.Anim.Bone(locator) : null;
            if (_mouth == null && base.Anim != null) _mouth = base.Anim.Bone("Head_1");
            Vector3 p = BeamTarget();
            _targetPos = _lookPos = _predicted = _startPlayerPos = p;
            _firingT = 0f;
            _beamPerTick = BeamDamage.Damage * (LocoForHealth() == Combat ? 1.25f : 1f);
            if (_beamOn && _beamState <= 2) return;
            _beamOn = true;
            _beamState = 1;
            _expT = 0f;
            _shutT = 0f;
            _beamLen = 0f;
            _pendingDamage = 0f;
            BeamsFired++;
            if (_contact != null) _contact.Kill();
            _contact = MpEffects.Spawn(BeamContactFx, BonePos("Head_1"), MpEffects.GameOrientation(Quaternion.identity), true);
            if (_contact != null && _contact.Gen != null)
            {
                _contact.Gen.SetGlobalScale(new V3(4f, 4f, 4f));
                _contact.Gen.SetParticleEmission(false);
            }

            MpLog.Info("Parasite Queen: beam on from " + locator + (_mouth != null ? "" : " (NO BONE)") + " at " + p.ToString("0.0") + ", " + (_beamPerTick * 60f).ToString("0.0") + "/s");
        }

        /// <summary>ResetBeam(false): the beam narrows away (release), then shuts down.</summary>
        private void BeamOff()
        {
            if (!_beamOn || _beamState > 2) return;
            _beamState = 3;
            // the burn still owed (hits are merged around Samus's 0.5 s immunity)
            if (_pendingDamage > 0f && G != null && G.DamagePlayer(_pendingDamage, _beamFrom, true)) BeamDamageDealt += _pendingDamage;
            _pendingDamage = 0f;
            if (_contact != null && _contact.Gen != null) _contact.Gen.SetParticleEmission(false);
            MpLog.Info("Parasite Queen: beam off after " + _firingT.ToString("0.00") + " s, on Samus " + BeamTicksOnSamus + " frames, damage " + BeamDamageDealt.ToString("0.0"));
        }

        /// <summary>ResetBeam(true): gone at once.</summary>
        private void BeamKill()
        {
            _beamOn = false;
            _beamState = 0;
            if (_contact != null) { _contact.Finish(); _contact = null; }
            if (_beamGo != null) _beamGo.SetActive(false);
        }

        private Vector3 GameAxis(Transform t, Vector3 local)
        {
            Vector3 d = MpSpace.ToGame(t.TransformPoint(local)) - MpSpace.ToGame(t.position);
            return d.sqrMagnitude > 1e-8f ? d.normalized : Facing;
        }

        /// <summary>CNewIntroBoss::Think's beam part + CPlasmaProjectile::UpdateFx / CBeamProjectile::UpdateFx.</summary>
        private void UpdateBeam(float dt)
        {
            if (!_beamOn) return;
            _firingT += dt;
            Vector3 from = _mouth != null ? MpSpace.ToGame(_mouth.position) : BonePos("Head_1");
            Vector3 fwd = _mouth != null ? GameAxis(_mouth, Vector3.up) : Facing;
            Vector3 dir = fwd;
            if (!Dead)
            {
                float w = Mathf.Min(1f, _firingT / 1.5f);
                Vector3 to = _lookPos + w * (_startPlayerPos - _lookPos) - from;
                if (to.sqrMagnitude > 1e-6f) dir = Vector3.RotateTowards(fwd, to.normalized, 30f * Mathf.Deg2Rad, 0f);
            }

            switch (_beamState)
            {
                case 1:
                    if (_expT > 0.5f) _beamState = 2;
                    else _expT += dt * BeamExpansion;
                    break;
                case 3:
                    _expT += dt * BeamExpansion;
                    if (_expT > 1f) { _expT = 1f; _beamState = 4; }
                    break;
                case 4:
                    _shutT += dt;
                    if (_shutT > BeamShutdown) { BeamKill(); return; }
                    break;
            }

            float expansion = -4f * _expT * (_expT - 1f);
            bool damaging = _beamState == 1 || _beamState == 2;
            _beamLen = Mathf.Min(BeamLength, _beamLen + BeamTravel * dt);

            // the world (and the reactor's rings) stops it; Samus in the way first takes the burn
            float len = _beamLen;
            Vector3 normal = -dir;
            RaycastHit hit;
            bool world = BeamRay(MpSpace.ToUnity(from), MpSpace.DirToUnity(dir), len * MpSpace.Scale, out hit);
            if (world) { len = hit.distance / MpSpace.Scale; normal = MpSpace.DirToGame(hit.normal); }
            float onSamus = SamusAlong(from, dir, len);
            _beamOnSamus = onSamus >= 0f;
            if (_beamOnSamus) { len = onSamus; normal = -dir; }
            _beamFrom = from;
            _beamDir = dir;
            _beamEnd = from + dir * len;

            if (damaging && _beamOnSamus && G != null)
            {
                BeamTicksOnSamus++;
                _pendingDamage += _beamPerTick * 60f * dt;       // CDamageInfo::MakeScaledForTime
                if (G.DamagePlayer(_pendingDamage, from)) { BeamDamageDealt += _pendingDamage; _pendingDamage = 0f; }
            }

            // the contact system pulses on whatever the beam touches, every 1/16 s
            if (_contact != null && _contact.Gen != null)
            {
                _contactPulseT -= dt;
                bool pulse = (world || _beamOnSamus) && _beamState <= 3 && _contactPulseT <= 0f;
                if (pulse)
                {
                    _contact.SetPlace(_beamEnd + 0.001f * normal, LookAt(normal));
                    _contactPulseT = 1f / 16f;
                }

                _contact.Gen.SetParticleEmission(pulse);
            }

            _beamAngle += 720f * dt;
            if (_beamAngle > 360f) _beamAngle = 0f;
            _pulseY += dt * BeamPulseSpeed;
            if (_pulseY > 5f) _pulseY = 0f;
            Rumble(dt, damaging);
            DrawBeam(from, dir, len, expansion * BeamRadius);
        }

        private void Rumble(float dt, bool on)
        {
            _rumbleT -= dt;
            if (!on || _rumbleT > 0f || !MpXr.Present || MpSettings.Haptics <= 0f) return;
            _rumbleT = 0.1f;
            float amp = 0.35f * MpSettings.Haptics;
            MpXr.Pulse(true, amp, 0.12f);
            MpXr.Pulse(false, amp, 0.12f);
        }

        private static readonly RaycastHit[] BeamHits = new RaycastHit[24];

        /// <summary>
        /// CBeamProjectile::RayCollisionCheckWithWorld: the nearest thing that stops a shot - the area's shot collision (surfaces
        /// marked projectile pass-through, like the reactor's glass railing, let it by), platforms (the rings), doors; not the
        /// enemies' shot boxes.
        /// </summary>
        public static bool BeamRay(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(from, dir, BeamHits, len, (1 << 0) | (1 << MpAreaView.ShotOnlyLayer), QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = BeamHits[i];
                if (h.distance >= bd || MpSpace.Root == null || !h.collider.transform.IsChildOf(MpSpace.Root)) continue;
                if (h.collider.name == "Collision (walk)") continue;
                if (h.collider.GetComponentInParent<MpHitTarget>() != null) continue;
                MpDoorShotGate gate = h.collider.GetComponent<MpDoorShotGate>();
                if (gate != null && !gate.Blocks) continue;
                best = h;
                bd = h.distance;
                any = true;
            }

            return any;
        }

        /// <summary>Unity-free: where along the beam (game units) it enters Samus's capsule, or -1 (CBeamProjectile's ray against her bounds).</summary>
        private static float SamusAlong(Vector3 from, Vector3 dir, float len)
        {
            if (G == null || G.State == null || G.State.Energy <= 0f) return -1f;
            Vector3 feet = PlayerPos;
            float r = MpEnemyShots.PlayerRadius, h = MpEnemyShots.PlayerHeight;
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / (r * 0.5f)), 1, 400);
            for (int k = 0; k <= steps; k++)
            {
                float t = len * k / steps;
                Vector3 p = from + dir * t;
                if (p.z < feet.z || p.z > feet.z + h) continue;
                if (new Vector2(p.x - feet.x, p.y - feet.y).sqrMagnitude <= r * r) return t;
            }

            return -1f;
        }

        private static M3 LookAt(Vector3 fwd)
        {
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.up;
            Vector3 up = Mathf.Abs(fwd.z) > 0.99f ? Vector3.up : Vector3.forward;
            Vector3 right = Vector3.Cross(fwd, up).normalized;
            up = Vector3.Cross(right, fwd);
            return new M3 { C0 = new V3(right.x, right.y, right.z), C1 = new V3(fwd.x, fwd.y, fwd.z), C2 = new V3(up.x, up.y, up.z) };
        }

        // ------------------------------------------------------------------ the beam's look (CPlasmaProjectile::Render / RenderBeam)

        private static Material[] _beamMats;
        private readonly List<Vector3> _bv = new List<Vector3>();
        private readonly List<Color> _bc = new List<Color>();
        private readonly List<Vector2> _bu = new List<Vector2>();
        private readonly List<int>[] _bt = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

        private Material[] BeamMaterials()
        {
            if (_beamMats != null && _beamMats[0] != null) return _beamMats;
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return null;
            Texture2D beam = MpTextures.Get(BeamTexture), glow = MpTextures.Get(BeamGlowTexture);
            _beamMats = new[]
            {
                BeamMat(carrier, MpTextures.White, true),       // 3 sides, white 0.3, untextured, src alpha + one
                BeamMat(carrier, beam, false),                  // 4 sides, inner colour, beam texture, additive
                BeamMat(carrier, beam, false),                  // 8 sides, outer colour, beam texture, additive
                BeamMat(carrier, glow, true),                   // 6 sides, outer colour, glow texture, src alpha + one
            };
            return _beamMats;
        }

        private static Material BeamMat(Material carrier, Texture tex, bool srcAlpha)
        {
            var m = new Material(carrier) { name = "Mp queen beam" };
            m.SetTexture("_MainTex", tex != null ? tex : MpTextures.White);
            m.SetFloat("_SrcBlend", (float)(srcAlpha ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            m.SetFloat("_Cull", 0);
            m.SetFloat("_Billboard", 0);
            m.renderQueue = MpEffects.Queue;
            return m;
        }

        private void DrawBeam(Vector3 from, Vector3 dir, float len, float width)
        {
            Material[] mats = BeamMaterials();
            if (mats == null || _root == null) return;
            if (_beamGo == null)
            {
                _beamGo = new GameObject("ParasiteQueen beam");
                _beamGo.transform.SetParent(_root, false);
                _beamMesh = new Mesh { name = "Mp queen beam" };
                _beamMesh.MarkDynamic();
                _beamGo.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
                var mr = _beamGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            if (!_beamGo.activeSelf) _beamGo.SetActive(true);
            _beamGo.transform.localPosition = from;
            _beamGo.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            _bv.Clear(); _bc.Clear(); _bu.Clear();
            foreach (List<int> t in _bt) t.Clear();
            float uv0 = -(0.0625f * _pulseY);
            Tube(0, 3, 0.25f * width, new Color(1f, 1f, 1f, 0.3f), 0f, len, uv0, false);
            Tube(1, 4, 0.5f * width, BeamInner, _beamAngle, len, uv0, false);
            Tube(2, 8, width, BeamOuter, -_beamAngle, len, uv0, false);
            Tube(3, 6, 1.25f * width, BeamOuter, 0f, len, uv0, true);
            _beamMesh.Clear();
            _beamMesh.SetVertices(_bv);
            _beamMesh.SetColors(_bc);
            _beamMesh.SetUVs(0, _bu);
            _beamMesh.subMeshCount = 4;
            for (int i = 0; i < 4; i++) _beamMesh.SetTriangles(_bt[i], i, false);
            _beamMesh.bounds = new Bounds(new Vector3(0f, len * 0.5f, 0f), new Vector3(4f, len + 4f, 4f));
        }

        /// <summary>One RenderBeam strip: a tube of <paramref name="sides"/> around the beam's Y axis, turned by <paramref name="spin"/> degrees.</summary>
        private void Tube(int sub, int sides, float width, Color c, float spin, float len, float uv0, bool glow)
        {
            int start = _bv.Count;
            bool flip = false;
            float step = 2f * Mathf.PI / sides, a0 = spin * Mathf.Deg2Rad;
            for (int i = 0; i <= sides; i++)
            {
                float a = a0 + step * i, x = Mathf.Cos(a), z = Mathf.Sin(a);
                float u = glow ? 0.5f * Mathf.Sin(step * i) : (flip ? width : 0f);
                flip = !flip;
                var p = new Vector3(width * x, 0f, width * z);
                _bv.Add(p);
                _bv.Add(p + new Vector3(0f, len, 0f));
                _bc.Add(c); _bc.Add(c);
                _bu.Add(new Vector2(u, uv0));
                _bu.Add(new Vector2(u, 2f));
            }

            List<int> t = _bt[sub];
            for (int i = 0; i < sides; i++)
            {
                int k = start + i * 2;
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
                t.Add(k + 2); t.Add(k + 1); t.Add(k + 3);
            }
        }

        // ------------------------------------------------------------------ head tracking (CBoneTracking on Head_1: 80 degrees, 180 a second)

        private Quaternion _track = Quaternion.identity;

        private void TrackHead(float dt)
        {
            if (_headBone == null) return;
            Vector3 fwd = (_headBone.TransformPoint(Vector3.up) - _headBone.position).normalized;
            Vector3 want = (MpSpace.ToUnity(_targetPos + new Vector3(0f, 0f, 10f)) - _headBone.position).normalized;
            Quaternion goal = Quaternion.FromToRotation(fwd, Vector3.RotateTowards(fwd, want, 80f * Mathf.Deg2Rad, 0f));
            _track = Quaternion.RotateTowards(_track, goal, 180f * dt);
            _headBone.rotation = _track * _headBone.rotation;
        }

        // ------------------------------------------------------------------ frame, damage, death

        private float _deathT = -1f, _deathDur;
        private bool _deadSent;
        public float DeathAnimLength { get { return _deathDur; } }

        public override void Think(float dt)
        {
            if (Gone || Body == null) return;
            if (Dead)
            {
                if (base.Anim != null) base.Anim.Tick(dt);
                PlaceParts();
                UpdateBeam(dt);
                _deathT += dt;
                // CPatterned::Dead: the die animation, then a 3 s fade, then DeathDelete sends Dead
                if (!_deadSent && _deathT >= _deathDur + 3f)
                {
                    _deadSent = true;
                    MpLog.Info("Parasite Queen: Dead (" + _deathT.ToString("0.0") + " s after the killing blow)");
                    Send(MpScriptMsg.StDead);
                    Remove();
                }

                return;
            }

            base.Think(dt);
            if (!Active || Gone || Dead) return;
            if (_frozenT <= 0f) TrackHead(dt);
            PlaceParts();
            UpdateBeam(dt);
            if (G != null && G.Hud != null) G.Hud.SetBoss(true, BossName, BarMax > 0f ? Mathf.Clamp01(HealthNow / BarMax) : 0f);
        }

        /// <summary>Co-op: the boss bar's maximum scales with the health.</summary>
        public override void ScaleHealth(float scale)
        {
            float old = MaxHealth;
            base.ScaleHealth(scale);
            if (old > 0f) BarMax = BarMax * MaxHealth / old;
        }

        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            return HitPart(_head, weapon, charged, damage);
        }

        private bool HitPart(Part part, int weapon, bool charged, float damage)
        {
            if (!Active || Dead || Gone) return false;
            MpVulnerability v = part != null ? part.Vuln : Info.Vuln;
            float d = v != null ? v.Damage(weapon, charged, damage) : damage;
            if (d <= 0f)
            {
                if (G != null) G.OnWeaponIneffective(this);
                return true;
            }

            float before = HealthNow;
            HealthNow -= d;
            _hurtT = 0.33f;
            Send(MpScriptMsg.StDamage);
            MpLog.Info("Parasite Queen: hit on " + (part != null ? part.Bone : "body") + " by weapon " + weapon + (charged ? " charged" : "") + " for " + d.ToString("0.0") + " -> " + HealthNow.ToString("0.0") + "/" + MaxHealth);
            if (HealthNow <= 0f) Die(before - HealthNow);
            return true;
        }

        /// <summary>CNewIntroBoss::Think at no health: the beam is cut, the body back at its start, Death(DeathRattle); the death animation plays.</summary>
        public override void Die(float lastBlow)
        {
            if (Dead) return;
            Dead = true;
            HealthNow = 0f;
            Live.Remove(this);
            _deathT = 0f;
            BeamKill();
            StopLoop();
            _turning = false;
            Pos = _homePos;
            Yaw = _homeYaw;
            ApplyYaw();
            foreach (Part p in new[] { _head, _pelvis }) if (p != null && p.Box != null) p.Box.enabled = false;
            if (G != null && G.Hud != null) G.Hud.SetBoss(false, null, 0f);
            MpLog.Info("Parasite Queen: DeathRattle (last blow " + lastBlow.ToString("0.0") + ")");
            Send(StDeathRattle);
            int a = AnimNo("CINE_Death_introboss", 14);
            if (base.Anim != null && PlayAnim(a, false, 0.2f)) _deathDur = base.Anim.Duration;
            else _deathDur = 1f;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            bool was = Active;
            if (msg == MpScriptMsg.Action) MpLog.Info("Parasite Queen: Action from " + from);
            base.OnMessage(msg, from);
            if (Active == was) return;
            _attackT = 8f;
            MpLog.Info("Parasite Queen: " + (Active ? "activated" : "deactivated") + " by " + from);
            if (Active && !Dead) { _again = false; Enter(State.Patrol); }
            if (!Active)
            {
                BeamKill();
                StopLoop();
                if (G != null && G.Hud != null && !Dead) G.Hud.SetBoss(false, null, 0f);
            }
        }

        public override void OnRemoved()
        {
            BeamKill();
            StopLoop();
            if (_beamGo != null) Object.Destroy(_beamGo);
            if (_beamMesh != null) Object.Destroy(_beamMesh);
            base.OnRemoved();
        }

        /// <summary>For the lab: state, stage, health, attack timer, beam.</summary>
        public string Report()
        {
            return "state " + _state + " prog " + _prog + " stage " + _loco + " hp " + HealthNow.ToString("0.0") + "/" + MaxHealth + " attackT " + _attackT.ToString("0.0")
                + " turning " + _turning + " yaw " + Yaw.ToString("0") + " anim " + (base.Anim != null ? base.Anim.Anim + " t " + base.Anim.Time.ToString("0.00") + "/" + base.Anim.Duration.ToString("0.00") : "-")
                + " beam " + (_beamOn ? "on state " + _beamState + " from " + _beamFrom.ToString("0.0") + " dir " + _beamDir.ToString("0.00") + " end " + _beamEnd.ToString("0.0") + " on Samus " + _beamOnSamus : "off")
                + " active " + Active + " dead " + Dead + " gone " + Gone;
        }

        /// <summary>For the lab: where the head and pelvis boxes are (game space).</summary>
        public Vector3 HeadPoint { get { return _head != null && _head.Box != null ? MpSpace.ToGame(_head.Box.transform.position) : BonePos("Head_1"); } }
        public Vector3 PelvisPoint { get { return _pelvis != null && _pelvis.Box != null ? MpSpace.ToGame(_pelvis.Box.transform.position) : Pos; } }
    }
}
