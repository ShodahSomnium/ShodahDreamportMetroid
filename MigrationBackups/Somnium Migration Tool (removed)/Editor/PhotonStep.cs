using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Somnium.MigrationTool
{
    /// <summary>Step 3 (delete the old Photon, Photon Voice included) and step 4 (import Photon Fusion 2.1, without Photon Voice).</summary>
    internal static class PhotonStep
    {
        public const string KeptFilesFolder = "Assets/Photon Migration - Kept Files";

        public class DeletePlan
        {
            public readonly List<string> FoldersToDelete = new List<string>();
            public readonly List<string> FilesToDelete = new List<string>();
            public readonly List<string> SettingsToKeep = new List<string>();
            public readonly List<string> UnknownFiles = new List<string>();   // Not a known Photon file: maybe the creator's
            public readonly List<string> OtherFoldersLeft = new List<string>(); // Assets/Photon/<x> we do not know
            public readonly List<string> UpmPackages = new List<string>();
            public long Size;
        }

        public static DeletePlan BuildDeletePlan()
        {
            var plan = new DeletePlan();
            var known = KnownFiles.Instance;
            string root = MigrationConstants.PhotonRoot;
            string absRoot = MigrationIO.ToAbsolute(root);
            if (Directory.Exists(absRoot))
            {
                foreach (string d in Directory.GetDirectories(absRoot))
                {
                    string rel = root + "/" + Path.GetFileName(d);
                    if (Array.IndexOf(MigrationConstants.PhotonFoldersToDelete, Path.GetFileName(d)) >= 0)
                    {
                        plan.FoldersToDelete.Add(rel);
                        plan.Size += MigrationIO.FolderSize(rel);
                        foreach (string f in MigrationIO.ListFiles(rel))
                        {
                            if (Array.IndexOf(MigrationConstants.PhotonSettingsToKeep, f) >= 0)
                                plan.SettingsToKeep.Add(f);
                            else if (!known.IsKnownPhoton(MigrationIO.ReadMetaGuid(f)) && !IsPhotonGeneratedFile(f))
                                plan.UnknownFiles.Add(f);
                        }
                    }
                    else
                    {
                        plan.OtherFoldersLeft.Add(rel);
                    }
                }
                foreach (string f in MigrationConstants.PhotonLooseFilesToDelete)
                {
                    if (File.Exists(absRoot + "/" + f))
                        plan.FilesToDelete.Add(root + "/" + f);
                }
            }
            string manifest = MigrationIO.ToAbsolute("Packages/manifest.json");
            if (File.Exists(manifest))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(manifest), "\"(com\\.photonengine\\.[^\"]+)\""))
                    plan.UpmPackages.Add(m.Groups[1].Value);
            }
            return plan;
        }

        /// <summary>Files Photon creates itself in a project (settings, caches): not creator content.</summary>
        static bool IsPhotonGeneratedFile(string path)
        {
            string name = Path.GetFileName(path);
            return name.EndsWith(".fusion") || name == "PhotonAppSettings.asset" || name == "VoiceAppSettings.asset" ||
                name == "PhotonServerSettings.asset" || name.StartsWith("Fusion.CodeGen.Trigger") || path.Contains("/Fusion/User/");
        }

        public static void ExecuteDelete(DeletePlan plan, bool keepUnknownFiles)
        {
            var state = MigrationState.Current;
            if (!MigrationIO.CloseScenesSafely(state))
                return;

            var scopes = new List<string>();
            scopes.AddRange(plan.FoldersToDelete);
            scopes.AddRange(plan.FilesToDelete);
            scopes.Add("Packages/manifest.json");
            if (keepUnknownFiles && plan.UnknownFiles.Count > 0)
                scopes.Add(KeptFilesFolder);
            var backup = BackupManager.Create(3, "Old Photon", scopes);

            // 1. Save the creator settings that live inside the Photon folders (with their folder .meta files)
            string keepFolder = MigrationState.WorkFolder + "/" + backup.Id + "/settings";
            state.PhotonSettingsKept.Clear();
            foreach (string f in plan.SettingsToKeep)
            {
                CopyWithMeta(f, keepFolder + "/" + f);
                string dir = Path.GetDirectoryName(f).Replace('\\', '/');
                while (dir.Length > MigrationConstants.PhotonRoot.Length)
                {
                    if (File.Exists(MigrationIO.ToAbsolute(dir) + ".meta"))
                        CopyFile(MigrationIO.ToAbsolute(dir) + ".meta", keepFolder + "/" + dir + ".meta");
                    dir = Path.GetDirectoryName(dir).Replace('\\', '/');
                }
                state.PhotonSettingsKept.Add(f);
            }

            // 2. Move unknown files out of the way (they could be the creator's)
            if (keepUnknownFiles)
            {
                foreach (string f in plan.UnknownFiles)
                {
                    string target = KeptFilesFolder + "/" + f.Substring(MigrationConstants.PhotonRoot.Length + 1);
                    MigrationIO.MoveFileWithMeta(f, target);
                }
            }

            // 3. Delete
            var paths = new List<string>(plan.FoldersToDelete);
            paths.AddRange(plan.FilesToDelete);
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(paths.ToArray(), failed);
            foreach (string f in failed)
            {
                MigrationState.LogWarning("AssetDatabase could not delete " + f + ", deleting from disk.");
                string abs = MigrationIO.ToAbsolute(f);
                if (Directory.Exists(abs)) Directory.Delete(abs, true);
                else if (File.Exists(abs)) File.Delete(abs);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }

            // 4. Put the settings back right away, before anything can create new ones with other GUIDs
            foreach (string f in state.PhotonSettingsKept)
                CopyWithMeta(keepFolder + "/" + f, MigrationIO.ToAbsolute(f), true);
            foreach (string meta in Directory.Exists(keepFolder) ? Directory.GetFiles(keepFolder, "*.meta", SearchOption.AllDirectories) : new string[0])
            {
                string rel = meta.Replace('\\', '/').Substring(keepFolder.Length + 1);
                string folder = MigrationIO.ToAbsolute(rel.Substring(0, rel.Length - 5));
                if (Directory.Exists(folder) && !File.Exists(folder + ".meta"))
                    File.Copy(meta, folder + ".meta");
            }

            // 5. Photon packages installed with the Package Manager
            if (plan.UpmPackages.Count > 0)
                RemoveUpmPackages(plan.UpmPackages);

            state.PhotonDeleted = true;
            state.PhotonImported = false; // Run again (advanced mode) after step 4: Photon must be installed again
            state.Save();
            MigrationState.Log($"Step 3 done: deleted {paths.Count} Photon folders/files, kept {state.PhotonSettingsKept.Count} settings files, " +
                $"moved {(keepUnknownFiles ? plan.UnknownFiles.Count : 0)} unknown files, removed {plan.UpmPackages.Count} UPM packages.");
            AssetDatabase.Refresh();
        }

        static void CopyWithMeta(string from, string to, bool fromIsAbsolute = false)
        {
            string absFrom = fromIsAbsolute ? from : MigrationIO.ToAbsolute(from);
            if (fromIsAbsolute)
            {
                // from is a kept copy, to is the project path
                CopyFile(absFrom, to);
                if (File.Exists(absFrom + ".meta"))
                    CopyFile(absFrom + ".meta", to + ".meta");
                return;
            }
            CopyFile(absFrom, to);
            if (File.Exists(absFrom + ".meta"))
                CopyFile(absFrom + ".meta", to + ".meta");
        }

        static void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        static void RemoveUpmPackages(List<string> packages)
        {
            string manifest = MigrationIO.ToAbsolute("Packages/manifest.json");
            var lines = new List<string>(File.ReadAllLines(manifest));
            lines.RemoveAll(l => Regex.IsMatch(l, "\"com\\.photonengine\\.[^\"]+\"\\s*:"));
            string json = string.Join("\n", lines);
            json = Regex.Replace(json, ",(\\s*)}", "$1}"); // Trailing comma left by a removed last entry
            File.WriteAllText(manifest, json + "\n");
            Client.Resolve();
            MigrationState.Log("Removed from Packages/manifest.json: " + string.Join(", ", packages));
        }

        // ------------------------------------------------------------------ step 4

        public static string PackagePath => MigrationIO.AssetPathFromGuid(MigrationConstants.PhotonPackageGuid);

        /// <summary>A Photon older than 2.1 is in the project (installing 2.1 over it would mix both versions).</summary>
        public static bool OldPhotonPresent =>
            Directory.Exists(MigrationIO.ToAbsolute(MigrationConstants.PhotonRoot + "/Fusion/Assemblies")) && !MigrationIO.IsFusion21Installed();

        public static void ExecuteImport()
        {
            var state = MigrationState.Current;
            string pkg = PackagePath;
            if (pkg == null)
            {
                EditorUtility.DisplayDialog("Migration", "The Photon package of the migration tool is missing. Please re-import the migration tool.", "OK");
                return;
            }
            if (!MigrationIO.CloseScenesSafely(state))
                return;

            // Scripting defines are recorded in the state (Unity keeps Player Settings in memory, a file copy would not restore them)
            BackupManager.Create(4, "Before Photon 2.1 import", new[] { MigrationConstants.PhotonRoot });

            // Defines first, as Fusion's installer would set them: Fusion can only add them itself after a successful compile.
            int changed = MigrationIO.ApplyFusion21Defines();
            state.DefinesUpdated = true;
            MigrationState.Log("Fusion 2.1 scripting defines applied on " + changed + " build targets.");

            state.PhotonImportRequested = true;
            state.PhotonImported = false;
            state.PendingAction = MigrationResume.AfterPhotonImport;
            state.Save();
            MigrationState.Log("Importing " + pkg);
            AssetDatabase.ImportPackage(pkg, false);
        }

        /// <summary>Checks after the import (also run after the domain reload).</summary>
        public static List<string> VerifyImport()
        {
            var problems = new List<string>();
            var state = MigrationState.Current;
            if (!MigrationIO.IsFusion21Installed())
                problems.Add("Fusion 2.1 was not found in Assets/Photon/Fusion (version: " + MigrationIO.ReadFusionVersion() + ").");
            string config = MigrationIO.AssetPathFromGuid(MigrationConstants.NetworkProjectConfigGuid);
            if (state.PhotonSettingsKept.Contains("Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion") && config == null)
                problems.Add("Your NetworkProjectConfig was not found after the import.");
            var configs = new List<string>();
            foreach (string f in MigrationIO.ListFiles(MigrationConstants.PhotonRoot))
            {
                if (f.EndsWith(".fusion") && Path.GetFileName(f).StartsWith("NetworkProjectConfig"))
                    configs.Add(f);
            }
            if (configs.Count > 1)
                problems.Add("More than one NetworkProjectConfig exists: " + string.Join(", ", configs) + ". Keep only " + config + ".");
            return problems;
        }
    }
}
