using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumMetroid.Data;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Finds the player's own Metroid Prime disc image (GameCube: .iso / .gcm / .ciso / .nkit.iso; identified by its game id GM8E01 /
    /// GM8P01 / GM8J01, never by name). The Quake III Dreamport's way (Q3Install): ask, never assume.
    /// 1. the path remembered from the last run (settings.txt in the client's persistent data folder, "MetroidDreamport");
    /// 2. the folders Dolphin is set up with (Dolphin.ini [General] ISOPath0..n, in Documents and AppData) - most players keep their
    ///    discs where Dolphin finds them;
    /// 3. a bounded search of every ready drive (depth 4, 6000 folders), the user's home and Downloads first.
    /// Every place tried goes in the report (logged), so a miss says exactly what the disk looked like from here. Runs on the loader
    /// thread. RVZ / WIA (Dolphin's compressed formats) are noticed and reported: not readable yet.
    /// </summary>
    public static class MpDiscFinder
    {
        private const int Depth = 4;
        private const int Budget = 6000;
        private static readonly string[] Extensions = { ".iso", ".gcm", ".ciso" };

        public sealed class Result
        {
            public string Path;
            public string GameId;
            public string Report;
        }

        public static string SettingsFolder;     // set by MpGame from Application.persistentDataPath

        public static string SettingsFile
        {
            get { return SettingsFolder == null ? null : System.IO.Path.Combine(SettingsFolder, "settings.txt"); }
        }

        public static string Remembered()
        {
            try
            {
                string f = SettingsFile;
                if (f == null || !File.Exists(f)) return null;
                foreach (string line in File.ReadAllLines(f))
                    if (line.StartsWith("disc=", StringComparison.OrdinalIgnoreCase)) return line.Substring(5).Trim();
            }
            catch (Exception) { }

            return null;
        }

        public static void Remember(string path)
        {
            try
            {
                if (SettingsFolder == null) return;
                Directory.CreateDirectory(SettingsFolder);
                var lines = new List<string>();
                if (File.Exists(SettingsFile))
                    foreach (string l in File.ReadAllLines(SettingsFile))
                        if (!l.StartsWith("disc=", StringComparison.OrdinalIgnoreCase)) lines.Add(l);
                lines.Insert(0, "disc=" + path);
                File.WriteAllLines(SettingsFile, lines.ToArray());
            }
            catch (Exception) { }
        }

        public static Result Find()
        {
            var log = new StringBuilder();
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string remembered = Remembered();
            if (!string.IsNullOrEmpty(remembered))
            {
                string id = Check(remembered, log);
                if (id != null) return Done(remembered, id, log, "remembered from the last run");
            }

            foreach (string folder in DolphinFolders(log))
            {
                string hit = SearchFolder(folder, 2, tried, log);
                if (hit != null) return Done(hit, MpDisc.Identify(hit), log, "Dolphin's game folder " + folder);
            }

            var roots = new List<string>();
            string home = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(home))
            {
                roots.Add(System.IO.Path.Combine(home, "Downloads"));
                roots.Add(System.IO.Path.Combine(home, "Documents"));
                roots.Add(System.IO.Path.Combine(home, "Desktop"));
            }

            log.Append("  drives: ");
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        log.Append(d.Name).Append(' ');
                        if (d.IsReady && d.DriveType != DriveType.CDRom) roots.Add(d.RootDirectory.FullName);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception e) { log.Append("<" + e.GetType().Name + ">"); }

            log.Append('\n');
            int budget = Budget;
            foreach (string r in roots)
            {
                string hit = Search(r, Depth, tried, log, ref budget);
                if (hit != null) return Done(hit, MpDisc.Identify(hit), log, "search under " + r);
                if (budget <= 0) { log.Append("  search budget used up\n"); break; }
            }

            return new Result { Report = log.ToString() };
        }

        private static Result Done(string path, string id, StringBuilder log, string how)
        {
            log.Append("  found ").Append(path).Append(" (").Append(id).Append(") - ").Append(how).Append('\n');
            Remember(path);
            return new Result { Path = path, GameId = id, Report = log.ToString() };
        }

        private static string Check(string path, StringBuilder log)
        {
            try
            {
                if (!File.Exists(path)) { log.Append("  ").Append(path).Append(": not there\n"); return null; }
                string id = MpDisc.Identify(path);
                log.Append("  ").Append(path).Append(": ").Append(id ?? "not a Metroid Prime GameCube image").Append('\n');
                return id;
            }
            catch (Exception e)
            {
                log.Append("  ").Append(path).Append(": ").Append(e.Message).Append('\n');
                return null;
            }
        }

        private static IEnumerable<string> DolphinFolders(StringBuilder log)
        {
            var inis = new List<string>();
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(docs)) inis.Add(System.IO.Path.Combine(docs, "Dolphin Emulator", "Config", "Dolphin.ini"));
            if (!string.IsNullOrEmpty(appdata)) inis.Add(System.IO.Path.Combine(appdata, "Dolphin Emulator", "Config", "Dolphin.ini"));
            var folders = new List<string>();
            foreach (string ini in inis)
            {
                try
                {
                    if (!File.Exists(ini)) continue;
                    foreach (string line in File.ReadAllLines(ini))
                    {
                        string t = line.Trim();
                        if (!t.StartsWith("ISOPath", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ISOPaths", StringComparison.OrdinalIgnoreCase)) continue;
                        int eq = t.IndexOf('=');
                        if (eq < 0) continue;
                        string f = t.Substring(eq + 1).Trim();
                        if (f.Length > 0 && !folders.Contains(f)) folders.Add(f);
                    }

                    log.Append("  Dolphin settings ").Append(ini).Append(": ").Append(folders.Count).Append(" game folder(s)\n");
                }
                catch (Exception e) { log.Append("  ").Append(ini).Append(": ").Append(e.Message).Append('\n'); }
            }

            return folders;
        }

        private static string SearchFolder(string folder, int depth, HashSet<string> tried, StringBuilder log)
        {
            int budget = 500;
            return Search(folder, depth, tried, log, ref budget);
        }

        private static string Search(string dir, int depth, HashSet<string> tried, StringBuilder log, ref int budget)
        {
            if (depth < 0 || budget <= 0 || string.IsNullOrEmpty(dir) || !tried.Add(dir)) return null;
            budget--;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { return null; }

            foreach (string f in files)
            {
                string lower = f.ToLowerInvariant();
                if (lower.EndsWith(".rvz") || lower.EndsWith(".wia"))
                {
                    log.Append("  ").Append(f).Append(": Dolphin's compressed format (RVZ/WIA) is not readable yet - convert it to ISO in Dolphin\n");
                    continue;
                }

                bool candidate = false;
                foreach (string ext in Extensions) if (lower.EndsWith(ext)) candidate = true;
                if (!candidate) continue;
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length < 100L * 1024 * 1024) continue;
                }
                catch (Exception) { continue; }

                string id = MpDisc.Identify(f);
                if (id != null) return f;
            }

            if (depth == 0) return null;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { return null; }

            foreach (string s in subs)
            {
                string name = System.IO.Path.GetFileName(s);
                if (name.StartsWith("$") || name.StartsWith(".") || name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Library", StringComparison.OrdinalIgnoreCase) || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                string hit = Search(s, depth - 1, tried, log, ref budget);
                if (hit != null) return hit;
                if (budget <= 0) return null;
            }

            return null;
        }
    }
}
