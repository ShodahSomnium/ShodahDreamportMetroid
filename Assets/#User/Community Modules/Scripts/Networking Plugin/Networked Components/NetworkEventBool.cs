using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkEventBool : MonoBehaviour
    {
        // Public events, ment to be used by scripts
        public event UnityAction<bool> OnChange;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking (Use 1 Data Slot)")]
        [SerializeField] private NetworkBridgeData _networkBridgeData;
        [Tooltip("Index of the networked data value used in the NetworkBridgeData component")]
        [SerializeField] private byte _dataIndex = 0;
        [Header("Network Event")]
        [Tooltip("Value when initialized")]
        [SerializeField] private bool _initialValue = false;
        [SerializeField] private UnityEvent<bool> _onEvent;

        private NetValue _netBoolValue; // Networked variable

        // Awake, not Start: a spawned prefab can receive OnSpawned before its Start runs
        private void Awake()
        {
            // Setup network variable
            _netBoolValue = new NetValue(_networkBridgeData, _dataIndex);

            // Setup network events
            _networkBridgeData.OnSpawned += OnSpawn;
            _networkBridgeData.OnMessageToAll += OnMessageToAll;
        }

        // Cleanup when component is destroy
        private void OnDestroy()
        {
            _networkBridgeData.OnSpawned -= OnSpawn;
            _networkBridgeData.OnMessageToAll -= OnMessageToAll;
        }

        /// <summary>
        /// Set value for all players
        /// </summary>
        public void SetValue(bool value)
        {
            // Create the data to be sent
            BytesWriter data = new(2);
            data.AddByte(_dataIndex);
            data.AddByte(value ? (byte)1 : (byte)0);

            if (SceneNetworking.IsDebugMode) // Debug mode, only call locally
                OnMessageToAll(data.Data);
            else
                _networkBridgeData.RPC_SendMessageToAll(data.Data);
        }

        // OnSpawn, called once when the network object is ready
        private void OnSpawn()
        {
            // Debug mode ignore networking
            if (SceneNetworking.IsDebugMode)
            {
                CallEvents(_initialValue);
                return;
            }

            // Init for Authority owner
            if (_networkBridgeData.Object.HasStateAuthority)
            {
                // Apply locally the default value
                CallEvents(_initialValue);
                // Save default value in synced variable
                _netBoolValue.SetBool(_initialValue);
            }
            // Init for other players
            else
            {
                // Apply locally from network variable
                CallEvents(_netBoolValue.GetBool());
            }
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            if (data.Length != 2)
            {
                Debug.LogError($"[{nameof(NetworkEventBool)}.OnMessageToAll(...)] data.Length: {data.Length}, is not 2.");
                return;
            }

            // Read values from data
            BytesReader reader = new(data);
            byte id = reader.NextByte();

            // Check if the index correspond
            if (id == _dataIndex)
            {
                bool value = reader.NextByte() == 1;
                // If owner, update network variable
                if (SceneNetworking.IsConnected && _networkBridgeData.Object.HasStateAuthority)
                    _netBoolValue.SetBool(value);

                // Apply locally
                CallEvents(value);
            }
        }

        // Call the events
        private void CallEvents(bool value)
        {
            OnChange?.Invoke(value);
            _onEvent?.Invoke(value);
        }

        // Editor Only, validate inspector values
        private void OnValidate()
        {
            if(_networkBridgeData == null)
                _networkBridgeData = GetComponent<NetworkBridgeData>();

            if (_networkBridgeData != null)
            {
                // Clamping data index to valid range
                if (_dataIndex <= 0)
                    _dataIndex = 0;
                if (_dataIndex >= _networkBridgeData.IntCount)
                    _dataIndex = (byte)(_networkBridgeData.IntCount - 1);
            }
        }
    }
}
