using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The shared start of the Tallon Overworld / Chozo Ruins enemies: the script object's head (with the flavor word for the
    /// types whose loader reads one), CPatternedInfo, CActorParameters; the home point, a state clock, the time alive, the
    /// animation picked by name with a numbered fallback, a body that falls when a flier dies.
    /// </summary>
    public abstract class MpRuinsEnemy : MpEnemy
    {
        public uint Flavor;
        public Vector3 Home;
        public float AliveFor;
        protected float _stateT;
        protected float _fallV = -1f;

        /// <summary>ScriptLoader: name, [flavor], editor transform, scale, CPatternedInfo, CActorParameters; the reader is left after them.</summary>
        protected static MpReader ReadStart(MpScriptObject o, MpRuinsEnemy e, bool flavor)
        {
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            e.Head = new MpScriptRead.Head { Name = r.CString() };
            if (flavor) e.Flavor = r.U32();
            e.Head.Pos = MpVec3.Read(r);
            e.Head.Rot = MpVec3.Read(r);
            e.Head.Scale = MpVec3.Read(r);
            e.Info = MpPatternedInfo.Read(r);
            e.Params = MpScriptRead.ReadActorParams(r);
            return r;
        }

        protected static MpEntity Finish(MpRuinsEnemy e, MpAreaView view, string kind)
        {
            e.Build(view, kind + " " + e.Head.Name);
            e.Home = e.Pos;
            e.SetActive(e.Info.Active);
            return e;
        }

        /// <summary>The first animation whose name holds one of the words, else <paramref name="fallback"/>.</summary>
        protected int Pick(int fallback, params string[] words)
        {
            int a = FindAnim(words);
            return a >= 0 ? a : (Anim != null && Anim.Has(fallback) ? fallback : -1);
        }

        protected void Loop(int anim) { if (anim >= 0 && Anim != null && (Anim.Anim != anim || !Anim.Loop)) PlayAnim(anim, true, 0.2f, false); }

        protected void Once(int anim) { if (anim >= 0) PlayAnim(anim, false, 0.1f); }

        protected bool AnimDone { get { return Anim == null || Anim.Done; } }

        protected float AttackWait()
        {
            float avg = Info.AverageAttackTime > 0f ? Info.AverageAttackTime : 2f;
            return Mathf.Max(0.5f, avg + Random.Range(-1f, 1f) * Info.AttackTimeVariation);
        }

        public override void Think(float dt)
        {
            if (Active && !Dead && !Gone) AliveFor += dt;
            if (Dead && _fallV >= 0f && Body != null && !Gone)
            {
                // a flier's body drops to the floor (CWarWasp / CFireFlea death fall)
                _fallV += 25f * dt;
                float ground;
                Vector3 next = Pos - new Vector3(0f, 0f, _fallV * dt);
                if (GroundAt(Pos, out ground) && next.z <= ground) { next.z = ground; _fallV = 0f; }
                Pos = next;
            }

            base.Think(dt);
        }
    }

    /// <summary>
    /// CWarWasp (ScriptLoader::LoadWarWasp, 13 properties), its state machine in short:
    /// - Generate: a generated wasp (inactive at load) bursts out of the hive with its initial animation, turned up to 22.5 degrees off.
    /// - Telegraph / circle burst (the Hive Totem's wasps: a CloseIn -> Follow link names the burst waypoint): the wasps orbit the
    ///   burst point at their minimum attack range, sideways around it (teams above / below each other), speeding up for 0.8 s of
    ///   every 2.8 s; when a wasp is within 10 degrees of its burst angle off the waypoint's facing, its attack time is up, the
    ///   melee slot is free and Samus stands within 9.5 units of the burst point, it dives through the point (the "circle burst");
    ///   the sting hurts once per dive (the WARTAIL locator in Samus's box; here: the body within 1.5 units).
    /// - Otherwise (no burst point): one wasp at a time (CTeamAiMgr melee slot) dashes at Samus's aim point and 7.5 units past it;
    ///   the others hold at their close-in point in front of her. Flavor Two wasps fire their projectile instead, when facing her.
    /// - The attack clock runs slower while Samus looks away from the wasp (down to a third); the next attack comes after the
    ///   average attack time plus a random part of the variation, halved while the melee slot is free.
    /// - Dead: it is hurled down and drops to the floor (CPatterned flyer death).
    /// </summary>
    public sealed class MpWarWasp : MpRuinsEnemy
    {
        public bool Collider;
        public MpDamageInfo Touch, ShotDamage;
        public uint Weapon = 0xFFFFFFFF;

        private enum S { Generate, Patrol, Telegraph, Burst, CloseIn, Attack, Projectile, Hurt }
        private S _s = S.Patrol;
        private int _aFly, _aReady, _aStab, _aHurt, _aDeath, _aOut, _aBurst;
        private float _attackT, _speedMul = 1f, _offAngle = 45f;
        private bool _stung, _hasBurst, _generated;
        private int _team;
        private Vector3 _burstPos, _burstDir = Vector3.up, _burstRight = Vector3.right, _dashTo;

        /// <summary>CTeamAiMgr's melee slot (one attacker at a time), per room.</summary>
        private static readonly Dictionary<int, MpWarWasp> MeleeSlot = new Dictionary<int, MpWarWasp>();
        public static int LabBursts, LabDashes, LabStings, LabShots;

        private static MpWarWasp Read(MpScriptObject o)
        {
            if (o.PropCount != 13) return null;
            var e = new MpWarWasp();
            MpReader r = ReadStart(o, e, true);
            e.Collider = r.Bool();
            e.Touch = MpDamageInfo.Read(r);
            e.Weapon = r.U32();
            e.ShotDamage = MpDamageInfo.Read(r);
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpWarWasp e = Read(o);
            if (e == null) return;
            MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
            if (MpAssets.Valid(e.Weapon)) MpEnemyShots.PrepareWeapon(assets, e.Weapon);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpWarWasp e = Read(o);
            return e != null ? Finish(e, view, "War Wasp") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _aFly = Pick(3, "B_flightloop_");
            _aReady = Pick(5, "B_ready_");
            _aStab = Pick(19, "A_stab", "straightburst", "B_burst");
            _aBurst = Pick(_aStab, "straightburst", "burst", "A_stab");
            _aHurt = Pick(11, "R_impact1", "R_damage");
            _aDeath = Pick(10, "R_death1_", "intodeath");
            _aOut = Info.Anim.Animation;
            _generated = !Info.Active;
            _attackT = CalcTimeToNextAttack();
            _offAngle = OffTotemAngle();
        }

        /// <summary>CWarWasp::SetUpCircleBurstWaypoint: the waypoint its CloseIn -> Follow link names (position, facing, right).</summary>
        public override void OnLoaded()
        {
            base.OnLoaded();
            FindBurstPoint();
        }

        private void FindBurstPoint()
        {
            if (_hasBurst || Links == null || World == null) return;
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != 11 || l.Message != MpScriptMsg.Follow) continue;
                var w = World.Find(l.Target) as MpWaypointEntity;
                if (w == null) continue;
                _hasBurst = true;
                _burstPos = w.GamePos;
                _burstDir = w.Rot * Vector3.up;
                _burstRight = w.Rot * Vector3.right;
                break;
            }
        }

        public override void SetActive(bool on)
        {
            bool was = Active;
            base.SetActive(on);
            if (!on || was) return;
            FindBurstPoint();
            if (_generated && _aOut >= 0)
            {
                // CWarWasp::Generate: out of the hive, turned a random -22.5..22.5 degrees
                Yaw += Random.Range(-22.5f, 22.5f);
                ApplyYaw();
                PlayAnim(_aOut, false, 0f);
                Enter(S.Generate);
            }
        }

        /// <summary>CWarWasp::CalcOffTotemAngle (GameCube PAL): 0..80 degrees off the burst waypoint's facing.</summary>
        private static float OffTotemAngle() { return Random.Range(0f, 80f); }

        private float CalcTimeToNextAttack()
        {
            float m = SlotFree ? 0.5f : 1f;
            return m * (Info.AttackTimeVariation * Random.value + Mathf.Max(0.5f, Info.AverageAttackTime));
        }

        private float Cruise { get { return 7f * Mathf.Max(0.5f, Info.Speed) * _speedMul; } }

        private bool SlotFree
        {
            get
            {
                MpWarWasp w;
                if (!MeleeSlot.TryGetValue(Area, out w) || w == null || w == this) return true;
                return w.Dead || w.Gone || !w.Active || (w._s != S.Attack && w._s != S.Burst);
            }
        }

        private void TakeSlot() { MeleeSlot[Area] = this; }

        private void FreeSlot() { MpWarWasp w; if (MeleeSlot.TryGetValue(Area, out w) && w == this) MeleeSlot.Remove(Area); }

        protected override void Behave(float dt)
        {
            _stateT += dt;
            // CWarWasp::Think: the attack clock runs slower while Samus faces away (1 - angle / pi * 0.666)
            Camera head = MpPlayer.Head();
            Vector3 look = head != null ? MpSpace.DirToGame(head.transform.forward) : Vector3.up;
            if (_attackT > 0f)
            {
                float mul = 1f;
                if (head != null && G != null && !G.Ball.Morphed)
                {
                    Vector3 to = Pos - PlayerPos;
                    float ang = Vector2.Angle(new Vector2(look.x, look.y), new Vector2(to.x, to.y)) * Mathf.Deg2Rad;
                    mul = 1f - ang / Mathf.PI * 0.666f;
                }

                _attackT -= dt * mul;
            }

            Vector3 aim = PlayerChest - new Vector3(0f, 0f, 0.35f);
            bool sees = CanSeePlayer(false) || _generated;
            switch (_s)
            {
                case S.Generate:
                    FlyTowards(Pos + Facing * 3f + new Vector3(0f, 0f, 0.5f), 4f, dt);
                    if (AnimDone || _stateT > 3f) Enter(_hasBurst ? S.Telegraph : S.CloseIn);
                    break;
                case S.Patrol:
                    Loop(_aReady >= 0 ? _aReady : _aFly);
                    FlyTowards(Home + new Vector3(0f, 0f, Mathf.Sin(AliveFor * 2f) * 0.3f), 2f, dt);
                    if (sees) Enter(_hasBurst ? S.Telegraph : S.CloseIn);
                    break;
                case S.Telegraph:
                {
                    Loop(_aFly);
                    // UpdateTelegraphMoveSpeed: 2 s at speed, then 0.8 s rising from 0.7x to 2x
                    float cyc = Mathf.Repeat(_stateT, 2.8f);
                    _speedMul = cyc < 2f ? 1f : Mathf.Lerp(0.7f, 2f, (cyc - 2f) / 0.8f);
                    Vector3 from = Pos - _burstPos;
                    from.z = 0f;
                    Vector3 dir = from.sqrMagnitude > 1e-4f ? from.normalized : -_burstDir;
                    Vector3 side = Vector3.Cross(dir, Vector3.forward);
                    if (_team > 0) side = -side;
                    float stratum = _team == 0 ? 0f : ((_team & 1) == 1 ? -3f - 3f * (_team >> 1) : 3f * (_team >> 1));
                    Vector3 seek = _burstPos + dir * Mathf.Max(3f, Info.MinAttackRange) + side * 5f + new Vector3(0f, 0f, stratum + 1f);
                    float seekMag = _team == 0 ? 0.4f : _team == 1 ? 0.6f : 1f;
                    FlyTowards(seek, Cruise * seekMag * 2f, dt);
                    TurnTowards(Pos + side, dt);
                    // ShouldSpecialAttack: the attack time is up, Samus is by the burst point, the wasp is at its burst angle
                    Vector3 pd = PlayerPos - _burstPos;
                    Vector3 threshold = Vector3.Slerp(_burstRight, _burstDir, Mathf.Clamp01(_offAngle / 90f));
                    threshold.z = 0f;
                    if (_attackT <= 0f && SlotFree && new Vector2(pd.x, pd.y).sqrMagnitude < 90.25f && Vector3.Angle(threshold, from) < 10f)
                    {
                        TakeSlot();
                        Once(_aBurst);
                        _stung = false;
                        LabBursts++;
                        Enter(S.Burst);
                    }

                    break;
                }

                case S.Burst:
                {
                    // the circle burst: a melee dive aimed through the burst point (Samus's spot), the sting live all the way
                    _speedMul = 1f;
                    Vector3 target = _burstPos + new Vector3(0f, 0f, 1.2f);
                    if (Vector3.Dot(Facing, target - Pos) > 0f) TurnTowards(target, dt, 720f);
                    float left = FlyTowards(target + Facing * 6f, Cruise * 2.6f, dt);
                    Sting();
                    if (left < 0.4f || _stateT > 1.8f) { FreeSlot(); _attackT = CalcTimeToNextAttack(); _offAngle = OffTotemAngle(); _team = (_team + 1) % 3; Enter(S.Telegraph); }
                    break;
                }

                case S.CloseIn:
                {
                    Loop(_aFly);
                    // GetCloseInPos: half way between the attack ranges in front of Samus, at her eye height
                    Vector3 fwd = new Vector3(look.x, look.y, 0f);
                    fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.up;
                    float mid = 0.5f * (Info.MinAttackRange + Info.MaxAttackRange);
                    Vector3 spot = aim + fwd * mid;
                    spot.z = PlayerPos.z + MpSpace.SamusEyeHeight;
                    FlyTowards(spot, Cruise, dt);
                    TurnTowards(PlayerPos, dt);
                    if (Flavor == 2)
                    {
                        float facing = Vector3.Dot(Facing, (aim - Pos).normalized);
                        if (_attackT <= 0f && facing >= 0.906f && MpAssets.Valid(Weapon)) Enter(S.Projectile);
                    }
                    else if (_attackT <= 0f && SlotFree && sees && LineOfSight(Pos, aim))
                    {
                        TakeSlot();
                        _dashTo = aim + (aim - Pos).normalized * 7.5f;
                        _stung = false;
                        Once(_aStab);
                        LabDashes++;
                        Enter(S.Attack);
                    }

                    if (!sees && _stateT > 8f && DistToPlayer > Info.DetectionRange * 1.2f) Enter(S.Patrol);
                    break;
                }

                case S.Attack:
                {
                    TurnTowards(_dashTo, dt, 360f);
                    float rest = FlyTowards(_dashTo, Cruise * 2.2f, dt);
                    Sting();
                    if (rest < 0.4f || _stateT > 2f) { FreeSlot(); _attackT = CalcTimeToNextAttack(); Enter(S.CloseIn); }
                    break;
                }

                case S.Projectile:
                    if (_stateT - dt <= 0f)
                    {
                        Vector3 from = Pos + new Vector3(0f, 0f, 0.4f);
                        MpEnemyShots.Fire(Weapon, from, (PlayerChest - from).normalized, ShotDamage.Damage, this);
                        LabShots++;
                    }

                    if (_stateT > 0.8f) { _attackT = CalcTimeToNextAttack(); Enter(S.CloseIn); }
                    break;
                case S.Hurt:
                    if (AnimDone || _stateT > 0.5f) Enter(_hasBurst ? S.Telegraph : S.CloseIn);
                    break;
            }
        }

        /// <summary>CWarWasp::ApplyDamage: once per attack, the stinger in Samus's box hurts with the touch damage.</summary>
        private void Sting()
        {
            if (_stung || G == null) return;
            Vector3 d = PlayerChest - Pos;
            if (new Vector2(d.x, d.y).magnitude < 1.5f && Mathf.Abs(d.z) < 1.8f && G.DamagePlayer(Touch.Damage > 0f ? Touch.Damage : Info.Contact.Damage, Pos)) { _stung = true; LabStings++; }
        }

        /// <summary>The wasp hurts only with its sting.</summary>
        protected override void ContactDamage(float dt) { }

        private void Enter(S s) { _s = s; _stateT = 0f; }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            if (HealthNow > 0f && _s != S.Burst && _s != S.Attack && _s != S.Generate) { Once(_aHurt); Enter(S.Hurt); }
        }

        public override void Die(float lastBlow)
        {
            FreeSlot();
            base.Die(lastBlow);
        }

        protected override void PlayDeathAnim()
        {
            if (_aDeath >= 0) PlayAnim(_aDeath, false, 0.1f);
            _fallV = 0f;
        }
    }

    /// <summary>
    /// CBeetle (ScriptLoader::LoadBeetle, 16 properties): waits under the ground until its script wakes it (Activate), digs out,
    /// then runs at Samus and headbutts her (contact damage plus the touch damage), jumps back and comes again. Its plating turns
    /// shots from the front with the plating vulnerability; the body's own vulnerability counts from the sides and back.
    /// </summary>
    public sealed class MpBeetle : MpRuinsEnemy
    {
        public MpDamageInfo Touch;
        public MpVulnerability Tail, Plating;
        public int Entrance;
        public float InitialAttackDelay, RetreatTime;

        private enum S { Underground, DigOut, Chase, Attack, Back, Hurt }
        private S _s = S.DigOut;
        private int _aDig, _aRun, _aWalk, _aReady, _aButt, _aBack, _aHurt, _aDeath;
        private float _attackT;
        private bool _hitThisAttack;

        private static MpBeetle Read(MpScriptObject o)
        {
            if (o.PropCount != 16) return null;
            var e = new MpBeetle();
            MpReader r = ReadStart(o, e, true);
            e.Touch = MpDamageInfo.Read(r);
            MpVec3.Read(r);
            r.F32();
            e.Tail = MpScriptRead.ReadVulnerability(r);
            e.Plating = MpScriptRead.ReadVulnerability(r);
            r.U32();
            e.Entrance = r.S32();
            e.InitialAttackDelay = r.F32();
            e.RetreatTime = r.F32();
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpBeetle e = Read(o);
            if (e != null) MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpBeetle e = Read(o);
            return e != null ? Finish(e, view, "Beetle") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _aDig = Info.Anim.Animation;
            _aRun = Pick(21, "B_run_");
            _aWalk = Pick(25, "B_walk_");
            _aReady = Pick(20, "B_ready_");
            _aButt = Pick(1, "A_headbutt", "A_bigheadbutt");
            _aBack = Pick(13, "jumpbackward");
            _aHurt = Pick(30, "R_impact1");
            _aDeath = Pick(29, "R_death");
            _attackT = InitialAttackDelay;
            _s = Entrance == 0 && _aDig >= 0 ? S.DigOut : S.Chase;
        }

        public override void SetActive(bool on)
        {
            bool was = Active;
            base.SetActive(on);
            if (on && !was && _s == S.DigOut && _aDig >= 0) { PlayAnim(_aDig, false, 0f); _stateT = 0f; }
        }

        private float RunSpeed { get { return 6f * Mathf.Max(0.5f, Info.Speed); } }

        protected override void Behave(float dt)
        {
            _stateT += dt;
            _attackT -= dt;
            Vector3 p = PlayerPos;
            switch (_s)
            {
                case S.DigOut:
                    if (AnimDone || _stateT > 3f) Enter(S.Chase);
                    break;
                case S.Chase:
                {
                    float d = DistToPlayer;
                    TurnTowards(p, dt);
                    if (d > 2.2f + Info.HalfExtent) { Loop(d > 6f ? _aRun : _aWalk); WalkTowards(p, d > 6f ? RunSpeed : RunSpeed * 0.5f, dt); }
                    else Loop(_aReady);
                    if (_attackT <= 0f && d < Mathf.Max(3.5f, Info.MinAttackRange + 2.5f)) { Once(_aButt); _hitThisAttack = false; Enter(S.Attack); }
                    break;
                }

                case S.Attack:
                    // the headbutt lunges a couple of units; touching Samus in it hurts with the touch damage
                    if (_stateT < 0.5f) WalkTowards(Pos + Facing * 3f, RunSpeed * 1.2f, dt);
                    if (!_hitThisAttack && DistToPlayer < 1.6f + Info.HalfExtent && G != null && G.DamagePlayer(Mathf.Max(Touch.Damage, Info.Contact.Damage), Pos)) _hitThisAttack = true;
                    if (AnimDone || _stateT > 1.4f) { Once(_aBack); Enter(S.Back); }
                    break;
                case S.Back:
                    if (_stateT < 0.5f) WalkTowards(Pos - Facing * 4f, RunSpeed, dt);
                    if (AnimDone || _stateT > Mathf.Max(0.8f, RetreatTime)) { _attackT = AttackWait(); Enter(S.Chase); }
                    break;
                case S.Hurt:
                    if (AnimDone || _stateT > 0.6f) Enter(S.Chase);
                    break;
            }
        }

        private void Enter(S s) { _s = s; _stateT = 0f; }

        /// <summary>CBeetle::GetDamageVulnerability: the plating takes shots from in front, the body (patterned) from the sides and back.</summary>
        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            Vector3 toShooter = PlayerPos - Pos;
            toShooter.z = 0f;
            bool front = Plating != null && toShooter.sqrMagnitude > 0.01f && Vector3.Angle(Facing, toShooter) < 60f;
            MpVulnerability keep = Vuln;
            if (front) Vuln = Plating;
            bool r = base.TakeDamage(weapon, charged, damage);
            Vuln = keep;
            return r;
        }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            if (HealthNow > 0f && _s == S.Chase) { Once(_aHurt); Enter(S.Hurt); }
        }

        protected override void PlayDeathAnim() { if (_aDeath >= 0) PlayAnim(_aDeath, false, 0.1f); }
    }

    /// <summary>
    /// CEyeBall (ScriptLoader::LoadEyeBall, 21 properties): a wall eye, closed until Samus is in its detection range; open, it
    /// turns to her and after its attack delay fires its weapon at her in short bursts; shot, it flinches and its attack waits.
    /// </summary>
    public sealed class MpEyeBall : MpRuinsEnemy
    {
        public float AttackDelay, AttackStartTime;
        public uint Weapon = 0xFFFFFFFF;
        public MpDamageInfo Beam;
        public bool AttackDisabled;

        private enum S { Closed, Opening, Watch, Fire }
        private S _s = S.Closed;
        private int _aClosed, _aOpen, _aActive, _aHurt;
        private float _fireT;

        private static MpEyeBall Read(MpScriptObject o)
        {
            if (o.PropCount != 21) return null;
            var e = new MpEyeBall();
            MpReader r = ReadStart(o, e, true);
            e.AttackDelay = r.F32();
            e.AttackStartTime = r.F32();
            e.Weapon = r.U32();
            e.Beam = MpDamageInfo.Read(r);
            for (int i = 0; i < 4; i++) r.U32();
            for (int i = 0; i < 5; i++) r.U32();
            e.AttackDisabled = r.Bool();
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpEyeBall e = Read(o);
            if (e == null) return;
            MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
            if (MpAssets.Valid(e.Weapon)) MpEnemyShots.PrepareWeapon(assets, e.Weapon);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpEyeBall e = Read(o);
            return e != null ? Finish(e, view, "Eyeball") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _aClosed = Pick(2, "B_closed");
            _aOpen = Pick(4, "B_intoactive_");
            _aActive = Pick(1, "B_active_");
            _aHurt = Pick(5, "R_impact");
            Loop(_aClosed);
        }

        protected override void Behave(float dt)
        {
            _stateT += dt;
            bool near = DistToPlayer < Mathf.Max(8f, Info.DetectionRange * 0.35f) && CanSeePlayer(false);
            switch (_s)
            {
                case S.Closed:
                    Loop(_aClosed);
                    if (near) { Once(_aOpen); Enter(S.Opening); }
                    break;
                case S.Opening:
                    if (AnimDone || _stateT > 1.5f) { Enter(S.Watch); _fireT = Mathf.Max(0.5f, AttackStartTime); }
                    break;
                case S.Watch:
                    Loop(_aActive);
                    TurnTowards(PlayerPos, dt, 90f);
                    if (!near && _stateT > 3f) { Once(_aOpen); Enter(S.Closed); break; }
                    _fireT -= dt;
                    if (_fireT <= 0f && !AttackDisabled) Enter(S.Fire);
                    break;
                case S.Fire:
                    TurnTowards(PlayerPos, dt, 60f);
                    _fireT -= dt;
                    if (_fireT <= 0f)
                    {
                        _fireT = 0.35f;
                        Vector3 from = Pos + new Vector3(0f, 0f, Info.Height * 0.5f) + Facing * 0.8f;
                        Vector3 dir = (PlayerChest - from).normalized;
                        if (MpAssets.Valid(Weapon)) MpEnemyShots.Fire(Weapon, from, dir, Beam.Damage, this);
                        else if (LineOfSight(from, PlayerChest) && G != null) G.DamagePlayer(Beam.Damage, from);
                    }

                    if (_stateT > 2f) { Enter(S.Watch); _fireT = Mathf.Max(1f, AttackDelay); }
                    break;
            }
        }

        private void Enter(S s) { _s = s; _stateT = 0f; }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            if (HealthNow <= 0f) return;
            Once(_aHurt);
            if (_s == S.Closed) { Enter(S.Opening); }
            else { Enter(S.Watch); _fireT = Mathf.Max(1f, AttackDelay); }
        }
    }

    /// <summary>CFireFlea (ScriptLoader::LoadFireFlea, 9 properties): a glowing flier drifting about its spot; it bumps into Samus (contact damage) when she is close.</summary>
    public sealed class MpFireFlea : MpRuinsEnemy
    {
        private int _aFly, _aHurt, _aDeath;
        private Vector3 _goal;

        private static MpFireFlea Read(MpScriptObject o)
        {
            if (o.PropCount != 9) return null;
            var e = new MpFireFlea();
            ReadStart(o, e, false);
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpFireFlea e = Read(o);
            if (e != null) MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpFireFlea e = Read(o);
            return e != null ? Finish(e, view, "Fire Flea") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _aFly = Pick(0, "B_flightloop");
            _aHurt = Pick(3, "R_damage");
            _aDeath = Pick(4, "R_death1");
            Loop(_aFly);
            _goal = Pos;
        }

        protected override void Behave(float dt)
        {
            _stateT += dt;
            Loop(_aFly);
            bool chase = DistToPlayer < Mathf.Max(4f, Info.MinAttackRange) && CanSeePlayer(false);
            if (chase) { FlyTowards(PlayerChest, 3f * Mathf.Max(0.5f, Info.Speed), dt); TurnTowards(PlayerPos, dt); return; }
            if (_stateT > 2.5f || (_goal - Pos).magnitude < 0.3f)
            {
                _stateT = 0f;
                _goal = Home + new Vector3(Random.Range(-3f, 3f), Random.Range(-3f, 3f), Random.Range(-1f, 1.5f));
            }

            FlyTowards(_goal, 1.5f * Mathf.Max(0.5f, Info.Speed), dt);
            TurnTowards(_goal, dt);
        }

        protected override void OnHurt(int weapon, bool charged, float damage) { if (HealthNow > 0f) Once(_aHurt); }

        protected override void PlayDeathAnim() { if (_aDeath >= 0) PlayAnim(_aDeath, false, 0.1f); _fallV = 0f; }
    }

    /// <summary>
    /// The Geemer (ScriptLoader::LoadGeemer: a CParasite wall walker, 16 properties): crawls slowly about its spot, stops and
    /// crouches when shot (its halt / get-up sounds), touching it hurts. The Dreamport keeps it to the floor it starts on.
    /// </summary>
    public sealed class MpGeemer : MpRuinsEnemy
    {
        public int HaltSfx = -1, GetUpSfx = -1, CrouchSfx = -1;
        public float HaltDelay;
        private int _aWalk, _aActive, _aStun;
        private Vector3 _goal;

        private static MpGeemer Read(MpScriptObject o)
        {
            if (o.PropCount != 16) return null;
            var e = new MpGeemer();
            MpReader r = ReadStart(o, e, false);
            r.F32(); r.F32(); r.F32(); r.F32(); r.F32();
            e.HaltDelay = r.F32();
            r.F32();
            e.HaltSfx = (int)(r.U32() & 0xFFFF);
            e.GetUpSfx = (int)(r.U32() & 0xFFFF);
            e.CrouchSfx = (int)(r.U32() & 0xFFFF);
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpGeemer e = Read(o);
            if (e != null) MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpGeemer e = Read(o);
            return e != null ? Finish(e, view, "Geemer") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _aWalk = Pick(2, "walk_geemer");
            _aActive = Pick(1, "walkActive");
            _aStun = Pick(0, "stunIdle");
            _goal = Pos;
        }

        protected override void Behave(float dt)
        {
            _stateT -= dt;
            if (_stateT > 0f) { Loop(_aStun); return; }
            bool alert = DistToPlayer < Mathf.Max(3f, Info.DetectionRange) && CanSeePlayer(false);
            Loop(alert ? _aActive : _aWalk);
            if ((_goal - Pos).magnitude < 0.3f || Random.value < dt * 0.2f)
                _goal = Home + new Vector3(Random.Range(-4f, 4f), Random.Range(-4f, 4f), 0f);
            if (WalkTowards(_goal, (alert ? 1.6f : 0.8f) * Mathf.Max(0.5f, Info.Speed), dt) > 0.05f) TurnTowards(_goal, dt);
        }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            if (HealthNow <= 0f) return;
            if (_stateT <= 0f && HaltSfx > 0) PlayDataSfx(HaltSfx);
            _stateT = Mathf.Max(1f, HaltDelay);
        }
    }

    /// <summary>
    /// CAmbientAI (ScriptLoader::LoadAmbientAI, 16 properties): a harmless animal of the scenery. It plays its idle animation,
    /// its alert animation while Samus is within the alert range and its impact animation when shot; it has health and dies.
    /// </summary>
    public sealed class MpAmbientAI : MpRuinsEnemy
    {
        public float AlertRange, ImpactRange;
        public int AlertAnim = -1, ImpactAnim = -1;
        private int _idle;

        private static MpAmbientAI Read(MpScriptObject o)
        {
            if (o.PropCount != 16) return null;
            var e = new MpAmbientAI();
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            e.Head = MpScriptRead.ReadHead(r, true);
            MpVec3.Read(r);
            MpVec3.Read(r);
            r.F32();
            r.U32();
            var info = new MpPatternedInfo { Health = r.F32(), KnockbackResistance = r.F32() };
            info.Vuln = MpScriptRead.ReadVulnerability(r);
            info.Anim = MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
            e.Params = MpScriptRead.ReadActorParams(r);
            e.AlertRange = r.F32();
            e.ImpactRange = r.F32();
            e.AlertAnim = r.S32();
            e.ImpactAnim = r.S32();
            info.Active = r.Bool();
            info.HalfExtent = 0.5f;
            info.Height = 1f;
            e.Info = info;
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpAmbientAI e = Read(o);
            if (e != null) MpEnemyFactory.CollectCharacter(assets, e.Info.Anim.Ancs, e.Info.Anim.Character, tex);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpAmbientAI e = Read(o);
            return e != null ? Finish(e, view, "Ambient") : null;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _idle = Info.Anim.Animation;
            if (_idle >= 0) Loop(_idle);
        }

        protected override void Behave(float dt)
        {
            _stateT -= dt;
            if (_stateT > 0f) return;
            bool alert = AlertAnim >= 0 && DistToPlayer < AlertRange;
            Loop(alert ? AlertAnim : _idle);
        }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            if (ImpactAnim >= 0 && HealthNow > 0f) { Once(ImpactAnim); _stateT = 0.8f; }
        }

        protected override void ContactDamage(float dt) { }
    }
}
