using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;

namespace Somnium.MigrationTool
{
    /// <summary>
    ///     Zip backups taken before every destructive action, stored in MigrationBackups/ next to Assets.
    ///     A backup lists "scopes" (folders or files, project-relative). Restoring replaces each scope entirely with its
    ///     backed-up content, and deletes scopes that did not exist when the backup was made.
    /// </summary>
    internal static class BackupManager
    {
        public static BackupRecord Create(int step, string label, IEnumerable<string> scopes)
        {
            var state = MigrationState.Current;
            var record = new BackupRecord
            {
                Id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_step" + step,
                Label = label,
                CreatedLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Step = step,
            };
            string folder = MigrationState.WorkFolder + "/" + record.Id;
            Directory.CreateDirectory(folder);
            string zipPath = folder + "/backup.zip";

            var files = new List<string>();
            foreach (string scope in scopes)
            {
                string abs = MigrationIO.ToAbsolute(scope);
                if (Directory.Exists(abs))
                {
                    record.Scopes.Add(scope);
                    foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                        files.Add(MigrationIO.ToProjectRelative(f));
                    if (File.Exists(abs + ".meta"))
                        files.Add(scope + ".meta");
                }
                else if (File.Exists(abs))
                {
                    record.Scopes.Add(scope);
                    files.Add(scope);
                    if (File.Exists(abs + ".meta"))
                        files.Add(scope + ".meta");
                }
                else
                {
                    record.MissingScopes.Add(scope);
                }
            }

            try
            {
                using (var stream = new FileStream(zipPath, FileMode.Create))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (i % 20 == 0)
                            EditorUtility.DisplayProgressBar("Migration backup", files[i], (float)i / Math.Max(1, files.Count));
                        var entry = zip.CreateEntry(files[i], CompressionLevel.Fastest);
                        using (var es = entry.Open())
                        using (var fs = File.OpenRead(MigrationIO.ToAbsolute(files[i])))
                            fs.CopyTo(es);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            File.WriteAllText(folder + "/info.json", UnityEngine.JsonUtility.ToJson(record, true));
            state.Backups.Add(record);
            state.Save();
            MigrationState.Log($"Backup '{label}' created: {files.Count} files, {MigrationIO.FormatSize(new FileInfo(zipPath).Length)} ({record.Id})");
            return record;
        }

        public static string ZipPath(BackupRecord record) => MigrationState.WorkFolder + "/" + record.Id + "/backup.zip";

        public static void Restore(BackupRecord record)
        {
            string zipPath = ZipPath(record);
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("Backup file not found: " + zipPath);

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string scope in record.Scopes)
                    DeleteScope(scope);
                foreach (string scope in record.MissingScopes)
                    DeleteScope(scope);

                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    int i = 0;
                    foreach (var entry in zip.Entries)
                    {
                        if (i++ % 20 == 0)
                            EditorUtility.DisplayProgressBar("Restoring backup", entry.FullName, (float)i / Math.Max(1, zip.Entries.Count));
                        string abs = MigrationIO.ToAbsolute(entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(abs));
                        using (var es = entry.Open())
                        using (var fs = new FileStream(abs, FileMode.Create))
                            es.CopyTo(fs);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            record.Restored = true;
            if (record.Step == 4)
                MigrationIO.RestoreDefines();
            MigrationState.Current.Save();
            MigrationState.Log($"Backup '{record.Label}' restored ({record.Id})");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        static void DeleteScope(string scope)
        {
            string abs = MigrationIO.ToAbsolute(scope);
            if (Directory.Exists(abs))
                Directory.Delete(abs, true);
            else if (File.Exists(abs))
                File.Delete(abs);
            if (File.Exists(abs + ".meta"))
                File.Delete(abs + ".meta");
        }
    }
}
