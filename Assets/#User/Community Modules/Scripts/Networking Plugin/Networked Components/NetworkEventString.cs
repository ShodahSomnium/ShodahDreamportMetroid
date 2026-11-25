using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkEventString : MonoBehaviour
    {
        /// <summary> Event to all players </summary>
        public event UnityAction<string> OnEvent;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking")]
        [SerializeField] private NetworkBridgeEvents _networkBridge;
        [Tooltip("ID of the event, for the same NetworkBridge, if you use multiple events, assign different values")]
        [SerializeField] private byte _eventID = 0;
        [Header("Network Event (No Memory)")]
        [Space]
        [SerializeField] private UnityEvent<string> _onEvent;

        void Start()
        {
            // Setup network events
            _networkBridge.OnMessageToAll += OnMessageToAll;
        }

        /// <summary>
        /// Send the event with the message to all players including locally
        /// </summary>
        public void SendEvent(string message)
        {
            byte[] strData = System.Text.Encoding.UTF8.GetBytes(message);
            BytesWriter bw = new(1 + strData.Length);
            bw.AddByte(_eventID);
            bw.AddByteArray(strData);
            _networkBridge.RPC_SendMessageToAll(bw.Data);
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            if(data.Length <= 1)
            {
                Debug.LogError($"[{nameof(NetworkSimpleEvent)}.OnMessageToAll(...)] Received data length: {data.Length}, is not >1.");
                return;
            }
            BytesReader br = new(data);
            byte eventId = br.NextByte();
            byte[] messageData = br.NextByteArray(data.Length - 1);
            string message = System.Text.Encoding.UTF8.GetString(messageData);

            if (eventId == _eventID)
            {
                _onEvent?.Invoke(message);
                OnEvent?.Invoke(message);
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
