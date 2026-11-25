using SomniumSpace.Network.Bridge;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkEvent : MonoBehaviour
    {
        /// <summary> Event to all players </summary>
        public event Action<byte[]> OnEventToAll;
        /// <summary> Event to the player with authority (controller of the object) </summary>
        public event Action<byte[]> OnEventToController;
        /// <summary> Event to all player except the one with authority (controller of the object) </summary>
        public event Action<byte[]> OnEventToProxies;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking")]
        [SerializeField] private NetworkBridgeData _networkBridge;
        [Tooltip("ID of the event, for the same NetworkBridge, if you use multiple events, assign different values")]
        [SerializeField] private byte _eventID = 0;
        [Header("Network Events")]
        [SerializeField] private UnityEvent<byte[]> _onEventToAll;
        [SerializeField] private UnityEvent<byte[]> _onEventToController;
        [SerializeField] private UnityEvent<byte[]> _onEventToProxies;

        void Start()
        {
            // Setup network events
            _networkBridge.OnMessageToAll += OnMessageToAll;
            _networkBridge.OnMessageToController += OnMessageController;
            _networkBridge.OnMessageToProxies+= OnMessageToProxies;
        }

        /// <summary>
        /// Send the event to all players including locally
        /// </summary>
        public void SendEventToAll(byte[] data = null)
        {
            if (data == null)
                data = new byte[1] { 0 }; // make sure message is not null, need to be double checked if necessary

            BytesWriter bw = new(1 + data.Length); // 1 byte event id + payload
            bw.AddByte(_eventID);
            bw.AddByteArray(data);

            _networkBridge.RPC_SendMessageToAll(bw.Data);
        }

        /// <summary>
        /// Send an event to the player that own this network component (the player with authority)
        /// </summary>
        public void SendEventToController(byte[] data = null)
        {
            if (data == null)
                data = new byte[1] { 0 }; // make sure message is not null, need to be double checked

            BytesWriter bw = new(1 + data.Length); // 1 byte event id + payload
            bw.AddByte(_eventID);
            bw.AddByteArray(data);

            _networkBridge.RPC_SendMessageToController(bw.Data);
        }

        /// <summary>
        /// Send an event to all players except the owner of the network component
        /// </summary>
        public void SendEventToProxies(byte[] data = null)
        {
            if (data == null)
                data = new byte[1] { 0 }; // make sure message is not null, need to be double checked

            BytesWriter bw = new(1 + data.Length); // 1 byte event id + payload
            bw.AddByte(_eventID);
            bw.AddByteArray(data);

            _networkBridge.RPC_SendMessageToProxies(bw.Data);
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            BytesReader br = new(data);
            int id = br.NextByte();
            byte[] message = br.NextByteArray(data.Length-1);

            if (id == _eventID)
            {
                _onEventToAll?.Invoke(message);
                OnEventToAll?.Invoke(message);
            }
        }

        // Receive network message
        private void OnMessageController( byte[] data)
        {
            BytesReader br = new(data);
            int id = br.NextByte();
            byte[] message = br.NextByteArray(data.Length - 1);

            if (id == _eventID)
            {
                _onEventToController?.Invoke(message);
                OnEventToController?.Invoke(message);
            }
        }

        // Receive network message
        private void OnMessageToProxies(byte[] data)
        {
            BytesReader br = new(data);
            int id = br.NextByte();
            byte[] message = br.NextByteArray(data.Length - 1);

            if (id == _eventID)
            {
                _onEventToProxies?.Invoke(message);
                OnEventToProxies?.Invoke(message);
            }
        }

        // Editor Only, validate/setup inspector values
        private void OnValidate()
        {
            if (_networkBridge != null)
                _networkBridge = GetComponent<NetworkBridgeData>();
        }
    }
}
