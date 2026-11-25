using System.Collections.Generic;
using System.Text;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Runs the migration steps without the window, for testing (Unity CLI eval or a batch method).
    ///     Same code paths as the buttons; returns a short report per step.
    /// </summary>
    public static class MigrationAutomation
    {
        /// <summary>
        ///     The whole migration without the window, for coding agents and scripts. Run it on a closed project:
        ///     <c>Unity.exe -batchmode -ignorecompilererrors -projectPath &lt;project&gt; -executeMethod Somnium.MigrationTool.MigrationAutomation.RunUnattended
        ///     [-migrationTask Full|PhotonOnly|CmOnly] [-sdkPackage &lt;Somnium SDK .unitypackage&gt;] [-migrationTimeout &lt;minutes&gt;]
        ///     -logFile &lt;file&gt;</c> (without -quit: Unity quits by itself with the exit code). Exit codes and the summary line:
        ///     see UnattendedRun and the migration guide.
        /// </summary>
        public static void RunUnattended() => UnattendedRun.Begin();

        public static string Step1Analyse()
        {
            var a = ProjectAnalyzer.Run();
            var sb = new StringBuilder();
            sb.Append("found=").Append(a.CmFound).Append(" hasAsmdef=").Append(a.CmHasAsmdef).Append(" asmdef=").Append(a.CmAsmdef?.name).Append(" ns=").Append(a.CmAsmdef?.rootNamespace)
                .Append(" root=").Append(a.CmRoot).Append(" version=").Append(a.BestVersion).Append(" fusion=").Append(a.FusionVersion);
            foreach (FileCategory c in System.Enum.GetValues(typeof(FileCategory)))
                sb.Append(" ").Append(c).Append("=").Append(a.Count(c));
            sb.Append(" outsidePhoton=").Append(a.ScriptsOutsideUsingPhoton.Count);
            if (a.Legacy != null)
            {
                sb.Append(" retiredUsages=").Append(a.Legacy.RetiredUsages.Count).Append(" oldFloat=").Append(a.Legacy.OldFloatComponents)
                    .Append(" floatFiles=").Append(a.Legacy.FilesWithOldFloatFields.Count).Append(" renameScripts=").Append(a.Legacy.ScriptsUsingRenamedClasses.Count);
                foreach (var u in a.Legacy.RetiredUsages) sb.Append("\nRETIRED ").Append(u.ScriptName).Append(": ").Append(u.GameObjectName).Append(" in ").Append(u.AssetPath);
            }
            foreach (string n in a.Notes) sb.Append("\nNOTE ").Append(n);
            foreach (string w in a.Warnings) sb.Append("\nWARN ").Append(w);
            foreach (string e in a.Errors) sb.Append("\nERROR ").Append(e);
            if (a.Errors.Count == 0)
            {
                a.SaveTo(MigrationState.Current);
                MigrationState.Current.CurrentStep = 2;
                MigrationState.Current.Save();
            }
            return sb.ToString();
        }

        /// <param name="mode">Smart, FullReset, Choose or Nothing.</param>
        public static string Step2Cleanup(string mode = "Smart", bool parkOutside = true, bool keepRetired = true)
        {
            var a = ProjectAnalyzer.Run();
            var m = (CleanupMode)System.Enum.Parse(typeof(CleanupMode), mode);
            var plan = CleanupStep.BuildPlan(a, m, new List<string>(), parkOutside, keepRetired);
            CleanupStep.Execute(a, plan, m);
            return $"cleanup {m}: delete={plan.Delete.Count} park={plan.Park.Count} keepRetired={plan.KeepRetired.Count} keep={plan.Keep.Count} done={MigrationState.Current.CleanupDone}";
        }

        public static string Step3DeletePhoton(bool keepUnknownFiles = true)
        {
            var plan = PhotonStep.BuildDeletePlan();
            PhotonStep.ExecuteDelete(plan, keepUnknownFiles);
            return $"photon delete: folders={plan.FoldersToDelete.Count} settingsKept={plan.SettingsToKeep.Count} unknown={plan.UnknownFiles.Count} " +
                $"upm={plan.UpmPackages.Count} other={string.Join(",", plan.OtherFoldersLeft)} done={MigrationState.Current.PhotonDeleted}";
        }

        public static string Step4ImportPhoton()
        {
            PhotonStep.ExecuteImport();
            return "photon import requested";
        }

        public static string Step5ImportCommunityModules(string keepMyVersionCsv = "", bool ignoreWarnings = false)
        {
            var plan = CommunityModulesStep.BuildPlan();
            if (plan.Problems.Count > 0 || (!ignoreWarnings && plan.Warnings.Count > 0))
                return "blocked: " + string.Join(" | ", plan.Problems) + " " + string.Join(" | ", plan.Warnings);
            var keep = new List<string>();
            foreach (string s in keepMyVersionCsv.Split(','))
                if (s.Trim().Length > 0) keep.Add(s.Trim());
            CommunityModulesStep.ExecuteImport(plan, keep);
            return $"cm import requested: rootMove={plan.RootMoveFrom ?? "-"} moves={plan.Moves.Count} asides={plan.Asides.Count} modifiedAssets={plan.ModifiedAssets.Count} keep={keep.Count}";
        }

        public static string Step5Skip()
        {
            CommunityModulesStep.Skip();
            return "cm skipped: parked=" + MigrationState.Current.Parked.Count;
        }

        public static string Step6ScanProject()
        {
            var sb = new StringBuilder();
            var found = NetworkObjectConverter.ScanProject();
            sb.Append("found=").Append(found.Count);
            foreach (var f in found)
                sb.Append("\n").Append(f.AssetPath).Append(" #").Append(f.FileId).Append(" ").Append(f.GameObjectName);
            return sb.ToString();
        }

        public static string Step6ConvertProject()
        {
            var paths = new List<string>();
            foreach (var f in NetworkObjectConverter.ScanProject())
                paths.Add(f.AssetPath);
            var files = NetworkObjectConverter.OrderForConversion(paths);
            int n = 0;
            foreach (string f in files)
                n += NetworkObjectConverter.Fix(f, null);
            return "converted=" + n + " in " + files.Count + " files: " + string.Join(", ", files);
        }

        public static string Step6RecommendedSettings()
        {
            int n = NetworkObjectConverter.ApplyRecommendedSettingsEverywhere();
            return "recommended settings applied to " + n + " component(s)";
        }

        /// <summary>Every NetworkTransform still missing a recommended setting, read through the API (the file may not hold the fields).</summary>
        public static string NetworkTransformsNeedingSettings()
        {
            var sb = new StringBuilder();
            int bad = 0, total = 0;
            foreach (string f in NetworkObjectConverter.FilesWithNetworkTransforms())
            {
                if (f.EndsWith(".prefab"))
                {
                    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(f))
                        if (o is UnityEngine.Component c && c.GetType().Name == "NetworkTransform") { total++; if (NetworkObjectConverter.NeedsRecommendedSettings(c)) { bad++; sb.Append("\n").Append(f).Append(" ").Append(c.name); } }
                }
                else
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(f, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    foreach (var go in scene.GetRootGameObjects())
                        foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true))
                            if (c != null && c.GetType().Name == "NetworkTransform") { total++; if (NetworkObjectConverter.NeedsRecommendedSettings(c)) { bad++; sb.Append("\n").Append(f).Append(" ").Append(c.name); } }
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
            return "networkTransforms=" + total + " needingSettings=" + bad + sb;
        }

        public static string MoveCommunityModulesBack()
        {
            int n = MigrationState.Current.MovedForImport.Count;
            CommunityModulesStep.MoveBack();
            return "moved back " + n + " entries";
        }

        /// <summary>Restore the newest backups first, down to and including the backup of <paramref name="step"/>.</summary>
        public static string RestoreBackupsDownToStep(int step)
        {
            var state = MigrationState.Current;
            var sb = new StringBuilder();
            for (int i = state.Backups.Count - 1; i >= 0; i--)
            {
                var b = state.Backups[i];
                if (b.Restored || b.Step < step)
                    continue;
                BackupManager.Restore(b);
                state.InvalidateFromStep(b.Step);
                sb.Append("restored ").Append(b.Id).Append("; ");
            }
            return sb.ToString();
        }

        public static string Status()
        {
            var s = MigrationState.Current;
            return $"step={s.CurrentStep} analysed={s.Analysed} cleanup={s.CleanupDone}({s.CleanupMode}) photonDeleted={s.PhotonDeleted} " +
                $"photonImported={s.PhotonImported} cmImported={s.CmImported} cmSkipped={s.CmSkipped} cmPresent={s.CmPresent} asmdef={s.AsmdefReapplied} parked={s.Parked.Count} restored={s.ParkedRestored} " +
                $"refsFixed={s.AsmdefReferencesFixed} pending={s.PendingAction} converted={s.ConvertedComponents} compileErrors={s.LastCompileErrors.Count} backups={s.Backups.Count}";
        }
    }
}
