using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The player's shots, the game's way: each is a CProjectileWeapon (Data/Particles/MpProjectileWeapon) from the beam's normal or
    /// charged WPSC (the missile's own) - it flies by its IVEC / PSVM at 60 Hz in the frame it was fired in, carrying its APSM / APS2
    /// systems and swooshes; each frame the segment it moved is cast against the area's shot collision, actor / lock boxes (closed
    /// doors let shots through unless set otherwise). A hit applies the PlayerGun tweak's damage through the target's vulnerability,
    /// stops the shot's systems and starts the weapon's collision response (CRSC) for the surface hit (its world material table) with
    /// its sound; the shot is gone once its systems have finished.
    /// </summary>
    public sealed class MpProjectiles
    {
        private sealed class Shot
        {
            public MpProjectileWeapon W;
            public int Beam;               // 0..3, 4 = missile
            public bool Charged, Hit;
            public float Damage;
            public Vector3 Prev;           // Unity, last frame's translation
        }

        private readonly List<Shot> _shots = new List<Shot>();
        private readonly MpGunAssets _assets;
        public const int HitMask = (1 << 0) | (1 << MpAreaView.ShotOnlyLayer);
        public int Fired, Hits;
        public string LastHit = "";      // what the last shot stopped on (the labs log it)

        public MpProjectiles(MpGunAssets assets) { _assets = assets; }

        /// <summary>A light colour per beam (the cannon's charge light; the shots light the world with their own systems' lights).</summary>
        public static Color BeamColor(int beam)
        {
            switch (beam)
            {
                case 0: return new Color(1f, 0.78f, 0.35f, 1f);
                case 1: return new Color(0.55f, 0.85f, 1f, 1f);
                case 2: return new Color(0.82f, 0.45f, 1f, 1f);
                case 3: return new Color(1f, 0.42f, 0.12f, 1f);
                default: return new Color(1f, 0.6f, 0.25f, 1f);
            }
        }

        private bool Launch(MpWeaponDescFull desc, int beam, bool charged, float damage, Vector3 muzzle, Vector3 dir, float scale = 1f, bool partial = false)
        {
            if (desc == null || MpEffects.Lib == null) return false;
            M3 orient = MpEffects.GameOrientation(Quaternion.LookRotation(dir.sqrMagnitude > 1e-8f ? dir : Vector3.forward));
            Vector3 g = MpSpace.ToGame(muzzle);
            var w = new MpProjectileWeapon(desc, MpEffects.Lib, orient, new V3(g.x, g.y, g.z), V3.Zero, new V3(scale, scale, scale), partial);
            var s = new Shot { W = w, Beam = beam, Charged = charged, Damage = damage, Prev = muzzle };
            _shots.Add(s);
            MpEffects.Projectiles.Add(w);
            Fired++;
            return true;
        }

        /// <summary>CGunWeapon::Fire: a charged shot's damage and size go with the charge factor; short of 1 it is a partial charge (OPTS).</summary>
        public void FireBeam(int beam, bool charged, Vector3 muzzle, Vector3 dir, float chargeFactor = 1f)
        {
            float dmg = 2f;
            if (_assets.Tweak != null) dmg = charged ? _assets.Tweak.Charged[beam].Damage * chargeFactor : _assets.Tweak.Normal[beam].Damage;
            Launch(charged ? _assets.ChargedWeapon[beam] : _assets.NormalWeapon[beam], beam, charged, dmg, muzzle, dir,
                charged ? chargeFactor : 1f, charged && Mathf.Abs(chargeFactor - 1f) > 1e-5f);
            MpGame.Instance.PlaySfxAt(charged ? MpSfx.FireCharged[beam] : MpSfx.Fire[beam], MpSpace.ToGame(muzzle), 1f, false, 60f);
            if (MpXr.Present) MpXr.Pulse(true, charged ? 0.9f : 0.45f, charged ? 0.12f : 0.05f);
        }

        public void FireMissile(Vector3 muzzle, Vector3 dir)
        {
            float dmg = _assets.Tweak != null ? _assets.Tweak.Missile.Damage : 30f;
            Launch(_assets.MissileWeapon, 4, false, dmg, muzzle, dir);
            MpGame.Instance.PlaySfxAt(MpSfx.MissileFire, MpSpace.ToGame(muzzle), 1f, false, 60f);
            if (MpXr.Present) MpXr.Pulse(true, 0.8f, 0.1f);
        }

        private static Vector3 U(V3 g) { return MpSpace.ToUnity(g.X, g.Y, g.Z); }

        public void Tick(float dt)
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                Shot s = _shots[i];
                s.W.Update(dt);
                if (!s.Hit && s.W.Active)
                {
                    Vector3 now = U(s.W.Translation);
                    Vector3 step = now - s.Prev;
                    float len = step.magnitude;
                    RaycastHit hit;
                    if (len > 1e-5f && ShotRay(s.Prev, step / len, len, out hit, s.Beam == 4 ? 6 : MpPlayerState.WeaponOf(s.Beam), s.Charged))
                    {
                        s.Hit = true;
                        Vector3 g = MpSpace.ToGame(hit.point);
                        s.W.Hit(new V3(g.x, g.y, g.z));
                        Impact(s, hit);
                    }

                    s.Prev = now;
                }

                s.W.UpdateParticleFX();
                if (s.W.IsSystemDeletable)
                {
                    MpEffects.Projectiles.Remove(s.W);
                    _shots.RemoveAt(i);
                }
            }
        }

        private static readonly RaycastHit[] RayHits = new RaycastHit[24];

        /// <summary>
        /// The nearest thing a shot hits along a segment: the area's shot collision, actor / lock boxes - but not a closed door whose
        /// projectiles-collide flag is off (CGameProjectile asks CScriptDoor::GetProjectileBounds: such a door lets shots through to
        /// its lock and beyond).
        /// </summary>
        public static bool ShotRay(Vector3 from, Vector3 dir, float len, out RaycastHit best, int weapon = 0, bool charged = false)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(from, dir, RayHits, len, HitMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = RayHits[i];
                if (h.distance >= bestD) continue;
                MpDoorShotGate gate = h.collider.GetComponent<MpDoorShotGate>();
                if (gate != null && !gate.Blocks) continue;
                if (PassesThrough(h.collider, weapon, charged)) continue;
                best = h;
                bestD = h.distance;
                any = true;
            }

            return any;
        }

        /// <summary>CGameProjectile::CanCollideWith: an actor or trigger whose vulnerability to this weapon is pass-through lets the shot through.</summary>
        private static bool PassesThrough(Collider c, int weapon, bool charged)
        {
            MpVulnerability v = null;
            MpShotVulnerability a = c.GetComponent<MpShotVulnerability>();
            if (a != null && a.Entity != null) v = a.Entity.Vuln;
            else
            {
                MpHitTarget t = c.GetComponentInParent<MpHitTarget>();
                var dt = t != null ? t.Entity as MpDamageableTriggerEntity : null;
                if (dt != null) v = dt.Vuln;
            }

            return v != null && v.Of(weapon, charged) == MpVulnerability.PassThrough;
        }

        /// <summary>The world collision response for a surface (CCollisionResponseData::skWorldMaterialTable by the material's lowest surface bit).</summary>
        private static readonly int[] WorldTable = { 1, 2, 3, 4, 5, 6, 3, 7, 4, 10, 10, 5, 9, 3, 1, 6, 6, 11, 1, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        private const uint CheckMaterial = 0x3FFFE | (1u << 21) | (1u << 22) | (1u << 23);

        public static int ResponseFor(uint material)
        {
            uint m = material & CheckMaterial;
            for (int i = 0; i < 32; i++) if ((m & (1u << i)) != 0) return WorldTable[i];
            return MpCollisionResponse.Default;
        }

        private void Impact(Shot s, RaycastHit hit)
        {
            Hits++;
            LastHit = hit.collider.name + " @" + hit.point.ToString("0.00");
            var target = hit.collider.GetComponentInParent<MpHitTarget>();
            if (target != null && target.Entity != null)
            {
                int weapon = s.Beam == 4 ? 6 : MpPlayerState.WeaponOf(s.Beam);
                bool took = target.Entity.TakeDamage(weapon, s.Charged, s.Damage);
                LastHit += " on " + target.Entity + (took ? " (took " + s.Damage.ToString("0.0") + ")" : " (refused)");
            }

            int response = target != null ? MpCollisionResponse.Default : ResponseFor(MpAreaView.MaterialAt(hit));
            MpCollisionResponse cr = MpEffects.Lib != null ? MpEffects.Lib.Response(s.W.Desc.COLR) : null;
            Vector3 gp = MpSpace.ToGame(hit.point);
            if (cr != null)
            {
                uint part = cr.ParticleFor(response);
                if (MpAssets.Valid(part)) MpEffects.Spawn(part, gp, MpEffects.GameOrientation(Quaternion.LookRotation(hit.normal)));
            }

            int sfx = cr != null ? cr.SoundFor(response) : -1;
            if (sfx >= 0) MpGame.Instance.PlayDataSfxAt(sfx, gp, 1f, false, cr.AudibleRange);
            else MpGame.Instance.PlaySfxAt(s.Beam == 4 ? MpSfx.MissileExplode : (s.Charged ? MpSfx.HitCharged[s.Beam] : MpSfx.Hit[s.Beam]), gp, 1f, false, 60f);
        }

        public void Clear()
        {
            foreach (Shot s in _shots) MpEffects.Projectiles.Remove(s.W);
            _shots.Clear();
        }
    }
}
