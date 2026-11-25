using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     The migration without the window, for coding agents and scripts: started once with
    ///     <c>-executeMethod Somnium.MigrationTool.MigrationAutomation.RunUnattended</c> in batch mode, it runs every step of
    ///     the chosen task, waits for the imports and compiles in between (the task is kept in the saved state, so it goes on
    ///     after each domain reload), checks the result and quits Unity with an exit code.
    ///     It only ever runs in batch mode: an Editor opened normally never continues an unattended run.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnattendedRun
    {
        public const int ExitSuccess = 0;
        public const int ExitStepFailed = 1;      // A step failed or threw (see the log)
        public const int ExitPrerequisite = 2;    // Somnium SDK too old, wrong task, CM-only without Fusion 2.1...
        public const int ExitCompileErrors = 3;   // Migration done but the project does not compile
        public const int ExitTimeout = 4;
        public const int ExitSettingsMissing = 5; // NetworkTransforms still without the recommended settings

        public const string ResultFileName = "unattended_result.txt";

        const double SettleSeconds = 4.0; // Lets Unity start the refresh/compile an action triggers before the next check

        static double _nextTick;
        static bool _busy;

        static MigrationState State => MigrationState.Current;

        static UnattendedRun()
        {
            // Resume after a domain reload only in the process started for it (the command line survives the reload), so a
            // run left unfinished never takes over another batch job on this project
            if (Application.isBatchMode && StartedForUnattendedRun() && MigrationState.Exists &&
                !string.IsNullOrEmpty(MigrationState.Current.UnattendedTask))
                Start(SettleSeconds);
        }

        static bool StartedForUnattendedRun() => Array.Exists(Environment.GetCommandLineArgs(), a => a.EndsWith(".RunUnattended"));

        /// <summary>Entry point (see MigrationAutomation.RunUnattended). Reads the command line, then runs asynchronously.</summary>
        public static void Begin()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogError("[Migration] RunUnattended only runs in batch mode (Unity.exe -batchmode ... -executeMethod ...). Use the window instead.");
                return;
            }
            string task = Argument("-migrationTask") ?? "Full";
            if (task != "Full" && task != "PhotonOnly" && task != "CmOnly")
            {
                Finish(ExitPrerequisite, "Unknown -migrationTask '" + task + "'. Use Full, PhotonOnly or CmOnly.");
                return;
            }
            var state = State;
            state.UnattendedTask = task;
            state.UnattendedStartedUtc = DateTime.UtcNow.Ticks.ToString();
            state.UnattendedSdkPackage = Argument("-sdkPackage");
            state.UnattendedSdkImportRequested = false;
            state.UnattendedConversionDone = false;
            if (int.TryParse(Argument("-migrationTimeout"), out int minutes) && minutes > 0)
                state.UnattendedTimeoutMinutes = minutes;
            state.Save();
            DeleteResultFile();
            Log("Started: task " + task + (string.IsNullOrEmpty(state.UnattendedSdkPackage) ? "" : ", SDK package " + state.UnattendedSdkPackage) +
                ", timeout " + state.UnattendedTimeoutMinutes + " min.");
            Start(0);
        }

        static void Start(double delay)
        {
            _nextTick = EditorApplication.timeSinceStartup + delay;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (_busy || EditorApplication.timeSinceStartup < _nextTick)
                return;
            var state = State;
            if (string.IsNullOrEmpty(state.UnattendedTask))
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (TimedOut(state))
            {
                Finish(ExitTimeout, "Timed out after " + state.UnattendedTimeoutMinutes + " min (step " + DescribeProgress(state) + ").");
                return;
            }
            // Wait while Unity works or a step waits for its import (MigrationResume finishes it after the domain reload)
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(state.PendingAction))
                return;

            _busy = true;
            try
            {
                Advance(state);
            }
            catch (Exception e)
            {
                MigrationState.LogError(e.ToString());
                Finish(ExitStepFailed, "Unexpected error: " + e.Message);
            }
            finally
            {
                _busy = false;
                _nextTick = EditorApplication.timeSinceStartup + SettleSeconds;
            }
        }

        /// <summary>Runs the next step that is not done yet. Every step checks the saved state, so a reload never repeats one.</summary>
        static void Advance(MigrationState state)
        {
            string task = state.UnattendedTask;
            bool photon = task == "Full" || task == "PhotonOnly";
            bool cm = task == "Full" || task == "CmOnly";

            // ---- Prerequisites, before anything is changed
            if (cm && !state.CmImported && !SdkIsRecentEnough())
            {
                if (string.IsNullOrEmpty(state.UnattendedSdkPackage))
                {
                    Finish(ExitPrerequisite, "The Somnium SDK in this project is too old for Community Modules " + MigrationConstants.TargetCommunityModulesVersion +
                        ". Update the Somnium SDK first (or pass its package with -sdkPackage <path>), then run again. Nothing was changed.");
                    return;
                }
                if (state.UnattendedSdkImportRequested)
                {
                    Finish(ExitPrerequisite, "The Somnium SDK is still too old after importing " + state.UnattendedSdkPackage +
                        ". Check that it is the latest Somnium SDK package.");
                    return;
                }
                if (!File.Exists(state.UnattendedSdkPackage))
                {
                    Finish(ExitPrerequisite, "-sdkPackage file not found: " + state.UnattendedSdkPackage);
                    return;
                }
                state.UnattendedSdkImportRequested = true;
                state.Save();
                Log("Importing the Somnium SDK package " + state.UnattendedSdkPackage);
                AssetDatabase.ImportPackage(state.UnattendedSdkPackage, false);
                return;
            }
            if (task == "CmOnly" && !MigrationIO.IsFusion21Installed())
            {
                Finish(ExitPrerequisite, "Photon Fusion " + MigrationConstants.TargetFusionVersion + " is not installed: run the task Full (or PhotonOnly first).");
                return;
            }
            if (task == "PhotonOnly" && !state.PhotonImported && OldCommunityModulesPresent())
            {
                Finish(ExitPrerequisite, "Community Modules made for the old Photon is in this project: Photon only would leave it unable to compile. " +
                    "Run the task Full instead. Nothing was changed.");
                return;
            }

            // ---- Step 1: find the world code (also needed by Photon only: it records the settings step 5 would restore)
            if (!state.Analysed)
            {
                var a = ProjectAnalyzer.Run();
                if (a.Errors.Count > 0)
                {
                    Finish(ExitStepFailed, "Step 1: " + string.Join(" ", a.Errors));
                    return;
                }
                a.SaveTo(state);
                foreach (string w in a.Warnings)
                    Log("Step 1 warning: " + w);
                Log("Step 1 done: " + (a.CmFound ? "Community Modules " + KnownFiles.PrettyVersion(a.BestVersion ?? "?") + ", assembly '" + a.CmAsmdef?.name + "'" : "no Community Modules") +
                    ", Fusion " + a.FusionVersion + ".");
                return;
            }

            // ---- Step 2: old Community Modules scripts (Smart), creator scripts set aside
            if (cm && !state.CleanupDone)
            {
                var a = ProjectAnalyzer.Run();
                var plan = CleanupStep.BuildPlan(a, CleanupMode.Smart, new List<string>(), true, true);
                var mode = a.CmFound || plan.Park.Count > 0 ? CleanupMode.Smart : CleanupMode.Nothing;
                foreach (string f in plan.RetiredNeedingVoice)
                    Log("Step 2 warning: " + f + " needs Photon Voice, which is not installed anymore: removed, its objects get a missing script.");
                CleanupStep.Execute(a, plan, mode);
                if (!state.CleanupDone)
                {
                    Finish(ExitStepFailed, "Step 2 did not complete (see the log).");
                    return;
                }
                return;
            }

            // ---- Steps 3 and 4: Photon
            if (photon && !state.PhotonImported)
            {
                if (!state.PhotonDeleted)
                {
                    PhotonStep.ExecuteDelete(PhotonStep.BuildDeletePlan(), true);
                    if (!state.PhotonDeleted)
                        Finish(ExitStepFailed, "Step 3 did not complete (see the log).");
                    return;
                }
                if (state.PhotonImportRequested)
                {
                    // The import finished (nothing pending) without Fusion 2.1 on disk
                    Finish(ExitStepFailed, "Step 4: the Photon import did not install Fusion " + MigrationConstants.TargetFusionVersion + " (see the log).");
                    return;
                }
                PhotonStep.ExecuteImport();
                return;
            }

            // ---- Step 5: Community Modules 4.0, or put the set-aside scripts back in a project without it
            if (cm && !state.CmImported && !state.CmSkipped)
            {
                if (!state.CmPresent)
                {
                    CommunityModulesStep.Skip();
                    return;
                }
                if (state.CmImportRequested)
                {
                    Finish(ExitStepFailed, "Step 5: the Community Modules import did not complete (see the log).");
                    return;
                }
                var plan = CommunityModulesStep.BuildPlan();
                if (plan.Problems.Count > 0 || plan.Warnings.Count > 0)
                {
                    var all = new List<string>(plan.Problems);
                    all.AddRange(plan.Warnings);
                    Finish(ExitStepFailed, "Step 5: " + string.Join(" ", all));
                    return;
                }
                CommunityModulesStep.ExecuteImport(plan, new List<string>());
                return;
            }

            // ---- Step 6: convert NetworkRigidbody3D, then the recommended NetworkTransform settings
            if (!state.UnattendedConversionDone)
            {
                if (!MigrationIO.IsFusion21Installed())
                {
                    Finish(ExitStepFailed, "Step 6: Photon Fusion " + MigrationConstants.TargetFusionVersion + " is not installed.");
                    return;
                }
                var found = NetworkObjectConverter.ScanProject();
                var paths = new List<string>();
                foreach (var f in found)
                    paths.Add(f.AssetPath);
                int converted = 0;
                foreach (string f in NetworkObjectConverter.OrderForConversion(paths))
                    converted += NetworkObjectConverter.Fix(f, null);
                int settings = NetworkObjectConverter.ApplyRecommendedSettingsEverywhere();
                NetworkObjectConverter.RebuildPrefabTable(); // Tools > Fusion > Rebuild Prefab Table, as the window's last step
                state.ProjectScanClean = found.Count == 0;
                state.UnattendedConversionDone = true;
                state.Save();
                Log("Step 6 done: " + converted + " NetworkRigidbody3D converted, recommended settings applied to " + settings +
                    " NetworkTransform(s), Fusion prefab table rebuilt.");
                return;
            }

            Verify(state);
        }

        /// <summary>Final checks once Unity is idle: compile errors, then every NetworkTransform's settings.</summary>
        static void Verify(MigrationState state)
        {
            if (EditorUtility.scriptCompilationFailed || state.LastCompileErrors.Count > 0)
            {
                int n = Math.Max(state.LastCompileErrors.Count, 1);
                string first = state.LastCompileErrors.Count > 0 ? " First: " + state.LastCompileErrors[0] : "";
                Finish(ExitCompileErrors, "Migration steps done, but the project has " + n + " compile error(s)." + first);
                return;
            }
            int missing = CountNetworkTransformsNeedingSettings(out int total);
            if (missing > 0)
            {
                Finish(ExitSettingsMissing, missing + " of " + total + " NetworkTransform(s) still lack the recommended settings.");
                return;
            }
            var missingScripts = FindMissingScripts();
            string missingText = "0 missing scripts";
            if (missingScripts.Count > 0)
            {
                foreach (string m in missingScripts)
                    Log("Missing script left in the project: " + m);
                missingText = missingScripts.Count + " missing script(s) left in the project (not fixed by the migration; remove those components): " +
                    string.Join("; ", missingScripts.GetRange(0, Math.Min(5, missingScripts.Count))) + (missingScripts.Count > 5 ? "; ..." : "");
            }
            Finish(ExitSuccess, "Task " + state.UnattendedTask + " done: 0 compile errors, " + total + " NetworkTransform(s) checked, " +
                state.ConvertedComponents + " converted, " + state.Parked.Count + " of your scripts set aside and put back, " + missingText + ".");
        }

        static readonly System.Text.RegularExpressions.Regex ScriptRef =
            new System.Text.RegularExpressions.Regex(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: 3\}");

        /// <summary>
        ///     Components whose script no longer exists, in every scene and prefab of the world ("file (script GUID)").
        ///     Reported, not a failure: some worlds had them before the migration.
        /// </summary>
        static List<string> FindMissingScripts()
        {
            var result = new List<string>();
            string tool = ProjectAnalyzer.ToolRoot;
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!(f.EndsWith(".unity") || f.EndsWith(".prefab")) || f.StartsWith(tool + "/"))
                    continue;
                var seen = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in ScriptRef.Matches(File.ReadAllText(MigrationIO.ToAbsolute(f))))
                {
                    string guid = m.Groups[1].Value;
                    if (seen.Add(guid) && MigrationIO.AssetPathFromGuid(guid) == null)
                        result.Add(f + " (script " + guid + ")");
                }
            }
            return result;
        }

        static int CountNetworkTransformsNeedingSettings(out int total)
        {
            total = 0;
            int missing = 0;
            foreach (string f in NetworkObjectConverter.FilesWithNetworkTransforms())
            {
                if (f.EndsWith(".prefab"))
                {
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(f))
                    {
                        if (o is Component c && c.GetType().Name == "NetworkTransform")
                        {
                            total++;
                            if (NetworkObjectConverter.NeedsRecommendedSettings(c))
                                missing++;
                        }
                    }
                }
                else
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(f, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        foreach (var c in go.GetComponentsInChildren<Component>(true))
                        {
                            if (c != null && c.GetType().Name == "NetworkTransform")
                            {
                                total++;
                                if (NetworkObjectConverter.NeedsRecommendedSettings(c))
                                    missing++;
                            }
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
            return missing;
        }

        // ------------------------------------------------------------------ Somnium SDK check

        /// <summary>
        ///     True when the Somnium SDK has the API Community Modules 4.0 uses. Checked by feature, never by version number:
        ///     older SDKs have the players container without the avatar and outfit events, and no PoseType.
        /// </summary>
        public static bool SdkIsRecentEnough()
        {
            Assembly bridge = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "SomniumSpace.Bridge")
                {
                    bridge = asm;
                    break;
                }
            }
            if (bridge == null)
                return false;
            Type container = null;
            bool poseType = false;
            Type[] types;
            try { types = bridge.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            foreach (var t in types)
            {
                if (t.Name == "ISomniumPlayersContainer")
                    container = t;
                else if (t.Name == "PoseType")
                    poseType = true;
            }
            return container != null && poseType &&
                container.GetEvent("OnPlayerAvatarChanged") != null && container.GetEvent("OnPlayerOutfitPutOn") != null;
        }

        static bool OldCommunityModulesPresent()
        {
            bool cm = MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid) != null ||
                MigrationIO.AssetPathFromGuid(MigrationConstants.NetworkGrabbableGuid) != null;
            return cm && !CommunityModulesStep.IsTargetVersionImported();
        }

        // ------------------------------------------------------------------ helpers

        static bool TimedOut(MigrationState state)
        {
            if (!long.TryParse(state.UnattendedStartedUtc, out long ticks))
                return false;
            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMinutes > state.UnattendedTimeoutMinutes;
        }

        static string DescribeProgress(MigrationState s) =>
            $"analysed={s.Analysed} cleanup={s.CleanupDone} photonDeleted={s.PhotonDeleted} photonImported={s.PhotonImported} " +
            $"cm={s.CmImported || s.CmSkipped} converted={s.UnattendedConversionDone} pending={s.PendingAction}";

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        static void Log(string message) => MigrationState.Log("[Unattended] " + message);

        static string ResultPath => MigrationState.WorkFolder + "/" + ResultFileName;

        static void DeleteResultFile()
        {
            if (File.Exists(ResultPath))
                File.Delete(ResultPath);
        }

        /// <summary>Writes the summary line and the result file, ends the unattended run and quits Unity with the exit code.</summary>
        static void Finish(int exitCode, string message)
        {
            var state = State;
            string task = string.IsNullOrEmpty(state.UnattendedTask) ? "?" : state.UnattendedTask;
            string line = "UNATTENDED RESULT: " + (exitCode == ExitSuccess ? "SUCCESS" : "FAILED") + " (exit code " + exitCode + ", task " + task + "). " + message;
            if (exitCode == ExitSuccess)
                MigrationState.Log(line);
            else
                MigrationState.LogError(line);
            try
            {
                Directory.CreateDirectory(MigrationState.WorkFolder);
                File.WriteAllText(ResultPath, "exitCode=" + exitCode + Environment.NewLine + "task=" + task + Environment.NewLine + "message=" + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // The log line and the exit code are enough
            }
            state.UnattendedTask = null;
            state.Save();
            EditorApplication.update -= Tick;
            EditorApplication.Exit(exitCode);
        }
    }
}
