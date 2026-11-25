#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "Plugin Descriptor", menuName = "- Community Modules/Plugin Descriptor")]
public class PluginDescriptor : ScriptableObject
{
    public string Name;
    public string Author;
    public string Version = "1.0.0";
    public string DateVersion = DateTime.Now.ToString("dd/MM/yyyy");
    [TextArea(0, 12)] public string Notes = "";
    
    [Space]

    public MonoScript[] Scripts;
    public PluginDescriptor[] Dependencies;

    //public string[] AssemblyDefinitionReferences; // TODO
}
# endif