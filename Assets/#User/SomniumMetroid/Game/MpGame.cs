using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The Metroid Prime Dreamport: the scene's root component. Order: the whitelist verdict (nothing before it), the player's own disc
    /// image (MpDiscFinder), its archives (MpAssets), the Space Pirate Frigate (MLVL), the start area and its spawn point, then the
    /// rooms stream around the player (MpWorldView) and the gameplay runs (MpPlay). Nothing of the game is shipped: every model,
    /// texture, material, sound and number comes from the disc at run time.
    /// </summary>
    public sealed partial class MpGame : MonoBehaviour
    {
        [Tooltip("Carrier for the TEV shader (Somnium worlds only pack shaders a scene material references).")]
        [SerializeField] private Material _tevMaterial;
        [Tooltip("Carrier for the effects shader (particles, beams, HUD).")]
        [SerializeField] private Material _fxMaterial;

        public static MpGame Instance { get; private set; }
        public MpAssets Assets { get; private set; }
        public MpWorld World { get; private set; }
        public MpWorldView WorldView { get; private set; }
        public MpLoader Loader { get; private set; }
        public Transform WorldRoot { get; private set; }
        public bool Running { get; private set; }
        public string Status { get; private set; }

        /// <summary>The world to start in and its pak (the game's new-game start: the Space Pirate Frigate).</summary>
        public const string StartPak = "Metroid1.pak";

        /// <summary>Lab / editor: start in this area instead of the frigate's first (-1 = the game's start).</summary>
        public static int StartAreaOverride = -1;

        /// <summary>Lab: a disc image path to use instead of searching.</summary>
        public static string DiscOverride;

        public static Material FxMaterial { get { return Instance != null ? Instance._fxMaterial : null; } }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(Boot());
        }

        private IEnumerator Boot()
        {
            yield return MpAccess.Check();
            if (!MpAccess.Allowed) yield break;      // a visitor: nothing at all
            MpLog.Sink = Debug.Log;
            MpLog.WarnSink = Debug.LogWarning;
            MpLog.ErrorSink = Debug.LogError;
            MpLog.Info("Metroid Prime Dreamport " + MpVersion.Label + " - " + MpVersion.Summary);
            MpMaterials.Carrier = _tevMaterial;
            if (_tevMaterial == null) MpLog.Error("Boot: no TEV material carrier in the scene: nothing can be drawn");
            MpDiscFinder.SettingsFolder = Path.Combine(Application.persistentDataPath, "MetroidDreamport");
            MpSettings.Load();
            Loader = new MpLoader();

            // 1. the disc
            Status = "Looking for your Metroid Prime disc image";
            MpDiscFinder.Result found = null;
            bool done = false;
            Loader.Add(() =>
            {
                if (!string.IsNullOrEmpty(DiscOverride)) return new MpDiscFinder.Result { Path = DiscOverride, GameId = MpDisc.Identify(DiscOverride), Report = "  lab override\n" };
                return MpDiscFinder.Find();
            }, (r, e) => { found = r as MpDiscFinder.Result; done = true; if (e != null) MpLog.Error("Disc: search failed: " + e); });
            while (!done) { Loader.Drain(4); yield return null; }
            if (found == null || found.Path == null)
            {
                Status = "No Metroid Prime (GameCube) disc image found";
                MpLog.Error("Disc: no Metroid Prime GameCube image found. Put your .iso / .gcm / .ciso where Dolphin finds it, or write disc=<full path> into " + MpDiscFinder.SettingsFile + ". What was tried:\n" + (found != null ? found.Report : ""));
                yield break;
            }

            MpLog.Info("Disc: " + found.Path + " (" + found.GameId + ")\n" + found.Report);

            // 2. the archives and the world
            Status = "Reading the disc";
            done = false;
            Exception error = null;
            string firstPak = string.IsNullOrEmpty(StartPakOverride) ? StartPak : StartPakOverride;
            CurrentPak = firstPak;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Loader.Add(() =>
            {
                var assets = new MpAssets(found.Path);
                return new object[] { assets, ReadWorld(assets, firstPak) };
            }, (r, e) =>
            {
                error = e;
                if (r != null)
                {
                    var arr = (object[])r;
                    Assets = (MpAssets)arr[0];
                    World = (MpWorld)arr[1];
                }

                done = true;
            });
            while (!done) { Loader.Drain(4); yield return null; }
            if (error != null || World == null)
            {
                Status = "The disc could not be read";
                MpLog.Error("Disc: could not read the archives: " + error);
                yield break;
            }

            MpLog.Info("Disc: " + Assets.Count + " resources in " + Assets.Paks.Count + " archives (" + Assets.Disc.GameId + ", " + (Assets.Disc.IsPal ? "PAL" : "NTSC") + "), world '" + firstPak + "' with " + World.Areas.Count + " areas, read in " + sw.ElapsedMilliseconds + " ms");

            // 3. the world view and the start area; the gun, the HUD and the sound banks behind it on the loader
            PrepareScripts();
            WorldRoot = MpSpace.MakeRoot(transform);
            WorldView = new MpWorldView(this, Assets, World, Loader, WorldRoot);
            int startArea = StartAreaOverride >= 0 ? StartAreaOverride : 0;
            Status = "Loading " + World.Areas[startArea].Name;
            WorldView.CurrentArea = startArea;
            WorldView.Request(startArea);
            MpHudPayload hudPayload = null;
            MpSoundBank sounds = null;
            int parts = 0;
            MpAssets assetsForJobs = Assets;
            Loader.Add(() => MpGunAssets.Prepare(assetsForJobs), (r, e) => { GunAssets = r as MpGunAssets; parts++; if (e != null) MpLog.Error("Gun: not prepared: " + e); });
            Loader.Add(() => MpHudPayload.Prepare(assetsForJobs), (r, e) => { hudPayload = r as MpHudPayload; parts++; if (e != null) MpLog.Error("HUD: not prepared: " + e); });
            Loader.Add(() => MpSoundBank.Load(assetsForJobs), (r, e) => { sounds = r as MpSoundBank; parts++; if (e != null) MpLog.Error("Sound: banks not read: " + e); });
            MpReticle.Payload reticle = null;
            Loader.Add(() => MpReticle.Prepare(assetsForJobs), (r, e) => { reticle = r as MpReticle.Payload; parts++; if (e != null) MpLog.Warn("Reticle: not prepared: " + e.Message); });
            MpWorldView view = WorldView;
            Loader.Add(() => { view.PrepareSky(); return null; }, (r, e) => { parts++; if (e != null) MpLog.Warn("World: sky not read: " + e.Message); });
            while (!WorldView.IsLoaded(startArea) || parts < 5) { Loader.Drain(4); yield return null; }
            if (reticle != null) Reticle = new MpReticle(reticle, transform);

            if (hudPayload != null)
            {
                Hud = new MpHud(hudPayload, State);
                _scanSpeeds = hudPayload.ScanSpeeds;
                MpShaderGlobals.SetPalettes(hudPayload.ThermalPalette, hudPayload.ThermalCount, hudPayload.XRayPalette, hudPayload.XRayCount);
            }

            if (sounds != null)
            {
                Audio = new MpAudio(sounds, transform);
                Audio.UseDisc(Assets, Loader);
                MpLog.Info("Sound: " + sounds.Report);
            }

            MpAreaView start = WorldView.Areas[startArea];
            Vector3 spawnGame;
            float spawnYaw;
            int[] items;
            PickSpawn(start, out spawnGame, out spawnYaw, out items);
            MpLog.Info("Start: area " + startArea + " '" + start.Info.Name + "', spawn at game " + spawnGame + " facing " + spawnYaw.ToString("0") + " deg");
            Running = true;
            Status = null;
            OnWorldStarted(spawnGame, spawnYaw, items);
        }

        /// <summary>The area's default active spawn point on an active layer (CScriptSpawnPoint), else the first one, else the area centre.</summary>
        private static void PickSpawn(MpAreaView area, out Vector3 pos, out float yaw, out int[] items)
        {
            MpScriptRead.SpawnPoint best = null;
            foreach (MpScriptObject o in area.Payload.Objects)
            {
                if (o.Type != MpScriptType.SpawnPoint || !MpAreaView.LayerActive(area.Info, o.Layer)) continue;
                MpScriptRead.SpawnPoint s = MpScriptRead.ReadSpawnPoint(o);
                if (s == null) continue;
                if (best == null || (s.Default && s.Active && !(best.Default && best.Active))) best = s;
            }

            items = best != null ? best.Items : null;

            if (best != null)
            {
                pos = new Vector3(best.Pos.X, best.Pos.Y, best.Pos.Z);
                yaw = best.Rot.Z;
                return;
            }

            float[] b = area.Info.Bounds;
            pos = new Vector3((b[0] + b[3]) * 0.5f, (b[1] + b[4]) * 0.5f, b[2] + 1f);
            yaw = 0f;
        }

        private void Update()
        {
            if (Loader != null) Loader.Drain(8);
            if (!Running) return;
            TickPlay();
        }

        private void OnDestroy()
        {
            if (Loader != null) Loader.Dispose();
            if (WorldView != null) WorldView.Destroy();
            if (Assets != null) Assets.Dispose();
            OnStopped();
            if (Instance == this) Instance = null;
        }
    }
}
