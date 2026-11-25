using SomniumSpace.Bridge;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CommunityModules
{
    [DefaultExecutionOrder(999999)] // Necessary to be updated before the PlayerController is moved
    public class PlayerAnchor : MonoBehaviour
    {
        [SerializeField] private Transform _motionAnchor;
        [SerializeField] private Transform _platformObject;
        [SerializeField] private LocalPlayerTrigger _trigger;
        [SerializeField] private float _disconnectDistance = 10.0f;
        [SerializeField] private bool _rotatePlayer = false;
        [SerializeField] private bool _lockRotation = false;
        [SerializeField] private InputActionAsset _inputs;
        [SerializeField] private UnityEvent<bool> _onEnterPlatformState;

        private InputAction _exitAttach;
        private CharacterController _character;
        private float _lastPlatformAngle;
        private float _angleOffset;
        private bool _isPlayerAttached = false;

        private void Awake()
        {
            if (_motionAnchor == null || _trigger == null || _inputs == null)
            { 
                Debug.LogError($"[{nameof(PlayerAnchor)}] Reference is missing in inspector.");
                enabled = false;
                return;
            }

            // Own copy of the actions, so disabling one anchor does not switch off the input of the others
            _inputs = Instantiate(_inputs);
            _exitAttach = _inputs.FindAction("Exit", throwIfNotFound: true);
            _trigger.OnPlayerEnter += OnCharacterEnter;
            _trigger.OnPlayerExit += OnCharacterExit;

            _lastPlatformAngle = GetPlatformAngle();
        }

        private void OnEnable()
        {
            // Actions of an InputActionAsset start disabled: a disabled action always reads 0
            if (_exitAttach != null)
                _exitAttach.Enable();
        }

        private void OnDisable()
        {
            if (_exitAttach != null)
                _exitAttach.Disable();

            // Never leave the player without gravity and movement when the anchor goes away
            if (_isPlayerAttached)
                ExitAnchor();
        }

        private void OnDestroy()
        {
            if (_trigger != null)
            {
                _trigger.OnPlayerEnter -= OnCharacterEnter;
                _trigger.OnPlayerExit -= OnCharacterExit;
            }
            if (_exitAttach != null) // Only set once the copy exists: never destroy the original asset
                Destroy(_inputs);
        }

        private void OnCharacterEnter(CharacterController character)
        {
            _character = character;
            _angleOffset = _character.transform.eulerAngles.y - Camera.main.transform.eulerAngles.y;
            Debug.Log($"[{nameof(PlayerAnchor)}] OnCharacterEnter: Carac {_character.transform.eulerAngles.y}, Cam {Camera.main.transform.eulerAngles.y}");

            _onEnterPlatformState?.Invoke(true);
            _isPlayerAttached = true;
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetGravity(0, 0);
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetMovementSpeed(0, 0, 0);
        }

        private void OnCharacterExit(CharacterController character)
        {
            if (_isPlayerAttached)
                ExitAnchor();
        }

        private void ExitAnchor()
        {
            _character = null;
            _isPlayerAttached = false;
            _onEnterPlatformState?.Invoke(false);

            // The Bridge does not exist in the editor
            if (SomniumBridge.PlayersContainer == null || SomniumBridge.PlayersContainer.LocalPlayer == null)
                return;
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetGravity(1, 1);
            SomniumBridge.PlayersContainer.LocalPlayer.Features.Motion.SetMovementSpeed(1,1,1);
        }

        void Update()
        {
            UpdateCharacter();
            if (_isPlayerAttached && _exitAttach != null && _exitAttach.ReadValue<float>() > 0.5f)
                ExitAnchor();
        }

        private void LateUpdate()
        {
            UpdateRotation();
        }

        private void UpdateCharacter()
        {
            if (_platformObject != null)
            {
                _platformObject.position = _motionAnchor.position;
                _platformObject.rotation = _motionAnchor.rotation;
            }

            if (_character != null)
            {
                Vector3 playerPosition = _character.transform.TransformPoint(new(_character.center.x, 0, _character.center.z));
                if (Vector3.Distance(playerPosition, _motionAnchor.position) > _disconnectDistance)
                {
                    ExitAnchor(); // Also restores gravity and movement
                    return;
                }

                Vector3 bodyOffset = playerPosition - _character.transform.position;
                _character.transform.position = _motionAnchor.position - bodyOffset;
            }
        }

        private void UpdateRotation()
        {
            // Calculate platform rotation angle
            float angle = GetPlatformAngle();

            if (_character != null)
            {
                //Vector3 playerPosition = _character.transform.TransformPoint(new(_character.center.x, 0, _character.center.z));
                //if (Vector3.Distance(playerPosition, _motionAnchor.position) > _disconnectDistance)
                //{
                //    _character = null;
                //    return;
                //}
                //Vector3 bodyOffset = playerPosition - _character.transform.position;

                if (_lockRotation)
                {
                    Debug.Log($"[{nameof(PlayerAnchor)}] Offset {_angleOffset}");
                    _character.transform.rotation = Quaternion.Euler(0, -angle + _angleOffset + 90.0f, 0);
                }
                else if (_rotatePlayer)
                    _character.transform.RotateAround(_motionAnchor.position, Vector3.up, -angle + _lastPlatformAngle);
            }

            _lastPlatformAngle = angle;
        }

        private float GetPlatformAngle()
        { 
            return Mathf.Atan2(_motionAnchor.forward.z, _motionAnchor.forward.x) * Mathf.Rad2Deg;
        }
    }
}
