using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// A play-mode test bench run from the command line (batch mode with a graphics device, the BO2 Dreamport's Bo2Lab way):
    ///   Unity -batchmode -projectPath ... -mplab view [-mparea 0] [-mpdisc path] -mpout D:\dir -logFile lab.log
    /// It opens the scene, boots the real game code (MpGame), puts its own camera where the player would stand and writes pictures and
    /// "LAB:" log lines, then exits. Scenarios: view (the start area from the spawn, four directions).
    /// </summary>
    [InitializeOnLoad]
    public static class MpLabBoot
    {
        static MpLabBoot()
        {
            if (Arg("-mplab", null) == null) return;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EditorSceneManager.OpenScene("Assets/Metroid Prime Scene.unity");
                EditorApplication.EnterPlaymode();
            };
        }

        public static string Arg(string name, string fallback)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        private static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            var go = new GameObject("Metroid Lab");
            go.AddComponent<MpLabRunner>();
        }
    }

    public sealed partial class MpLabRunner : MonoBehaviour
    {
        private Camera _cam;
        private string _out;
        private bool _failed;
        private MpGame G;

        private void Awake()
        {
            string area = MpLabBoot.Arg("-mparea", null);
            if (area != null) MpGame.StartAreaOverride = int.Parse(area);
            MpGame.DiscOverride = MpLabBoot.Arg("-mpdisc", @"D:\Dreamport\Metroid Prime\ISO\Metroid Prime (Europe) (En,Fr,De,Es,It).ciso");
            CoopLinkUp();
        }

        private void Start()
        {
            Application.runInBackground = true;
            _out = MpLabBoot.Arg("-mpout", @"D:\Dreamport\Metroid Prime\METROIDPRIME\DevTools\lab");
            Directory.CreateDirectory(_out);
            StartCoroutine(Guarded(Main()));
        }

        public static void Log(string s) { Debug.Log("LAB: " + s); }

        /// <summary>Runs the scenario, stepping nested IEnumerators itself so an exception anywhere ends the lab (Unity would just stop the inner coroutine and hang).</summary>
        private IEnumerator Guarded(IEnumerator body)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                object cur;
                try
                {
                    IEnumerator top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    cur = top.Current;
                }
                catch (Exception e)
                {
                    Log("EXCEPTION " + e);
                    _failed = true;
                    break;
                }

                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return cur;
            }

            Log("done");
            Quit(_failed ? 1 : 0);
        }

        private IEnumerator Wait(Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Log("TIMEOUT waiting for " + what + " (status: " + (G != null ? G.Status : "no game") + ")"); _failed = true; yield break; }
                yield return null;
            }
        }

        private IEnumerator Main()
        {
            string scenario = MpLabBoot.Arg("-mplab", "view");
            yield return Wait(() => (G = MpGame.Instance) != null && G.Running, 240f, "the boot");
            if (_failed) yield break;
            Log("game running: area " + G.WorldView.CurrentArea + " '" + G.World.Areas[G.WorldView.CurrentArea].Name + "'");
            MakeCamera();
            if (scenario == "view") yield return ViewScenario();
            else yield return ExtraScenario(scenario);
        }

        partial void ExtraScenarioImpl(string scenario, ref IEnumerator run);

        private IEnumerator ExtraScenario(string scenario)
        {
            IEnumerator run = null;
            ExtraScenarioImpl(scenario, ref run);
            if (run == null) { Log("unknown scenario " + scenario); _failed = true; yield break; }
            yield return run;
        }

        private void MakeCamera()
        {
            var go = new GameObject("Lab Camera");
            _cam = go.AddComponent<Camera>();
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 2000f;
            _cam.fieldOfView = 70f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.enabled = false;
        }

        /// <summary>Puts the camera at a game-space eye position looking along a game yaw (degrees about Z, 0 = +Y) and pitch.</summary>
        private void Aim(Vector3 gameEye, float gameYaw, float pitch)
        {
            _cam.transform.position = MpSpace.ToUnity(gameEye);
            _cam.transform.rotation = Quaternion.Euler(pitch, -gameYaw, 0f);
        }

        private IEnumerator ViewScenario()
        {
            Vector3 spawn = G.PlayerGame;
            MpGame.LabFocus = spawn;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
            Vector3 eye = spawn + new Vector3(0, 0, MpSpace.SamusEyeHeight);
            Log("loaded areas: " + string.Join(", ", new List<int>(G.WorldView.Areas.Keys)) + "; textures " + MpTextures.Uploaded + " (" + (MpTextures.UploadedBytes >> 20) + " MB), materials " + MpMaterials.Created);
            for (int d = 0; d < 4; d++)
            {
                Aim(eye, d * 90f, 5f);
                MpShaderGlobals.Apply(_cam);
                Shoot(_cam, "view_" + d + ".png", 1280, 720);
                yield return null;
            }
        }

        /// <summary>Lab: pictures without the particle systems (to tell a particle from a model).</summary>
        public static bool NoFx;

        public void Shoot(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            if (MpGame.Instance != null && MpGame.Instance.Running && !NoFx) MpEffects.Render(cam);   // the effects are queued late in the frame: queue them for this picture
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(_out, file), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            DestroyImmediate(rt);
            DestroyImmediate(tex);
            Log("wrote " + file);
        }

        private void Quit(int code)
        {
            Debug.Log("LAB: exit " + code);
            EditorApplication.Exit(code);
        }
    }
}
