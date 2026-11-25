using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumMetroid.Data;

static partial class Probe
{
    public static readonly Dictionary<int, string> TypeNames = new Dictionary<int, string>
    {
        {0x00,"Actor"},{0x01,"Enemy"},{0x02,"Waypoint"},{0x03,"Door"},{0x04,"Trigger"},{0x05,"Timer"},{0x06,"Counter"},{0x07,"Effect"},{0x08,"Platform"},{0x09,"Sound"},
        {0x0A,"Generator"},{0x0B,"Dock"},{0x0C,"Camera"},{0x0D,"CameraWaypoint"},{0x0E,"NewIntroBoss"},{0x0F,"SpawnPoint"},{0x10,"CameraHint"},{0x11,"Pickup"},
        {0x12,"JumpPoint"},{0x13,"MemoryRelay"},{0x14,"RandomRelay"},{0x15,"Relay"},{0x16,"Beetle"},{0x17,"HUDMemo"},{0x18,"CameraFilterKeyframe"},{0x19,"CameraBlurKeyframe"},
        {0x1A,"DamageableTrigger"},{0x1B,"Debris"},{0x1C,"CameraShaker"},{0x1D,"ActorKeyframe"},{0x20,"Water"},{0x21,"Warwasp"},{0x22,"MapStation"},{0x24,"SpacePirate"},
        {0x25,"FlyingPirate"},{0x26,"ElitePirate"},{0x2F,"PathCamera"},{0x30,"GrapplePoint"},{0x35,"DistanceFog"},{0x38,"DockAreaChange"},{0x39,"ActorRotate"},
        {0x3A,"SpecialFunction"},{0x3D,"Parasite"},{0x3E,"PlayerHint"},{0x40,"PickupGenerator"},{0x41,"AIKeyframe"},{0x42,"PointOfInterest"},{0x45,"DebrisExtended"},
        {0x46,"Steam"},{0x47,"Ripple"},{0x48,"BallTrigger"},{0x49,"TargetingPoint"},{0x4C,"PlayerActor"},{0x4E,"AreaAttributes"},{0x51,"VisorFlare"},{0x52,"WorldTeleporter"},
        {0x53,"VisorGoo"},{0x55,"ControllerAction"},{0x56,"Switch"},{0x57,"PlayerStateChange"},{0x59,"SaveStation"},{0x5A,"WallCrawlerSwarm"},{0x5D,"RoomAcoustics"},
        {0x5E,"ColorModulate"},{0x60,"Midi"},{0x61,"StreamedAudio"},{0x62,"WorldTeleporterToo"},{0x64,"GunTurret"},{0x68,"RadialDamage"},{0x69,"CameraPitchVolume"},
        {0x73,"CameraHintTrigger"},{0x74,"RumbleEffect"},{0x7B,"Ridley"},{0x89,"NewCameraShaker"},
    };

    static readonly string[] StateNames = { "Active","Arrived","Closed","Entered","Exited","Inactive","Inside","MaxReached","Open","Zero","Attack","CloseIn","Retreat","Patrol","Dead","CameraPath","CameraTarget","DeactivateState","Play","MassiveDeath","DeathRattle","AboutToMassivelyDie","Damage","InvulnDamage","MassiveFrozenDeath","Modify","ScanStart","ScanProcessing","ScanDone","UnFrozen","Default","ReflectedDamage","InheritBounds" };
    static readonly string[] MsgNames = { "M0","Activate","Arrived","Close","Deactivate","Decrement","Follow","Increment","Next","Open","Reset","ResetAndStart","SetToMax","SetToZero","Start","Stop","StopAndReset","ToggleActive","M18","Action","Play","Alert" };

    static string TName(int t) { return TypeNames.TryGetValue(t, out var n) ? n : t.ToString("X2"); }
    static string SName(int s) { return s >= 0 && s < StateNames.Length ? StateNames[s] : s.ToString(); }
    static string MName(int m) { return m >= 0 && m < MsgNames.Length ? MsgNames[m] : m.ToString(); }

