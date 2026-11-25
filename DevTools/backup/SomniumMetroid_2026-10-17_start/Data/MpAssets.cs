using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// Every resource of the game by its 32-bit asset id, across all the disc's .pak archives (a resource used in several worlds
    /// is stored in each of their paks; any copy will do). Also the named resources ("FRME_CombatHud" ...). Thread-safe reads.
    /// </summary>
    public sealed class MpAssets : IDisposable
    {
        public readonly MpDisc Disc;
        public readonly List<MpPak> Paks = new List<MpPak>();
        private readonly Dictionary<uint, MpPak.Entry> _byId = new Dictionary<uint, MpPak.Entry>();
        private readonly Dictionary<string, MpPak.Entry> _named = new Dictionary<string, MpPak.Entry>(StringComparer.OrdinalIgnoreCase);

        public MpAssets(string imagePath)
        {
            Disc = new MpDisc(imagePath);
            foreach (MpDisc.FileEntry f in Disc.Files)
            {
                if (!f.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) continue;
                MpPak pak;
                try { pak = new MpPak(Disc, f); }
                catch (Exception e) { MpLog.Warn("Disc: " + f.Path + " not read (" + e.Message + ")"); continue; }
                Paks.Add(pak);
                foreach (MpPak.Entry e in pak.Entries)
                    if (!_byId.ContainsKey(e.Id)) _byId[e.Id] = e;
                foreach (var kv in pak.Named)
                    if (!_named.ContainsKey(kv.Key)) _named[kv.Key] = kv.Value;
            }
        }

        public int Count { get { return _byId.Count; } }

        public MpPak.Entry Entry(uint id)
        {
            MpPak.Entry e;
            return _byId.TryGetValue(id, out e) ? e : null;
        }

        public MpPak.Entry Named(string name)
        {
            MpPak.Entry e;
            return _named.TryGetValue(name, out e) ? e : null;
        }

        public IEnumerable<KeyValuePair<string, MpPak.Entry>> AllNamed { get { return _named; } }

        public MpPak FindPak(string name)
        {
            foreach (MpPak p in Paks) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>The resource's bytes (decompressed), or null if the id is not on the disc.</summary>
        public byte[] Read(uint id)
        {
            MpPak.Entry e = Entry(id);
            return e == null ? null : e.Pak.Read(e);
        }

        public string TypeOf(uint id)
        {
            MpPak.Entry e = Entry(id);
            return e == null ? null : e.Type;
        }

        public static bool Valid(uint id) { return id != 0xFFFFFFFF && id != 0; }

        public void Dispose() { Disc.Dispose(); }
    }
}
