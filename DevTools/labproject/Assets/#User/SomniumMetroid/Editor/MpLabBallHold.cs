using System.Collections;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Editor
{
    /// <summary>
    /// Lab scenario "ballhold" (-mparea 0): the third-person morph ball moves the AVATAR (MpAvatarHold), never the world - a stand-in body
    /// (CharacterController: the editor has no Somnium avatar) is carried along the ball camera while the ball rolls, the world stays at
    /// MpSpace.Home the whole time, and on unmorphing the body stands where the ball is.
    /// </summary>
    public sealed partial class MpLabRunner
    {
        private IEnumerator BallHoldScenario()
        {
            _cam.enabled = true;
            _cam.tag = "MainCamera";
            bool was3rd = MpSettings.BallThirdPerson;
            MpSettings.BallThirdPerson = true;
            float t0 = Time.realtimeSinceStartup;
            while (G.WorldView.IsBusy && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            for (int k = 0; k < 10 && MpCinematics.Active; k++) { MpCinematics.Skip(); yield return Frames(2); }
            var body = new GameObject("Lab Player Body").AddComponent<CharacterController>();
            body.transform.position = MpSpace.ToUnity(G.PlayerGame) + Vector3.up * 0.05f;
            yield return Seconds(2.5f);
            Vector3 start = body.transform.position;
            bool toggle = true;
            Vector2 drive = Vector2.zero;
            MpGame.LabBall = (MpGame g, out Vector2 stick) => { stick = drive; bool t = toggle; toggle = false; return t; };
            yield return Frames(5);
            Log("morphed " + G.Ball.Morphed + ", third person " + G.Ball.ThirdPerson + ", avatar held " + G.Ball.Hold.Taken);
            float maxWorld = 0f, maxGap = 0f;
            drive = new Vector2(0f, 1f);
            float r0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - r0 < 3f)
            {
                yield return null;
                maxWorld = Mathf.Max(maxWorld, (MpSpace.Origin - MpSpace.Home).magnitude);
                if (G.Ball.Hold.Taken) maxGap = Mathf.Max(maxGap, (body.transform.position + G.Ball.EyeInRoot - MpSpace.ToUnity(G.Ball.ViewPos)).magnitude);
            }

            drive = Vector2.zero;
            yield return Seconds(0.5f);
            float carried = (body.transform.position - start).magnitude;
            Vector3 ballFeet = MpSpace.ToUnity(G.Ball.Pos - new Vector3(0f, 0f, MpMorphBall.Radius));
            toggle = true;
            yield return Frames(5);
            yield return Seconds(0.5f);
            float standGap = (body.transform.position - ballFeet).magnitude;
            Log("CHECK ballhold-world " + (maxWorld < 0.001f ? "ok" : "FAILED") + " (the world moved at most " + maxWorld.ToString("0.000") + " m)");
            Log("CHECK ballhold-follows " + (maxGap < 0.05f && carried > 1f ? "ok" : "FAILED") + " (the body carried " + carried.ToString("0.0") + " m, its eyes off the ball camera by at most " + maxGap.ToString("0.000") + " m)");
            Log("CHECK ballhold-release " + (!G.Ball.Morphed && !G.Ball.Hold.Taken && standGap < 0.3f ? "ok" : "FAILED") + " (unmorphed " + !G.Ball.Morphed + ", held " + G.Ball.Hold.Taken + ", the body " + standGap.ToString("0.00") + " m from where the ball was)");
            MpGame.LabBall = null;
            MpSettings.BallThirdPerson = was3rd;
        }
    }
}
