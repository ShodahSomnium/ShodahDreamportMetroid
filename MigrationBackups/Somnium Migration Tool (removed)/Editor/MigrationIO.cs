using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Somnium.MigrationTool
{
    /// <summary>File system, hashing, scene and Player Settings helpers shared by the steps.</summary>
    internal static class MigrationIO
    {
        static readonly Regex GuidLine = new Regex(@"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline);
        static readonly Regex NamespaceRegex = new Regex(@"\bnamespace\s+[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*");

        public static string ProjectRoot => MigrationState.ProjectRoot;

        public static string ToAbsolute(string projectRelative) => ProjectRoot + "/" + projectRelative.TrimStart('/');

        public static string ToProjectRelative(string absolute)
        {
            absolute = absolute.Replace('\\', '/');
            string root = ProjectRoot + "/";
            return absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? absolute.Substring(root.Length) : absolute;
        }

        public static bool IsCodeFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".cs" || ext == ".asmdef" || ext == ".asmref" || ext == ".rsp";
        }

        /// <summary>GUID from the .meta next to <paramref name="projectRelative"/>, or null.</summary>
        public static string ReadMetaGuid(string projectRelative)
        {
            string meta = ToAbsolute(projectRelative) + ".meta";
            if (!File.Exists(meta))
                return null;
            var m = GuidLine.Match(File.ReadAllText(meta));
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        ///     MD5 of the file with CR bytes and a leading UTF-8 BOM removed when it looks like text (no NUL byte in the
        ///     first 8000 bytes), raw MD5 otherwise. Must match mkknown.pl, which produced the known-file tables.
        /// </summary>
        public static string NormalizedHash(string projectRelative, bool neutralizeNamespaces)
        {
            byte[] bytes = File.ReadAllBytes(ToAbsolute(projectRelative));
            bool isText = Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) < 0;
            if (isText)
            {
                var list = new List<byte>(bytes.Length);
                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                for (int i = start; i < bytes.Length; i++)
                {
                    if (bytes[i] != (byte)'\r')
                        list.Add(bytes[i]);
                }
                bytes = list.ToArray();
                if (neutralizeNamespaces)
                {
                    // Latin-1 keeps one char per byte, so the regex works on the raw bytes without changing any of them
                    string s = Encoding.GetEncoding(28591).GetString(bytes);
                    s = NamespaceRegex.Replace(s, "namespace NS");
                    bytes = Encoding.GetEncoding(28591).GetBytes(s);
                }
            }
            using (var md5 = MD5.Create())
            {
                var sb = new StringBuilder(32);
                foreach (byte b in md5.ComputeHash(bytes))
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>All files (no .meta) under a project-relative folder, project-relative, sorted.</summary>
        public static List<string> ListFiles(string projectRelativeFolder)
        {
            var result = new List<string>();
            string abs = ToAbsolute(projectRelativeFolder);
            if (!Directory.Exists(abs))
                return result;
            foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string rel = ToProjectRelative(f);
                if (rel.Contains("/.") || Path.GetFileName(rel).StartsWith("."))
                    continue; // Hidden files are not Unity assets
                result.Add(rel);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public static long FolderSize(string projectRelative)
        {
            string abs = ToAbsolute(projectRelative);
            if (File.Exists(abs))
                return new FileInfo(abs).Length;
            if (!Directory.Exists(abs))
                return 0;
            long total = 0;
            foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                total += new FileInfo(f).Length;
            return total;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes > 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
            if (bytes > 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        /// <summary>Move a file and its .meta outside Unity's control (paths project-relative). Creates folders.</summary>
        public static void MoveFileWithMeta(string from, string to)
        {
            string absFrom = ToAbsolute(from), absTo = ToAbsolute(to);
            Directory.CreateDirectory(Path.GetDirectoryName(absTo));
            if (File.Exists(absTo))
                File.Delete(absTo);
            File.Move(absFrom, absTo);
            if (File.Exists(absFrom + ".meta"))
            {
                if (File.Exists(absTo + ".meta"))
                    File.Delete(absTo + ".meta");
                File.Move(absFrom + ".meta", absTo + ".meta");
            }
        }

        /// <summary>Remove folders that became empty (and their .meta) under a project-relative root, deepest first.</summary>
        public static void DeleteEmptyFolders(string projectRelativeRoot)
        {
            string abs = ToAbsolute(projectRelativeRoot);
            if (!Directory.Exists(abs))
                return;
            var dirs = new List<string>(Directory.GetDirectories(abs, "*", SearchOption.AllDirectories));
            dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (string d in dirs)
            {
                if (Directory.GetFiles(d).Length == 0 && Directory.GetDirectories(d).Length == 0)
                {
                    Directory.Delete(d);
                    if (File.Exists(d + ".meta"))
                        File.Delete(d + ".meta");
                }
            }
        }

        // ---------------------------------------------------------------- scenes

        /// <summary>
        ///     Asks to save modified scenes, remembers the open scenes and switches to an empty scene, so no scene is
        ///     open (and can be saved by accident) while scripts are missing. Returns false if the user cancelled.
        /// </summary>
        public static bool CloseScenesSafely(MigrationState state)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Migration", "Please exit Play Mode first.", "OK");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            if (state.OpenScenesBefore.Count == 0)
            {
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    string p = EditorSceneManager.GetSceneAt(i).path;
                    if (!string.IsNullOrEmpty(p))
                        state.OpenScenesBefore.Add(p);
                }
                state.Save();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return true;
        }

        /// <summary>Re-open the scenes that were open when the migration started.</summary>
        public static void ReopenScenes(MigrationState state)
        {
            bool first = true;
            foreach (string p in state.OpenScenesBefore)
            {
                if (!File.Exists(ToAbsolute(p)))
                    continue;
                EditorSceneManager.OpenScene(p, first ? OpenSceneMode.Single : OpenSceneMode.Additive);
                first = false;
            }
        }

        // ---------------------------------------------------------------- scripting defines

        /// <summary>
        ///     Writes the Fusion 2.1.3 defines exactly like Fusion's own installer does on its first load (removes the old
        ///     FUSION_x_y defines, adds the new ones), and removes the PUN and Photon Voice defines (both are removed with
        ///     the old Photon). Setting them before the import means code that checks them compiles right away, instead of
        ///     after Fusion's own first successful compile.
        /// </summary>
        public static int ApplyFusion21Defines()
        {
            var versionDefine = new Regex(@"^FUSION(_[0-9]+){1,3}(_OR_NEWER)?$");
            var state = MigrationState.Current;
            bool record = state.DefinesBefore.Count == 0;
            var targets = AllNamedTargets();

            int changed = 0;
            foreach (var target in targets)
            {
                try
                {
                    string current = PlayerSettings.GetScriptingDefineSymbols(target);
                    if (record)
                        state.DefinesBefore.Add(target.TargetName + "=" + current);
                    var list = new List<string>(current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    list.RemoveAll(d => versionDefine.IsMatch(d) || d == "PHOTON_UNITY_NETWORKING" || d.StartsWith("PUN_2_") || d.StartsWith("PHOTON_VOICE"));
                    foreach (string d in MigrationConstants.Fusion21Defines)
                    {
                        if (!list.Contains(d))
                            list.Add(d);
                    }
                    if (!list.Exists(d => d.StartsWith("FUSION_LOGLEVEL_")))
                        list.Add("FUSION_LOGLEVEL_INFO");
                    string updated = string.Join(";", list);
                    if (updated != current)
                    {
                        PlayerSettings.SetScriptingDefineSymbols(target, updated);
                        changed++;
                    }
                }
                catch (ArgumentException)
                {
                    // Obsolete or unsupported target
                }
            }
            state.Save();
            return changed;
        }

        /// <summary>Put back the scripting defines recorded before step 4.</summary>
        public static void RestoreDefines()
        {
            var state = MigrationState.Current;
            if (state.DefinesBefore.Count == 0)
                return;
            foreach (var target in AllNamedTargets())
            {
                foreach (string entry in state.DefinesBefore)
                {
                    int eq = entry.IndexOf('=');
                    if (eq > 0 && entry.Substring(0, eq) == target.TargetName)
                    {
                        try { PlayerSettings.SetScriptingDefineSymbols(target, entry.Substring(eq + 1)); }
                        catch (ArgumentException) { }
                    }
                }
            }
            state.DefinesBefore.Clear();
            state.Save();
            MigrationState.Log("Scripting define symbols restored to their values before step 4.");
        }

        static HashSet<NamedBuildTarget> AllNamedTargets()
        {
            var targets = new HashSet<NamedBuildTarget>();
            foreach (BuildTargetGroup g in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (g == BuildTargetGroup.Unknown)
                    continue;
                var field = typeof(BuildTargetGroup).GetField(g.ToString());
                if (field == null || field.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
                    continue;
                try { targets.Add(NamedBuildTarget.FromBuildTargetGroup(g)); }
                catch (Exception) { /* Not a valid named target on this Unity version */ }
            }
            targets.Add(NamedBuildTarget.Server);
            return targets;
        }

        // ---------------------------------------------------------------- misc

        /// <summary>Fusion version from Assets/Photon/Fusion/build_info.txt, or "none".</summary>
        public static string ReadFusionVersion()
        {
            string p = ToAbsolute(MigrationConstants.PhotonRoot + "/Fusion/build_info.txt");
            if (!File.Exists(p))
                return File.Exists(ToAbsolute(MigrationConstants.PhotonRoot + "/Fusion/Assemblies/Fusion.Runtime.dll")) ? "unknown" : "none";
            foreach (string line in File.ReadAllLines(p))
            {
                if (line.StartsWith("build:"))
                    return line.Substring(6).Trim();
            }
            return "unknown";
        }

        public static bool IsFusion21Installed() => ReadFusionVersion().StartsWith("2.1");

        public static bool IsGitProject() => Directory.Exists(ToAbsolute(".git")) || File.Exists(ToAbsolute(".git"));

        public static string AssetPathFromGuid(string guid)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(p) || !(File.Exists(ToAbsolute(p)) || Directory.Exists(ToAbsolute(p))) ? null : p;
        }
    }
}
