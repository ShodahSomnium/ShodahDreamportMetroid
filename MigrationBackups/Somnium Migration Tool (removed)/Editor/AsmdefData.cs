using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Somnium.MigrationTool
{
    /// <summary>Serializable mirror of an .asmdef file (JsonUtility drops unknown fields, which is fine for asmdefs).</summary>
    [Serializable]
    internal class AsmdefData
    {
        [Serializable]
        public class VersionDefine
        {
            public string name;
            public string expression;
            public string define;
        }

        public string name;
        public string rootNamespace;
        public List<string> references = new List<string>();
        public List<string> includePlatforms = new List<string>();
        public List<string> excludePlatforms = new List<string>();
        public bool allowUnsafeCode;
        public bool overrideReferences;
        public List<string> precompiledReferences = new List<string>();
        public bool autoReferenced = true;
        public List<string> defineConstraints = new List<string>();
        public List<VersionDefine> versionDefines = new List<VersionDefine>();
        public bool noEngineReferences;

        public static AsmdefData Parse(string json) => JsonUtility.FromJson<AsmdefData>(json);

        public static AsmdefData Read(string projectRelative) => Parse(File.ReadAllText(MigrationIO.ToAbsolute(projectRelative)));

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Display name of a reference ("GUID:xxx" is resolved to the asmdef name when it exists).</summary>
        public static string DescribeReference(string reference)
        {
            if (!reference.StartsWith("GUID:"))
                return reference;
            string path = MigrationIO.AssetPathFromGuid(reference.Substring(5));
            if (path == null)
                return reference + "  (missing)";
            try
            {
                return Read(path).name + "  (" + path + ")";
            }
            catch (Exception)
            {
                return reference + "  (" + path + ")";
            }
        }

        /// <summary>
        ///     Removes references to assemblies that the Photon update deletes and renames PhotonRealtime.
        ///     Returns a human readable list of changes (empty if nothing changed).
        /// </summary>
        public List<string> FixPhotonReferences()
        {
            var changes = new List<string>();
            for (int i = references.Count - 1; i >= 0; i--)
            {
                string r = references[i];
                if (r.StartsWith("GUID:"))
                {
                    if (Array.IndexOf(MigrationConstants.ObsoleteAssemblyGuids, r.Substring(5)) >= 0)
                    {
                        changes.Add("removed " + r);
                        references.RemoveAt(i);
                    }
                    continue;
                }
                if (Array.IndexOf(MigrationConstants.ObsoleteAssemblyNames, r) >= 0)
                {
                    changes.Add("removed " + r);
                    references.RemoveAt(i);
                    continue;
                }
                for (int k = 0; k < MigrationConstants.RenamedAssemblies.GetLength(0); k++)
                {
                    if (r == MigrationConstants.RenamedAssemblies[k, 0])
                    {
                        references[i] = MigrationConstants.RenamedAssemblies[k, 1];
                        changes.Add("renamed " + r + " to " + references[i]);
                    }
                }
            }
            return changes;
        }

        /// <summary>True if this asmdef references the given asmdef, by GUID or by name.</summary>
        public bool References(string guid, string asmName)
        {
            foreach (string r in references)
            {
                if (r == "GUID:" + guid || (!string.IsNullOrEmpty(asmName) && r == asmName))
                    return true;
            }
            return false;
        }

        public static void Write(string projectRelative, AsmdefData data)
        {
            File.WriteAllText(MigrationIO.ToAbsolute(projectRelative), data.ToJson());
            AssetDatabase.ImportAsset(projectRelative);
        }
    }
}
