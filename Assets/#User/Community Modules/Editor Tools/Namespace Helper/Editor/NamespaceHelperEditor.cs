#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// The file work is done by NamespaceHelperUtility, which can also be called from code.
[CustomEditor(typeof(NamespaceHelper))]
public class NamespaceHelperEditor : Editor
{
    [Header("V1.3 [2026, 10, 15]")]
    private string _asmdefAssetGuid = string.Empty;
    private string _asmdefAssetPath;
    private string _rootNamespace;
    private string[] _scriptsPath = new string[0];
    private string[] _scriptsName = new string[0];
    private string[] _scriptsNameSpace = new string[0];
    private MonoScript[] _scripts = new MonoScript[0];

    private bool _initFlag = false;
    private bool _multipleASMDEFsFound = false;
    private bool _asmdefNotFound = false;
    private bool _sectionAsmdef = false;
    private bool _sectionScripts = false;

    private GUIStyle _fontStyle;

    private NamespaceHelperEditor()
    {
        EditorApplication.projectChanged += Setup;
    }

    private void Setup()
    {
        _fontStyle = new() { fontSize = 18 };
        _fontStyle.normal.textColor = Color.white;
        _fontStyle.fontStyle = FontStyle.Bold;
        ScanProject();
    }

    private void ScanProject()
    {
        Debug.Log($"[{nameof(NamespaceHelperEditor)}] Scan Project");
        NamespaceHelper so = (NamespaceHelper)target;

        string[] asmdefGuids = GetASMDEFsInProject(so);
        int asmdefCOunt = asmdefGuids.Length;
        if (asmdefCOunt > 1)
        {
            _multipleASMDEFsFound = true;
            _asmdefNotFound = false;
            return;
        }
        else if (asmdefCOunt == 0)
        {
            _multipleASMDEFsFound = false;
            _asmdefNotFound = true;
            return;
        }
        else
        {
            _multipleASMDEFsFound = false;
            _asmdefNotFound = false;
        }

        _asmdefAssetGuid = asmdefGuids[0];
        _asmdefAssetPath = AssetDatabase.GUIDToAssetPath(_asmdefAssetGuid);
        _rootNamespace = NamespaceHelperUtility.GetAsmdefName(_asmdefAssetPath);
        string[] guids = GetAllScriptGUID(so);
        _scriptsPath = GetScriptsPath(guids);
        _scriptsName = GetScriptsName(guids);
        _scripts = GetScriptsReference(guids);
        _scriptsNameSpace = GetScriptsNameSpace(_scriptsPath);
    }

