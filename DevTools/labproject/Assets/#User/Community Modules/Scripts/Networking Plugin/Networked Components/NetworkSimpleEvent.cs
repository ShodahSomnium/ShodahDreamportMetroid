using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkSimpleEvent : MonoBehaviour
    {
        /// <summary> Event to all players </summary>
        public event UnityAction OnEvent;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking")]
        [SerializeField] private NetworkBridgeEvents _networkBridge;
        [Tooltip("ID of the event, for the same NetworkBridge, if you use multiple events, assign different values")]
        [SerializeField] private byte _eventID = 0;
        [Header("Network Event")]
        [Space]
        [SerializeField] private UnityEvent _onEvent;

        void Start()
        {
            // Setup network events
            _networkBridge.OnMessageToAll += OnMessageToAll;
        }

        /// <summary>
        /// Send the event to all players including locally
        /// </summary>
        public void SendEvent()
        {
            _networkBridge.RPC_SendMessageToAll(new byte[1] { _eventID });
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            if(data.Length != 1)
            {
                Debug.LogError($"[{nameof(NetworkSimpleEvent)}.OnMessageToAll(...)] Received data length: {data.Length}, is not 1.");
                return;
            }

            if (data[0] == _eventID)
            {
                _onEvent?.Invoke();
                OnEvent?.Invoke();
            }
        }

        // Editor Only, validate/setup inspector values
        private void OnValidate()
        {
            if (_networkBridge == null)
                _networkBridge = GetComponent<NetworkBridgeEvents>();
        }
    }
}
