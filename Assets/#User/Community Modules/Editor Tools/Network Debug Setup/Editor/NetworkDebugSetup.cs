# if UNITY_EDITOR
using Fusion;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NetworkDebugSetup
{
    private const string AssemblyName = "SomniumSpace.Network";

    static NetworkDebugSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("Community/Setup Debug Networking")]
    private static void ManualSetup()
    {
        Ensure();
    }

    private static void Ensure()
    {
        if (Application.isPlaying)
            return;

        //Debug.Log("[NetworkDebugSetup] Check Fusion Config...");
        var path = AssetDatabase.FindAssets("NetworkProjectConfig")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => p.EndsWith(".fusion", System.StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[NetworkDebugSetup] Fusion config error, NetworkProjectConfig.fusion not found.");
            return;
        }

        var json = File.ReadAllText(path);
        var config = NetworkProjectConfig.Deserialize(json);

        var list = config.AssembliesToWeave != null
            ? new List<string>(config.AssembliesToWeave)
            : new List<string>();

        if (list.Any(a => string.Equals(a, AssemblyName, System.StringComparison.OrdinalIgnoreCase)))
        {
            Debug.Log($"[NetworkDebugSetup] Fusion config is valid.");
            return;
        }

        list.Add(AssemblyName);
        config.AssembliesToWeave = list.ToArray();

        File.WriteAllText(path, NetworkProjectConfig.Serialize(config));
        AssetDatabase.ImportAsset(path);
        NetworkProjectConfig.UnloadGlobal(); // force reload on next access
        Debug.Log($"[NetworkDebugSetup] Added '{AssemblyName}' to AssembliesToWeave.");

        // Run weaver
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Run Weaver");
    }
}
#endif