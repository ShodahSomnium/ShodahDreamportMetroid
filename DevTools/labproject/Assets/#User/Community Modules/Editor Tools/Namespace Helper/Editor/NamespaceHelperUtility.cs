#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Namespace Helper's work, callable from code: the inspector buttons use it, and so can a script,
/// a coding agent (for example through the Unity CLI) or a batch-mode build (-executeMethod).
/// It renames the world asmdef (file, "name" and "rootNamespace") and the namespace of every script
/// in the asmdef's folder and sub-folders. A copy of each file is kept in "#Scripts Backup" first.
/// </summary>
public static class NamespaceHelperUtility
{
    public const string BackupFolderName = "#Scripts Backup";

    // The assembly name is also the namespace, and it must follow the SDK's assembly name rules.
    public const int MaxNameLength = 40;

    private const string LogPrefix = "[NamespaceHelper]";

    // Only matches a declaration at the start of a line, so a comment such as
    // "// set the namespace here" is never touched. Works for block and file-scoped namespaces.
    private static readonly Regex NamespaceDeclaration =
        new Regex(@"^(\s*namespace\s+)([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)", RegexOptions.Multiline);

    /// <summary>
    /// Renames the world assembly and namespace, using the folder of the project's single
    /// NamespaceHelper asset (the Community Modules "Scripts" folder by default).
    /// Example: NamespaceHelperUtility.RenameAll("CreatorName.WorldName");
    /// </summary>
    /// <returns>True when everything was renamed.</returns>
    public static bool RenameAll(string newNamespace)
    {
        string asmdefPath = FindWorldAsmdef();
        if (asmdefPath == null)
            return false;
        return RenameAll(asmdefPath, newNamespace);
    }

    /// <summary>
    /// Renames the asmdef at <paramref name="asmdefPath"/> (e.g. "Assets/#User/Community Modules/Scripts/CommunityModules.asmdef")
    /// and the namespace of every script in its folder and sub-folders.
    /// A script in the old root namespace or below it keeps its sub-namespace ("Old.UI" becomes "New.UI");
    /// any other namespace is replaced by the new root. Scripts without a namespace are left as they are.
    /// </summary>
    /// <returns>True when everything was renamed.</returns>
    public static bool RenameAll(string asmdefPath, string newNamespace)
    {
        if (!IsValidName(newNamespace, out string error))
        {
            Debug.LogError($"{LogPrefix} Invalid namespace \"{newNamespace}\": {error}");
            return false;
        }
        if (string.IsNullOrEmpty(asmdefPath) || !File.Exists(asmdefPath))
        {
            Debug.LogError($"{LogPrefix} Assembly definition not found: \"{asmdefPath}\".");
            return false;
        }
        // Check before touching any script, so a failure does not leave the world half renamed.
        if (!CanRenameAsmdef(asmdefPath, newNamespace))
            return false;

        string oldNamespace = GetAsmdefRootNamespace(asmdefPath);
        string[] scriptPaths = GetScriptPaths(asmdefPath);

        int renamedCount = 0;
        foreach (string scriptPath in scriptPaths)
        {
            if (RenameScriptNamespace(scriptPath, oldNamespace, newNamespace, asmdefPath))
                renamedCount++;
        }

        string newAsmdefPath = SetAsmdefName(asmdefPath, newNamespace);
        AssetDatabase.Refresh();

        Debug.Log($"{LogPrefix} Renamed \"{oldNamespace}\" to \"{newNamespace}\": {renamedCount} of {scriptPaths.Length} scripts changed, asmdef is now \"{newAsmdefPath}\".");
        return newAsmdefPath != null;
    }

    /// <summary>
    /// Entry point for batch mode, with the editor closed:
    /// Unity -batchmode -quit -projectPath "&lt;project&gt;" -executeMethod NamespaceHelperUtility.RenameFromCommandLine -namespace CreatorName.WorldName
    /// Exits with code 1 on failure.
    /// </summary>
    public static void RenameFromCommandLine()
    {
        string newNamespace = null;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-namespace")
                newNamespace = args[i + 1];
        }

        bool success = newNamespace != null && RenameAll(newNamespace);
        if (newNamespace == null)
            Debug.LogError($"{LogPrefix} Missing argument: -namespace <Name>.");

