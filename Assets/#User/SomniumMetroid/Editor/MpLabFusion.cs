using System.Collections;
using Fusion;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "fusionsolo": the world's networking over a REAL Fusion runner set up the way the Somnium client does it live -
    /// the Community Modules' SceneNetworking removed (an uploaded world does not compile them), a runner started with no scene
    /// (GameMode.Single: the project has no Photon app id of its own, so no cloud session). Our MpNet must then register this
    /// world's scene NetworkObjects itself, claim a player slot and, alone, run the game. (The socket co-op lab never touched Fusion.)
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator FusionSoloScenario()
        {
            // as live: no Community Modules networking in the world
            int removed = 0;
            foreach (MonoBehaviour b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == "SceneNetworking") { Object.DestroyImmediate(b); removed++; }
            Log("Community Modules SceneNetworking removed: " + removed + "; runners before: " + NetworkRunner.Instances.Count);
            // as live: the world's own copy (MpGame adds it at start when no other SceneNetworking runs)
            if (SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking.Instance == null) G.gameObject.AddComponent<SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking>();
            Log("our SceneNetworking present: " + (SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking.Instance != null));

            var go = new GameObject("Lab Network Runner");
            var runner = go.AddComponent<NetworkRunner>();
            var start = runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, SessionName = "MpLabSolo" });
            float t0 = Time.realtimeSinceStartup;
            while (!start.IsCompleted && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Log("runner started: " + (start.IsCompleted ? start.Result.Ok + " " + start.Result.ShutdownReason : "timed out") + ", running " + runner.IsRunning + ", local player " + runner.LocalPlayer + " real " + runner.LocalPlayer.IsRealPlayer);

            float w0 = Time.realtimeSinceStartup;
            int valid = 0, total = 0;
            MpNet net = G.Net as MpNet;
            while (Time.realtimeSinceStartup - w0 < 20f)
            {
                yield return Seconds(1f);
                valid = 0; total = 0;
                foreach (NetworkObject o in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (o.gameObject.scene != G.gameObject.scene && o.gameObject.name.IndexOf("Mp Net", System.StringComparison.Ordinal) < 0) continue;
                    total++;
                    if (o.IsValid) valid++;
                }

                if (net != null && net.Online && net.MySlot >= 0 && valid == total && total > 0) break;
            }

            Log("CHECK fusion-registered " + (valid == total && total > 0 ? "ok" : "FAILED") + " (" + valid + " of " + total + " world network objects valid)");
            Log("CHECK fusion-ready " + (SomniumSpace.Worlds.SomniumMetroid.Net.SceneNetworking.IsNetworkReady ? "ok" : "FAILED") + " (our SceneNetworking: registered on our join)");
            Log("CHECK fusion-online " + (net != null && net.Online ? "ok" : "FAILED"));
            Log("CHECK fusion-slot " + (net != null && net.MySlot >= 0 ? "ok (slot " + net.MySlot + ")" : "FAILED"));
            Log("fusion: master " + (net != null && net.IsMaster) + ", world record valid " + (net != null && net.WorldValid) + ", remotes " + (net != null ? net.Remotes.Count : -1));
            runner.Shutdown();
            yield return Seconds(1f);
        }
    }
}
