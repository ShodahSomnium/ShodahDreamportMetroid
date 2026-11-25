namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>A CDamageInfo as the tweaks store it (read_damageInfo): weapon type, damage, radius damage, radius, knockback.</summary>
    public struct MpDamage
    {
        public int Weapon;
        public float Damage, RadiusDamage, Radius, Knockback;

        public static MpDamage Read(MpReader r)
        {
            return new MpDamage { Weapon = r.S32(), Damage = r.F32(), RadiusDamage = r.F32(), Radius = r.F32(), Knockback = r.F32() };
        }
    }

    /// <summary>
    /// CTweakPlayerGun (decomp src/MetroidPrime/Tweaks/CTweakPlayerGun.cpp, read in its order): aim angles and spreads, bomb times,
    /// gun transform / holster times, the gun's position by the eye, bomb / power bomb / missile damage, per beam (power, ice, wave,
    /// plasma, phazon) the cool-down and the normal and charged damage, the five charge combos, ricochet factors.
    /// </summary>
    public sealed class MpTweakGun
    {
        public float UpLookAngle, DownLookAngle, GunTransformTime, GunHolsterTime, GunNotFiringTime, GunExtendDistance;
        public float GunPosX, GunPosY, GunPosZ, FixedVerticalAim;
        /// <summary>Seconds a bomb burns before it explodes; seconds before the three bombs come back after the first of three is dropped.</summary>
        public float BombFuseTime = 1f, BombDropDelayTime = 1f;
        public MpDamage Bomb, PowerBomb, Missile;
        public readonly float[] CoolDown = new float[5];
        public readonly MpDamage[] Normal = new MpDamage[5];
        public readonly MpDamage[] Charged = new MpDamage[5];
        public readonly MpDamage[] Combo = new MpDamage[5];

        public static MpTweakGun Read(byte[] b)
        {
            var t = new MpTweakGun();
            var r = new MpReader(b);
            t.UpLookAngle = r.F32();
            t.DownLookAngle = r.F32();
            r.Skip(4 * 8);               // spreads, aim speeds
            t.BombFuseTime = r.F32();
            t.BombDropDelayTime = r.F32();
            r.Skip(4);                   // holo hold
            t.GunTransformTime = r.F32();
            t.GunHolsterTime = r.F32();
            t.GunNotFiringTime = r.F32();
            t.FixedVerticalAim = r.F32();
            t.GunExtendDistance = r.F32();
            t.GunPosX = r.F32();
            t.GunPosY = r.F32();
            t.GunPosZ = r.F32();
            r.Skip(12);                  // x58
            r.Skip(12);                  // grappling arm position
            t.Bomb = MpDamage.Read(r);
            t.PowerBomb = MpDamage.Read(r);
            t.Missile = MpDamage.Read(r);
            for (int i = 0; i < 5; i++)
            {
                t.CoolDown[i] = r.F32();
                t.Normal[i] = MpDamage.Read(r);
                t.Charged[i] = MpDamage.Read(r);
            }

            for (int i = 0; i < 5; i++) t.Combo[i] = MpDamage.Read(r);
            return t;
        }
    }
}
