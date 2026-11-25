using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Package imports trigger compilation and a domain reload, which wipes every in-memory callback. Actions that must
    ///     run after an import are stored in MigrationState.PendingAction and continued here: from the import callback when
    ///     it fires, or on the next editor load. Each continuation checks the files on disk first, so running twice is safe.
    /// </summary>
    [InitializeOnLoad]
    internal static class MigrationResume
    {
        public const string AfterPhotonImport = "AfterPhotonImport";
        public const string AfterCmImport = "AfterCmImport";

        static readonly List<string> CompileErrors = new List<string>();

        static MigrationResume()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageFailed += OnImportFailed;
            AssetDatabase.importPackageCancelled += OnImportCancelled;
            CompilationPipeline.compilationStarted += _ => CompileErrors.Clear();
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += _ => StoreCompileErrors();

            if (MigrationState.Exists && !string.IsNullOrEmpty(MigrationState.Current.PendingAction))
                Schedule();
        }

        static void OnImportCompleted(string packageName)
        {
            if (MigrationState.Exists && !string.IsNullOrEmpty(MigrationState.Current.PendingAction))
            {
                MigrationState.Log("Package imported: " + packageName);
                Schedule();
            }
        }

        static void OnImportFailed(string packageName, string error)
        {
            if (!MigrationState.Exists || string.IsNullOrEmpty(MigrationState.Current.PendingAction))
                return;
            MigrationState.LogError("Package import failed: " + packageName + ": " + error);
            MigrationState.Current.PendingAction = null;
            MigrationState.Current.Save();
            EditorUtility.DisplayDialog("Migration", "The package import failed:\n\n" + error + "\n\nYou can restore the last backup from the Backups tab.", "OK");
        }

        static void OnImportCancelled(string packageName)
        {
            if (!MigrationState.Exists || string.IsNullOrEmpty(MigrationState.Current.PendingAction))
                return;
            MigrationState.LogWarning("Package import cancelled: " + packageName);
            MigrationState.Current.PendingAction = null;
            MigrationState.Current.Save();
        }

        // Polling, not a single check: changing scripting defines (step 4) triggers its own compile and domain reload while
        // the import may still be running, which wipes the import callback. So the continuation keeps checking the files
        // on disk until the import result is there.
        const double PollSeconds = 2.0;
        const double GiveUpSeconds = 900.0;
        static double _nextPoll;
        static double _giveUpAt;

        /// <summary>Restart the continuation if something is pending (called when the window gets focus).</summary>
        public static void EnsureRunning()
        {
            if (MigrationState.Exists && !string.IsNullOrEmpty(MigrationState.Current.PendingAction))
                Schedule();
        }

        static void Schedule()
        {
            _giveUpAt = EditorApplication.timeSinceStartup + GiveUpSeconds;
            _nextPoll = 0;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!MigrationState.Exists || string.IsNullOrEmpty(MigrationState.Current.PendingAction) || Continue())
            {
                EditorApplication.update -= Poll;
                return;
            }
            if (EditorApplication.timeSinceStartup > _giveUpAt)
            {
                EditorApplication.update -= Poll;
                MigrationState.LogWarning("Still waiting for '" + MigrationState.Current.PendingAction + "' after " + GiveUpSeconds +
                    " s. If Unity is idle, the import may have failed: check the Console, or restore the last backup.");
            }
        }

        /// <summary>Returns true when the pending action is finished (or nothing is pending).</summary>
        static bool Continue()
        {
            var state = MigrationState.Current;
            switch (state.PendingAction)
            {
                case AfterPhotonImport:
                    if (!MigrationIO.IsFusion21Installed())
                        return false; // Import still running
                    state.PhotonImported = true;
                    state.PendingAction = null;
                    state.Save();
                    // Assemblies that referenced removed or renamed Photon assemblies (also needed when only Photon is updated)
                    CommunityModulesStep.FixOtherAsmdefs();
                    foreach (string p in PhotonStep.VerifyImport())
                        MigrationState.LogWarning(p);
                    MigrationState.Log("Step 4 done: Photon Fusion " + MigrationIO.ReadFusionVersion() + " installed.");
                    MigrationWindow.RepaintIfOpen();
                    return true;
                case AfterCmImport:
                    if (!CommunityModulesStep.IsTargetVersionImported())
                        return false; // Import still running
                    CommunityModulesStep.AfterImport();
                    MigrationWindow.RepaintIfOpen();
                    return true;
            }
            return true;
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            foreach (var m in messages)
            {
                if (m.type == CompilerMessageType.Error)
                    CompileErrors.Add(m.message);
            }
        }

        static void StoreCompileErrors()
        {
            if (!MigrationState.Exists)
                return;
            var state = MigrationState.Current;
            state.LastCompileErrors = new List<string>(CompileErrors);
            state.Save();
            MigrationWindow.RepaintIfOpen();
        }
    }
}
