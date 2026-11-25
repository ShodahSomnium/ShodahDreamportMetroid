namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Sound effect ids the game itself uses (decomp include/MetroidPrime/SFX/Weapons.h, UI.h; CPowerBeam / CIceBeam / CWaveBeam /
    /// CPlasmaBeam fire tables, CAuxWeapon, CPlayerGun switch tables). The samples are in the disc's MusyX groups (AGSC), played by
    /// MpAudio.
    /// </summary>
    public static class MpSfx
    {
        // the beams' fire sounds: normal, charged (by EBeamId power, ice, wave, plasma)
        public static readonly int[] Fire = { 1770, 1797, 1801, 1803 };
        public static readonly int[] FireCharged = { 1767, 1776, 1845, 1840 };
        public static readonly int[] ChargeLoop = { 1766, 1759, 1844, 1839 };
        public static readonly int[] Hit = { 1771, 1798, 1802, 1804 };
        public static readonly int[] HitCharged = { 1757, 1832, 1846, 1841 };
        public static readonly int[] IntoBeam = { -1, 1821, 1827, 1825 };     // CPlayerGun mIntoBeamSound
        public const int MissileFire = 1768, MissileHit = 1764, MissileExplode = 1758, MissileEmpty = 1763, MissileLoad = 1769;
        public const int BeamChange = 1774, BeamChangeDone = 1775;
        public const int ChargeStart = -2;      // the charge loop of the current beam (MpAudio picks it)
        public const int VisorOn = 1383, VisorOff = 1382, VisorSelect = 1395, WeaponSelect = 1398;
        public const int ScanOn = 1411, ScanOff = 1409, ScanUp = 1414, ScanDone = 1417, ScanType = 1418, LockOn = 1377;
        public const int HudOn = 1401, HudDamage = 1399, Warning = 1396;
        // Samus (CPlayer::DoSfxEffects / SetHudDamage: MiscSamus.h)
        public const int PlayerDeath = 1516, HitLight = 1489, HitMed = 1511, HitHeavy = 1512, SuitHit = 1467, NearDeath = 1591;
    }
}
