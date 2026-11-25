using System;
using System.Collections.Generic;
using System.IO;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Every file shipped by the Community Modules releases we know (2.0 to 4.0) and every old Photon file, by GUID.
    ///     Lets the tool tell an untouched original from a modified original from a creator's own file.
    /// </summary>
    internal class KnownFiles
    {
        internal class Entry
        {
            public string Version;
            public string Path;
            public string Hash;   // Normalized hash (see MigrationIO.NormalizedHash)
            public string NsHash; // Same, with namespace lines neutralized (code only)
            public string Kind;   // code | asset | folder
        }

        public readonly Dictionary<string, List<Entry>> CommunityModules = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> Photon = new Dictionary<string, List<Entry>>();
        public readonly List<string> CmVersions = new List<string>();

        static KnownFiles _instance;

        public static KnownFiles Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        static KnownFiles Load()
        {
            var k = new KnownFiles();
            k.Read(MigrationConstants.KnownCommunityModulesDataGuid, k.CommunityModules, true);
            k.Read(MigrationConstants.KnownPhotonDataGuid, k.Photon, false);
            return k;
        }

        void Read(string dataGuid, Dictionary<string, List<Entry>> target, bool isCm)
        {
            string path = MigrationIO.AssetPathFromGuid(dataGuid);
            if (path == null)
                throw new FileNotFoundException("Migration tool data file is missing (GUID " + dataGuid + "). Re-import the migration tool package.");
            foreach (string raw in File.ReadAllLines(MigrationIO.ToAbsolute(path)))
            {
                if (raw.Length == 0 || raw[0] == '#')
                    continue;
                string[] c = raw.Split('\t');
                Entry e;
                if (isCm)
                {
                    if (c.Length < 6) continue;
                    e = new Entry { Version = c[0], Path = c[2], Hash = c[3], NsHash = c[4], Kind = c[5] };
                    if (!CmVersions.Contains(e.Version))
                        CmVersions.Add(e.Version);
                }
                else
                {
                    if (c.Length < 4) continue;
                    e = new Entry { Version = c[0], Path = c[2], Kind = c[3] };
                }
                if (!target.TryGetValue(c[1], out var list))
                    target[c[1]] = list = new List<Entry>();
                list.Add(e);
            }
        }

        public bool IsKnownCm(string guid) => guid != null && CommunityModules.ContainsKey(guid);

        public bool IsKnownPhoton(string guid) => guid != null && Photon.ContainsKey(guid);

        /// <summary>Versions in which this exact content shipped (empty if modified or unknown).</summary>
        public List<string> MatchingCmVersions(string guid, string hash, string nsHash)
        {
            var result = new List<string>();
            if (guid == null || !CommunityModules.TryGetValue(guid, out var list))
                return result;
            foreach (var e in list)
            {
                bool same = e.Hash == hash || (!string.IsNullOrEmpty(nsHash) && e.NsHash == nsHash);
                if (same && !result.Contains(e.Version))
                    result.Add(e.Version);
            }
            return result;
        }

        /// <summary>Path of this GUID in the target Community Modules release, or null if the release does not ship it.</summary>
        public string PathInTargetCm(string guid)
        {
            if (guid == null || !CommunityModules.TryGetValue(guid, out var list))
                return null;
            foreach (var e in list)
            {
                if (e.Version == "v" + MigrationConstants.TargetCommunityModulesVersion)
                    return e.Path;
            }
            return null;
        }

        public static string PrettyVersion(string v) => v.StartsWith("v") ? v.Substring(1).Replace("a", " Alpha") : v;
    }
}
