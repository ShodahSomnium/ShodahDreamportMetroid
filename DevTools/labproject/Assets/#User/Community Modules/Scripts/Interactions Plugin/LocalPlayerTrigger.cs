using SomniumSpace.Bridge;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    /// <summary>
    /// Detect somnium spcac player CharacterController, usufull for player detection
    /// </summary>
    public class LocalPlayerTrigger : MonoBehaviour
    {
        public event Action<CharacterController> OnPlayerEnter;
        public event Action<CharacterController> OnPlayerExit;

        [Header("Events")]
        [SerializeField] UnityEvent<CharacterController> _onPlayerEnter;
        [SerializeField] UnityEvent<CharacterController> _onPlayerExit;

        private GameObject _localPlayerBody = null;
        private CharacterController _localCharacterController = null;

        private void Start()
        {
            if(!Application.isEditor)
                SomniumBridge.Network.OnJoinedToGameSession += GetLocalPlayer;
        }

        private void OnDestroy()
        {
            if (!Application.isEditor && SomniumBridge.Network != null)
                SomniumBridge.Network.OnJoinedToGameSession -= GetLocalPlayer;
        }

        private void GetLocalPlayer()
        {
            Transform bodyTr = SomniumBridge.PlayersContainer?.LocalPlayer?.References?.Body?.Root;

            if (bodyTr == null)
            {
                Debug.LogError($"[{nameof(LocalPlayerTrigger)}] Can't find Local Player Body");
                return;
            }

            _localPlayerBody = bodyTr.gameObject;

            if (_localPlayerBody == null)
            {
                Debug.LogError($"[{nameof(LocalPlayerTrigger)}] Can't find Local Player Body CharacterController");
                return;
            }
            
            _localCharacterController = _localPlayerBody.GetComponent<CharacterController>();
        }

        private void OnTriggerEnter(Collider other)
        {
            // DEBUG REMEVE ME
            //if (other.gameObject.TryGetComponent(out CharacterController characterController))
            //{
            //    OnPlayerEnter?.Invoke(characterController);
            //    _onPlayerEnter?.Invoke(characterController);
            //}

            if (_localPlayerBody == null) // Local player not joined yet (always the case in the editor)
                return;

            if (_localPlayerBody == other.gameObject)
            {
                //Debug.Log($"[{nameof(CharacterTrigger)}] OnTriggerEnter: {other.name}");
                OnPlayerEnter?.Invoke(_localCharacterController);
                _onPlayerEnter?.Invoke(_localCharacterController);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // DEBUG REMEVE ME
            //if (other.gameObject.TryGetComponent(out CharacterController characterController))
            //{
            //    OnPlayerExit?.Invoke(characterController);
            //    _onPlayerExit?.Invoke(characterController);
            //}

            if (_localPlayerBody == null) // Local player not joined yet (always the case in the editor)
                return;

            if (_localPlayerBody == other.gameObject)
            {
                //Debug.Log($"[{nameof(CharacterTrigger)}] OnTriggerEnter: {other.name}");
                OnPlayerExit?.Invoke(_localCharacterController);
                _onPlayerExit?.Invoke(_localCharacterController);
            }
        }
    }
}
