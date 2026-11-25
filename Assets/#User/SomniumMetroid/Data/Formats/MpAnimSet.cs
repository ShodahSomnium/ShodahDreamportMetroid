using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// The ANCS animation set (decomp CAnimationSet / CAnimation / CMetaAnimFactory), read only as far as the animations: u16 table
    /// count, u32 animation count, per animation the name and a meta animation (u32 type: 0 play = CPrimitive (ANIM id, u32 index,
    /// name) + CCharAnimTime (float, u32); 1 blend / 2 phase blend = two meta animations, float, bool; 3 random = u32 count of (meta
    /// animation, u32 weight); 4 sequence = u32 count of meta animations). Each entry keeps the ANIM ids it plays, in order: a
    /// sequence plays them one after the other (CMetaAnimSequence), a random entry picks one of its <see cref="Entry.Options"/> by
    /// weight (CMetaAnimRandom), a blend shows its first part.
    /// </summary>
    public sealed class MpAnimSet
    {
        public const int Play = 0, Blend = 1, PhaseBlend = 2, Random = 3, Sequence = 4;

        public sealed class Entry
        {
            public string Name;
            public int Type;
            public readonly List<uint> Anims = new List<uint>();
            public readonly List<uint> Options = new List<uint>();     // random: the first ANIM of each choice
            public readonly List<int> Weights = new List<int>();
            public readonly List<uint> Steps = new List<uint>();       // sequence: the first ANIM of each step

            /// <summary>The ANIMs this entry plays now, in order (a random entry rolls with <paramref name="roll"/> in 0..1).</summary>
            public List<uint> Pick(float roll, List<uint> into)
            {
                into.Clear();
                if (Type == Sequence && Steps.Count > 0) { into.AddRange(Steps); return into; }
                if (Type == Random && Options.Count > 0)
                {
                    int total = 0;
                    foreach (int w in Weights) total += w > 0 ? w : 1;
                    float at = roll * total;
                    for (int i = 0; i < Options.Count; i++)
                    {
                        at -= Weights[i] > 0 ? Weights[i] : 1;
                        if (at <= 0f || i == Options.Count - 1) { into.Add(Options[i]); return into; }
                    }
                }

                if (Anims.Count > 0) into.Add(Anims[0]);
                return into;
            }
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public static MpAnimSet Read(byte[] b, int offset)
        {
            var s = new MpAnimSet();
            var r = new MpReader(b);
            r.Seek(offset);
            r.U16();
            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                var e = new Entry { Name = r.CString() };
                e.Type = (int)MpReader.U32At(b, r.Pos);
                ReadMeta(r, e.Anims, e, true);
                s.Entries.Add(e);
            }

            return s;
        }

        private static void ReadMeta(MpReader r, List<uint> anims, Entry top, bool isTop)
        {
            int type = r.S32();
            switch (type)
            {
                case Play:
                    anims.Add(r.U32());
                    r.U32();
                    r.CString();
                    r.F32();
                    r.U32();
                    break;
                case Blend:
                case PhaseBlend:
                    ReadMeta(r, anims, top, false);
                    ReadMeta(r, anims, top, false);
                    r.F32();
                    r.U8();
                    break;
                case Random:
                {
                    int n = r.S32();
                    for (int i = 0; i < n; i++)
                    {
                        int before = anims.Count;
                        ReadMeta(r, anims, top, false);
                        int w = r.S32();
                        if (isTop && anims.Count > before) { top.Options.Add(anims[before]); top.Weights.Add(w); }
                    }

                    break;
                }
                case Sequence:
                {
                    int n = r.S32();
                    for (int i = 0; i < n; i++)
                    {
                        int before = anims.Count;
                        ReadMeta(r, anims, top, false);
                        if (isTop && anims.Count > before) top.Steps.Add(anims[before]);
                    }

                    break;
                }
                default:
                    throw new System.FormatException("meta animation type " + type);
            }
        }

        /// <summary>The animation set entry a character's animation number plays (CCharacterInfo index list, else the number itself), or -1.</summary>
        public int EntryOf(MpAncs.Character c, int anim)
        {
            int idx = c.AnimIndex.Count > 0 ? (anim >= 0 && anim < c.AnimIndex.Count ? c.AnimIndex[anim] : -1) : anim;
            return idx >= 0 && idx < Entries.Count ? idx : -1;
        }

        /// <summary>The first ANIM a character's animation number plays.</summary>
        public uint AnimOf(MpAncs.Character c, int anim)
        {
            int idx = EntryOf(c, anim);
            return idx >= 0 && Entries[idx].Anims.Count > 0 ? Entries[idx].Anims[0] : 0xFFFFFFFF;
        }
    }
}
