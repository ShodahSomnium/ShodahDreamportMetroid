using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The local avatar's right arm, for the "Cannon on forearm" option (VR): the cannon follows the forearm (the avatar's elbow to
    /// its wrist, as the client's IK places them) instead of the controller's pointing, and the avatar's own right hand and forearm
    /// are hidden inside it - the WoW Dreamport's ArmourBodyHide way, on every visual copy of the avatar (the main copy, the mirror
    /// copy the tablet camera and mirrors draw): a COPY of each skinned mesh whose hand / forearm vertices are moved onto their own
    /// bone's bind position (the triangles collapse and draw nothing). Only vertex positions change, so the index buffer and Unity 6's
    /// mesh LOD ranges stay intact. The originals come back when the option goes off, the avatar changes, or the world stops.
    /// The avatar is found the WoW Dreamport's way (GearBody / GearAvatarRoots): the humanoid Animator whose head bone is nearest
    /// Body.Head, the client's MAIN copy (MainRoot / NonXRMainRoot, by type name) before its mirror / teleport / wearables copies.
    /// </summary>
    public sealed class MpAvatarArm
    {
        private sealed class Swap { public SkinnedMeshRenderer R; public Mesh Original, Copy; }

        private Animator _anim;
        private Avatar _animAvatar;
        public Transform Elbow, Wrist;
        private readonly List<Swap> _swaps = new List<Swap>();
        private readonly List<Transform> _roots = new List<Transform>();
        private readonly HashSet<SkinnedMeshRenderer> _seen = new HashSet<SkinnedMeshRenderer>();
        private readonly HashSet<string> _hideNames = new HashSet<string>();
        private float _nextFind, _lastHide = -10f;
        private bool _hidden, _forearmToo, _forearmHidden;
        private int _logs;

        public const string Main = "MainRoot", NonXrMain = "NonXRMainRoot", Mirror = "MirrorRoot", Teleport = "TeleportRoot", Wearables = "WearablesRoot";

        /// <summary>Co-op: another player's avatar - the one whose head bone is nearest this (their Body.Head); null = this player's own.</summary>
        public Transform TargetHead;

        public bool Ready { get { return _anim != null && Elbow != null && Wrist != null; } }

        /// <summary>Every frame (VR): find the avatar when needed; hide or show the hand and forearm by <paramref name="hide"/>.</summary>
        public void Tick(bool hide, bool forearmToo)
        {
            try
            {
                if (_anim != null && (!_anim.gameObject.activeInHierarchy || _anim.avatar != _animAvatar)) Drop("the avatar changed");
                if (_anim == null) Find();
                if (!hide || _anim == null) { if (_hidden) Restore(); return; }
                // wrist mode hides the hand only (the forearm stays in view behind the cannon); forearm mode hides both
                if (_hidden && forearmToo != _forearmHidden) { Restore(); _lastHide = -10f; }
                _forearmToo = forearmToo;
                bool stale = false;
                for (int i = 0; i < _swaps.Count; i++) if (_swaps[i].R == null || _swaps[i].R.sharedMesh != _swaps[i].Copy) { stale = true; break; }
                if (_hidden && !stale) return;
                if (Time.unscaledTime - _lastHide < 0.5f) return;
                _lastHide = Time.unscaledTime;
                Restore();
                Hide();
            }
            catch (Exception e)
            {
                Restore();
                if (_logs++ < 4) MpLog.Warn("Cannon: the avatar's arm could not be followed / hidden (" + e.GetType().Name + ": " + e.Message + ") - the controller is used.");
            }
        }

        /// <summary>The forearm frame: the wrist's place and the elbow-to-wrist direction (false until the avatar's arm is known).</summary>
        public bool TryForearm(out Vector3 wrist, out Vector3 along)
        {
            wrist = Vector3.zero;
            along = Vector3.forward;
            if (!Ready) return false;
            Vector3 d = Wrist.position - Elbow.position;
            if (d.sqrMagnitude < 1e-4f) return false;
            wrist = Wrist.position;
            along = d.normalized;
            return true;
        }

        private void Find()
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 2f;
            Transform headBone = TargetHead != null ? TargetHead : MpPlayer.HeadBone;
            Camera cam = TargetHead != null ? null : MpPlayer.Head();
            Vector3 head = headBone != null ? headBone.position : cam != null ? cam.transform.position : Vector3.zero;
            if (headBone == null && cam == null) return;
            Animator best = null, other = null;
            float bestD = 0.6f, otherD = 0.6f;
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (a == null || !a.gameObject.activeInHierarchy || !a.isHuman || a.avatar == null) continue;
                Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                if (h == null) continue;
                float d = (h.position - head).magnitude;
                string kind = KindOf(a);
                if (kind == Main || kind == NonXrMain) { if (d < bestD) { bestD = d; best = a; } }
                else if (kind == null && d < otherD) { otherD = d; other = a; }
            }

            if (best == null) best = other;
            if (best == null) return;
            _anim = best;
            _animAvatar = best.avatar;
            Elbow = best.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Wrist = best.GetBoneTransform(HumanBodyBones.RightHand);
            MpLog.Info("Cannon: " + (TargetHead != null ? "another player's" : "the") + " avatar's right arm found (humanoid Animator '" + best.name + "', " + (KindOf(best) ?? "no client root") + ", elbow " + (Elbow != null) + ", wrist " + (Wrist != null) + ")");
        }

        private void Drop(string why)
        {
            Restore();
            _anim = null;
            Elbow = Wrist = null;
            _nextFind = 0f;
            MpLog.Info("Cannon: the avatar's arm dropped (" + why + ")");
        }

        /// <summary>The right forearm and hand (and fingers) collapsed on every visual copy of the avatar (WoW ArmourBodyHide).</summary>
        private void Hide()
        {
            _hideNames.Clear();
            Transform lower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm), hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (lower == null || hand == null) return;
            if (_forearmToo) _hideNames.Add(lower.name);
            _hideNames.Add(hand.name);
            // the walk up from a bone stops at the bone above the hidden part (the forearm's own vertices stay in wrist mode)
            Transform upper = _forearmToo ? _anim.GetBoneTransform(HumanBodyBones.RightUpperArm) : lower;
            string stopAt = upper != null ? upper.name : null;
            _forearmHidden = _forearmToo;
            VisualRoots(_anim, _roots);
            _seen.Clear();
            int meshes = 0, moved = 0;
            foreach (Transform root in _roots)
            {
                if (root == null) continue;
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || !_seen.Add(smr)) continue;
                    Mesh src = smr.sharedMesh;
                    if (src == null || !src.isReadable) continue;
                    Transform[] bones = smr.bones;
                    Matrix4x4[] bind = src.bindposes;
                    if (bones == null || bones.Length == 0 || bind == null || bind.Length != bones.Length) continue;
                    var hideBone = new bool[bones.Length];
                    var at = new Vector3[bones.Length];
                    bool any = false;
                    for (int b = 0; b < bones.Length; b++)
                    {
                        at[b] = bind[b].inverse.MultiplyPoint3x4(Vector3.zero);
                        // the bone or a parent up to the upper arm is the forearm or hand (the fingers hang under the hand)
                        for (Transform t = bones[b]; t != null; t = t.parent)
                        {
                            if (stopAt != null && t.name == stopAt) break;
                            if (_hideNames.Contains(t.name)) { hideBone[b] = true; any = true; break; }
                        }
                    }

                    if (!any) continue;
                    BoneWeight[] w = src.boneWeights;
                    Vector3[] v = src.vertices;
                    if (w.Length != v.Length) continue;
                    int n = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        BoneWeight bw = w[i];
                        int bi = bw.boneIndex0; float best = bw.weight0;
                        if (bw.weight1 > best) { best = bw.weight1; bi = bw.boneIndex1; }
                        if (bw.weight2 > best) { best = bw.weight2; bi = bw.boneIndex2; }
                        if (bw.weight3 > best) bi = bw.boneIndex3;
                        if (bi < 0 || bi >= hideBone.Length || !hideBone[bi]) continue;
                        v[i] = at[bi];
                        n++;
                    }

                    if (n == 0) continue;
                    Mesh copy = UnityEngine.Object.Instantiate(src);
                    copy.name = src.name + " (Metroid cannon: right forearm hidden)";
                    copy.vertices = v;
                    smr.sharedMesh = copy;
                    _swaps.Add(new Swap { R = smr, Original = src, Copy = copy });
                    meshes++;
                    moved += n;
                }
            }

            _hidden = true;
            if (_logs++ < 6) MpLog.Info("Cannon: the avatar's right hand and forearm hidden inside the cannon - " + moved + " vertices on " + meshes + " skinned meshes, " + _roots.Count + " visual copies.");
        }

        public void Restore()
        {
            for (int i = 0; i < _swaps.Count; i++)
            {
                Swap s = _swaps[i];
                if (s.R != null && s.R.sharedMesh == s.Copy) s.R.sharedMesh = s.Original;
                if (s.Copy != null) UnityEngine.Object.Destroy(s.Copy);
            }

            _swaps.Clear();
            _hidden = false;
        }

        public void Destroy() { Restore(); ShowBody(); _anim = null; Elbow = Wrist = null; }

        // ------------------------------------------------------------------ the whole avatar (the morph ball: WoW FormAvatarHider)

        private readonly List<Renderer> _off = new List<Renderer>();
        private readonly List<Renderer> _scan = new List<Renderer>();
        private float _nextBodyScan;

        public bool BodyHidden { get; private set; }

        /// <summary>Every frame: the whole avatar hidden (Renderer.forceRenderingOff on every visual copy; re-scanned once a second) or shown.</summary>
        public void TickBody(bool hide)
        {
            try
            {
                if (!hide) { if (BodyHidden) ShowBody(); return; }
                if (_anim == null) Find();
                if (_anim == null) return;
                if (BodyHidden && Time.unscaledTime < _nextBodyScan) return;
                _nextBodyScan = Time.unscaledTime + 1f;
                bool first = !BodyHidden;
                BodyHidden = true;
                VisualRoots(_anim, _roots);
                Transform exclude = MpSpace.Root;
                foreach (Transform root in _roots)
                {
                    if (root == null) continue;
                    _scan.Clear();
                    root.GetComponentsInChildren(true, _scan);
                    for (int i = 0; i < _scan.Count; i++)
                    {
                        Renderer r = _scan[i];
                        if (r == null || r.forceRenderingOff || r is LineRenderer || r is TrailRenderer) continue;
                        if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                        r.forceRenderingOff = true;
                        _off.Add(r);
                    }
                }

                _scan.Clear();
                if (first) MpLog.Info("Ball: the avatar hidden (" + _off.Count + " renderers on " + _roots.Count + " visual copies)");
            }
            catch (Exception e)
            {
                ShowBody();
                if (_logs++ < 4) MpLog.Warn("Ball: the avatar could not be hidden (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        public void ShowBody()
        {
            for (int i = 0; i < _off.Count; i++) if (_off[i] != null) _off[i].forceRenderingOff = false;
            _off.Clear();
            BodyHidden = false;
            _nextBodyScan = 0f;
        }

        // ------------------------------------------------------------------ the client's avatar copies (WoW GearAvatarRoots, by type names)

        private static readonly List<MonoBehaviour> Buf = new List<MonoBehaviour>(16);

        private static string KindOf(Component c)
        {
            Transform t = c != null ? c.transform : null;
            for (int up = 0; t != null && up <= 3; up++, t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                {
                    if (Buf[i] == null) continue;
                    string n = Buf[i].GetType().Name;
                    if (n == Main || n == NonXrMain || n == Mirror || n == Teleport || n == Wearables) { Buf.Clear(); return n; }
                }
            }

            Buf.Clear();
            return null;
        }

        /// <summary>Every visual copy of the avatar (main, mirror, teleport) under its holder, else just the model itself.</summary>
        private static void VisualRoots(Animator model, List<Transform> into)
        {
            into.Clear();
            into.Add(model.transform);
            Transform holder = null;
            for (Transform t = model.transform; t != null && holder == null; t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                    if (Buf[i] != null && Buf[i].GetType().Name.EndsWith("RootsHolder", StringComparison.Ordinal)) { holder = t; break; }
            }

            Buf.Clear();
            if (holder == null) return;
            foreach (MonoBehaviour m in holder.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (m == null) continue;
                string n = m.GetType().Name;
                if ((n == Main || n == NonXrMain || n == Mirror || n == Teleport) && !into.Contains(m.transform)) into.Add(m.transform);
            }
        }
    }
}
