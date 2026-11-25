using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The frigate's Parasite (decomp CParasite, a CWallWalker of type kWT_Parasite, ScriptLoader::LoadParasite) run by its state
    /// machine (AFSM 29735BBA on this disc): Start -> Generate -> (patrol path) TargetPatrol -> Patrol, else PathFind -> Patrol.
    /// Patrol crawls over any surface (floor, wall, ceiling: the body keeps its up on the surface under it, climbs a wall ahead,
    /// tumbles over an edge) toward its waypoints with the flocking weights of its script object (seek, forward, cohesion,
    /// alignment, separation from other parasites, from Samus and from doors), pauses where a waypoint says, sends each waypoint
    /// Arrived, and deletes itself at the end of its path (Deactivate). With no surface near it falls (Jump) and lands on whatever
    /// it hits. Shot: it alerts the parasites near it (TelegraphAttack) and turns to Samus (TargetPlayer); within its max attack
    /// range it leaps at her (Attack: the game's 15-unit jump arc), hops back (Retreat) when it touched her - though every Parasite
    /// on this disc has a max attack range of 0, so in the frigate they never leap (as in the game: they scurry and bump).
    /// Touching Samus does its contact damage every damage-wait time (CParasite::Think). A movement-disabled parasite (the
    /// Emergency Evacuation Area's munching ones) stays put until Activate. ActorKeyframe animations play over the AI as the
    /// game's scripted body state does (a one-shot holds it until it ends). Death is the patterned one with the x-damage
    /// threshold of 0 the disc gives: DeathRattle, the death sound, MassiveDeath, Dead (the scripts' gib generators and death
    /// screams hang off these), gone at once.
    ///
    /// Left out: the PAS body-state database (animations are picked by name: the walk for its lurking locomotion, ready when
    /// still, the jump set, a knock-back), the bending additive animation, the ice zoomer / geemer / oculus variants.
    /// </summary>
    public sealed class MpParasite : MpEnemy
    {
        // ScriptLoader::LoadParasite's own fields
        public uint Flavor;
        public float MaxTelegraphReactDist, AdvanceWpRadius, AlignAngVel, StuckTimeThreshold, CollisionCloseMargin, SearchRadius;
        public float SeparationDist, SeparationWeight, AlignmentWeight, CohesionWeight, DestSeekWeight, ForwardMoveWeight;
        public float PlayerSeparationDist, PlayerSeparationWeight, PlayerObstructionMinDist;
        public bool DisableMove;

        public enum St { Generate, PathFind, Patrol, TargetPatrol, Jump, TelegraphAttack, Attack, TargetPlayer, Retreat, Deactivate }

        public St State = St.Generate;

        // lab counters (all parasites)
        public static int LabHits, LabDeaths, LabContacts;
        public static float LabDamage;

        private static readonly float AttackTime = 2f * Mathf.Sqrt(2.5f / MpCrawl.Gravity);
        private static readonly float AttackVelocity = 15f / AttackTime, RetreatVelocity = 3f / AttackTime;

        private Quaternion _rot = Quaternion.identity;
        private Vector3 _vel, _destPos, _targetPos, _home;
        private MpWaypointEntity _dest;
        private bool _started, _hadPath, _align, _hasSurface, _landed, _onGround = true, _attackOver = true, _hitByPlayer;
        private bool _receivedTelegraph, _obstructed, _scripted, _myLoop = true;
        private float _stateT, _stateRand, _pause, _tumble, _telegraphT, _knockT, _speedNow;
        private int _think, _myAnim = -1;
        private int _aReady = 3, _aWalk = 5, _aRun = 4, _aIntoJump = 0, _aJumpLoop = 1, _aOutOfJump = 2, _aKnock = 6;
        private float _runSpeed, _walkSpeed;
        private Vector3 _sepMove, _cohMove, _alignMove;
        private List<KeyValuePair<Vector3, float>> _doors;

        /// <summary>The collision sphere's radius (CWallWalker mColSphere: the patterned half extent): the body keeps this far off its surface.</summary>
        private float R { get { return Info != null && Info.HalfExtent > 0f ? Info.HalfExtent : 0.3f; } }

        private float ModelScale { get { return Head != null ? Mathf.Max(0.1f, Mathf.Abs(Head.Scale.Y)) : 1f; } }

        // ------------------------------------------------------------------ script object

        /// <summary>ScriptLoader::LoadParasite (25 properties): name, flavor, transform, scale, CPatternedInfo, CActorParameters, 17 floats, disable move.</summary>
        private static MpParasite Read(MpScriptObject o)
        {
            if (o.PropCount != 25) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var e = new MpParasite();
            e.Head = new MpScriptRead.Head { Name = r.CString() };
            e.Flavor = r.U32();
            e.Head.Pos = MpVec3.Read(r);
            e.Head.Rot = MpVec3.Read(r);
            e.Head.Scale = MpVec3.Read(r);
            e.Info = MpPatternedInfo.Read(r);
            e.Params = MpScriptRead.ReadActorParams(r);
            e.MaxTelegraphReactDist = r.F32();
            e.AdvanceWpRadius = r.F32();
            r.F32();
            e.AlignAngVel = r.F32();
            r.F32();
            e.StuckTimeThreshold = r.F32();
            e.CollisionCloseMargin = r.F32();
            e.SearchRadius = r.F32();
            e.SeparationDist = r.F32();
            e.SeparationWeight = r.F32();
            e.AlignmentWeight = r.F32();
            e.CohesionWeight = r.F32();
            e.DestSeekWeight = r.F32();
            e.ForwardMoveWeight = r.F32();
            e.PlayerSeparationDist = r.F32();
            e.PlayerSeparationWeight = r.F32();
            e.PlayerObstructionMinDist = r.F32();
            e.DisableMove = r.Bool();
            return e;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpParasite e = Read(o);
            if (e != null) MpEnemyFactory.CollectPatterned(assets, e.Info, tex);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpParasite e = Read(o);
            if (e == null) return null;
            e.Build(view, "Parasite " + o.Name);
            e.SetActive(e.Info.Active);
            return e;
        }

        public override void Build(MpAreaView view, string name)
        {
            base.Build(view, name);
            _rot = MpSpace.GameRotation(new Vector3(Head.Rot.X, Head.Rot.Y, Head.Rot.Z));
            Body.localRotation = _rot;
            float s = ModelScale;
            if (ShotBox != null)
            {
                ShotBox.transform.localPosition = new Vector3(0f, -0.25f, -0.08f) * s;
                ShotBox.size = new Vector3(1f, 1.3f, 0.6f) * s;
            }

            if (Char != null)
            {
                _aReady = Pick("B_ready_parasite", 3);
                _aWalk = Pick("B_walk_parasite", 5);
                _aRun = Pick("B_run_parasite", 4);
                _aIntoJump = Pick("B_intojumploop_parasite", 0);
                _aJumpLoop = Pick("B_jumploop_parasite", 1);
                _aOutOfJump = Pick("B_outofjumploop_parasite", 2);
                _aKnock = Pick("R_knockinto_parasite", 6);
                _runSpeed = MpCrawl.RootSpeed(Char, _aRun);
                _walkSpeed = MpCrawl.RootSpeed(Char, _aWalk);
            }

            if (_runSpeed <= 0.05f) _runSpeed = 1.2f;
            if (_walkSpeed <= 0.05f) _walkSpeed = 0.6f;
            if (Anim != null) { _myAnim = Anim.Anim; _myLoop = Anim.Loop; }
        }

        private int Pick(string animName, int fallback)
        {
            int n = Char.AnimNumber(animName);
            return n >= 0 ? n : fallback;
        }

        /// <summary>A line for the lab: where it is, its state, its animation.</summary>
        public string Report()
        {
            return Name + " " + State + (DisableMove ? " (move disabled)" : "") + (Active ? "" : " (inactive)") + " at " + Pos.ToString("0.00") + " up " + MpCrawl.Up(_rot).ToString("0.00")
                + (_onGround ? (_hasSurface ? " on its surface" : " looking for a surface") : " airborne") + " hp " + HealthNow.ToString("0.#") + " anim " + (Anim != null ? Anim.Anim + "" : "-") + (_scripted ? " (scripted)" : "") + " speed " + _speedNow.ToString("0.00")
                + " path " + (_dest != null ? _dest.Id.ToString("X8") : (_hadPath ? "over" : "none")) + " run root " + _runSpeed.ToString("0.00") + " walk root " + _walkSpeed.ToString("0.00");
        }

        // ------------------------------------------------------------------ messages

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Activate) DisableMove = false;     // CParasite::AcceptScriptMsg (kSM_Activate): free to move, lurking
            base.OnMessage(msg, from);
        }

        public override bool TakeDamage(int weapon, bool charged, float damage)
        {
            bool took = base.TakeDamage(weapon, charged, damage);
            if (took) LabHits++;
            return took;
        }

        protected override void OnHurt(int weapon, bool charged, float damage)
        {
            _hitByPlayer = true;
            if (HealthNow > 0f && Anim != null && PlayAnim(_aKnock, false, 0.05f))
            {
                _myAnim = _aKnock;
                _myLoop = false;
                _knockT = Mathf.Max(0.2f, Anim.Duration);
            }
        }

        // ------------------------------------------------------------------ the state machine

        protected override void Behave(float dt)
        {
            if (!_started) Begin();
            _think++;
            _stateT += dt;
            WatchScripted();
            _knockT = Mathf.Max(0f, _knockT - dt);

            // CParasite::Think: far from Samus and out of her sight it does nothing
            if (_think % 10 == 1)
                _obstructed = PlayerObstructionMinDist > 0f && DistToPlayer > PlayerObstructionMinDist && !LineOfSight(PlayerChest, Pos + MpCrawl.Up(_rot) * 0.2f);
            if (_obstructed) return;
            if (!DisableMove && _telegraphT > 0f) _telegraphT -= dt;

            _landed = false;
            Step(dt);
            if (Dead || Gone) return;
            Body.localRotation = _rot;
            Vector3 f = MpCrawl.Fwd(_rot);
            if (new Vector2(f.x, f.y).sqrMagnitude > 1e-4f) Yaw = Mathf.Atan2(-f.x, f.y) * Mathf.Rad2Deg;
        }

        private void Begin()
        {
            _started = true;
            Vector3 head = new Vector3(Head.Pos.X, Head.Pos.Y, Head.Pos.Z);
            if ((Pos - head).sqrMagnitude > 1e-4f || Mathf.Abs(Mathf.DeltaAngle(Yaw, Head.Rot.Z)) > 0.01f)
                _rot = Quaternion.AngleAxis(Yaw, Vector3.forward);     // a generator's copy, placed after it was made
            _home = Pos;
            _destPos = Pos;
            Enter(St.Generate);
        }

        private void Enter(St s)
        {
            switch (State)
            {
                case St.Patrol:
                case St.PathFind:
                    _align = false;
                    break;
                case St.Jump:
                case St.Retreat:
                    _onGround = true;
                    break;
                case St.Attack:
                    _onGround = true;
                    _attackOver = true;
                    break;
            }

            State = s;
            _stateT = 0f;
            _stateRand = Random.value;
            switch (s)
            {
                case St.PathFind:
                case St.Patrol:
                    _align = true;
                    _hasSurface = false;
                    break;
                case St.TargetPatrol:
                    _dest = ClosestPatrolWaypoint();
                    if (_dest != null) { _destPos = _dest.GamePos; _hadPath = true; }
                    break;
                case St.Jump:
                    _onGround = false;
                    _align = false;
                    if (Anim != null) Anim.Speed = 1f;
                    LoopAnim(_aJumpLoop, true);
                    break;
                case St.TelegraphAttack:
                    Telegraph();
                    break;
                case St.TargetPlayer:
                    _targetPos = PlayerPos + new Vector3(0f, 0f, 1.5f);
                    break;
                case St.Attack:
                {
                    _telegraphT = 0f;
                    Vector3 aim = PlayerPos + new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value + 0.5f) - Pos;
                    _targetPos = Pos + 15f * (aim.sqrMagnitude > 1e-6f ? aim.normalized : MpCrawl.Fwd(_rot));
                    _attackOver = false;
                    _receivedTelegraph = false;
                    Leap(_targetPos, AttackVelocity);
                    break;
                }
                case St.Retreat:
                {
                    Vector3 d = PlayerPos - Pos;
                    d.z = 0f;
                    d = d.sqrMagnitude > 1e-6f ? d.normalized : Facing;
                    _targetPos = Pos - d * 3f;
                    Leap(_targetPos, RetreatVelocity);
                    break;
                }
                case St.Deactivate:
                    Send(17);       // DeactivateState
                    Remove();
                    break;
            }
        }

        private void Step(float dt)
        {
            switch (State)
            {
                case St.Generate:
                    // CParasite::Generate: the parasite has no generate animation, so the body state is over at once
                    if (HasPatrolPath()) Enter(St.TargetPatrol);
                    else Enter(St.PathFind);
                    return;
                case St.PathFind:
                    Crawl(dt);
                    Enter(St.Patrol);
                    return;
                case St.TargetPatrol:
                    Enter(St.Patrol);
                    return;
                case St.Patrol:
                    if (_pause > 0f) _pause -= dt;
                    NextWaypoint();
                    Crawl(dt);
                    if (ShouldAttack() && _stateT > 1.3f) Enter(St.TargetPlayer);
                    else if (_hitByPlayer) Enter(St.TelegraphAttack);
                    else if (!HitSomething()) Enter(St.Jump);
                    else if (_hadPath && _dest == null) Enter(St.Deactivate);
                    return;
                case St.Jump:
                    Ballistic(dt);
                    if (_landed && _stateT > 0.02f) Enter(St.TargetPatrol);
                    else if (_stateT > 60f) Enter(St.Deactivate);
                    return;
                case St.TelegraphAttack:
                    Enter(St.TargetPlayer);
                    return;
                case St.TargetPlayer:
                    _rot = MpCrawl.TurnForward(_rot, _targetPos - Pos, 115f * dt);
                    if (!Idle()) LoopAnim(_aReady, true);
                    if (!InMaxRange() || _stateT > 2f) Enter(St.Patrol);
                    else if (_stateT > 1.5f * _stateRand) Enter(St.Attack);
                    return;
                case St.Attack:
                    Ballistic(dt);
                    if (_attackOver && _stateT > 0.01f) Enter(St.Retreat);
                    else if ((_landed && _stateT > 0.01f) || _stateT > 5f) Enter(St.TargetPatrol);
                    return;
                case St.Retreat:
                    Ballistic(dt);
                    if ((_landed && _stateT > 0.1f) || _stateT > 5f) Enter(St.TargetPatrol);
                    return;
            }
        }

        // ------------------------------------------------------------------ triggers

        private bool HasPatrolPath() { return FirstPatrolLink() != 0u; }

        private uint FirstPatrolLink()
        {
            if (Links == null) return 0u;
            foreach (MpScriptObject.Link l in Links)
                if (l.State == 13 && l.Message == MpScriptMsg.Follow) return l.Target;
            return 0u;
        }

        private float PlayerDistSq { get { return (PlayerPos - Pos).sqrMagnitude; } }

        private bool InMaxRange() { return Info != null && PlayerDistSq < Info.MaxAttackRange * Info.MaxAttackRange; }

        private bool TooClose() { return Info != null && PlayerDistSq < Info.MinAttackRange * Info.MinAttackRange; }

        private bool InDetectionRange()
        {
            if (Info == null || PlayerDistSq >= Info.DetectionRange * Info.DetectionRange) return false;
            float dz = PlayerPos.z - Pos.z;
            return Info.DetectionHeight <= 0f || dz * dz < Info.DetectionHeight * Info.DetectionHeight;
        }

        /// <summary>CParasite::ShouldAttack.</summary>
        private bool ShouldAttack()
        {
            if (G == null || G.State == null || G.State.Energy <= 0f) return false;
            bool told = _receivedTelegraph && _telegraphT > 0.1f;
            return !TooClose() && InMaxRange() && (told || InDetectionRange());
        }

        /// <summary>CParasite::HitSomething: true every other think; else something solid close by (and not tumbled round).</summary>
        private bool HitSomething()
        {
            if ((_think & 1) != 0) return true;
            return _tumble < 270f && MpCrawl.CloseToWall(Pos, R + CollisionCloseMargin);
        }

        /// <summary>CParasite::TelegraphAttack: the living parasites near it turn on Samus for a moment (and come at where it was).</summary>
        private void Telegraph()
        {
            float r2 = MaxTelegraphReactDist * MaxTelegraphReactDist;
            if (r2 > 0f)
                foreach (MpEnemy e in Live.ToArray())
                {
                    var p = e as MpParasite;
                    if (p == null || p == this || p.Dead || (p.Pos - Pos).sqrMagnitude >= r2) continue;
                    p._receivedTelegraph = true;
                    p._telegraphT = Random.value * 0.5f + 0.5f;
                    p._targetPos = Pos;
                }

            _hitByPlayer = false;
        }

        // ------------------------------------------------------------------ waypoints

        /// <summary>CParasite::GetClosestWaypointForState (Patrol / Follow): the nearest waypoint along each patrol path.</summary>
        private MpWaypointEntity ClosestPatrolWaypoint()
        {
            if (Links == null || World == null) return null;
            MpWaypointEntity best = null;
            float bestD = float.MaxValue;
            var seen = new HashSet<MpWaypointEntity>();
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != 13 || l.Message != MpScriptMsg.Follow) continue;
                var w = World.Find(l.Target) as MpWaypointEntity;
                while (w != null && seen.Add(w))
                {
                    float d = (w.GamePos - Pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = w; }
                    w = NextOf(w);
                }
            }

            return best;
        }

        private MpWaypointEntity NextOf(MpWaypointEntity w)
        {
            if (w.Links == null) return null;
            foreach (MpScriptObject.Link l in w.Links)
                if (l.State == MpScriptMsg.StArrived && l.Message == MpScriptMsg.Next)
                {
                    var n = World.Find(l.Target) as MpWaypointEntity;
                    if (n != null && n.Active) return n;
                }

            return null;
        }

        /// <summary>CWallWalker::GotoNextWaypoint: within the advance radius the next one is taken (with its pause) and the waypoint told Arrived.</summary>
        private void NextWaypoint()
        {
            MpWaypointEntity w = _dest;
            if (w == null) return;
            if ((w.GamePos - Pos).sqrMagnitude < AdvanceWpRadius * AdvanceWpRadius)
            {
                _dest = w.Next();
                if (Mathf.Abs(w.Pause) > 1e-5f) _pause = w.Pause;
                w.OnMessage(MpScriptMsg.Arrived, this);
            }

            _destPos = w.GamePos;
        }

        // ------------------------------------------------------------------ crawling

        /// <summary>
        /// Patrol / PathFind each think: CParasite::DoFlockingBehavior's locomotion commands summed (the body controller's way), the
        /// body turned toward that and moved along its forward at the run animation's root speed (times the patterned speed: the
        /// playback rate), kept on its surface (CWallWalker::AlignToFloor, PreThink's pull to the plane).
        /// </summary>
        private void Crawl(float dt)
        {
            Vector3 up = MpCrawl.Up(_rot), fwd = MpCrawl.Fwd(_rot);
            Vector3 move = Vector3.zero;
            bool still = DisableMove || _pause > 0f || _scripted || _knockT > 0f;
            if (!still)
            {
                if (_think % 6 == 1) FlockNeighbours(fwd);
                if ((PlayerPos - Pos).sqrMagnitude < PlayerSeparationDist * PlayerSeparationDist)
                    move += Flat(Separation(PlayerPos, PlayerSeparationDist), up) * PlayerSeparationWeight;
                if (_sepMove != Vector3.zero) move += Flat(_sepMove, up) * SeparationWeight;
                foreach (KeyValuePair<Vector3, float> d in Doors())
                    if ((d.Key - Pos).sqrMagnitude < d.Value * d.Value) move += Flat(Separation(d.Key, d.Value), up);
                if (_telegraphT <= 0f)
                {
                    move += Flat(_cohMove, up) * CohesionWeight;
                    move += Flat(_alignMove, up) * AlignmentWeight;
                    Vector3 seek = _destPos - Pos;
                    if (seek.sqrMagnitude > 1e-4f) move += Flat(seek.normalized, up) * DestSeekWeight;
                    move += fwd * ForwardMoveWeight;
                }
            }

            float frac = Mathf.Clamp01(move.magnitude);
            if (frac > 0.05f)
            {
                _rot = MpCrawl.TurnForward(_rot, move, (Info.TurnSpeed > 0f ? Info.TurnSpeed : 360f) * dt);
                // CParasite only ever asks for the lurk locomotion: the walk, played at the patterned speed (CPatterned multiplies the playback rate)
                LoopAnim(_aWalk, true);
                if (Anim != null) Anim.Speed = Mathf.Max(0.1f, Info.Speed);
                _speedNow = _walkSpeed * Mathf.Max(0.1f, Info.Speed) * ModelScale;
                fwd = MpCrawl.Fwd(_rot);
                _vel = fwd * _speedNow;
                Pos += _vel * dt;
            }
            else
            {
                _speedNow = 0f;
                _vel = Vector3.zero;
                if (!DisableMove) Idle();
            }

            if (_align) AlignToFloor(dt);
        }

        /// <summary>The still pose: ready, unless a script's animation or the initial one of a parked parasite is playing.</summary>
        private bool Idle()
        {
            if (_scripted || _knockT > 0f) return true;
            LoopAnim(_aReady, true);
            if (Anim != null) Anim.Speed = 1f;
            return true;
        }

        /// <summary>CWallWalker::AlignToFloor + PreThink: the surface under the body (looking a little ahead), up turned to it, the body pulled to its height; none: tumble forward over the edge.</summary>
        private void AlignToFloor(float dt)
        {
            Vector3 up = MpCrawl.Up(_rot), fwd = MpCrawl.Fwd(_rot);
            Vector3 p, n;
            float h;
            Vector3 ahead = Pos + 2f * dt * _vel;
            float reach = R + Mathf.Max(0.1f, CollisionCloseMargin);
            if (MpCrawl.FindSurface(ahead, up, fwd, reach, out p, out n, out h))
            {
                _rot = MpCrawl.TurnUp(_rot, n, (AlignAngVel > 0f ? AlignAngVel : 360f) * dt);
                _tumble = 0f;
                _hasSurface = true;
                float now = Vector3.Dot(Pos - p, n);
                Vector3 onPlane = Pos - (now - R - 0.01f) * n;
                Pos = Vector3.Lerp(Pos, onPlane, Mathf.Clamp01(10f * dt));
            }
            else
            {
                float ang = _vel.magnitude * Mathf.Rad2Deg / Mathf.Max(0.05f, R) * dt;
                _rot = MpCrawl.TurnUp(_rot, fwd, ang);
                _tumble += ang;
                _hasSurface = false;
            }
        }

        private static Vector3 Flat(Vector3 v, Vector3 up) { return v - Vector3.Dot(v, up) * up; }

        /// <summary>CSteeringBehaviors::Separation: away from a point, stronger the closer.</summary>
        private Vector3 Separation(Vector3 from, float radius)
        {
            Vector3 d = Pos - from;
            float d2 = d.sqrMagnitude, r2 = radius * radius;
            if (d2 < 1e-8f || d2 >= r2) return Vector3.zero;
            return d.normalized * ((r2 - d2) / r2);
        }

        /// <summary>The flocking terms over the living parasites within the search radius (every sixth think, as the game).</summary>
        private void FlockNeighbours(Vector3 fwd)
        {
            _sepMove = _cohMove = _alignMove = Vector3.zero;
            if (SearchRadius <= 0f) return;
            float r2 = SearchRadius * SearchRadius, closest = 2f + SeparationDist * SeparationDist;
            Vector3 centre = Vector3.zero, heading = Vector3.zero;
            MpParasite near = null;
            int count = 0;
            foreach (MpEnemy e in Live)
            {
                var p = e as MpParasite;
                if (p == null || p == this || p.Dead || !p.Active) continue;
                float d2 = (p.Pos - Pos).sqrMagnitude;
                if (d2 > r2) continue;
                count++;
                centre += p.Pos;
                heading += MpCrawl.Fwd(p._rot);
                if (d2 < closest) { closest = d2; near = p; }
            }

            if (near != null && SeparationWeight > 0f && SeparationDist > 0f) _sepMove = Separation(near.Pos, SeparationDist);
            if (count == 0) return;
            Vector3 c = centre / count - Pos;
            float cd = c.magnitude;
            if (cd > 1e-4f) _cohMove = c / cd * Mathf.Clamp01(cd / 0.6f);
            Vector3 h = heading / count;
            if (h.sqrMagnitude > 1e-6f) _alignMove = h.normalized * (Vector3.Angle(fwd, h) / 180f);
        }

        /// <summary>CParasite::AddDoorRepulsors: each door of its area pushes it away within 0.75 of its box's diagonal.</summary>
        private List<KeyValuePair<Vector3, float>> Doors()
        {
            if (_doors != null) return _doors;
            _doors = new List<KeyValuePair<Vector3, float>>();
            if (World == null) return _doors;
            List<MpEntity> list;
            if (!World.ByArea.TryGetValue(Area, out list)) return _doors;
            foreach (MpEntity e in list)
            {
                var d = e as MpDoorEntity;
                if (d == null || d.Solid == null) continue;
                Bounds b = d.Solid.bounds;
                _doors.Add(new KeyValuePair<Vector3, float>(MpSpace.ToGame(b.center), 0.75f * b.size.magnitude / MpSpace.Scale));
            }

            return _doors;
        }

        // ------------------------------------------------------------------ leaps and falls

        /// <summary>CParasite::Attack / Retreat with UpdateJumpVelocity: faces the target and leaps at it on the game's arc.</summary>
        private void Leap(Vector3 target, float speed)
        {
            Vector3 d = target - Pos;
            d.z = 0f;
            Vector3 flat = d.sqrMagnitude > 1e-6f ? d.normalized : Facing;
            _rot = MpCrawl.Basis(Vector3.forward, flat);
            _vel = MpCrawl.LeapVelocity(Pos, target, flat, speed);
            _onGround = false;
            _align = false;
            if (Anim != null) Anim.Speed = 1f;
            LoopAnim(_aIntoJump, false);
        }

        /// <summary>In the air: gravity, and the first surface along the way stops it (CParasite::CollidedWith: oriented to the surface, landed).</summary>
        private void Ballistic(float dt)
        {
            if (Anim != null && Anim.Anim == _aIntoJump && Anim.Done) LoopAnim(_aJumpLoop, true);
            Vector3 step = _vel * dt;
            float len = step.magnitude;
            Vector3 p, n;
            if (len > 1e-5f && MpCrawl.Ray(Pos, step, len + R, out p, out n))
            {
                Vector3 f = MpCrawl.Fwd(_rot);
                _rot = MpCrawl.Basis(n, f - Vector3.Dot(f, n) * n);
                Pos = p + n * (R + 0.01f);
                _vel = Vector3.zero;
                _landed = true;
                _onGround = true;
                LoopAnim(_aOutOfJump, false);
                return;
            }

            Pos += step;
            _vel.z -= MpCrawl.Gravity * dt;
        }

        // ------------------------------------------------------------------ animation

        /// <summary>An animation of the AI's own (not restarted when it is already playing); a script's keyframe animation playing wins until it ends.</summary>
        private void LoopAnim(int anim, bool loop)
        {
            if (Anim == null || anim < 0) return;
            if (_scripted) return;
            if (Anim.Anim == anim && Anim.Loop == loop) return;
            if (PlayAnim(anim, loop, 0.15f)) { _myAnim = anim; _myLoop = loop; }
        }

        /// <summary>An ActorKeyframe played an animation on it (CBSScripted): a one-shot holds the AI until it ends, a loop until the AI moves it.</summary>
        private void WatchScripted()
        {
            if (Anim == null) return;
            if (Anim.Anim != _myAnim || Anim.Loop != _myLoop)
            {
                _scripted = true;
                _myAnim = Anim.Anim;
                _myLoop = Anim.Loop;
            }

            if (_scripted && ((!Anim.Loop && Anim.Done) || (Anim.Loop && !DisableMove && _pause <= 0f))) _scripted = false;
        }

        // ------------------------------------------------------------------ contact and death

        /// <summary>
        /// CParasite::Think's touch: Samus's box overlapping the parasite's (its unmorphed radius: half its height; the collision
        /// sphere while it leaps) - a leap that reaches her is over, and the contact damage lands every damage-wait time.
        /// </summary>
        protected override void ContactDamage(float dt)
        {
            _contactT -= dt;
            if (G == null || G.State == null || G.State.Energy <= 0f) return;
            float r = !_attackOver ? R : (Info.Height > 0f ? Info.Height * 0.5f : R);
            Vector3 feet = PlayerPos;
            float pr = G.Scripts != null ? G.Scripts.PlayerRadius : 0.5f, ph = G.Scripts != null ? G.Scripts.PlayerHeight : 2.7f;
            if (Mathf.Abs(feet.x - Pos.x) > pr + r || Mathf.Abs(feet.y - Pos.y) > pr + r) return;
            if (Pos.z + r < feet.z || Pos.z - r > feet.z + ph) return;
            if (!_attackOver) { _attackOver = true; _landed = false; }
            if (_contactT > 0f || Info.Contact.Damage <= 0f) return;
            LabContacts++;
            if (G.DamagePlayer(Info.Contact.Damage, Pos)) LabDamage += Info.Contact.Damage;
            _contactT = Mathf.Max(0.25f, Info.DamageWaitTime);
        }

        protected override float DeathLinger { get { return 0.5f; } }

        /// <summary>
        /// CParasite::Death (the parasites near it are alerted) and CPatterned::Death: DeathRattle. Every parasite on the disc has an
        /// x-damage threshold of 0, so any killing blow is a massive death (CPatterned::MassiveDeath): the death sound, MassiveDeath,
        /// the death explosion if it has one, then DeathDelete: Dead, gone.
        /// </summary>
        public override void Die(float lastBlow)
        {
            if (Dead) return;
            Dead = true;
            _deadT = 0f;
            HealthNow = 0f;
            if (ShotBox != null) ShotBox.enabled = false;
            Live.Remove(this);
            LabDeaths++;
            Telegraph();
            Send(20);
            if (Info == null || lastBlow >= Info.XDamage)
            {
                if (Info != null && Info.DeathSfx >= 0) PlayDataSfx(Info.DeathSfx, 80f);
                Send(19);
                if (Info != null && MpAssets.Valid(Info.Particle1))
                {
                    Vector3 off = new Vector3(Info.Particle1Scale.X, Info.Particle1Scale.Y, Info.Particle1Scale.Z) * ModelScale;
                    MpEffects.Spawn(Info.Particle1, Pos + _rot * off, MpEffects.GameOrientation(Quaternion.identity));
                }

                Send(MpScriptMsg.StDead);
                Remove();
                return;
            }

            Send(MpScriptMsg.StDead);
            if (Info.DeathSfx >= 0) PlayDataSfx(Info.DeathSfx, 80f);
            if (Anim != null) PlayAnim(_aKnock, false, 0.05f);
        }
    }
}
