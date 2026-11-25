using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Somnium.MigrationTool
{
    /// <summary>Step 5: import Community Modules 4.0 and restore what makes the world unique (asmdef name, namespace, scripts).</summary>
    internal static class CommunityModulesStep
    {
        public class ImportPlan
        {
            public string RootMoveFrom; // Whole Community Modules folder moved back in one go (keeps every folder GUID)
            public readonly List<KeyValuePair<string, string>> Moves = new List<KeyValuePair<string, string>>();  // current path -> package path
            public readonly List<KeyValuePair<string, string>> Asides = new List<KeyValuePair<string, string>>(); // blocking file -> new name
            public readonly List<string> ModifiedAssets = new List<string>();
            public readonly List<string> Problems = new List<string>(); // Block the import
            public readonly List<string> Warnings = new List<string>(); // Block it in the wizard only (advanced mode warns)
        }

        public static string PackagePath => MigrationIO.AssetPathFromGuid(MigrationConstants.CommunityModulesPackageGuid);

        /// <summary>
        ///     True when the target release's NetworkGrabbable is on disk (it changed in 4.0, so an old copy never matches).
        ///     The asmdef alone is not proof: with "Do nothing" the old one is still there before the import.
        /// </summary>
        public static bool IsTargetVersionImported()
        {
            string path = MigrationIO.AssetPathFromGuid(MigrationConstants.NetworkGrabbableGuid);
            if (path == null)
                return false;
            var versions = KnownFiles.Instance.MatchingCmVersions(MigrationConstants.NetworkGrabbableGuid,
                MigrationIO.NormalizedHash(path, false), MigrationIO.NormalizedHash(path, true));
            return versions.Contains(TargetVersion);
        }

        static string TargetVersion => "v" + MigrationConstants.TargetCommunityModulesVersion;

        public static ImportPlan BuildPlan()
        {
            var plan = new ImportPlan();
            var state = MigrationState.Current;
            string canonical = MigrationConstants.CmCanonicalRoot;
            if (!string.IsNullOrEmpty(state.CmRoot) && state.CmRoot != canonical && Directory.Exists(MigrationIO.ToAbsolute(state.CmRoot)) &&
                !Directory.Exists(MigrationIO.ToAbsolute(canonical)))
                plan.RootMoveFrom = state.CmRoot;

            foreach (var kv in KnownFiles.Instance.CommunityModules)
            {
                foreach (var e in kv.Value)
                {
                    if (e.Version != TargetVersion || e.Kind == "folder")
                        continue;
                    string current = MigrationIO.AssetPathFromGuid(kv.Key);
                    if (current != null && plan.RootMoveFrom != null && current.StartsWith(plan.RootMoveFrom + "/"))
                        current = canonical + current.Substring(plan.RootMoveFrom.Length); // Where it will be after the root move
                    if (current != null && current != e.Path)
                        plan.Moves.Add(new KeyValuePair<string, string>(current, e.Path));
                    if (current != e.Path && File.Exists(MigrationIO.ToAbsolute(e.Path)))
                    {
                        string blockingGuid = MigrationIO.ReadMetaGuid(e.Path);
                        if (blockingGuid != kv.Key)
                        {
                            string ext = Path.GetExtension(e.Path);
                            string aside = e.Path.Substring(0, e.Path.Length - ext.Length) + " (before " + MigrationConstants.TargetCommunityModulesVersion + ")" + ext;
                            plan.Asides.Add(new KeyValuePair<string, string>(e.Path, aside));
                        }
                    }
                }
            }
            foreach (string p in state.ModifiedOriginalAssets)
            {
                if (File.Exists(MigrationIO.ToAbsolute(p)))
                    plan.ModifiedAssets.Add(p);
            }
            if (PackagePath == null)
                plan.Problems.Add("The Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " package of the migration tool is missing. Please re-import the migration tool.");
            if (!MigrationIO.IsFusion21Installed())
                plan.Warnings.Add("Photon Fusion 2.1 is not installed yet (step 4). Community Modules " + MigrationConstants.TargetCommunityModulesVersion +
                    " needs it: installed now, it shows compile errors until Fusion 2.1 is installed.");
            return plan;
        }

        public static void ExecuteImport(ImportPlan plan, ICollection<string> keepMine)
        {
            var state = MigrationState.Current;
            if (!MigrationIO.CloseScenesSafely(state))
                return;

            // The import replaces the asmdef and the namespaces: what step 5 restores afterwards must run again, even when
            // it already ran (advanced mode can install Community Modules again)
            state.ResetCommunityModulesImport();

            var scopes = new List<string> { MigrationConstants.CmCanonicalRoot };
            if (!string.IsNullOrEmpty(state.CmRoot) && state.CmRoot != MigrationConstants.CmCanonicalRoot)
                scopes.Add(state.CmRoot);
            foreach (var m in plan.Moves)
            {
                if (!m.Key.StartsWith(MigrationConstants.CmCanonicalRoot + "/") && !scopes.Contains(m.Key))
                    scopes.Add(m.Key);
            }
            var backup = BackupManager.Create(5, "Before Community Modules 4.0 import", scopes);

            // Keep copies of the creator's edited assets they want to keep
            state.KeepMyVersion = new List<string>(keepMine);
            string keepFolder = MigrationState.WorkFolder + "/" + backup.Id + "/keep";
            foreach (string p in keepMine)
            {
                string to = keepFolder + "/" + p;
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(MigrationIO.ToAbsolute(p), to, true);
            }
            state.KeepFolder = keepFolder;

            // Files with a package GUID somewhere else would be duplicated by the import: move them to the package path
            if (plan.RootMoveFrom != null)
            {
                EnsureFolder(Path.GetDirectoryName(MigrationConstants.CmCanonicalRoot).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(plan.RootMoveFrom, MigrationConstants.CmCanonicalRoot);
                if (!string.IsNullOrEmpty(err))
                {
                    MigrationState.LogError("Could not move " + plan.RootMoveFrom + " to " + MigrationConstants.CmCanonicalRoot + ": " + err);
                    EditorUtility.DisplayDialog("Migration", "Community Modules could not be moved to its standard location:\n" + err + "\n\nNothing was imported.", "OK");
                    return;
                }
                state.MovedForImport.Add(plan.RootMoveFrom + "|" + MigrationConstants.CmCanonicalRoot);
                MigrationState.Log("Moved " + plan.RootMoveFrom + " to " + MigrationConstants.CmCanonicalRoot + " for the update.");
            }
            foreach (var a in plan.Asides)
            {
                string err = AssetDatabase.MoveAsset(a.Key, a.Value);
                MigrationState.Log("Moved aside " + a.Key + " -> " + a.Value + (string.IsNullOrEmpty(err) ? "" : " FAILED: " + err));
            }
            foreach (var m in plan.Moves)
            {
                EnsureFolder(Path.GetDirectoryName(m.Value).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(m.Key, m.Value);
                if (!string.IsNullOrEmpty(err))
                    MigrationState.LogWarning("Could not move " + m.Key + " to " + m.Value + ": " + err);
                else
                    state.MovedForImport.Add(m.Key + "|" + m.Value);
            }
            state.CmMovedToCanonical = plan.Moves.Count > 0 || plan.RootMoveFrom != null;
            if (plan.RootMoveFrom == null && !string.IsNullOrEmpty(state.CmRoot) && state.CmRoot != MigrationConstants.CmCanonicalRoot)
                MigrationIO.DeleteEmptyFolders(state.CmRoot);

            // NetworkFloat (2.x) values: rename the fields now, while the script is missing. After the import Unity and Fusion
            // re-save prefabs with the new class, and fields it does not know would be dropped.
            if (state.UpdateFloatValues && !state.FloatValuesUpdated)
            {
                var files = LegacyCommunityModules.Analyse(new string[0]).FilesWithOldFloatFields;
                if (files.Count > 0)
                {
                    BackupManager.Create(5, "NetworkFloat values", files);
                    foreach (string f in files)
                        LegacyCommunityModules.UpdateFloatFields(f, true);
                }
                state.FloatValuesUpdated = true;
            }

            state.CmImportRequested = true;
            state.PendingAction = MigrationResume.AfterCmImport;
            state.Save();
            MigrationState.Log("Importing " + PackagePath);
            AssetDatabase.ImportPackage(PackagePath, false);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Where a path from before the update is now, if the whole Community Modules folder was moved for it.</summary>
        static string MapThroughRootMove(MigrationState state, string path)
        {
            foreach (string pair in state.MovedForImport)
            {
                string[] parts = pair.Split('|');
                if (parts.Length == 2 && parts[1] == MigrationConstants.CmCanonicalRoot && path.StartsWith(parts[0] + "/"))
                    return MigrationConstants.CmCanonicalRoot + path.Substring(parts[0].Length);
            }
            return path;
        }

        /// <summary>Everything after the import. Idempotent: safe to run again after a domain reload.</summary>
        public static void AfterImport()
        {
            var state = MigrationState.Current;

            // 1. Assets the creator wants to keep (copied before the import, at their path from before the update)
            if (!string.IsNullOrEmpty(state.KeepFolder))
            {
                foreach (string p in state.KeepMyVersion)
                {
                    string from = state.KeepFolder + "/" + p;
                    string to = MapThroughRootMove(state, p);
                    if (File.Exists(from) && File.Exists(MigrationIO.ToAbsolute(to)))
                    {
                        File.Copy(from, MigrationIO.ToAbsolute(to), true);
                        AssetDatabase.ImportAsset(to);
                        MigrationState.Log("Kept your version of " + to);
                    }
                }
            }

            // 2. The world's own assembly name, namespace and references
            if (!state.AsmdefReapplied)
            {
                ReapplyAsmdef(state);
                state.AsmdefReapplied = true;
                state.Save();
            }

            // 3. Creator scripts parked in step 2 (follow the Community Modules folder if it was moved for the update)
            RestoreParked(state);

            // 3b. The creator's scripts that use renamed classes (CharacterTrigger, NetworkFloat)
            if (state.RenameInCreatorScripts && !state.RenamesApplied && state.CreatorScriptsToRename.Count > 0)
            {
                var paths = new List<string>();
                foreach (string s in state.CreatorScriptsToRename)
                {
                    string now = MapThroughRootMove(state, s);
                    if (File.Exists(MigrationIO.ToAbsolute(now)))
                        paths.Add(now);
                }
                if (paths.Count > 0)
                {
                    BackupManager.Create(5, "Your scripts before class renames", paths);
                    foreach (string s in paths)
                        LegacyCommunityModules.ApplyRenames(s);
                }
                state.RenamesApplied = true;
                state.Save();
            }

            // 4. Other assemblies referencing assemblies that the Photon update removed or renamed
            if (!state.AsmdefReferencesFixed)
            {
                FixOtherAsmdefs();
                state.AsmdefReferencesFixed = true;
            }

            state.CmImported = true;
            state.PendingAction = null;
            state.Save();
            MigrationState.Log("Step 5 done: Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " imported.");
            AssetDatabase.Refresh();
        }

        static void RestoreParked(MigrationState state)
        {
            if (state.ParkedRestored)
                return;
            string cmAsmdef = MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid);
            string scriptsFolder = cmAsmdef != null ? Path.GetDirectoryName(cmAsmdef).Replace('\\', '/') : null;
            foreach (var p in state.Parked)
            {
                p.OriginalPath = MapThroughRootMove(state, p.OriginalPath);
                if (p.Retired && scriptsFolder != null)
                    p.OriginalPath = LegacyCommunityModules.RetiredFolder(scriptsFolder) + "/" + Path.GetFileName(p.OriginalPath);
                if (!File.Exists(MigrationIO.ToAbsolute(p.ParkedPath)))
                    continue;
                if (File.Exists(MigrationIO.ToAbsolute(p.OriginalPath)))
                {
                    MigrationState.LogWarning("Not restoring " + p.OriginalPath + ": a file already exists there. Your version is in " + p.ParkedPath);
                    continue;
                }
                MigrationIO.MoveFileWithMeta(p.ParkedPath, p.OriginalPath);
                if (p.Retired)
                    LegacyCommunityModules.ApplyRenames(p.OriginalPath); // Written against classes that were renamed since
            }
            state.ParkedRestored = true;
            state.Save();
        }

        /// <summary>
        ///     Step 5 without installing Community Modules (project that never had it): puts back the scripts set aside in
        ///     step 2 and fixes the assembly references to the removed Photon assemblies.
        /// </summary>
        public static void Skip()
        {
            var state = MigrationState.Current;
            RestoreParked(state);
            FixOtherAsmdefs();
            state.AsmdefReferencesFixed = true;
            state.CmSkipped = true;
            state.Save();
            MigrationState.Log("Step 5 skipped: Community Modules not installed, " + state.Parked.Count + " set-aside script(s) put back.");
            AssetDatabase.Refresh();
        }

        static void ReapplyAsmdef(MigrationState state)
        {
            string path = MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid);
            if (path != null && string.IsNullOrEmpty(state.CmAsmdefJson))
            {
                // The project had no Community Modules asmdef (deleted, or no Community Modules at all): keep the default one
                MigrationState.Log("No previous assembly definition to restore: '" + Path.GetFileName(path) + "' keeps its default name and namespace.");
                return;
            }
            if (path == null)
            {
                MigrationState.LogWarning("Could not re-apply the assembly name: the Community Modules asmdef is missing after the import.");
                return;
            }
            var imported = AsmdefData.Read(path);
            var saved = AsmdefData.Parse(state.CmAsmdefJson);
            foreach (string c in saved.FixPhotonReferences())
                MigrationState.Log("World asmdef reference " + c);

            // Keep everything the creator set, and make sure the references Community Modules 4.0 needs are there
            var refs = new List<string>(imported.references);
            foreach (string r in saved.references)
            {
                if (refs.Contains(r))
                    continue;
                if (r.StartsWith("GUID:") && MigrationIO.AssetPathFromGuid(r.Substring(5)) == null)
                {
                    MigrationState.LogWarning("Dropped reference to a missing assembly: " + r);
                    continue;
                }
                refs.Add(r);
            }
            saved.references = refs;
            if (string.IsNullOrEmpty(saved.name))
                saved.name = imported.name;
            if (string.IsNullOrEmpty(saved.rootNamespace))
                saved.rootNamespace = imported.rootNamespace;
            AsmdefData.Write(path, saved);

            string wantedFile = saved.name + ".asmdef";
            if (Path.GetFileName(path) != wantedFile)
            {
                string err = AssetDatabase.RenameAsset(path, saved.name);
                if (!string.IsNullOrEmpty(err))
                    MigrationState.LogWarning("Could not rename " + path + " to " + wantedFile + ": " + err);
            }
            MigrationState.Log($"World asmdef restored: name '{saved.name}', namespace '{saved.rootNamespace}', {refs.Count} references.");

            // Namespace of the Community Modules scripts (what the Namespace Helper did before)
            string ns = saved.rootNamespace;
            if (!string.IsNullOrEmpty(ns) && ns != MigrationConstants.CmDefaultAsmdefName)
            {
                string scriptsFolder = Path.GetDirectoryName(MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid)).Replace('\\', '/');
                var rx = new Regex(@"\bnamespace\s+" + MigrationConstants.CmDefaultAsmdefName + @"\b");
                int count = 0;
                foreach (string f in MigrationIO.ListFiles(scriptsFolder))
                {
                    if (!f.EndsWith(".cs") || KnownFiles.Instance.PathInTargetCm(MigrationIO.ReadMetaGuid(f)) == null)
                        continue;
                    string abs = MigrationIO.ToAbsolute(f);
                    string text = File.ReadAllText(abs);
                    string updated = rx.Replace(text, "namespace " + ns);
                    if (updated != text)
                    {
                        File.WriteAllText(abs, updated);
                        count++;
                    }
                }
                MigrationState.Log($"Namespace '{ns}' applied to {count} Community Modules scripts.");
            }
        }

        public static List<string> FixOtherAsmdefs()
        {
            var report = new List<string>();
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!f.EndsWith(".asmdef") || f.StartsWith(MigrationConstants.PhotonRoot + "/") || f.StartsWith(tool + "/"))
                    continue;
                try
                {
                    var d = AsmdefData.Read(f);
                    var changes = d.FixPhotonReferences();
                    if (changes.Count == 0)
                        continue;
                    AsmdefData.Write(f, d);
                    string line = f + ": " + string.Join(", ", changes);
                    report.Add(line);
                    MigrationState.Log("Fixed asmdef references in " + line);
                }
                catch (Exception e)
                {
                    MigrationState.LogWarning("Could not check " + f + ": " + e.Message);
                }
            }
            return report;
        }

        /// <summary>Optional: put moved Community Modules files back where the creator had them.</summary>
        public static void MoveBack()
        {
            var state = MigrationState.Current;
            foreach (string pair in state.MovedForImport)
            {
                string[] p = pair.Split('|');
                if (p.Length != 2)
                    continue;
                EnsureFolder(Path.GetDirectoryName(p[0]).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(p[1], p[0]);
                if (!string.IsNullOrEmpty(err))
                    MigrationState.LogWarning("Could not move back " + p[1] + ": " + err);
            }
            state.MovedForImport.Clear();
            state.Save();
        }
    }
}
