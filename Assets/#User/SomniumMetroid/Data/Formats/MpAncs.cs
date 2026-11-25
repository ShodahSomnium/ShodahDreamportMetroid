using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// ANCS character set (decomp CAnimCharacterSet / CCharacterSet / CCharacterInfo / CPASDatabase): u16 version, then the
    /// character set (u16 table count, u32 count, per character: u32 id, u16 table count, name, CMDL, CSKR, CINF, animation names
    /// (u32 count + (u32 index, string, string)), PAS database ("PAS4", u32 state count, u32 default state, states: u32 id, u32 parm
    /// count, u32 anim count, parm infos (type, weight fn, weight, min, max by type), anims (u32 id + one value per parm)),
    /// particle resources (PART, SWHC, ELSC id vectors, a set when table count &gt; 5), u32, [&gt; 1: AABBs (name + box)],
    /// [&gt; 2: effects (name + components: name, FourCC, id, bone name, scale, parented mode, flags)], [&gt; 3: overlay CMDL + CSKR],
    /// [&gt; 4: animation index vector]). The animation set that follows is read by MpAnimSet (when animations are played).
    /// </summary>
    public sealed class MpAncs
    {
        public sealed class Character
        {
            public uint Id;
            public string Name;
            public uint Cmdl, Cskr, Cinf, OverlayCmdl, OverlayCskr;
            public readonly List<KeyValuePair<int, string>> AnimNames = new List<KeyValuePair<int, string>>();
            public readonly List<int> AnimIndex = new List<int>();     // CCharacterInfo::GetAnimationIndexList: anim number -> set entry
            public readonly List<uint> Particles = new List<uint>();
            public readonly List<uint> Swooshes = new List<uint>();
            public readonly List<uint> Electrics = new List<uint>();
            public readonly List<Effect> Effects = new List<Effect>();
        }

        public sealed class Effect
        {
            public string Name;
            public readonly List<EffectComponent> Components = new List<EffectComponent>();
        }

        public struct EffectComponent
        {
            public string Name, Type, Bone;
            public uint Id;
            public float Scale;
            public uint ParentedMode, Flags;
        }

        public readonly List<Character> Characters = new List<Character>();
        public int AnimSetOffset;      // where the animation set starts (MpAnimSet)

        public static MpAncs Read(byte[] b)
        {
            var a = new MpAncs();
            var r = new MpReader(b);
            r.U16();
            r.U16();
            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                var c = new Character { Id = r.U32() };
                int table = r.U16();
                c.Name = r.CString();
                c.Cmdl = r.U32();
                c.Cskr = r.U32();
                c.Cinf = r.U32();
                int anims = r.S32();
                for (int k = 0; k < anims; k++)
                {
                    int idx = r.S32();
                    string s1 = r.CString();
                    string s2 = r.CString();
                    c.AnimNames.Add(new KeyValuePair<int, string>(idx, s2.Length > 0 ? s2 : s1));
                }

                SkipPas(r);
                ReadIds(r, c.Particles);
                ReadIds(r, c.Swooshes);
                ReadIds(r, c.Electrics);
                if (table > 5) ReadIds(r, c.Electrics);
                r.U32();
                if (table > 1)
                {
                    int boxes = r.S32();
                    for (int k = 0; k < boxes; k++) { r.CString(); r.Skip(24); }
                }

                if (table > 2)
                {
                    int effects = r.S32();
                    for (int k = 0; k < effects; k++)
                    {
                        var e = new Effect { Name = r.CString() };
                        int comps = r.S32();
                        for (int m = 0; m < comps; m++)
                        {
                            var ec = new EffectComponent { Name = r.CString(), Type = r.FourCC(), Id = r.U32(), Bone = r.CString(), Scale = r.F32(), ParentedMode = r.U32(), Flags = r.U32() };
                            e.Components.Add(ec);
                        }

                        c.Effects.Add(e);
                    }
                }

                if (table > 3)
                {
                    c.OverlayCmdl = r.U32();
                    c.OverlayCskr = r.U32();
                }

                if (table > 4)
                {
                    int n = r.S32();
                    for (int k = 0; k < n; k++) c.AnimIndex.Add(r.S32());
                }

                a.Characters.Add(c);
            }

            a.AnimSetOffset = r.Pos;
            return a;
        }

        private static void ReadIds(MpReader r, List<uint> into)
        {
            int n = r.S32();
            for (int i = 0; i < n; i++) into.Add(r.U32());
        }

        private static void SkipPas(MpReader r)
        {
            r.U32();     // 'PAS4'
            int states = r.S32();
            r.S32();     // default state
            for (int s = 0; s < states; s++)
            {
                r.S32();
                int parms = r.S32();
                int anims = r.S32();
                var types = new int[parms];
                for (int p = 0; p < parms; p++)
                {
                    types[p] = r.S32();
                    r.S32();
                    r.F32();
                    switch (types[p])
                    {
                        case 3: r.Skip(2); break;
                        case 0: case 1: case 2: case 4: r.Skip(8); break;
                    }
                }

                for (int k = 0; k < anims; k++)
                {
                    r.S32();
                    for (int p = 0; p < parms; p++) r.Skip(types[p] == 3 ? 1 : (types[p] >= 0 && types[p] <= 4 ? 4 : 0));
                }
            }
        }
    }
}
