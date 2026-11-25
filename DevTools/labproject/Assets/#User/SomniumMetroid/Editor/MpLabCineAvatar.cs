using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "cineavatar" (-mparea 27): the save station's cutscene (Samus's save animation, a CScriptPlayerActor) with a
    /// humanoid standing in for the master's avatar (the SDK template's Ready Player Me model). Pictures of Samus and of the avatar
    /// posed from her skeleton (MpCinemaAvatar) at the same moments, from a camera in front of her.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator CineAvatarScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SomniumSpaceTemplates/ItemsTemplate/Models/Avatars/RPM Men.fbx");
            if (prefab == null) { Log("no template avatar"); yield break; }
            GameObject man = Object.Instantiate(prefab);
            man.transform.position = _cam.transform.position + new Vector3(30f, 0f, 0f);
            Animator anim = man.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) { Log("the template avatar has no humanoid Animator"); yield break; }
            MpCinemaAvatar.LabModel = anim;
            _armRest = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;
            Log("stand-in avatar '" + man.name + "' humanoid " + anim.isHuman + ", skinned meshes " + man.GetComponentsInChildren<SkinnedMeshRenderer>().Length);

            MpTriggerEntity into = null;
            foreach (MpEntity e in G.Scripts.All) { var t = e as MpTriggerEntity; if (t != null && e.Name == "Trigger Save Station In") into = t; }
            if (into == null) { Log("no save station here"); yield break; }
            if (!into.Active) into.SetActive(true);
            Vector3 c = (into.Min + into.Max) * 0.5f;
            MpGame.LabFocus = new Vector3(c.x, c.y, into.Min.z + 0.1f);
            yield return Seconds(2f);

            var shot = new GameObject("Lab Avatar Camera").AddComponent<Camera>();
            shot.enabled = false;
            shot.nearClipPlane = 0.02f;
            shot.fieldOfView = 85f;
            for (int i = 0; i < 5; i++)
            {
                yield return Seconds(1.2f);
                MpPlayerActorEntity actor = null;
                foreach (MpPlayerActorEntity a in MpCinematics.CinemaActors) if (a.Active && a.Go != null) actor = a;
                if (actor == null) { Log("  " + i + ": no Samus actor shown (cutscene " + (MpCinematics.Active ? MpCinematics.Current.Name : "none") + ")"); continue; }
                Transform pelvis = actor.Anim != null ? actor.Anim.Bone("Pelvis") : null;
                if (i == 0 && actor.Anim != null) foreach (string bn in new[] { "Head_1", "L_shoulder", "L_elbow", "L_wrist", "R_shoulder", "R_elbow", "Charge_LCTR", "L_knee", "L_ankle" }) { Transform bt = actor.Anim.Bone(bn); if (bt != null && pelvis != null) Log("    Samus " + bn + " from the pelvis " + actor.Go.transform.InverseTransformDirection(bt.position - pelvis.position).ToString("F2")); }
                Vector3 at = pelvis != null ? pelvis.position : actor.Go.transform.position;
                Vector3 fwd = actor.Go.transform.TransformDirection(Vector3.up);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                shot.transform.position = at + fwd * 2.4f + Vector3.up * 0.3f;
                shot.transform.LookAt(at);
                // Samus herself
                bool was = MpSettings.CinemaAvatar;
                MpSettings.CinemaAvatar = false;
                actor.SetCinemaVisible(true);
                Shoot(shot, "cineavatar_" + i + "_samus.png", 640, 640);
                // the avatar playing her
                MpSettings.CinemaAvatar = true;
                actor.SetCinemaVisible(true);
                Shoot(shot, "cineavatar_" + i + "_avatar.png", 640, 640);
                shot.transform.position = at + fwd * 1.6f + Vector3.up * 0.9f;
                shot.transform.LookAt(at + Vector3.up * 0.75f);
                Shoot(shot, "cineavatar_" + i + "_head.png", 640, 640);
                actor.SetCinemaVisible(false);
                MpSettings.CinemaAvatar = was;
                Log("  " + i + ": cutscene '" + (MpCinematics.Active ? MpCinematics.Current.Name : "none") + "', posed frames " + MpCinemaAvatar.Posed + ", " + MpCinemaAvatar.LastReport + ", the avatar's real bones unchanged " + (anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation == _armRest));
            }

            // the template avatar itself (does its head draw with its own materials?)
            Transform mh = anim.GetBoneTransform(HumanBodyBones.Hips);
            shot.transform.position = mh.position + man.transform.forward * 2.4f + Vector3.up * 0.3f;
            shot.transform.LookAt(mh.position);
            Shoot(shot, "cineavatar_real.png", 640, 640);
            foreach (SkinnedMeshRenderer r in man.GetComponentsInChildren<SkinnedMeshRenderer>()) Log("  template part " + r.name + " material " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none"));
            Object.Destroy(shot.gameObject);
            MpCinemaAvatar.LabModel = null;
            MpGame.LabFocus = null;
        }

        private Quaternion _armRest;
    }
}
