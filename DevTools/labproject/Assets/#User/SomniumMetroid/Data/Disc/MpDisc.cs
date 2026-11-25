using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// The player's own Metroid Prime disc image, read in place (nothing is copied or shipped).
    ///
    /// Containers: a plain GameCube image (.iso / .gcm, also NKit's .nkit.iso, which keeps a valid file table) and CISO
    /// (.ciso: "CISO", u32 LE block size, a 0x7FF8-byte map of present blocks, block data from 0x8000 in map order - measured on
    /// the user's PAL image: block size 0x200000). Disc: header at 0 (game id "GM8E01"/"GM8P01"/"GM8J01", name at 0x20),
    /// u32 BE file table (FST) offset at 0x424 and size at 0x428; FST entries are 12 bytes (u8 flags 1 = folder | u24 name offset,
    /// u32 file offset / parent, u32 file size / next index), the name table follows the last entry.
    ///
    /// Thread-safe: every read takes the lock (the area loader reads from a worker thread).
    /// </summary>
    public sealed class MpDisc : IDisposable
    {
        public sealed class FileEntry
        {
            public string Path;       // "Metroid1.pak", "Audio/frigate.dsp" (forward slashes, case as on the disc)
            public long Offset;
            public int Size;
        }

        public readonly string ImagePath;
        public string GameId { get; private set; }
        public string GameName { get; private set; }
        public int DiscVersion { get; private set; }
        public bool IsPal { get { return GameId != null && GameId.Length >= 4 && GameId[3] == 'P'; } }

        private readonly Dictionary<string, FileEntry> _files = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileEntry> _ordered = new List<FileEntry>();
        private FileStream _stream;
        private readonly object _lock = new object();

        // CISO
        private bool _ciso;
        private int _blockSize;
        private int[] _blockIndex;     // disc block -> stored block number, -1 = absent (zeros)
        private const int CisoHeader = 0x8000;

        public IList<FileEntry> Files { get { return _ordered; } }

        public MpDisc(string path)
        {
            ImagePath = path;
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            var head = new byte[8];
            ReadRaw(0, head, 0, 8);
            if (head[0] == 'C' && head[1] == 'I' && head[2] == 'S' && head[3] == 'O') OpenCiso();
            ReadHeader();
        }

        /// <summary>True when the file at <paramref name="path"/> looks like a Metroid Prime (GameCube) image; reads 0x8010 bytes at most.</summary>
        public static string Identify(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var b = new byte[8];
                    if (fs.Read(b, 0, 8) != 8) return null;
                    long at = 0;
                    if (b[0] == 'C' && b[1] == 'I' && b[2] == 'S' && b[3] == 'O') at = CisoHeader;
                    fs.Position = at;
                    var id = new byte[6];
                    if (fs.Read(id, 0, 6) != 6) return null;
                    string s = Encoding.ASCII.GetString(id);
                    return s.StartsWith("GM8", StringComparison.Ordinal) ? s : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OpenCiso()
        {
            var hdr = new byte[CisoHeader];
            ReadRaw(0, hdr, 0, CisoHeader);
            _blockSize = hdr[4] | (hdr[5] << 8) | (hdr[6] << 16) | (hdr[7] << 24);
            if (_blockSize <= 0 || _blockSize > (64 << 20)) throw new InvalidDataException("CISO block size " + _blockSize);
            int count = CisoHeader - 8;
            _blockIndex = new int[count];
            int n = 0;
            for (int i = 0; i < count; i++) _blockIndex[i] = hdr[8 + i] != 0 ? n++ : -1;
            _ciso = true;
        }

        private void ReadHeader()
        {
            var h = Read(0, 0x440);
            GameId = Encoding.ASCII.GetString(h, 0, 6);
            DiscVersion = h[7];
            int z = 0x20;
            while (z < 0x400 && h[z] != 0) z++;
            GameName = Encoding.ASCII.GetString(h, 0x20, z - 0x20);
            long fstOffset = MpReader.U32At(h, 0x424);
            int fstSize = (int)MpReader.U32At(h, 0x428);
            var fst = Read(fstOffset, fstSize);
            int entries = (int)MpReader.U32At(fst, 8);
            int names = entries * 12;
            ReadDir(fst, names, 1, entries, "");
        }

        private int ReadDir(byte[] fst, int names, int first, int end, string prefix)
        {
            int i = first;
            while (i < end)
            {
                int e = i * 12;
                bool dir = fst[e] != 0;
                int nameOff = (fst[e + 1] << 16) | (fst[e + 2] << 8) | fst[e + 3];
                int z = names + nameOff;
                while (fst[z] != 0) z++;
                string name = Encoding.ASCII.GetString(fst, names + nameOff, z - names - nameOff);
                if (dir)
                {
                    int next = (int)MpReader.U32At(fst, e + 8);
                    ReadDir(fst, names, i + 1, next, prefix + name + "/");
                    i = next;
                }
                else
                {
                    var f = new FileEntry { Path = prefix + name, Offset = MpReader.U32At(fst, e + 4), Size = (int)MpReader.U32At(fst, e + 8) };
                    _files[f.Path] = f;
                    _ordered.Add(f);
                    i++;
                }
            }

            return i;
        }

        public FileEntry Find(string path)
        {
            FileEntry f;
            return _files.TryGetValue(path, out f) ? f : null;
        }

        public byte[] ReadFile(string path)
        {
            FileEntry f = Find(path);
            return f == null ? null : Read(f.Offset, f.Size);
        }

        /// <summary>Bytes of the disc (not of the container) at a disc offset.</summary>
        public byte[] Read(long offset, int count)
        {
            var b = new byte[count];
            Read(offset, b, 0, count);
            return b;
        }

        public void Read(long offset, byte[] buffer, int index, int count)
        {
            lock (_lock)
            {
                if (!_ciso)
                {
                    ReadRaw(offset, buffer, index, count);
                    return;
                }

                while (count > 0)
                {
                    long block = offset / _blockSize;
                    int within = (int)(offset % _blockSize);
                    int n = Math.Min(count, _blockSize - within);
                    int stored = block < _blockIndex.Length ? _blockIndex[block] : -1;
                    if (stored < 0) Array.Clear(buffer, index, n);
                    else ReadRaw(CisoHeader + (long)stored * _blockSize + within, buffer, index, n);
                    offset += n;
                    index += n;
                    count -= n;
                }
            }
        }

        private void ReadRaw(long offset, byte[] buffer, int index, int count)
        {
            _stream.Position = offset;
            int got = 0;
            while (got < count)
            {
                int r = _stream.Read(buffer, index + got, count - got);
                if (r <= 0) throw new EndOfStreamException("disc image ends at " + (offset + got));
                got += r;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_stream != null) _stream.Dispose();
                _stream = null;
            }
        }
    }
}
