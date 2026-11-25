using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// A swarm of crawlers on the walls (decomp CWallCrawlerSwarm, ScriptLoader::LoadWallCrawlerSwarm) - the frigate's ventilation
    /// shaft parasites. While active it breeds boids at its patrol waypoint (the generation rate, up to its boid count and its
    /// most-ever count) on the surface nearest that waypoint. Each boid walks by its animation's root motion (x1.5, the boids'
    /// model scale), stays on the surface under it (climbs walls ahead, tumbles over edges, drops out after 30 thinks with
    /// nothing under it) and steers as the game's boids do: away from doors, toward Samus inside the attraction radius, apart
    /// from its nearest neighbour, along its waypoint path (gone at the path's end), toward and along with its neighbours. A
    /// parasite swarm leaps boids at Samus (2% a think for each boid within 18 units in her sight, at most its launch count in
    /// the air): a leaping boid that reaches her is spent; one on a surface hurts her by the crab damage at most every cooldown.
    /// Shots kill boids (their own health and vulnerability; each boid has a small shot sphere through <see cref="MpHitTarget"/>);
    /// a killed boid bursts with the swarm's particles and may send DeathRattle / Dead to the scripts.
    ///
    /// Drawing (as the game: a few shared poses, CWallCrawlerSwarm::RenderBoid): four poses of the walk animation a quarter apart
    /// plus the leap one, each posed once a frame and baked; every boid is a plain mesh renderer showing the baked mesh of its
    /// pose. The walk sounds play from the boid nearest Samus, louder with more boids (UpdateEffects).
    ///
    /// The game finds a boid's surface by searching the area's triangles round it; here rays do (both faces), every think for
    /// every boid, and a boid lost for a few thinks is put back on the nearest surface within 1.5 units before it may drop out.
    ///
    /// Left out: the scarab and crab flavours' explosions and attraction animation, ice (FreezeCollision), radius damage, orbit
    /// lock-on, the area lighting per partition cell.
    /// </summary>
    public sealed class MpWallCrawlerSwarm : MpActorEntity
    {
        // ScriptLoader::LoadWallCrawlerSwarm
        public MpScriptRead.Head Head;
        public MpActorParams Params;
        public int Flavor;
        public uint Ancs;
        public int CharIndex, DefaultAnim, LaunchAnim, AttractAnim;
        public uint[] Particles = new uint[4];
        public MpDamageInfo CrabDamage, ScarabDamage;
        public float CrabDamageCooldown, BoidRadius, TouchRadius, PlayerTouchRadius, AnimSpeed;
        public int NumBoids, MaxCreatedBoids, MaxLaunches;
        public float SeparationRadius, CohesionMagnitude, AlignmentWeight, SeparationMagnitude, MoveToWaypointWeight;
        public float AttractionMagnitude, AttractionRadius, BoidGenRate, ScarabBoxMargin, ScarabScatterXY, ScarabTimeToExplode;
        public float BoidHealth;
        public int LaunchSfx = -1, ScatterSfx = -1;
        public bool StartActive;

        public const float ModelScale = 1.5f, WaypointGoalRadius = 3f;

        // lab counters
        public int Created, Kills, Launches, Touches, Spent, Dropped, PathEnds;
        public float DamageDealt, LastThinkMs;
        public static int LabShotHits;
        public readonly List<string> DropLog = new List<string>();

        private sealed class Boid
        {
            public int Index;
            public Vector3 Pos, Vel;
            public Quaternion Rot = Quaternion.identity;
            public MpWaypointEntity Target;
            public bool Active, Launched;
            public float Health, AirT, Born;
            public int NotOnSurface, LaunchNoSurface, Pose = -1;
            public Transform Draw;
            public MeshFilter[] Filters;
        }

        /// <summary>The shot target of one boid: hits go to the swarm, which hurts that boid (CWallCrawlerSwarm::Touch with a projectile).</summary>
        private sealed class BoidTarget : MpEntity
        {
            public MpWallCrawlerSwarm Swarm;
            public Boid B;
            public override bool TakeDamage(int weapon, bool charged, float damage) { return Swarm.ShotBoid(B, weapon, charged, damage); }
            public override string ToString() { return Swarm + " boid " + B.Index; }
        }

        private readonly List<Boid> _boids = new List<Boid>();
        private MpCharacter _char;
        private MpAnimator[] _poses;            // 0..3 the walk a quarter apart, 4 the leap
        private Mesh[][] _baked;
        private Material[][] _mats;
        private int[] _poseAnim;
        private float[] _poseT;
        private Vector3[] _poseDelta;
        private bool[] _poseUsed;
        private Transform _root, _poseRoot;
        private float _genT, _crabT;
        private int _think;
        private bool _surfaceKnown, _surfaceFound;
        private Vector3 _surfaceP, _surfaceN;
        private MpWaypointEntity _surfaceFor;
        private List<KeyValuePair<Vector3, float>> _doors;
        private readonly List<KeyValuePair<MpEffects.Effect, float>> _bursts = new List<KeyValuePair<MpEffects.Effect, float>>();
        private readonly List<Boid> _near = new List<Boid>(50);
        private int _soundVol;

        public int Alive
        {
            get { int n = 0; foreach (Boid b in _boids) if (b.Active) n++; return n; }
        }

        public int InAir
        {
            get { int n = 0; foreach (Boid b in _boids) if (b.Active && b.Launched) n++; return n; }
        }

        // ------------------------------------------------------------------ script object

        /// <summary>ScriptLoader::LoadWallCrawlerSwarm (39 properties).</summary>
        private static MpWallCrawlerSwarm Read(MpScriptObject o)
        {
            if (o.PropCount != 0x27) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var s = new MpWallCrawlerSwarm();
            s.Head = MpScriptRead.ReadHead(r, true);
            s.StartActive = r.Bool();
            s.Params = MpScriptRead.ReadActorParams(r);
            s.Flavor = r.S32();
            s.Ancs = r.U32();
            s.CharIndex = r.S32();
            s.DefaultAnim = r.S32();
            s.LaunchAnim = r.S32();
            s.AttractAnim = r.S32();
            for (int i = 0; i < 4; i++) s.Particles[i] = r.U32();
            s.CrabDamage = MpDamageInfo.Read(r);
            s.CrabDamageCooldown = r.F32();
            s.ScarabDamage = MpDamageInfo.Read(r);
            s.BoidRadius = r.F32();
            s.TouchRadius = r.F32();
            s.PlayerTouchRadius = r.F32();
            s.AnimSpeed = r.F32();
            s.NumBoids = Mathf.Clamp(r.S32(), 0, 500);
            s.MaxCreatedBoids = r.S32();
            s.SeparationRadius = r.F32();
            s.CohesionMagnitude = r.F32();
            s.AlignmentWeight = r.F32();
            s.SeparationMagnitude = r.F32();
            s.MoveToWaypointWeight = r.F32();
            s.AttractionMagnitude = r.F32();
            s.AttractionRadius = r.F32();
            s.BoidGenRate = r.F32();
            s.MaxLaunches = r.S32();
            s.ScarabBoxMargin = r.F32();
            s.ScarabScatterXY = r.F32();
            s.ScarabTimeToExplode = r.F32();
            MpScriptRead.SkipHealth(r, out s.BoidHealth);
            s.Vuln = MpScriptRead.ReadVulnerability(r);
            s.LaunchSfx = r.S32();
            s.ScatterSfx = r.S32();
            return s;
        }

        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            MpWallCrawlerSwarm s = Read(o);
            if (s == null) return;
            MpEnemyFactory.CollectCharacter(assets, s.Ancs, s.CharIndex, tex);
            foreach (uint p in s.Particles) MpEnemyShots.PreparePart(assets, p);
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            MpWallCrawlerSwarm s = Read(o);
            if (s == null) return null;
            s.Build(view, "Swarm " + o.Name);
            s.SetActive(s.StartActive);
            return s;
        }

        private void Build(MpAreaView view, string name)
        {
            _root = new GameObject(name).transform;
            _root.SetParent(view.Root.transform, false);
            Go = _root.gameObject;
            for (int i = 0; i < NumBoids; i++) _boids.Add(new Boid { Index = i });
            _char = MpAssets.Valid(Ancs) ? MpCharacter.Get(Ancs, CharIndex) : null;
            _poseDelta = new Vector3[5];
            if (_char == null || _char.Model == null) return;
            _char.Upload(true);
            if (_char.Model.Meshes == null) { _char = null; return; }

            // the shared poses, outside the (mirrored) world so their baked meshes are plain model space
            _poseRoot = new GameObject(name + " poses").transform;
            _poses = new MpAnimator[5];
            _baked = new Mesh[5][];
            _poseAnim = new int[5];
            _poseT = new float[5];
            _poseUsed = new bool[5];
            int walk = DefaultAnim >= 0 ? DefaultAnim : 0, leap = LaunchAnim >= 0 ? LaunchAnim : 0;
            for (int i = 0; i < 5; i++)
            {
                var a = new MpAnimator(_char, name + " pose " + i, _poseRoot);
                _poseAnim[i] = i < 4 ? walk : leap;
                a.Play(_poseAnim[i], true, 0f);
                if (i < 4) a.Time = a.Duration * (i / 16f);      // CWallCrawlerSwarm::AllocateSkinnedModels: each a little further on
                a.Speed = AnimSpeed > 0f ? AnimSpeed : 1f;
                _poseT[i] = a.Time;
                a.Tick(0f);
                foreach (SkinnedMeshRenderer r in a.Renderers) r.forceRenderingOff = true;
                if (i < 4) { int k = i; a.SoundEvent = s => OnPoseSound(k, s); }
                _poses[i] = a;
                _baked[i] = new Mesh[a.Renderers.Count];
                for (int m = 0; m < a.Renderers.Count; m++) _baked[i][m] = new Mesh { name = name + " pose " + i + " mesh " + m };
            }

            _mats = new Material[_poses[0].Renderers.Count][];
            for (int m = 0; m < _mats.Length; m++) _mats[m] = _poses[0].Renderers[m].sharedMaterials;
            Bake();
        }

        public override void OnRemoved()
        {
            if (_poseRoot != null) Object.Destroy(_poseRoot.gameObject);
            if (_baked != null) foreach (Mesh[] ms in _baked) foreach (Mesh m in ms) if (m != null) Object.Destroy(m);
            base.OnRemoved();
        }

        /// <summary>A line for the lab.</summary>
        public string Report()
        {
            Vector3 c = Vector3.zero;
            int n = 0;
            foreach (Boid b in _boids) if (b.Active) { c += b.Pos; n++; }
            return Name + (Active ? "" : " (inactive)") + ": " + n + "/" + NumBoids + " boids alive (" + Created + " made, " + InAir + " in the air), centre " + (n > 0 ? (c / n).ToString("0.0") : "-")
                + ", kills " + Kills + ", launches " + Launches + ", touches " + Touches + ", spent " + Spent + ", dropped off " + Dropped + ", at the path end " + PathEnds + ", damage dealt " + DamageDealt.ToString("0.###") + ", think " + LastThinkMs.ToString("0.00") + " ms"
                + (_char == null ? " NO MODEL" : "") + (_surfaceKnown ? ", birth surface " + (_surfaceFound ? _surfaceN.ToString("0.00") + " at " + _surfaceP.ToString("0.0") : "none") : "") + ", walk root " + MpCrawl.RootSpeed(_char, DefaultAnim).ToString("0.00");
        }

        /// <summary>One boid for the lab: where it is, its up and forward, frames off a surface, its waypoint.</summary>
        public string BoidLine(int i)
        {
            if (i < 0 || i >= _boids.Count) return "-";
            Boid b = _boids[i];
            return "boid " + i + (b.Active ? "" : " (gone)") + (b.Launched ? " (in the air)" : "") + " at " + b.Pos.ToString("0.00") + " up " + MpCrawl.Up(b.Rot).ToString("0.00") + " fwd " + MpCrawl.Fwd(b.Rot).ToString("0.00")
                + " v " + b.Vel.magnitude.ToString("0.0") + " off " + b.NotOnSurface + " to " + (b.Target != null ? b.Target.GamePos.ToString("0.0") : "-");
        }

        /// <summary>The nearest living boid to a game point (the lab aims at it).</summary>
        public bool NearestBoid(Vector3 to, out Vector3 pos)
        {
            pos = Vector3.zero;
            float best = float.MaxValue;
            foreach (Boid b in _boids)
            {
                if (!b.Active) continue;
                float d = (b.Pos - to).sqrMagnitude;
                if (d < best) { best = d; pos = b.Pos; }
            }

            return best < float.MaxValue;
        }

        /// <summary>The swarm's patrol waypoint (Patrol -> Follow).</summary>
        public MpWaypointEntity PatrolWaypoint()
        {
            if (Links == null || World == null) return null;
            foreach (MpScriptObject.Link l in Links)
                if (l.State == 13 && l.Message == MpScriptMsg.Follow) return World.Find(l.Target) as MpWaypointEntity;
            return null;
        }

        // ------------------------------------------------------------------ think

        public override void Think(float dt)
        {
            if (!Active || _root == null || dt <= 0f) return;
            float t0 = Time.realtimeSinceStartup;
            _think++;
            _genT -= dt;
            _crabT -= dt;
            UpdateBursts(dt);

            // breed boids at the patrol waypoint
            while ((MaxCreatedBoids == 0 || Created < MaxCreatedBoids) && _genT <= 0f && BoidGenRate > 0f)
            {
                Boid free = null;
                foreach (Boid b in _boids) if (!b.Active) { free = b; break; }
                _genT += 1f / BoidGenRate;
                if (free == null || !CreateBoid(free)) break;
                Created++;
            }

            // every boid keeps to its surface each think (Unity rays are coarser than the game's triangle search); half of them steer each think
            int count = 0;
            foreach (Boid b in _boids)
            {
                count++;
                if (b.Active) UpdateBoid(b, dt, b.Launched || (_think & 1) == (count & 1));
            }

            // the poses advance; each walking boid moves by its pose's root motion (x1.5), leaping ones by their velocity
            AdvancePoses(dt);
            foreach (Boid b in _boids)
            {
                if (!b.Active) continue;
                if (b.Launched) { b.Pos += b.Vel * dt; continue; }
                int pose = b.Index & 3;
                b.Vel = 1.5f * (b.Rot * _poseDelta[pose]) / dt;
                b.Pos += b.Vel * dt;
            }

            Touch();
            if (Flavor == 0 && MaxLaunches > 0) LaunchSome();
            Bake();
            Place();
            LastThinkMs = (Time.realtimeSinceStartup - t0) * 1000f;
        }

        // ------------------------------------------------------------------ boids

        /// <summary>CWallCrawlerSwarm::CreateBoid: on the surface nearest the patrol waypoint, up along its normal.</summary>
        private bool CreateBoid(Boid b)
        {
            MpWaypointEntity w = PatrolWaypoint();
            if (w == null) return false;
            if (!_surfaceKnown || _surfaceFor != w)
            {
                _surfaceKnown = true;
                _surfaceFor = w;
                _surfaceFound = NearestSurface(w.GamePos, out _surfaceP, out _surfaceN);
            }

            Vector3 n = _surfaceFound ? _surfaceN : Vector3.forward;
            Vector3 at = _surfaceFound ? w.GamePos - Vector3.Dot(w.GamePos - _surfaceP, n) * n : w.GamePos;
            b.Pos = at + n * BoidRadius;
            b.Rot = n.z < -0.9999f ? Quaternion.AngleAxis(180f, new Vector3(1f, 0f, 0f)) : Quaternion.FromToRotation(Vector3.forward, n);
            b.Active = true;
            b.Launched = false;
            b.Vel = Vector3.zero;
            b.Target = w;
            b.NotOnSurface = 0;
            b.Health = BoidHealth;
            b.AirT = 0f;
            b.Born = Time.time;
            Show(b, true);
            return true;
        }

        /// <summary>CWallCrawlerSwarm::FindBestCollisionInBox: the nearest surface around a point (rays along the six axes, out to the swarm's box).</summary>
        private bool NearestSurface(Vector3 at, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float best = float.MaxValue;
            float reach = Mathf.Max(2f, 0.75f * Mathf.Max(Mathf.Abs(Head.Scale.X), Mathf.Max(Mathf.Abs(Head.Scale.Y), Mathf.Abs(Head.Scale.Z))));
            Vector3[] dirs = { Vector3.back, Vector3.forward, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            foreach (Vector3 d in dirs)
            {
                Vector3 p, n;
                if (!MpCrawl.Ray(at, d, reach, out p, out n)) continue;
                float h = Vector3.Dot(at - p, n);
                if (h < best) { best = h; point = p; normal = n; }
            }

            return best < float.MaxValue;
        }

        /// <summary>CWallCrawlerSwarm::UpdateBoid: a leaping boid lands on what it meets or falls; a walking one keeps to its surface and steers.</summary>
        private void UpdateBoid(Boid b, float dt, bool steer)
        {
            if (b.Launched)
            {
                b.AirT += dt;
                Vector3 step = b.Vel * dt * 1.5f;
                Vector3 p, n;
                if (b.LaunchNoSurface == 0 && step.sqrMagnitude > 1e-8f && MpCrawl.Ray(b.Pos, step, step.magnitude + BoidRadius, out p, out n))
                {
                    b.Rot = MpCrawl.TurnUp(b.Rot, n, 180f);
                    b.Pos = p + n * (BoidRadius + 0.01f);
                    b.NotOnSurface = 0;
                    b.Launched = false;
                    b.Vel = Vector3.zero;
                    return;
                }

                b.Vel.z -= MpCrawl.Gravity * dt;
                if (b.LaunchNoSurface > 0) b.LaunchNoSurface--;
                if (b.AirT > 6f) Kill(b, 0f, 0f, false);
                return;
            }

            if (b.NotOnSurface >= 60) { if (DropLog.Count < 12) DropLog.Add(BoidLine(b.Index) + " lived " + (Time.time - b.Born).ToString("0.00") + " s"); b.Active = false; Dropped++; Show(b, false); return; }
            Vector3 up = MpCrawl.Up(b.Rot), fwd = MpCrawl.Fwd(b.Rot);
            Vector3 sp, sn;
            float h;
            bool found = false;
            if (MpCrawl.FindSurface(b.Pos + 1.5f * dt * b.Vel, up, fwd, 2f * BoidRadius, out sp, out sn, out h))
            {
                b.Rot = MpCrawl.TurnUp(b.Rot, sn, 180f * dt);
                float d = Vector3.Dot(b.Pos - sp, sn);
                if (d <= 1.5f * BoidRadius)
                {
                    b.Pos -= (d - BoidRadius - 0.01f) * sn;
                    b.NotOnSurface = 0;
                    found = true;
                }
            }

            if (!found && b.NotOnSurface >= 4 && MpCrawl.FindSurface(b.Pos, up, fwd, 5f * BoidRadius, out sp, out sn, out h))
            {
                // lost for a few thinks: back onto the nearest surface round it (the game's triangle search would have kept it
                // there; the rays here miss thin edges)
                b.Rot = MpCrawl.TurnUp(b.Rot, sn, 90f);
                b.Pos -= (Vector3.Dot(b.Pos - sp, sn) - BoidRadius - 0.01f) * sn;
                b.NotOnSurface = 0;
                found = true;
            }

            if (!found)
            {
                float ang = b.Vel.magnitude / Mathf.Max(0.05f, BoidRadius) * Mathf.Rad2Deg * dt;
                b.Rot = MpCrawl.TurnUp(b.Rot, MpCrawl.Fwd(b.Rot), ang);
                b.NotOnSurface++;
            }

            if (!steer) return;

            // steering: the game's order, stopping once the push is strong enough
            NearList(b);
            up = MpCrawl.Up(b.Rot);
            fwd = MpCrawl.Fwd(b.Rot);
            Vector3 ahead = 0.3f * fwd;
            for (int i = 0; i < 8 && ahead.sqrMagnitude < 9f; i++)
            {
                switch (i)
                {
                    case 0:
                        foreach (KeyValuePair<Vector3, float> door in Doors())
                            if ((door.Key - b.Pos).sqrMagnitude < door.Value * door.Value) Separate(b, door.Key, door.Value, 4.5f, ref ahead);
                        break;
                    case 3:
                        Attract(b, MpEnemy.PlayerPos, AttractionRadius, AttractionMagnitude, ref ahead);
                        break;
                    case 4:
                        if (_near.Count > 0)
                        {
                            Vector3 closest = Vector3.zero;
                            float cd = float.MaxValue;
                            foreach (Boid o in _near)
                            {
                                float d2 = (b.Pos - o.Pos).sqrMagnitude;
                                if (d2 > 0f && d2 < cd) { cd = d2; closest = o.Pos; }
                            }

                            Separate(b, closest, SeparationRadius, SeparationMagnitude, ref ahead);
                        }

                        break;
                    case 5:
                        if (!ToWaypoint(b, ref ahead)) return;
                        break;
                    case 6:
                        if (_near.Count > 0)
                        {
                            Vector3 c = Vector3.zero;
                            foreach (Boid o in _near) c += o.Pos;
                            Cohere(b, c / _near.Count, SeparationRadius, CohesionMagnitude, ref ahead);
                        }

                        break;
                    case 7:
                        if (_near.Count > 0)
                        {
                            Vector3 dir = Vector3.zero;
                            foreach (Boid o in _near) dir += MpCrawl.Fwd(o.Rot);
                            dir /= _near.Count;
                            ahead += Vector3.Angle(fwd, dir) / 180f * (AlignmentWeight * dir);
                        }

                        break;
                }
            }

            Vector3 flat = ahead - Vector3.Dot(ahead, up) * up;
            if (flat.sqrMagnitude > 1e-8f) b.Rot = MpCrawl.TurnForward(b.Rot, flat, 180f * dt);
        }

        /// <summary>CWallCrawlerSwarm::BuildBoidNearList: up to 50 boids within the separation radius (squared, as the game compares).</summary>
        private void NearList(Boid b)
        {
            _near.Clear();
            float r = SeparationRadius;
            foreach (Boid o in _boids)
            {
                if (o == b || !o.Active) continue;
                float d = (o.Pos - b.Pos).sqrMagnitude;
                if (d != 0f && d < r) { _near.Add(o); if (_near.Count >= 50) return; }
            }
        }

        private static void Separate(Boid b, Vector3 from, float radius, float magnitude, ref Vector3 ahead)
        {
            Vector3 d = b.Pos - from;
            float d2 = d.sqrMagnitude, r2 = radius * radius;
            if (d2 < 1e-8f || d2 >= r2) return;
            ahead += (1f - d2 / r2) * d.normalized * magnitude;
        }

        private static void Cohere(Boid b, Vector3 to, float radius, float magnitude, ref Vector3 ahead)
        {
            Vector3 d = to - b.Pos;
            float d2 = d.sqrMagnitude, r2 = radius * radius;
            if (d2 < 1e-8f) return;
            ahead += (d2 > r2 ? 1f : d2 / r2) * d.normalized * magnitude;
        }

        private static void Attract(Boid b, Vector3 to, float radius, float magnitude, ref Vector3 ahead)
        {
            Vector3 d = to - b.Pos;
            float d2 = d.sqrMagnitude, r2 = radius * radius;
            if (d2 < 1e-8f || d2 > r2) return;
            ahead += (1f - d2 / r2) * d.normalized * magnitude;
        }

        /// <summary>CWallCrawlerSwarm::MoveToWayPoint: on toward its waypoint; reached, the next one - none left and the boid is gone.</summary>
        private bool ToWaypoint(Boid b, ref Vector3 ahead)
        {
            MpWaypointEntity w = b.Target;
            if (w == null) return true;
            if ((w.GamePos - b.Pos).sqrMagnitude < WaypointGoalRadius * WaypointGoalRadius)
            {
                b.Target = w.Next();
                if (b.Target == null) { b.Active = false; PathEnds++; Show(b, false); return false; }
                w = b.Target;
            }

            Vector3 d = w.GamePos - b.Pos;
            if (d.sqrMagnitude > 1e-8f) ahead += d.normalized * MoveToWaypointWeight;
            return true;
        }

        /// <summary>CWallCrawlerSwarm::AddDoorRepulsors: the doors of its area keep the boids off (0.75 of a door box's diagonal).</summary>
        private List<KeyValuePair<Vector3, float>> Doors()
        {
            if (_doors != null) return _doors;
            _doors = new List<KeyValuePair<Vector3, float>>();
            List<MpEntity> list;
            if (World == null || !World.ByArea.TryGetValue(Area, out list)) return _doors;
            foreach (MpEntity e in list)
            {
                var d = e as MpDoorEntity;
                if (d == null || d.Solid == null) continue;
                Bounds bb = d.Solid.bounds;
                _doors.Add(new KeyValuePair<Vector3, float>(MpSpace.ToGame(bb.center), 0.75f * bb.size.magnitude / MpSpace.Scale));
            }

            return _doors;
        }

        // ------------------------------------------------------------------ leaps and touches

        /// <summary>The parasite flavour's leaps (CWallCrawlerSwarm::Think): boids within 18 units of Samus with a clear line jump at her chest.</summary>
        private void LaunchSome()
        {
            if (MpGame.Instance == null || MpGame.Instance.State == null || MpGame.Instance.State.Energy <= 0f) return;
            Vector3 target = MpEnemy.PlayerPos + new Vector3(0f, 0f, 1f);
            int inAir = InAir;
            foreach (Boid b in _boids)
            {
                if (inAir >= MaxLaunches) return;
                if (!b.Active || b.Launched || (b.Pos - target).sqrMagnitude >= 18f * 18f || Random.value > 0.02f) continue;
                if (!MpEnemy.LineOfSight(b.Pos, target)) continue;
                Launch(b, target);
                inAir++;
            }
        }

        /// <summary>CWallCrawlerSwarm::LaunchBoid: upright, facing the target, on the parasite leap's arc (its flat speed x0.6).</summary>
        private void Launch(Boid b, Vector3 target)
        {
            float attackTime = 2f * Mathf.Sqrt(2.5f / MpCrawl.Gravity);
            Vector3 d = target - b.Pos;
            d.z = 0f;
            Vector3 flat = d.sqrMagnitude > 1e-6f ? d.normalized : MpCrawl.Fwd(b.Rot);
            b.Rot = MpCrawl.Basis(Vector3.forward, flat);
            b.Vel = MpCrawl.LeapVelocity(b.Pos, target, flat, 15f / attackTime, 0.6f);
            b.Launched = true;
            b.LaunchNoSurface = 1;
            b.AirT = 0f;
            Launches++;
            if (LaunchSfx >= 0 && MpGame.Instance != null) MpGame.Instance.PlayDataSfxAt(LaunchSfx, b.Pos, 1f, false, 50f);
        }

        /// <summary>
        /// CWallCrawlerSwarm::Touch with Samus: a leaping boid that reaches her is spent (a token hit, Dead to the scripts); one on a
        /// surface touching her does the crab damage at most once per cooldown.
        /// </summary>
        private void Touch()
        {
            MpGame g = MpGame.Instance;
            if (g == null || g.State == null || g.State.Energy <= 0f) return;
            Vector3 feet = MpEnemy.PlayerPos;
            float pr = g.Scripts != null ? g.Scripts.PlayerRadius : 0.5f, ph = g.Scripts != null ? g.Scripts.PlayerHeight : 2.7f;
            float r = PlayerTouchRadius > 1e-4f ? PlayerTouchRadius : TouchRadius;
            foreach (Boid b in _boids)
            {
                if (!b.Active) continue;
                Vector3 p = b.Pos;
                if (Mathf.Abs(p.x - feet.x) > pr + r || Mathf.Abs(p.y - feet.y) > pr + r || p.z + r < feet.z || p.z - r > feet.z + ph) continue;
                if (b.Launched && Flavor == 0)
                {
                    g.DamagePlayer(0.00002f, p);
                    DamageDealt += 0.00002f;
                    Spent++;
                    Kill(b, 0f, 1f, true);
                }
                else if (_crabT <= 0f)
                {
                    Touches++;
                    if (g.DamagePlayer(CrabDamage.Damage, p)) DamageDealt += CrabDamage.Damage;
                    _crabT = CrabDamageCooldown;
                    return;
                }
            }
        }

        /// <summary>A shot on a boid (CWallCrawlerSwarm::Touch with a projectile): the boids it overlaps lose the shot's damage through the swarm's vulnerability.</summary>
        private bool ShotBoid(Boid hit, int weapon, bool charged, float damage)
        {
            if (!Active || !hit.Active) return false;
            float d = Vuln != null ? Vuln.Damage(weapon, charged, damage) : damage;
            if (d <= 0f) { if (MpGame.Instance != null) MpGame.Instance.OnWeaponIneffective(this); return true; }
            LabShotHits++;
            float r = 0.1f + TouchRadius + (weapon == 6 ? 0.5f : 0.15f);
            Vector3 at = hit.Pos;
            foreach (Boid b in _boids)
            {
                if (!b.Active || (b != hit && (b.Pos - at).sqrMagnitude > r * r)) continue;
                b.Health -= d;
                if (b.Health <= 0f) Kill(b, 1f, 0.1f, true);
            }

            return true;
        }

        /// <summary>CWallCrawlerSwarm::KillBoid: a burst of the swarm's particles where it was, and by chance DeathRattle / Dead to the scripts.</summary>
        private void Kill(Boid b, float deathRattleChance, float deadChance, bool burst)
        {
            b.Active = false;
            b.Launched = false;
            Show(b, false);
            Kills++;
            if (burst)
            {
                M3 o = Orientation(b.Rot);
                foreach (uint p in Particles)
                {
                    if (!MpAssets.Valid(p)) continue;
                    MpEffects.Effect e = MpEffects.Spawn(p, b.Pos, o);
                    if (e != null) _bursts.Add(new KeyValuePair<MpEffects.Effect, float>(e, 0.1f));
                }
            }

            float dead = Random.value, rattle = Random.value;
            if (rattle < deathRattleChance) Send(20);
            if (dead < deadChance) Send(MpScriptMsg.StDead);
        }

        /// <summary>The game forces a few particles out of shared generators; here each burst emits for a moment and then runs out.</summary>
        private void UpdateBursts(float dt)
        {
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                float left = _bursts[i].Value - dt;
                if (left > 0f) { _bursts[i] = new KeyValuePair<MpEffects.Effect, float>(_bursts[i].Key, left); continue; }
                if (_bursts[i].Key.Gen != null) _bursts[i].Key.Gen.SetParticleEmission(false);
                _bursts.RemoveAt(i);
            }
        }

        private static M3 Orientation(Quaternion q)
        {
            Vector3 r = q * new Vector3(1f, 0f, 0f), f = MpCrawl.Fwd(q), u = MpCrawl.Up(q);
            return new M3 { C0 = new V3(r.x, r.y, r.z), C1 = new V3(f.x, f.y, f.z), C2 = new V3(u.x, u.y, u.z) };
        }

        // ------------------------------------------------------------------ poses and drawing

        private void AdvancePoses(float dt)
        {
            if (_poses == null)
            {
                for (int i = 0; i < 4; i++) _poseDelta[i] = Vector3.zero;
                return;
            }

            for (int i = 0; i < 5; i++) _poseUsed[i] = false;
            int walking = 0;
            foreach (Boid b in _boids)
            {
                if (!b.Active) continue;
                if (b.Launched) _poseUsed[4] = true;
                else { _poseUsed[b.Index & 3] = true; walking++; }
            }

            _soundVol = NumBoids > 0 ? walking * 44 / NumBoids + 83 : 100;
            for (int i = 0; i < 5; i++)
            {
                MpAnimator a = _poses[i];
                float before = a.Time;
                a.Tick(dt);
                _poseDelta[i] = i < 4 ? MpCrawl.RootDelta(_char, _poseAnim[i], before, a.Time) : Vector3.zero;
            }
        }

        /// <summary>CWallCrawlerSwarm::UpdateEffects: a walk sound plays from the boid nearest Samus, its volume by how many boids walk.</summary>
        private void OnPoseSound(int pose, MpAnimEvents.Sound s)
        {
            if (MpGame.Instance == null || _poseUsed == null || !_poseUsed[pose]) return;
            Vector3 at;
            if (!NearestBoid(MpEnemy.PlayerPos, out at)) return;
            MpGame.Instance.PlayDataSfxAt(s.SfxId, at, Mathf.Clamp(_soundVol, 0, 127) / 127f, false, s.MaxDist > 0f ? s.MaxDist : 50f);
        }

        /// <summary>Each used pose's skinned meshes baked once (model space: the poses sit outside the world).</summary>
        private void Bake()
        {
            if (_poses == null) return;
            for (int i = 0; i < 5; i++)
            {
                if (!_poseUsed[i] && Created > 0) continue;
                MpAnimator a = _poses[i];
                for (int m = 0; m < a.Renderers.Count; m++) a.Renderers[m].BakeMesh(_baked[i][m]);
            }
        }

        private void Place()
        {
            foreach (Boid b in _boids)
            {
                if (!b.Active || b.Draw == null) continue;
                b.Draw.localPosition = b.Pos;
                b.Draw.localRotation = b.Rot;
                int pose = b.Launched ? 4 : (b.Index & 3);
                if (pose == b.Pose || b.Filters == null) continue;
                b.Pose = pose;
                for (int m = 0; m < b.Filters.Length; m++) b.Filters[m].sharedMesh = _baked[pose][m];
            }
        }

        /// <summary>A boid's drawing and shot sphere: made the first time it lives, shown while it does.</summary>
        private void Show(Boid b, bool on)
        {
            if (on && b.Draw == null)
            {
                var go = new GameObject("boid " + b.Index);
                b.Draw = go.transform;
                b.Draw.SetParent(_root, false);
                b.Draw.localScale = Vector3.one * ModelScale;
                if (_baked != null)
                {
                    b.Filters = new MeshFilter[_baked[0].Length];
                    for (int m = 0; m < b.Filters.Length; m++)
                    {
                        var part = new GameObject("mesh " + m);
                        part.transform.SetParent(b.Draw, false);
                        b.Filters[m] = part.AddComponent<MeshFilter>();
                        b.Filters[m].sharedMesh = _baked[0][m];
                        var mr = part.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = _mats[m];
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    }

                    b.Pose = 0;
                }

                var shot = new GameObject("boid " + b.Index + " shot");
                shot.layer = MpAreaView.ShotOnlyLayer;
                shot.transform.SetParent(b.Draw, false);
                var sc = shot.AddComponent<SphereCollider>();
                sc.radius = (0.1f + TouchRadius + 0.15f) / ModelScale;
                shot.AddComponent<MpHitTarget>().Entity = new BoidTarget { Swarm = this, B = b, Type = Type, Id = Id, Area = Area, Name = Name, World = World, Active = true };
            }

            if (b.Draw == null) return;
            if (on) { b.Draw.localPosition = b.Pos; b.Draw.localRotation = b.Rot; }
            if (b.Draw.gameObject.activeSelf != on) b.Draw.gameObject.SetActive(on);
        }
    }
}
