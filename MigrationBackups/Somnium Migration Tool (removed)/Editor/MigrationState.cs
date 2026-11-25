using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Somnium.MigrationTool
{
    [Serializable]
    internal class BackupRecord
    {
        public string Id;          // Folder name under MigrationBackups
        public string Label;       // Human readable
        public string CreatedLocal;
        public int Step;
        public List<string> Scopes = new List<string>();      // Project-relative folders/files replaced entirely on restore
        public List<string> MissingScopes = new List<string>(); // Scopes that did not exist at backup time (deleted on restore)
        public bool Restored;
    }

    [Serializable]
    internal class ParkedFile
    {
        public string OriginalPath; // Project-relative, e.g. Assets/#User/Community Modules/Scripts/MyScripts/Foo.cs
        public string ParkedPath;   // Project-relative, under MigrationBackups/Parked
        public bool Retired;        // Original script of an older release, kept because the world uses it
    }

    /// <summary>
    ///     Everything the wizard needs to remember between steps. Saved as JSON in MigrationBackups/ (outside Assets),
    ///     so it survives domain reloads, package imports and even a deleted Library folder.
    /// </summary>
    [Serializable]
    internal class MigrationState
    {
        public int FormatVersion = 1;
        public string ToolVersion = MigrationConstants.ToolVersion;
        public string StartedLocal;
        public int CurrentStep;
        public bool AdvancedMode;   // Any step at any time, plus the quick tasks (see MigrationWindow)
        public int AdvancedPage;    // Page shown in advanced mode (steps, then quick tasks)
        public List<string> OpenScenesBefore = new List<string>();

        // Step 1 - code base
        public bool Analysed;
        public bool CmPresent = true;    // False when the project has no Community Modules at all
        public string CmAsmdefPath;
        public string CmAsmdefName;
        public string CmRootNamespace;
        public string CmAsmdefJson;      // Full original asmdef, re-applied after the Community Modules update
        public string CmRoot;            // Folder that contains Scripts/, Plugins/, Prefabs/ ...
        public string DetectedCmVersion;
        public string FusionVersionBefore;
        public List<string> OtherAsmdefsReferencingCm = new List<string>();
        public List<string> AnalysisWarnings = new List<string>();

        // Step 2 - old scripts
        public string CleanupMode;       // Smart | FullReset | Choose | Nothing
        public bool CleanupDone;
        public List<ParkedFile> Parked = new List<ParkedFile>();
        public List<string> DeletedFiles = new List<string>();
        public List<string> ModifiedOriginalAssets = new List<string>(); // Snapshot taken before Photon re-saves prefabs
        public bool KeepUsedRetiredScripts = true;   // Keep scripts from older releases that the world still uses
        public List<string> RetiredUsages = new List<string>(); // "Script: object in asset" lines, for the reports

        // Older releases: renamed classes and NetworkFloat values
        public bool UpdateFloatValues = true;
        public bool RenameInCreatorScripts = true;
        public List<string> CreatorScriptsToRename = new List<string>();
        public int OldFloatComponents;
        public bool FloatValuesUpdated;
        public bool RenamesApplied;

        // Step 3 - old Photon
        public bool PhotonDeleted;
        public List<string> PhotonSettingsKept = new List<string>();

        // Step 4 - Photon 2.1
        public bool DefinesUpdated;
        public List<string> DefinesBefore = new List<string>(); // "TargetName=defines", restored with the step 4 backup
        public bool PhotonImportRequested;
        public bool PhotonImported;

        // Step 5 - Community Modules 4.0
        public bool CmMovedToCanonical;
        public string CmOriginalRootBeforeMove;
        public bool CmImportRequested;
        public bool CmImported;
        public bool CmSkipped;           // Project without Community Modules: the creator chose not to install it
        public bool AsmdefReapplied;
        public bool ParkedRestored;
        public bool AsmdefReferencesFixed;
        public List<string> KeepMyVersion = new List<string>(); // Modified original assets the creator wants to keep
        public string KeepFolder;                                // Where the kept copies wait during the import
        public List<string> MovedForImport = new List<string>(); // "from|to" moves done so the import updates files in place

        // Step 6 - conversion
        public int ConvertedComponents;
        public bool ProjectScanClean; // Whole-project scan found nothing to convert
        public bool NetworkTransformSettingsDone; // Forecast off on every NetworkTransform + no State Authority interpolation on Kinematic grabs

        // Unattended run (see UnattendedRun): survives the domain reloads of the package imports
        public string UnattendedTask;            // Full | PhotonOnly | CmOnly, empty when no unattended run is active
        public string UnattendedStartedUtc;      // DateTime ticks, for the timeout and the summary
        public int UnattendedTimeoutMinutes = 90;
        public string UnattendedSdkPackage;      // Optional Somnium SDK package to import when the SDK is too old
        public bool UnattendedSdkImportRequested;
        public bool UnattendedConversionDone;

        // Bookkeeping
        public string PendingAction;     // Continued after a domain reload (see MigrationResume)
        public List<BackupRecord> Backups = new List<BackupRecord>();
        public List<string> LastCompileErrors = new List<string>();

        static MigrationState _current;

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        public static string WorkFolder => ProjectRoot + "/" + MigrationConstants.WorkFolderName;
        static string StatePath => WorkFolder + "/migration_state.json";
        static string LogPath => WorkFolder + "/migration_log.txt";

        public static MigrationState Current
        {
            get
            {
                if (_current == null)
                    _current = Load();
                return _current;
            }
        }

        public static bool Exists => File.Exists(StatePath);

        static MigrationState Load()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var state = JsonUtility.FromJson<MigrationState>(File.ReadAllText(StatePath));
                    if (state != null)
                        return state;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Migration] Could not read the saved migration state, starting fresh: " + e.Message);
            }
            return new MigrationState { StartedLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        }

        public void Save()
        {
            Directory.CreateDirectory(WorkFolder);
            File.WriteAllText(StatePath, JsonUtility.ToJson(this, true));
        }

        /// <summary>After restoring the backup of <paramref name="step"/>, that step and the later ones are no longer done.</summary>
        public void InvalidateFromStep(int step)
        {
            if (step <= 2) { CleanupDone = false; Parked.Clear(); DeletedFiles.Clear(); }
            if (step <= 3) { PhotonDeleted = false; PhotonSettingsKept.Clear(); }
            if (step <= 4) { PhotonImported = false; PhotonImportRequested = false; }
            if (step <= 5)
            {
                CmImported = false; CmSkipped = false; CmImportRequested = false; AsmdefReapplied = false; ParkedRestored = false;
                AsmdefReferencesFixed = false; MovedForImport.Clear(); FloatValuesUpdated = false; RenamesApplied = false;
            }
            PendingAction = null;
            CurrentStep = Math.Min(CurrentStep, step);
            Save();
        }

        /// <summary>
        ///     Step 5 has to run (again) after its results were replaced, for example when step 2 runs again in advanced mode.
        ///     Parked files already put back are forgotten; the ones still parked stay listed so step 5 restores them.
        /// </summary>
        public void ResetCommunityModulesImport()
        {
            CmImported = false; CmSkipped = false; CmImportRequested = false; AsmdefReapplied = false; ParkedRestored = false;
            AsmdefReferencesFixed = false;
            Parked.RemoveAll(p => !File.Exists(Path.Combine(ProjectRoot, p.ParkedPath)));
        }

        /// <summary>Forget the current migration (backups stay on disk).</summary>
        public static void Reset()
        {
            if (File.Exists(StatePath))
                File.Move(StatePath, StatePath.Replace(".json", "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            _current = null;
        }

        public static void Log(string message)
        {
            Debug.Log("[Migration] " + message);
            AppendLog("INFO  " + message);
        }

        public static void LogWarning(string message)
        {
            Debug.LogWarning("[Migration] " + message);
            AppendLog("WARN  " + message);
        }

        public static void LogError(string message)
        {
            Debug.LogError("[Migration] " + message);
            AppendLog("ERROR " + message);
        }

        static void AppendLog(string line)
        {
            try
            {
                Directory.CreateDirectory(WorkFolder);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never break the migration
            }
        }

        public static string LogFilePath => LogPath;
    }
}
