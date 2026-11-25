using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The Morph Ball, the Dreamport's way (the frigate needs it: the tunnel under Deck Beta Conduit Hall, the freight clamps of the
    /// Cargo Freight Lift and Reactor Core Entrance). Morphing (VR: left X; monitor: Q) leaves the player's own avatar standing
    /// where it is and puts the game's ball (SamusBallANCS, the CTweakBall gravity and the ball's top speed) at its feet; the left
    /// stick / WASD rolls it, relative to a ball camera that trails it as CBallCamera does, shown on the cinema screen in front of
    /// the player (nobody's view is moved). While morphed the game treats the ball as Samus: triggers that detect the morphed
    /// player, enemies, the rooms streaming, ball triggers pulling it. Unmorphing (where Samus fits standing) moves the avatar to
    /// the ball, facing the way the camera looks. The client's walking and turning are held off while the ball rolls.
    /// </summary>
    public sealed class MpMorphBall
    {
        public bool Morphed { get; private set; }
        public Vector3 Pos;                    // game space, the ball's centre
        public Vector3 Vel;
        public float CamYaw;                   // degrees about game Z (0 = +Y)
        private Transform _root;
        private MpAnimator _anim;
        private GameObject _staticBall;
        private Quaternion _spin = Quaternion.identity;
        private float _holdT;                  // after unmorphing: the ball stays "Samus" until the client's teleport lands
        public const float Radius = 0.7f;      // CTweakPlayer ball radius
        public float Gravity = 76f;            // CTweakBall mBallGravity
        public float MaxSpeed = 12f, Accel = 40f, Friction = 6f;
        /// <summary>The fall acceleration the rolling uses (a quarter of the tweak: the ball's feel at our scale).</summary>
        public float FallGravity { get { return Gravity * 0.25f; } }
        public static Vector3? PullTarget;     // a ball trigger's pull (MpBallTriggerEntity sets it each frame)
        public static float PullForce;

        public Vector3 ViewPos, ViewForward, ViewUp = Vector3.forward;

        public bool Holding { get { return Morphed || _holdT > 0f; } }

        /// <summary>
        /// Third person (tablet: Morph ball camera, the default): the game holds the player's avatar (MpAvatarHold, the WoW way) and
        /// carries it, hidden, so its eyes are at the chase point behind the ball - the AVATAR moves, never the world (co-op: everyone
        /// sees the same world); the client's camera, headset or desktop, becomes the ball camera.
        /// Off: the ball camera on the cinema screen, the avatar standing in view (1.1's way).
        /// </summary>
        public bool ThirdPerson { get; private set; }
        /// <summary>Third person: the avatar's feet (Unity) when it morphed - the chase point's ground is kept here.</summary>
        /// <summary>Third person: the game holds the avatar (MpAvatarHold) and carries it so its eyes are at the ball camera.</summary>
        public readonly MpAvatarHold Hold = new MpAvatarHold();
        /// <summary>Where the eyes were on the avatar's body when it morphed (head position less root position).</summary>
        public Vector3 EyeInRoot;
        public const float ChaseBack = 4.5f, ChaseUp = 1.6f;   // game units behind and above the ball (CBallCamera's default distance / elevation)

        public bool CanMorph(MpPlayerState s) { return s.Items[MpPlayerState.ItemMorphBall] > 0 || MpPlayerState.UnlockAll; }

        public void Morph(Vector3 feetGame, float yawGame)
        {
            MpGame g = MpGame.Instance;
            Pos = feetGame + new Vector3(0f, 0f, Radius + 0.05f);
            Vel = Vector3.zero;
            CamYaw = yawGame;
            Morphed = true;
            _camInit = false;
            _haveSafe = false;
            Vector3 feet;
            ThirdPerson = MpSettings.BallThirdPerson && MpPlayer.TryFeet(out feet) && MpGame.LabFocus == null;
            if (ThirdPerson)
            {
                // the AVATAR rides along the ball camera (never the world: co-op players must all see the same world)
                string why;
                Transform root = MpPlayer.BodyRoot;
                Camera head = MpPlayer.Head();
                if (root != null && head != null && Hold.TakeOver(root, out why))
                    EyeInRoot = head.transform.position - root.position;     // the eyes' place on the body now (head motion stays the player's)
                else ThirdPerson = false;
            }

            Build();
            if (_root != null) _root.gameObject.SetActive(true);
            if (g != null) g.PlaySfxAt(1478, Pos, 1f, false, 40f);        // SFXsam_b_morphin_00
            MpClientKeys.HoldMovement = true;
            MpCinematics.PlaceAgain();
            MpLog.Info("Samus: morph ball at game " + Pos);
        }

        /// <summary>True when Samus fits standing here (2.7 units of headroom over the ball).</summary>
        public bool CanUnmorph()
        {
            Vector3 from = MpSpace.ToUnity(Pos);
            RaycastHit h;
            return !MpEnemy.WorldRay(from, Vector3.up, (2.7f - Radius) * MpSpace.Scale, out h);
        }

        public void Unmorph()
        {
            MpGame g = MpGame.Instance;
            Morphed = false;
            _holdT = 0.6f;
            if (_root != null) _root.gameObject.SetActive(false);
            MpClientKeys.HoldMovement = false;
            Vector3 feet = Pos - new Vector3(0f, 0f, Radius);
            string how;
            if (ThirdPerson)
            {
                // the avatar given back standing where the ball is (the client's own teleport there)
                float yaw;
                string src;
                if (!MpPlayer.TryFacingYaw(out yaw, out src)) yaw = -CamYaw;
                Hold.Release(MpSpace.ToUnity(feet) + Vector3.up * 0.05f, yaw, MpXr.Present, out how);
                ThirdPerson = false;
            }
            else MpPlayer.Teleport(MpSpace.ToUnity(feet) + Vector3.up * 0.05f, -CamYaw, out how);
            if (g != null) g.PlaySfxAt(1479, Pos, 1f, false, 40f);         // SFXsam_b_morphout_00
            MpLog.Info("Samus: unmorphed at game " + feet + " (" + how + ")");
        }

        private void Build()
        {
            if (_root != null) return;
            _root = new GameObject("Morph Ball").transform;
            _root.SetParent(MpSpace.Root, false);
            MpCharacter ch = MpCharacter.Get(MpGame.Instance != null && MpGame.Instance.BallAncs != 0 ? MpGame.Instance.BallAncs : 0xFFFFFFFFu, 0);
            if (ch != null && ch.Model != null)
            {
                ch.Upload(true);
                _anim = new MpAnimator(ch, "Morph Ball model", _root);
                _anim.Play(0, true, 0f);
                _anim.Tick(0f);
            }
            else
            {
                _staticBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(_staticBall.GetComponent<Collider>());
                _staticBall.transform.SetParent(_root, false);
                _staticBall.transform.localScale = Vector3.one * Radius * 2f;
            }
        }

        /// <summary>The frame's rolling: input relative to the camera, gravity, friction, the world's collision (sphere casts, sliding).</summary>
        public void Tick(float dt, Vector2 stick)
        {
            if (_holdT > 0f) _holdT -= dt;
            if (!Morphed || _root == null) return;
            // the stick steers relative to the camera; in third person relative to where the player LOOKS (the headset / the
            // client's camera), while the chase camera itself trails the ball's rolling (turning the head never swings the world)
            float inYaw = CamYaw;
            if (ThirdPerson)
            {
                Camera head = MpPlayer.Head();
                if (head != null)
                {
                    Vector3 hf = head.transform.forward;
                    if (hf.x * hf.x + hf.z * hf.z > 0.01f) inYaw = -Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;
                }
            }

            float iy = inYaw * Mathf.Deg2Rad;
            Vector3 inFwd = new Vector3(-Mathf.Sin(iy), Mathf.Cos(iy), 0f), inRight = new Vector3(inFwd.y, -inFwd.x, 0f);
            Vector3 want = (inFwd * stick.y + inRight * stick.x);
            if (want.sqrMagnitude > 1f) want.Normalize();
            Vector3 flat = new Vector3(Vel.x, Vel.y, 0f);
            flat += want * Accel * dt;
            if (want.sqrMagnitude < 0.01f) flat = Vector3.MoveTowards(flat, Vector3.zero, Friction * dt);
            if (PullTarget.HasValue)
            {
                Vector3 to = PullTarget.Value - Pos;
                to.z = 0f;
                flat += to.normalized * Mathf.Min(PullForce, 30f) * dt * Mathf.Clamp01(to.magnitude);
                if (to.magnitude < 0.3f) flat *= 0.8f;
            }

            if (flat.magnitude > MaxSpeed) flat = flat.normalized * MaxSpeed;
            Vel = new Vector3(flat.x, flat.y, Vel.z - FallGravity * dt);
            Vector3 step = Vel * dt;
            Move(ref step);
            Depenetrate();
            Ground();
            Rescue();
            // CBallCamera chase: the yaw turns toward where the ball rolls, faster the faster it rolls
            if (flat.magnitude > 1f)
            {
                float wantYaw = Mathf.Atan2(-flat.x, flat.y) * Mathf.Rad2Deg;
                float rate = ThirdPerson ? 70f : 90f;
                CamYaw = Mathf.MoveTowardsAngle(CamYaw, wantYaw, rate * dt * Mathf.Clamp01(flat.magnitude / MaxSpeed));
            }

            // the ball rolls: turns about the axis across its motion
            float dist = new Vector2(step.x, step.y).magnitude;
            if (dist > 1e-4f)
            {
                Vector3 axis = Vector3.Cross(Vector3.forward, new Vector3(step.x, step.y, 0f).normalized);
                _spin = Quaternion.AngleAxis(dist / Radius * Mathf.Rad2Deg, axis) * _spin;
            }

            _root.localPosition = Pos;
            _root.localRotation = _spin;
            if (_anim != null) _anim.Tick(dt);
            Camera3(dt);
        }

        // CBallCamera (CTweakBall on the disc): elevation 2.736 units, distance 5 to 6 units by speed, look-at a unit above the ball.
        // The chase point is spring-smoothed (horizontal ~0.18 s, height ~0.35 s: bounces and bomb jumps do not jolt the view);
        // a wall between the ball and the camera pulls it in at once (never through a wall), it eases back out.
        public const float CamElevation = 2.736f, CamMinDistance = 5f, CamMaxDistance = 6f;
        private Vector3 _camSmooth, _camVelXY;
        private float _camVelZ, _camDist = CamMinDistance;
        private bool _camInit;

        private void Camera3(float dt)
        {
            float yr = CamYaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(-Mathf.Sin(yr), Mathf.Cos(yr), 0f);
            float speedFrac = Mathf.Clamp01(new Vector2(Vel.x, Vel.y).magnitude / MaxSpeed);
            Vector3 look = Pos + new Vector3(0f, 0f, 1f);
            Vector3 ideal = Pos - fwd * Mathf.Lerp(CamMinDistance, CamMaxDistance, speedFrac) + new Vector3(0f, 0f, CamElevation);
            if (!_camInit || dt <= 0f) { _camSmooth = ideal; _camVelXY = Vector3.zero; _camVelZ = 0f; _camInit = true; }
            else
            {
                Vector3 xy = Vector3.SmoothDamp(new Vector3(_camSmooth.x, _camSmooth.y, 0f), new Vector3(ideal.x, ideal.y, 0f), ref _camVelXY, 0.18f, Mathf.Infinity, dt);
                float z = Mathf.SmoothDamp(_camSmooth.z, ideal.z, ref _camVelZ, 0.35f, Mathf.Infinity, dt);
                _camSmooth = new Vector3(xy.x, xy.y, z);
            }

            // walls: pull in at once, let out slowly
            Vector3 cu = MpSpace.ToUnity(look), cd = MpSpace.ToUnity(_camSmooth) - cu;
            float want = cd.magnitude / MpSpace.Scale;
            RaycastHit h;
            if (cd.sqrMagnitude > 1e-6f && MpEnemy.WorldRay(cu, cd.normalized, cd.magnitude + 0.3f * MpSpace.Scale, out h))
                want = Mathf.Max(0.8f, h.distance / MpSpace.Scale - 0.5f);
            _camDist = want < _camDist ? want : Mathf.MoveTowards(_camDist, want, 4f * dt);
            Vector3 dir = (_camSmooth - look);
            Vector3 camPos = dir.sqrMagnitude > 1e-6f ? look + dir.normalized * Mathf.Min(_camDist, dir.magnitude) : _camSmooth;
            ViewPos = camPos;
            ViewForward = (look - camPos).sqrMagnitude > 1e-6f ? (look - camPos).normalized : fwd;
            ViewUp = Vector3.forward;
        }

        // the ball's last place on a floor: a fall far below it (through a seam, out of the map) puts it back there
        private Vector3 _safe;
        private bool _haveSafe;
        private float _safeT;

        private void Rescue()
        {
            if (_grounded)
            {
                _safeT += Time.deltaTime;
                if (_safeT > 0.25f) { _safe = Pos; _haveSafe = true; }
                return;
            }

            _safeT = 0f;
            if (_haveSafe && Pos.z < _safe.z - 25f)
            {
                MpLog.Warn("Samus: the morph ball fell out of the world at " + Pos + " - back to " + _safe);
                Pos = _safe + new Vector3(0f, 0f, 0.3f);
                Vel = Vector3.zero;
                _camInit = false;
            }
        }

        /// <summary>Moves the ball by a step through the world's collision: up to three slides along what it hits.</summary>
        private void Move(ref Vector3 step)
        {
            float r = Radius * MpSpace.Scale * 0.98f;
            for (int i = 0; i < 3 && step.sqrMagnitude > 1e-10f; i++)
            {
                Vector3 from = MpSpace.ToUnity(Pos);
                Vector3 du = MpSpace.DirToUnity(step) * MpSpace.Scale;
                float len = du.magnitude;
                RaycastHit hit;
                if (!SphereCast(from, r, du / len, len, out hit))
                {
                    Pos += step;
                    return;
                }

                float travel = Mathf.Max(0f, hit.distance - 0.002f);
                Pos += step * (travel / len);
                Vector3 n = MpSpace.DirToGame(hit.normal);
                Vector3 rest = step * (1f - travel / len);
                rest -= n * Vector3.Dot(rest, n);
                if (Vector3.Dot(Vel, n) < 0f) Vel -= n * Vector3.Dot(Vel, n);
                step = rest;
            }
        }

        private SphereCollider _probe;
        private static readonly Collider[] Overlaps = new Collider[32];

        /// <summary>Pushes the ball out of any world collision it ended inside (sphere casts do not see what they start in).</summary>
        private void Depenetrate()
        {
            if (_probe == null)
            {
                var go = new GameObject("Morph Ball probe");
                go.layer = 2;      // Ignore Raycast
                _probe = go.AddComponent<SphereCollider>();
                _probe.isTrigger = true;
            }

            float r = Radius * MpSpace.Scale;
            _probe.radius = r;
            Vector3 c = MpSpace.ToUnity(Pos);
            _probe.transform.position = c;
            for (int pass = 0; pass < 3; pass++)
            {
                int n = Physics.OverlapSphereNonAlloc(c, r, Overlaps, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    Collider col = Overlaps[i];
                    if (MpSpace.Root == null || !col.transform.IsChildOf(MpSpace.Root)) continue;
                    Vector3 dir;
                    float dist;
                    if (!Physics.ComputePenetration(_probe, c, Quaternion.identity, col, col.transform.position, col.transform.rotation, out dir, out dist)) continue;
                    c += dir * (dist + 0.001f);
                    Vector3 n2 = MpSpace.DirToGame(dir);
                    if (Vector3.Dot(Vel, n2) < 0f) Vel -= n2 * Vector3.Dot(Vel, n2);
                    moved = true;
                }

                if (!moved) break;
                _probe.transform.position = c;
            }

            Pos = MpSpace.ToGame(c);
        }

        private bool _grounded;

        /// <summary>A floor just under the ball holds it (a ray from its centre: thin floors and seams the sphere might slip through).</summary>
        private void Ground()
        {
            RaycastHit h;
            _grounded = false;
            Vector3 c = MpSpace.ToUnity(Pos + new Vector3(0f, 0f, Radius * 0.5f));
            if (!MpEnemy.WorldRay(c, Vector3.down, Radius * 1.5f * MpSpace.Scale, out h)) return;
            float floor = MpSpace.ToGame(h.point).z;
            if (Pos.z < floor + Radius) { Pos.z = floor + Radius; if (Vel.z < 0f) Vel.z = 0f; }
            _grounded = Pos.z <= floor + Radius + 0.15f;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private static bool SphereCast(Vector3 from, float r, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.SphereCastNonAlloc(from, r, dir, Hits, len, MpEnemy.WorldMask, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].distance >= bd || MpSpace.Root == null || !Hits[i].collider.transform.IsChildOf(MpSpace.Root)) continue;
                if (Hits[i].distance <= 0f && Hits[i].point == Vector3.zero) continue;      // started inside: let it out
                best = Hits[i];
                bd = Hits[i].distance;
                any = true;
            }

            return any;
        }

        public void Destroy()
        {
            if (Hold.Taken && Hold.Root != null) { string how; Hold.Release(Hold.Root.position, Hold.Root.eulerAngles.y, MpXr.Present, out how); }      // the avatar given back
            MpClientKeys.HoldMovement = false;
            if (_probe != null) Object.Destroy(_probe.gameObject);
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
