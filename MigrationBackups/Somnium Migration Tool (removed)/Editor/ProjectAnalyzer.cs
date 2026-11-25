using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Somnium.MigrationTool
{
    internal enum FileCategory
    {
        OriginalCode,          // Community Modules script, unchanged (namespace renames are ignored)
        ModifiedOriginalCode,  // Community Modules script edited by the creator
        CreatorCode,           // Script the creator added
        CmAsmdef,              // The world scripts asmdef (renamed by the Namespace Helper)
        NamespaceBackup,       // Copies written by the Namespace Helper in "#Scripts Backup"
        OriginalAsset,         // Community Modules asset, unchanged
        ModifiedOriginalAsset, // Community Modules asset edited by the creator
        CreatorAsset,          // Asset the creator added
    }

    internal class ClassifiedFile
    {
        public string Path;
        public string Guid;
        public FileCategory Category;
        public string Versions; // Releases this exact content shipped in

        public bool IsCode => Category == FileCategory.OriginalCode || Category == FileCategory.ModifiedOriginalCode ||
            Category == FileCategory.CreatorCode || Category == FileCategory.CmAsmdef;
    }

    /// <summary>Read-only analysis of the project (step 1) and classification of the Community Modules files (step 2).</summary>
    internal class ProjectAnalyzer
    {
        public bool CmFound;
        public bool CmHasAsmdef;
        public string CmAsmdefPath;
        public AsmdefData CmAsmdef;
        public string CmAsmdefJson;
        public string CmRoot;
        public bool CmAtCanonicalPath;
        public string BestVersion;
        public readonly Dictionary<string, int> VersionScores = new Dictionary<string, int>();
        public string FusionVersion;
        public bool VoiceInAssets;
        public bool PunInAssets;
        public readonly List<string> UpmPhotonPackages = new List<string>();
        public readonly List<string> OtherAsmdefsReferencingCm = new List<string>();
        public readonly List<string> ScriptsOutsideUsingPhoton = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<ClassifiedFile> Files = new List<ClassifiedFile>();
        public readonly List<string> KnownCodeOutsideRoot = new List<string>(); // Original CM scripts moved out of the CM folder
        public readonly List<string> CreatorScripts = new List<string>();       // Every .cs that is not Photon, the tool or a CM original
        public LegacyCommunityModules.Report Legacy;
        public bool HasGit;

        public static ProjectAnalyzer Run()
        {
            var a = new ProjectAnalyzer();
            a.Analyse();
            return a;
        }

        void Analyse()
        {
            var known = KnownFiles.Instance;
            HasGit = Directory.Exists(MigrationIO.ToAbsolute(".git")) || File.Exists(MigrationIO.ToAbsolute(".git"));

            // ---- Community Modules code base, found by the asmdef GUID (stable in every release, survives renames and moves)
            CmAsmdefPath = MigrationIO.AssetPathFromGuid(MigrationConstants.CmScriptsAsmdefGuid);
            if (CmAsmdefPath == null)
            {
                // No asmdef: the creator may have deleted it (the scripts then compile in Assembly-CSharp), or the project
                // has no Community Modules at all. Look for the original files themselves.
                CmRoot = FindRootWithoutAsmdef(known);
                if (CmRoot != null)
                {
                    CmFound = true;
                    CmAtCanonicalPath = CmRoot == MigrationConstants.CmCanonicalRoot;
                    Warnings.Add("Community Modules was found in " + CmRoot + " but without its assembly definition: its scripts compile in " +
                        "Assembly-CSharp. Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " brings its own assembly definition " +
                        "(" + MigrationConstants.CmDefaultAsmdefName + "). Your scripts inside the Community Modules folder become part of it: if they use " +
                        "scripts outside that folder, move them out after the update.");
                    if (!CmAtCanonicalPath)
                        Warnings.Add("Community Modules is not at its standard location (" + MigrationConstants.CmCanonicalRoot + "). " +
                            "The update moves the files to the standard location first, otherwise Unity would create duplicates.");
                    ClassifyCmFiles(known);
                }
                else
                {
                    Notes.Add("No Community Modules in this project. The migration still updates Photon and converts your network objects; " +
                        "installing Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " is optional (step 5).");
                }
            }
            else
            {
                CmFound = true;
                CmHasAsmdef = true;
                CmAsmdefJson = File.ReadAllText(MigrationIO.ToAbsolute(CmAsmdefPath));
                try { CmAsmdef = AsmdefData.Parse(CmAsmdefJson); }
                catch (Exception e) { Errors.Add("Could not read " + CmAsmdefPath + ": " + e.Message); }

                string scriptsFolder = Path.GetDirectoryName(CmAsmdefPath).Replace('\\', '/');
                if (Path.GetFileName(scriptsFolder) == "Scripts")
                {
                    CmRoot = Path.GetDirectoryName(scriptsFolder).Replace('\\', '/');
                }
                else
                {
                    CmRoot = scriptsFolder;
                    Warnings.Add("The Community Modules assembly definition is not in a 'Scripts' folder (" + CmAsmdefPath + "). The layout looks customised.");
                }
                CmAtCanonicalPath = CmRoot == MigrationConstants.CmCanonicalRoot;
                if (!CmAtCanonicalPath)
                    Warnings.Add("Community Modules is not at its standard location (" + MigrationConstants.CmCanonicalRoot + ") but at " + CmRoot +
                        ". The update moves the files to the standard location first, otherwise Unity would create duplicates.");
                ClassifyCmFiles(known);
            }

            // ---- Original Community Modules scripts moved outside the CM folder (would be duplicated by the update)
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!MigrationIO.IsCodeFile(f) || (CmRoot != null && f.StartsWith(CmRoot + "/")))
                    continue;
                string g = MigrationIO.ReadMetaGuid(f);
                if (known.IsKnownCm(g))
                    KnownCodeOutsideRoot.Add(f);
            }
            if (KnownCodeOutsideRoot.Count > 0)
                Warnings.Add(KnownCodeOutsideRoot.Count + " Community Modules script(s) were moved outside the Community Modules folder. They are treated like the other original scripts.");

            // ---- Photon
            FusionVersion = MigrationIO.ReadFusionVersion();
            VoiceInAssets = Directory.Exists(MigrationIO.ToAbsolute(MigrationConstants.PhotonRoot + "/PhotonVoice"));
            PunInAssets = Directory.Exists(MigrationIO.ToAbsolute(MigrationConstants.PhotonRoot + "/PhotonUnityNetworking"));
            string manifest = MigrationIO.ToAbsolute("Packages/manifest.json");
            if (File.Exists(manifest))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(manifest), "\"(com\\.photonengine\\.[^\"]+)\""))
                    UpmPhotonPackages.Add(m.Groups[1].Value);
            }
            if (VoiceInAssets)
                Warnings.Add("Photon Voice is in this project. It is removed with the old Photon and not installed again: Somnium worlds and " +
                    "Community Modules " + MigrationConstants.TargetCommunityModulesVersion + " do not use it. Your own scripts that use Photon Voice " +
                    "will not compile anymore.");
            if (UpmPhotonPackages.Count > 0)
                Warnings.Add("Photon packages are installed through the Package Manager (" + string.Join(", ", UpmPhotonPackages) +
                    "). They will be removed from Packages/manifest.json and replaced by the Photon files of this migration.");

            // ---- Other assemblies that depend on the Community Modules assembly
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!f.EndsWith(".asmdef") || f == CmAsmdefPath || f.StartsWith(MigrationConstants.PhotonRoot + "/"))
                    continue;
                try
                {
                    var d = AsmdefData.Read(f);
                    if (d.References(MigrationConstants.CmScriptsAsmdefGuid, CmAsmdef != null ? CmAsmdef.name : null))
                        OtherAsmdefsReferencingCm.Add(f + "  (" + d.name + ")");
                }
                catch (Exception) { /* Unreadable asmdef: Unity reports it anyway */ }
            }

            // ---- Scripts outside Community Modules and Photon that use Photon or Community Modules types
            string ns = CmAsmdef != null && !string.IsNullOrEmpty(CmAsmdef.rootNamespace) ? Regex.Escape(CmAsmdef.rootNamespace) : "CommunityModules";
            var usesPhoton = new Regex(@"using\s+(Fusion|Photon|ExitGames)\b|\bFusion\.|NetworkRigidbody3D|using\s+" + ns + @"\b|\b" + ns + @"\.");
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (!f.EndsWith(".cs") || f.StartsWith(MigrationConstants.PhotonRoot + "/") || (CmRoot != null && f.StartsWith(CmRoot + "/")) ||
                    KnownCodeOutsideRoot.Contains(f) || f.StartsWith(ToolRoot + "/"))
                    continue;
                if (usesPhoton.IsMatch(File.ReadAllText(MigrationIO.ToAbsolute(f))))
                    ScriptsOutsideUsingPhoton.Add(f);
            }

            // ---- Older releases: retired scripts still used, renamed classes, NetworkFloat values
            foreach (string f in MigrationIO.ListFiles("Assets"))
            {
                if (f.EndsWith(".cs") && !f.StartsWith(MigrationConstants.PhotonRoot + "/") && !f.StartsWith(ToolRoot + "/") &&
                    !known.IsKnownCm(MigrationIO.ReadMetaGuid(f)))
                    CreatorScripts.Add(f);
            }
            Legacy = LegacyCommunityModules.Analyse(CreatorScripts);
            AddRetiredWarnings();
            if (Legacy.ScriptsUsingRenamedClasses.Count > 0)
                Warnings.Add(Legacy.ScriptsUsingRenamedClasses.Count + " of your script(s) use classes that were renamed (CharacterTrigger → LocalPlayerTrigger, " +
                    "NetworkFloat → NetworkEventFloat). They are updated in step 5.");
            if (Legacy.OldFloatComponents > 0)
                Warnings.Add(Legacy.OldFloatComponents + " NetworkFloat component(s) keep their settings in step 5 (they become NetworkEventFloat). " +
                    "Each needs a NetworkBridgeData component on its object afterwards.");
        }

        /// <summary>
        ///     Says what really happens to the objects that use Community Modules scripts the target release no longer has:
        ///     kept (step 2 keeps the script), removed (it needs Photon Voice), or already missing before the migration.
        /// </summary>
        void AddRetiredWarnings()
        {
            int kept = 0, needVoice = 0, missing = 0;
            var missingNames = new List<string>();
            foreach (var u in Legacy.RetiredUsages)
            {
                if (u.ScriptMissing)
                {
                    missing++;
                    string what = u.ScriptName + " on '" + u.GameObjectName + "' in " + u.AssetPath;
                    if (!missingNames.Contains(what))
                        missingNames.Add(what);
                    continue;
                }
                string path = MigrationIO.AssetPathFromGuid(u.ScriptGuid);
                if (path != null && File.ReadAllText(MigrationIO.ToAbsolute(path)).Contains("Photon.Voice"))
                    needVoice++;
                else
                    kept++;
            }
            string target = MigrationConstants.TargetCommunityModulesVersion;
            if (kept > 0)
                Warnings.Add(kept + " object(s) use Community Modules scripts that version " + target + " no longer includes. By default the scripts " +
                    "are kept in '_Retired (older Community Modules)' so these objects keep working (see step 2).");
            if (needVoice > 0)
                Warnings.Add(needVoice + " object(s) use an older Community Modules script that needs Photon Voice, which is not installed anymore: " +
                    "the script is removed in step 2 and these objects get a missing script. Replace them after the migration.");
            if (missing > 0)
                Warnings.Add(missing + " object(s) already have a missing script before the migration (a Community Modules script an earlier " +
                    "update removed): nothing can be kept, they stay as they are. Remove the component or the object: " + string.Join("; ", missingNames));
        }

        public static string ToolRoot =>MigrationIO.AssetPathFromGuid(MigrationConstants.ToolRootFolderGuid) ?? "Assets/Somnium Migration Tool";

        /// <summary>
        ///     Community Modules folder found from its original files, when the asmdef is gone: each file found at
        ///     "&lt;root&gt;/&lt;its path inside Community Modules&gt;" votes for that root. Null when no original file is in the project.
        /// </summary>
        static string FindRootWithoutAsmdef(KnownFiles known)
        {
            string canonical = MigrationConstants.CmCanonicalRoot;
            var votes = new Dictionary<string, int>();
            foreach (var kv in known.CommunityModules)
            {
                string current = MigrationIO.AssetPathFromGuid(kv.Key);
                if (current == null || current.StartsWith(ToolRoot + "/"))
                    continue;
                foreach (var e in kv.Value)
                {
                    if (e.Kind == "folder" || !e.Path.StartsWith(canonical + "/"))
                        continue;
                    string inside = e.Path.Substring(canonical.Length); // "/Scripts/..."
                    if (!current.EndsWith(inside) || current.Length == inside.Length)
                        continue;
                    string root = current.Substring(0, current.Length - inside.Length);
                    votes.TryGetValue(root, out int n);
                    votes[root] = n + 1;
                    break;
                }
            }
            string best = null;
            int bestVotes = 0;
            foreach (var kv in votes)
            {
                if (kv.Value > bestVotes)
                {
                    best = kv.Key;
                    bestVotes = kv.Value;
                }
            }
            return best;
        }

        void ClassifyCmFiles(KnownFiles known)
        {
            foreach (string f in MigrationIO.ListFiles(CmRoot))
            {
                string guid = MigrationIO.ReadMetaGuid(f);
                var cf = new ClassifiedFile { Path = f, Guid = guid };
                bool code = MigrationIO.IsCodeFile(f);

                if (f.Contains("/" + MigrationConstants.ScriptsBackupFolderName + "/"))
                {
                    cf.Category = FileCategory.NamespaceBackup;
                }
                else if (guid == MigrationConstants.CmScriptsAsmdefGuid)
                {
                    cf.Category = FileCategory.CmAsmdef;
                }
                else if (known.IsKnownCm(guid))
                {
                    string hash = MigrationIO.NormalizedHash(f, false);
                    string nsHash = code ? MigrationIO.NormalizedHash(f, true) : null;
                    var versions = known.MatchingCmVersions(guid, hash, nsHash);
                    cf.Versions = string.Join(", ", versions.ConvertAll(KnownFiles.PrettyVersion));
                    foreach (string v in versions)
                    {
                        VersionScores.TryGetValue(v, out int s);
                        VersionScores[v] = s + 1;
                    }
                    if (code)
                        cf.Category = versions.Count > 0 ? FileCategory.OriginalCode : FileCategory.ModifiedOriginalCode;
                    else
                        cf.Category = versions.Count > 0 ? FileCategory.OriginalAsset : FileCategory.ModifiedOriginalAsset;
                }
                else
                {
                    cf.Category = code ? FileCategory.CreatorCode : FileCategory.CreatorAsset;
                }
                Files.Add(cf);
            }

            int best = -1;
            foreach (var kv in VersionScores)
            {
                // Ties go to the newer release (versions sort alphabetically in release order)
                if (kv.Value > best || (kv.Value == best && string.CompareOrdinal(kv.Key, BestVersion) > 0))
                {
                    best = kv.Value;
                    BestVersion = kv.Key;
                }
            }
        }

        public int Count(FileCategory c)
        {
            int n = 0;
            foreach (var f in Files)
            {
                if (f.Category == c)
                    n++;
            }
            return n;
        }

        public List<ClassifiedFile> Of(FileCategory c) => Files.FindAll(f => f.Category == c);

        /// <summary>Store what later steps need. Called when the creator confirms step 1.</summary>
        public void SaveTo(MigrationState state)
        {
            state.Analysed = true;
            state.CmPresent = CmFound;
            state.CmAsmdefPath = CmAsmdefPath;
            state.CmAsmdefName = CmAsmdef?.name;
            state.CmRootNamespace = CmAsmdef?.rootNamespace;
            state.CmAsmdefJson = CmAsmdefJson;
            state.CmRoot = CmRoot;
            state.DetectedCmVersion = BestVersion;
            state.FusionVersionBefore = FusionVersion;
            state.OtherAsmdefsReferencingCm = new List<string>(OtherAsmdefsReferencingCm);
            state.AnalysisWarnings = new List<string>(Warnings);
            state.RetiredUsages = new List<string>();
            if (Legacy != null)
            {
                foreach (var u in Legacy.RetiredUsages)
                    state.RetiredUsages.Add(u.ScriptName + ": " + u.GameObjectName + " in " + u.AssetPath);
                state.CreatorScriptsToRename = new List<string>(Legacy.ScriptsUsingRenamedClasses);
                state.OldFloatComponents = Legacy.OldFloatComponents;
            }
            state.Save();
            MigrationState.Log($"Analysis saved: asmdef '{state.CmAsmdefName}' at {CmAsmdefPath}, namespace '{state.CmRootNamespace}', " +
                $"Community Modules {KnownFiles.PrettyVersion(BestVersion ?? "?")}, Fusion {FusionVersion}");
        }
    }
}
