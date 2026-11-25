using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// The ANCS animation set (decomp CAnimationSet / CAnimation / CMetaAnimFactory), read only as far as the animations: u16 table
    /// count, u32 animation count, per animation the name and a meta animation (u32 type: 0 play = CPrimitive (ANIM id, u32 index,
    /// name) + CCharAnimTime (float, u32); 1 blend / 2 phase blend = two meta animations, float, bool; 3 random = u32 count of (meta
    /// animation, u32 weight); 4 sequence = u32 count of meta animations). Each entry keeps the ANIM ids it plays, in order.
    /// </summary>
    public sealed class MpAnimSet
    {
        public sealed class Entry
        {
            public string Name;
            public int Type;
            public readonly List<uint> Anims = new List<uint>();
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
                ReadMeta(r, e.Anims);
                s.Entries.Add(e);
            }

            return s;
        }

        private static void ReadMeta(MpReader r, List<uint> anims)
        {
            int type = r.S32();
            switch (type)
            {
                case 0:
                    anims.Add(r.U32());
                    r.U32();
                    r.CString();
                    r.F32();
                    r.U32();
                    break;
                case 1:
                case 2:
                    ReadMeta(r, anims);
                    ReadMeta(r, anims);
                    r.F32();
                    r.U8();
                    break;
                case 3:
                {
                    int n = r.S32();
                    for (int i = 0; i < n; i++) { ReadMeta(r, anims); r.U32(); }
                    break;
                }
                case 4:
                {
                    int n = r.S32();
                    for (int i = 0; i < n; i++) ReadMeta(r, anims);
                    break;
                }
                default:
                    throw new System.FormatException("meta animation type " + type);
            }
        }

        /// <summary>The first ANIM a character's animation number plays (CCharacterInfo index list, else the number itself).</summary>
        public uint AnimOf(MpAncs.Character c, int anim)
        {
            int idx = c.AnimIndex.Count > 0 ? (anim >= 0 && anim < c.AnimIndex.Count ? c.AnimIndex[anim] : -1) : anim;
            return idx >= 0 && idx < Entries.Count && Entries[idx].Anims.Count > 0 ? Entries[idx].Anims[0] : 0xFFFFFFFF;
        }
    }
}
