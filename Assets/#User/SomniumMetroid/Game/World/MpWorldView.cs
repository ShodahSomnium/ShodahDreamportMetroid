using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The loaded world (MLVL): the area the player stands in plus the areas attached to it (MLVL attached-area lists - the rooms
    /// behind its doors) are kept loaded; areas further away are dropped. The area the player is in = the one whose box (MLVL bounds)
    /// holds the player's game position (smallest box first: rooms nest inside big halls' boxes). The skybox (MLVL skybox CMDL) is
    /// drawn around the camera when the area asks for it, as CWorld::DrawSky does.
    /// </summary>
    public sealed class MpWorldView
    {
        public readonly MpAssets Assets;
        public readonly MpWorld World;
        public readonly MpLoader Loader;
        public readonly Transform Root;
        public readonly Dictionary<int, MpAreaView> Areas = new Dictionary<int, MpAreaView>();
        private readonly HashSet<int> _loading = new HashSet<int>();
        private readonly MonoBehaviour _host;
        public int CurrentArea = -1;

        /// <summary>Co-op, the master: the rooms the other players stand in - kept loaded with their neighbours, so the master's script
        /// world (and its enemies) runs where every player is (MpCoop fills it each frame).</summary>
        public readonly HashSet<int> KeepAlso = new HashSet<int>();
        public int Visor;
        public GameObject Sky;
        public event Action<MpAreaView> AreaReady;

        public MpWorldView(MonoBehaviour host, MpAssets assets, MpWorld world, MpLoader loader, Transform root)
        {
            _host = host;
            Assets = assets;
            World = world;
            Loader = loader;
            Root = root;
        }

        public bool IsLoaded(int area)
        {
            MpAreaView v;
            return Areas.TryGetValue(area, out v) && v.Ready;
        }

        public bool IsBusy { get { return _loading.Count > 0; } }

        public void Request(int area)
        {
            if (area < 0 || area >= World.Areas.Count || Areas.ContainsKey(area) || _loading.Contains(area)) return;
            _loading.Add(area);
            MpWorld.Area info = World.Areas[area];
            Loader.Add(() => MpAreaView.Prepare(Assets, info), (result, error) =>
            {
                _loading.Remove(area);
                if (error != null)
                {
                    MpLog.Error("World: area " + area + " '" + info.Name + "' could not be read: " + error);
                    return;
                }

                var p = (MpAreaPayload)result;
                var view = new MpAreaView();
                Areas[area] = view;
                _host.StartCoroutine(BuildThenReport(view, p));
            });
        }

        private System.Collections.IEnumerator BuildThenReport(MpAreaView view, MpAreaPayload p)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            yield return view.Build(p, Root, 4f);
            if (view.Root == null) yield break;      // dropped while it was being built
            view.ApplyVisor(Visor);
            MpGame g = MpGame.Instance;
            if (g != null && g.Scripts != null) MpEntityFactory.Build(view, g.Scripts);
            MpLog.Info("World: area " + p.Info.Index + " '" + p.Info.Name + "' ready: " + p.Area.ModelCount + " models, " + p.Collision.TriangleCount + " collision triangles, " + p.Objects.Count + " script objects, " + p.Textures.Count + " new textures, " + p.Models.Count + " actor models; parsed in " + p.ParseMs + " ms, built over " + sw.ElapsedMilliseconds + " ms");
            AreaReady?.Invoke(view);
        }

        private readonly HashSet<int> _stale = new HashSet<int>();

        /// <summary>A script switched a layer of this area: if it is built, it is rebuilt (now, or once the player is not in it).</summary>
        public void LayersChanged(int area)
        {
            if (Areas.ContainsKey(area) || _loading.Contains(area)) _stale.Add(area);
        }

        private void Unload(int a, string why)
        {
            MpLog.Info("World: area " + a + " '" + World.Areas[a].Name + "' unloaded" + why);
            MpGame gm = MpGame.Instance;
            if (gm != null && gm.Scripts != null) gm.Scripts.RemoveArea(a);
            Areas[a].Destroy();
            Areas.Remove(a);
        }

        /// <summary>Every frame: which area the player is in, what to load and drop.</summary>
        public void Tick(Vector3 playerGame)
        {
            if (_stale.Count > 0)
            {
                List<int> redo = null;
                foreach (int a in _stale)
                    if (a != CurrentArea && !_loading.Contains(a) && Areas.ContainsKey(a) && Areas[a].Ready) { if (redo == null) redo = new List<int>(); redo.Add(a); }
                    else if (!Areas.ContainsKey(a) && !_loading.Contains(a)) { if (redo == null) redo = new List<int>(); redo.Add(-1 - a); }
                if (redo != null)
                    foreach (int a in redo)
                    {
                        if (a < 0) { _stale.Remove(-1 - a); continue; }
                        _stale.Remove(a);
                        Unload(a, " (its script layers changed: rebuilt)");
                    }
            }

            int inside = AreaAt(playerGame);
            if (inside >= 0 && inside != CurrentArea)
            {
                if (CurrentArea >= 0) MpLog.Info("World: entered area " + inside + " '" + World.Areas[inside].Name + "'");
                CurrentArea = inside;
            }

            if (CurrentArea < 0) return;
            MpWorld.Area cur = World.Areas[CurrentArea];
            Request(CurrentArea);
            foreach (int a in cur.Attached) Request(a);
            foreach (int k in KeepAlso)
            {
                if (k < 0 || k >= World.Areas.Count) continue;
                Request(k);
                foreach (int a in World.Areas[k].Attached) Request(a);
            }

            // drop areas that are neither current nor attached (keep one ring more while doors are being walked through)
            List<int> drop = null;
            foreach (var kv in Areas)
            {
                if (kv.Key == CurrentArea || Array.IndexOf(cur.Attached, kv.Key) >= 0 || Kept(kv.Key)) continue;
                bool nearRing = false;
                foreach (int a in cur.Attached)
                    if (a >= 0 && a < World.Areas.Count && Array.IndexOf(World.Areas[a].Attached, kv.Key) >= 0) nearRing = true;
                if (nearRing) continue;
                if (drop == null) drop = new List<int>();
                drop.Add(kv.Key);
            }

            if (drop != null)
                foreach (int a in drop) Unload(a, "");
        }

        private bool Kept(int area)
        {
            foreach (int k in KeepAlso)
                if (k == area || (k >= 0 && k < World.Areas.Count && Array.IndexOf(World.Areas[k].Attached, area) >= 0)) return true;
            return false;
        }

        /// <summary>The area whose box holds the point (the smallest one when boxes overlap), or -1.</summary>
        public int AreaAt(Vector3 p)
        {
            int best = -1;
            float bestVol = float.MaxValue;
            foreach (MpWorld.Area a in World.Areas)
            {
                float[] b = a.Bounds;
                if (p.x < b[0] || p.y < b[1] || p.z < b[2] || p.x > b[3] || p.y > b[4] || p.z > b[5]) continue;
                float vol = (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]);
                if (vol < bestVol)
                {
                    bestVol = vol;
                    best = a.Index;
                }
            }

            return best;
        }

        public void SetVisor(int visor)
        {
            Visor = visor;
            foreach (var kv in Areas) if (kv.Value.Ready) kv.Value.ApplyVisor(visor);
        }

        // ------------------------------------------------------------------ the sky

        private readonly Dictionary<uint, GameObject> _skies = new Dictionary<uint, GameObject>();
        private List<KeyValuePair<uint, MpTexture>> _skyTextures;
        private readonly Dictionary<uint, float> _skyRadius = new Dictionary<uint, float>();

        /// <summary>Loader thread: the world's sky model (MLVL skybox) and its textures.</summary>
        public void PrepareSky()
        {
            if (!MpAssets.Valid(World.Skybox)) return;
            MpModel m = MpModels.Load(Assets, World.Skybox);
            if (m == null) return;
            var ids = new HashSet<uint>();
            MpMaterials.TexturesOf(m.Set, ids);
            var list = new List<KeyValuePair<uint, MpTexture>>();
            foreach (uint id in ids)
            {
                if (MpTextures.Has(id)) continue;
                try { list.Add(new KeyValuePair<uint, MpTexture>(id, MpTexture.Read(Assets.Read(id)))); }
                catch (Exception) { }
            }

            _skyTextures = list;
        }

        /// <summary>
        /// Before the camera draws: when the current area wants the sky (AreaAttributes), its sky (the area's override, else the
        /// world's) sits on the camera - CWorld::DrawSky: translated to the view point, no lights, white ambient, behind everything
        /// (depth range 0.999..1). Here: drawn just after the opaque world (queue 2501: URP's depth priming forces opaque draws to depth-equal,
        /// which would throw a sky drawn first away), out at 0.8 x the far plane with a normal depth test, writing no depth, outside the prepass.
        /// </summary>
        public void TickSky(Camera cam)
        {
            MpAreaView here = null;
            bool show = cam != null && Areas.TryGetValue(CurrentArea, out here) && here.Payload != null && here.Payload.NeedsSky;
            uint id = show ? (MpAssets.Valid(here.Payload.SkyOverride) ? here.Payload.SkyOverride : World.Skybox) : 0xFFFFFFFF;
            foreach (var kv in _skies) if (kv.Value != null && kv.Key != id && kv.Value.activeSelf) kv.Value.SetActive(false);
            if (!show || !MpAssets.Valid(id)) return;
            GameObject sky = SkyObject(id, cam);
            if (sky == null) return;
            if (!sky.activeSelf) sky.SetActive(true);
            sky.transform.position = cam.transform.position;
            // out near the far plane, so everything of the world is nearer and wins the depth test (its size is invisible: it is centred on the eye)
            float r;
            if (_skyRadius.TryGetValue(id, out r) && r > 0f) sky.transform.localScale = Vector3.one * (0.8f * cam.farClipPlane / r);
        }

        private GameObject SkyObject(uint id, Camera cam)
        {
            GameObject go;
            if (_skies.TryGetValue(id, out go)) return go;
            MpModel m = MpModels.Get(id);
            if (m == null) return null;
            if (id == World.Skybox && _skyTextures != null)
            {
                foreach (var kv in _skyTextures) MpTextures.Upload(kv.Key, kv.Value);
                _skyTextures = null;
            }

            m.Build(true);
            go = m.Instantiate("Sky " + id.ToString("X8"), Root);
            float radius = Mathf.Max(m.Min.magnitude, m.Max.magnitude) * MpSpace.Scale;
            _skyRadius[id] = radius;
            int q = 0;
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                Material[] src = r.sharedMaterials;
                var mats = new Material[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    if (src[k] == null) continue;
                    var mat = new Material(src[k]) { name = src[k].name + " (sky)" };
                    mat.SetFloat("_Lit", 2f);
                    mat.SetColor("_Ambient", Color.white);
                    mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetShaderPassEnabled("DepthOnly", false);
                    mat.SetShaderPassEnabled("DepthNormals", false);
                    mat.SetShaderPassEnabled("ShadowCaster", false);
                    mat.renderQueue = 2501 + Mathf.Min(40, q++);
                    mats[k] = mat;
                }

                r.sharedMaterials = mats;
            }

            _skies[id] = go;
            MpLog.Info("World: sky " + id.ToString("X8") + " built (model radius " + radius.ToString("0") + " m, drawn out at 0.8 x the far plane, after the opaque world)");
            return go;
        }

        public void Destroy()
        {
            foreach (var kv in Areas) kv.Value.Destroy();
            Areas.Clear();
            if (Sky != null) UnityEngine.Object.Destroy(Sky);
            foreach (var kv in _skies) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _skies.Clear();
        }
    }
}
