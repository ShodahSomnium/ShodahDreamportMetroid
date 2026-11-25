using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// A character's bone hierarchy ready for posing (decomp CHierarchyPoseBuilder built from CCharLayoutInfo): the bones in
    /// parent-first order, each with its parent's index (-1 under the character), its reference-stance position (model space,
    /// CCharLayoutInfo::GetReferenceStanceOffset / GetFromRootUnrotated) and its offset from the parent (GetFromParentUnrotated).
    /// </summary>
    public sealed class MpRig
    {
        public int Count;
        public int[] Ids;            // segment id per bone
        public int[] Parent;         // bone index of the parent, -1 = the root's (the character)
        public float[] Ref;          // 3 per bone: reference position, model space
        public float[] FromParent;   // 3 per bone: reference offset from the parent
        public readonly int[] IndexOfId = new int[256];
        public readonly Dictionary<string, int> ByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int RootIndex = -1;

        /// <summary>The segment id the game never poses (CAnimData::RecalcPoseBuilder skips it: its motion moves the actor).</summary>
        public const int RootSegment = 3;

        public static MpRig From(MpSkeleton s)
        {
            var r = new MpRig();
            for (int i = 0; i < 256; i++) r.IndexOfId[i] = -1;
            var order = new List<uint>();
            var placed = new HashSet<uint>();
            // parent first: a bone goes after its parent (the bones' own parents, recursively; the build order list is a hint only)
            Action<uint> place = null;
            place = id =>
            {
                if (placed.Contains(id)) return;
                MpSkeleton.Bone b;
                if (!s.Bones.TryGetValue(id, out b)) return;
                placed.Add(id);
                if (s.Bones.ContainsKey(b.Parent) && b.Parent != id) place(b.Parent);
                order.Add(id);
            };
            foreach (uint id in s.BuildOrder) place(id);
            foreach (uint id in s.Bones.Keys) place(id);

            r.Count = order.Count;
            r.Ids = new int[r.Count];
            r.Parent = new int[r.Count];
            r.Ref = new float[r.Count * 3];
            r.FromParent = new float[r.Count * 3];
            for (int i = 0; i < r.Count; i++)
            {
                MpSkeleton.Bone b = s.Bones[order[i]];
                r.Ids[i] = (int)(b.Id & 0xFF);
                r.IndexOfId[b.Id & 0xFF] = i;
                r.Ref[i * 3] = b.X; r.Ref[i * 3 + 1] = b.Y; r.Ref[i * 3 + 2] = b.Z;
                if (!string.IsNullOrEmpty(b.Name)) r.ByName[b.Name] = i;
                if (b.Id == RootSegment) r.RootIndex = i;
            }

            for (int i = 0; i < r.Count; i++)
            {
                MpSkeleton.Bone b = s.Bones[order[i]];
                int p = b.Parent < 256 ? r.IndexOfId[b.Parent & 0xFF] : -1;
                if (p == i) p = -1;
                r.Parent[i] = p;
                for (int k = 0; k < 3; k++) r.FromParent[i * 3 + k] = r.Ref[i * 3 + k] - (p >= 0 ? r.Ref[p * 3 + k] : 0f);
            }

            return r;
        }

        public int Find(string name)
        {
            int i;
            return name != null && ByName.TryGetValue(name, out i) ? i : -1;
        }
    }

    /// <summary>
    /// One pose of a rig: per bone the local rotation (w, x, y, z, relative to the parent) and offset from the parent (the
    /// animation's offset channel, else the reference offset), then the accumulated model-space rotation and offset
    /// (CHierarchyPoseBuilder::RecursivelyBuildNoScale: rotation = parent rotation * local; offset = parent offset + parent
    /// rotation * local offset). The skinning matrix of a bone is [R | offset - R * reference] (CSkinRules::BuildAccumulatedTransforms).
    /// </summary>
    public sealed class MpPose
    {
        public readonly MpRig Rig;
        public readonly float[] LRot, LOff;      // local: 4 / 3 per bone
        public readonly float[] Rot, Off;        // model space: 4 / 3 per bone
        private readonly float[] _q = new float[4], _o = new float[3];

        public MpPose(MpRig rig)
        {
            Rig = rig;
            LRot = new float[rig.Count * 4];
            LOff = new float[rig.Count * 3];
            Rot = new float[rig.Count * 4];
            Off = new float[rig.Count * 3];
            Reset();
        }

        /// <summary>The reference stance: no rotations, reference offsets.</summary>
        public void Reset()
        {
            for (int i = 0; i < Rig.Count; i++)
            {
                LRot[i * 4] = 1f; LRot[i * 4 + 1] = LRot[i * 4 + 2] = LRot[i * 4 + 3] = 0f;
                for (int k = 0; k < 3; k++) LOff[i * 3 + k] = Rig.FromParent[i * 3 + k];
            }
        }

        /// <summary>The animation at <paramref name="t"/> seconds into the local arrays (bones it does not drive keep no rotation and their reference offset).</summary>
        public void Sample(MpAnimation a, float t)
        {
            Reset();
            if (a == null) return;
            for (int i = 0; i < Rig.Count; i++)
            {
                int id = Rig.Ids[i];
                if (id == MpRig.RootSegment) continue;
                if (a.Rotation(id, t, _q)) { LRot[i * 4] = _q[0]; LRot[i * 4 + 1] = _q[1]; LRot[i * 4 + 2] = _q[2]; LRot[i * 4 + 3] = _q[3]; }
                if (a.Offset(id, t, _o)) { LOff[i * 3] = _o[0]; LOff[i * 3 + 1] = _o[1]; LOff[i * 3 + 2] = _o[2]; }
            }
        }

        /// <summary>Blends <paramref name="other"/>'s local pose into this one by <paramref name="w"/> (0 = this, 1 = other).</summary>
        public void BlendTo(MpPose other, float w)
        {
            if (w <= 0f) return;
            if (w > 1f) w = 1f;
            for (int i = 0; i < Rig.Count; i++)
            {
                MpAnimation.Slerp(LRot, i * 4, other.LRot, i * 4, w, _q);
                LRot[i * 4] = _q[0]; LRot[i * 4 + 1] = _q[1]; LRot[i * 4 + 2] = _q[2]; LRot[i * 4 + 3] = _q[3];
                for (int k = 0; k < 3; k++) LOff[i * 3 + k] += (other.LOff[i * 3 + k] - LOff[i * 3 + k]) * w;
            }
        }

        public void CopyLocal(MpPose from)
        {
            Array.Copy(from.LRot, LRot, LRot.Length);
            Array.Copy(from.LOff, LOff, LOff.Length);
        }

        /// <summary>Model-space rotation and offset of every bone from the local arrays.</summary>
        public void Build()
        {
            for (int i = 0; i < Rig.Count; i++)
            {
                int p = Rig.Parent[i];
                int q = i * 4, o = i * 3;
                if (p < 0)
                {
                    Rot[q] = LRot[q]; Rot[q + 1] = LRot[q + 1]; Rot[q + 2] = LRot[q + 2]; Rot[q + 3] = LRot[q + 3];
                    Off[o] = LOff[o]; Off[o + 1] = LOff[o + 1]; Off[o + 2] = LOff[o + 2];
                    continue;
                }

                int pq = p * 4, po = p * 3;
                Mul(Rot, pq, LRot, q, Rot, q);
                Rotate(Rot, pq, LOff[o], LOff[o + 1], LOff[o + 2], out float x, out float y, out float z);
                Off[o] = Off[po] + x; Off[o + 1] = Off[po + 1] + y; Off[o + 2] = Off[po + 2] + z;
            }
        }

        /// <summary>Quaternion product a * b into o (w, x, y, z); o may alias b.</summary>
        public static void Mul(float[] a, int ai, float[] b, int bi, float[] o, int oi)
        {
            float aw = a[ai], ax = a[ai + 1], ay = a[ai + 2], az = a[ai + 3];
            float bw = b[bi], bx = b[bi + 1], by = b[bi + 2], bz = b[bi + 3];
            o[oi] = aw * bw - ax * bx - ay * by - az * bz;
            o[oi + 1] = aw * bx + ax * bw + ay * bz - az * by;
            o[oi + 2] = aw * by - ax * bz + ay * bw + az * bx;
            o[oi + 3] = aw * bz + ax * by - ay * bx + az * bw;
        }

        /// <summary>The vector (x, y, z) turned by the quaternion at q[i].</summary>
        public static void Rotate(float[] q, int i, float x, float y, float z, out float rx, out float ry, out float rz)
        {
            float w = q[i], qx = q[i + 1], qy = q[i + 2], qz = q[i + 3];
            // t = 2 * cross(q.xyz, v); v' = v + w * t + cross(q.xyz, t)
            float tx = 2f * (qy * z - qz * y), ty = 2f * (qz * x - qx * z), tz = 2f * (qx * y - qy * x);
            rx = x + w * tx + (qy * tz - qz * ty);
            ry = y + w * ty + (qz * tx - qx * tz);
            rz = z + w * tz + (qx * ty - qy * tx);
        }
    }
}
