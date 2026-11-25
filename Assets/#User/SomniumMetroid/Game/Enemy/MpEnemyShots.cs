using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Enemy projectiles (CEnergyProjectile fired by CPatterned / turrets): the game's own WPSC weapon (its particle systems, trails
    /// and motion run by MpProjectileWeapon), checked each frame along its path against the world's collision and Samus (a capsule
    /// of her radius and height); on Samus it takes the damage, on the world it shows its collision response. Also effects-only
    /// helpers: <see cref="PrepareWeapon"/> / <see cref="PreparePart"/> preload a WPSC or PART (and everything it spawns) on the
    /// loader thread so the first shot does not stall.
    /// </summary>
    public static class MpEnemyShots
    {
        private sealed class Shot
        {
            public MpProjectileWeapon W;
            public float Damage;
            public Vector3 Prev;
            public bool Hit;
            public MpEntity Owner;
            public float Age, MaxAge;
        }

        private static readonly List<Shot> Shots = new List<Shot>();
        public static int Fired, HitPlayer;
        public const float PlayerRadius = 0.5f, PlayerHeight = 2.7f;

        /// <summary>Loader thread: a weapon's systems, swooshes and collision response effects.</summary>
        public static void PrepareWeapon(MpAssets assets, uint wpsc)
        {
            if (!MpAssets.Valid(wpsc)) return;
            if (MpEffects.Lib == null) MpEffects.Lib = new MpPartLibrary(assets);
            MpWeaponDescFull w = MpEffects.Lib.Weapon(wpsc);
            if (w == null) return;
            var parts = new List<uint> { w.APSM, w.APS2 };
            var sw = new List<uint> { w.ASW1, w.ASW2, w.ASW3 };
            MpCollisionResponse cr = MpEffects.Lib.Response(w.COLR);
            if (cr != null) foreach (uint p in cr.Particles) if (MpAssets.Valid(p)) parts.Add(p);
            MpEffects.Prepare(assets, parts, sw);
        }

        /// <summary>Loader thread: a PART and everything it spawns.</summary>
        public static void PreparePart(MpAssets assets, uint part)
        {
            if (!MpAssets.Valid(part)) return;
            if (MpEffects.Lib == null) MpEffects.Lib = new MpPartLibrary(assets);
            MpEffects.Prepare(assets, new[] { part }, new uint[0]);
        }

        /// <summary>Fires a WPSC from a game point along a game direction. False when the weapon cannot be read.</summary>
        /// <summary>Co-op, the master: an enemy fired (weapon, from, direction, damage, scale) - every helper flies the same shot, which hurts its own Samus.</summary>
        public static System.Action<uint, Vector3, Vector3, float, float> OnFire;

        public static bool Fire(uint wpsc, Vector3 fromGame, Vector3 dirGame, float damage, MpEntity owner, float scale = 1f)
        {
            if (MpEffects.Lib == null || !MpAssets.Valid(wpsc)) return false;
            if (OnFire != null && owner != null) OnFire(wpsc, fromGame, dirGame, damage, scale);
            MpWeaponDescFull desc = MpEffects.Lib.Weapon(wpsc);
            if (desc == null) return false;
            Vector3 du = MpSpace.DirToUnity(dirGame.sqrMagnitude > 1e-8f ? dirGame.normalized : Vector3.up);
            M3 orient = MpEffects.GameOrientation(Quaternion.LookRotation(du));
            var w = new MpProjectileWeapon(desc, MpEffects.Lib, orient, new V3(fromGame.x, fromGame.y, fromGame.z), V3.Zero, new V3(scale, scale, scale));
            Shots.Add(new Shot { W = w, Damage = damage, Prev = MpSpace.ToUnity(fromGame), Owner = owner, MaxAge = 10f });
            MpEffects.Projectiles.Add(w);
            Fired++;
            return true;
        }

        public static void Tick(float dt)
        {
            MpGame g = MpGame.Instance;
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                Shot s = Shots[i];
                s.Age += dt;
                s.W.Update(dt);
                if (!s.Hit && s.W.Active)
                {
                    V3 t = s.W.Translation;
                    Vector3 now = MpSpace.ToUnity(t.X, t.Y, t.Z);
                    Vector3 step = now - s.Prev;
                    float len = step.magnitude;
                    if (len > 1e-5f)
                    {
                        float hitPlayerAt = PlayerHitAlong(s.Prev, now);
                        RaycastHit hit;
                        bool world = MpEnemy.ShotWorldRay(s.Prev, step / len, len, out hit);
                        if (hitPlayerAt >= 0f && (!world || hitPlayerAt <= hit.distance))
                        {
                            s.Hit = true;
                            Vector3 at = MpSpace.ToGame(s.Prev + step / len * hitPlayerAt);
                            s.W.Hit(new V3(at.x, at.y, at.z));
                            if (g != null && g.DamagePlayer(s.Damage, MpSpace.ToGame(s.Prev))) HitPlayer++;
                        }
                        else if (world)
                        {
                            s.Hit = true;
                            Vector3 at = MpSpace.ToGame(hit.point);
                            s.W.Hit(new V3(at.x, at.y, at.z));
                            Impact(s, hit);
                        }
                    }

                    s.Prev = now;
                }

                s.W.UpdateParticleFX();
                if (s.W.IsSystemDeletable || s.Age > s.MaxAge)
                {
                    MpEffects.Projectiles.Remove(s.W);
                    Shots.RemoveAt(i);
                }
            }
        }

        /// <summary>Unity distance along a segment where it enters Samus's capsule, or -1.</summary>
        private static float PlayerHitAlong(Vector3 a, Vector3 b)
        {
            MpGame g = MpGame.Instance;
            if (g == null || g.State == null || g.State.Energy <= 0f) return -1f;
            float r = PlayerRadius * MpSpace.Scale;
            Vector3 feet = MpSpace.ToUnity(g.PlayerGame);
            Vector3 top = feet + Vector3.up * (PlayerHeight * MpSpace.Scale);
            Vector3 d = b - a;
            float len = d.magnitude;
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / (r * 0.5f)), 1, 64);
            for (int k = 0; k <= steps; k++)
            {
                Vector3 p = a + d * (k / (float)steps);
                float y = Mathf.Clamp(p.y, feet.y + r, top.y - r);
                if ((p - new Vector3(feet.x, y, feet.z)).sqrMagnitude <= r * r) return len * (k / (float)steps);
            }

            return -1f;
        }

        private static void Impact(Shot s, RaycastHit hit)
        {
            MpCollisionResponse cr = MpEffects.Lib != null ? MpEffects.Lib.Response(s.W.Desc.COLR) : null;
            if (cr == null) return;
            Vector3 gp = MpSpace.ToGame(hit.point);
            uint part = cr.ParticleFor(MpProjectiles.ResponseFor(MpAreaView.MaterialAt(hit)));
            if (MpAssets.Valid(part)) MpEffects.Spawn(part, gp, MpEffects.GameOrientation(Quaternion.LookRotation(hit.normal)));
            int sfx = cr.SoundFor(MpProjectiles.ResponseFor(MpAreaView.MaterialAt(hit)));
            if (sfx >= 0 && MpGame.Instance != null) MpGame.Instance.PlayDataSfxAt(sfx, gp, 1f, false, cr.AudibleRange);
        }

        public static void Clear()
        {
            foreach (Shot s in Shots) MpEffects.Projectiles.Remove(s.W);
            Shots.Clear();
        }
    }
}
