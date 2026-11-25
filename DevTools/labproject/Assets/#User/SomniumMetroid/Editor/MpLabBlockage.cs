using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "blockage" (-mparea 6, Deck Alpha Umbilical Hall): the lab player faces the debris blockage ("Platform collision"
    /// and "Actor Blockage Front"), fires a normal Power Beam shot (the blockage should shrug it off), then a full charge (it should
    /// break: Dead, the blockage actors gone). Logs the platform's health and the actors' state; pictures before and after.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator BlockageScenario()
        {
            G.God = true;
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            MpPlatformEntity plat = null;
            MpActorEntity front = null;
            foreach (MpEntity e in G.Scripts.All)
            {
                if (e is MpPlatformEntity && e.Name == "Platform collision") plat = (MpPlatformEntity)e;
                if (e is MpActorEntity && !(e is MpPlatformEntity) && e.Name == "Actor Blockage Front") front = (MpActorEntity)e;
            }

            if (plat == null || plat.Solid == null) { Log("no blockage platform here"); yield break; }
            Vector3 c = MpSpace.ToGame(plat.Solid.bounds.center);
            Log("blockage at " + c + ", health " + plat.Health + ", front actor " + (front != null ? (front.Active ? "shown" : "hidden") : "missing"));
            // stand 6 units away on the floor, on the side the player comes from (toward the area's door at the larger y)
            Vector3 stand = c + new Vector3(0f, 6f, 0f);
            RaycastHit h;
            if (MpEnemy.WorldRay(MpSpace.ToUnity(stand + new Vector3(0, 0, 3f)), Vector3.down, 12f * MpSpace.Scale, out h)) stand = MpSpace.ToGame(h.point);
            MpGame.LabFocus = stand;
            Vector3 eye = stand + new Vector3(0, 0, MpSpace.SamusEyeHeight);
            _cam.transform.position = MpSpace.ToUnity(eye);
            _cam.transform.rotation = Quaternion.LookRotation(MpSpace.ToUnity(c) - _cam.transform.position);
            MpGame.LabFire = g => _labFire;
            yield return Frames(20);
            Shoot(_cam, "blockage_0.png", 960, 540);
            float before = plat.Health;
            _labFire = true; yield return Frames(2); _labFire = false; yield return Seconds(0.8f);
            Log("normal shot: health " + before + " -> " + plat.Health + ", dead " + plat.Dead + ", last hit " + G.Shots.LastHit);
            _labFire = true; yield return Seconds(2.2f); _labFire = false; yield return Seconds(1.5f);
            Log("charged shot: health " + plat.Health + ", dead " + plat.Dead + ", platform active " + plat.Active + ", front actor " + (front != null ? (front.Active ? "shown" : "hidden") : "missing") + ", last hit " + G.Shots.LastHit);
            Shoot(_cam, "blockage_1.png", 960, 540);
            MpGame.LabFire = null;
        }
    }
}
