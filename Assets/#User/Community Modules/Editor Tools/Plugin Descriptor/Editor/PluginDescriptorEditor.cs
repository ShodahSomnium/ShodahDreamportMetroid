#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PluginDescriptor))]
public class PluginDescriptorEditor : Editor
{
    // "V1.2 [2026, 02, 28]"

    private string[] _scriptsPath = new string[0];
    private string[] _scriptsName = new string[0];
    private string[] _scriptsNameSpace = new string[0];
    private MonoScript[] _scripts = new MonoScript[0];
    private bool _advancedSection = false;

    private const string DATE_PATERN = @"^(0[1-9]|[12][0-9]|3[01])/(0[1-9]|1[0-2])/(\d{4})$";
    private const string VERSION_PATERN = @"^\d+(\.\d+)*$";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PluginDescriptor so = (PluginDescriptor)target;

        EditorGUILayout.Space();
        _advancedSection = EditorGUILayout.Foldout(_advancedSection, "Advanced");
        if (_advancedSection)
        {
            if (GUILayout.Button("Get Scripts", GUILayout.Width(80)))
            {
                string[] guids = GetAllScriptGUID(so);
                _scriptsPath = GetScriptsPath(guids);
                _scriptsName = GetScriptsName(guids);
                _scripts = GetScriptsReference(guids);
                so.Scripts = _scripts;
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // Check if the date string matches the pattern
        if (!Regex.IsMatch(so.DateVersion, DATE_PATERN))
        {
            so.DateVersion = DateTime.Now.ToString("dd/MM/yyyy");
        }

        // Check if the date string matches the pattern
        if (!Regex.IsMatch(so.Version, VERSION_PATERN))
        {
            so.Version = "1.0.0";
        }
    }

    private string[] GetAllScriptGUID(PluginDescriptor so)
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

    private string GetScriptNamespace(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string pattern = @"\bnamespace\s+([a-zA-Z_][\w\.]*)";
        var match = Regex.Match(content, pattern);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        return string.Empty;
    }
}
#endif