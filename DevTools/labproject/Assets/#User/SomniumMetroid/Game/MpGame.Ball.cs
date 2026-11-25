using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The morph ball in the frame: the morph button (VR left X, monitor Q), steering (VR left stick, monitor WASD), and while
    /// morphed the ball is where "Samus" is for the game (PlayerGame: the rooms, triggers, enemies, pickups follow it).
    /// </summary>
    public sealed partial class MpGame
    {
        public readonly MpMorphBall Ball = new MpMorphBall();
        public readonly MpBombs Bombs = new MpBombs();
        public uint BallAncs { get { return GunAssets != null ? GunAssets.BallAncs : 0u; } }

        /// <summary>After the player's feet are known: the morph toggle, the ball's rolling, Samus's place for the game.</summary>
        private void TickBall(float dt, bool vr)
        {
            MpCinematics.Ball = Ball;
            bool toggle = false;
            Vector2 stick = Vector2.zero;
            if (vr)
            {
                MpHand l = MpXr.Hand(false);
                toggle = l.PrimaryPressed;
                stick = l.Stick;
            }
            else if (LabFire == null)
            {
                toggle = SafeKey(() => Input.GetKeyDown(KeyCode.Q));
                stick = new Vector2((SafeKey(() => Input.GetKey(KeyCode.D)) ? 1f : 0f) - (SafeKey(() => Input.GetKey(KeyCode.A)) ? 1f : 0f),
                    (SafeKey(() => Input.GetKey(KeyCode.W)) ? 1f : 0f) - (SafeKey(() => Input.GetKey(KeyCode.S)) ? 1f : 0f));
            }

            if (LabBall != null) { toggle = LabBall(this, out stick); }
            if (toggle && !MpTabletApp.IsOpen && State.Energy > 0f && !_warping)
            {
                if (!Ball.Morphed)
                {
                    if (Ball.CanMorph(State))
                    {
                        float yaw;
                        string src;
                        Ball.Morph(PlayerGame, MpPlayer.TryFacingYaw(out yaw, out src) ? -yaw : _spawnYaw);
                        if (Cannon != null) Cannon.CancelCharge();
                    }
                    else PlayUiSfx(MpSfx.MissileEmpty);
                }
                else if (Ball.CanUnmorph()) Ball.Unmorph();
                else PlayUiSfx(MpSfx.MissileEmpty);
            }

            Ball.Tick(dt, stick);
            if (Ball.Morphed && Ball.ThirdPerson && Ball.Hold.Taken)
            {
                // the avatar carried so its eyes are at the ball camera (the world stays where it is for everyone)
                Ball.Hold.Write(MpSpace.ToUnity(Ball.ViewPos) - Ball.EyeInRoot);
            }

            MpMovement.Frozen = (Ball.Morphed && Ball.ThirdPerson) || MpCinemaView.Active;

            MpMorphBall.PullTarget = null;
            if (Ball.Holding) PlayerGame = Ball.Pos - new Vector3(0f, 0f, MpMorphBall.Radius);
            Scripts.PlayerMorphed = Ball.Morphed;
        }

        /// <summary>The world as one kinematic body before it is slid around: moving it is cheap for the physics (static colliders re-built every frame are not).</summary>
        private void WorldAsBody()
        {
            if (_rideBody || MpSpace.Root == null) return;
            _rideBody = true;
            var rb = MpSpace.Root.gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = MpSpace.Root.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// <summary>Lab: drive the morph button and the stick (returns the toggle).</summary>
        public delegate bool LabBallDriver(MpGame g, out Vector2 stick);
        public static LabBallDriver LabBall;
    }

    /// <summary>
    /// CScriptBallTrigger: a volume that pulls the morph ball toward its centre (the frigate's freight clamps: the ball is drawn
    /// into the clamp's slot, where a morph-only trigger takes it).
    /// </summary>
    public sealed class MpBallTriggerEntity : MpEntity
    {
        public Vector3 Center, Size;
        public float Force;

        public static MpEntity Make(Data.MpScriptObject o)
        {
            if (o.PropCount != 9) return null;
            var r = new Data.MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            r.CString();
            var e = new MpBallTriggerEntity();
            e.Center = new Vector3(r.F32(), r.F32(), r.F32());
            e.Size = new Vector3(r.F32(), r.F32(), r.F32());
            e.Active = r.Bool();
            e.Force = r.F32();
            return e;
        }

        public override void Think(float dt)
        {
            MpGame g = MpGame.Instance;
            if (!Active || g == null || !g.Ball.Morphed) return;
            Vector3 d = g.Ball.Pos - Center, h = Size * 0.5f + Vector3.one * MpMorphBall.Radius;
            if (Mathf.Abs(d.x) > h.x || Mathf.Abs(d.y) > h.y || Mathf.Abs(d.z) > h.z) return;
            MpMorphBall.PullTarget = Center;
            MpMorphBall.PullForce = Mathf.Max(10f, Force * 0.25f);
        }
    }
}
