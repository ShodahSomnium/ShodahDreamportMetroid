using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// CINF skeleton (decomp CCharLayoutInfo / TSegIdMap / CSegIdList): u32 bone count, per bone u32 id, u32 parent id, float3
    /// reference-stance position (model space), u32 linked count + u32 ids; u32 build-order count + ids; u32 name count + (name, id).
    /// </summary>
    public sealed class MpSkeleton
    {
        public sealed class Bone
        {
            public uint Id, Parent;
            public float X, Y, Z;
            public string Name;
        }

        public readonly Dictionary<uint, Bone> Bones = new Dictionary<uint, Bone>();
        public readonly List<uint> BuildOrder = new List<uint>();
        public readonly Dictionary<string, uint> Names = new Dictionary<string, uint>(System.StringComparer.OrdinalIgnoreCase);

        public static MpSkeleton Read(byte[] b)
        {
            var s = new MpSkeleton();
            var r = new MpReader(b);
            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                var bone = new Bone { Id = r.U32(), Parent = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32() };
                int linked = r.S32();
                r.Skip(linked * 4);
                s.Bones[bone.Id] = bone;
            }

            int order = r.S32();
            for (int i = 0; i < order; i++) s.BuildOrder.Add(r.U32());
            int names = r.S32();
            for (int i = 0; i < names; i++)
            {
                string n = r.CString();
                uint id = r.U32();
                s.Names[n] = id;
                Bone bone;
                if (s.Bones.TryGetValue(id, out bone)) bone.Name = n;
            }

            return s;
        }

        public Bone Find(string name)
        {
            uint id;
            Bone b;
            return Names.TryGetValue(name, out id) && Bones.TryGetValue(id, out b) ? b : null;
        }
    }
}
