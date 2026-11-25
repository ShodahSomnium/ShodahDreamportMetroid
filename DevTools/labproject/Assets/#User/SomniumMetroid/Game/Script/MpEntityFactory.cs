using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>Marks a collider that shots can hit and the entity that takes the damage.</summary>
    /// <summary>On a door's box: shots pass it unless the door's ProjectilesCollide is set (the box still blocks the player).</summary>
    public sealed class MpDoorShotGate : MonoBehaviour
    {
        public MpDoorEntity Door;
        public bool Blocks { get { return Door != null && Door.ProjectilesCollide; } }
    }

    public sealed class MpHitTarget : MonoBehaviour
    {
        public MpEntity Entity;
    }

    /// <summary>A solid actor's box: its vulnerability decides whether a shot stops on it (CGameProjectile::CanCollideWith).</summary>
    public sealed class MpShotVulnerability : MonoBehaviour
    {
        public MpActorEntity Entity;
    }

    /// <summary>
    /// The script objects of a built area as entities (only those on the area's active layers, as CStateManager loads them). Models
    /// are placed in game space under the area root with the game's editor transform (position, Z*Y*X euler, scale). Collision boxes
    /// are axis-aligned in area space at the object's position + offset (ScriptLoader GetCollisionBox); platforms use their DCLN.
    /// </summary>
    public static class MpEntityFactory
    {
        public static void Build(MpAreaView view, MpScripts scripts)
        {
            MpAreaPayload p = view.Payload;
            int made = 0, models = 0;
            foreach (MpScriptObject o in p.Objects)
            {
                if (!MpAreaView.LayerActive(view.Info, o.Layer)) continue;
                MakeEntity(o, view, scripts, ref models);
                made++;
            }

            MpEffects.Upload();
            scripts.AreaLoaded(view.Index);
            MpLog.Info("Scripts: area " + view.Index + " '" + view.Info.Name + "': " + made + " entities, " + models + " models placed");
        }

        /// <summary>One script object as an entity, added to the scripts (also how generators make their copies: CStateManager::GenerateObject).</summary>
        public static MpEntity MakeEntity(MpScriptObject o, MpAreaView view, MpScripts scripts, ref int models)
        {
            MpEntity e = Make(o, view, ref models);
            if (e == null) e = new MpEntity { Active = true };
            e.Id = o.Id;
            e.Type = o.Type;
            e.Area = view.Index;
            e.Name = o.Name;
            e.Links = o.Links;
            e.View = view;
            e.Source = o;
            scripts.Add(e);
            return e;
        }

        private static Vector3 V(MpVec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        private static GameObject PlaceModel(MpAreaView view, MpModelRef mr, MpScriptRead.Head h, string name, ref int models)
        {
            MpGame g = MpGame.Instance;
            if (g == null || g.Assets == null) return null;
            uint cmdl = MpModels.CmdlOf(g.Assets, mr);
            MpModel m = MpModels.Get(cmdl);
            if (m == null) return null;
            m.Build(true);
            GameObject go = m.Instantiate(name, view.Root.transform);
            go.transform.localPosition = V(h.Pos);
            go.transform.localRotation = MpSpace.GameRotation(V(h.Rot));
            go.transform.localScale = V(h.Scale);
            models++;
            return go;
        }

        private static BoxCollider Box(MpAreaView view, string name, Vector3 center, Vector3 size, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(view.Root.transform, false);
            go.transform.localPosition = center;
            go.layer = layer;
            var b = go.AddComponent<BoxCollider>();
            b.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            return b;
        }

        /// <summary>The collision box of an actor: extent/offset when set, else the model's bounds turned by the object (GetBounds).</summary>
        private static BoxCollider ActorBox(MpAreaView view, string name, GameObject go, MpScriptRead.Head h, Vector3 extent, Vector3 offset, int layer)
        {
            Vector3 pos = V(h.Pos);
            bool none = extent == Vector3.zero || extent.x < 0f || extent.y < 0f || extent.z < 0f;
            if (!none) return Box(view, name, pos + offset, extent, layer);
            if (go == null) return null;
            var mf = go.GetComponent<MeshFilter>();
            Mesh shape = mf != null ? mf.sharedMesh : null;
            if (shape == null)
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) shape = smr.sharedMesh;
            }

            if (shape == null) return null;
            Bounds b = shape.bounds;
            // the model's box turned by the actor's rotation and scale, as an axis-aligned box in area space
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, go.transform.localRotation, go.transform.localScale);
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), mx = -mn;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
                Vector3 t = m.MultiplyPoint3x4(c);
                mn = Vector3.Min(mn, t);
                mx = Vector3.Max(mx, t);
            }

            return Box(view, name, pos + (mn + mx) * 0.5f, mx - mn, layer);
        }

        private static MpEntity Make(MpScriptObject o, MpAreaView view, ref int models)
        {
            if (MpEnemyFactory.IsEnemy(o.Type)) return MpEnemyFactory.Make(o, view);
            MpEntity more = MpScriptEntities.Make(o, view, ref models);
            if (more != null) return more;
            switch (o.Type)
            {
                case MpScriptType.Actor:
                {
                    MpScriptRead.Actor a = MpScriptRead.ReadActor(o);
                    if (a == null) return null;
                    var e = new MpActorEntity { Health = a.Health, Vuln = a.Vuln, ScriptActor = true };
                    e.Go = PlaceModel(view, a.Model, a.Head, "Actor " + o.Name, ref models);
                    if (MpAssets.Valid(a.Params.XrayModel)) e.XrayGo = PlaceModel(view, new MpModelRef { Cmdl = a.Params.XrayModel, Ancs = 0xFFFFFFFF }, a.Head, "Actor " + o.Name + " (x-ray)", ref models);
                    if (a.Solid) e.Solid = ActorBox(view, "Actor box " + o.Name, e.Go, a.Head, V(a.CollisionExtent), V(a.Centroid), 0);
                    if (e.Solid != null) e.Solid.gameObject.AddComponent<MpShotVulnerability>().Entity = e;
                    e.SetActive(a.Active);
                    if (MpAssets.Valid(a.Params.ScanId)) AddScan(view, o, a.Params.ScanId, V(a.Head.Pos), e.Go != null ? e.Go.transform : null);
                    return e;
                }

                case MpScriptType.Platform:
                {
                    MpScriptRead.Platform pl = MpScriptRead.ReadPlatform(o);
                    if (pl == null) return null;
                    var e = new MpPlatformEntity { Health = pl.Health, ScriptActor = true };
                    e.Go = PlaceModel(view, pl.Model, pl.Head, "Platform " + o.Name, ref models);
                    if (MpAssets.Valid(pl.Params.XrayModel)) e.XrayGo = PlaceModel(view, new MpModelRef { Cmdl = pl.Params.XrayModel, Ancs = 0xFFFFFFFF }, pl.Head, "Platform " + o.Name + " (x-ray)", ref models);
                    MpCollision dcln;
                    if (e.Go != null && view.Payload.Dclns.TryGetValue(pl.Dcln, out dcln) && dcln != null)
                    {
                        var mesh = new Mesh { name = "DCLN " + pl.Dcln.ToString("X8") };
                        var verts = new Vector3[dcln.Vertices.Length / 3];
                        for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(dcln.Vertices[i * 3], dcln.Vertices[i * 3 + 1], dcln.Vertices[i * 3 + 2]);
                        mesh.vertices = verts;
                        mesh.triangles = dcln.Triangles;
                        var col = new GameObject("Platform collision");
                        col.transform.SetParent(e.Go.transform, false);
                        var mc = col.AddComponent<MeshCollider>();
                        mc.sharedMesh = mesh;
                        e.Solid = mc;
                    }
                    else e.Solid = ActorBox(view, "Platform box " + o.Name, e.Go, pl.Head, V(pl.Extent), V(pl.Centroid), 0);
                    e.SetActive(pl.Active);
                    return e;
                }

                case MpScriptType.Door:
                {
                    MpScriptRead.Door d = MpScriptRead.ReadDoor(o);
                    if (d == null) return null;
                    var e = new MpDoorEntity { GamePos = V(d.Head.Pos) };
                    MpCharacter dch = MpAssets.Valid(d.Model.Ancs) ? MpCharacter.Get(d.Model.Ancs, 0) : null;
                    if (dch != null && dch.Model != null)
                    {
                        // the door animates open and shut (its own ANCS animations)
                        dch.Upload(true);
                        var body = new GameObject("Door " + o.Name);
                        body.transform.SetParent(view.Root.transform, false);
                        body.transform.localPosition = V(d.Head.Pos);
                        body.transform.localRotation = MpSpace.GameRotation(V(d.Head.Rot));
                        body.transform.localScale = V(d.Head.Scale);
                        e.Anim = new MpAnimator(dch, "Door model", body.transform);
                        e.Go = body;
                        models++;
                    }
                    else e.Go = PlaceModel(view, d.Model, d.Head, "Door " + o.Name, ref models);
                    e.Solid = ActorBox(view, "Door box " + o.Name, e.Go, d.Head, V(d.CollisionExtent), V(d.Offset), 0);
                    e.ProjectilesCollide = d.ProjectilesCollide;
                    if (e.Solid != null) e.Solid.gameObject.AddComponent<MpDoorShotGate>().Door = e;
                    e.Active = d.Active;
                    e.IsOpen = d.Open;
                    e.SetActive(d.Active);
                    if (MpAssets.Valid(d.Params.ScanId)) AddScan(view, o, d.Params.ScanId, V(d.Head.Pos), null);
                    return e;
                }

                case MpScriptType.DamageableTrigger:
                {
                    MpScriptRead2.DamageableTrigger dt = MpScriptRead2.ReadDamageableTrigger(o);
                    if (dt == null) return null;
                    var e = new MpDamageableTriggerEntity { MaxHealth = dt.Health, HealthNow = dt.Health, Vuln = dt.Vuln };
                    Vector3 c = V(dt.Pos), s = V(dt.Volume);
                    e.Min = c - s * 0.5f;
                    e.Max = c + s * 0.5f;
                    e.Box = Box(view, "Damageable " + o.Name, c, Vector3.Max(s, new Vector3(0.1f, 0.1f, 0.1f)), MpAreaView.ShotOnlyLayer);
                    e.Box.gameObject.AddComponent<MpHitTarget>().Entity = e;
                    e.SetActive(dt.Active);
                    return e;
                }

                case MpScriptType.Trigger:
                {
                    MpScriptRead.Trigger t = MpScriptRead.ReadTrigger(o);
                    if (t == null) return null;
                    Vector3 c = V(t.Pos), s = V(t.Extent);
                    return new MpTriggerEntity { Min = c - s * 0.5f, Max = c + s * 0.5f, Flags = t.Flags, Active = t.Active, DeactivateOnEnter = t.DeactivateOnEntered, DeactivateOnExit = t.DeactivateOnExited, Damage = t.Damage };
                }

                case MpScriptType.Timer:
                {
                    var t = MpScriptRead2.ReadTimer(o);
                    return t == null ? null : new MpTimerEntity { Start = t.Start, MaxRandom = t.MaxRandom, Loop = t.Loop, AutoStart = t.AutoStart, Active = t.Active };
                }

                case MpScriptType.Counter:
                {
                    var c = MpScriptRead2.ReadCounter(o);
                    return c == null ? null : new MpCounterEntity { Initial = c.Initial, Max = c.Max, Current = c.Initial, AutoReset = c.AutoReset, Active = c.Active };
                }

                case MpScriptType.Relay:
                {
                    var r = MpScriptRead2.ReadRelay(o);
                    return r == null ? null : new MpRelayEntity { Active = r.Active };
                }

                case MpScriptType.MemoryRelay:
                {
                    var r = MpScriptRead2.ReadMemoryRelay(o);
                    return r == null ? null : new MpMemoryRelayEntity { Active = r.DefaultActive, SkipSendActive = r.SkipSendActive, IgnoreMessages = r.IgnoreMessages };
                }

                case MpScriptType.HUDMemo:
                {
                    var m = MpScriptRead2.ReadHudMemo(o);
                    return m == null ? null : new MpHudMemoEntity { Strg = m.Strg, DisplayTime = m.DisplayTime, Active = m.Active };
                }

                case MpScriptType.ActorRotate:
                {
                    var r = MpScriptRead2.ReadActorRotate(o);
                    return r == null ? null : new MpActorRotateEntity { Rotation = V(r.Rotation), MaxTime = r.Time, UpdateActors = r.UpdateActors, Active = r.Active };
                }

                case MpScriptType.PointOfInterest:
                {
                    var poi = MpScriptRead.ReadPointOfInterest(o);
                    if (poi == null) return null;
                    return new MpScanPointEntity { Scan = poi.ScanId, GamePos = V(poi.Head.Pos), Active = poi.Active };
                }

                case MpScriptType.SpawnPoint:
                {
                    var s = MpScriptRead.ReadSpawnPoint(o);
                    return s == null ? null : new MpSpawnEntity { GamePos = V(s.Pos), Yaw = s.Rot.Z, Default = s.Default, Active = s.Active };
                }

                case MpScriptType.StreamedAudio:
                {
                    var m = MpScriptRead2.ReadStreamedMusic(o);
                    return m == null ? null : new MpMusicEntity { File = m.File, FadeIn = m.FadeIn, FadeOut = m.FadeOut, Volume = m.Volume / 127f, Loop = m.Loop, Music = m.Music, Active = m.Active };
                }

                case MpScriptType.Sound:
                {
                    var s = MpScriptRead2.ReadSound(o);
                    return s == null ? null : new MpSoundEntity { SoundId = s.SoundId, GamePos = V(s.Head.Pos), Looped = s.Looped, NonEmitter = s.NonEmitter, AutoStart = s.AutoStart, Volume = s.Vol / 127f, MaxDist = s.MaxDist, Active = s.Active };
                }
            }

            return null;
        }

        private static void AddScan(MpAreaView view, MpScriptObject o, uint scan, Vector3 pos, Transform follow)
        {
            MpGame g = MpGame.Instance;
            if (g == null || g.Scripts == null) return;
            var e = new MpScanPointEntity { Id = o.Id | 0x80000000u, Type = -1, Area = view.Index, Name = o.Name + " (scan)", Scan = scan, GamePos = pos, Follow = follow, Active = true, View = view };
            g.Scripts.Add(e);
        }
    }
}
