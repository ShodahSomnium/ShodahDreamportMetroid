using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Data.Particles;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// An ELSC electric effect (CParticleElectric, ported in Data/Particles/MpElectricGen) placed by its owner (the wave beam's
    /// charge arcs, Wave2nd_1 / Wave2nd_2; CWaveBeam::UpdateGunFx sets its global translation / orientation every frame and its
    /// global scale once). It lives in MpEffects' list: stepped at 60 Hz by MpEffects.Tick, drawn by MpEffects.Render (the arcs as
    /// additive line layers, the GPSM / EPSM / SSWH children with the PART / SWHC renderers) and dropped by MpEffects.Clear.
    /// The children's textures / models are loaded on the loader thread the first time an ELSC is spawned (the gun's preparation
    /// does not list ELSCs); until then only the lines are drawn.
    /// </summary>
    public sealed class MpElectricFx
    {
        /// <summary>Seconds after which it stops emitting (the wave beam's cancel arcs run 3 s); &lt;= 0 = until killed.</summary>
        public float StopAfter;
        public bool Hidden { get { return _effect.Hidden; } set { _effect.Hidden = value; } }
        public bool Dead { get { return _effect.Dead; } }

        private readonly MpEffects.Effect _effect;
        private readonly MpElectricGen _gen;
        private readonly float _born;
        private bool _stopArmed;

        private MpElectricFx(MpEffects.Effect effect, MpElectricGen gen)
        {
            _effect = effect;
            _gen = gen;
            _born = Time.time;
        }

        /// <summary>A new effect from an ELSC asset id, or null when the id is not a readable ELSC.</summary>
        public static MpElectricFx Spawn(uint elsc)
        {
            MpEffects.Effect e = MpEffects.SpawnElectric(elsc);
            var gen = e != null ? e.Gen as MpElectricGen : null;
            if (gen == null) return null;
            var fx = new MpElectricFx(e, gen);
            gen.DrawChildren = ChildrenLoaded(elsc, gen.Desc);
            return fx;
        }

        /// <summary>Every frame from the owner: where it is (game space), its orientation and scale (CWaveBeam::UpdateGunFx).</summary>
        public void Place(Vector3 gamePos, M3 orientation, float scale)
        {
            if (_effect.Dead) return;
            _effect.SetGlobal(gamePos, orientation, scale);
            if (!_gen.DrawChildren) _gen.DrawChildren = ChildrenLoaded(_gen.Desc.Id, _gen.Desc);
            if (StopAfter > 0f && !_stopArmed)
            {
                // MpEffects.Tick ends emission at FinishAt (Effect.Finish: no longer persistent, so it goes once the arcs are gone)
                _stopArmed = true;
                _effect.FinishAt = Mathf.Max(_born + StopAfter, 0.0001f);
            }
        }

        public void Kill() { _effect.Kill(); }

        // ------------------------------------------------------------------ the children's resources

        private static MpPartLibrary _resLib;
        private static readonly Dictionary<uint, bool> ResReady = new Dictionary<uint, bool>();   // ELSC -> loaded (false = loading)

        private sealed class Loaded
        {
            public readonly List<KeyValuePair<uint, MpTexture>> Textures = new List<KeyValuePair<uint, MpTexture>>();
            public readonly List<MpModel> Models = new List<MpModel>();
        }

        /// <summary>Main thread: true once the ELSC's children can be drawn; the first call starts loading their textures and models.</summary>
        private static bool ChildrenLoaded(uint elsc, MpElectricDesc desc)
        {
            MpPartLibrary lib = MpEffects.Lib;
            if (lib == null) return false;
            if (!ReferenceEquals(lib, _resLib)) { ResReady.Clear(); _resLib = lib; }
            bool ready;
            if (ResReady.TryGetValue(elsc, out ready)) return ready;
            if (!MpAssets.Valid(desc.SSWH) && !MpAssets.Valid(desc.GPSM) && !MpAssets.Valid(desc.EPSM)) { ResReady[elsc] = true; return true; }
            ResReady[elsc] = false;
            MpLoader loader = MpGame.Instance != null ? MpGame.Instance.Loader : null;
            if (loader == null)
            {
                Upload(Load(lib, desc));
                ResReady[elsc] = true;
                return true;
            }

            loader.Add(() => Load(lib, desc), (r, e) =>
            {
                if (e != null) MpLog.Warn("Effects: ELSC " + elsc.ToString("X8") + " children not loaded: " + e.Message);
                if (!ReferenceEquals(lib, _resLib)) return;
                Upload(r as Loaded);
                ResReady[elsc] = true;
            });
            return false;
        }

        /// <summary>Loader thread: the children's descriptions, textures decoded and models read (as MpEffects.Prepare does for PARTs).</summary>
        private static Loaded Load(MpPartLibrary lib, MpElectricDesc desc)
        {
            var res = new Loaded();
            var tex = new HashSet<uint>();
            var models = new HashSet<uint>();
            MpElectricGen.ResourcesOf(desc, lib, tex, models);
            MpAssets assets = lib.Assets;
            foreach (uint m in models)
            {
                MpModel model = MpModels.Load(assets, m);
                if (model == null) continue;
                res.Models.Add(model);
                MpMaterials.TexturesOf(model.Set, tex);
            }

            foreach (uint t in tex)
            {
                try
                {
                    byte[] b = assets.Read(t);
                    if (b != null) res.Textures.Add(new KeyValuePair<uint, MpTexture>(t, MpTexture.Read(b)));
                }
                catch (Exception e) { MpLog.Warn("Effects: TXTR " + t.ToString("X8") + " not decoded: " + e.Message); }
            }

            return res;
        }

        private static void Upload(Loaded r)
        {
            if (r == null) return;
            foreach (var kv in r.Textures) MpTextures.Upload(kv.Key, kv.Value);
            foreach (MpModel m in r.Models) if (!m.Built) m.Build(true);
        }
    }
}
