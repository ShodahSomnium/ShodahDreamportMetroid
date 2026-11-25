using SomniumSpace.Worlds.SomniumMetroid.Data;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CSpacePirate::CSpacePirateData (CSpacePirate.cpp, read in its order after the patterned info and actor parameters):
    /// aggression / cover / search / fall-back / hearing checks, the behaviour flags, the arm cannon's projectile (WPSC + damage) and
    /// its sound, the blade (melee) damage, the kneel attack, dodge chance, impact / alert / hurled / death sounds (data ids: through
    /// the sound lookup), the gap between burst shots, the gun track delay, the count of opening bursts, cloak opacity and dodge delays.
    /// </summary>
    public sealed class MpPirateData
    {
        public const uint PendingAmbush = 0x1, CeilingAmbush = 0x2, NonAggressive = 0x4, Melee = 0x8, NoShuffleCloseCheck = 0x10,
            OnlyAttackInRange = 0x20, NoKnockbackImpulseReset = 0x80, NoMeleeAttack = 0x200, BreakAttack = 0x400, Seated = 0x1000,
            ShadowPirate = 0x2000, AlertBeforeCloak = 0x4000, NoBreakDodge = 0x8000, FloatingCorpse = 0x10000,
            RagdollNoAiCollision = 0x20000, Trooper = 0x40000;

        public float AggressionCheck, CoverCheck, SearchRadius, FallBackCheck, FallBackRadius, HearingRadius;
        public uint Flags;
        public uint Projectile = 0xFFFFFFFF, KneelShot = 0xFFFFFFFF;
        public MpDamageInfo ProjectileDamage, BladeDamage, KneelDamage;
        public int SfxProjectile = -1, SfxImpact = -1, SfxAlert = -1, SfxHurled = -1, SfxDeath = -1;
        public float KneelAttackChance, DodgeCheck, AverageNextShotTime, NextShotTimeVariation, GunTrackDelay;
        public int FirstBurstCount;
        public float CloakOpacity, MaxCloakOpacity, DodgeDelayMin, DodgeDelayMax, Unk = 0.2f, AvoidDistance = 8f;

        public bool Has(uint flag) { return (Flags & flag) != 0; }

        public static MpPirateData Read(MpReader r, int propCount)
        {
            var d = new MpPirateData();
            d.AggressionCheck = r.F32(); d.CoverCheck = r.F32(); d.SearchRadius = r.F32();
            d.FallBackCheck = r.F32(); d.FallBackRadius = r.F32(); d.HearingRadius = r.F32();
            d.Flags = r.U32();
            r.Bool();
            d.Projectile = r.U32();
            d.ProjectileDamage = MpDamageInfo.Read(r);
            d.SfxProjectile = r.S32();
            d.BladeDamage = MpDamageInfo.Read(r);
            d.KneelAttackChance = r.F32();
            d.KneelShot = r.U32();
            d.KneelDamage = MpDamageInfo.Read(r);
            d.DodgeCheck = r.F32();
            d.SfxImpact = r.S32();
            d.AverageNextShotTime = r.F32(); d.NextShotTimeVariation = r.F32();
            d.SfxAlert = r.S32();
            d.GunTrackDelay = r.F32();
            d.FirstBurstCount = r.S32();
            d.CloakOpacity = r.F32(); d.MaxCloakOpacity = r.F32();
            d.DodgeDelayMin = r.F32(); d.DodgeDelayMax = r.F32();
            d.SfxHurled = r.S32();
            d.SfxDeath = r.S32();
            if (propCount >= 35) d.Unk = r.F32();
            if (propCount >= 36) d.AvoidDistance = r.F32();
            return d;
        }
    }

    /// <summary>
    /// ScriptLoader::LoadGunTurret + CScriptGunTurretData (CScriptGunTurret.cpp): name, component (0 base, 1 gun), transform, scale,
    /// collision extent / offset, animation parameters, actor parameters, health, vulnerability, then the turret data: into
    /// deactivate / activate delays, reload time and variance, pan start / hold times, left / right / down limits and turn speed
    /// (degrees here), detection range and height, freeze time, the projectile (WPSC + damage), the light / frozen / charging /
    /// panning / visor effects, the tracking / lock-on / unfreeze / stop clank / charging / visor sounds (data ids), the extension
    /// model and drop, initial and later burst sizes, the frenzy time and scripted start only.
    /// </summary>
    public sealed class MpTurretData
    {
        public string Name;
        public int Component;
        public MpVec3 Pos, Rot, Scale, CollisionExtent, CollisionOffset;
        public MpModelRef Anim;
        public MpActorParams Params;
        public float Health, KnockbackResistance;
        public MpVulnerability Vuln;

        public float IntoDeactivateDelay, IntoActivateDelay, ReloadTime, ReloadTimeVariance, PanStartTime, PanHoldTime;
        public float PanTotalSearchTime = 30f;
        public float LeftMaxAngle, RightMaxAngle, DownMaxAngle, TurnSpeed, DetectionRange, DetectionZRange, FreezeDuration, FreezeVariance;
        public bool FreezeTimeout;
        public uint Projectile = 0xFFFFFFFF;
        public MpDamageInfo ProjectileDamage;
        public uint IdleLight = 0xFFFFFFFF, DeactivateLight = 0xFFFFFFFF, TargettingLight = 0xFFFFFFFF, FrozenEffect = 0xFFFFFFFF,
            ChargingEffect = 0xFFFFFFFF, PanningEffect = 0xFFFFFFFF, VisorEffect = 0xFFFFFFFF;
        public int SfxTracking = -1, SfxLockOn = -1, SfxUnfreeze = -1, SfxStopClank = -1, SfxCharging = -1, SfxVisor = -1;
        public uint ExtensionModel = 0xFFFFFFFF;
        public float ExtensionDropDownDist;
        public int NumInitialShots, InitialShotTableIndex, NumSubsequentShots;
        public float FrenzyDuration = 3f;
        public bool ScriptedStartOnly;

        public bool IsGun { get { return Component == 1; } }

        public static MpTurretData Read(MpScriptObject o)
        {
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            int pc = o.PropCount;
            var d = new MpTurretData { Name = r.CString() };
            d.Component = r.S32();
            d.Pos = MpVec3.Read(r); d.Rot = MpVec3.Read(r); d.Scale = MpVec3.Read(r);
            d.CollisionExtent = MpVec3.Read(r); d.CollisionOffset = MpVec3.Read(r);
            d.Anim = MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
            d.Params = MpScriptRead.ReadActorParams(r);
            r.U32();
            d.Health = r.F32();
            d.KnockbackResistance = r.F32();
            d.Vuln = MpScriptRead.ReadVulnerability(r);
            d.IntoDeactivateDelay = r.F32(); d.IntoActivateDelay = r.F32();
            d.ReloadTime = r.F32(); d.ReloadTimeVariance = r.F32();
            d.PanStartTime = r.F32(); d.PanHoldTime = r.F32();
            d.LeftMaxAngle = r.F32(); d.RightMaxAngle = r.F32(); d.DownMaxAngle = r.F32(); d.TurnSpeed = r.F32();
            d.DetectionRange = r.F32(); d.DetectionZRange = r.F32();
            d.FreezeDuration = r.F32(); d.FreezeVariance = r.F32();
            d.FreezeTimeout = pc >= 48 && r.Bool();
            d.Projectile = r.U32();
            d.ProjectileDamage = MpDamageInfo.Read(r);
            d.IdleLight = r.U32(); d.DeactivateLight = r.U32(); d.TargettingLight = r.U32();
            d.FrozenEffect = r.U32(); d.ChargingEffect = r.U32(); d.PanningEffect = r.U32();
            if (pc >= 44) d.VisorEffect = r.U32();
            d.SfxTracking = r.S32(); d.SfxLockOn = r.S32(); d.SfxUnfreeze = r.S32(); d.SfxStopClank = r.S32(); d.SfxCharging = r.S32();
            if (pc >= 45) d.SfxVisor = r.S32();
            d.ExtensionModel = r.U32();
            d.ExtensionDropDownDist = r.F32();
            d.NumInitialShots = r.S32(); d.InitialShotTableIndex = r.S32(); d.NumSubsequentShots = r.S32();
            if (pc >= 47) d.FrenzyDuration = r.F32();
            if (pc >= 46) d.ScriptedStartOnly = r.Bool();
            return d;
        }
    }

    /// <summary>
    /// CBurstFire: a table of bursts (a random selection weight, up to 8 shot angles around the target - 0 or less ends the burst -,
    /// time to the next shot and its variance); <see cref="Start"/> picks a burst (the first few times the scripted first one),
    /// <see cref="Update"/> says when to fire, <see cref="GetDistanceCompensatedError"/> the aim error of the current shot (around
    /// Samus: x to the side, z up; full beyond the given distance).
    /// </summary>
    public sealed class MpBurstFire
    {
        public sealed class Burst
        {
            public int Weight;
            public int[] Angles;
            public float Next, NextVar;
            public Burst(int w, int[] a, float n, float v) { Weight = w; Angles = a; Next = n; NextVar = v; }
        }

        public int BurstType = -1;
        public int FirstBurstIndex;
        private int _angleIdx = -1, _firstCounter;
        private float _timeToNext;
        public bool ShouldFire, AvoidAccuracy;
        private Burst _cur;
        private readonly Burst[][] _defs;
        private readonly System.Random _rnd;

        public MpBurstFire(Burst[][] defs, int firstBurstCount, int seed)
        {
            _defs = defs;
            _firstCounter = firstBurstCount;
            _rnd = new System.Random(seed);
        }

        public bool IsBurstSet { get { return _cur != null; } }

        public void SetTimeToNextShot(float t) { if (_timeToNext > 0f) _timeToNext = t; }

        public float Rand() { return (float)_rnd.NextDouble(); }

        public void Start()
        {
            if (BurstType < 0 || BurstType >= _defs.Length) BurstType = 0;
            Burst[] bursts = _defs[BurstType];
            int idx = -1;
            if (_firstCounter-- > 0) idx = FirstBurstIndex >= 0 && FirstBurstIndex < bursts.Length ? FirstBurstIndex : 0;
            else
            {
                int random = _rnd.Next(0, 101), acc = 0;
                do
                {
                    idx++;
                    if (idx >= bursts.Length) { idx = bursts.Length - 1; break; }
                    acc += bursts[idx].Weight;
                }
                while (random > acc);
            }

            _cur = bursts[idx];
            _angleIdx = -1;
            _timeToNext = 0f;
            ShouldFire = false;
        }

        public void Update(float dt)
        {
            ShouldFire = false;
            if (_cur == null) return;
            _timeToNext -= dt;
            if (_timeToNext >= 0f) return;
            _angleIdx++;
            if (_angleIdx < _cur.Angles.Length && _cur.Angles[_angleIdx] > 0)
            {
                ShouldFire = true;
                _timeToNext = _cur.Next + (Rand() - 0.5f) * _cur.NextVar;
            }
            else _cur = null;
        }

        /// <summary>The aim error (x side, z up) for the shot being fired.</summary>
        public void GetError(float xMag, float zMag, out float x, out float z)
        {
            x = z = 0f;
            if (!ShouldFire || _cur == null || _angleIdx < 0 || _angleIdx >= _cur.Angles.Length) return;
            int a = _cur.Angles[_angleIdx];
            if (AvoidAccuracy && (a == 4 || a == 12)) a = _cur.Angles[_angleIdx > 0 ? _angleIdx - 1 : _angleIdx + 1];
            if (a <= 0) return;
            double ang = a * (-22.5 * System.Math.PI / 180.0);
            x = (float)System.Math.Cos(ang) * xMag;
            z = (float)System.Math.Sin(ang) * zMag;
        }

        /// <summary>CBurstFire::GetDistanceCompensatedError: max x error = player half extent * 3.625 + 0.2, max z = the eye offset.</summary>
        public void GetDistanceCompensatedError(float dist, float maxErrDist, out float x, out float z)
        {
            const float maxX = 0.5f * 3.625f + 0.2f, maxZ = 0.2f;
            float k = dist / maxErrDist;
            GetError(System.Math.Min(k * maxX, maxX), System.Math.Min(k * maxZ, maxZ), out x, out z);
        }

        public static Burst B(int w, float n, float v, params int[] a) { return new Burst(w, a, n, v); }
    }
}
