using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Somnium.MigrationTool
{
    internal enum CleanupMode { Smart, FullReset, Choose, Nothing }

    /// <summary>
    ///     Step 2: remove the old Community Modules scripts before Photon changes, so the project never has to compile old
    ///     Community Modules code against the new Photon.
    ///     Original scripts are always deleted: the Community Modules 4.0 import brings them back with the same GUIDs, so
    ///     every scene and prefab reference is kept. Creator scripts are "parked" outside Assets until step 5 (so they do not
    ///     break compilation while Community Modules is missing), or deleted when the creator asks for it.
    /// </summary>
    internal static class CleanupStep
    {
        public class Plan
        {
            public readonly List<string> Delete = new List<string>();
            public readonly List<string> Park = new List<string>();
            public readonly List<string> Keep = new List<string>();
            public readonly List<string> KeepRetired = new List<string>(); // Parked too, restored into the _Retired folder
            public readonly List<string> RetiredNeedingVoice = new List<string>(); // Used but not kept: Photon Voice is gone
        }

        /// <summary>
        ///     Older Community Modules scripts that use Photon Voice (ARSConnectRemotePlayerAudioSource in 2.x) cannot be kept:
        ///     Photon Voice is not installed anymore, and one script that does not compile breaks the whole world assembly.
        /// </summary>
        static bool UsesPhotonVoice(string path)
        {
            try { return File.ReadAllText(MigrationIO.ToAbsolute(path)).Contains("Photon.Voice"); }
            catch (IOException) { return false; }
        }

        /// <summary>Top-level folders that contain creator scripts (for the "Choose" mode).</summary>
        public static List<string> FoldersWithCreatorCode(ProjectAnalyzer a)
        {
            var folders = new List<string>();
            foreach (var f in a.Of(FileCategory.CreatorCode))
            {
                string folder = TopFolder(a.CmRoot, f.Path);
                if (!folders.Contains(folder))
                    folders.Add(folder);
            }
            folders.Sort(StringComparer.Ordinal);
            return folders;
        }

        /// <summary>Folder directly under Community Modules/Scripts (or under Community Modules) that holds the file.</summary>
        public static string TopFolder(string cmRoot, string path)
        {
            string rel = path.Substring(cmRoot.Length + 1);
            string[] parts = rel.Split('/');
            if (parts.Length >= 3 && parts[0] == "Scripts")
                return cmRoot + "/Scripts/" + parts[1];
            return parts.Length >= 2 ? cmRoot + "/" + parts[0] : cmRoot;
        }

        public static Plan BuildPlan(ProjectAnalyzer a, CleanupMode mode, ICollection<string> foldersToDelete, bool parkOutsideScripts,
            bool keepUsedRetired = true)
        {
            var plan = new Plan();
            if (mode == CleanupMode.Nothing)
                return plan;

            foreach (var f in a.Files)
            {
                if (keepUsedRetired && a.Legacy != null && (f.Category == FileCategory.OriginalCode || f.Category == FileCategory.ModifiedOriginalCode) &&
                    a.Legacy.UsedRetiredGuids.Contains(f.Guid))
                {
                    if (UsesPhotonVoice(f.Path))
                    {
                        plan.RetiredNeedingVoice.Add(f.Path);
                        plan.Delete.Add(f.Path);
                    }
                    else
                    {
                        plan.KeepRetired.Add(f.Path);
                    }
                    continue;
                }
                switch (f.Category)
                {
                    case FileCategory.OriginalCode:
                    case FileCategory.ModifiedOriginalCode:
                    case FileCategory.CmAsmdef:
                    case FileCategory.NamespaceBackup:
                        plan.Delete.Add(f.Path);
                        break;
                    case FileCategory.CreatorCode:
                        bool delete = mode == CleanupMode.FullReset ||
                            (mode == CleanupMode.Choose && foldersToDelete != null && foldersToDelete.Contains(TopFolder(a.CmRoot, f.Path)));
                        (delete ? plan.Delete : plan.Park).Add(f.Path);
                        break;
                    default:
                        plan.Keep.Add(f.Path); // Prefabs, materials, scenes...: updated in place by the import in step 5
                        break;
                }
            }
            foreach (string f in a.KnownCodeOutsideRoot)
            {
                bool usedRetired = keepUsedRetired && a.Legacy != null && a.Legacy.UsedRetiredGuids.Contains(MigrationIO.ReadMetaGuid(f));
                if (usedRetired && UsesPhotonVoice(f))
                {
                    plan.RetiredNeedingVoice.Add(f);
                    usedRetired = false;
                }
                (usedRetired ? plan.KeepRetired : plan.Delete).Add(f);
            }
            if (parkOutsideScripts)
                plan.Park.AddRange(a.ScriptsOutsideUsingPhoton);
            return plan;
        }

        public static void Execute(ProjectAnalyzer a, Plan plan, CleanupMode mode)
        {
            var state = MigrationState.Current;
            if (!MigrationIO.CloseScenesSafely(state))
                return;

            // Remember which original assets the creator changed, before Photon 2.1 re-saves network prefabs
            state.KeepMyVersion.Clear();
            state.ModifiedOriginalAssets = new List<string>();
            foreach (var f in a.Of(FileCategory.ModifiedOriginalAsset))
                state.ModifiedOriginalAssets.Add(f.Path);

            if (mode == CleanupMode.Nothing)
            {
                state.CleanupMode = mode.ToString();
                state.CleanupDone = true;
                state.Save();
                MigrationState.Log("Step 2: creator chose to delete nothing.");
                return;
            }

            // Run again (advanced mode) after Community Modules was installed: step 5 must run again to bring the scripts back
            if (plan.Delete.Count > 0 || plan.Park.Count > 0)
                state.ResetCommunityModulesImport();

            var scopes = new List<string>();
            if (a.CmRoot != null)
                scopes.Add(a.CmRoot);
            foreach (string p in plan.Park)
            {
                if (a.CmRoot == null || !p.StartsWith(a.CmRoot + "/"))
                    scopes.Add(p);
            }
            scopes.AddRange(a.KnownCodeOutsideRoot);
            if (scopes.Count > 0)
                BackupManager.Create(2, "Old Community Modules scripts", scopes);

            // Park creator scripts outside Assets (with their .meta, so GUIDs and references survive)
            foreach (string p in plan.Park)
            {
                string parked = MigrationConstants.WorkFolderName + "/Parked/" + p;
                MigrationIO.MoveFileWithMeta(p, parked);
                state.Parked.Add(new ParkedFile { OriginalPath = p, ParkedPath = parked });
            }
            foreach (string p in plan.KeepRetired)
            {
                string parked = MigrationConstants.WorkFolderName + "/Parked/" + p;
                MigrationIO.MoveFileWithMeta(p, parked);
                state.Parked.Add(new ParkedFile { OriginalPath = p, ParkedPath = parked, Retired = true });
            }
            state.KeepUsedRetiredScripts = plan.KeepRetired.Count > 0;

            // Delete through the AssetDatabase so Unity stays consistent
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(plan.Delete.ToArray(), failed);
            foreach (string f in failed)
                MigrationState.LogWarning("Could not delete " + f);

            state.DeletedFiles.AddRange(plan.Delete);
            if (a.CmRoot != null)
                MigrationIO.DeleteEmptyFolders(a.CmRoot + "/Scripts");
            state.CleanupMode = mode.ToString();
            state.CleanupDone = true;
            state.Save();
            MigrationState.Log($"Step 2 done ({mode}): {plan.Delete.Count} deleted, {plan.Park.Count} parked, {plan.KeepRetired.Count} retired scripts kept.");
            AssetDatabase.Refresh();
        }
    }
}
