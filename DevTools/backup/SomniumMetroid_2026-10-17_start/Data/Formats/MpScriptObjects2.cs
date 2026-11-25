namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// CDamageVulnerability: per weapon type (EWeaponType: 0 power, 1 ice, 2 wave, 3 plasma, 4 bomb, 5 power bomb, 6 missile, 7 boost,
    /// 8 phazon ...) an EVulnerability (0 weak, 1 normal, 2 deflect, 3 immune, 4 pass through, 5 direct weak, 6 direct normal,
    /// 7 direct immune), charged and combo shots of the four beams separately. CDamageInfo::GetDamage: deflect = 0, weak = x2.
    /// </summary>
    public sealed class MpVulnerability
    {
        public const int Weak = 0, NormalV = 1, Deflect = 2, ImmuneV = 3, PassThrough = 4, DirectWeak = 5, DirectNormal = 6, DirectImmune = 7;
        public readonly int[] Normal = new int[15];
        public readonly int[] Charged = new int[4];
        public readonly int[] Combo = new int[4];

        public static MpVulnerability Immune()
        {
            var v = new MpVulnerability();
            for (int i = 0; i < 15; i++) v.Normal[i] = ImmuneV;
            for (int i = 0; i < 4; i++) { v.Charged[i] = ImmuneV; v.Combo[i] = ImmuneV; }
            return v;
        }

        public int Of(int weapon, bool charged)
        {
            if (weapon < 0 || weapon >= 15) return ImmuneV;
            if (charged && weapon < 4) return Charged[weapon];
            return Normal[weapon];
        }

        public float Damage(int weapon, bool charged, float damage)
        {
            int v = Of(weapon, charged);
            if (v == Deflect || v == ImmuneV || v == DirectImmune || v == PassThrough) return 0f;
            if (v == Weak || v == DirectWeak) return damage * 2f;
            return damage;
        }
    }

    /// <summary>More typed decoders (ScriptLoader.cpp, field by field).</summary>
    public static class MpScriptRead2
    {
        public sealed class DamageableTrigger
        {
            public string Name;
            public MpVec3 Pos, Volume;
            public float Health;
            public MpVulnerability Vuln;
            public int Flags;
            public uint Pattern1, Pattern2, ColorTex;
            public bool CanOrbit, Active;
        }

        public sealed class Timer { public string Name; public float Start, MaxRandom; public bool Loop, AutoStart, Active; }
        public sealed class Counter { public string Name; public int Initial, Max; public bool AutoReset, Active; }
        public sealed class Relay { public string Name; public bool Active; }
        public sealed class MemoryRelay { public string Name; public bool DefaultActive, SkipSendActive, IgnoreMessages; }
        public sealed class HudMemo { public string Name; public float DisplayTime; public bool Clear; public int DisplayType; public uint Strg; public bool Active; }
        public sealed class ActorRotate { public string Name; public MpVec3 Rotation; public float Time; public bool UpdateActors, UpdateOnCreation, Active; }
        public sealed class StreamedMusic { public string Name; public bool Active; public string File; public bool NoStopOnDeactivate; public float FadeIn, FadeOut; public uint Volume; public bool Loop, Music; }

        public sealed class Sound
        {
            public MpScriptRead.Head Head;
            public int SoundId;
            public bool Active, Looped, NonEmitter, AutoStart, WorldSfx;
            public float MaxDist, DistComp, StartDelay;
            public uint MinVol, Vol, Prio, Pan;
            public int Pitch;
        }

        public static DamageableTrigger ReadDamageableTrigger(MpScriptObject o)
        {
            if (o.PropCount != 12) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var d = new DamageableTrigger { Name = r.CString(), Pos = MpVec3.Read(r), Volume = MpVec3.Read(r) };
            MpScriptRead.SkipHealth(r, out d.Health);
            d.Vuln = MpScriptRead.ReadVulnerability(r);
            d.Flags = r.S32();
            d.Pattern1 = r.U32();
            d.Pattern2 = r.U32();
            d.ColorTex = r.U32();
            d.CanOrbit = r.Bool();
            d.Active = r.Bool();
            return d;
        }

        public static Timer ReadTimer(MpScriptObject o)
        {
            if (o.PropCount != 6) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            return new Timer { Name = r.CString(), Start = r.F32(), MaxRandom = r.F32(), Loop = r.Bool(), AutoStart = r.Bool(), Active = r.Bool() };
        }

        public static Counter ReadCounter(MpScriptObject o)
        {
            if (o.PropCount != 5) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            return new Counter { Name = r.CString(), Initial = r.S32(), Max = r.S32(), AutoReset = r.Bool(), Active = r.Bool() };
        }

        /// <summary>Type 0x15 Relay (ScriptLoader::LoadFaultyRelay): name, [u32 when 3 properties], active.</summary>
        public static Relay ReadRelay(MpScriptObject o)
        {
            if (o.PropCount < 2 || o.PropCount > 3) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var x = new Relay { Name = r.CString() };
            if (o.PropCount >= 3) r.U32();
            x.Active = r.Bool();
            return x;
        }

        /// <summary>Type 0x13 MemoryRelay (ScriptLoader::LoadRelay): name, default active, skip send active, [ignore messages].</summary>
        public static MemoryRelay ReadMemoryRelay(MpScriptObject o)
        {
            if (o.PropCount < 3 || o.PropCount > 4) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var x = new MemoryRelay { Name = r.CString(), DefaultActive = r.Bool(), SkipSendActive = r.Bool() };
            if (o.PropCount > 3) x.IgnoreMessages = r.Bool();
            return x;
        }

        public static HudMemo ReadHudMemo(MpScriptObject o)
        {
            if (o.PropCount != 5 && o.PropCount != 6) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var m = new HudMemo { Name = r.CString(), DisplayTime = r.F32(), Clear = r.Bool() };
            m.DisplayType = o.PropCount == 5 ? 1 : r.S32();
            m.Strg = r.U32();
            m.Active = r.Bool();
            return m;
        }

        public static ActorRotate ReadActorRotate(MpScriptObject o)
        {
            if (o.PropCount != 6) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            return new ActorRotate { Name = r.CString(), Rotation = MpVec3.Read(r), Time = r.F32(), UpdateActors = r.Bool(), UpdateOnCreation = r.Bool(), Active = r.Bool() };
        }

        public static StreamedMusic ReadStreamedMusic(MpScriptObject o)
        {
            if (o.PropCount != 9) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var m = new StreamedMusic { Name = r.CString(), Active = r.Bool(), File = r.CString(), NoStopOnDeactivate = r.Bool(), FadeIn = r.F32(), FadeOut = r.F32(), Volume = r.U32() };
            m.Loop = r.U32() == 0;
            m.Music = r.Bool();
            return m;
        }

        public static Sound ReadSound(MpScriptObject o)
        {
            if (o.PropCount != 20) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var s = new Sound { Head = MpScriptRead.ReadHead(r, false) };
            s.SoundId = r.S32();
            s.Active = r.Bool();
            s.MaxDist = r.F32();
            s.DistComp = r.F32();
            s.StartDelay = r.F32();
            s.MinVol = r.U32();
            s.Vol = r.U32();
            s.Prio = r.U32();
            s.Pan = r.U32();
            s.Looped = r.Bool();
            s.NonEmitter = r.Bool();
            s.AutoStart = r.Bool();
            r.Bool(); r.Bool();
            s.WorldSfx = r.Bool();
            r.Bool();
            s.Pitch = r.S32();
            return s;
        }
    }
}
