using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>Script object types (decomp CEntityInfo.hpp EScriptObjectType).</summary>
    public static class MpScriptType
    {
        public const int Actor = 0x00, Waypoint = 0x02, Door = 0x03, Trigger = 0x04, Timer = 0x05, Counter = 0x06, Effect = 0x07,
            Platform = 0x08, Sound = 0x09, Generator = 0x0A, Dock = 0x0B, Camera = 0x0C, CameraWaypoint = 0x0D, NewIntroBoss = 0x0E,
            SpawnPoint = 0x0F, CameraHint = 0x10, Pickup = 0x11, MemoryRelay = 0x13, RandomRelay = 0x14, Relay = 0x15, HUDMemo = 0x17,
            CameraFilterKeyframe = 0x18, DamageableTrigger = 0x1A, Debris = 0x1B, CameraShaker = 0x1C, ActorKeyframe = 0x1D,
            Water = 0x20, SpacePirate = 0x24, DistanceFog = 0x35, DockAreaChange = 0x38, ActorRotate = 0x39, SpecialFunction = 0x3A,
            Parasite = 0x3D, PlayerHint = 0x3E, PointOfInterest = 0x42, DebrisExtended = 0x45, PlayerActor = 0x4C, AreaAttributes = 0x4E,
            VisorFlare = 0x51, ControllerAction = 0x55, Switch = 0x56, RoomAcoustics = 0x5D, ColorModulate = 0x5E, Midi = 0x60,
            StreamedAudio = 0x61, WorldTeleporterToo = 0x62;
    }

    /// <summary>Script states and messages (EScriptObjectState / EScriptObjectMessage).</summary>
    public static class MpScriptMsg
    {
        public const int StActive = 0, StArrived = 1, StClosed = 2, StEntered = 3, StExited = 4, StInactive = 5, StInside = 6,
            StMaxReached = 7, StOpen = 8, StZero = 9, StAttack = 10, StDead = 14, StPlay = 18, StDamage = 22, StModify = 25,
            StScanStart = 26, StScanProcessing = 27, StScanDone = 28, StDefault = 30;
        public const int Activate = 1, Arrived = 2, Close = 3, Deactivate = 4, Decrement = 5, Follow = 6, Increment = 7, Next = 8,
            Open = 9, Reset = 10, ResetAndStart = 11, SetToMax = 12, SetToZero = 13, Start = 14, Stop = 15, StopAndReset = 16,
            ToggleActive = 17, Action = 19, Play = 20, Alert = 21;
    }

    public struct MpVec3
    {
        public float X, Y, Z;
        public MpVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static MpVec3 Read(MpReader r) { return new MpVec3(r.F32(), r.F32(), r.F32()); }
        public override string ToString() { return "(" + X.ToString("0.##") + ", " + Y.ToString("0.##") + ", " + Z.ToString("0.##") + ")"; }
    }

    /// <summary>The model a script actor shows: a static CMDL or an ANCS character (with its initial animation).</summary>
    public struct MpModelRef
    {
        public uint Cmdl;
        public uint Ancs;
        public int Character;
        public int Animation;
        public bool Valid { get { return MpAssets.Valid(Cmdl) || MpAssets.Valid(Ancs); } }
    }

    /// <summary>CActorParameters (ScriptLoader LoadActorParameters) - the parts the Dreamport uses.</summary>
    public struct MpActorParams
    {
        public uint ScanId;
        public uint XrayModel, XraySkin, ThermalModel, ThermalSkin;
        public float[] NoLightsAmbient;     // rgba
        public bool ThermalHeat;
        public uint VisorMask;              // bit 0 combat, 1 scan, 2 thermal, 3 x-ray (which visors draw it)
        public float ThermalMag;
    }

    /// <summary>
    /// Typed decoders following decomp src/MetroidPrime/ScriptLoader.cpp field by field (property counts checked as the game does).
    /// </summary>
    public static class MpScriptRead
    {
        /// <summary>Debug: bytes read by the last decoder (the probe compares it with the object size).</summary>
        public static int LastRead;

        public sealed class Head
        {
            public string Name;
            public MpVec3 Pos, Rot, Scale = new MpVec3(1, 1, 1);
        }

        public sealed class Actor
        {
            public Head Head;
            public MpVec3 CollisionExtent, Centroid;
            public float Health;
            public MpModelRef Model;
            public MpActorParams Params;
            public bool Looping, Immovable, Solid, CameraPassthrough, Active;
            public MpVulnerability Vuln;
            public uint ShaderIdx;
            public float XrayAlpha;
        }

        public sealed class Door
        {
            public Head Head;
            public MpModelRef Model;
            public MpActorParams Params;
            public MpVec3 OrbitPos, CollisionExtent, Offset;
            public bool Active, Open, ProjectilesCollide, Morphball;
            public float AnimLength;
        }

        public sealed class Platform
        {
            public Head Head;
            public MpVec3 Extent, Centroid;
            public MpModelRef Model;
            public MpActorParams Params;
            public float Speed;
            public bool Active;
            public uint Dcln;
            public float Health;
            public MpVulnerability Vuln;
        }

        public sealed class SpawnPoint
        {
            public string Name;
            public MpVec3 Pos, Rot;
            public int[] Items;
            public bool Default, Active, Morphed;
        }

        public sealed class Dock
        {
            public string Name;
            public bool Active, LoadConnected;
            public MpVec3 Pos, Scale;
            public int DockIndex, AreaIndex;
        }

        public sealed class Trigger
        {
            public string Name;
            public MpVec3 Pos, Extent, Force;
            public int WeaponType;
            public float Damage, Radius, Knockback;
            public uint Flags;
            public bool Active, DeactivateOnEntered, DeactivateOnExited;
        }

        public sealed class PointOfInterest
        {
            public Head Head;
            public bool Active;
            public uint ScanId;
            public float PointSize;
        }

        public static Head ReadHead(MpReader r, bool scaled)
        {
            var h = new Head { Name = r.CString(), Pos = MpVec3.Read(r), Rot = MpVec3.Read(r) };
            if (scaled) h.Scale = MpVec3.Read(r);
            return h;
        }

        public static void SkipHealth(MpReader r, out float health)
        {
            r.U32();
            health = r.F32();
            r.F32();
        }

        public static void SkipVulnerability(MpReader r) { ReadVulnerability(r); }

        /// <summary>CDamageVulnerability(in): 11 properties = the old table (15 normal + deflect), else LoadData (normal, deflect, charged 4, combo 4).</summary>
        public static MpVulnerability ReadVulnerability(MpReader r)
        {
            var v = MpVulnerability.Immune();
            int count = r.S32();
            if (count == 11)
            {
                for (int i = 0; i < 15; i++) v.Normal[i] = r.S32();
                if (count != 15) r.U32();
                for (int i = 0; i < 4; i++) { v.Charged[i] = v.Normal[i]; v.Combo[i] = v.Normal[i]; }
                return v;
            }

            int prop = count - 3;
            for (int i = 0; i < prop; i++)
            {
                int x = r.S32();
                if (i < 15) v.Normal[i] = x;      // (any past 15 discarded, as LoadData does)
            }

            for (int i = prop; i < 15; i++) v.Normal[i] = MpVulnerability.Deflect;
            r.U32();               // deflect
            r.U32();               // charged count
            for (int i = 0; i < 4; i++) v.Charged[i] = r.S32();
            r.U32();               // charged deflect
            r.U32();               // combo count
            for (int i = 0; i < 4; i++) v.Combo[i] = r.S32();
            r.U32();               // combo deflect
            return v;
        }

        public static MpModelRef ReadAnimParams(MpReader r, uint staticId)
        {
            var m = new MpModelRef { Cmdl = staticId };
            m.Ancs = r.U32();
            m.Character = r.S32();
            m.Animation = r.S32();
            return m;
        }

        public static MpActorParams ReadActorParams(MpReader r)
        {
            var p = new MpActorParams { VisorMask = 0xF, ThermalMag = 1f, NoLightsAmbient = new[] { 0f, 0f, 0f, 1f } };
            int count = r.S32();
            if (count < 5 || count > 14) return p;
            int lp = r.S32();
            if (lp == 14)
            {
                r.Bool(); r.F32(); r.S32(); r.F32(); r.F32();
                p.NoLightsAmbient = new[] { r.F32(), r.F32(), r.F32(), r.F32() };
                r.Bool(); r.S32(); r.S32();
                r.Skip(12);
                r.S32(); r.S32(); r.Bool(); r.S32();
            }

            if (count > 5)
            {
                int sc = r.S32();
                if (sc == 1) p.ScanId = r.U32();
            }

            p.XrayModel = r.U32();
            p.XraySkin = r.U32();
            p.ThermalModel = r.U32();
            p.ThermalSkin = r.U32();
            if (count > 7) r.Bool();
            if (count > 8) r.F32();
            if (count > 9) r.F32();
            if (count > 6)
            {
                int vc = r.S32();
                if (vc >= 1 && vc <= 3)
                {
                    r.Bool();
                    if (vc > 2) r.Bool();
                    p.VisorMask = vc >= 2 ? r.U32() : 0xFu;
                }
            }

            if (count > 10) p.ThermalHeat = r.Bool();
            if (count > 11) r.Bool();
            if (count > 12) r.Bool();
            if (count > 13) p.ThermalMag = r.F32();
            return p;
        }

        public static Actor ReadActor(MpScriptObject o)
        {
            if (o.PropCount < 24) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var a = new Actor { Head = ReadHead(r, true) };
            a.CollisionExtent = MpVec3.Read(r);
            a.Centroid = MpVec3.Read(r);
            r.F32(); r.F32();
            SkipHealth(r, out a.Health);
            a.Vuln = ReadVulnerability(r);
            uint staticId = r.U32();
            a.Model = ReadAnimParams(r, staticId);
            a.Params = ReadActorParams(r);
            a.Looping = r.Bool();
            a.Immovable = r.Bool();
            a.Solid = r.Bool();
            a.CameraPassthrough = r.Bool();
            a.Active = r.Bool();
            a.ShaderIdx = r.U32();
            a.XrayAlpha = r.F32();
            LastRead = r.Pos - o.PropAt + 4;
            return a;
        }

        public static Door ReadDoor(MpScriptObject o)
        {
            if (o.PropCount < 13 || o.PropCount > 14) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var d = new Door { Head = ReadHead(r, true) };
            d.Model = ReadAnimParams(r, 0xFFFFFFFF);
            d.Model.Character = 0;
            d.Params = ReadActorParams(r);
            d.OrbitPos = MpVec3.Read(r);
            d.CollisionExtent = MpVec3.Read(r);
            d.Offset = MpVec3.Read(r);
            d.Active = r.Bool();
            d.Open = r.Bool();
            d.ProjectilesCollide = r.Bool();
            d.AnimLength = r.F32();
            if (o.PropCount == 14) d.Morphball = r.Bool();
            else d.Morphball = d.Model.Ancs == 0x1f9da858;
            LastRead = r.Pos - o.PropAt;
            return d;
        }

        public static Platform ReadPlatform(MpScriptObject o)
        {
            if (o.PropCount != 19) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var p = new Platform { Head = ReadHead(r, true) };
            p.Extent = MpVec3.Read(r);
            p.Centroid = MpVec3.Read(r);
            uint staticId = r.U32();
            p.Model = ReadAnimParams(r, staticId);
            p.Params = ReadActorParams(r);
            p.Speed = r.F32();
            p.Active = r.Bool();
            p.Dcln = r.U32();
            SkipHealth(r, out p.Health);
            p.Vuln = ReadVulnerability(r);
            return p;
        }

        public static SpawnPoint ReadSpawnPoint(MpScriptObject o)
        {
            if (o.PropCount < 35) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var s = new SpawnPoint { Name = r.CString(), Pos = MpVec3.Read(r), Rot = MpVec3.Read(r) };
            s.Items = new int[o.PropCount - 6];
            for (int i = 0; i < s.Items.Length; i++) s.Items[i] = r.S32();
            s.Default = r.Bool();
            s.Active = r.Bool();
            if (o.PropCount >= 35) s.Morphed = r.Bool();
            LastRead = r.Pos - o.PropAt;
            return s;
        }

        public static Dock ReadDock(MpScriptObject o)
        {
            if (o.PropCount != 7) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            return new Dock { Name = r.CString(), Active = r.Bool(), Pos = MpVec3.Read(r), Scale = MpVec3.Read(r), DockIndex = r.S32(), AreaIndex = r.S32(), LoadConnected = r.Bool() };
        }

        public static Trigger ReadTrigger(MpScriptObject o)
        {
            if (o.PropCount != 9) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var t = new Trigger { Name = r.CString(), Pos = MpVec3.Read(r), Extent = MpVec3.Read(r) };
            r.U32();
            t.WeaponType = r.S32();
            t.Damage = r.F32();
            t.Radius = r.F32();
            t.Knockback = r.F32();
            t.Force = MpVec3.Read(r);
            t.Flags = r.U32();
            t.Active = r.Bool();
            t.DeactivateOnEntered = r.Bool();
            t.DeactivateOnExited = r.Bool();
            LastRead = r.Pos - o.PropAt;
            return t;
        }

        public static PointOfInterest ReadPointOfInterest(MpScriptObject o)
        {
            if (o.PropCount != 6) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            var p = new PointOfInterest { Head = ReadHead(r, false), Active = r.Bool() };
            int sc = r.S32();
            if (sc == 1) p.ScanId = r.U32();
            p.PointSize = r.F32();
            LastRead = r.Pos - o.PropAt;
            return p;
        }
    }
}
