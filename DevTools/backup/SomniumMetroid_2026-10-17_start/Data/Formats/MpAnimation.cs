using System;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// ANIM (decomp CAllFormatsAnimSource: u32 format, 0 = CAnimSource, 2 = CFBStreamedCompression), decoded once into key frames:
    /// per bone id a rotation channel (quaternion w, x, y, z per key) and maybe an offset channel (x, y, z per key), and the frame
    /// number of every key (time = frame * interval). Sampling finds the keys around the time (CFBFullBodyAspectsForStream::SetTime),
    /// slerps the rotations and lerps the offsets; a non-looping animation holds its last key past its end.
    /// </summary>
    public sealed class MpAnimation
    {
        public float Duration, Interval;
        public int Format, Root, LastFrame;
        public bool Looping;
        public int[] KeyFrame = new int[0];                  // per key: its frame number
        public readonly int[] RotChannel = new int[256];     // bone id -> channel (-1 none)
        public readonly int[] OffChannel = new int[256];     // bone id -> offset channel (-1 none)
        public float[][] Rot;                                // per channel: 4 floats per key
        public float[][] Off;                                // per offset channel: 3 floats per key

        public int KeyCount { get { return KeyFrame.Length; } }

        public int BoneCount
        {
            get { int n = 0; for (int i = 0; i < 256; i++) if (RotChannel[i] >= 0) n++; return n; }
        }

        private MpAnimation()
        {
            for (int i = 0; i < 256; i++) { RotChannel[i] = -1; OffChannel[i] = -1; }
        }

        public static MpAnimation Read(byte[] b)
        {
            var a = new MpAnimation();
            var r = new MpReader(b);
            a.Format = r.S32();
            switch (a.Format)
            {
                case 0: a.ReadUncompressed(r); break;
                case 2: a.ReadStreamed(r); break;
                default: throw new FormatException("ANIM format " + a.Format);
            }

            return a;
        }

        // CAnimSource: duration, interval (CCharAnimTime: float, u32), u32 frames, u32 root, schar rotation channel per bone id,
        // schar offset channel per rotation channel, quaternions (channel-major: channel * frames + frame), offsets, u32 EVNT
        private void ReadUncompressed(MpReader r)
        {
            Duration = r.F32(); r.U32();
            Interval = r.F32(); r.U32();
            int frames = r.S32();
            Root = (int)(r.U32() & 0xFF);
            int nRot = r.S32();
            var rotCh = new int[nRot];
            for (int i = 0; i < nRot; i++) rotCh[i] = r.S8();
            int nOff = r.S32();
            var offCh = new int[nOff];
            for (int i = 0; i < nOff; i++) offCh[i] = r.S8();
            int q = r.S32();
            int rotChannels = frames > 0 ? q / frames : 0;
            Rot = new float[rotChannels][];
            for (int c = 0; c < rotChannels; c++)
            {
                Rot[c] = new float[frames * 4];
                for (int f = 0; f < frames * 4; f++) Rot[c][f] = r.F32();
            }

            int o = r.S32();
            int offChannels = frames > 0 ? o / frames : 0;
            Off = new float[offChannels][];
            for (int c = 0; c < offChannels; c++)
            {
                Off[c] = new float[frames * 3];
                for (int f = 0; f < frames * 3; f++) Off[c][f] = r.F32();
            }

            for (int bone = 0; bone < nRot && bone < 256; bone++)
            {
                int ch = rotCh[bone];
                if (ch < 0 || ch >= rotChannels) continue;
                RotChannel[bone] = ch;
                int oc = ch < nOff ? offCh[ch] : -1;
                if (oc >= 0 && oc < offChannels) OffChannel[bone] = oc;
            }

            KeyFrame = new int[frames];
            for (int i = 0; i < frames; i++) KeyFrame[i] = i;
            LastFrame = Math.Max(0, frames - 1);
            Looping = true;
        }

        // CFBStreamedCompression: u32 scratch size, u32 EVNT; main header (u32, max time, interval, root, looping, rotation value
        // for one, offset resolution, bone channel count, u32); time header (u32 bit count + bitmap words: bit n = frame n is a
        // key); u32; channels (u32 count, per channel u32 bone id, rotation header: u16 key count + 3 x (s16 initial, u8 bits) for
        // x y z (w is a 1-bit sign), offset header: u16 key count + 3 x (s16 initial, u8 bits) when non-zero); then the bit stream
        // (32-bit words, read from the low bit up): per key after the first, per channel the w sign, then x y z deltas, then the
        // offset deltas (CFBStreamedAnimReaderTotals::IncrementInto). Rotation x y z = sin(v * (pi/2) / value for one).
        private void ReadStreamed(MpReader r)
        {
            r.U32();
            r.U32();
            r.U32();
            Duration = r.F32();
            Interval = r.F32();
            Root = (int)(r.U32() & 0xFF);
            Looping = r.U32() != 0;
            float rotScale = (float)(Math.PI / 2.0) / Math.Max(1u, r.U32());
            float offScale = r.F32();
            r.U32();
            r.U32();

            int bitCount = r.S32();
            var bitmap = new uint[(bitCount + 31) / 32];
            for (int i = 0; i < bitmap.Length; i++) bitmap[i] = r.U32();
            r.U32();

            int channels = r.S32();
            var bone = new int[channels];
            var keys = new int[channels];
            var hasOff = new bool[channels];
            var init = new int[channels * 7];
            var bits = new int[channels * 7];
            for (int c = 0; c < channels; c++)
            {
                bone[c] = (int)(r.U32() & 0xFF);
                keys[c] = r.U16();
                bits[c * 7] = 1;
                if (keys[c] != 0)
                    for (int k = 1; k < 4; k++) { init[c * 7 + k] = r.S16(); bits[c * 7 + k] = r.U8(); }
                int offKeys = r.U16();
                hasOff[c] = offKeys != 0;
                if (hasOff[c])
                    for (int k = 4; k < 7; k++) { init[c * 7 + k] = r.S16(); bits[c * 7 + k] = r.U8(); }
            }

            int keyCount = channels > 0 ? keys[0] : 0;
            KeyFrame = new int[keyCount];
            int found = 0;
            for (int f = 0; f < bitCount && found < keyCount; f++)
                if ((bitmap[f >> 5] & (1u << (f & 31))) != 0) KeyFrame[found++] = f;
            for (; found < keyCount; found++) KeyFrame[found] = found > 0 ? KeyFrame[found - 1] + 1 : 0;
            LastFrame = (int)(0.5f + Duration / Math.Max(1e-6f, Interval));

            Rot = new float[channels][];
            Off = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                Rot[c] = new float[keyCount * 4];
                Off[c] = hasOff[c] ? new float[keyCount * 3] : null;
                if (bone[c] < 256) { RotChannel[bone[c]] = c; if (hasOff[c]) OffChannel[bone[c]] = c; }
            }

            var bitsIn = new BitLoader(r);
            var cur = (int[])init.Clone();
            for (int k = 0; k < keyCount; k++)
            {
                if (k > 0)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int at = c * 7;
                        cur[at] = (int)bitsIn.Unsigned(1);
                        for (int i = 1; i < 4; i++) cur[at + i] = (short)(cur[at + i] + bitsIn.Signed(bits[at + i]));
                        if (hasOff[c])
                            for (int i = 4; i < 7; i++) cur[at + i] = (short)(cur[at + i] + bitsIn.Signed(bits[at + i]));
                    }
                }

                // CFBStreamedAnimReaderTotals::CalculateDown
                for (int c = 0; c < channels; c++)
                {
                    int at = c * 7;
                    float x = (float)Math.Sin(rotScale * cur[at + 1]), y = (float)Math.Sin(rotScale * cur[at + 2]), z = (float)Math.Sin(rotScale * cur[at + 3]);
                    float w = (float)Math.Sqrt(Math.Max(0f, 1f - (x * x + y * y + z * z)));
                    float[] q = Rot[c];
                    q[k * 4] = cur[at] != 0 ? -w : w;
                    q[k * 4 + 1] = x; q[k * 4 + 2] = y; q[k * 4 + 3] = z;
                    if (hasOff[c])
                        for (int i = 0; i < 3; i++) Off[c][k * 3 + i] = cur[at + 4 + i] * offScale;
                }
            }
        }

        private sealed class BitLoader
        {
            private readonly MpReader _r;
            private uint _word;
            private int _bit;

            public BitLoader(MpReader r) { _r = r; _word = Next(); }

            private uint Next() { return _r.Remaining >= 4 ? _r.U32() : 0u; }

            public uint Unsigned(int bits)
            {
                uint result = 0;
                int shift = 0;
                while (bits > 0)
                {
                    int count = Math.Min(32 - _bit, bits);
                    uint chunk = (_word >> _bit) & (count == 32 ? 0xFFFFFFFFu : ((1u << count) - 1u));
                    result |= chunk << shift;
                    _bit += count;
                    shift += count;
                    bits -= count;
                    if (_bit == 32) { _bit = 0; _word = Next(); }
                }

                return result;
            }

            public int Signed(int bits)
            {
                if (bits <= 0) return 0;
                uint v = Unsigned(bits);
                if (bits < 32 && (v & (1u << (bits - 1))) != 0) v |= ~0u << bits;
                return (int)v;
            }
        }

        // the keys around time t and the fraction between them (CFBFullBodyAspectsForStream::SetTime)
        private float Keys(float t, out int k0, out int k1)
        {
            int n = KeyFrame.Length;
            if (n <= 1 || Interval <= 0f) { k0 = k1 = 0; return 0f; }
            if (t < 0f) t = 0f;
            if (t > Duration) t = Duration;
            float f = t / Interval;
            k0 = 0;
            while (k0 + 1 < n - 1 && KeyFrame[k0 + 1] < f) k0++;
            k1 = k0 + 1;
            int span = KeyFrame[k1] - KeyFrame[k0];
            float u = span > 0 ? (f - KeyFrame[k0]) / span : 1f;
            return u < 0f ? 0f : u > 1f ? 1f : u;
        }

        /// <summary>The bone's rotation (w, x, y, z) at <paramref name="t"/> seconds; false when the animation does not drive it.</summary>
        public bool Rotation(int bone, float t, float[] wxyz)
        {
            int ch = bone >= 0 && bone < 256 ? RotChannel[bone] : -1;
            if (ch < 0 || KeyFrame.Length == 0) return false;
            int k0, k1;
            float u = Keys(t, out k0, out k1);
            Slerp(Rot[ch], k0 * 4, Rot[ch], k1 * 4, u, wxyz);
            return true;
        }

        /// <summary>The bone's offset from its parent at <paramref name="t"/>; false when the animation has none for it.</summary>
        public bool Offset(int bone, float t, float[] xyz)
        {
            int ch = bone >= 0 && bone < 256 ? OffChannel[bone] : -1;
            if (ch < 0 || KeyFrame.Length == 0 || Off[ch] == null) return false;
            int k0, k1;
            float u = Keys(t, out k0, out k1);
            float[] k = Off[ch];
            for (int i = 0; i < 3; i++) xyz[i] = k[k0 * 3 + i] + (k[k1 * 3 + i] - k[k0 * 3 + i]) * u;
            return true;
        }

        public static void Slerp(float[] a, int ai, float[] b, int bi, float t, float[] o)
        {
            float aw = a[ai], ax = a[ai + 1], ay = a[ai + 2], az = a[ai + 3];
            float bw = b[bi], bx = b[bi + 1], by = b[bi + 2], bz = b[bi + 3];
            float dot = aw * bw + ax * bx + ay * by + az * bz;
            if (dot < 0f) { dot = -dot; bw = -bw; bx = -bx; by = -by; bz = -bz; }
            float s0, s1;
            if (dot > 0.9995f) { s0 = 1f - t; s1 = t; }
            else
            {
                float th = (float)Math.Acos(dot), sn = (float)Math.Sin(th);
                s0 = (float)Math.Sin((1f - t) * th) / sn;
                s1 = (float)Math.Sin(t * th) / sn;
            }

            float w = aw * s0 + bw * s1, x = ax * s0 + bx * s1, y = ay * s0 + by * s1, z = az * s0 + bz * s1;
            float len = (float)Math.Sqrt(w * w + x * x + y * y + z * z);
            if (len < 1e-8f) len = 1f;
            o[0] = w / len; o[1] = x / len; o[2] = y / len; o[3] = z / len;
        }
    }
}
