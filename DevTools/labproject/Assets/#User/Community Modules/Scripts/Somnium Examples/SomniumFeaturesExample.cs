using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Audio.GeneratorInstance;

namespace CommunityModules
{
    public class SomniumFeaturesExample : MonoBehaviour
    {

        [SerializeField] private TextMeshProUGUI _ui;

        [SerializeField] private Button _buttonMotionDoTeleportToPoint;
        [SerializeField] private Vector3 _motionTeleportToPointValue;
        [SerializeField] private Slider _sliderMotionSetMovementSpeed;
        [SerializeField] private Slider _sliderMotionSetGlideSpeed;
        [SerializeField] private Slider _sliderMotionSetGravity;
        [SerializeField] private Slider _sliderMotionSetJump;
        [SerializeField] private Slider _sliderMotionSetTeleportation;
        [SerializeField] private Toggle _toggleMotionSetTeleportationDisableState;
        [SerializeField] private Toggle _toggleMotionSetCollisionDisableState;
        [SerializeField] private Toggle _toggleMotionSetFlyModeDisableState;
        [SerializeField] private Toggle _toggleMotionSetGlideDisableState;

        [SerializeField] private Slider _sliderVoiceEnableAmplify;
        [SerializeField] private Slider _sliderVoiceSetVoiceLevel;

        [SerializeField] private Slider _sliderScaleSetHeight;

        [SerializeField] private Toggle _toggleAvatarSetVRIKLegMovementDisabled;

        [SerializeField] private Slider _sliderPosesSetPose;

        [SerializeField] private Toggle _toggleAnimationSetFallingAnimationDisabled;

        [SerializeField] private Toggle _toggleInfoSetPlayerTagDisableState;

        [SerializeField] private Button _buttonGetPlayerInfos;
        [SerializeField] private Button _buttonGetBodyParts;

        /*
         TODO
            ISomniumPlayer GetPlayerByID(string userId);
            ISomniumPlayer GetPlayerByPhotonID(int photonId);
            ISomniumPlayer GetPlayerByNickName(string nickName);
         */

        void Start()
        {
            SomniumBridge.PlayersContainer.OnPlayerAvatarChanged += OnPlayerAvatarChanged;
            SomniumBridge.PlayersContainer.OnPlayerOutfitPutOn += OnPlayerOutfitPutOnEvent;
            SomniumBridge.PlayersContainer.OnPlayerOutfitTakeOff += OnPlayerOutfitTakeOff;

            _buttonMotionDoTeleportToPoint.onClick.AddListener(() => Motion_DoTeleportToPoint(_motionTeleportToPointValue));
            _sliderMotionSetMovementSpeed.onValueChanged.AddListener(Motion_SetMovementSpeed);
            _sliderMotionSetGlideSpeed.onValueChanged.AddListener(Motion_SetGlideSpeed);
            _sliderMotionSetGravity.onValueChanged.AddListener(Motion_SetGravity);
            _sliderMotionSetJump.onValueChanged.AddListener(Motion_SetJump);
            _sliderMotionSetTeleportation.onValueChanged.AddListener(Motion_SetTeleportation);
            _toggleMotionSetTeleportationDisableState.onValueChanged.AddListener(Motion_SetTeleportationDisableState);
            _toggleMotionSetCollisionDisableState.onValueChanged.AddListener(Motion_SetCollisionDisableState);
            _toggleMotionSetFlyModeDisableState.onValueChanged.AddListener(Motion_SetFlyModeDisableState);
            _toggleMotionSetGlideDisableState.onValueChanged.AddListener(Motion_SetGlideDisableState);
            _sliderVoiceEnableAmplify.onValueChanged.AddListener(Voice_EnableAmplify);
            _sliderVoiceSetVoiceLevel.onValueChanged.AddListener(Voice_SetVoiceLevel);
            _sliderScaleSetHeight.onValueChanged.AddListener(Scale_SetHeight);
            _toggleAvatarSetVRIKLegMovementDisabled.onValueChanged.AddListener(Avatar_SetVRIKLegMovementDisabled);
            _sliderPosesSetPose.onValueChanged.AddListener(value => Poses_SetPose((int)value));
            _toggleAnimationSetFallingAnimationDisabled.onValueChanged.AddListener(Animation_SetFallingAnimationDisabled);
            _toggleInfoSetPlayerTagDisableState.onValueChanged.AddListener(Info_SetPlayerTagDisableState);
            _buttonGetPlayerInfos.onClick.AddListener(GetPlayerInfos);
            _buttonGetBodyParts.onClick.AddListener(GetBodyParts);
        }

        private void OnPlayerAvatarChanged(ISomniumPlayer player)
        {
            _ui.text += "\n OnPlayerAvatarChanged";
        }

