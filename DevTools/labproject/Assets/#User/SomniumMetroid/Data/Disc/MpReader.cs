using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// A big-endian cursor over a byte array (the GameCube is big-endian: every Retro format is). No Span types: the Somnium
    /// upload server compiles against an mscorlib without them.
    /// </summary>
    public sealed class MpReader
    {
        public readonly byte[] Data;
        public int Pos;
        public readonly int Start, End;

        public MpReader(byte[] data) : this(data, 0, data.Length) { }

        public MpReader(byte[] data, int start, int length)
        {
            Data = data;
            Start = start;
            Pos = start;
            End = start + length;
        }

        public int Remaining { get { return End - Pos; } }
        public int Offset { get { return Pos - Start; } set { Pos = Start + value; } }

        public void Seek(int offsetFromStart) { Pos = Start + offsetFromStart; }
        public void Skip(int n) { Pos += n; }
        public void Align(int n) { int o = Pos - Start; o = (o + n - 1) / n * n; Pos = Start + o; }

        public byte U8() { return Data[Pos++]; }
        public sbyte S8() { return (sbyte)Data[Pos++]; }

        public ushort U16()
        {
            ushort v = (ushort)((Data[Pos] << 8) | Data[Pos + 1]);
            Pos += 2;
            return v;
        }

        public short S16() { return (short)U16(); }

        public uint U32()
        {
            uint v = ((uint)Data[Pos] << 24) | ((uint)Data[Pos + 1] << 16) | ((uint)Data[Pos + 2] << 8) | Data[Pos + 3];
            Pos += 4;
            return v;
        }

        public int S32() { return (int)U32(); }

        public ulong U64() { ulong hi = U32(); return (hi << 32) | U32(); }

        public float F32() { FloatBits b = default(FloatBits); b.U = U32(); return b.F; }

        public bool Bool() { return Data[Pos++] != 0; }

        public string FourCC()
        {
            string s = Encoding.ASCII.GetString(Data, Pos, 4);
            Pos += 4;
            return s;
        }

        /// <summary>A zero-terminated ASCII string.</summary>
        public string CString()
        {
            int s = Pos;
            while (Pos < End && Data[Pos] != 0) Pos++;
            string r = Encoding.ASCII.GetString(Data, s, Pos - s);
            if (Pos < End) Pos++;
            return r;
        }

        public string Ascii(int n)
        {
            string r = Encoding.ASCII.GetString(Data, Pos, n);
            Pos += n;
            int z = r.IndexOf('\0');
            return z >= 0 ? r.Substring(0, z) : r;
        }

        public byte[] Bytes(int n)
        {
            var b = new byte[n];
            Buffer.BlockCopy(Data, Pos, b, 0, n);
            Pos += n;
            return b;
        }

        public static uint U32At(byte[] d, int o) { return ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3]; }
        public static ushort U16At(byte[] d, int o) { return (ushort)((d[o] << 8) | d[o + 1]); }
        public static float F32At(byte[] d, int o) { FloatBits b = default(FloatBits); b.U = U32At(d, o); return b.F; }
    }

    /// <summary>The bits of a float without allocating (no unsafe code, no BitConverter.Int32BitsToSingle on older profiles).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct FloatBits
    {
        [FieldOffset(0)] public uint U;
        [FieldOffset(0)] public float F;
    }
}
