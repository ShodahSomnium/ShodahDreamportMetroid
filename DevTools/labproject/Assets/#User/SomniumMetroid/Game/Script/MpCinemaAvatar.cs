using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// In cutscenes the instance master's own Somnium avatar stands where Samus's body (CScriptPlayerActor) would: a live copy of the
    /// avatar (each skinned mesh baked every frame it is shown, with the avatar's own materials), placed at Samus's feet, facing her
    /// way, sized to her height (2.7 units). The master is MpPlayer.MasterHead (the longest-present player; this player when alone);
    /// the avatar model is the humanoid Animator whose head bone is at that head (the WoW Dreamport's GearBody way). If no avatar
    /// can be found, Samus is drawn as before. Tablet: "Cutscenes: your avatar as Samus".
    /// </summary>
    public static class MpCinemaAvatar
    {
        private sealed class Statue
        {
            public GameObject Root;
            public readonly List<KeyValuePair<SkinnedMeshRenderer, MeshFilter>> Parts = new List<KeyValuePair<SkinnedMeshRenderer, MeshFilter>>();
        }

        private static readonly Dictionary<MpPlayerActorEntity, Statue> Statues = new Dictionary<MpPlayerActorEntity, Statue>();
        private static Animator _model;
        private static float _nextFind;
        private static int _logs;

        /// <summary>True when Samus is to be replaced (the option is on and an avatar was found).</summary>
        public static bool Replaces { get { return MpSettings.CinemaAvatar && Model() != null; } }

        /// <summary>Labs: a humanoid standing in for the master's avatar (the editor has no Somnium players).</summary>
        public static Animator LabModel;

        private static Animator Model()
        {
            if (LabModel != null) return LabModel;
            if (_model != null && _model.gameObject.activeInHierarchy) return _model;
            if (Time.unscaledTime < _nextFind) return null;
            _nextFind = Time.unscaledTime + 2f;
            bool local;
            Transform head = MpPlayer.MasterHead(out local);
            if (head == null) return null;
            Animator best = null;
            float bestD = 0.6f;
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (a == null || !a.gameObject.activeInHierarchy || !a.isHuman || a.avatar == null) continue;
                Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                if (h == null) continue;
                float d = (h.position - head.position).magnitude;
                if (d < bestD && a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) { bestD = d; best = a; }
            }

            if (best != _model) { DropAll(); if (best != null && _logs++ < 5) MpLog.Info("Cinema: Samus is played by the " + (local ? "local player's" : "instance master's") + " avatar ('" + best.name + "')"); }
            _model = best;
            if (_model != null) _model.cullingMode = AnimatorCullingMode.AlwaysAnimate;      // it may be hidden (first-person cutscenes) but must keep its pose
            return _model;
        }

        /// <summary>From MpPlayerActorEntity.SetCinemaVisible: the avatar shown (posed, placed) or hidden in Samus's place.</summary>
        public static void Show(MpPlayerActorEntity actor, bool on)
        {
            Statue s;
            Statues.TryGetValue(actor, out s);
            if (!on)
            {
                if (s != null && s.Root != null && s.Root.activeSelf) s.Root.SetActive(false);
                return;
            }

            Animator model = Model();
            if (model == null || actor.Go == null) return;
            try
            {
                if (s == null || s.Root == null) { s = Build(model); Statues[actor] = s; }
                if (!s.Root.activeSelf) s.Root.SetActive(true);
                Place(s, model, actor);
            }
            catch (Exception e)
            {
                if (_logs++ < 5) MpLog.Warn("Cinema: the avatar could not stand in for Samus: " + e.Message);
                MpSettings.CinemaAvatar = false;
            }
        }

        private static Statue Build(Animator model)
        {
            var s = new Statue { Root = new GameObject("Cinema avatar (Samus)") };
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var go = new GameObject(smr.name);
                go.transform.SetParent(s.Root.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = new Mesh { name = smr.sharedMesh.name + " (cinema copy)" };
                mf.sharedMesh.MarkDynamic();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s.Parts.Add(new KeyValuePair<SkinnedMeshRenderer, MeshFilter>(smr, mf));
            }

            return s;
        }

        // ------------------------------------------------------------------ Samus's cutscene animation on the avatar (the Skyrim / WoW
        // GearPoser way: transforms only - the client's build has no SetHumanPose - posed, baked, then put back exactly as found)

        /// <summary>A humanoid bone aimed along a segment of Samus's skeleton (CINF names of the cutscene suit, ANCS 27550808).</summary>
        private struct Aim { public HumanBodyBones Bone, Child; public string From, To; }

        private static readonly Aim[] Aims =
        {
            new Aim { Bone = HumanBodyBones.Spine, Child = HumanBodyBones.Chest, From = "Pelvis", To = "Spine_2" },
            new Aim { Bone = HumanBodyBones.Chest, Child = HumanBodyBones.Neck, From = "Spine_2", To = "Collar" },
            new Aim { Bone = HumanBodyBones.Neck, Child = HumanBodyBones.Head, From = "Neck_1", To = "Head_1" },
            new Aim { Bone = HumanBodyBones.LeftUpperArm, Child = HumanBodyBones.LeftLowerArm, From = "L_shoulder", To = "L_elbow" },
            new Aim { Bone = HumanBodyBones.LeftLowerArm, Child = HumanBodyBones.LeftHand, From = "L_elbow", To = "L_wrist" },
            new Aim { Bone = HumanBodyBones.LeftHand, Child = HumanBodyBones.LeftMiddleProximal, From = "L_wrist", To = "L_middle_1" },
            new Aim { Bone = HumanBodyBones.RightUpperArm, Child = HumanBodyBones.RightLowerArm, From = "R_shoulder", To = "R_elbow" },
            new Aim { Bone = HumanBodyBones.RightLowerArm, Child = HumanBodyBones.RightHand, From = "R_elbow", To = "Charge_LCTR" },   // her right forearm is the cannon
            new Aim { Bone = HumanBodyBones.LeftUpperLeg, Child = HumanBodyBones.LeftLowerLeg, From = "L_hip", To = "L_knee" },
            new Aim { Bone = HumanBodyBones.LeftLowerLeg, Child = HumanBodyBones.LeftFoot, From = "L_knee", To = "L_ankle" },
            new Aim { Bone = HumanBodyBones.LeftFoot, Child = HumanBodyBones.LeftToes, From = "L_ankle", To = "L_ball" },
            new Aim { Bone = HumanBodyBones.RightUpperLeg, Child = HumanBodyBones.RightLowerLeg, From = "R_hip", To = "R_knee" },
            new Aim { Bone = HumanBodyBones.RightLowerLeg, Child = HumanBodyBones.RightFoot, From = "R_knee", To = "R_ankle" },
            new Aim { Bone = HumanBodyBones.RightFoot, Child = HumanBodyBones.RightToes, From = "R_ankle", To = "R_ball" },
        };

        private static readonly List<KeyValuePair<Transform, Quaternion>> SavedRot = new List<KeyValuePair<Transform, Quaternion>>();
        private static Transform _savedHips;
        private static Vector3 _savedHipsPos;
        /// <summary>Frames posed from Samus's skeleton so far (labs).</summary>
        public static int Posed;
        public static string LastReport = "";

        /// <summary>Samus's frame now: her feet (under the pelvis, on her actor's floor) and facing (across her hips, level).</summary>
        /// <summary>The game's up in Unity now (the world may be turned in a first-person cutscene).</summary>
        private static Vector3 Up { get { return MpSpace.DirToUnity(Vector3.forward); } }

        private static void SamusFrame(MpPlayerActorEntity actor, out Vector3 feet, out Vector3 fwd)
        {
            Transform t = actor.Go.transform;
            Vector3 up = Up;
            feet = t.position;
            fwd = t.TransformDirection(Vector3.up);         // the actor's local frame is game space: +Y is forward
            MpAnimator a = actor.Anim;
            Transform pelvis = a != null ? a.Bone("Pelvis") : null, lh = a != null ? a.Bone("L_hip") : null, rh = a != null ? a.Bone("R_hip") : null;
            // under her pelvis, on her actor's floor
            if (pelvis != null) feet = pelvis.position - up * Vector3.Dot(pelvis.position - t.position, up);
            // her soles, not her actor's anchor: jumps, falls and landings move her whole skeleton (the root segment is never posed),
            // so the avatar stands where her lower foot is (ankle 0.11 units above the sole)
            Transform la = a != null ? a.Bone("L_ankle") : null, ra = a != null ? a.Bone("R_ankle") : null;
            if (la != null && ra != null)
                feet += up * (Mathf.Min(Vector3.Dot(la.position - feet, up), Vector3.Dot(ra.position - feet, up)) - 0.11f * Mathf.Abs(t.lossyScale.x));
            if (lh != null && rh != null)
            {
                Vector3 f = Vector3.Cross(rh.position - lh.position, up);
                if (f.sqrMagnitude > 1e-8f) fwd = f;
            }

            fwd = Vector3.ProjectOnPlane(fwd, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            fwd.Normalize();
        }

        /// <summary>The avatar's bones turned to Samus's pose this frame (saved first: Unpose puts them back).</summary>
        private static void Pose(Animator model, MpPlayerActorEntity actor, Vector3 samusFeet, Vector3 samusFwd)
        {
            SavedRot.Clear();
            _savedHips = null;
            MpAnimator a = actor.Anim;
            if (a == null) return;
            Transform root = model.transform;
            Quaternion q = root.rotation * Quaternion.Inverse(Quaternion.LookRotation(samusFwd, Up));   // Samus's world -> the avatar's
            Transform hips = model.GetBoneTransform(HumanBodyBones.Hips);
            Transform lul = model.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rul = model.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform pelvis = a.Bone("Pelvis"), lh = a.Bone("L_hip"), rh = a.Bone("R_hip"), collar = a.Bone("Collar");
            if (hips == null || pelvis == null) return;
            foreach (Aim m in Aims) { Transform b = model.GetBoneTransform(m.Bone); if (b != null) SavedRot.Add(new KeyValuePair<Transform, Quaternion>(b, b.localRotation)); }
            Transform head = model.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) SavedRot.Add(new KeyValuePair<Transform, Quaternion>(head, head.localRotation));
            SavedRot.Add(new KeyValuePair<Transform, Quaternion>(hips, hips.localRotation));
            _savedHips = hips;
            _savedHipsPos = hips.localPosition;
            Vector3 headFacing0 = head != null ? Quaternion.Inverse(head.rotation) * root.forward : Vector3.forward;

            // the hips: placed and turned as her pelvis (height in proportion: crouches and kneels come through)
            float samusPelvisH = Mathf.Max(0.1f, 1.18f * Mathf.Abs(actor.Go.transform.lossyScale.x));
            float avatarHipsH = Mathf.Max(0.1f, root.InverseTransformPoint(hips.position).y);
            hips.position = root.position + q * (pelvis.position - samusFeet) * (avatarHipsH / samusPelvisH);
            if (lh != null && rh != null && collar != null && lul != null && rul != null)
            {
                Transform neck = model.GetBoneTransform(HumanBodyBones.Neck);
                Vector3 curRight = rul.position - lul.position;
                Vector3 curUp = neck != null ? neck.position - hips.position : Vector3.up;
                Vector3 wantRight = q * (rh.position - lh.position), wantUp = q * (collar.position - pelvis.position);
                Quaternion cur = Frame(curRight, curUp), want = Frame(wantRight, wantUp);
                hips.rotation = want * Quaternion.Inverse(cur) * hips.rotation;
            }

            // each segment aimed as hers, parents first (the list's order)
            foreach (Aim m in Aims)
            {
                Transform b = model.GetBoneTransform(m.Bone), c = model.GetBoneTransform(m.Child);
                Transform sf = a.Bone(m.From), st = a.Bone(m.To);
                if (b == null || c == null || sf == null || st == null) continue;
                Vector3 cur = c.position - b.position, want = q * (st.position - sf.position);
                if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) continue;
                b.rotation = Quaternion.FromToRotation(cur, want) * b.rotation;
            }

            // the head: facing where her visor faces (between her eyes, from her head bone)
            Transform sh = a.Bone("Head_1"), le = a.Bone("L_eye"), re = a.Bone("R_eye");
            if (head != null && sh != null && le != null && re != null)
            {
                // her eyes sit above the head bone: in the bind pose the head-to-eyes line rises 40.6 degrees (0.06 up, 0.07 forward), so
                // her facing is that line turned down by it about the line between her eyes
                Vector3 eyes = (le.position + re.position) * 0.5f - sh.position;
                Vector3 facing = Quaternion.AngleAxis(40.6f, re.position - le.position) * eyes;
                Vector3 want = q * facing, cur = head.rotation * headFacing0;
                if (want.sqrMagnitude > 1e-8f && cur.sqrMagnitude > 1e-8f) head.rotation = Quaternion.FromToRotation(cur, want) * head.rotation;
            }

            Posed++;
            if (LabModel != null)
            {
                Transform lhand = model.GetBoneTransform(HumanBodyBones.LeftHand), rhand = model.GetBoneTransform(HumanBodyBones.RightHand), lua = model.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                LastReport = "avatar (root space, from the hips): left hand " + root.InverseTransformDirection(lhand.position - hips.position).ToString("F2") + ", right hand " + root.InverseTransformDirection(rhand.position - hips.position).ToString("F2") + ", left shoulder " + root.InverseTransformDirection(lua.position - hips.position).ToString("F2") + ", head " + (head != null ? root.InverseTransformDirection(head.position - hips.position).ToString("F2") : "-") + (head != null ? ", head up " + root.InverseTransformDirection(head.up).ToString("F2") + " fwd " + root.InverseTransformDirection(head.forward).ToString("F2") + " facing0 " + headFacing0.ToString("F2") : "") + "; root fwd " + root.forward.ToString("F2") + " samus fwd " + samusFwd.ToString("F2");
            }
        }

        private static Quaternion Frame(Vector3 right, Vector3 up)
        {
            Vector3 f = Vector3.Cross(right, up);
            if (f.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f) return Quaternion.identity;
            return Quaternion.LookRotation(f, up);
        }

        private static void Unpose()
        {
            for (int i = SavedRot.Count - 1; i >= 0; i--) if (SavedRot[i].Key != null) SavedRot[i].Key.localRotation = SavedRot[i].Value;
            if (_savedHips != null) _savedHips.localPosition = _savedHipsPos;
            SavedRot.Clear();
            _savedHips = null;
        }

        /// <summary>Samus's place, facing and height; the avatar posed as she is, baked, and put back as it was.</summary>
        private static void Place(Statue s, Animator model, MpPlayerActorEntity actor)
        {
            Transform t = actor.Go.transform;
            Vector3 feet, fwd;
            SamusFrame(actor, out feet, out fwd);
            Transform root = model.transform;
            Transform head = model.GetBoneTransform(HumanBodyBones.Head);
            float avatarH = head != null ? Mathf.Max(0.5f, root.InverseTransformPoint(head.position).y + 0.12f) : 1.75f;
            float samusH = 2.7f * MpSpace.Scale * Mathf.Abs(t.lossyScale.x / MpSpace.Scale);
            s.Root.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(fwd, Up));
            s.Root.transform.localScale = Vector3.one * Mathf.Clamp(samusH / avatarH, 0.5f, 2f);
            try
            {
                Pose(model, actor, feet, fwd);
                Matrix4x4 inv = root.worldToLocalMatrix;
                foreach (var kv in s.Parts)
                {
                    SkinnedMeshRenderer smr = kv.Key;
                    if (smr == null) continue;
                    smr.BakeMesh(kv.Value.sharedMesh, true);
                    Matrix4x4 rel = inv * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    kv.Value.transform.localPosition = rel.GetColumn(3);
                    kv.Value.transform.localRotation = rel.rotation;
                    kv.Value.transform.localScale = Vector3.one;
                }
            }
            finally { Unpose(); }
        }

        private static void DropAll()
        {
            foreach (Statue s in Statues.Values)
            {
                if (s.Root == null) continue;
                foreach (var kv in s.Parts) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value.sharedMesh);
                UnityEngine.Object.Destroy(s.Root);
            }

            Statues.Clear();
        }

        /// <summary>Every stand-in hidden (a cutscene ended or was skipped: nothing of it may stay in the player's view).</summary>
        public static void HideAll()
        {
            foreach (Statue s in Statues.Values) if (s.Root != null && s.Root.activeSelf) s.Root.SetActive(false);
        }

        /// <summary>The world stops, or an actor goes away.</summary>
        public static void Clear() { DropAll(); _model = null; _nextFind = 0f; }

        public static void Remove(MpPlayerActorEntity actor)
        {
            Statue s;
            if (!Statues.TryGetValue(actor, out s)) return;
            if (s.Root != null)
            {
                foreach (var kv in s.Parts) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value.sharedMesh);
                UnityEngine.Object.Destroy(s.Root);
            }

            Statues.Remove(actor);
        }
    }
}
