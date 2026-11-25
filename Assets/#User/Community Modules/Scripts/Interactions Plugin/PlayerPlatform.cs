using SomniumSpace.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommunityModules
{
    // default value 1
    [DefaultExecutionOrder(1)] // Necessary to be updated before the PlayerController is moved
    public class PlayerPlatform : MonoBehaviour
    {
        [SerializeField] private Transform _motionAnchor;
        [SerializeField] private Transform _platformObject;
        [SerializeField] private LocalPlayerTrigger _trigger;
        [SerializeField] private float _disconnectDistance = 10.0f;
        [SerializeField] private bool _rotatePlayer = false;

        private Vector3 _lastPosition;
        private float _lastPlatformAngle;
        private CharacterController _character;

        private void Awake()
        {
            if (_motionAnchor == null || _trigger == null)
            { 
                Debug.LogError($"[{nameof(PlayerPlatform)}] Reference is missing in inspector.");
                enabled = false;
                return;
            }

            _trigger.OnPlayerEnter += OnCharacterEnter;
            _trigger.OnPlayerExit += OnCharacterExit;
            _lastPosition = _motionAnchor.position;
        }

        private void OnDestroy()
        {
            if (_trigger != null)
            {
                _trigger.OnPlayerEnter -= OnCharacterEnter;
                _trigger.OnPlayerExit -= OnCharacterExit;
            }
        }

        private void OnCharacterEnter(CharacterController character)
        {
            Debug.Log($"OnCharacterEnter: {character.name}");
            _lastPosition = _motionAnchor.position;
            _character = character;
        }

        private void OnCharacterExit(CharacterController character)
        {
            Debug.Log($"OnCharacterExit: {character.name}");
            _character = null;

        }

        void Update()
        {
            UpdateCharacter();
        }

        private void UpdateCharacter()
        {
            if (_platformObject != null)
            {
                _platformObject.position = _motionAnchor.position;
                _platformObject.rotation = _motionAnchor.rotation;
            }
            // Get angle and position
            float platformAngle = GetPlatformAngle();
            Vector3 deltaPos = _motionAnchor.position - _lastPosition;

            if (_character != null)
            {
                Vector3 playerPosition = _character.transform.TransformPoint(new(_character.center.x, 0, _character.center.z));
                if (Vector3.Distance(playerPosition, _motionAnchor.position) > _disconnectDistance)
                {
                    _character = null;
                    return;
                }

                if (deltaPos.y > 0.0f)
                {
                    _character.Move(deltaPos - new Vector3(0f, 0.01f, 0f));
                }
                if (deltaPos != Vector3.zero)
                {
                    _character.Move(deltaPos);
                }

                // Rotate player
                if (_rotatePlayer)
                    _character.transform.RotateAround(_motionAnchor.position, Vector3.up, -platformAngle + _lastPlatformAngle);
                else
                {
                    Vector3 rotDiff = RotatePointAround(playerPosition, _motionAnchor.position, platformAngle - _lastPlatformAngle) - playerPosition;
                    _character.Move(rotDiff);
                }
            }

            _lastPosition = _motionAnchor.position;
            _lastPlatformAngle = platformAngle;
        }

        private float GetPlatformAngle()
        {
            return Mathf.Atan2(_motionAnchor.forward.z, _motionAnchor.forward.x) * Mathf.Rad2Deg;
        }

        private Vector3 RotatePointAround(Vector3 point, Vector3 pivot, float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            Vector3 d = point - pivot;
            return pivot + new Vector3(d.x * cos - d.z * sin, d.y, d.x * sin + d.z * cos);
        }

        // Not Working
        //private VRIKHandler GetLocalVRIK()
        //{
        //    string name = SomniumBridge.PlayersContainer.LocalPlayer.Properties.NickName;
        //    Scene activeScene = SceneManager.GetActiveScene();
        //    GameObject[] roots = activeScene.GetRootGameObjects();

        //    GameObject found = null;
        //    foreach (GameObject go in roots)
        //    {
        //        if (go.name.StartsWith("Avatar") && go.name.Contains("[" + name + "]"))
        //        {
        //            found = go;
        //            break;
        //        }
        //    }

        //    if (found == null)
        //        return null;

        //    Debug.Log($"[{nameof(PlayerPlatform)}] Found local avatar: {found.name}", found);

        //    VRIKHandler vrik = found.GetComponentInChildren<VRIKHandler>();

        //    Debug.Log($"[{nameof(PlayerPlatform)}] VRIK found={vrik!=null}", vrik);

        //    if (vrik != null)
        //        return vrik;
        //    else
        //        return null;
        //}
    }
}
