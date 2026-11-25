namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>CDamageInfo(in) as script objects store it (u32 count, weapon type, damage, radius, knockback); radius damage = damage.</summary>
    public struct MpDamageInfo
    {
        public int Weapon;
        public float Damage, Radius, Knockback;

        public static MpDamageInfo Read(MpReader r)
        {
            r.U32();
            return new MpDamageInfo { Weapon = r.S32(), Damage = r.F32(), Radius = r.F32(), Knockback = r.F32() };
        }
    }

    /// <summary>
    /// CPatternedInfo (decomp CPatternedInfo.cpp, read in its order after its u32 property count, 35..38): mass, speed, turn speed,
    /// detection range / height / angle, min / max attack range, average attack time and variation, leash radius, player leash
    /// radius and time, contact damage, damage wait time, health (CHealthInfo: u32, health, knockback resistance), vulnerability,
    /// half extent, height, body origin, step up height, x-damage, frozen x-damage, x-damage delay, death sound (a data id: through
    /// the sound lookup), the animation parameters (ANCS, character, initial animation), active, state machine (AFSM), into /
    /// out of / freeze durations, pathfinding index, particle 1 scale + id, electric id, [particle 2 scale + id], [ice shatter sound].
    /// </summary>
    public sealed class MpPatternedInfo
    {
        public int PropCount;
        public float Mass, Speed, TurnSpeed, DetectionRange, DetectionHeight, DetectionAngle, MinAttackRange, MaxAttackRange;
        public float AverageAttackTime, AttackTimeVariation, LeashRadius, PlayerLeashRadius, PlayerLeashTime;
        public MpDamageInfo Contact;
        public float DamageWaitTime, Health, KnockbackResistance;
        public MpVulnerability Vuln;
        public float HalfExtent, Height;
        public MpVec3 BodyOrigin;
        public float StepUpHeight, XDamage, FrozenXDamage, XDamageDelay;
        public int DeathSfx = -1;
        public MpModelRef Anim;
        public bool Active;
        public uint StateMachine;
        public float IntoFreezeDur, OutOfFreezeDur, FreezeDur;
        public int PathfindingIndex;
        public MpVec3 Particle1Scale, Particle2Scale;
        public uint Particle1 = 0xFFFFFFFF, Electric = 0xFFFFFFFF, Particle2 = 0xFFFFFFFF;
        public int IceShatterSfx = -1;

        public static MpPatternedInfo Read(MpReader r)
        {
            var p = new MpPatternedInfo { PropCount = r.S32() };
            p.Mass = r.F32(); p.Speed = r.F32(); p.TurnSpeed = r.F32();
            p.DetectionRange = r.F32(); p.DetectionHeight = r.F32(); p.DetectionAngle = r.F32();
            p.MinAttackRange = r.F32(); p.MaxAttackRange = r.F32();
            p.AverageAttackTime = r.F32(); p.AttackTimeVariation = r.F32();
            p.LeashRadius = r.F32(); p.PlayerLeashRadius = r.F32(); p.PlayerLeashTime = r.F32();
            p.Contact = MpDamageInfo.Read(r);
            p.DamageWaitTime = r.F32();
            r.U32();
            p.Health = r.F32();
            p.KnockbackResistance = r.F32();
            p.Vuln = MpScriptRead.ReadVulnerability(r);
            p.HalfExtent = r.F32(); p.Height = r.F32();
            p.BodyOrigin = MpVec3.Read(r);
            p.StepUpHeight = r.F32(); p.XDamage = r.F32(); p.FrozenXDamage = r.F32(); p.XDamageDelay = r.F32();
            p.DeathSfx = r.S32();
            p.Anim = MpScriptRead.ReadAnimParams(r, 0xFFFFFFFF);
            p.Active = r.Bool();
            p.StateMachine = r.U32();
            p.IntoFreezeDur = r.F32(); p.OutOfFreezeDur = r.F32(); p.FreezeDur = r.F32();
            p.PathfindingIndex = r.S32();
            p.Particle1Scale = MpVec3.Read(r);
            p.Particle1 = r.U32();
            p.Electric = r.U32();
            if (p.PropCount >= 0x24) p.Particle2Scale = MpVec3.Read(r);
            if (p.PropCount >= 0x25) p.Particle2 = r.U32();
            if (p.PropCount >= 0x26) p.IceShatterSfx = r.S32();
            return p;
        }
    }
}