    public override void OnInspectorGUI()
    {
        // Run once check
        if (!_initFlag)
        {
            Setup();
            _initFlag = true;
        }

        //DrawDefaultInspector();

        NamespaceHelper so = (NamespaceHelper)target;

        CheckASMDEFAsset(so);

        if (_multipleASMDEFsFound)
        {
            EditorGUILayout.HelpBox("More than one AssemblyDefinition found !", MessageType.Warning);
            return;
        }

        if (_asmdefNotFound)
        {
            EditorGUILayout.HelpBox("No AssemblyDefinition file found in folder or sub-folders", MessageType.Warning);
            return;
        }

        ///

        EditorGUILayout.Space(6);
        _fontStyle.fontSize = 18;
        EditorGUILayout.LabelField("Namespace Helper", _fontStyle);
        EditorGUILayout.LabelField("Rename the namespace of all your scripts in one click !");

        ///

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        _rootNamespace = EditorGUILayout.TextField("Namespace:", _rootNamespace);
        _rootNamespace = NamespaceHelperUtility.FormatNamespace(_rootNamespace);
        if (GUILayout.Button("Rename All", GUILayout.Width(80)))
        {
            if (_asmdefAssetGuid.Length != 0 && _rootNamespace.Length != 0)
                NamespaceHelperUtility.RenameAll(_asmdefAssetPath, _rootNamespace);
        }
        EditorGUILayout.EndHorizontal();

        ///

        EditorGUILayout.Space(6);
        _sectionAsmdef = EditorGUILayout.BeginFoldoutHeaderGroup(_sectionAsmdef, "Assembly Definition Asset");
        if (_sectionAsmdef)
        {
            EditorGUILayout.LabelField("This is the *.asmdef file of your project");

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            so.ASMDEFAsset = (AssemblyDefinitionAsset)EditorGUILayout.ObjectField(so.ASMDEFAsset, typeof(AssemblyDefinitionAsset), false);

            if (so.ASMDEFAsset == null)
                return;

            _rootNamespace = EditorGUILayout.TextField(_rootNamespace);
            _rootNamespace = NamespaceHelperUtility.FormatNamespace(_rootNamespace);
            if (GUILayout.Button("Rename", GUILayout.Width(60)))
            {
                if (_asmdefAssetGuid.Length != 0 && _rootNamespace.Length != 0)
                {
                    NamespaceHelperUtility.SetAsmdefName(_asmdefAssetPath, _rootNamespace);
                    AssetDatabase.Refresh();
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        ///

        EditorGUILayout.Space(6);
        _sectionScripts = EditorGUILayout.BeginFoldoutHeaderGroup(_sectionScripts, "Scripts");
        if (_sectionScripts)
        {
            EditorGUILayout.LabelField("Here is the list of all the scripts under the Assembly Definition Asset (Same folder or lower)");
            EditorGUILayout.LabelField("You can individually rename the namespace of scripts here.");
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scripts");
            EditorGUILayout.LabelField("NameSpace");
            EditorGUILayout.EndHorizontal();

            for (int n = 0; n < _scriptsName.Length; n++)
            {
                if (n < _scripts.Length)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(_scripts[n], typeof(MonoScript), false);
                    _scriptsNameSpace[n] = EditorGUILayout.TextField(_scriptsNameSpace[n]);
                    if (GUILayout.Button("Rename", GUILayout.Width(60)))
                    {
                        NamespaceHelperUtility.SetScriptNamespace(_scriptsPath[n], _scriptsNameSpace[n], _asmdefAssetPath);
                        AssetDatabase.Refresh();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rename all scripts", GUILayout.Width(160)))
            {
                for (int n = 0; n < _scriptsPath.Length; n++)
                {
                    if (_rootNamespace.Length != 0)
                        NamespaceHelperUtility.SetScriptNamespace(_scriptsPath[n], _rootNamespace, _asmdefAssetPath);
                }
                AssetDatabase.Refresh();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private string[] GetAllScriptGUID(NamespaceHelper so)
    {
        string assetPath = AssetDatabase.GetAssetPath(so);
        if (assetPath.Length == 0) // Can happen when the asset is been created
            return new string[0];
        string folder = Path.GetDirectoryName(assetPath);
        return AssetDatabase.FindAssets("t:MonoScript", new[] { folder });
    }

    private string[] GetScriptsPath(string[] guids)
    {
        string[] scriptsPath = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            scriptsPath[i] = path;
        }
        return scriptsPath;
    }

    private string[] GetScriptsName(string[] guids)
    {
        string[] scriptsName = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            scriptsName[i] = Path.GetFileName(path);
        }
        return scriptsName;
    }

    private MonoScript[] GetScriptsReference(string[] guids)
    {
        MonoScript[] scripts = new MonoScript[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            scripts[i] = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        }
        return scripts;
    }

    private string[] GetScriptsNameSpace(string[] paths)
    {
        string[] scriptsNameSpace = new string[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            scriptsNameSpace[i] = NamespaceHelperUtility.GetScriptNamespace(paths[i]);
        }
        return scriptsNameSpace;
    }

    private void CheckASMDEFAsset(NamespaceHelper so)
    {
        if (so.ASMDEFAsset == null)
        {
            string assetPath = AssetDatabase.GetAssetPath(so);
            if (assetPath.Length == 0) // Can happen when the asset is been created
                return;
            string folder = System.IO.Path.GetDirectoryName(assetPath);
            string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { folder });
            foreach (string guid in guids)
            {
                string firstPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(folder, Path.GetDirectoryName(firstPath), System.StringComparison.OrdinalIgnoreCase))
                {
                    AssemblyDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(firstPath);
                    if (asset != null)
                    {
                        so.ASMDEFAsset = asset;
                        EditorUtility.SetDirty(so);
                        Debug.Log($"[{nameof(NamespaceHelperEditor)}] Auto assign ASMDEF asset.");
                    }
                }
            }
        }
    }

    private string[] GetASMDEFsInProject(NamespaceHelper so)
    {
        string assetPath = AssetDatabase.GetAssetPath(so);
        if (assetPath.Length == 0) // Can happen when the asset is been created
            return new string[0];
        string folder = Path.GetDirectoryName(assetPath);
        string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { folder });
        return guids;
    }
}
#endif
