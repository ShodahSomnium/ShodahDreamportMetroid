using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The script object types added for full frigate progression, read field by field as ScriptLoader.cpp does: animated actors,
    /// waypoints and camera waypoints, moving platforms (elevators), actor keyframes, effects, generators and their copies, pickups
    /// and pickup generators, player actors (cinematic Samus), special functions, cinematic cameras, random relays, switches,
    /// camera shakers and rumble (controller ticks), radial damage, the world teleporter (the end of the frigate).
    /// </summary>
    public static class MpScriptEntities
    {
        public const int Waypoint = 0x02, Effect = 0x07, Generator = 0x0A, Camera = 0x0C, CameraWaypoint = 0x0D, Pickup = 0x11,
            RandomRelay = 0x14, CameraShaker = 0x1C, ActorKeyframe = 0x1D, SpecialFunction = 0x3A, PickupGenerator = 0x40,
            PlayerActor = 0x4C, Switch = 0x56, WorldTeleporterToo = 0x62, RadialDamage = 0x68, RumbleEffect = 0x74, NewCameraShaker = 0x89,
            Actor = 0x00, Platform = 0x08, WorldTeleporter = 0x52;

        private static Vector3 V(MpVec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        private static MpReader R(MpScriptObject o) { return new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt); }

        public static MpEntity Make(MpScriptObject o, MpAreaView view, ref int models)
        {
            try
            {
                switch (o.Type)
                {
                    case Actor: return MpAnimActorEntity.TryMake(o, view, ref models);
                    case Platform: return MpMovingPlatform.Make(o, view, ref models);
                    case Waypoint:
                    {
                        if (o.PropCount != 13) return null;
                        var r = R(o);
                        MpScriptRead.Head h = MpScriptRead.ReadHead(r, false);
                        var w = new MpWaypointEntity { GamePos = V(h.Pos), Rot = MpSpace.GameRotation(V(h.Rot)), Active = r.Bool(), Speed = r.F32(), Pause = r.F32() };
                        r.S32(); r.S32(); r.S32(); r.S32(); r.S32(); r.S32();
                        w.Animation = r.S32();
                        return w;
                    }

                    case CameraWaypoint:
                    {
                        if (o.PropCount != 6) return null;
                        var r = R(o);
                        MpScriptRead.Head h = MpScriptRead.ReadHead(r, false);
                        return new MpWaypointEntity { GamePos = V(h.Pos), Rot = MpSpace.GameRotation(V(h.Rot)), Active = r.Bool(), HFov = r.F32(), IsCamera = true };
                    }

                    case Effect: return MpEffectEntity.Make(o, view);
                    case Generator:
                    {
                        if (o.PropCount != 8) return null;
                        var r = R(o);
                        r.CString();
                        var g = new MpGeneratorEntity { SpawnCount = r.S32(), NoReuseFollowers = r.Bool(), NoInheritXf = r.Bool(), Offset = V(MpVec3.Read(r)), Active = r.Bool(), MinScale = r.F32(), MaxScale = r.F32() };
                        return g;
                    }

                    case Pickup: return MpPickupEntity.Make(o, view, ref models);
                    case PickupGenerator:
                    {
                        if (o.PropCount != 4) return null;
                        var r = R(o);
                        r.CString();
                        return new MpPickupGeneratorEntity { Offset = V(MpVec3.Read(r)), Active = r.Bool(), Frequency = r.F32() };
                    }

                    case ActorKeyframe:
                    {
                        if (o.PropCount != 7) return null;
                        var r = R(o);
                        r.CString();
                        var k = new MpActorKeyframeEntity { Anim = r.S32(), Looping = r.Bool(), Lifetime = r.F32(), Active = r.Bool() };
                        int fade = r.S32();
                        k.TimedLoop = ((fade << 3) & 0x10) != 0;
                        k.TotalPlayback = r.F32();
                        return k;
                    }

                    case PlayerActor: return MpPlayerActorEntity.Make(o, view);
                    case SpecialFunction: return MpSpecialFunctionEntity.Make(o, view);
                    case Camera: return MpCinematicCamera.Make(o);
                    case RandomRelay:
                    {
                        if (o.PropCount != 5) return null;
                        var r = R(o);
                        r.CString();
                        return new MpRandomRelayEntity { SetSize = r.S32(), Variance = r.S32(), Percent = r.Bool(), Active = r.Bool() };
                    }

                    case Switch:
                    {
                        if (o.PropCount != 4) return null;
                        var r = R(o);
                        r.CString();
                        return new MpSwitchEntity { Active = r.Bool(), Opened = r.Bool(), CloseOnOpened = r.Bool() };
                    }

                    case CameraShaker:
                    case NewCameraShaker:
                        return new MpShakerEntity { Active = true, Strength = 0.6f, Seconds = 0.4f };
                    case RumbleEffect:
                    {
                        if (o.PropCount != 6) return null;
                        var r = R(o);
                        r.CString();
                        MpVec3.Read(r);
                        return new MpShakerEntity { Active = r.Bool(), Strength = 0.7f, Seconds = 0.3f };
                    }

                    case RadialDamage:
                    {
                        if (o.PropCount != 5) return null;
                        var r = R(o);
                        r.CString();
                        var d = new MpRadialDamageEntity { Center = V(MpVec3.Read(r)), Active = r.Bool() };
                        d.Damage = MpDamageInfo.Read(r);
                        d.Radius = r.F32();
                        return d;
                    }

                    case 0x48: return MpBallTriggerEntity.Make(o);
                    case 0x45:
                    case 0x1B: return MpDebrisEntity.Make(o, view);
                    case 0x0B:
                    {
                        MpScriptRead.Dock d = MpScriptRead.ReadDock(o);
                        if (d == null) return null;
                        MpWorld.Area wa = view.Info;
                        int to = d.DockIndex >= 0 && d.DockIndex < wa.Docks.Count && wa.Docks[d.DockIndex].Refs.Count > 0 ? wa.Docks[d.DockIndex].Refs[0].Key : -1;
                        return new MpDockEntity { Active = d.Active, DockIndex = d.DockIndex, Connected = to };
                    }

                    case WorldTeleporterToo:
                    case WorldTeleporter:
                    {
                        var r = R(o);
                        r.CString();
                        return new MpWorldTeleporterEntity { Active = r.Bool(), WorldId = r.U32(), AreaId = r.U32() };
                    }
                }
            }
            catch (System.Exception e) { MpLog.Warn("Scripts: " + o.Type.ToString("X2") + " " + o.Id.ToString("X8") + " '" + o.Name + "' not made: " + e.Message); }
            return null;
        }
    }

    /// <summary>Loader thread: what the added script types need (animated characters, models, effects), textures into the area's list.</summary>
    public static class MpScriptAssets
    {
        public static void Collect(MpAssets assets, MpScriptObject o, HashSet<uint> tex)
        {
            try
            {
                switch (o.Type)
                {
                    case MpScriptEntities.Actor:
                    {
                        MpScriptRead.Actor a = MpScriptRead.ReadActor(o);
                        if (a != null && MpAssets.Valid(a.Model.Ancs)) MpEnemyFactory.CollectCharacter(assets, a.Model.Ancs, a.Model.Character, tex);
                        break;
                    }

                    case 0x03:
                    {
                        MpScriptRead.Door d = MpScriptRead.ReadDoor(o);
                        if (d != null && MpAssets.Valid(d.Model.Ancs)) MpEnemyFactory.CollectCharacter(assets, d.Model.Ancs, 0, tex);
                        break;
                    }

                    case MpScriptEntities.Platform:
                    {
                        MpScriptRead.Platform p = MpScriptRead.ReadPlatform(o);
                        if (p != null && MpAssets.Valid(p.Model.Ancs)) MpEnemyFactory.CollectCharacter(assets, p.Model.Ancs, 0, tex);
                        break;
                    }

                    case MpScriptEntities.PlayerActor:
                    {
                        MpModelRef m = MpPlayerActorEntity.ReadModel(o);
                        if (MpAssets.Valid(m.Ancs)) MpEnemyFactory.CollectCharacter(assets, m.Ancs, m.Character, tex);
                        break;
                    }

                    case MpScriptEntities.Pickup:
                    {
                        MpModelRef m;
                        uint effect;
                        MpPickupEntity.ReadModel(o, out m, out effect);
                        if (MpAssets.Valid(m.Ancs) && assets.TypeOf(m.Ancs) == "ANCS") MpEnemyFactory.CollectCharacter(assets, m.Ancs, m.Character, tex);
                        else if (MpAssets.Valid(m.Cmdl)) MpModels.TexturesOf(MpModels.Load(assets, m.Cmdl), tex);
                        MpEnemyShots.PreparePart(assets, effect);
                        break;
                    }

                    case 0x45:
                    case 0x1B:
                    {
                        uint dm, d0, d1, d2;
                        if (MpDebrisEntity.Parts(o, out dm, out d0, out d1, out d2)) { MpEnemyShots.PreparePart(assets, d0); MpEnemyShots.PreparePart(assets, d1); MpEnemyShots.PreparePart(assets, d2); }
                        break;
                    }

                    case MpScriptEntities.Effect:
                    {
                        uint part = MpEffectEntity.PartOf(o);
                        MpEnemyShots.PreparePart(assets, part);
                        break;
                    }
                }
            }
            catch (System.Exception e) { MpLog.Warn("Scripts: " + o.Type.ToString("X2") + " '" + o.Name + "' assets not prepared: " + e.Message); }
        }
    }

    // ---------------------------------------------------------------------------------------------------------- waypoints

    /// <summary>CScriptWaypoint / CScriptCameraWaypoint: a point (and orientation) on a path; Arrived passes on Arrived; NextWaypoint picks an active Next.</summary>
    public sealed class MpWaypointEntity : MpEntity
    {
        public Vector3 GamePos;
        public Quaternion Rot = Quaternion.identity;
        public float Speed, Pause, HFov = 55f;
        public int Animation;
        public bool IsCamera;

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if (Active && msg == MpScriptMsg.Arrived) Send(MpScriptMsg.StArrived);
        }

        /// <summary>CScriptWaypoint::NextWaypoint: one of the active waypoints linked Arrived -> Next, at random.</summary>
        public MpWaypointEntity Next()
        {
            if (Links == null) return null;
            List<MpWaypointEntity> list = null;
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != MpScriptMsg.StArrived || l.Message != MpScriptMsg.Next) continue;
                var w = World.Find(l.Target) as MpWaypointEntity;
                if (w == null || !w.Active) continue;
                if (list == null) list = new List<MpWaypointEntity>();
                list.Add(w);
            }

            return list == null ? null : list[Mathf.Min(list.Count - 1, (int)(Random.value * list.Count * 0.99f))];
        }
    }

    // ---------------------------------------------------------------------------------------------------------- actors

    /// <summary>
    /// A script actor whose model is an ANCS character (CScriptActor with animation): the character posed and played (its initial
    /// animation, looping as the actor says), ActorKeyframes play others on it, its sound and effect events play. Static actors stay
    /// MpActorEntity (the factory's own case).
    /// </summary>
    public class MpAnimActorEntity : MpActorEntity
    {
        public MpAnimator Anim;
        public bool LoopDefault;
        private float _fadeT, _fadeDur;
        private bool _fadingOut;

        public static MpEntity TryMake(MpScriptObject o, MpAreaView view, ref int models)
        {
            MpScriptRead.Actor a = MpScriptRead.ReadActor(o);
            if (a == null || !MpAssets.Valid(a.Model.Ancs)) return null;
            MpCharacter ch = MpCharacter.Get(a.Model.Ancs, a.Model.Character);
            if (ch == null || ch.Model == null) return null;
            ch.Upload(true);
            var e = new MpAnimActorEntity { Health = a.Health, Vuln = a.Vuln, LoopDefault = a.Looping, ScriptActor = true };
            var body = new GameObject("Actor " + o.Name);
            body.transform.SetParent(view.Root.transform, false);
            body.transform.localPosition = new Vector3(a.Head.Pos.X, a.Head.Pos.Y, a.Head.Pos.Z);
            body.transform.localRotation = MpSpace.GameRotation(new Vector3(a.Head.Rot.X, a.Head.Rot.Y, a.Head.Rot.Z));
            body.transform.localScale = new Vector3(a.Head.Scale.X, a.Head.Scale.Y, a.Head.Scale.Z);
            e.Anim = new MpAnimator(ch, "Actor " + o.Name + " model", body.transform, (int)a.ShaderIdx);
            e.Anim.SoundEvent = s => { MpGame g = MpGame.Instance; if (g != null) g.PlayDataSfxAt(s.SfxId, MpSpace.ToGame(body.transform.position), 1f, false, s.MaxDist > 0f ? s.MaxDist : 60f); };
            e.Anim.ParticleEvent = p =>
            {
                if (p.Kind != "PART") return;
                Transform b = !string.IsNullOrEmpty(p.Bone) ? e.Anim.Bone(p.Bone) : null;
                Transform at = b != null ? b : body.transform;
                e.Anim.EventEffect(p, MpSpace.ToGame(at.position), MpEffects.GameOrientation(at.rotation));
            };
            if (a.Model.Animation >= 0) e.Anim.Play(a.Model.Animation, a.Looping, 0f);
            e.Anim.Tick(0f);
            e.Go = body;
            models++;
            if (a.Solid)
            {
                Vector3 ext = new Vector3(a.CollisionExtent.X, a.CollisionExtent.Y, a.CollisionExtent.Z);
                if (ext.x > 0f && ext.y > 0f && ext.z > 0f)
                {
                    var bx = new GameObject("Actor box " + o.Name);
                    bx.transform.SetParent(view.Root.transform, false);
                    bx.transform.localPosition = new Vector3(a.Head.Pos.X + a.Centroid.X, a.Head.Pos.Y + a.Centroid.Y, a.Head.Pos.Z + a.Centroid.Z);
                    var c = bx.AddComponent<BoxCollider>();
                    c.size = ext;
                    e.Solid = c;
                    bx.AddComponent<MpShotVulnerability>().Entity = e;
                }
            }

            e.SetActive(a.Active);
            if (MpAssets.Valid(a.Params.ScanId))
            {
                MpGame g = MpGame.Instance;
                if (g != null && g.Scripts != null)
                    g.Scripts.Add(new MpScanPointEntity { Id = o.Id | 0x80000000u, Type = -1, Area = view.Index, Name = o.Name + " (scan)", Scan = a.Params.ScanId, GamePos = body.transform.localPosition, Follow = body.transform, Active = true, View = view });
            }

            return e;
        }

        public override void OnRemoved()
        {
            if (Anim != null) Anim.StopEffects();
            base.OnRemoved();
        }

        /// <summary>CScriptActorKeyframe::UpdateEntity: an animation (set looping or not) from the start.</summary>
        public virtual void PlayKeyframe(int anim, bool loop)
        {
            if (Anim == null) return;
            Anim.Play(anim, loop, 0.1f);
        }

        public override void Think(float dt)
        {
            if (!Active || Anim == null || Go == null) return;
            Anim.Tick(dt);
            if (_fadeDur > 0f)
            {
                _fadeT += dt;
                if (_fadeT >= _fadeDur) { _fadeDur = 0f; if (_fadingOut) SetActive(false); }
            }
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Increment:     // CScriptActor: fade in (activates)
                    if (!Active) OnMessage(MpScriptMsg.Activate, from);
                    return;
                case MpScriptMsg.Decrement:     // fade out (ColorModulate FadeOutHelper): gone after a short fade
                    _fadingOut = true;
                    _fadeT = 0f;
                    _fadeDur = 0.5f;
                    return;
                case MpScriptMsg.Activate:
                    _fadingOut = false;
                    break;
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>
    /// CScriptActorKeyframe: Action (while active) plays its animation on every actor / platform / enemy linked Play -> Play
    /// (activating them first), then sends Play; a timed loop stops looping after its lifetime.
    /// </summary>
    public sealed class MpActorKeyframeEntity : MpEntity
    {
        public int Anim;
        public bool Looping, TimedLoop;
        public float Lifetime, TotalPlayback, Left;
        private bool _playing;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Action && Active)
            {
                if (Links != null)
                    foreach (MpScriptObject.Link l in Links)
                    {
                        if (l.State != MpScriptMsg.StPlay || l.Message != MpScriptMsg.Play) continue;
                        List<MpEntity> list;
                        if (!World.ById.TryGetValue(l.Target, out list)) continue;
                        foreach (MpEntity e in list.ToArray()) Apply(e);
                    }

                _playing = true;
                Left = Lifetime;
                Send(MpScriptMsg.StPlay);
            }

            base.OnMessage(msg, from);
        }

        private void Apply(MpEntity e)
        {
            if (!e.Active) e.OnMessage(MpScriptMsg.Activate, this);
            var a = e as MpAnimActorEntity;
            if (a != null) { a.PlayKeyframe(Anim, Looping); return; }
            var p = e as MpMovingPlatform;
            if (p != null && p.Anim != null) { p.Anim.Play(Anim, Looping, 0.1f); return; }
            var en = e as MpEnemy;
            if (en != null) en.PlayAnim(Anim, Looping, 0.1f);
        }

        public override void Think(float dt)
        {
            if (!TimedLoop || !_playing || !Looping || Left <= 0f) return;
            Left -= dt;
            if (Left > 0f) return;
            _playing = false;
        }
    }

    // ---------------------------------------------------------------------------------------------------------- platforms

    /// <summary>
    /// CScriptPlatform: a model with its own collision (DCLN, else its box) that follows waypoints: Start sets off toward the next
    /// waypoint (the one linked Follow first) at that waypoint's speed, Arrived goes to each waypoint reached, a waypoint with no
    /// speed or no next one stops it (Stop), Next jumps to the next waypoint at once. Actors and effects linked Play -> Activate
    /// ride with it. When Samus stands on it the world slides under her instead (MpGame.Ride): the client's avatar is never moved.
    /// </summary>
    public sealed class MpMovingPlatform : MpPlatformEntity
    {
        public MpAnimator Anim;
        public float Speed, CurSpeed;
        public MpWaypointEntity Current, Target;
        public Vector3 GamePos;
        public Vector3 Extent, Centroid;
        private readonly List<KeyValuePair<Transform, Vector3>> _slaves = new List<KeyValuePair<Transform, Vector3>>();
        private bool _slavesBuilt;
        public Vector3 LastDelta;

        private static Vector3 V(MpVec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        public static MpEntity Make(MpScriptObject o, MpAreaView view, ref int models)
        {
            MpScriptRead.Platform pl = MpScriptRead.ReadPlatform(o);
            if (pl == null) return null;
            var e = new MpMovingPlatform { ScriptActor = true, Vuln = pl.Vuln, Health = pl.Health, Speed = pl.Speed, CurSpeed = pl.Speed, GamePos = V(pl.Head.Pos), Extent = V(pl.Extent), Centroid = V(pl.Centroid) };
            MpCharacter ch = MpAssets.Valid(pl.Model.Ancs) ? MpCharacter.Get(pl.Model.Ancs, 0) : null;
            GameObject go;
            if (ch != null && ch.Model != null)
            {
                ch.Upload(true);
                go = new GameObject("Platform " + o.Name);
                go.transform.SetParent(view.Root.transform, false);
                e.Anim = new MpAnimator(ch, "Platform " + o.Name + " model", go.transform);
                if (pl.Model.Animation >= 0) e.Anim.Play(pl.Model.Animation, true, 0f);
                e.Anim.Tick(0f);
                models++;
            }
            else
            {
                MpGame g = MpGame.Instance;
                uint cmdl = g != null ? MpModels.CmdlOf(g.Assets, pl.Model) : 0xFFFFFFFF;
                MpModel m = MpModels.Get(cmdl);
                if (m != null) { m.Build(true); go = m.Instantiate("Platform " + o.Name, view.Root.transform); models++; }
                else { go = new GameObject("Platform " + o.Name); go.transform.SetParent(view.Root.transform, false); }
            }

            go.transform.localPosition = e.GamePos;
            go.transform.localRotation = MpSpace.GameRotation(V(pl.Head.Rot));
            go.transform.localScale = V(pl.Head.Scale);
            e.Go = go;
            MpCollision dcln;
            if (view.Payload.Dclns.TryGetValue(pl.Dcln, out dcln) && dcln != null)
            {
                var mesh = new Mesh { name = "DCLN " + pl.Dcln.ToString("X8") };
                var verts = new Vector3[dcln.Vertices.Length / 3];
                for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(dcln.Vertices[i * 3], dcln.Vertices[i * 3 + 1], dcln.Vertices[i * 3 + 2]);
                mesh.vertices = verts;
                mesh.triangles = dcln.Triangles;
                var col = new GameObject("Platform collision");
                col.transform.SetParent(go.transform, false);
                var mc = col.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                e.Solid = mc;
            }
            else
            {
                Vector3 ext = e.Extent;
                if (ext.x > 0f && ext.y > 0f && ext.z > 0f)
                {
                    var bx = new GameObject("Platform box");
                    bx.transform.SetParent(go.transform, false);
                    bx.transform.localPosition = Quaternion.Inverse(go.transform.localRotation) * e.Centroid;
                    var c = bx.AddComponent<BoxCollider>();
                    c.size = new Vector3(ext.x / Mathf.Max(1e-3f, Mathf.Abs(pl.Head.Scale.X)), ext.y / Mathf.Max(1e-3f, Mathf.Abs(pl.Head.Scale.Y)), ext.z / Mathf.Max(1e-3f, Mathf.Abs(pl.Head.Scale.Z)));
                    e.Solid = c;
                }
                else if (go.GetComponent<MeshFilter>() != null)
                {
                    var c = go.AddComponent<MeshCollider>();
                    c.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                    e.Solid = c;
                }
            }

            // shots reach it (its health and vulnerability)
            if (e.Solid != null)
            {
                e.Solid.gameObject.AddComponent<MpHitTarget>().Entity = e;
                e.Solid.gameObject.AddComponent<MpShotVulnerability>().Entity = e;
            }

            e.SetActive(pl.Active);
            return e;
        }

        private MpWaypointEntity FirstWaypoint()
        {
            if (Links == null) return null;
            foreach (MpScriptObject.Link l in Links)
                if (l.Message == MpScriptMsg.Follow)
                {
                    var w = World.Find(l.Target) as MpWaypointEntity;
                    if (w != null) return w;
                }

            return null;
        }

        /// <summary>CScriptPlatform::GetNext: the next waypoint after one (the Follow waypoint when there is none); its speed is taken.</summary>
        private MpWaypointEntity GetNext(MpWaypointEntity from)
        {
            MpWaypointEntity n = from == null ? FirstWaypoint() : from.Next();
            if (n != null && from != null) CurSpeed = n.Speed;
            return n;
        }

        private void BuildSlaves()
        {
            _slavesBuilt = true;
            if (Links == null || Go == null) return;
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != MpScriptMsg.StPlay || l.Message != MpScriptMsg.Activate) continue;
                List<MpEntity> list;
                if (!World.ById.TryGetValue(l.Target, out list)) continue;
                foreach (MpEntity e in list)
                {
                    Transform t = SlaveTransform(e);
                    if (t != null) _slaves.Add(new KeyValuePair<Transform, Vector3>(t, t.localPosition - Go.transform.localPosition));
                }
            }
        }

        private static Transform SlaveTransform(MpEntity e)
        {
            var a = e as MpActorEntity;
            if (a != null && a.Go != null) return a.Go.transform;
            var fx = e as MpEffectEntity;
            if (fx != null) return fx.Anchor;
            return null;
        }

        public override void OnMessage(int msg, MpEntity from)
        {
            switch (msg)
            {
                case MpScriptMsg.Stop:
                    CurSpeed = 0f;
                    break;
                case MpScriptMsg.Next:
                {
                    MpWaypointEntity t = GetNext(Current);
                    if (t == null) { OnMessage(MpScriptMsg.Stop, this); break; }
                    CurSpeed = 0f;
                    MoveTo(t.GamePos);
                    Current = t;
                    Target = GetNext(Current);
                    t.OnMessage(MpScriptMsg.Arrived, this);
                    break;
                }

                case MpScriptMsg.Start:
                {
                    Target = GetNext(Current);
                    if (Target == null) { OnMessage(MpScriptMsg.Stop, this); break; }
                    CurSpeed = Target.Speed;
                    break;
                }

                case MpScriptMsg.Increment:
                    if (!Active) OnMessage(MpScriptMsg.Activate, from);
                    break;
            }

            base.OnMessage(msg, from);
        }

        private void MoveTo(Vector3 p)
        {
            Vector3 d = p - GamePos;
            GamePos = p;
            LastDelta += d;
            if (Go != null) Go.transform.localPosition = p;
            foreach (var kv in _slaves) if (kv.Key != null) kv.Key.localPosition = p + kv.Value;
        }

        /// <summary>CScriptPlatform::Move: toward the target waypoint at the current speed; reaching one sends it Arrived and goes on (or stops).</summary>
        public override void Think(float dt)
        {
            if (!_slavesBuilt) BuildSlaves();
            if (!Active) return;
            if (Anim != null) Anim.Tick(dt);
            // CScriptPlatform::PreThink -> Move: an active platform always heads for its next waypoint at its current speed (0 = held)
            int guard = 8;
            while (guard-- > 0)
            {
                MpWaypointEntity wp = Target ?? GetNext(Current);
                if (wp == null) return;
                Vector3 delta = wp.GamePos - GamePos;
                if (delta.sqrMagnitude <= 0.02f * 0.02f)
                {
                    Current = wp;
                    wp.OnMessage(MpScriptMsg.Arrived, this);
                    bool stop = Mathf.Abs(CurSpeed) < 0.02f;
                    Target = GetNext(Current);
                    if (stop) { CurSpeed = 0f; return; }
                    if (Target == null) { OnMessage(MpScriptMsg.Stop, this); return; }
                    continue;
                }

                if (Mathf.Abs(CurSpeed) < 1e-4f) return;
                float step = CurSpeed * dt;
                if (step * step >= delta.sqrMagnitude) MoveTo(wp.GamePos);
                else MoveTo(GamePos + delta.normalized * step);
                return;
            }
        }

        /// <summary>
        /// Co-op, a helper: the master's platform (where it is, the waypoints it is between, its speed). Small differences are left
        /// alone (both move it the same way); a larger one moves it there - through MoveTo, so a rider is carried along.
        /// </summary>
        public void Correct(Vector3 pos, uint current, uint target, float speed)
        {
            Current = current != 0 ? World.FindNet(current) as MpWaypointEntity : null;
            Target = target != 0 ? World.FindNet(target) as MpWaypointEntity : null;
            CurSpeed = speed;
            if ((pos - GamePos).sqrMagnitude > 0.3f * 0.3f) MoveTo(pos);
        }

        /// <summary>The platform's top surface holds the given game point (Samus's feet), for riding.</summary>
        public bool Carries(Vector3 feet)
        {
            if (Solid == null || !Solid.enabled || !Active) return false;
            Bounds b = Solid.bounds;
            Vector3 u = MpSpace.ToUnity(feet);
            float pad = 0.25f;
            return u.x >= b.min.x - pad && u.x <= b.max.x + pad && u.z >= b.min.z - pad && u.z <= b.max.z + pad && u.y >= b.max.y - 0.6f && u.y <= b.max.y + 0.6f;
        }
    }

    // ---------------------------------------------------------------------------------------------------------- effects

    /// <summary>
    /// CScriptEffect: the game's own particle system (PART) placed at the object (its scale), running while active; Deactivate
    /// lets it finish, Activate starts it again (rebuilt when the object says so).
    /// </summary>
    public sealed class MpEffectEntity : MpEntity
    {
        public uint Part;
        public Vector3 Scale = Vector3.one;
        public Transform Anchor;
        public bool Rebuild, DieWhenDone;
        private MpEffects.Effect _fx;

        public static uint PartOf(MpScriptObject o)
        {
            if (o.PropCount != 24) return 0xFFFFFFFF;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.ReadHead(r, true);
            return r.U32();
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view)
        {
            if (o.PropCount != 24) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, true);
            var e = new MpEffectEntity { Part = r.U32() };
            r.U32();
            r.Bool(); r.Bool();
            e.Rebuild = r.Bool();
            e.Active = r.Bool();
            e.Scale = new Vector3(h.Scale.X, h.Scale.Y, h.Scale.Z);
            if (!MpAssets.Valid(e.Part)) return null;
            e.Anchor = new GameObject("Effect " + o.Name).transform;
            e.Anchor.SetParent(view.Root.transform, false);
            e.Anchor.localPosition = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z);
            e.Anchor.localRotation = MpSpace.GameRotation(new Vector3(h.Rot.X, h.Rot.Y, h.Rot.Z));
            return e;
        }

        public override void OnLoaded()
        {
            if (Active) Start();
        }

        private void Start()
        {
            if (_fx != null && !_fx.Dead && !Rebuild) return;
            if (_fx != null) _fx.Finish();
            _fx = MpEffects.Spawn(Part, Anchor.localPosition, MpEffects.GameOrientation(Anchor.rotation), true);
            if (_fx != null) _fx.SetGlobal(Anchor.localPosition, MpEffects.GameOrientation(Anchor.rotation), Mathf.Max(Scale.x, Mathf.Max(Scale.y, Scale.z)));
        }

        public override void SetActive(bool on)
        {
            bool was = Active;
            base.SetActive(on);
            if (on && !was) Start();
            else if (!on && was && _fx != null) { _fx.Finish(); _fx = null; }
        }

        public override void Think(float dt)
        {
            if (_fx == null || Anchor == null) return;
            // a riding effect (platform slave) follows its anchor
            _fx.SetPlace(Anchor.localPosition, MpEffects.GameOrientation(Anchor.rotation));
        }

        public override void OnRemoved()
        {
            if (_fx != null) _fx.Kill();
            if (Anchor != null) Object.Destroy(Anchor.gameObject);
        }
    }

    // ---------------------------------------------------------------------------------------------------------- generators and pickups

    /// <summary>
    /// CScriptGenerator: SetToZero (while active) sends its Zero links' other messages, then makes up to SpawnCount copies of the
    /// objects linked Zero -> Activate (a sound when there is one), each placed at a random "follow" object (Zero -> Follow, else
    /// the sender) plus the offset, scaled at random, and activated. The copies keep the original's editor id (messages to it reach them).
    /// </summary>
    public sealed class MpGeneratorEntity : MpEntity
    {
        public int SpawnCount;
        public bool NoReuseFollowers, NoInheritXf;
        public Vector3 Offset;
        public float MinScale = 1f, MaxScale = 1f;
        public static int Copies;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.SetToZero && Active) Generate(from);
            base.OnMessage(msg, from);
        }

        /// <summary>Co-op, the master: a copy was made (generator, template, the copy, where, its yaw, its scale) - the helpers make the same one.</summary>
        public static System.Action<MpEntity, MpEntity, MpEntity, Vector3, float, float> OnGenerated;

        private void Generate(MpEntity sender)
        {
            if (Links == null || MpScripts.Follower) return;      // a helper: the master's copies arrive (MpCoop, Replay)
            var follows = new List<MpEntity>();
            var activates = new List<MpEntity>();
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != MpScriptMsg.StZero) continue;
                MpEntity t = World.Find(l.Target);
                if (t == null) continue;
                if (l.Message == MpScriptMsg.Follow) { if (t.Active) follows.Add(t); }
                else if (l.Message == MpScriptMsg.Activate) activates.Add(t);
                else World.Deliver(this, l.Target, l.Message, MpScriptMsg.StZero);
            }

            if (follows.Count == 0 && sender != null) follows.Add(sender);
            for (int i = 0; i < SpawnCount && activates.Count > 0 && follows.Count > 0; i++)
            {
                int ai = Mathf.Min(activates.Count - 1, (int)(0.99f * Random.value * activates.Count));
                for (int j = 0; j < activates.Count; j++) if (activates[j] is MpSoundEntity) { ai = j; break; }
                int fi = Mathf.Min(follows.Count - 1, (int)(0.99f * Random.value * follows.Count));
                MpEntity template = activates[ai];
                float scale;
                MpEntity copy = Copy(template, follows[fi], out scale);
                if (copy != null && OnGenerated != null) { Vector3 at; PositionOf(copy, out at); var en = copy as MpEnemy; OnGenerated(this, template, copy, at, en != null ? en.Yaw : 0f, scale); }
                if (copy != null) copy.OnMessage(MpScriptMsg.Activate, this);
                activates.RemoveAt(ai);
                if (NoReuseFollowers) follows.RemoveAt(fi);
            }
        }

        /// <summary>CStateManager::GenerateObject: a fresh entity from the template's script object, placed at the follow object.</summary>
        private MpEntity Copy(MpEntity template, MpEntity follow, out float s)
        {
            s = 1f;
            if (template.Source == null || template.View == null || template.View.Root == null) return null;
            if (template.GetType() == typeof(MpEntity)) return null;      // a kind not in the Dreamport (debris): no endless empty copies
            int models = 0;
            MpEntity e = MpEntityFactory.MakeEntity(template.Source, template.View, World, ref models);
            e.Generated = true;
            Copies++;
            Vector3 at;
            if (PositionOf(follow, out at)) Place(e, at + Offset, follow);
            s = Random.Range(MinScale, MaxScale);
            ScaleBy(e, s);
            e.OnLoaded();
            return e;
        }

        /// <summary>
        /// Co-op, a helper: the master's copy of a template made here too, under the master's id, at its place, yaw and scale, then
        /// activated by its generator (or the master's pickup generator) as the master's was.
        /// </summary>
        public static MpEntity Replay(MpScripts world, MpEntity generator, MpEntity template, uint netId, Vector3 at, float yaw, float scale)
        {
            if (template == null || template.Source == null || template.View == null || template.View.Root == null) return null;
            if (template.GetType() == typeof(MpEntity)) return null;
            int models = 0;
            MpEntity e = MpEntityFactory.MakeEntity(template.Source, template.View, world, ref models);
            e.Generated = true;
            world.SetNetId(e, netId);
            var en = e as MpEnemy;
            if (en != null) { en.Pos = at; en.Yaw = yaw; en.ApplyYaw(); }
            var a = e as MpActorEntity;
            if (en == null && a != null && a.Go != null) a.Go.transform.localPosition = at;
            var p = e as MpPickupEntity;
            if (p != null) p.MoveTo(at);
            var snd = e as MpSoundEntity;
            if (snd != null) snd.GamePos = at;
            var fx = e as MpEffectEntity;
            if (fx != null && fx.Anchor != null) fx.Anchor.localPosition = at;
            ScaleBy(e, scale);
            e.OnLoaded();
            e.OnMessage(MpScriptMsg.Activate, generator);
            if (p != null) p.SetActive(true);
            return e;
        }

        public static bool PositionOf(MpEntity e, out Vector3 game)
        {
            var en = e as MpEnemy;
            if (en != null && en.Body != null) { game = en.Pos; return true; }
            var a = e as MpActorEntity;
            if (a != null && a.Go != null) { game = a.Go.transform.localPosition; return true; }
            var w = e as MpWaypointEntity;
            if (w != null) { game = w.GamePos; return true; }
            var fx = e as MpEffectEntity;
            if (fx != null && fx.Anchor != null) { game = fx.Anchor.localPosition; return true; }
            var sp = e as MpSpecialFunctionEntity;
            if (sp != null) { game = sp.GamePos; return true; }
            var snd = e as MpSoundEntity;
            if (snd != null) { game = snd.GamePos; return true; }
            game = Vector3.zero;
            return false;
        }

        private void Place(MpEntity e, Vector3 at, MpEntity follow)
        {
            var en = e as MpEnemy;
            if (en != null)
            {
                en.Pos = at;
                var fen = follow as MpEnemy;
                if (!NoInheritXf && fen != null) { en.Yaw = fen.Yaw; }
                return;
            }

            var a = e as MpActorEntity;
            if (a != null && a.Go != null) a.Go.transform.localPosition = at;
            var p = e as MpPickupEntity;
            if (p != null) p.MoveTo(at);
            var snd = e as MpSoundEntity;
            if (snd != null) snd.GamePos = at;
            var fx = e as MpEffectEntity;
            if (fx != null && fx.Anchor != null) fx.Anchor.localPosition = at;
        }

        private static void ScaleBy(MpEntity e, float s)
        {
            if (Mathf.Abs(s - 1f) < 1e-3f) return;
            var en = e as MpEnemy;
            if (en != null && en.Anim != null) { en.Anim.Go.transform.localScale *= s; return; }
            var a = e as MpActorEntity;
            if (a != null && a.Go != null) a.Go.transform.localScale *= s;
        }
    }

    /// <summary>
    /// CScriptPickup: an item lying in the world (energy, missiles, an upgrade). Samus touching it gives the item (capacity, then
    /// amount), plays its pickup effect, sends Arrived and it is gone; a generated one fades in, lives its lifetime and fades out.
    /// </summary>
    public sealed class MpPickupEntity : MpEntity
    {
        public int Item, Capacity, Amount;
        public float Possibility, Lifetime, FadeIn, StartDelay, Age;
        public uint PickupEffect;
        public Transform Body;
        public MpAnimator Anim;
        public Vector3 Extent = new Vector3(1f, 1f, 1f);
        private bool _taken;

        public static void ReadModel(MpScriptObject o, out MpModelRef m, out uint effect)
        {
            m = default(MpModelRef);
            effect = 0xFFFFFFFF;
            if (o.PropCount != 18) return;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.ReadHead(r, true);
            MpVec3.Read(r); MpVec3.Read(r);
            r.S32(); r.S32(); r.S32(); r.F32(); r.F32(); r.F32();
            uint staticId = r.U32();
            m = MpScriptRead.ReadAnimParams(r, staticId);
            MpScriptRead.ReadActorParams(r);
            r.Bool(); r.F32();
            effect = r.U32();
        }

        public static MpEntity Make(MpScriptObject o, MpAreaView view, ref int models)
        {
            if (o.PropCount != 18) return null;
            var r = new MpReader(o.Data, o.PropAt, o.PropEnd - o.PropAt);
            MpScriptRead.Head h = MpScriptRead.ReadHead(r, true);
            var e = new MpPickupEntity();
            Vector3 ext = new Vector3(r.F32(), r.F32(), r.F32());
            MpVec3.Read(r);
            e.Item = r.S32(); e.Capacity = r.S32(); e.Amount = r.S32();
            e.Possibility = r.F32(); e.Lifetime = r.F32(); e.FadeIn = r.F32();
            uint staticId = r.U32();
            MpModelRef m = MpScriptRead.ReadAnimParams(r, staticId);
            MpScriptRead.ReadActorParams(r);
            e.Active = r.Bool();
            e.StartDelay = r.F32();
            e.PickupEffect = r.U32();
            if (ext.x > 0f && ext.y > 0f && ext.z > 0f) e.Extent = ext;
            e.Body = new GameObject("Pickup " + o.Name).transform;
            e.Body.SetParent(view.Root.transform, false);
            e.Body.localPosition = new Vector3(h.Pos.X, h.Pos.Y, h.Pos.Z);
            e.Body.localRotation = MpSpace.GameRotation(new Vector3(h.Rot.X, h.Rot.Y, h.Rot.Z));
            e.Body.localScale = new Vector3(h.Scale.X, h.Scale.Y, h.Scale.Z);
            MpCharacter ch = MpAssets.Valid(m.Ancs) ? MpCharacter.Get(m.Ancs, m.Character) : null;
            if (ch != null && ch.Model != null)
            {
                ch.Upload(true);
                e.Anim = new MpAnimator(ch, "Pickup model", e.Body);
                if (m.Animation >= 0) e.Anim.Play(m.Animation, true, 0f);
                e.Anim.Tick(0f);
                models++;
            }
            else if (MpAssets.Valid(m.Cmdl))
            {
                MpModel model = MpModels.Get(m.Cmdl);
                if (model != null) { model.Build(true); model.Instantiate("Pickup model", e.Body); models++; }
            }

            e.Body.gameObject.SetActive(e.Active);
            return e;
        }

        public void MoveTo(Vector3 game) { if (Body != null) Body.localPosition = game; }

        public override void SetActive(bool on)
        {
            base.SetActive(on);
            if (Body != null) Body.gameObject.SetActive(on && !_taken);
        }

        public override void Think(float dt)
        {
            if (!Active || _taken || Body == null) return;
            if (StartDelay > 0f) { StartDelay -= dt; return; }
            Age += dt;
            if (Anim != null) Anim.Tick(dt);
            else Body.localRotation = Quaternion.AngleAxis(90f * dt, Vector3.forward) * Body.localRotation;
            if (Lifetime > 0f && Age > Lifetime) { Gone(); return; }
            // CScriptPickup::Touch: Samus's body within the pickup's box
            Vector3 d = MpEnemy.PlayerPos - Body.localPosition;
            float rx = Extent.x * 0.5f + 0.6f, ry = Extent.y * 0.5f + 0.6f;
            if (Mathf.Abs(d.x) > rx || Mathf.Abs(d.y) > ry || d.z > Extent.z * 0.5f + 0.5f || d.z < -2.7f) return;
            // co-op: a helper asks the master for it (the master gives it and says so)
            if (MpScripts.Follower) { if (TakeAsk != null && Time.time >= _askT) { _askT = Time.time + 1f; TakeAsk(this); } return; }
            Take(-1);
        }

        /// <summary>Co-op, a helper: asks the master for this pickup (MpCoop).</summary>
        public static System.Action<MpPickupEntity> TakeAsk;

        /// <summary>Co-op, the master: another player took a pickup (their id): the item is theirs to apply (MpCoop sends it).</summary>
        public static System.Action<int, MpPickupEntity> GivenTo;
        private float _askT;

        public bool Taken { get { return _taken; } }

        /// <summary>Samus touched it: this player (-1) or, on the master, another player who asked.</summary>
        public void Take(int player)
        {
            MpGame g = MpGame.Instance;
            if (g == null || _taken) return;
            _taken = true;
            if (MpAssets.Valid(PickupEffect)) MpEffects.Spawn(PickupEffect, Body.localPosition, MpEffects.GameOrientation(Body.rotation));
            if (player < 0) g.GiveItem(Item, Capacity, Amount);
            else
            {
                g.GiveUnlock(Item, Capacity);      // the game's unlocks are the master's (everyone gets them); the refill is the taker's
                if (GivenTo != null) GivenTo(player, this);
            }

            Send(MpScriptMsg.StArrived);
            Gone();
        }

        public override int NetWord { get { return _taken ? 1 : 0; } }

        public override void ApplyNetWord(int word)
        {
            if ((word & 1) != 0 && !_taken)
            {
                if (Body != null && Body.gameObject.activeInHierarchy && MpAssets.Valid(PickupEffect)) MpEffects.Spawn(PickupEffect, Body.localPosition, MpEffects.GameOrientation(Body.rotation));
                Gone();
            }
        }

        private void Gone()
        {
            _taken = true;
            if (Body != null) Body.gameObject.SetActive(false);
        }

        public override void OnRemoved()
        {
            if (Body != null) Object.Destroy(Body.gameObject);
        }
    }

    /// <summary>
    /// CScriptPickupGenerator: SetToZero (an enemy's death) maybe drops one of its pickup templates (Zero -> Activate) at the
    /// sender plus the offset, picked by the templates' possibilities - missiles and energy only when Samus can use them (else
    /// 30%), and now and then nothing at all (the frequency).
    /// </summary>
    public sealed class MpPickupGeneratorEntity : MpEntity
    {
        public Vector3 Offset;
        public float Frequency, Delay;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.SetToZero && Active && Frequency != 100f)
            {
                Delay -= 1f;
                if (Delay < 1e-5f) Delay += Frequency > 0f ? 100f / Frequency : float.MaxValue;
                else Drop(from);
            }

            base.OnMessage(msg, from);
        }

        private void Drop(MpEntity sender)
        {
            MpGame g = MpGame.Instance;
            if (g == null || Links == null || MpScripts.Follower) return;      // a helper: the master's drop arrives (MpCoop)
            MpEntity follow = sender;
            var choices = new List<KeyValuePair<float, MpPickupEntity>>();
            float total = 0f;
            foreach (MpScriptObject.Link l in Links)
            {
                if (l.State != MpScriptMsg.StZero) continue;
                MpEntity t = World.Find(l.Target);
                if (l.Message == MpScriptMsg.Follow && t != null && t.Active) follow = t;
                var p = t as MpPickupEntity;
                if (l.Message != MpScriptMsg.Activate || p == null) continue;
                bool always = true, thirty = false;
                if (p.Item == MpPlayerState.ItemMissiles) { always = g.State.MissileCapacity > 0 && g.State.Missiles < g.State.MissileCapacity; thirty = g.State.MissileCapacity > 0 && !always; }
                else if (p.Item == MpPlayerState.ItemHealthRefill) { always = g.State.Energy < g.State.MaxEnergy; thirty = !always; }
                if ((always || (thirty && Random.value < 0.3f)) && p.Possibility > 0f) { total += p.Possibility; choices.Add(new KeyValuePair<float, MpPickupEntity>(p.Possibility, p)); }
            }

            if (choices.Count == 0) return;
            float r = Random.Range(0f, total), acc = 0f;
            MpPickupEntity pick = null;
            foreach (var kv in choices) { if (r >= acc && r <= acc + kv.Key) { pick = kv.Value; break; } acc += kv.Key; }
            if (pick == null || pick.Source == null || pick.View == null) return;
            int models = 0;
            var copy = MpEntityFactory.MakeEntity(pick.Source, pick.View, World, ref models) as MpPickupEntity;
            if (copy == null) return;
            copy.Generated = true;
            Vector3 at;
            if (MpGeneratorEntity.PositionOf(follow, out at)) copy.MoveTo(at + Offset);
            if (MpGeneratorEntity.OnGenerated != null) { Vector3 p; MpGeneratorEntity.PositionOf(copy, out p); if (copy.Body != null) p = copy.Body.localPosition; MpGeneratorEntity.OnGenerated(this, pick, copy, p, 0f, 1f); }
            copy.OnMessage(MpScriptMsg.Activate, this);
            copy.SetActive(true);
        }
    }

    // ---------------------------------------------------------------------------------------------------------- relays, switches, shakers, damage

    /// <summary>CScriptRandomRelay: SetToZero sends Zero to a random part of its Zero links (a count, or a percentage, with a variance).</summary>
    public sealed class MpRandomRelayEntity : MpEntity
    {
        public int SetSize, Variance;
        public bool Percent;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.SetToZero && Active && Links != null)
            {
                var zero = new List<MpScriptObject.Link>();
                foreach (MpScriptObject.Link l in Links) if (l.State == MpScriptMsg.StZero) zero.Add(l);
                int n = Percent ? Mathf.RoundToInt(zero.Count * (SetSize + Random.Range(-Variance, Variance + 1)) / 100f) : SetSize + Random.Range(-Variance, Variance + 1);
                n = Mathf.Clamp(n, 0, zero.Count);
                for (int i = 0; i < n; i++)
                {
                    int k = Random.Range(0, zero.Count);
                    World.Deliver(this, zero[k].Target, zero[k].Message, MpScriptMsg.StZero);
                    zero.RemoveAt(k);
                }
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>CScriptSwitch: Open / Close set it; SetToZero sends Open while opened (and closes it again when told to).</summary>
    public sealed class MpSwitchEntity : MpEntity
    {
        public bool Opened, CloseOnOpened;

        public override int NetWord { get { return Opened ? 1 : 0; } }

        public override void ApplyNetWord(int word) { Opened = (word & 1) != 0; }

        public override void OnMessage(int msg, MpEntity from)
        {
            if (Active)
            {
                if (msg == MpScriptMsg.Open) Opened = true;
                else if (msg == MpScriptMsg.Close) Opened = false;
                else if (msg == MpScriptMsg.SetToZero)
                {
                    // CScriptSwitch: asked, it answers Open or Closed (the Closed answer was missing: the freight clamp's
                    // "Is First Door Closed" check never opened the Reactor Core Entrance door)
                    if (Opened)
                    {
                        Send(MpScriptMsg.StOpen);
                        if (CloseOnOpened) Opened = false;
                    }
                    else Send(MpScriptMsg.StClosed);
                }
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>Camera shakers and rumble effects: Action shakes the controllers (VR) - the view itself is never shaken (comfort).</summary>
    public sealed class MpShakerEntity : MpEntity
    {
        public float Strength, Seconds;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Action && Active && MpXr.Present && MpSettings.Haptics > 0f)
            {
                MpXr.Pulse(true, Strength * MpSettings.Haptics, Seconds);
                MpXr.Pulse(false, Strength * MpSettings.Haptics, Seconds);
            }

            base.OnMessage(msg, from);
        }
    }

    /// <summary>CScriptSpecialFunction RadialDamage (script type 0x68): Action hurts Samus within the radius (and enemies), falling off with distance.</summary>
    public sealed class MpRadialDamageEntity : MpEntity
    {
        public Vector3 Center;
        public MpDamageInfo Damage;
        public float Radius;

        public override void OnMessage(int msg, MpEntity from)
        {
            if (msg == MpScriptMsg.Action && Active) Apply(Center, Damage.Damage, Radius, Damage.Weapon, this);
            base.OnMessage(msg, from);
        }

        public static void Apply(Vector3 center, float damage, float radius, int weapon, MpEntity source)
        {
            MpGame g = MpGame.Instance;
            if (g == null || radius <= 0f) return;
            float d = (MpEnemy.PlayerChest - center).magnitude;
            if (d < radius) g.DamagePlayer(damage * (1f - d / radius * 0.5f), center, true);
            if (MpScripts.Follower) return;      // co-op: the master hurts the enemies (its own radial damage)
            foreach (MpEnemy e in MpEnemy.Live.ToArray())
            {
                if (e == source || e.Dead) continue;
                float de = (e.Pos - center).magnitude;
                if (de < radius) e.TakeDamage(weapon, false, damage * (1f - de / radius * 0.5f));
            }
        }
    }

    /// <summary>
    /// CScriptWorldTeleporter: Samus leaves the world (MpGame.OnWorldTeleport loads the next one). SetToZero goes at once (the
    /// frigate's end); Play is an elevator's start - the game plays its ride movie (CWorldTransManager) and then changes world;
    /// here the platform gets a few seconds to carry Samus up out of sight first.
    /// </summary>
    public sealed class MpWorldTeleporterEntity : MpEntity
    {
        public uint WorldId, AreaId;
        private float _rideT = -1f;
        public const float RideSeconds = 4f;

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            if (!Active) return;
            if (msg == MpScriptMsg.SetToZero) Go();
            else if (msg == MpScriptMsg.Play && _rideT < 0f) { _rideT = RideSeconds; MpLog.Info("World: elevator '" + Name + "' started"); }
        }

        public override void Think(float dt)
        {
            if (_rideT < 0f) return;
            _rideT -= dt;
            if (_rideT < 0f) Go();
        }

        private void Go()
        {
            _rideT = -1f;
            MpGame g = MpGame.Instance;
            if (g != null) g.OnWorldTeleport(WorldId, AreaId);
        }
    }
}

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// CScriptDock (the door plane between two rooms): each frame it watches the room on the other side - loaded, it sends
    /// MaxReached; dropped, Zero (CScriptDock::Think). SetToMax asks for that room to load (SetLoadConnected). Scripts build on
    /// this: the frigate's Mech Shaft only removes its hatch into the Air Lock once its dock reports the Air Lock loaded.
    /// </summary>
    public sealed class MpDockEntity : MpEntity
    {
        public int DockIndex, Connected = -1;
        private bool _loaded;

        public override void OnMessage(int msg, MpEntity from)
        {
            base.OnMessage(msg, from);
            MpGame g = MpGame.Instance;
            if (msg == MpScriptMsg.SetToMax && Connected >= 0 && g != null && g.WorldView != null) g.WorldView.Request(Connected);
        }

        public override void Think(float dt)
        {
            if (!Active || Connected < 0) return;
            MpGame g = MpGame.Instance;
            if (g == null || g.WorldView == null) return;
            bool now = g.WorldView.IsLoaded(Connected);
            if (now == _loaded) return;
            _loaded = now;
            Send(now ? MpScriptMsg.StMaxReached : MpScriptMsg.StZero);
        }
    }
}
