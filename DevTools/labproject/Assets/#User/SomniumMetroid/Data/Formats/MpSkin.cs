using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// CSKR skin rules (decomp DolphinCSkinRules.cpp / DolphinCVirtualBone.cpp): u32 virtual bone count, per virtual bone u32 weight
    /// count, (u32 bone id, float weight) each (the game keeps the first three), u32 vertex count; then the point and normal counts.
    /// The virtual bones cover the model's position array in order (bone 0 the first N0 positions, ...), and the normal array the
    /// same way: a vertex's skinning is found from its position (or normal) index.
    /// </summary>
    public sealed class MpSkin
    {
        public sealed class Group
        {
            public int Count;
            public readonly List<int> Bones = new List<int>();
            public readonly List<float> Weights = new List<float>();
        }

        public readonly List<Group> Groups = new List<Group>();
        public int PointCount, NormalCount;

        public static MpSkin Read(byte[] b)
        {
            var s = new MpSkin();
            var r = new MpReader(b);
            int n = r.S32();
            for (int i = 0; i < n; i++)
            {
                var g = new Group();
                int w = r.S32();
                for (int k = 0; k < w; k++)
                {
                    int bone = (int)(r.U32() & 0xFF);
                    float weight = r.F32();
                    if (k < 3) { g.Bones.Add(bone); g.Weights.Add(weight); }
                }

                g.Count = r.S32();
                s.Groups.Add(g);
            }

            s.PointCount = CountOf(r);
            s.NormalCount = CountOf(r);
            return s;
        }

        // StreamFloatToShort: a count (then count * 3 floats the Dolphin build skips), or -1 then the count
        private static int CountOf(MpReader r)
        {
            if (r.Remaining < 4) return -1;
            int c = r.S32();
            if (c == -1) return r.Remaining >= 4 ? r.S32() : -1;
            r.Skip(c * 12);
            return c;
        }

        /// <summary>Per array index (position or normal) the virtual bone that skins it (-1 past the end).</summary>
        public int[] GroupPerIndex(int length)
        {
            var map = new int[length];
            int at = 0;
            for (int g = 0; g < Groups.Count; g++)
                for (int k = 0; k < Groups[g].Count && at < length; k++) map[at++] = g;
            for (; at < length; at++) map[at] = -1;
            return map;
        }
    }
}
