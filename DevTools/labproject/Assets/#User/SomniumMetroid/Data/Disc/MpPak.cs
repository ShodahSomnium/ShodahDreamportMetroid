using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// A Metroid Prime (GameCube) .pak archive, read from the disc. Layout (big-endian): u16 3, u16 5, u32 0; named resources
    /// (u32 count; FourCC type, u32 id, u32 name length, name); resources (u32 count; u32 compressed, FourCC type, u32 id,
    /// u32 size, u32 offset from the pak start). A compressed resource is u32 decompressed size + a zlib stream.
    /// </summary>
    public sealed class MpPak
    {
        public sealed class Entry
        {
            public MpPak Pak;
            public string Type;
            public uint Id;
            public bool Compressed;
            public int Size;
            public long Offset;     // in the pak
            public override string ToString() { return Type + " " + Id.ToString("X8") + " (" + Pak.Name + ")"; }
        }

        public readonly string Name;
        public readonly MpDisc Disc;
        public readonly long DiscOffset;
        public readonly List<Entry> Entries = new List<Entry>();
        public readonly Dictionary<string, Entry> Named = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<uint, Entry> ById = new Dictionary<uint, Entry>();

        public MpPak(MpDisc disc, MpDisc.FileEntry file)
        {
            Disc = disc;
            Name = file.Path;
            DiscOffset = file.Offset;
            // the tables sit at the start; read a generous head, more if needed
            int head = Math.Min(file.Size, 1 << 16);
            byte[] h = disc.Read(DiscOffset, head);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Parse(h);
                    return;
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException || ex is ArgumentException)
                {
                    if (head >= file.Size) throw;
                    head = Math.Min(file.Size, head * 4);
                    h = disc.Read(DiscOffset, head);
                    Entries.Clear();
                    Named.Clear();
                    ById.Clear();
                }
            }
        }

        private void Parse(byte[] h)
        {
            var r = new MpReader(h);
            ushort major = r.U16(), minor = r.U16();
            r.U32();
            if (major != 3 || minor != 5) throw new InvalidDataException(Name + ": pak version " + major + "." + minor);
            int named = r.S32();
            var names = new List<KeyValuePair<string, uint>>();
            for (int i = 0; i < named; i++)
            {
                string type = r.FourCC();
                uint id = r.U32();
                int len = r.S32();
                string name = r.Ascii(len);
                names.Add(new KeyValuePair<string, uint>(name, id));
            }

            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                var e = new Entry { Pak = this, Compressed = r.U32() != 0, Type = r.FourCC(), Id = r.U32(), Size = r.S32(), Offset = r.U32() };
                Entries.Add(e);
                if (!ById.ContainsKey(e.Id)) ById[e.Id] = e;
            }

            foreach (var kv in names)
            {
                Entry e;
                if (ById.TryGetValue(kv.Value, out e)) Named[kv.Key] = e;
            }
        }

        /// <summary>The resource's bytes, decompressed.</summary>
        public byte[] Read(Entry e)
        {
            byte[] raw = Disc.Read(DiscOffset + e.Offset, e.Size);
            if (!e.Compressed) return raw;
            int size = (int)MpReader.U32At(raw, 0);
            return Inflate(raw, 4, raw.Length - 4, size);
        }

        /// <summary>A zlib stream (2-byte header, deflate, adler32) into exactly <paramref name="size"/> bytes.</summary>
        public static byte[] Inflate(byte[] src, int offset, int length, int size)
        {
            var dst = new byte[size];
            using (var ms = new MemoryStream(src, offset + 2, length - 2, false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < size)
                {
                    int n = ds.Read(dst, got, size - got);
                    if (n <= 0) break;
                    got += n;
                }

                if (got != size) throw new InvalidDataException("inflate gave " + got + " of " + size + " bytes");
            }

            return dst;
        }
    }
}