        private void OnPlayerOutfitPutOnEvent(ISomniumPlayer player)
        {
            _ui.text += "\n OnPlayerOutfitPutOn";
        }

        private void OnPlayerOutfitTakeOff(ISomniumPlayer player)
        {
            _ui.text += "\n OnPlayerOutfitTakeOff";
        }

        // Features.Motion

        public void Motion_DoTeleportToPoint(Vector3 position)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.DoTeleportToPoint(position, Vector3.forward);
        }

        public void Motion_SetMovementSpeed(float speed)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetMovementSpeed(speed, speed, speed); // 0 to 10.
        }

        public void Motion_SetGlideSpeed(float speed)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetGlideSpeed(speed); // 0 to 10.
        }

        public void Motion_SetGravity(float value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetGravity(value, value); // 0 to 10.
        }

        public void Motion_SetJump(float value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetJump(value, value); // 0 to 10.
        }

        public void Motion_SetTeleportation(float value)
        {
            Gradient teleportRayGradient = new ();
            teleportRayGradient.SetKeys(
               new[]
               {
                 new GradientColorKey(Color.cyan, 0f),
                 new GradientColorKey(Color.blue, 1f)
               },
               new[]
               {
                 new GradientAlphaKey(1f, 0f),
                 new GradientAlphaKey(0.25f, 1f)
               });
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetTeleportation(value, 1.0f, teleportRayGradient); 
        }

        public void Motion_SetTeleportationDisableState(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetTeleportationDisableState(value);
        }

        public void Motion_SetCollisionDisableState(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetCollisionDisableState(value);
        }

        public void Motion_SetFlyModeDisableState(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetFlyModeDisableState(value);
        }

        public void Motion_SetGlideDisableState(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetGlideDisableState(value);
        }

        // Features.Voice

        public void Voice_EnableAmplify(float value)
        {
            if(value > 0.01f)
                SomniumBridge.PlayersContainer.LocalPlayer.Features.Voice.EnableAmplify(value);
            else
                SomniumBridge.PlayersContainer.LocalPlayer.Features.Voice.DisableAmplify();
        }

        public void Voice_SetVoiceLevel(float value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Voice.SetVoiceLevel(value);
        }

        // Features.Scale

        public void Scale_SetHeight(float value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Scale.SetHeight(value, value); //0.5 - 5
        }

        // Features.Avatar

        public void Avatar_SetVRIKLegMovementDisabled(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Avatar.SetVRIKLegMovementDisabled(value);
        }

        // Features.Poses

        public void Poses_SetPose(int value)
        {
            PoseType pose = value switch
            {
                0 => PoseType.None,
                1 => PoseType.SittingStraight,
                2 => PoseType.SittingCrossedLegs,
                3 => PoseType.Meditation,
                4 => PoseType.SittingKneesApart,
                5 => PoseType.Kneeling,
                6 => PoseType.Squat,
                7 => PoseType.ProneForward,
                8 => PoseType.ProneRelax,
                9 => PoseType.LayDownBack,
                10 => PoseType.SittingFloor,
                11 => PoseType.SittingCoolOnFloor,
                _ => PoseType.None
            };

            SomniumBridge.PlayersContainer.LocalPlayer.Features.Poses.SetPose(pose);
        }

        // Features.Animation

        public void Animation_SetFallingAnimationDisabled(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Animation.SetFallingAnimationDisabled(value);
        }

        // Features.Info

        public void Info_SetPlayerTagDisableState(bool value)
        {
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Info.SetPlayerTagDisableState(value);
        }

        // Properties

        public void GetPlayerInfos()
        {
            ISomniumPlayer player = SomniumBridge.PlayersContainer.LocalPlayer;
            _ui.text = "";
            _ui.text += $"\n[NickName {player?.Properties?.NickName}]";
            _ui.text += $"\n[Id {player?.Properties?.Id}]";
            _ui.text += $"\n[PhotonId {player?.Properties?.PhotonId}]";
            _ui.text += $"\n[IsLocal {player?.Properties?.IsLocal}]";
        }

        // References.Body

        public void GetBodyParts()
        {
            ISomniumPlayer player = SomniumBridge.PlayersContainer.LocalPlayer;
            var root = player?.References?.Body?.Root;
            var head = player?.References?.Body?.Head;
            var neck = player?.References?.Body?.Neck;
            var leftHand = player?.References?.Body?.LeftHand;
            var rightHand = player?.References?.Body?.RightHand;

            _ui.text = "";
            _ui.text += $"\n[root {root != null}]";
            _ui.text += $"\n[head {head != null}]";
            _ui.text += $"\n[neck {neck != null}]";
            _ui.text += $"\n[leftHand {leftHand != null}]";
            _ui.text += $"\n[rightHand {rightHand != null}]";
        }
    }
}