        if (Application.isBatchMode && !success)
            EditorApplication.Exit(1);
    }

    /// <summary>
    /// Describes the current state without changing anything: the asmdef, its names, and every script
    /// whose namespace is not the root namespace or below it. Also written to the log.
    /// </summary>
    public static string GetStatus()
    {
        string asmdefPath = FindWorldAsmdef();
        if (asmdefPath == null)
            return $"{LogPrefix} No world asmdef found (see the error above).";

        string rootNamespace = GetAsmdefRootNamespace(asmdefPath);
        string[] scriptPaths = GetScriptPaths(asmdefPath);

        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Asmdef: {asmdefPath}");
        report.AppendLine($"Assembly name: {GetAsmdefName(asmdefPath)}, root namespace: {rootNamespace}, scripts: {scriptPaths.Length}");

        int wrongCount = 0;
        foreach (string scriptPath in scriptPaths)
        {
            string scriptNamespace = GetScriptNamespace(scriptPath);
            if (!IsInNamespace(scriptNamespace, rootNamespace))
            {
                report.AppendLine($"Outside the root namespace: {scriptPath} ({(scriptNamespace.Length == 0 ? "no namespace" : scriptNamespace)})");
                wrongCount++;
            }
        }
        report.AppendLine(wrongCount == 0 ? "All scripts are in the root namespace." : $"{wrongCount} scripts outside the root namespace.");

        string result = report.ToString();
        Debug.Log(result);
        return result;
    }

    /// <summary>
    /// Checks a name against the assembly name rules: 1 to 40 characters, letters, digits and dots,
    /// each part starting with a letter.
    /// </summary>
    public static bool IsValidName(string name, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(name))
            error = "it is empty.";
        else if (name.Length > MaxNameLength)
            error = $"it is longer than {MaxNameLength} characters.";
        else if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$"))
            error = "use only letters, digits and dots, and start each part with a letter.";
        return error == null;
    }

    /// <summary>
    /// Turns any text into a valid namespace by dropping invalid characters (used by the inspector while typing).
    /// </summary>
    public static string FormatNamespace(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return string.Empty;

        string valid = Regex.Replace(namespaceName, @"[^A-Za-z0-9\.]", "");
        valid = Regex.Replace(valid, @"\.{2,}", "."); // Replace multiple dots with a single dot
        string[] parts = valid.Split('.');
        string finalNamespace = string.Empty;
        foreach (string part in parts)
        {
            string cleanedPart = Regex.Replace(part, @"^\d+", ""); // Remove all leading digits
            if (cleanedPart.Length != 0)
                finalNamespace += cleanedPart + ".";
        }
        return finalNamespace.Trim('.');
    }

    /// <summary>
    /// The first namespace declared in a script, or an empty string.
    /// </summary>
    public static string GetScriptNamespace(string scriptPath)
    {
        Match match = NamespaceDeclaration.Match(File.ReadAllText(scriptPath));
        return match.Success ? match.Groups[2].Value : string.Empty;
    }

    /// <summary>
    /// Sets every namespace declared in a script to <paramref name="newNamespace"/>.
    /// </summary>
    /// <returns>True when the file changed.</returns>
    public static bool SetScriptNamespace(string scriptPath, string newNamespace, string asmdefPath)
    {
        return RewriteScriptNamespaces(scriptPath, asmdefPath, oldName => newNamespace);
    }

    /// <summary>
    /// Renames the asmdef file (and its .meta, so its GUID survives) and sets its "name" and "rootNamespace".
    /// </summary>
    /// <returns>The new asmdef path, or null on failure.</returns>
    public static string SetAsmdefName(string asmdefPath, string newName)
    {
        if (string.IsNullOrEmpty(asmdefPath) || !File.Exists(asmdefPath))
        {
            Debug.LogError($"{LogPrefix} Assembly definition not found: \"{asmdefPath}\".");
            return null;
        }
        if (!CanRenameAsmdef(asmdefPath, newName))
            return null;

        asmdefPath = asmdefPath.Replace('\\', '/');
        SaveBackup(asmdefPath, asmdefPath);

        JObject json = JObject.Parse(File.ReadAllText(asmdefPath));
        json["name"] = newName;
        json["rootNamespace"] = newName;
        File.WriteAllText(asmdefPath, json.ToString());

        string newPath = GetRenamedAsmdefPath(asmdefPath, newName);
        if (!string.Equals(asmdefPath, newPath, System.StringComparison.Ordinal))
        {
            // Through a temporary name, so a change of case only also works on Windows.
            string tempPath = asmdefPath + ".renaming";
            File.Move(asmdefPath, tempPath);
            File.Move(tempPath, newPath);
            File.Move(asmdefPath + ".meta", tempPath + ".meta");
            File.Move(tempPath + ".meta", newPath + ".meta");
        }
        return newPath;
    }

    public static string GetAsmdefName(string asmdefPath)
    {
        JObject json = JObject.Parse(File.ReadAllText(asmdefPath));
        return json["name"]?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// The asmdef's "rootNamespace", or its "name" when the root namespace is empty.
    /// </summary>
    public static string GetAsmdefRootNamespace(string asmdefPath)
    {
        JObject json = JObject.Parse(File.ReadAllText(asmdefPath));
        string rootNamespace = json["rootNamespace"]?.ToString();
        return string.IsNullOrEmpty(rootNamespace) ? json["name"]?.ToString() ?? string.Empty : rootNamespace;
    }

    /// <summary>
    /// Every script in the asmdef's folder and sub-folders.
    /// </summary>
    public static string[] GetScriptPaths(string asmdefPath)
    {
        string folder = Path.GetDirectoryName(asmdefPath).Replace('\\', '/');
        string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folder });
        string[] paths = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
            paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
        return paths;
    }

    /// <summary>
    /// The asmdef in the folder of the project's single NamespaceHelper asset, or null (with an error)
    /// when there is not exactly one helper asset, or not exactly one asmdef in its folder.
    /// </summary>
    public static string FindWorldAsmdef()
    {
        string[] helperGuids = AssetDatabase.FindAssets($"t:{nameof(NamespaceHelper)}");
        if (helperGuids.Length != 1)
        {
            Debug.LogError($"{LogPrefix} Expected exactly one NamespaceHelper asset in the project, found {helperGuids.Length}. " +
                "Create one in the world scripts folder (Assets > Create > - Community Modules > NamespaceHelper), " +
                "or call RenameAll(asmdefPath, newNamespace).");
            return null;
        }

        string folder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(helperGuids[0])).Replace('\\', '/');
        string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { folder });
        if (asmdefGuids.Length != 1)
        {
            Debug.LogError($"{LogPrefix} Expected exactly one assembly definition in \"{folder}\" and its sub-folders, found {asmdefGuids.Length}.");
            return null;
        }
        return AssetDatabase.GUIDToAssetPath(asmdefGuids[0]);
    }

    private static string GetRenamedAsmdefPath(string asmdefPath, string newName)
    {
        return Path.Combine(Path.GetDirectoryName(asmdefPath), newName + ".asmdef").Replace('\\', '/');
    }

    // The asmdef can be renamed unless another file already has the new name.
    private static bool CanRenameAsmdef(string asmdefPath, string newName)
    {
        string newPath = GetRenamedAsmdefPath(asmdefPath, newName);
        bool sameFile = string.Equals(asmdefPath.Replace('\\', '/'), newPath, System.StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(newPath))
        {
            Debug.LogError($"{LogPrefix} Cannot rename the asmdef: \"{newPath}\" already exists.");
            return false;
        }
        return true;
    }

    private static bool RenameScriptNamespace(string scriptPath, string oldNamespace, string newNamespace, string asmdefPath)
    {
        return RewriteScriptNamespaces(scriptPath, asmdefPath, oldName =>
        {
            // Keep the sub-namespace of scripts already under the old root.
            if (oldNamespace.Length != 0 && oldName.StartsWith(oldNamespace + "."))
                return newNamespace + oldName.Substring(oldNamespace.Length);
            return newNamespace;
        });
    }

    private static bool RewriteScriptNamespaces(string scriptPath, string asmdefPath, System.Func<string, string> getNewName)
    {
        string content = File.ReadAllText(scriptPath);
        string updatedContent = NamespaceDeclaration.Replace(content, match => match.Groups[1].Value + getNewName(match.Groups[2].Value));
        if (updatedContent == content)
            return false;

        SaveBackup(scriptPath, asmdefPath);
        File.WriteAllText(scriptPath, updatedContent);
        return true;
    }

    private static bool IsInNamespace(string scriptNamespace, string rootNamespace)
    {
        return scriptNamespace == rootNamespace || scriptNamespace.StartsWith(rootNamespace + ".");
    }

    // Safety copy in "#Scripts Backup" next to the asmdef, saved as .txt so Unity does not compile it.
    private static void SaveBackup(string sourcePath, string asmdefPath)
    {
        string backupFolder = Path.Combine(Path.GetDirectoryName(asmdefPath), BackupFolderName);
        Directory.CreateDirectory(backupFolder);

        string timeStamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupPath = Path.Combine(backupFolder, Path.GetFileName(sourcePath) + $"._{timeStamp}.txt");
        File.Copy(sourcePath, backupPath, true);
    }
}
#endif
