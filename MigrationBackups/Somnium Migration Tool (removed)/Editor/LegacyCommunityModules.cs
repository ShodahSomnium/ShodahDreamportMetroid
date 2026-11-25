using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     What older Community Modules releases (2.0, 2.2) need beyond the plain update:
    ///     - retired scripts (not in the target release) still used by the world: kept instead of deleted;
    ///     - classes renamed under the same GUID (CharacterTrigger, NetworkFloat): code updated to the new names;
    ///     - NetworkFloat fields renamed in NetworkEventFloat: saved values carried over in scene and prefab files.
    /// </summary>
    internal static class LegacyCommunityModules
    {
        public const string NetworkEventFloatGuid = "55d7ded80d9e80d4eaf81bb8773ef011";

        /// <summary>Old name, new name. Member renames start with '.' so only member access is changed.</summary>
        public static readonly string[,] Renames =
        {
            { "CharacterTrigger", "LocalPlayerTrigger" },
            { ".OnCharacterEnter", ".OnPlayerEnter" },
            { ".OnCharacterExit", ".OnPlayerExit" },
            { "NetworkFloat", "NetworkEventFloat" },
        };

        /// <summary>NetworkFloat (2.x) serialized field, NetworkEventFloat (3.x) field.</summary>
        static readonly string[,] FloatFields =
        {
            { "_floatID", "_dataIndex" },
            { "_defaultValue", "_initialValue" },
            { "_syncedEvent", "_onEvent" },
        };

        static readonly Regex BlockHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)( stripped)?");
        static readonly Regex ScriptGuid = new Regex(@"^\s*m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})");
        static readonly Regex GameObjectRef = new Regex(@"^\s*m_GameObject: \{fileID: (-?\d+)\}");
        static readonly Regex NameLine = new Regex(@"^\s*m_Name: (.*)$");
        static readonly Regex ModTarget = new Regex(@"^\s*- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32})");
        static readonly Regex PropertyPath = new Regex(@"^(\s*propertyPath: )(_floatID|_defaultValue|_syncedEvent)\b");

        internal class Usage
        {
            public string ScriptGuid;
            public string ScriptName;
            public string AssetPath;
            public string GameObjectName;
            public bool ScriptMissing; // The script file is already gone from the project: nothing can be kept
        }

        public class Report
        {
            public readonly List<Usage> RetiredUsages = new List<Usage>();
            public readonly HashSet<string> UsedRetiredGuids = new HashSet<string>();
            public readonly List<string> FilesWithOldFloatFields = new List<string>();
            public int OldFloatComponents;
            public readonly List<string> ScriptsUsingRenamedClasses = new List<string>();
        }

        // ------------------------------------------------------------------ known data

        /// <summary>Code shipped by an older release and absent from the target: GUID -> script name.</summary>
        public static Dictionary<string, string> RetiredScripts()
        {
            var result = new Dictionary<string, string>();
            string target = "v" + MigrationConstants.TargetCommunityModulesVersion;
            foreach (var kv in KnownFiles.Instance.CommunityModules)
            {
                bool inTarget = false;
                string name = null;
                foreach (var e in kv.Value)
                {
                    if (e.Version == target) inTarget = true;
                    if (e.Kind == "code" && e.Path.EndsWith(".cs") && !e.Path.Contains("/" + MigrationConstants.ScriptsBackupFolderName + "/"))
                        name = Path.GetFileNameWithoutExtension(e.Path);
                }
                if (!inTarget && name != null)
                    result[kv.Key] = name;
            }
            return result;
        }

        /// <summary>True for a Community Modules file the target release ships too (the update replaces it).</summary>
        static bool IsReplacedByTarget(string path) => KnownFiles.Instance.PathInTargetCm(MigrationIO.ReadMetaGuid(path)) != null;

        // ------------------------------------------------------------------ analysis (read-only)

        /// <summary>
        ///     Scenes and prefabs that stay in the world after the update: the creator's own, and old Community Modules files
        ///     that the target release no longer ships (they are kept as they are).
        /// </summary>
        static IEnumerable<string> WorldAssets()
        {
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if ((f.EndsWith(".unity") || f.EndsWith(".prefab")) && !f.StartsWith(tool + "/") && !IsReplacedByTarget(f))
                    yield return f;
            }
        }

        public static Report Analyse(IEnumerable<string> creatorScripts)
        {
            var report = new Report();
            var retired = RetiredScripts();
            foreach (string asset in WorldAssets())
            {
                string text = File.ReadAllText(MigrationIO.ToAbsolute(asset));
                bool hasRetired = false;
                foreach (string g in retired.Keys)
                {
                    if (text.Contains(g)) { hasRetired = true; break; }
                }
                bool hasFloat = text.Contains(NetworkEventFloatGuid) && text.Contains("_floatID");
                if (!hasRetired && !hasFloat)
                    continue;

                var goNames = new Dictionary<string, string>();
                var components = new List<KeyValuePair<string, string>>(); // script guid, gameobject id
                string type = null, goId = null, script = null;
                bool stripped = false, floatFields = false;
                void Flush()
                {
                    if (type == "114" && !stripped && script != null)
                    {
                        if (retired.ContainsKey(script))
                            components.Add(new KeyValuePair<string, string>(script, goId));
                        if (script == NetworkEventFloatGuid && floatFields)
                            report.OldFloatComponents++;
                    }
                }
                string currentId = null;
                foreach (string line in text.Split('\n'))
                {
                    var h = BlockHeader.Match(line);
                    if (h.Success)
                    {
                        Flush();
                        type = h.Groups[1].Value; currentId = h.Groups[2].Value; stripped = h.Groups[3].Success;
                        goId = null; script = null; floatFields = false;
                        continue;
                    }
                    if (type == "1")
                    {
                        var n = NameLine.Match(line);
                        if (n.Success && !goNames.ContainsKey(currentId)) goNames[currentId] = n.Groups[1].Value.TrimEnd('\r');
                    }
                    else if (type == "114")
                    {
                        var g = GameObjectRef.Match(line); if (g.Success) goId = g.Groups[1].Value;
                        var s = ScriptGuid.Match(line); if (s.Success) script = s.Groups[1].Value;
                        if (line.StartsWith("  _floatID:")) floatFields = true;
                    }
                }
                Flush();

                foreach (var c in components)
                {
                    report.UsedRetiredGuids.Add(c.Key);
                    report.RetiredUsages.Add(new Usage
                    {
                        ScriptGuid = c.Key, ScriptName = retired[c.Key], AssetPath = asset,
                        GameObjectName = c.Value != null && goNames.TryGetValue(c.Value, out string name) ? name : "(object)",
                        ScriptMissing = MigrationIO.AssetPathFromGuid(c.Key) == null,
                    });
                }
                // Prefab overrides of NetworkFloat fields count as files to update too
                if (hasFloat || Regex.IsMatch(text, @"propertyPath: (_floatID|_defaultValue|_syncedEvent)\b"))
                {
                    if (hasFloat || NeedsFloatFieldUpdate(asset))
                        report.FilesWithOldFloatFields.Add(asset);
                }
            }

            var renameRx = RenameRegex();
            foreach (string s in creatorScripts)
            {
                if (File.Exists(MigrationIO.ToAbsolute(s)) && renameRx.IsMatch(File.ReadAllText(MigrationIO.ToAbsolute(s))))
                    report.ScriptsUsingRenamedClasses.Add(s);
            }
            return report;
        }

        // ------------------------------------------------------------------ class renames

        static Regex RenameRegex()
        {
            var parts = new List<string>();
            for (int i = 0; i < Renames.GetLength(0); i++)
            {
                string old = Renames[i, 0];
                parts.Add(old.StartsWith(".") ? Regex.Escape(old) + @"\b" : @"\b" + Regex.Escape(old) + @"\b");
            }
            return new Regex(string.Join("|", parts));
        }

        /// <summary>Apply the class/member renames to one C# file. Returns true if it changed.</summary>
        public static bool ApplyRenames(string projectRelative)
        {
            string abs = MigrationIO.ToAbsolute(projectRelative);
            string text = File.ReadAllText(abs);
            string updated = text;
            for (int i = 0; i < Renames.GetLength(0); i++)
            {
                string old = Renames[i, 0], now = Renames[i, 1];
                string pattern = old.StartsWith(".") ? Regex.Escape(old) + @"\b" : @"\b" + Regex.Escape(old) + @"\b";
                updated = Regex.Replace(updated, pattern, now);
            }
            if (updated == text)
                return false;
            File.WriteAllText(abs, updated);
            MigrationState.Log("Renamed classes in " + projectRelative);
            return true;
        }

        // ------------------------------------------------------------------ NetworkFloat -> NetworkEventFloat values

        static readonly Dictionary<string, Dictionary<string, string>> ScriptOfComponentCache = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>fileID -> script GUID of every MonoBehaviour declared in a prefab (cached).</summary>
        static Dictionary<string, string> ScriptsOfComponents(string prefabGuid)
        {
            if (ScriptOfComponentCache.TryGetValue(prefabGuid, out var map))
                return map;
            map = new Dictionary<string, string>();
            string path = MigrationIO.AssetPathFromGuid(prefabGuid);
            if (path != null && path.EndsWith(".prefab"))
            {
                string id = null; bool mono = false;
                foreach (string line in File.ReadAllLines(MigrationIO.ToAbsolute(path)))
                {
                    var h = BlockHeader.Match(line);
                    if (h.Success) { id = h.Groups[2].Value; mono = h.Groups[1].Value == "114"; continue; }
                    if (!mono) continue;
                    var s = ScriptGuid.Match(line);
                    if (s.Success) map[id] = s.Groups[1].Value;
                }
            }
            ScriptOfComponentCache[prefabGuid] = map;
            return map;
        }

        static bool NeedsFloatFieldUpdate(string asset) => UpdateFloatFields(asset, false) > 0;

        /// <summary>
        ///     Rename NetworkFloat's serialized fields to NetworkEventFloat's in one scene/prefab file: in components declared
        ///     there, and in prefab overrides whose target is a NetworkFloat component. Returns the number of lines changed.
        /// </summary>
        public static int UpdateFloatFields(string asset, bool write)
        {
            string abs = MigrationIO.ToAbsolute(asset);
            string text = File.ReadAllText(abs);
            var sb = new StringBuilder(text.Length);
            int changed = 0;
            string type = null;
            bool isFloat = false, stripped = false;
            string modTargetScript = null;
            foreach (string line in SplitKeepEndings(text))
            {
                string outLine = line;
                var h = BlockHeader.Match(line);
                if (h.Success)
                {
                    type = h.Groups[1].Value; stripped = h.Groups[3].Success; isFloat = false; modTargetScript = null;
                }
                else if (type == "114" && !stripped)
                {
                    var s = ScriptGuid.Match(line);
                    if (s.Success) isFloat = s.Groups[1].Value == NetworkEventFloatGuid;
                    else if (isFloat)
                    {
                        for (int i = 0; i < FloatFields.GetLength(0); i++)
                        {
                            string old = "  " + FloatFields[i, 0] + ":";
                            if (line.StartsWith(old))
                            {
                                outLine = "  " + FloatFields[i, 1] + ":" + line.Substring(old.Length);
                                changed++;
                            }
                        }
                    }
                }
                else if (type == "1001")
                {
                    var t = ModTarget.Match(line);
                    if (t.Success)
                        modTargetScript = ScriptsOfComponents(t.Groups[2].Value).TryGetValue(t.Groups[1].Value, out string g) ? g : null;
                    var p = PropertyPath.Match(line);
                    if (p.Success && modTargetScript == NetworkEventFloatGuid)
                    {
                        string field = p.Groups[2].Value, now = null;
                        for (int i = 0; i < FloatFields.GetLength(0); i++)
                            if (FloatFields[i, 0] == field) now = FloatFields[i, 1];
                        outLine = p.Groups[1].Value + now + line.Substring(p.Length);
                        changed++;
                    }
                }
                sb.Append(outLine);
            }
            if (write && changed > 0)
            {
                File.WriteAllText(abs, sb.ToString());
                MigrationState.Log($"NetworkEventFloat values carried over in {asset} ({changed} lines)");
            }
            return changed;
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

        /// <summary>Folder (under the Community Modules scripts folder) where kept retired scripts are restored.</summary>
        public static string RetiredFolder(string scriptsFolder) => scriptsFolder + "/_Retired (older Community Modules)";
    }
}
