using SomniumSpace.Worlds.SomniumMetroid.Data;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Morph Ball bombs (CPlayerGun::DropBomb, CBomb, CPlayer::BombJump): fire while morphed drops a bomb at the ball's centre;
    /// three at a time, the three come back CTweakPlayerGun mBombDropDelayTime after the first of three is dropped; each burns
    /// mBombFuseTime (the GunRes "bomb set" PART, the explosion PART for its last half second), then explodes: SFXsam_misl_expl_01,
    /// the tweak's bomb damage (weapon 4) to everything shootable in its radius, triggers that detect bombs (kTFL_DetectBombs) get
    /// Entered / Exited, and the ball, when within CTweakPlayer mBombJumpHeight (1.5) of it and not under it, is thrown up at
    /// sqrt(2 g mBombJumpRadius) - the game's names are swapped (g 35, 7.9: about 3.6 units under the ball's gravity).
    /// </summary>
    public sealed class MpBombs
    {
        private sealed class Bomb
        {
            public Vector3 Pos;               // game space
            public float Fuse;
            public bool Exploded;
            public MpEffects.Effect Set, Blast;
            public float Gone;
            public bool Ghost;                // another player's bomb (MpCoop): seen and heard; on the master it still touches bomb triggers
        }

        /// <summary>Co-op: this player dropped a bomb (game position); MpCoop tells the others.</summary>
        public static System.Action<Vector3> OnDropped;

        public const int Max = 3;
        public const uint DetectBombs = 1 << 6;
        public const float JumpReach = 1.5f, JumpRise = 3.64f;   // CTweakPlayer mBombJumpHeight; the rise of sqrt(2 * 35 * 7.9) under ball gravity 76
        private readonly List<Bomb> _live = new List<Bomb>();
        private readonly HashSet<MpEntity> _hit = new HashSet<MpEntity>();
        private readonly List<MpTriggerEntity> _touched = new List<MpTriggerEntity>();
        private static readonly Collider[] Overlaps = new Collider[64];
        public int Count = Max;
        private float _refill;
        public int Dropped, Exploded, Jumps;

        public bool CanDrop(MpPlayerState s)
        {
            return (s.Items[MpPlayerState.ItemBombs] > 0 || MpPlayerState.UnlockAll) && Count > 0;
        }

        public void Drop(Vector3 ballCentre, MpGunAssets assets)
        {
            if (Count <= 0) return;
            float fuse = assets != null && assets.Tweak != null && assets.Tweak.BombFuseTime > 0f ? assets.Tweak.BombFuseTime : 1f;
            var b = new Bomb { Pos = ballCentre, Fuse = fuse };
            if (assets != null) b.Set = MpEffects.Spawn(assets.BombSetPart, b.Pos, Data.Particles.M3.Identity, true);
            _live.Add(b);
            if (OnDropped != null) OnDropped(ballCentre);
            if (Count == Max) _refill = assets != null && assets.Tweak != null && assets.Tweak.BombDropDelayTime > 0f ? assets.Tweak.BombDropDelayTime : 1f;
            Count--;
            Dropped++;
            MpGame g = MpGame.Instance;
            if (g != null) g.PlaySfxAt(1756, b.Pos, 1f, false, 40f);      // SFXsam_a_bombset_00
        }

        /// <summary>Another player's bomb (MpCoop): its fuse, set and blast effects and sounds; no damage from here.</summary>
        public void DropGhost(Vector3 at, MpGunAssets assets)
        {
            float fuse = assets != null && assets.Tweak != null && assets.Tweak.BombFuseTime > 0f ? assets.Tweak.BombFuseTime : 1f;
            var b = new Bomb { Pos = at, Fuse = fuse, Ghost = true };
            if (assets != null) b.Set = MpEffects.Spawn(assets.BombSetPart, b.Pos, Data.Particles.M3.Identity, true);
            _live.Add(b);
            MpGame g = MpGame.Instance;
            if (g != null) g.PlaySfxAt(1756, b.Pos, 1f, false, 40f);
        }

        public void Tick(float dt, MpGame g)
        {
            // last frame's bomb touches leave their triggers
            for (int i = 0; i < _touched.Count; i++) if (_touched[i] != null && _touched[i].Active) _touched[i].Send(MpScriptMsg.StExited);
            _touched.Clear();
            if (_refill > 0f)
            {
                _refill -= dt;
                if (_refill <= 0f) Count = Max;
            }

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Bomb b = _live[i];
                if (!b.Exploded)
                {
                    b.Fuse -= dt;
                    if (b.Fuse <= 0.5f && b.Blast == null && g != null && g.GunAssets != null)
                    {
                        if (b.Set != null) { b.Set.Finish(); b.Set = null; }
                        b.Blast = MpEffects.Spawn(g.GunAssets.BombExplodePart, b.Pos, Data.Particles.M3.Identity);
                    }

                    if (b.Fuse <= 0f) Explode(b, g);
                }
                else
                {
                    b.Gone -= dt;
                    if (b.Gone <= 0f) _live.RemoveAt(i);
                }
            }
        }

        private void Explode(Bomb b, MpGame g)
        {
            b.Exploded = true;
            b.Gone = 2f;
            Exploded++;
            if (b.Set != null) { b.Set.Finish(); b.Set = null; }
            if (g == null) return;
            g.PlaySfxAt(1758, b.Pos, 1f, false, 60f);                       // SFXsam_misl_expl_01
            MpDamage d = g.GunAssets != null && g.GunAssets.Tweak != null ? g.GunAssets.Tweak.Bomb : default(MpDamage);
            float radius = d.Radius > 0f ? d.Radius : 3f, damage = d.Damage > 0f ? d.Damage : 10f;

            // CStateManager::ApplyDamageToWorld: everything shootable in the radius
            _hit.Clear();
            int n = Physics.OverlapSphereNonAlloc(MpSpace.ToUnity(b.Pos), radius * MpSpace.Scale, Overlaps, MpProjectiles.HitMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                MpHitTarget t = Overlaps[i] != null ? Overlaps[i].GetComponentInParent<MpHitTarget>() : null;
                if (b.Ghost || t == null || t.Entity == null || !_hit.Add(t.Entity)) continue;
                if (MpProjectiles.HitHook != null) MpProjectiles.HitHook(t.Entity, 4, false, damage);
                else t.Entity.TakeDamage(4, false, damage);
            }

            // triggers that detect bombs
            if (g.Scripts != null && !MpScripts.Follower)
                foreach (MpEntity e in g.Scripts.All)
                {
                    var tr = e as MpTriggerEntity;
                    if (tr == null || !tr.Active || (tr.Flags & DetectBombs) == 0) continue;
                    Vector3 c = Vector3.Max(tr.Min, Vector3.Min(tr.Max, b.Pos));
                    if ((c - b.Pos).sqrMagnitude > radius * radius) continue;
                    tr.Send(MpScriptMsg.StEntered);
                    _touched.Add(tr);
                }

            // CPlayer::BombJump
            MpMorphBall ball = g.Ball;
            if (ball != null && ball.Morphed && !b.Ghost)
            {
                Vector3 to = ball.Pos - b.Pos;
                if (to.sqrMagnitude < JumpReach * JumpReach && to.z >= -MpMorphBall.Radius)
                {
                    ball.Vel.z = Mathf.Sqrt(2f * ball.FallGravity * JumpRise);
                    Jumps++;
                    MpXr.Pulse(false, 0.3f, 0.1f);
                }
            }
        }

        public void Clear()
        {
            foreach (Bomb b in _live)
            {
                if (b.Set != null) b.Set.Kill();
                if (b.Blast != null) b.Blast.Kill();
            }

            _live.Clear();
            _touched.Clear();
            Count = Max;
            _refill = 0f;
        }
    }
}
