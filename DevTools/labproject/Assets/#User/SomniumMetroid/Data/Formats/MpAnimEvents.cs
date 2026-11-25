using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// EVNT, an animation's points of interest (decomp CAnimPOIData / CPOINode and its kinds): u32 version, then bool, int32 and
    /// particle node lists, and from version 2 the sound nodes. Every node: u16, name, u16 type, CCharAnimTime (float, u32), u32
    /// index, bool unique, float weight, s32 character index (-1 = any), s32 flags; then a bool, an int32 + locator name, the particle
    /// data (u32 duration, FourCC + id, bone name, float scale, u32 parent mode), or the sound (s32 sound id, falloff, max distance).
    /// The game plays a sound node's sound (a data id: through the sound lookup) and starts a particle node's effect on its bone.
    /// </summary>
    public sealed class MpAnimEvents
    {
        public struct Node
        {
            public string Name;
            public int Type;
            public float Time;
            public int CharIndex;
            public int Flags;
            public float Weight;
        }

        public struct Sound
        {
            public Node N;
            public int SfxId;
            public float Falloff, MaxDist;
        }

        public struct Particle
        {
            public Node N;
            public int Duration;
            public string Kind;      // PART, SWHC, ELSC
            public uint Id;
            public string Bone;
            public float Scale;
            public int ParentMode;
        }

        public struct Int32Event
        {
            public Node N;
            public int Value;
            public string Locator;
        }

        public struct BoolEvent
        {
            public Node N;
            public bool Value;
        }

        public readonly List<BoolEvent> Bools = new List<BoolEvent>();
        public readonly List<Int32Event> Ints = new List<Int32Event>();
        public readonly List<Particle> Particles = new List<Particle>();
        public readonly List<Sound> Sounds = new List<Sound>();

        private static Node ReadNode(MpReader r)
        {
            var n = new Node();
            r.U16();
            n.Name = r.CString();
            n.Type = r.U16();
            n.Time = r.F32();
            r.U32();
            r.U32();
            r.U8();
            n.Weight = r.F32();
            n.CharIndex = r.S32();
            n.Flags = r.S32();
            return n;
        }

        public static MpAnimEvents Read(byte[] b)
        {
            var e = new MpAnimEvents();
            if (b == null) return e;
            var r = new MpReader(b);
            uint version = r.U32();
            int n = r.S32();
            for (int i = 0; i < n; i++) e.Bools.Add(new BoolEvent { N = ReadNode(r), Value = r.U8() != 0 });
            n = r.S32();
            for (int i = 0; i < n; i++) { Node nd = ReadNode(r); e.Ints.Add(new Int32Event { N = nd, Value = r.S32(), Locator = r.CString() }); }
            n = r.S32();
            for (int i = 0; i < n; i++)
            {
                Node nd = ReadNode(r);
                var p = new Particle { N = nd, Duration = r.S32(), Kind = r.FourCC(), Id = r.U32(), Bone = r.CString(), Scale = r.F32(), ParentMode = r.S32() };
                e.Particles.Add(p);
            }

            if (version > 1 && r.Remaining >= 4)
            {
                n = r.S32();
                for (int i = 0; i < n; i++) { Node nd = ReadNode(r); e.Sounds.Add(new Sound { N = nd, SfxId = r.S32(), Falloff = r.F32(), MaxDist = r.F32() }); }
            }

            return e;
        }
    }
}
