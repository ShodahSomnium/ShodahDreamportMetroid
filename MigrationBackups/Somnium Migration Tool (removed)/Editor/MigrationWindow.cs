using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Somnium.MigrationTool
{
    /// <summary>The migration wizard. Every change happens on a clearly labelled button, after a confirmation dialog.</summary>
    internal class MigrationWindow : EditorWindow
    {
        static readonly string[] StepTitles =
        {
            "0. Before you start",
            "1. Find your world code",
            "2. Old scripts",
            "3. Remove old Photon",
            "4. Install Photon 2.1",
            "5. Update Community Modules",
            "6. Convert network objects",
        };

        // Advanced mode pages after the steps: shortcuts that combine the steps a partial update needs
        const int PhotonTaskPage = 7;
        const int CmTaskPage = 8;
        static readonly string[] TaskTitles = { "Install Photon 2.1 only", "Update Community Modules only" };

        enum Tab { Migration, Backups, Log }

        Tab _tab;
        Vector2 _scroll;
        bool _iHaveABackup;
        Action _pending; // Runs after OnGUI, so a click never changes the layout mid-frame

        // Step data (rebuilt when needed, never saved)
        ProjectAnalyzer _analysis;
        CleanupMode _mode = CleanupMode.Smart;
        readonly HashSet<string> _foldersToDelete = new HashSet<string>();
        bool _parkOutside = true;
        bool _keepRetired = true;
        PhotonStep.DeletePlan _photonPlan;
        bool _keepUnknownPhotonFiles = true;
        CommunityModulesStep.ImportPlan _cmPlan;
        readonly HashSet<string> _keepMine = new HashSet<string>();
        List<NetworkObjectConverter.Found> _found;
        string _foundSource;
        readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        GUIStyle _wrap, _title, _stepButton, _stepButtonCurrent;

        [MenuItem("Somnium SDK/Fusion 2.1 Migration", false, 1000)]
        public static void Open()
        {
            var w = GetWindow<MigrationWindow>(false, "Fusion 2.1 Migration", true);
            w.minSize = new Vector2(760, 560);
            w.Show();
        }

        public static void RepaintIfOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<MigrationWindow>())
                w.Repaint();
        }

        MigrationState State => MigrationState.Current;

        void OnFocus() => MigrationResume.EnsureRunning();

        void OnGUI()
        {
            InitStyles();
            DrawToolbar();
            switch (_tab)
            {
                case Tab.Migration: DrawMigration(); break;
                case Tab.Backups: DrawBackups(); break;
                case Tab.Log: DrawLog(); break;
            }

            if (_pending != null)
            {
                var action = _pending;
                _pending = null;
                EditorApplication.delayCall += () =>
                {
                    try { action(); }
                    catch (Exception e)
                    {
                        MigrationState.LogError(e.ToString());
                        EditorUtility.DisplayDialog("Migration error", e.Message + "\n\nNothing else was changed after this error. See the Log tab, and restore a backup if needed.", "OK");
                    }
                    Repaint();
                };
            }
        }

        void InitStyles()
        {
            if (_wrap != null)
                return;
            _wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            _stepButton = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, fixedHeight = 24 };
            _stepButtonCurrent = new GUIStyle(_stepButton) { fontStyle = FontStyle.Bold };
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Migration", "Backups (" + State.Backups.Count + ")", "Log" }, EditorStyles.toolbarButton, GUILayout.Width(330));
            if (GUILayout.Button("Migration guide", EditorStyles.toolbarButton, GUILayout.Width(110)))
                _pending = OpenGuide;
            GUILayout.FlexibleSpace();
            GUILayout.Label("Tool " + MigrationConstants.ToolVersion + "  ·  Fusion " + MigrationConstants.TargetFusionVersion +
                "  ·  Community Modules " + MigrationConstants.TargetCommunityModulesVersion, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Opens the creator guide shipped with the tool (Markdown, in the app Windows or macOS uses for .md files).</summary>
        static void OpenGuide()
        {
            string path = MigrationIO.AssetPathFromGuid(MigrationConstants.GuideGuid);
            if (path == null)
            {
                EditorUtility.DisplayDialog("Migration guide", "The migration guide (Fusion_2.1_Migration_Guide.md) is missing. Please re-import the migration tool.", "OK");
                return;
            }
            EditorUtility.OpenWithDefaultApp(MigrationIO.ToAbsolute(path));
        }

        // ====================================================================== migration tab

        bool IsDone(int step)
        {
            switch (step)
            {
                case 0: return State.Analysed || _iHaveABackup;
                case 1: return State.Analysed;
                case 2: return State.CleanupDone;
                case 3: return State.PhotonDeleted;
                case 4: return State.PhotonImported;
                case 5: return State.CmImported || State.CmSkipped;
                case 6: return (State.ConvertedComponents > 0 || State.ProjectScanClean) && State.NetworkTransformSettingsDone;
            }
            return false;
        }

        bool Advanced => State.AdvancedMode;

        /// <summary>Page shown: the wizard step, or in advanced mode any step or quick task.</summary>
        int Page => Advanced ? State.AdvancedPage : State.CurrentStep;

        void DrawMigration()
        {
            EditorGUILayout.BeginHorizontal();

            // Step list
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            GUILayout.Space(6);
            if (Advanced)
            {
                GUILayout.Label("Quick tasks", EditorStyles.boldLabel);
                for (int t = 0; t < TaskTitles.Length; t++)
                    DrawPageButton(PhotonTaskPage + t, (Page == PhotonTaskPage + t ? "▶ " : "    ") + TaskTitles[t], true);
                GUILayout.Space(6);
                GUILayout.Label("All steps", EditorStyles.boldLabel);
            }
            for (int i = 0; i < StepTitles.Length; i++)
            {
                bool reachable = Advanced || i == 0 || IsDone(i - 1) || IsDone(i);
                string mark = IsDone(i) ? "✔ " : (i == Page ? "▶ " : "    ");
                DrawPageButton(i, mark + StepTitles[i], reachable);
            }
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(State.PendingAction))
                EditorGUILayout.HelpBox("Working... Unity is importing and compiling. The next part continues by itself.", MessageType.Info);
            if (GUILayout.Button(Advanced ? "◀ Back to the wizard" : "Advanced mode...", GUILayout.Height(22)))
                _pending = ToggleAdvanced;
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            // Step content
            int page = Page;
            EditorGUILayout.BeginVertical();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Label(page < StepTitles.Length ? StepTitles[page] : TaskTitles[page - PhotonTaskPage], _title);
            GUILayout.Space(4);
            if (Advanced)
                EditorGUILayout.HelpBox("Advanced mode: every step is available at any time, in any order, and can run again. The wizard order is " +
                    "what keeps your project compiling and your references intact: read the warnings on each page before you act.", MessageType.Warning);
            if (page >= 2 && page != 6)
                DrawExpectedConsoleNoise();
            switch (page)
            {
                case 0: DrawStep0(); break;
                case 1: DrawStep1(); break;
                case 2: DrawStep2(); break;
                case 3: DrawStep3(); break;
                case 4: DrawStep4(); break;
                case 5: DrawStep5(); break;
                case 6: DrawStep6(); break;
                case PhotonTaskPage: DrawPhotonTask(); break;
                case CmTaskPage: DrawCmTask(); break;
            }
            EditorGUILayout.EndScrollView();

            // Navigation (steps only; the quick tasks stand alone)
            if (page < StepTitles.Length)
            {
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(page == 0))
                {
                    if (GUILayout.Button("◀ Back", GUILayout.Width(100), GUILayout.Height(26)))
                        _pending = () => GoTo(page - 1);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(page >= StepTitles.Length - 1 || (!Advanced && !IsDone(page))))
                {
                    if (GUILayout.Button("Next ▶", GUILayout.Width(100), GUILayout.Height(26)))
                        _pending = () => GoTo(page + 1);
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        void DrawPageButton(int page, string label, bool enabled)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(label, page == Page ? _stepButtonCurrent : _stepButton))
                    _pending = () => GoTo(page);
            }
        }

        void ToggleAdvanced()
        {
            if (!Advanced)
            {
                if (!EditorUtility.DisplayDialog("Advanced mode",
                    "Advanced mode lets you run any step at any time, in any order, and run a step again. It also has quick tasks, " +
                    "for example installing only Photon 2.1, or updating only Community Modules.\n\n" +
                    "The wizard order is what keeps your project compiling and your references intact. Use advanced mode if you know " +
                    "what each step does, and keep a full copy of your project.", "Use advanced mode", "Cancel"))
                    return;
                State.AdvancedMode = true;
                State.AdvancedPage = State.CurrentStep;
            }
            else
            {
                State.AdvancedMode = false;
            }
            State.Save();
            GoTo(Page);
        }

        void GoTo(int page)
        {
            if (Advanced)
                State.AdvancedPage = Mathf.Clamp(page, 0, CmTaskPage);
            else
                State.CurrentStep = Mathf.Clamp(page, 0, StepTitles.Length - 1);
            page = Page;
            State.Save();
            _scroll = Vector2.zero;
            // Plans are rebuilt on entry, so they always reflect the files on disk now
            bool cleanup = page == 2 || page == CmTaskPage;
            bool cmImport = page == 5 || page == CmTaskPage;
            if (cleanup && _analysis == null) _analysis = ProjectAnalyzer.Run();
            if (page == 3 || page == PhotonTaskPage) _photonPlan = PhotonStep.BuildDeletePlan();
            if (cmImport && Advanced && !State.Analysed)
            {
                // Step 5 restores the world's asmdef and namespace from step 1: read them now (nothing is changed)
                if (_analysis == null) _analysis = ProjectAnalyzer.Run();
                SaveAnalysis(_analysis);
            }
            if (cmImport) _cmPlan = CommunityModulesStep.BuildPlan();
        }

        /// <summary>Community Modules scripts of an older release are in the project (they do not compile against Fusion 2.1).</summary>
        static bool OldCommunityModulesPresent()
        {
            bool cm = MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid) != null ||
                MigrationIO.AssetPathFromGuid(MigrationConstants.NetworkGrabbableGuid) != null;
            return cm && !CommunityModulesStep.IsTargetVersionImported();
        }

        /// <summary>
        ///     Shows the "done" box of a step. Returns true when the page stops there: always in the wizard; in advanced mode
        ///     the step can run again, so its options follow.
        /// </summary>
        bool DoneAndStop(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Info);
            if (!Advanced)
                return true;
            Text("<i>Advanced mode: you can run this step again.</i>");
            return false;
        }

        // ---------------------------------------------------------------------- step 0

        void DrawStep0()
        {
            Text("This tool updates your world project to <b>Photon Fusion " + MigrationConstants.TargetFusionVersion + "</b> and <b>Community Modules " +
                MigrationConstants.TargetCommunityModulesVersion + "</b>. Worlds built with the old Photon version do not sync correctly anymore and must be updated and uploaded again.");
            Text("What happens, step by step:");
            Bullet("1. The tool finds your world code (your Community Modules scripts and their assembly definition). Nothing is changed.");
            Bullet("2. The old Community Modules scripts are removed. Your own scripts are kept and set aside for a moment.");
            Bullet("3. The old Photon is removed. Your Photon settings are kept.");
            Bullet("4. Photon Fusion " + MigrationConstants.TargetFusionVersion + " is installed. Photon Voice is not installed again: Somnium worlds do not use it.");
            Bullet("5. Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " is installed. Your assembly name, namespace and scripts are restored.");
            Bullet("6. Old NetworkRigidbody3D components are converted to NetworkTransform in your scenes and prefabs.");
            Text("Your project has no Community Modules? The wizard works too: step 5 then only offers to install it.");
            Text("Only want Photon " + MigrationConstants.TargetFusionVersion + ", or only Community Modules " + MigrationConstants.TargetCommunityModulesVersion +
                "? Use <b>Advanced mode</b> (bottom left): it has a quick task for each and lets you run any step on its own.");
            GUILayout.Space(6);

            EditorGUILayout.HelpBox("This migration changes many files in your project. Before you start, make a FULL COPY of your project folder " +
                "(or commit everything if you use version control). The tool also makes a backup before every step " +
                "(see the Backups tab), but a full copy is the only way to be completely safe.", MessageType.Warning);
            EditorGUILayout.HelpBox("Some of your own prefabs or scripts may need manual fixes after the migration. If something looks broken, " +
                "stop, keep the log file, and ask a developer for help.", MessageType.Info);

            if (MigrationIO.IsGitProject())
                EditorGUILayout.HelpBox("This project uses git. Commit all your changes now, so you can return to this exact state.", MessageType.Info);
            if (!Application.unityVersion.StartsWith("6000.3"))
                EditorGUILayout.HelpBox("This tool was made and tested with Unity 6000.3; you are using " + Application.unityVersion +
                    ". Newer versions should work, details may differ.", MessageType.Info);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play Mode before migrating.", MessageType.Error);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open project folder", GUILayout.Width(180)))
                _pending = () => EditorUtility.RevealInFinder(Application.dataPath);
            if (GUILayout.Button("Open the migration guide", GUILayout.Width(180)))
                _pending = OpenGuide;
            GUILayout.Label("Backups and log: " + MigrationState.WorkFolder, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
            _iHaveABackup = EditorGUILayout.ToggleLeft(" I have a full copy (or commit) of my project", _iHaveABackup || State.Analysed, EditorStyles.boldLabel);
        }

        // ---------------------------------------------------------------------- step 1

        void DrawStep1()
        {
            Text("The tool looks for the assembly definition of your world scripts (the Community Modules one, even if you renamed it or moved it). " +
                "<b>Nothing is changed in this step.</b>");
            if (GUILayout.Button(_analysis == null ? "Scan project" : "Scan again", GUILayout.Width(160), GUILayout.Height(24)))
                _pending = () => _analysis = ProjectAnalyzer.Run();
            if (_analysis == null)
            {
                if (State.Analysed)
                    EditorGUILayout.HelpBox("Saved from the first scan: asmdef '" + State.CmAsmdefName + "', namespace '" + State.CmRootNamespace + "'.", MessageType.None);
                return;
            }

            var a = _analysis;
            foreach (string e in a.Errors)
                EditorGUILayout.HelpBox(e, MessageType.Error);
            foreach (string n in a.Notes)
                EditorGUILayout.HelpBox(n, MessageType.Info);
            if (!a.CmFound)
            {
                Row("Photon Fusion", a.FusionVersion);
                Row("Photon Voice / PUN", (a.VoiceInAssets ? "Voice" : "no Voice") + " / " + (a.PunInAssets ? "PUN" : "no PUN"));
                if (a.ScriptsOutsideUsingPhoton.Count > 0)
                    Foldout("outside", "Your scripts that use Photon (" + a.ScriptsOutsideUsingPhoton.Count + ")", () => List(a.ScriptsOutsideUsingPhoton));
            }
            if (a.CmFound)
            {
                Header("Your world code");
                Row("Assembly name", a.CmHasAsmdef ? a.CmAsmdef?.name : "none (scripts compile in Assembly-CSharp)");
                Row("Namespace", a.CmAsmdef?.rootNamespace);
                Row("Assembly definition", a.CmAsmdefPath);
                Row("Community Modules folder", a.CmRoot + (a.CmAtCanonicalPath ? "" : "   (not the standard location)"));
                Row("Community Modules version", a.BestVersion != null ? KnownFiles.PrettyVersion(a.BestVersion) + "   (closest match)" : "unknown");
                Row("Photon Fusion", a.FusionVersion);
                Row("Photon Voice / PUN", (a.VoiceInAssets ? "Voice" : "no Voice") + " / " + (a.PunInAssets ? "PUN" : "no PUN"));
                if (a.CmAsmdef != null)
                {
                    Foldout("refs", "Assembly references (" + a.CmAsmdef.references.Count + ")", () =>
                    {
                        foreach (string r in a.CmAsmdef.references)
                            GUILayout.Label("   " + AsmdefData.DescribeReference(r), EditorStyles.miniLabel);
                    });
                }
                Header("Files in Community Modules");
                Row("Original scripts", a.Count(FileCategory.OriginalCode).ToString());
                Row("Original scripts you edited", a.Count(FileCategory.ModifiedOriginalCode).ToString());
                Row("Your own scripts", a.Count(FileCategory.CreatorCode).ToString());
                Row("Original assets / edited / yours", a.Count(FileCategory.OriginalAsset) + " / " + a.Count(FileCategory.ModifiedOriginalAsset) + " / " + a.Count(FileCategory.CreatorAsset));
                if (a.OtherAsmdefsReferencingCm.Count > 0)
                    Foldout("otherasm", "Other assemblies that use your world code (" + a.OtherAsmdefsReferencingCm.Count + ")", () => List(a.OtherAsmdefsReferencingCm));
                if (a.ScriptsOutsideUsingPhoton.Count > 0)
                    Foldout("outside", "Scripts outside Community Modules that use Photon or your world code (" + a.ScriptsOutsideUsingPhoton.Count + ")", () => List(a.ScriptsOutsideUsingPhoton));
                if (a.Legacy != null)
                {
                    if (a.Legacy.RetiredUsages.Count > 0)
                        Foldout("retired", "Objects using scripts that Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " no longer includes (" + a.Legacy.RetiredUsages.Count + ")", () =>
                        {
                            foreach (var u in a.Legacy.RetiredUsages)
                                GUILayout.Label("   " + u.ScriptName + ":  " + u.GameObjectName + "  in  " + u.AssetPath, EditorStyles.miniLabel);
                        });
                    if (a.Legacy.ScriptsUsingRenamedClasses.Count > 0)
                        Foldout("renamed", "Your scripts using renamed classes (" + a.Legacy.ScriptsUsingRenamedClasses.Count + ")", () => List(a.Legacy.ScriptsUsingRenamedClasses));
                }
            }
            foreach (string w in a.Warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(a.Errors.Count > 0))
            {
                if (GUILayout.Button(a.CmFound ? "This is my world code: save and continue" : "Continue without Community Modules", GUILayout.Height(28)))
                    _pending = () => { a.SaveTo(State); GoTo(2); };
            }
            if (a.Errors.Count > 0)
                EditorGUILayout.HelpBox("The migration cannot continue safely. Please contact a developer and send the log file (Log tab).", MessageType.Error);
        }

        // ---------------------------------------------------------------------- step 2

        void DrawStep2()
        {
            if (State.CleanupDone && DoneAndStop("Done (" + State.CleanupMode + "): " + State.DeletedFiles.Count + " files removed, " + State.Parked.Count +
                    " of your scripts set aside until step 5."))
                return;
            if (_analysis == null)
            {
                if (GUILayout.Button("Scan project", GUILayout.Width(160))) _pending = () => _analysis = ProjectAnalyzer.Run();
                return;
            }
            var a = _analysis;
            if (Advanced && GUILayout.Button("Scan again", GUILayout.Width(160)))
                _pending = () => _analysis = ProjectAnalyzer.Run();
            if (!a.CmFound)
            {
                DrawStep2WithoutCommunityModules(a);
                return;
            }
            Text("The old Community Modules scripts must go before Photon is replaced. <b>Your scenes and prefabs keep working</b>: " +
                "Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " brings every script back with the same identity in step 5.");
            if (Advanced && CommunityModulesStep.IsTargetVersionImported())
                EditorGUILayout.HelpBox("Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " is installed. Running this removes its " +
                    "scripts again: install it again afterwards (step 5).", MessageType.Warning);

            DrawModeOption(CleanupMode.Smart, "Smart (recommended)",
                "Removes the original Community Modules scripts. Keeps your own scripts: they are set aside and come back in step 5.");
            DrawModeOption(CleanupMode.FullReset, "Full reset",
                "Removes every script in Community Modules, yours included (they stay in the backup). Recommended only if you have no scripts of your own.");
            DrawModeOption(CleanupMode.Choose, "Choose folders",
                "Like Smart, but you choose folders of your own scripts to remove as well.");
            if (_mode == CleanupMode.Choose)
            {
                foreach (string folder in CleanupStep.FoldersWithCreatorCode(a))
                {
                    bool on = _foldersToDelete.Contains(folder);
                    bool now = EditorGUILayout.ToggleLeft("   Remove " + folder, on);
                    if (now != on) { if (now) _foldersToDelete.Add(folder); else _foldersToDelete.Remove(folder); }
                }
            }
            DrawModeOption(CleanupMode.Nothing, "Do nothing",
                "Removes nothing. You handle conflicts yourself: expect compile errors until step 5.");

            if (a.ScriptsOutsideUsingPhoton.Count > 0 && _mode != CleanupMode.Nothing)
                _parkOutside = EditorGUILayout.ToggleLeft(" Also set aside the " + a.ScriptsOutsideUsingPhoton.Count +
                    " script(s) outside Community Modules that use Photon (restored in step 5)", _parkOutside);

            int retiredOnDisk = 0;
            if (a.Legacy != null)
                foreach (string g in a.Legacy.UsedRetiredGuids)
                    if (MigrationIO.AssetPathFromGuid(g) != null) retiredOnDisk++; // Already missing ones cannot be kept
            if (retiredOnDisk > 0 && _mode != CleanupMode.Nothing)
            {
                _keepRetired = EditorGUILayout.ToggleLeft(" Keep the " + retiredOnDisk + " older Community Modules script(s) that your objects still use (recommended)", _keepRetired);
                GUILayout.Label("They are not part of version " + MigrationConstants.TargetCommunityModulesVersion + ". Without them, the objects listed in step 1 get a missing script. " +
                    "They come back in 'Scripts/_Retired (older Community Modules)', updated for renamed classes.", EditorStyles.wordWrappedMiniLabel);
            }

            var plan = CleanupStep.BuildPlan(a, _mode, _foldersToDelete, _parkOutside, _keepRetired);
            Header("What will happen");
            Row("Files removed", plan.Delete.Count.ToString());
            Row("Your scripts set aside", plan.Park.Count.ToString());
            if (plan.KeepRetired.Count > 0)
                Row("Older scripts kept", plan.KeepRetired.Count.ToString());
            if (plan.RetiredNeedingVoice.Count > 0)
                EditorGUILayout.HelpBox(plan.RetiredNeedingVoice.Count + " older Community Modules script(s) your objects use need Photon Voice, which is " +
                    "not installed anymore: they are removed, and those objects get a missing script. Replace them after the migration: " +
                    string.Join(", ", plan.RetiredNeedingVoice.ConvertAll(Path.GetFileName)), MessageType.Warning);
            if (a.Count(FileCategory.ModifiedOriginalCode) > 0)
                EditorGUILayout.HelpBox(a.Count(FileCategory.ModifiedOriginalCode) + " original Community Modules script(s) were edited. Your edits are replaced by version " +
                    MigrationConstants.TargetCommunityModulesVersion + " (they stay in the backup).", MessageType.Warning);
            Foldout("del", "Files removed (" + plan.Delete.Count + ")", () => List(plan.Delete));
            Foldout("park", "Scripts set aside (" + plan.Park.Count + ")", () => List(plan.Park));

            GUILayout.Space(8);
            if (GUILayout.Button(_mode == CleanupMode.Nothing ? "Continue without removing anything" : "Remove old scripts", GUILayout.Height(28)))
            {
                var mode = _mode;
                _pending = () =>
                {
                    if (!EditorUtility.DisplayDialog("Remove old scripts",
                        $"{plan.Delete.Count} files will be removed and {plan.Park.Count} of your scripts set aside.\n\nA backup is made first. Continue?", "Remove", "Cancel"))
                        return;
                    // Step 5 restores the asmdef name and namespace saved here; in advanced mode step 1 may have been skipped
                    if (Advanced)
                        SaveAnalysis(a);
                    CleanupStep.Execute(a, plan, mode);
                    _cmPlan = null; // Rebuilt from the saved analysis
                    _analysis = null; // The files changed: scan again before running again
                };
            }
        }

        /// <summary>
        ///     Saves a fresh analysis for step 5, unless it would lose what an earlier one found: once step 2 removed the asmdef,
        ///     a new scan no longer sees the world's assembly name and namespace.
        /// </summary>
        void SaveAnalysis(ProjectAnalyzer a)
        {
            if (a.Errors.Count == 0 && (!State.Analysed || a.CmHasAsmdef))
                a.SaveTo(State);
        }

        /// <summary>Step 2 for a project without Community Modules: only the creator's scripts that use Photon can be set aside.</summary>
        void DrawStep2WithoutCommunityModules(ProjectAnalyzer a)
        {
            Text("This project has no Community Modules: there are no old Community Modules scripts to remove.");
            var plan = CleanupStep.BuildPlan(a, CleanupMode.Smart, _foldersToDelete, _parkOutside, _keepRetired);
            if (a.ScriptsOutsideUsingPhoton.Count > 0)
            {
                Text("Your scripts that use Photon may not compile while Photon is replaced. They can be set aside now and put back in step 5.");
                _parkOutside = EditorGUILayout.ToggleLeft(" Set aside the " + a.ScriptsOutsideUsingPhoton.Count + " script(s) that use Photon (put back in step 5)", _parkOutside);
                Foldout("park", "Scripts set aside (" + plan.Park.Count + ")", () => List(plan.Park));
            }
            GUILayout.Space(8);
            bool park = plan.Park.Count > 0;
            if (GUILayout.Button(park ? "Set aside my Photon scripts" : "Continue", GUILayout.Height(28)))
                _pending = () =>
                {
                    if (park && !EditorUtility.DisplayDialog("Set aside scripts", plan.Park.Count + " of your scripts are set aside until step 5. A backup is made first. Continue?", "Continue", "Cancel"))
                        return;
                    SaveAnalysis(a);
                    CleanupStep.Execute(a, plan, park ? CleanupMode.Smart : CleanupMode.Nothing);
                    _cmPlan = null;
                    _analysis = null;
                };
        }

        void DrawModeOption(CleanupMode mode, string label, string description)
        {
            if (GUILayout.Toggle(_mode == mode, " " + label, EditorStyles.radioButton) && _mode != mode)
                _mode = mode;
            GUILayout.Label(description, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(2);
        }

        // ---------------------------------------------------------------------- step 3

        void DrawStep3()
        {
            if (State.PhotonDeleted && DoneAndStop("Done: old Photon removed, " + State.PhotonSettingsKept.Count + " settings file(s) kept."))
                return;
            if (_photonPlan == null) _photonPlan = PhotonStep.BuildDeletePlan();
            var p = _photonPlan;
            Text("The old Photon folders are deleted. Your Photon settings (NetworkProjectConfig, App settings) are kept and put back right away.");
            if (p.FoldersToDelete.Count == 0 && p.FilesToDelete.Count == 0 && p.UpmPackages.Count == 0)
                EditorGUILayout.HelpBox("No Photon in this project: nothing to delete.", MessageType.Info);
            else if (MigrationIO.IsFusion21Installed())
                EditorGUILayout.HelpBox("Photon Fusion " + MigrationConstants.TargetFusionVersion + " is already installed. Deleting it is only useful to " +
                    "install it again cleanly in step 4 (your settings are kept).", MessageType.Warning);
            Foldout("ph_del", "Folders deleted (" + p.FoldersToDelete.Count + ", " + MigrationIO.FormatSize(p.Size) + ")", () => List(p.FoldersToDelete));
            Foldout("ph_keep", "Settings kept (" + p.SettingsToKeep.Count + ")", () => List(p.SettingsToKeep));
            if (p.UnknownFiles.Count > 0)
            {
                EditorGUILayout.HelpBox(p.UnknownFiles.Count + " file(s) in the Photon folders are not part of the Photon versions this tool knows. They may be yours.", MessageType.Warning);
                _keepUnknownPhotonFiles = EditorGUILayout.ToggleLeft(" Keep them in '" + PhotonStep.KeptFilesFolder + "'", _keepUnknownPhotonFiles);
                Foldout("ph_unknown", "Unknown files (" + p.UnknownFiles.Count + ")", () => List(p.UnknownFiles));
            }
            if (p.OtherFoldersLeft.Count > 0)
                Foldout("ph_other", "Folders in Assets/Photon left untouched (" + p.OtherFoldersLeft.Count + ")", () => List(p.OtherFoldersLeft));
            if (p.UpmPackages.Count > 0)
                EditorGUILayout.HelpBox("Package Manager entries removed from Packages/manifest.json: " + string.Join(", ", p.UpmPackages), MessageType.Info);
            DrawOldCommunityModulesWarning();

            GUILayout.Space(8);
            if (GUILayout.Button(MigrationIO.IsFusion21Installed() ? "Delete Photon" : "Delete old Photon", GUILayout.Height(28)))
            {
                bool keep = _keepUnknownPhotonFiles;
                _pending = () =>
                {
                    if (!EditorUtility.DisplayDialog("Delete old Photon", "The Photon folders are deleted (a backup is made first). Your settings are kept. Continue?", "Delete", "Cancel"))
                        return;
                    PhotonStep.ExecuteDelete(p, keep);
                    _photonPlan = PhotonStep.BuildDeletePlan();
                };
            }
        }

        void DrawOldCommunityModulesWarning()
        {
            if (OldCommunityModulesPresent())
                EditorGUILayout.HelpBox("Community Modules scripts made for the old Photon are in the project: with Photon " + MigrationConstants.TargetFusionVersion +
                    " they show compile errors until Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " is installed (step 5). " +
                    "In the wizard this is expected.", MessageType.Warning);
        }

        // ---------------------------------------------------------------------- step 4

        void DrawStep4()
        {
            if (State.PhotonImported)
            {
                EditorGUILayout.HelpBox("Done: Photon Fusion " + MigrationIO.ReadFusionVersion() + " installed.", MessageType.Info);
                foreach (string problem in PhotonStep.VerifyImport())
                    EditorGUILayout.HelpBox(problem, MessageType.Warning);
                DrawCompileErrors();
                if (!Advanced)
                    return;
                Text("<i>Advanced mode: you can run this step again.</i>");
            }
            Text("Installs Photon Fusion " + MigrationConstants.TargetFusionVersion + " (without Photon Voice: Somnium worlds do not use it). Unity imports and compiles for a while, " +
                "this window continues by itself.");
            Text("The tool also sets the Fusion " + MigrationConstants.TargetFusionVersion + " scripting define symbols in Player Settings (Fusion would add them " +
                "itself, but only after its first successful compile), and fixes assembly definitions that " +
                "reference Photon assemblies that were removed or renamed.");
            bool oldPhoton = PhotonStep.OldPhotonPresent;
            if (oldPhoton)
                EditorGUILayout.HelpBox("An old Photon is still in the project. Installing over it mixes both versions: delete it first (step 3).", MessageType.Error);
            else if (MigrationIO.IsFusion21Installed())
                EditorGUILayout.HelpBox("Photon Fusion " + MigrationConstants.TargetFusionVersion + " is already installed. Installing again overwrites its " +
                    "files with the version of this tool; your settings are kept.", MessageType.Info);
            DrawOldCommunityModulesWarning();
            Row("Package", PhotonStep.PackagePath ?? "MISSING");
            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(PhotonStep.PackagePath == null || !string.IsNullOrEmpty(State.PendingAction) || (oldPhoton && !Advanced)))
            {
                if (GUILayout.Button("Install Photon Fusion " + MigrationConstants.TargetFusionVersion, GUILayout.Height(28)))
                {
                    _pending = () =>
                    {
                        string msg = "Install Photon Fusion " + MigrationConstants.TargetFusionVersion + " now?";
                        if (oldPhoton)
                            msg += "\n\nWARNING: the old Photon is still in the project. Both versions will be mixed, which usually does not compile.";
                        if (!EditorUtility.DisplayDialog("Install Photon", msg, "Install", "Cancel"))
                            return;
                        PhotonStep.ExecuteImport();
                    };
                }
            }
        }

        // ---------------------------------------------------------------------- step 5

        void DrawStep5()
        {
            if (State.CmImported)
            {
                EditorGUILayout.HelpBox("Done: Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " installed, assembly '" +
                    State.CmAsmdefName + "' and namespace '" + State.CmRootNamespace + "' restored, " + State.Parked.Count + " of your scripts back.", MessageType.Info);
                if (State.OldFloatComponents > 0)
                    EditorGUILayout.HelpBox(State.OldFloatComponents + " NetworkFloat component(s) are now NetworkEventFloat with their settings. Each one needs a " +
                        "NetworkBridgeData component (for example NetworkBridgeData1) on the same object: add it, the NetworkEventFloat finds it by itself.", MessageType.Warning);
                if (State.RetiredUsages.Count > 0 && State.KeepUsedRetiredScripts)
                    EditorGUILayout.HelpBox("Older Community Modules scripts your objects use were kept in 'Scripts/_Retired (older Community Modules)'. " +
                        "They are not maintained anymore: test these objects, and replace them when you can.", MessageType.Info);
                DrawCompileErrors();
                if (State.MovedForImport.Count > 0)
                {
                    Text(State.MovedForImport.Count + " Community Modules files were moved to the standard location for the update.");
                    if (GUILayout.Button("Move them back to where they were", GUILayout.Width(280)))
                        _pending = () => { if (EditorUtility.DisplayDialog("Move back", "Move the files back to their previous folders?", "Move", "Cancel")) CommunityModulesStep.MoveBack(); };
                }
                if (State.OpenScenesBefore.Count > 0 && GUILayout.Button("Re-open my scenes", GUILayout.Width(280)))
                    _pending = () => MigrationIO.ReopenScenes(State);
                if (!Advanced)
                    return;
                Text("<i>Advanced mode: you can install it again.</i>");
            }
            else if (State.CmSkipped && DoneAndStop("Skipped: Community Modules was not installed. " + State.Parked.Count + " of your scripts were put back."))
            {
                return;
            }
            if (_cmPlan == null) _cmPlan = CommunityModulesStep.BuildPlan();
            var p = _cmPlan;
            foreach (string problem in p.Problems)
                EditorGUILayout.HelpBox(problem, MessageType.Error);
            foreach (string w in p.Warnings)
                EditorGUILayout.HelpBox(w, Advanced ? MessageType.Warning : MessageType.Error);
            bool noCm = State.Analysed && !State.CmPresent && !CommunityModulesStep.IsTargetVersionImported();
            if (noCm)
            {
                Text("This project had no Community Modules. Installing Community Modules " + MigrationConstants.TargetCommunityModulesVersion +
                    " is <b>optional</b>: it adds the Somnium example scripts, prefabs and scenes in '" + MigrationConstants.CmCanonicalRoot + "'.");
                if (GUILayout.Button("Skip: do not install Community Modules", GUILayout.Height(26)))
                    _pending = () =>
                    {
                        if (EditorUtility.DisplayDialog("Skip Community Modules", "Continue without Community Modules? Your scripts set aside in step 2 are put back.", "Skip", "Cancel"))
                            CommunityModulesStep.Skip();
                    };
                GUILayout.Space(6);
            }
            if (Advanced && OldCommunityModulesPresent() && (!State.CleanupDone || State.CleanupMode == CleanupMode.Nothing.ToString()))
                EditorGUILayout.HelpBox("The old Community Modules scripts were not removed (step 2). The update replaces them, but the files that " +
                    "version " + MigrationConstants.TargetCommunityModulesVersion + " no longer has stay in the project and may not compile. " +
                    "Recommended: run step 2 first (the 'Update Community Modules only' task does both).", MessageType.Warning);
            Text("Installs Community Modules " + MigrationConstants.TargetCommunityModulesVersion + (noCm ? "." : ", then restores what makes your world unique:"));
            if (!string.IsNullOrEmpty(State.CmAsmdefJson))
                Bullet("Assembly name '" + State.CmAsmdefName + "' and namespace '" + State.CmRootNamespace + "', with your assembly references.");
            else if (State.CmPresent)
                Bullet("Your Community Modules had no assembly definition: it gets the default one ('" + MigrationConstants.CmDefaultAsmdefName + "').");
            Bullet(State.Parked.Count + " of your scripts set aside in step 2.");
            Bullet("Assembly references to removed Photon assemblies are fixed in your other assembly definitions.");
            if (p.RootMoveFrom != null)
                EditorGUILayout.HelpBox("Community Modules is in '" + p.RootMoveFrom + "'. It is moved to its standard location '" + MigrationConstants.CmCanonicalRoot +
                    "' for the update (otherwise Unity would create duplicates). You can move it back when the update is done.", MessageType.Warning);
            if (p.Moves.Count > 0)
            {
                EditorGUILayout.HelpBox(p.Moves.Count + " Community Modules file(s) are not at their standard location. They are moved there first, otherwise Unity would create duplicates.", MessageType.Warning);
                Foldout("cm_moves", "Files moved (" + p.Moves.Count + ")", () => { foreach (var m in p.Moves) GUILayout.Label("   " + m.Key + "  →  " + m.Value, EditorStyles.miniLabel); });
            }
            if (p.Asides.Count > 0)
                Foldout("cm_asides", "Other files renamed to make room (" + p.Asides.Count + ")", () => { foreach (var m in p.Asides) GUILayout.Label("   " + m.Key + "  →  " + m.Value, EditorStyles.miniLabel); });
            if (State.OldFloatComponents > 0)
            {
                State.UpdateFloatValues = EditorGUILayout.ToggleLeft(" Keep the settings of your " + State.OldFloatComponents +
                    " NetworkFloat component(s) (they become NetworkEventFloat: ID, initial value and event are carried over)", State.UpdateFloatValues);
            }
            if (State.CreatorScriptsToRename.Count > 0)
            {
                State.RenameInCreatorScripts = EditorGUILayout.ToggleLeft(" Update your " + State.CreatorScriptsToRename.Count +
                    " script(s) for renamed classes (CharacterTrigger → LocalPlayerTrigger, NetworkFloat → NetworkEventFloat)", State.RenameInCreatorScripts);
                Foldout("cm_rename", "Scripts updated (" + State.CreatorScriptsToRename.Count + ")", () => List(State.CreatorScriptsToRename));
            }
            if (p.ModifiedAssets.Count > 0)
            {
                Header("Community Modules assets you edited");
                Text("These original assets were changed in your project. By default they are replaced by version " + MigrationConstants.TargetCommunityModulesVersion +
                    " (which includes the fixes for Fusion 2.1). Tick the ones you want to keep as they are.");
                foreach (string m in p.ModifiedAssets)
                {
                    bool on = _keepMine.Contains(m);
                    bool now = EditorGUILayout.ToggleLeft("   Keep my version: " + m, on);
                    if (now != on) { if (now) _keepMine.Add(m); else _keepMine.Remove(m); }
                }
            }
            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(p.Problems.Count > 0 || (!Advanced && p.Warnings.Count > 0) || !string.IsNullOrEmpty(State.PendingAction)))
            {
                if (GUILayout.Button(noCm ? "Install Community Modules" : "Update Community Modules", GUILayout.Height(28)))
                {
                    var keep = new List<string>(_keepMine);
                    _pending = () =>
                    {
                        string msg = "Install Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " now? A backup is made first.";
                        if (p.Warnings.Count > 0)
                            msg += "\n\nWARNING: " + string.Join("\n", p.Warnings);
                        if (!EditorUtility.DisplayDialog("Update Community Modules", msg, "Install", "Cancel"))
                            return;
                        CommunityModulesStep.ExecuteImport(p, keep);
                    };
                }
            }
        }

        // ---------------------------------------------------------------------- step 6

        void DrawStep6()
        {
            Text("Old <b>NetworkRigidbody3D</b> components became missing scripts with Fusion 2.1. Each one is converted <b>in place</b> to " +
                "<b>NetworkTransform</b> with the recommended settings below, so it moves like before and every reference to it is kept.");
            Text("Grabbable objects (NetworkGrabbable) are now detached from their parent when the scene loads: NetworkTransform syncs local " +
                "positions and grabbing unparents the object. You can turn this off per object (Detach From Parent).");
            if (!MigrationIO.IsFusion21Installed())
                EditorGUILayout.HelpBox("Photon Fusion " + MigrationConstants.TargetFusionVersion + " is not installed (step 4). Convert after installing it: " +
                    "the recommended settings only exist in Fusion 2.1.", MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan open scenes", GUILayout.Height(24)))
                _pending = () => { _found = NetworkObjectConverter.ScanOpenScenes(); _foundSource = "open scenes"; };
            if (GUILayout.Button("Scan network prefab list", GUILayout.Height(24)))
                _pending = () =>
                {
                    _found = new List<NetworkObjectConverter.Found>();
                    var seen = new HashSet<string>();
                    foreach (string prefab in NetworkObjectConverter.NetworkPrefabList())
                        foreach (var f in NetworkObjectConverter.ScanWithNested(prefab))
                            if (seen.Add(f.AssetPath + f.FileId)) _found.Add(f);
                    _foundSource = "network prefab list of SceneNetworking";
                };
            if (GUILayout.Button("Scan whole project", GUILayout.Height(24)))
                _pending = () =>
                {
                    _found = NetworkObjectConverter.ScanProject();
                    _foundSource = "whole project";
                    State.ProjectScanClean = _found.Count == 0;
                    State.Save();
                };
            EditorGUILayout.EndHorizontal();
            if (State.OpenScenesBefore.Count > 0 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == "")
                if (GUILayout.Button("Re-open my scenes first", GUILayout.Width(220)))
                    _pending = () => MigrationIO.ReopenScenes(State);

            if (_found != null)
            {
                Header("Found in " + _foundSource + ": " + _found.Count);
                if (_found.Count == 0)
                    EditorGUILayout.HelpBox("Nothing to convert here.", MessageType.Info);
                var byFile = new Dictionary<string, List<NetworkObjectConverter.Found>>();
                foreach (var f in _found)
                {
                    if (!byFile.TryGetValue(f.AssetPath, out var l)) byFile[f.AssetPath] = l = new List<NetworkObjectConverter.Found>();
                    l.Add(f);
                }
                foreach (var kv in byFile)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label((kv.Key.EndsWith(".unity") ? "Scene  " : "Prefab  ") + kv.Key, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    string file = kv.Key;
                    if (kv.Value.Count > 1 && GUILayout.Button("Convert all " + kv.Value.Count, GUILayout.Width(120)))
                        _pending = () => ConvertAndRescan(file, null);
                    EditorGUILayout.EndHorizontal();
                    if (kv.Key.EndsWith(".prefab"))
                        GUILayout.Label("Converting here changes the prefab, so every instance in every scene gets the change.", EditorStyles.wordWrappedMiniLabel);
                    foreach (var f in kv.Value)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("   " + f.GameObjectName, GUILayout.ExpandWidth(true));
                        var item = f;
                        if (GUILayout.Button("Convert", GUILayout.Width(80)))
                            _pending = () => ConvertAndRescan(item.AssetPath, new[] { item.FileId });
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }
                if (_found.Count > 1 && GUILayout.Button("Convert everything listed (" + _found.Count + ")", GUILayout.Height(26)))
                    _pending = () =>
                    {
                        if (!EditorUtility.DisplayDialog("Convert", "Convert " + _found.Count + " components? A backup of each file is made first.", "Convert", "Cancel"))
                            return;
                        var paths = new List<string>();
                        foreach (var f in _found) paths.Add(f.AssetPath);
                        foreach (string f in NetworkObjectConverter.OrderForConversion(paths)) NetworkObjectConverter.Fix(f, null);
                        _found = null;
                    };
            }

            GUILayout.Space(10);
            Header("Recommended NetworkTransform settings");
            Text("Two settings make held and thrown objects move like before, for you and for the other players:");
            Bullet("<b>Forecast off.</b> With Forecast on, other players see a physics guess of the object that is corrected afterwards: a held " +
                "object looks floaty and runs ahead of the hand. With it off they see exactly the path the holder sent.");
            Bullet("<b>Disable Shared Mode Interpolation</b> (Config Flags), only on objects grabbed with XR Grab Interactable " +
                "<b>Movement Type: Kinematic</b>. Without it, the holder's own copy is redrawn between network updates, which fights the " +
                "grab: the object stutters for everyone. Other movement types do not need it.");
            Text("Converted objects already have these settings. This button also applies them to the NetworkTransforms you added yourself.");
            if (State.NetworkTransformSettingsDone)
                EditorGUILayout.HelpBox("Every NetworkTransform has the recommended settings. Components you add later start with the defaults: untick " +
                    "'Forecast Physics Enabled' on them, and on Kinematic grabs also set Config Flags to 'Disable Shared Mode Interpolation', or press " +
                    "the button again.", MessageType.Info);
            if (GUILayout.Button("Apply recommended settings to every NetworkTransform", GUILayout.Width(380)))
                _pending = () =>
                {
                    if (!EditorUtility.DisplayDialog("NetworkTransform settings", "Turn Forecast off on every NetworkTransform in your scenes and " +
                        "prefabs, and disable Shared Mode interpolation on the Kinematic grabs? A backup is made first.", "Apply", "Cancel"))
                        return;
                    int n = NetworkObjectConverter.ApplyRecommendedSettingsEverywhere();
                    EditorUtility.DisplayDialog("NetworkTransform settings", n + " NetworkTransform component(s) changed.", "OK");
                };

            GUILayout.Space(10);
            Header("When everything is converted");
            if (GUILayout.Button("Rebuild Fusion prefab table", GUILayout.Width(240)))
                _pending = NetworkObjectConverter.RebuildPrefabTable;
            Text("Then test with two clients (two Editors or Editor + build): no Console errors after entering the world; objects appear for both and " +
                "nothing disappears on spawn; movement and physics are smooth on the other client; grabbing and ownership transfer work; when the first " +
                "player leaves, objects stay or despawn as expected for the second player. Then upload the world again.");
            DrawCompileErrors();
            GUILayout.Space(6);
            if (GUILayout.Button("Finish: remove the migration tool from the project", GUILayout.Width(360)))
                _pending = RemoveTool;
        }

        // ---------------------------------------------------------------------- quick tasks (advanced mode)

        /// <summary>Steps 3 and 4 on one page: replace Photon, leave Community Modules and the creator's scripts alone.</summary>
        void DrawPhotonTask()
        {
            Text("Replaces the old Photon by <b>Photon Fusion " + MigrationConstants.TargetFusionVersion + "</b>, without touching " +
                "Community Modules or your scripts. Your Photon settings (App Id, NetworkProjectConfig) are kept.");
            Text("Then convert the old NetworkRigidbody3D components (step 6), unless your world has none.");
            if (OldCommunityModulesPresent())
                EditorGUILayout.HelpBox("Your Community Modules is made for the old Photon: after this it shows compile errors until you update it " +
                    "('" + TaskTitles[1] + "') or fix them yourself.", MessageType.Warning);

            Header("1. Remove the old Photon (step 3)");
            DrawStep3();
            Header("2. Install Photon " + MigrationConstants.TargetFusionVersion + " (step 4)");
            DrawStep4();
        }

        /// <summary>Steps 2 and 5 on one page: update Community Modules in a project that already has Photon 2.1.</summary>
        void DrawCmTask()
        {
            Text("Updates Community Modules to <b>" + MigrationConstants.TargetCommunityModulesVersion + "</b> without touching Photon. Your assembly " +
                "name, namespace and own scripts are kept, as in the wizard.");
            if (!MigrationIO.IsFusion21Installed())
                EditorGUILayout.HelpBox("Photon Fusion " + MigrationConstants.TargetFusionVersion + " is not installed. Community Modules " +
                    MigrationConstants.TargetCommunityModulesVersion + " needs it: install it first ('" + TaskTitles[0] + "').", MessageType.Warning);

            Header("1. Remove the old Community Modules scripts (step 2)");
            DrawStep2();
            Header("2. Install Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " (step 5)");
            DrawStep5();
        }

        /// <summary>Steps 2 to 4 make Unity and the old Photon log errors while files are missing. Say so before anyone panics.</summary>
        void DrawExpectedConsoleNoise()
        {
            if (!State.CleanupDone || State.CmImported)
                return;
            EditorGUILayout.HelpBox("Red messages in the Console are expected until step 5 is done, for example " +
                "\"Error while saving Prefab ... missing script\", \"Failed to create ServerSettings. PUN ...\" or " +
                "\"Unexpected error in Burst compilation ... Unable to load the unmanaged library\". " +
                "They come from code that is still loaded while its files are removed, and Unity refusing to save prefabs while " +
                "their scripts are away (which protects your prefabs). Nothing is broken.", MessageType.Info);
            if (GUILayout.Button("Clear Console", GUILayout.Width(140)))
                _pending = () =>
                {
                    var logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                    logEntries?.GetMethod("Clear")?.Invoke(null, null);
                };
        }

        void ConvertAndRescan(string file, ICollection<string> ids)
        {
            NetworkObjectConverter.Fix(file, ids);
            if (_found != null)
                _found.RemoveAll(f => f.AssetPath == file && (ids == null || ids.Contains(f.FileId)));
        }

        void RemoveTool()
        {
            if (!EditorUtility.DisplayDialog("Remove the migration tool",
                "Delete the migration tool folder (" + ProjectAnalyzer.ToolRoot + ")? The migration guide inside it goes too: copy it first if you " +
                "want to keep it.\n\nYour backups and the log stay in " + MigrationState.WorkFolder +
                ". Delete that folder yourself once you are happy with the result.", "Remove tool", "Cancel"))
                return;
            MigrationState.Log("Migration tool removed by the user.");
            Close();
            AssetDatabase.DeleteAsset(ProjectAnalyzer.ToolRoot);
        }

        void DrawCompileErrors()
        {
            if (State.LastCompileErrors.Count == 0 && !EditorUtility.scriptCompilationFailed)
            {
                EditorGUILayout.HelpBox("No compile errors.", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox(State.LastCompileErrors.Count + " compile error(s). Hints below; the Console has the details.", MessageType.Error);
            var shown = new HashSet<string>();
            foreach (string e in State.LastCompileErrors)
            {
                string hint = CompileHints.For(e);
                GUILayout.Label("• " + e, _wrap);
                if (hint != null && shown.Add(hint))
                    EditorGUILayout.HelpBox(hint, MessageType.None);
            }
        }

        // ====================================================================== backups tab

        void DrawBackups()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Text("A backup is made before every step. Restoring puts the backed-up files back exactly as they were. " +
                "<b>Restore the most recent backup first</b> when you go back several steps.");
            Text("Folder: " + MigrationState.WorkFolder);
            for (int i = State.Backups.Count - 1; i >= 0; i--)
            {
                var b = State.Backups[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Step " + b.Step + ": " + b.Label + (b.Restored ? "   (restored)" : ""), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(b.CreatedLocal, EditorStyles.miniLabel);
                var record = b;
                if (GUILayout.Button("Show", GUILayout.Width(60)))
                    _pending = () => EditorUtility.RevealInFinder(BackupManager.ZipPath(record));
                if (GUILayout.Button("Restore", GUILayout.Width(70)))
                    _pending = () => RestoreBackup(record);
                EditorGUILayout.EndHorizontal();
                GUILayout.Label("   " + string.Join(", ", b.Scopes), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            GUILayout.Space(10);
            if (GUILayout.Button("Start the migration over (keeps backups)", GUILayout.Width(300)))
                _pending = () =>
                {
                    if (!EditorUtility.DisplayDialog("Start over", "Forget the progress of this migration? Files are not changed, backups stay on disk.", "Start over", "Cancel"))
                        return;
                    MigrationState.Reset();
                    _analysis = null; _photonPlan = null; _cmPlan = null; _found = null;
                };
            EditorGUILayout.EndScrollView();
        }

        void RestoreBackup(BackupRecord b)
        {
            int newer = 0;
            foreach (var o in State.Backups)
                if (string.CompareOrdinal(o.Id, b.Id) > 0 && !o.Restored) newer++;
            string msg = "Restore '" + b.Label + "' from " + b.CreatedLocal + "?\n\nThese are replaced by the backup:\n" + string.Join("\n", b.Scopes);
            if (newer > 0)
                msg += "\n\n" + newer + " newer backup(s) exist and were not restored. Restore them first, newest first.";
            if (!EditorUtility.DisplayDialog("Restore backup", msg, "Restore", "Cancel"))
                return;
            if (!MigrationIO.CloseScenesSafely(State))
                return;
            BackupManager.Restore(b);
            State.InvalidateFromStep(b.Step); // The steps this backup covers are no longer done
            _analysis = null; _photonPlan = null; _cmPlan = null; _found = null;
            EditorUtility.DisplayDialog("Restore backup", "Restored. Unity now re-imports and compiles the restored files.", "OK");
        }

        // ====================================================================== log tab

        void DrawLog()
        {
            if (GUILayout.Button("Open log file", GUILayout.Width(160)))
                _pending = () => EditorUtility.OpenWithDefaultApp(MigrationState.LogFilePath);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (File.Exists(MigrationState.LogFilePath))
            {
                var lines = File.ReadAllLines(MigrationState.LogFilePath);
                for (int i = Math.Max(0, lines.Length - 300); i < lines.Length; i++)
                    GUILayout.Label(lines[i], EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        // ====================================================================== helpers

        void Text(string s) => GUILayout.Label(s, _wrap);

        void Bullet(string s) => GUILayout.Label("   " + s, _wrap);

        void Header(string s)
        {
            GUILayout.Space(6);
            GUILayout.Label(s, EditorStyles.boldLabel);
        }

        void Row(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(210));
            EditorGUILayout.SelectableLabel(value ?? "-", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        void Foldout(string key, string label, Action content)
        {
            _foldouts.TryGetValue(key, out bool open);
            bool now = EditorGUILayout.Foldout(open, label, true);
            _foldouts[key] = now;
            if (now)
                content();
        }

        void List(IEnumerable<string> items)
        {
            foreach (string s in items)
                GUILayout.Label("   " + s, EditorStyles.miniLabel);
        }
    }
}