    static partial void ExtraImpl13(string cmd, string[] args, ref int result)
    {
        switch (cmd)
        {
            case "trace":
            {
                // trace <area> [filter]: every object of the area (all layers, '*' = active at start) with its links resolved by name
                var w = LoadWorld(Environment.GetEnvironmentVariable("MP_PAK") ?? "Metroid1.pak");
                var all = new Dictionary<uint, (int area, MpScriptObject o)>();
                var perArea = new Dictionary<int, List<MpScriptObject>>();
                for (int i = 0; i < w.Areas.Count; i++)
                {
                    var a = MpArea.Read(Assets.Read(w.Areas[i].Mrea), false);
                    var objs = MpScriptLayers.Read(a);
                    perArea[i] = objs;
                    foreach (var o in objs) all[o.Id] = (i, o);
                }
                int ai = int.Parse(args[1]);
                string filter = args.Length > 2 ? args[2] : null;
                var info = w.Areas[ai];
                foreach (var o in perArea[ai])
                {
                    if (filter != null && TName(o.Type).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && o.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool act = (info.LayerBits & (1UL << o.Layer)) != 0;
                    Console.WriteLine((act ? "*" : " ") + "L" + o.Layer + " " + o.Id.ToString("X8") + " " + TName(o.Type) + " '" + o.Name + "'" + Extra(o));
                    foreach (var l in o.Links)
                    {
                        string tgt = all.TryGetValue(l.Target, out var t) ? (t.area != ai ? "[a" + t.area + "] " : "") + TName(t.o.Type) + " '" + t.o.Name + "'" : l.Target.ToString("X8") + " ?";
                        Console.WriteLine("        " + SName(l.State) + " -> " + MName(l.Message) + " -> " + tgt);
                    }
                }
                result = 0; break;
            }
            case "loops": Loops(ParseId(args[1])); result = 0; break;
            default: ExtraImpl14(cmd, args, ref result); break;
        }
    }

    static string Extra(MpScriptObject o)
    {
        try
        {
            var r = o.Props();
            switch (o.Type)
            {
                case 0x3A: // SpecialFunction: head (pos, rot), u32 function, string, float x3, u32 x2, u32, ...
                {
                    r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt); MpScriptRead.ReadHead(r, false);
                    int fn = r.S32();
                    string s = r.CString();
                    float f1 = r.F32(), f2 = r.F32(), f3 = r.F32();
                    int i1 = r.S32(), i2 = r.S32(), i3 = r.S32();
                    return " fn " + fn + " '" + s + "' " + f1 + " " + f2 + " " + f3 + " ints " + i1 + " " + i2 + " " + i3;
                }
                case 0x04:
                {
                    var t = MpScriptRead.ReadTrigger(o);
                    return t == null ? "" : " flags " + t.Flags.ToString("X") + " dmg " + t.Damage + " ext " + t.Extent + (t.Active ? "" : " inactive");
                }
                case 0x05:
                {
                    float t = r.F32(), rnd = r.F32(); bool loop = r.Bool(), auto = r.Bool(), act = r.Bool();
                    return " t " + t + (rnd > 0 ? "+" + rnd : "") + (loop ? " loop" : "") + (auto ? " auto" : "") + (act ? "" : " inactive");
                }
            }
        }
        catch (Exception) { }
        return "";
    }
}

static partial class Probe
{
    // loops <cmdl>: the open edges of a model (edges used by one triangle, welded by position index) chained into loops
    public static void Loops(uint cmdl)
    {
        var g = MpGeometry.ReadCmdl(Assets.Read(cmdl), out var sets);
        var edgeCount = new Dictionary<long, int>();
        var dirEdge = new Dictionary<long, (int a, int b)>();
        var pos = new Dictionary<int, (float x, float y, float z)>();
        foreach (var s in g.Surfaces)
        {
            for (int v = 0; v < s.VertexCount; v++) pos[s.PosIdx[v]] = (s.Pos[v * 3], s.Pos[v * 3 + 1], s.Pos[v * 3 + 2]);
            for (int t = 0; t + 2 < s.Tris.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = s.PosIdx[s.Tris[t + k]], b = s.PosIdx[s.Tris[t + (k + 1) % 3]];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    edgeCount.TryGetValue(key, out int n); edgeCount[key] = n + 1;
                    dirEdge[key] = (a, b);
                }
        }
        var next = new Dictionary<int, List<int>>();
        int open = 0;
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue;
            open++;
            var e = dirEdge[kv.Key];
            if (!next.ContainsKey(e.a)) next[e.a] = new List<int>();
            next[e.a].Add(e.b);
        }
        Console.WriteLine("surfaces " + g.Surfaces.Count + " edges " + edgeCount.Count + " open " + open);
        var used = new HashSet<int>();
        foreach (int start in next.Keys.ToList())
        {
            if (used.Contains(start)) continue;
            var loop = new List<int>();
            int cur = start;
            while (!used.Contains(cur) && next.ContainsKey(cur))
            {
                used.Add(cur); loop.Add(cur);
                cur = next[cur][0];
            }
            float cx = 0, cy = 0, cz = 0; foreach (int i in loop) { cx += pos[i].x; cy += pos[i].y; cz += pos[i].z; }
            cx /= loop.Count; cy /= loop.Count; cz /= loop.Count;
            float r = 0; foreach (int i in loop) r = Math.Max(r, (float)Math.Sqrt((pos[i].x - cx) * (pos[i].x - cx) + (pos[i].y - cy) * (pos[i].y - cy) + (pos[i].z - cz) * (pos[i].z - cz)));
            Console.WriteLine("  loop " + loop.Count + " verts, closed " + (cur == start) + " centre (" + cx.ToString("0.###") + ", " + cy.ToString("0.###") + ", " + cz.ToString("0.###") + ") radius " + r.ToString("0.###") + " first idx " + loop[0]);
        }
    }
}
