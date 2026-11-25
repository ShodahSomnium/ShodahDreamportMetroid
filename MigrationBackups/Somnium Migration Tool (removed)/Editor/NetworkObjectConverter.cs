using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Step 6: replace the old NetworkRigidbody3D components (missing scripts after the Photon update) by NetworkTransform
    ///     with the recommended settings (Forecast off; no State Authority interpolation in Shared Mode for Kinematic grabs).
    ///     A missing script is invisible to Unity's API, so the swap is done in the scene/prefab file: only the component's
    ///     m_Script line changes. The component keeps its file ID, so every reference to it (NetworkGrabbable,
    ///     NetworkObject's behaviour list, creator scripts) still points at it. The settings are then applied through the API.
    /// </summary>
    internal static class NetworkObjectConverter
    {
        static readonly Regex BlockHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)( stripped)?");
        static readonly Regex ScriptLine = new Regex(@"^(\s*m_Script: \{fileID: )11500000, guid: " + MigrationConstants.OldNetworkRigidbody3DGuid + @"(, type: 3\})");
        static readonly Regex GameObjectRef = new Regex(@"^\s*m_GameObject: \{fileID: (-?\d+)\}");
        static readonly Regex SourcePrefab = new Regex(@"^\s*m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})");
        static readonly Regex NameLine = new Regex(@"^\s*m_Name: (.*)$");

        internal class Found
        {
            public string AssetPath;       // File that declares the component (scene or prefab)
            public string FileId;          // Component file ID in that file
            public string GameObjectName;
            public string ViaInstance;     // For prefab-sourced scene objects: the instance name in the scene
        }

        // ------------------------------------------------------------------ scanning

        /// <summary>Old components declared (not just referenced) in one scene or prefab file.</summary>
        public static List<Found> ScanFile(string assetPath)
        {
            var result = new List<Found>();
            string abs = MigrationIO.ToAbsolute(assetPath);
            if (!File.Exists(abs))
                return result;
            string text = File.ReadAllText(abs);
            if (!text.Contains(MigrationConstants.OldNetworkRigidbody3DGuid))
                return result;

            var goNames = new Dictionary<string, string>();
            string blockType = null, blockId = null, goId = null;
            bool stripped = false, isOld = false;
            foreach (string line in text.Split('\n'))
            {
                var h = BlockHeader.Match(line);
                if (h.Success)
                {
                    Flush();
                    blockType = h.Groups[1].Value;
                    blockId = h.Groups[2].Value;
                    stripped = h.Groups[3].Success;
                    goId = null;
                    isOld = false;
                    continue;
                }
                if (blockType == "1")
                {
                    var n = NameLine.Match(line);
                    if (n.Success && !goNames.ContainsKey(blockId))
                        goNames[blockId] = n.Groups[1].Value.TrimEnd('\r');
                }
                else if (blockType == "114" && !stripped)
                {
                    var g = GameObjectRef.Match(line);
                    if (g.Success)
                        goId = g.Groups[1].Value;
                    if (ScriptLine.IsMatch(line))
                        isOld = true;
                }
            }
            Flush();
            foreach (var f in result)
                f.GameObjectName = f.GameObjectName != null && goNames.TryGetValue(f.GameObjectName, out string name) ? name : "(unnamed)";
            return result;

            void Flush()
            {
                if (blockType == "114" && !stripped && isOld)
                    result.Add(new Found { AssetPath = assetPath, FileId = blockId, GameObjectName = goId });
            }
        }

        /// <summary>Prefab GUIDs a scene/prefab file instantiates (nested prefabs included one level).</summary>
        static List<string> SourcePrefabs(string assetPath)
        {
            var list = new List<string>();
            string abs = MigrationIO.ToAbsolute(assetPath);
            if (!File.Exists(abs))
                return list;
            foreach (string line in File.ReadAllLines(abs))
            {
                var m = SourcePrefab.Match(line);
                if (m.Success && !list.Contains(m.Groups[1].Value))
                    list.Add(m.Groups[1].Value);
            }
            return list;
        }

        /// <summary>Every file in the prefab chain of <paramref name="assetPath"/> that declares old components.</summary>
        public static List<Found> ScanWithNested(string assetPath, HashSet<string> visited = null)
        {
            visited = visited ?? new HashSet<string>();
            var result = new List<Found>();
            if (!visited.Add(assetPath))
                return result;
            result.AddRange(ScanFile(assetPath));
            foreach (string guid in SourcePrefabs(assetPath))
            {
                string p = MigrationIO.AssetPathFromGuid(guid);
                if (p != null && p.EndsWith(".prefab"))
                    result.AddRange(ScanWithNested(p, visited));
            }
            return result;
        }

        /// <summary>Open scenes: components declared in the scene files and in the prefabs they use.</summary>
        public static List<Found> ScanOpenScenes()
        {
            var result = new List<Found>();
            var visited = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path))
                    result.AddRange(ScanWithNested(scene.path, visited));
            }
            return result;
        }

        /// <summary>Prefabs listed in SceneNetworking's network prefab list, in the open scenes.</summary>
        public static List<string> NetworkPrefabList()
        {
            var paths = new List<string>();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "SceneNetworking")
                    continue;
                var list = new SerializedObject(mb).FindProperty("_networkPrefabs");
                if (list == null || !list.isArray)
                    continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    string p = o != null ? AssetDatabase.GetAssetPath(o) : null;
                    if (!string.IsNullOrEmpty(p) && !paths.Contains(p))
                        paths.Add(p);
                }
            }
            return paths;
        }

        /// <summary>Every scene and prefab under Assets that declares old components.</summary>
        public static List<Found> ScanProject()
        {
            var result = new List<Found>();
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if ((f.EndsWith(".prefab") || f.EndsWith(".unity")) && !f.StartsWith(tool + "/"))
                    result.AddRange(ScanFile(f));
            }
            return result;
        }

        // ------------------------------------------------------------------ fixing

        /// <summary>Convert the given components of one file (all old components of the file when fileIds is null).</summary>
        public static int Fix(string assetPath, ICollection<string> fileIds)
        {
            bool isScene = assetPath.EndsWith(".unity");
            var openScene = isScene ? SceneManager.GetSceneByPath(assetPath) : default;
            if (isScene && openScene.IsValid() && openScene.isDirty)
            {
                if (!EditorUtility.DisplayDialog("Migration", "The scene " + Path.GetFileName(assetPath) + " has unsaved changes. Save it before converting?", "Save and convert", "Cancel"))
                    return 0;
                EditorSceneManager.SaveScene(openScene);
            }

            BackupManager.Create(6, "Convert " + Path.GetFileName(assetPath), new[] { assetPath });

            // 1. Swap the script reference in the file
            string abs = MigrationIO.ToAbsolute(assetPath);
            string text = File.ReadAllText(abs);
            var fixedIds = new List<string>();
            var sb = new StringBuilder(text.Length);
            string blockId = null;
            bool stripped = false;
            foreach (string line in SplitKeepEndings(text))
            {
                var h = BlockHeader.Match(line);
                if (h.Success)
                {
                    blockId = h.Groups[2].Value;
                    stripped = h.Groups[3].Success;
                }
                var s = ScriptLine.Match(line);
                if (s.Success && (stripped || fileIds == null || fileIds.Contains(blockId)))
                {
                    sb.Append(ScriptLine.Replace(line, "${1}" + MigrationConstants.NetworkTransformFileId + ", guid: " + MigrationConstants.FusionRuntimeDllGuid + "${2}"));
                    if (!stripped)
                        fixedIds.Add(blockId);
                    continue;
                }
                sb.Append(line);
            }
            if (fixedIds.Count == 0)
                return 0;
            File.WriteAllText(abs, sb.ToString());

            // 2. Reload and apply the recommended settings to the converted components
            if (isScene)
            {
                bool wasOpen = openScene.IsValid() && openScene.isLoaded;
                var scene = EditorSceneManager.OpenScene(assetPath, wasOpen ? OpenSceneMode.Single : OpenSceneMode.Additive);
                if (wasOpen && SceneManager.sceneCount > 1)
                    MigrationState.LogWarning("Other open scenes were closed to reload " + assetPath + ".");
                ApplyRecommendedSettings(scene, fixedIds);
                EditorSceneManager.SaveScene(scene);
                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }
            else
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ApplyRecommendedSettings(assetPath, fixedIds);
                ResaveScenesUsing(assetPath);
            }

            MigrationState.Current.ConvertedComponents += fixedIds.Count;
            MigrationState.Current.Save();
            MigrationState.Log($"Converted {fixedIds.Count} NetworkRigidbody3D to NetworkTransform in {assetPath}");
            return fixedIds.Count;
        }

        /// <summary>
        ///     Fusion stores the list of networked behaviours of every scene object when the scene is saved. A scene saved
        ///     while a prefab still had the missing script keeps an empty list as a prefab override, and that object would
        ///     not sync. Saving the scene again lets Fusion bake it with the converted prefab.
        /// </summary>
        static void ResaveScenesUsing(string prefabPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!f.EndsWith(".unity") || f.StartsWith(tool + "/") || !File.ReadAllText(MigrationIO.ToAbsolute(f)).Contains(guid))
                    continue;
                var scene = SceneManager.GetSceneByPath(f);
                bool wasLoaded = scene.IsValid() && scene.isLoaded;
                if (wasLoaded && scene.isDirty)
                {
                    MigrationState.LogWarning("Scene " + f + " uses " + prefabPath + " but has unsaved changes: save it yourself so Fusion updates it.");
                    continue;
                }
                if (!wasLoaded)
                    scene = EditorSceneManager.OpenScene(f, OpenSceneMode.Additive);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                MigrationState.Log("Re-saved " + f + " so Fusion updates the objects made from " + prefabPath);
            }
        }

        /// <summary>Prefabs first: scenes are saved after the prefabs they use are converted.</summary>
        public static List<string> OrderForConversion(IEnumerable<string> files)
        {
            var prefabs = new List<string>();
            var scenes = new List<string>();
            foreach (string f in files)
            {
                if (f.EndsWith(".unity")) { if (!scenes.Contains(f)) scenes.Add(f); }
                else if (!prefabs.Contains(f)) prefabs.Add(f);
            }
            prefabs.AddRange(scenes);
            return prefabs;
        }

        static IEnumerable<string> SplitKeepEndings(string text)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    yield return text.Substring(start, i - start + 1);
                    start = i + 1;
                }
            }
            if (start < text.Length)
                yield return text.Substring(start);
        }

        static void ApplyRecommendedSettings(string prefabPath, List<string> fileIds)
        {
            GameObject root = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(prefabPath))
            {
                if (o is GameObject go && go.transform.parent == null)
                    root = go;
                if (!(o is Component c) || c.GetType().Name != "NetworkTransform")
                    continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string _, out long id) || !fileIds.Contains(id.ToString()))
                    continue;
                ApplyRecommendedSettings(c);
            }
            if (root != null)
                PrefabUtility.SavePrefabAsset(root);
        }

        static void ApplyRecommendedSettings(Scene scene, List<string> fileIds)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.GetType().Name != "NetworkTransform")
                        continue;
                    ulong id = GlobalObjectId.GetGlobalObjectIdSlow(c).targetObjectId;
                    if (fileIds.Contains(((long)id).ToString()) || fileIds.Contains(id.ToString()))
                        ApplyRecommendedSettings(c);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ Recommended NetworkTransform settings

        const string NetworkTransformScriptRef = "fileID: 158639473, guid: e725a070cec140c4caffb81624c8c787";
        const string ForecastProperty = "PhysicsSettings.ForecastEnabled";
        const string ConfigFlagsProperty = "ConfigFlags";
        const string NoSharedModeInterpolationFlag = "DisableSharedModeInterpolation";
        const string GrabInteractableTypeName = "XRGrabInteractable";
        const string MovementTypeProperty = "m_MovementType";
        const int KinematicMovementType = 1; // XRBaseInteractable.MovementType.Kinematic

        /// <summary>
        ///     Applies the recommended NetworkTransform settings, and returns true when something changed:
        ///     <list type="bullet">
        ///         <item>Forecast off, on every NetworkTransform: other players see the exact path the holder sent instead of a physics guess.</item>
        ///         <item>
        ///             No State Authority interpolation in Shared Mode, only when an XRGrabInteractable with Kinematic movement
        ///             sits on the same GameObject: its grab moves the object outside FixedUpdateNetwork, and the interpolation
        ///             fights it (the grab stutters at the tick rate). Other movement types do not need it, so the flag is left
        ///             as it is on them (never cleared: the creator may have set it on purpose).
        ///         </item>
        ///     </list>
        /// </summary>
        public static bool ApplyRecommendedSettings(Component c)
        {
            if (c == null)
                return false;
            var so = new SerializedObject(c);
            bool changed = false;

            var forecast = so.FindProperty(ForecastProperty);
            if (forecast == null)
                MigrationState.LogWarning("Forecast setting not found on " + c.name + " (Fusion version changed?)");
            else if (forecast.boolValue)
            {
                forecast.boolValue = false;
                changed = true;
            }

            if (IsKinematicGrab(c.gameObject))
            {
                var flags = so.FindProperty(ConfigFlagsProperty);
                int flag = NoSharedModeInterpolationValue(c);
                if (flags == null || flag == 0)
                    MigrationState.LogWarning("Shared Mode interpolation setting not found on " + c.name + " (Fusion version changed?)");
                else if ((flags.intValue & flag) == 0)
                {
                    flags.intValue |= flag;
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(c);
            }
            return changed;
        }

        /// <summary>True when the component still has Forecast on, or is a Kinematic grab with State Authority interpolation on.</summary>
        public static bool NeedsRecommendedSettings(Component c)
        {
            if (c == null)
                return false;
            var so = new SerializedObject(c);
            var forecast = so.FindProperty(ForecastProperty);
            if (forecast != null && forecast.boolValue)
                return true;
            if (!IsKinematicGrab(c.gameObject))
                return false;
            var flags = so.FindProperty(ConfigFlagsProperty);
            int flag = NoSharedModeInterpolationValue(c);
            return flags != null && flag != 0 && (flags.intValue & flag) == 0;
        }

        /// <summary>
        ///     True when the GameObject has an XRGrabInteractable (or a subclass) set to Kinematic movement. Read by type name
        ///     and serialized field, so the tool does not depend on the XR Interaction Toolkit assembly.
        /// </summary>
        static bool IsKinematicGrab(GameObject go)
        {
            if (go == null)
                return false;
            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;
                bool isGrab = false;
                for (var t = mb.GetType(); t != null && !isGrab; t = t.BaseType)
                    isGrab = t.Name == GrabInteractableTypeName;
                if (!isGrab)
                    continue;
                var movement = new SerializedObject(mb).FindProperty(MovementTypeProperty);
                if (movement != null && movement.intValue == KinematicMovementType)
                    return true;
            }
            return false;
        }

        static int _noSharedModeInterpolationValue = -1;

        /// <summary>Value of NetworkTransformFlags.DisableSharedModeInterpolation, read by name (0 when it does not exist).</summary>
        static int NoSharedModeInterpolationValue(Component c)
        {
            if (_noSharedModeInterpolationValue >= 0)
                return _noSharedModeInterpolationValue;
            _noSharedModeInterpolationValue = 0;
            for (var t = c.GetType(); t != null && _noSharedModeInterpolationValue == 0; t = t.BaseType)
            {
                foreach (var nested in t.GetNestedTypes())
                {
                    if (nested.IsEnum && Enum.IsDefined(nested, NoSharedModeInterpolationFlag))
                    {
                        _noSharedModeInterpolationValue = Convert.ToInt32(Enum.Parse(nested, NoSharedModeInterpolationFlag));
                        break;
                    }
                }
            }
            return _noSharedModeInterpolationValue;
        }

        /// <summary>Scenes and prefabs that hold a NetworkTransform or override one of its settings.</summary>
        public static List<string> FilesWithNetworkTransforms()
        {
            var result = new List<string>();
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!(f.EndsWith(".prefab") || f.EndsWith(".unity")) || f.StartsWith(tool + "/"))
                    continue;
                string text = File.ReadAllText(MigrationIO.ToAbsolute(f));
                if (text.Contains(NetworkTransformScriptRef) || text.Contains(ForecastProperty) || text.Contains("propertyPath: " + ConfigFlagsProperty))
                    result.Add(f);
            }
            return result;
        }

        /// <summary>
        ///     Applies the recommended settings to every NetworkTransform of every prefab and scene, through the API: a
        ///     component saved before Fusion 2.1 has no line for them in its file and loads with the defaults, so a text edit
        ///     would miss it. Prefabs first, then scenes (where prefab instances may override the prefab values).
        ///     Returns the components changed.
        /// </summary>
        public static int ApplyRecommendedSettingsEverywhere()
        {
            var files = OrderForConversion(FilesWithNetworkTransforms());
            if (files.Count == 0)
            {
                MigrationState.Current.NetworkTransformSettingsDone = true;
                MigrationState.Current.Save();
                return 0;
            }
            if (!MigrationIO.CloseScenesSafely(MigrationState.Current))
                return 0;
            var scopes = new List<string>(files);
            string config = MigrationIO.AssetPathFromGuid(MigrationConstants.NetworkProjectConfigGuid);
            if (config != null)
                scopes.Add(config);
            BackupManager.Create(6, "Recommended NetworkTransform settings", scopes);

            int changed = 0;
            foreach (string f in files)
            {
                if (f.EndsWith(".prefab"))
                {
                    GameObject root = null;
                    int n = 0;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(f))
                    {
                        if (o is GameObject go && go.transform.parent == null)
                            root = go;
                        if (o is Component c && c.GetType().Name == "NetworkTransform" && ApplyRecommendedSettings(c))
                            n++;
                    }
                    if (n > 0 && root != null)
                        PrefabUtility.SavePrefabAsset(root);
                    changed += n;
                }
                else
                {
                    var scene = EditorSceneManager.OpenScene(f, OpenSceneMode.Additive);
                    int n = 0;
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        foreach (var c in go.GetComponentsInChildren<Component>(true))
                        {
                            if (c != null && c.GetType().Name == "NetworkTransform" && ApplyRecommendedSettings(c))
                                n++;
                        }
                    }
                    if (n > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                    EditorSceneManager.CloseScene(scene, true);
                    changed += n;
                }
            }

            // The world's own global switch (the client uses its own config; this keeps editor tests the same)
            if (config != null)
            {
                string abs = MigrationIO.ToAbsolute(config);
                string json = File.ReadAllText(abs);
                string updated = Regex.Replace(json, "(\"PhysicsForecast\"\\s*:\\s*)true", "${1}false");
                if (updated != json)
                {
                    File.WriteAllText(abs, updated);
                    AssetDatabase.ImportAsset(config);
                    MigrationState.Log("PhysicsForecast set to false in " + config);
                }
            }

            MigrationState.Current.NetworkTransformSettingsDone = true;
            MigrationState.Current.Save();
            MigrationState.Log($"Recommended settings applied to {changed} NetworkTransform component(s) in {files.Count} file(s).");
            return changed;
        }

        public static void RebuildPrefabTable()
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table"))
                MigrationState.LogWarning("Could not run Tools/Fusion/Rebuild Prefab Table. Run it by hand.");
            else
                MigrationState.Log("Fusion prefab table rebuilt.");
        }
    }
}
