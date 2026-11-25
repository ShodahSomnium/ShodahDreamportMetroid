using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Space Pirate (decomp CSpacePirate + ScriptLoader::LoadSpacePirate; the game drives it with an AFSM state machine, here a
    /// simplified one in the same spirit). Its script data (CSpacePirateData) decides the kind:
    ///   seated (flag 0x1000, the frigate's "TYPE 01 Injured" pirates): lies hurt, sits up when alerted and shoots seated bursts;
    ///   only-attack-in-range (0x20, the injured "Type 02 / 03" walkers): limps its patrol path, turns and shoots while Samus is
    ///     inside its leash radius;
    ///   ceiling ambush (0x2): hangs from the ceiling until a trigger activates it, drops and fights;
    ///   the rest: idles or walks its patrol waypoints (Patrol -> Follow link; Patrol state sent), notices Samus by sight
    ///     (detection range / height / cone and a line of sight), hearing (her moving or firing inside the hearing radius) or
    ///     being shot, alerts (alert animation and sound, the Attack state once), then fights: faces her, keeps its distance (runs
    ///     in when she is far or out of sight), strafes aside by its dodge chance, backs off when hurt by its cover chance,
    ///     slashes with the arm blade when she is close (its blade damage; no melee with flag 0x200).
    /// Shooting (CSpacePirate::UpdateAttacks / FireProjectile / CBurstFire): every average attack time (+ variation) a burst from
    /// the game's tables (standard, injured, seated, frenzied at low health; the out-of-view tables when Samus is not looking);
    /// each shot is the pirate's own WPSC from its R_gun_LCTR with the script's damage and sound, aimed at Samus with the burst's
    /// aim error, only when she is in front and in sight. Hits flinch it (light / heavy reaction animations); death plays its own
    /// death animation (seated, injured, knocked into the air by a heavy blow) and death sound; Dead / DeathRattle / Damage go to
    /// the links. Script messages: Activate (when already active: alert), Alert, Action (attack now), Start / Stop (shooting on /
    /// off), SetToZero (may retreat), Deactivate. The eye glow (OneEye / TwoEyes) rides the eye locator while alive.
    ///
    /// Left out: the additive aim and recoil layers, cover points (none on the frigate), jumps, wall hangs, cloaking, the rag
    /// doll (the death animation plays in place), the team AI and the AFSM's finer timing.
    /// </summary>
    public sealed class MpSpacePirate : MpEnemy
    {
        private enum Mode { Idle, Patrol, Hang, Drop, Fall, Land, Alert, SitUp, Combat, Flinch, Melee }

        private static MpBurstFire.Burst B(int w, params int[] a) { return MpBurstFire.B(w, 0.1f, 0.05f, a); }

        private static readonly MpBurstFire.Burst[][] Bursts =
        {
            new[] { B(20, 3, 4, 5), B(20, 2, 3, 4), B(20, 6, 5, 4), B(20, 1, 2, 3), B(20, 7, 6, 5) },                                         // quick
            new[] { B(15, 5, 3, 2, 1), B(20, 1, 2, 3, 4), B(20, 7, 6, 5, 4), B(15, 3, 4, 5, 6), B(15, 6, 5, 4, 3), B(15, 2, 3, 4, 5) },     // standard
            new[] { B(40, 1, 2, 3, 4, 5, 6), B(40, 7, 6, 5, 4, 3, 2), B(10, 2, 3, 4, 5, 4, 3), B(10, 6, 5, 4, 3, 4, 5) },                    // frenzied
            new[] { B(20, 16, 4), B(40, 5, 7), B(40, 1, 10) },                                                                               // jumping
            new[] { B(15, 16, 1, 3), B(20, 3, 4, 6), B(25, 7, 5, 4), B(25, 2, 6, 4), B(15, 7, 5, 3) },                                      // injured
            new[] { B(35, 7, 13), B(35, 9, 1), B(30, 16, 12) },                                                                              // seated
            new[] { B(10, 16, 15, 13), B(20, 13, 12, 10), B(30, 9, 11, 12), B(30, 14, 10, 12), B(10, 9, 11, 13) },
            new[] { B(26, 16, 8, 11, 14), B(26, 16, 13, 11, 12), B(16, 9, 11, 13, 10), B(16, 14, 13, 12, 11), B(8, 10, 11, 12, 13), B(8, 6, 8, 11, 13) },
            new[] { B(40, 1, 16, 14, 12, 10, 11), B(40, 9, 11, 12, 13, 11, 7), B(10, 8, 10, 11, 12, 13, 12), B(10, 15, 13, 12, 10, 12, 9) },
            new[] { B(40, 7, 13), B(40, 9, 1), B(20, 16, 12) },
            new[] { B(30, 9, 11, 13), B(10, 13, 12, 10), B(15, 9, 11, 12), B(15, 14, 10, 12), B(30, 16, 15, 13) },
            new[] { B(35, 7, 13), B(35, 9, 1), B(30, 16, 12) },
        };

        public MpPirateData P;
        public static int ShotsFired, BladeHits;
        private Mode _mode = Mode.Idle;
        private float _modeT;
        private bool _started, _alerted, _sentAttack, _stopped, _retreatOk, _satUp, _bladeDone, _aggressive;
        private MpBurstFire _burst;
        private int _burstBase = 1;
        private float _attackRem = 1f, _strafeDelay = 1.5f, _flinchCool, _meleeCool, _losT, _noLosT, _floorZ, _fallV, _wpWait;
        private bool _los = true;
        private Vector3 _moveDir;
        private float _moveT, _moveSpeed;
        private MpWaypointEntity _wp;
        private int _lastPlayerShots = -1;
        private Vector3 _lastPlayer;
        private float _lastBlow;
        private readonly List<KeyValuePair<MpEffects.Effect, string>> _eyes = new List<KeyValuePair<MpEffects.Effect, string>>();

        private int _aIdle = -1, _aReady = -1, _aWalk = -1, _aRun = -1, _aStrafeL = -1, _aStrafeR = -1, _aBack = -1, _aMelee = -1,
            _aLight = -1, _aHeavy = -1, _aHang = -1, _aOutHang = -1, _aAir = -1, _aLand = -1, _aInto = -1, _aActive = -1;
        private readonly List<int> _aAlerts = new List<int>();
        private int _after = -1;

        public bool Seated { get { return P.Has(MpPirateData.Seated); } }
        public bool InRangeOnly { get { return P.Has(MpPirateData.OnlyAttackInRange); } }
        public bool Ceiling { get { return P.Has(MpPirateData.CeilingAmbush); } }
        public bool Injured { get { return InRangeOnly && !Seated; } }
        public bool CanMelee { get { return !P.Has(MpPirateData.NoMeleeAttack) && !InRangeOnly && !Seated && _aMelee >= 0; } }
        public string ModeName { get { return _mode.ToString(); } }

        // ------------------------------------------------------------------ loading

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpScriptRead.Head h;
            MpPatternedInfo info;
            MpActorParams ap;
            MpReader r = MpEnemyFactory.ReadPatterned(o, out h, out info, out ap);
            MpPirateData p = MpPirateData.Read(r, o.PropCount);
            if (info.Anim.Character == 0) info.Anim.Character = 2;
            MpEnemyFactory.CollectPatterned(assets, info, tex);
            MpEnemyShots.PrepareWeapon(assets, p.Projectile);
            MpCharacter c = MpCharacter.Get(info.Anim.Ancs, info.Anim.Character);
            if (c != null && c.Info != null)
                foreach (MpAncs.Effect fx in c.Info.Effects)
                    foreach (MpAncs.EffectComponent comp in fx.Components)
                        if (comp.Type == "PART") MpEnemyShots.PreparePart(assets, comp.Id);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            var e = new MpSpacePirate();
            MpReader r = MpEnemyFactory.ReadPatterned(o, out e.Head, out e.Info, out e.Params);
            e.P = MpPirateData.Read(r, o.PropCount);
            if (e.Info.Anim.Character == 0) e.Info.Anim.Character = 2;      // ScriptLoader: character 0 is invalid for a pirate
            e.Build(view, "SpacePirate " + o.Name);
            if (e.ShotBox != null)
            {
                // CPatterned's box: half extent around the origin, the height up, not scaled by the model
                float he = e.Info.HalfExtent > 0f ? e.Info.HalfExtent : 1f, ht = e.Info.Height > 0f ? e.Info.Height : 2.5f;
                e.ShotBox.size = new Vector3(he * 2f, he * 2f, ht);
                e.ShotBox.transform.localPosition = new Vector3(e.Info.BodyOrigin.X, e.Info.BodyOrigin.Y, e.Info.BodyOrigin.Z + ht * 0.5f);
            }

            e._burst = new MpBurstFire(Bursts, e.P.FirstBurstCount, (int)o.Id);
            e._burstBase = e.InRangeOnly ? 4 : 1;
            e._attackRem = 1f;
            e.SetActive(e.Info.Active);
            return e;
        }

        private void ResolveAnims()
        {
            _aReady = A("B_ready_pirate", "B_activeReady", "B_seatedActiveReady_", "B_Ready");
            _aWalk = Injured ? A("B_walk1_injured") : A("B_walk_pirate", "S_walk_combat");
            _aRun = A("B_runloop_pirate", "S_run_type4", "B_walk_pirate");
            _aStrafeL = A("B_strafleft");
            _aStrafeR = A("B_strafright");
            _aBack = A("B_backward_pirate");
            _aMelee = A("A_crabstrike_pirate");
            _aLight = A("R_lightf");
            _aHeavy = A("R_heavyhitf");
            _aHang = A("B_ceilinghang");
            _aOutHang = A("B_outof_ceilinghang", "S_ambush_fulljump");
            _aAir = A("S_ambush_jumpairloop", "B_jumpairloop");
            _aLand = A("S_ambush_outofjump", "B_outofjump");
            _aInto = Seated ? A("B_intoseatedActiveReady") : A("B_intoactiveReady", "B_outofwalk1_injured");
            _aActive = Seated ? A("B_seatedActiveReady_") : _aReady;
            foreach (string n in new[] { "B_alert1", "B_alert2", "B_alert3", "B_taunt" })
            {
                int a = A(n);
                if (a >= 0) _aAlerts.Add(a);
            }

            int init = Info.Anim.Animation;
            string name = init >= 0 && Char != null ? NameOf(init) : null;
            _aIdle = name != null && name.IndexOf("additive", System.StringComparison.OrdinalIgnoreCase) < 0 ? init : A("B_rready", "B_look", "B_ready");
        }

        private int A(params string[] names) { return FindAnim(names); }

        private string NameOf(int anim)
        {
            foreach (var kv in Char.Info.AnimNames) if (kv.Key == anim) return kv.Value;
            return null;
        }

        // ------------------------------------------------------------------ messages

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Activate:
                case MpScriptMsg.Alert:
                    if (Active && !Dead) { Alert(); return; }
                    if (msg == MpScriptMsg.Alert) return;
                    break;
                case MpScriptMsg.Action:
                    if (Active && !Dead) { _attackRem = 0f; Alert(); }
                    return;
                case MpScriptMsg.SetToZero:
                    if (Active) _retreatOk = true;
                    return;
                case MpScriptMsg.Start:
                    _stopped = false;
                    return;
                case MpScriptMsg.Stop:
                    _stopped = true;
                    return;
                case 34:            // Deleted
                    StopEyes();
                    break;
            }

            base.OnMessage(msg, from);
        }

        public override void SetActive(bool on)
        {
            base.SetActive(on);
            if (!on) StopEyes();
        }

        // ------------------------------------------------------------------ think

        protected override void Behave(float dt)
        {
            if (!_started) Begin();
            _modeT += dt;
            _flinchCool -= dt;
            _meleeCool -= dt;
            _strafeDelay -= dt;
            Senses(dt);
            switch (_mode)
            {
                case Mode.Idle: break;
                case Mode.Patrol: Patrol(dt); break;
                case Mode.Hang: break;
                case Mode.Drop:
                    TurnTowards(PlayerPos, dt);
                    if (AnimOver(1.2f)) { _mode = Mode.Fall; _modeT = 0f; _fallV = 0f; PlayAnim(_aAir, true, 0.1f); }
                    break;
                case Mode.Fall:
                {
                    _fallV += 40f * dt;
                    Vector3 p = Pos;
                    p.z -= _fallV * dt;
                    if (p.z <= _floorZ) { p.z = _floorZ; Pos = p; _mode = Mode.Land; _modeT = 0f; PlayAnim(_aLand, false, 0.05f); PlayDataSfx(P.SfxImpact, 40f); }
                    else Pos = p;
                    break;
                }

                case Mode.Land:
                    if (AnimOver(1f)) EnterCombat();
                    break;
                case Mode.Alert:
                    TurnTowards(PlayerPos, dt);
                    if (AnimOver(1.6f)) EnterCombat();
                    break;
                case Mode.SitUp:
                    if (!Seated) TurnTowards(PlayerPos, dt);
                    if (AnimOver(1.5f)) { _satUp = true; EnterCombat(); }
                    break;
                case Mode.Combat: Combat(dt); break;
                case Mode.Flinch:
                    if (AnimOver(0.8f)) EnterCombat();
                    break;
                case Mode.Melee: Melee(dt); break;
            }

            UpdateAttacks(dt);
            UpdateEyes();
        }

        private void Begin()
        {
            _started = true;
            ResolveAnims();
            _lastPlayer = PlayerPos;
            _lastPlayerShots = G != null && G.Shots != null ? G.Shots.Fired : 0;
            if (Ceiling)
            {
                _mode = Mode.Hang;
                if (_aHang >= 0) PlayAnim(_aHang, true, 0f);
            }
            else
            {
                float z;
                if (GroundAt(Pos, out z)) Pos = new Vector3(Pos.x, Pos.y, z);
                _wp = FirstWaypoint();
                if (_wp != null && !Seated) StartPatrol();
                else if (_aIdle >= 0) PlayAnim(_aIdle, true, 0.1f);
            }

            StartEyes();
        }

        private bool AnimOver(float cap)
        {
            return Anim == null || Anim.Done || _modeT > Mathf.Max(cap, Anim.Duration + 0.05f);
        }

        // ------------------------------------------------------------------ noticing Samus

        private void Senses(float dt)
        {
            Vector3 pp = PlayerPos;
            bool moved = (pp - _lastPlayer).sqrMagnitude > 0.0025f;
            _lastPlayer = pp;
            int shots = G != null && G.Shots != null ? G.Shots.Fired : 0;
            bool fired = _lastPlayerShots >= 0 && shots != _lastPlayerShots;
            _lastPlayerShots = shots;
            if (_alerted) return;
            if (G == null || G.State == null || G.State.Energy <= 0f) return;
            bool see = Info.DetectionRange > 0f && CanSeePlayer();
            bool hear = P.HearingRadius > 0f && (moved || fired) && (pp - Pos).sqrMagnitude < P.HearingRadius * P.HearingRadius;
            if (see || hear) Alert();
        }

        /// <summary>Noticed Samus (sight, hearing, a hit, a script): the kind's own way into the fight; the Attack state goes out once.</summary>
        public void Alert()
        {
            if (_alerted || Dead) return;
            if (!_started) Begin();
            _alerted = true;
            _modeT = 0f;
            if (!_sentAttack) { _sentAttack = true; Send(MpScriptMsg.StAttack); }
            if (Ceiling && _mode == Mode.Hang)
            {
                // CSpacePirate::Generate (ceiling ambush): let go of the ceiling and drop to the floor below
                _floorZ = FloorBelow(Pos);
                _mode = Mode.Drop;
                if (_aOutHang >= 0) PlayAnim(_aOutHang, false, 0.1f);
                else { _mode = Mode.Fall; PlayAnim(_aAir, true, 0.1f); }
                return;
            }

            if (Seated || Injured)
            {
                _mode = Mode.SitUp;
                if (_aInto >= 0) PlayAnim(_aInto, false, 0.15f);
                else _modeT = 99f;
                PlayDataSfx(P.SfxAlert, 50f);
                return;
            }

            _mode = Mode.Alert;
            PlayDataSfx(P.SfxAlert, 50f);
            if (_aAlerts.Count > 0) PlayAnim(_aAlerts[Random.Range(0, _aAlerts.Count)], false, 0.15f);
            else _modeT = 99f;
        }

        private void EnterCombat()
        {
            _mode = Mode.Combat;
            _modeT = 0f;
            _moveT = 0f;
            _aggressive = CanMelee && Random.value * 100f < P.AggressionCheck;
            if (_aActive >= 0) PlayAnim(_aActive, true, 0.15f, false);
        }

        // ------------------------------------------------------------------ patrol

        private MpWaypointEntity FirstWaypoint()
        {
            if (Links == null || World == null) return null;
            foreach (MpScriptObject.Link l in Links)
                if (l.State == 13 && l.Message == MpScriptMsg.Follow)
                {
                    var w = World.Find(l.Target) as MpWaypointEntity;
                    if (w != null) return w;
                }

            return null;
        }

        private void StartPatrol()
        {
            _mode = Mode.Patrol;
            _modeT = 0f;
            Send(13);           // Patrol
            PlayAnim(_aWalk >= 0 ? _aWalk : _aIdle, true, 0.2f);
        }

        /// <summary>CPatterned::Patrol (simplified): walk to the waypoint, tell it Arrived, wait its pause, on to one of its next.</summary>
        private void Patrol(float dt)
        {
            if (_wp == null) { _mode = Mode.Idle; PlayAnim(_aIdle, true, 0.2f); return; }
            if (_wpWait > 0f)
            {
                _wpWait -= dt;
                if (_wpWait <= 0f) PlayAnim(_aWalk, true, 0.2f);
                return;
            }

            TurnTowards(_wp.GamePos, dt);
            float speed = (Injured ? 1.1f : 2.2f) * Mathf.Max(0.5f, Info.Speed);
            float left = WalkTowards(_wp.GamePos, speed, dt);
            if (left > 0.6f) return;
            MpWaypointEntity at = _wp;
            at.OnMessage(MpScriptMsg.Arrived, this);
            _wp = at.Next();
            if (at.Pause > 0f) { _wpWait = at.Pause; PlayAnim(_aIdle, true, 0.2f); }
        }

        // ------------------------------------------------------------------ fighting

        private void Combat(float dt)
        {
            Vector3 d = PlayerPos - Pos;
            float flat = new Vector2(d.x, d.y).magnitude;
            _losT -= dt;
            if (_losT <= 0f)
            {
                _losT = 0.25f;
                _los = LineOfSight(Pos + new Vector3(0f, 0f, 2f), PlayerChest);
            }

            _noLosT = _los ? 0f : _noLosT + dt;
            if (Seated) return;
            TurnTowards(PlayerPos, dt);
            if (Injured) return;

            if (CanMelee && _meleeCool <= 0f && flat < MeleeRange && Mathf.Abs(d.z) < 2f) { StartMelee(); return; }

            if (_moveT > 0f)
            {
                _moveT -= dt;
                Vector3 to = Pos + _moveDir * 2f;
                float before = (to - Pos).magnitude;
                if (WalkTowards(to, _moveSpeed, dt) >= before - 1e-4f) _moveT = 0f;     // blocked
                if (_moveT <= 0f) PlayAnim(_aActive, true, 0.2f);
                return;
            }

            float far = _aggressive ? MeleeRange * 0.8f : Mathf.Max(Info.MaxAttackRange, 10f);
            if (flat > far || _noLosT > 2.5f)
            {
                if (flat < 1.5f) { PlayAnim(_aActive, true, 0.2f, false); return; }
                PlayAnim(_aRun, true, 0.2f, false);
                WalkTowards(PlayerPos, 5.5f * Mathf.Max(0.5f, Info.Speed), dt);
                return;
            }

            if (_strafeDelay <= 0f)
            {
                _strafeDelay = Random.Range(2f, 4f);
                if (Random.value * 100f < P.DodgeCheck) { Strafe(); return; }
            }

            PlayAnim(_aActive, true, 0.2f, false);
        }

        private float MeleeRange { get { return Mathf.Max(3.5f, Info.MinAttackRange); } }

        /// <summary>CSpacePirate::GetStrafeDir (simplified): a side with room (no wall within 3 units, floor under it).</summary>
        private void Strafe()
        {
            Vector3 right = new Vector3(Facing.y, -Facing.x, 0f);
            Vector3 chest = Pos + new Vector3(0f, 0f, 1.2f);
            bool r = LineOfSight(chest, chest + right * 3f), l = LineOfSight(chest, chest - right * 3f);
            float z;
            r = r && GroundAt(Pos + right * 3f, out z);
            l = l && GroundAt(Pos - right * 3f, out z);
            if (r && l) { if (Random.value < 0.5f) r = false; else l = false; }
            if (!r && !l) return;
            _moveDir = r ? right : -right;
            _moveT = 0.9f;
            _moveSpeed = 3.5f;
            PlayAnim(r ? _aStrafeR : _aStrafeL, true, 0.12f);
        }

        private void FallBack()
        {
            Vector3 away = Pos - PlayerPos;
            away.z = 0f;
            if (away.sqrMagnitude < 1e-4f || _aBack < 0) return;
            away.Normalize();
            Vector3 chest = Pos + new Vector3(0f, 0f, 1.2f);
            float z;
            if (!LineOfSight(chest, chest + away * 3f) || !GroundAt(Pos + away * 3f, out z)) return;
            _moveDir = away;
            _moveT = 1.2f;
            _moveSpeed = 2.5f;
            PlayAnim(_aBack, true, 0.12f);
        }

        private void StartMelee()
        {
            _mode = Mode.Melee;
            _modeT = 0f;
            _bladeDone = false;
            PlayAnim(_aMelee, false, 0.1f);
        }

        /// <summary>CSpacePirate::Attack (melee) + CheckBlade: the blade swing hurts Samus once if she is at its reach in front.</summary>
        private void Melee(float dt)
        {
            TurnTowards(PlayerPos, dt);
            float f = Anim != null && Anim.Duration > 0f ? Anim.Time / Anim.Duration : 1f;
            if (!_bladeDone && f > 0.3f && f < 0.7f)
            {
                Vector3 d = PlayerPos - Pos;
                float flat = new Vector2(d.x, d.y).magnitude;
                if (flat < 3.2f && Mathf.Abs(d.z) < 2.5f && Vector2.Angle(new Vector2(Facing.x, Facing.y), new Vector2(d.x, d.y)) < 60f)
                {
                    _bladeDone = true;
                    if (G != null && G.DamagePlayer(P.BladeDamage.Damage, Pos)) BladeHits++;
                }
            }

            if (AnimOver(1.6f)) { _meleeCool = 2f; EnterCombat(); }
        }

        // ------------------------------------------------------------------ shooting

        /// <summary>CSpacePirate::UpdateAttacks: a burst every average attack time (+ variation); burst shots as CBurstFire says.</summary>
        private void UpdateAttacks(float dt)
        {
            if (!_alerted || _stopped || P.Has(MpPirateData.Melee) || _mode != Mode.Combat && _mode != Mode.Melee || (Seated && !_satUp)) return;
            if (_mode == Mode.Melee) return;
            if (InRangeOnly && Info.LeashRadius > 0f && (PlayerPos - Pos).sqrMagnitude > Info.LeashRadius * Info.LeashRadius) return;
            if (G == null || G.State == null || G.State.Energy <= 0f) return;
            _attackRem -= dt;
            if (_attackRem < 0f)
            {
                int type = _burstBase;
                if (ShouldFrenzy()) type = 2;
                if (Seated) type = 5;
                if (!MpGunTurret.SamusLooksAt(Pos + new Vector3(0f, 0f, 1.5f)) && type < 6) type += 6;
                _burst.BurstType = type;
                _burst.Start();
                _attackRem = Random.value * Info.AttackTimeVariation + Mathf.Max(0.2f, Info.AverageAttackTime);
            }

            _burst.Update(dt);
            if (_burst.ShouldFire)
            {
                FireProjectile();
                _burst.SetTimeToNextShot(P.NextShotTimeVariation * (Random.value - 0.5f) + P.AverageNextShotTime);
            }
        }

        private bool ShouldFrenzy()
        {
            return HealthNow < 0.3f * MaxHealth && Random.value < 0.25f;
        }

        public Vector3 GunPos
        {
            get
            {
                Transform b = Anim != null ? Anim.Bone("R_gun_LCTR") : null;
                return b != null ? MpSpace.ToGame(b.position) : Pos + Facing * 0.8f + new Vector3(0f, 0f, Seated ? 0.8f : 1.8f);
            }
        }

        /// <summary>CSpacePirate::FireProjectile: from the gun locator at Samus (in front, in sight), the burst's aim error added.</summary>
        private void FireProjectile()
        {
            Vector3 gun = GunPos, target = PlayerChest;
            Vector3 to = target - gun;
            float dist = to.magnitude;
            if (dist < 0.1f) return;
            float ang = Vector2.Angle(new Vector2(Facing.x, Facing.y), new Vector2(to.x, to.y));
            float lim = Seated ? 70f : dist < 6f ? 60f : 45f;
            if (ang > lim || !LineOfSight(gun, target)) return;
            float ex, ez;
            _burst.GetDistanceCompensatedError(dist, 6f, out ex, out ez);
            target += Body.localRotation * new Vector3(ex, 0f, ez);
            if (!MpEnemyShots.Fire(P.Projectile, gun, target - gun, P.ProjectileDamage.Damage, this)) return;
            ShotsFired++;
            PlayDataSfx(P.SfxProjectile, 60f);
        }

        // ------------------------------------------------------------------ hurt and death

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            _lastBlow = damage;
            bool was = _alerted;
            if (!was) { _attackRem = Mathf.Min(_attackRem, 0.5f); Alert(); }
            if (HealthNow - damage <= 0f || Seated || Injured || _mode == Mode.Melee || _mode == Mode.Fall || _mode == Mode.Drop || _mode == Mode.Hang) return;
            if (!was) return;
            if (_flinchCool <= 0f)
            {
                bool heavy = charged || damage >= 20f;
                int a = heavy && _aHeavy >= 0 ? _aHeavy : _aLight;
                if (a >= 0)
                {
                    _flinchCool = 1.2f;
                    _mode = Mode.Flinch;
                    _modeT = 0f;
                    _moveT = 0f;
                    PlayAnim(a, false, 0.06f);
                    return;
                }
            }

            if (_mode == Mode.Combat && _moveT <= 0f && (_retreatOk || Random.value * 100f < P.CoverCheck) && Random.value < 0.5f) FallBack();
        }

        public override void Die(float lastBlow)
        {
            _lastBlow = Mathf.Max(_lastBlow, lastBlow);
            StopEyes();
            base.Die(lastBlow);
        }

        protected override void PlayDeathAnim()
        {
            bool heavy = _lastBlow >= 20f;
            int a;
            if (Seated) a = A("R_deathSeated", "death");
            else if (Injured) a = heavy ? A("R_knockintoair", "R_death1_injured") : A("R_death1_injured", "death");
            else if (heavy) a = A("R_knockintoair_pirate", "R_backknockintoair", "R_death1_pirate");
            else a = Random.value < 0.5f ? A("R_death1_pirate", "death") : A("R_death2_pirate", "R_death1_pirate", "death");
            if (a >= 0) PlayAnim(a, false, 0.08f);
            PlayDataSfx(P.SfxDeath, 60f);
        }

        protected override float DeathLinger { get { return Anim != null ? Mathf.Clamp(Anim.Duration + 4f, 2f, 10f) : 2f; } }

        protected override void OnNamedEvent(string name, int value)
        {
            if (value == 16) Send(18);      // kUE_ScreenShake: the Play state (the frigate's camera shakers)
        }

        // ------------------------------------------------------------------ the floor below a ceiling pirate

        private float FloorBelow(Vector3 p)
        {
            RaycastHit hit;
            Vector3 from = MpSpace.ToUnity(p + new Vector3(0f, 0f, -0.4f));
            if (WorldRay(from, Vector3.down, 40f * MpSpace.Scale, out hit)) return MpSpace.ToGame(hit.point).z;
            return p.z - 6f;
        }

        // ------------------------------------------------------------------ the eye glow

        /// <summary>CSpacePirate::SetEyeParticleActive: TwoEyes (OneEye for the injured walkers) on the eye locator while alive.</summary>
        private void StartEyes()
        {
            if (Char == null || Char.Info == null || P.Has(MpPirateData.Trooper)) return;
            string want = !InRangeOnly || Seated ? (P.Has(MpPirateData.ShadowPirate) ? null : "TwoEyes") : "OneEye";
            if (want == null) return;
            foreach (MpAncs.Effect fx in Char.Info.Effects)
            {
                if (fx.Name != want) continue;
                foreach (MpAncs.EffectComponent c in fx.Components)
                {
                    if (c.Type != "PART" || !MpAssets.Valid(c.Id)) continue;
                    MpEffects.Effect e = MpEffects.Spawn(c.Id, Vector3.zero, MpEffects.GameOrientation(Quaternion.identity), true);
                    if (e != null) _eyes.Add(new KeyValuePair<MpEffects.Effect, string>(e, c.Bone));
                }
            }
        }

        private void UpdateEyes()
        {
            float s = Mathf.Abs(Head.Scale.X);
            foreach (var kv in _eyes)
            {
                Transform b = Anim != null && !string.IsNullOrEmpty(kv.Value) ? Anim.Bone(kv.Value) : null;
                Vector3 at = b != null ? MpSpace.ToGame(b.position) : Pos + new Vector3(0f, 0f, 2.5f);
                kv.Key.SetGlobal(at, MpEffects.GameOrientation(b != null ? b.rotation : Body.rotation), s);
            }
        }

        private void StopEyes()
        {
            foreach (var kv in _eyes) kv.Key.Finish();
            _eyes.Clear();
        }

        public override void OnRemoved()
        {
            StopEyes();
            base.OnRemoved();
        }
    }
}
