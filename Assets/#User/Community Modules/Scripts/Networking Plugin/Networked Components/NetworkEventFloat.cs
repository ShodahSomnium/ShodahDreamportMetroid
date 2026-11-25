using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkEventFloat : MonoBehaviour
    {
        /// <summary> Event to all players </summary>
        public event UnityAction<float> OnEvent;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking (Use 1 Data Slot)")]
        [SerializeField] private NetworkBridgeData _networkBridgeData;
        [Tooltip("Index of the networked data value used in the NetworkBridgeData component")]
        [SerializeField] private byte _dataIndex = 0;
        [Header("Network Event")]
        [Tooltip("Value when initialized")]
        [SerializeField] private float _initialValue = 0.0f;
        [SerializeField] private UnityEvent<float> _onEvent;

        private NetValue _netFloat; // Networked variable

        // Awake, not Start: a spawned prefab can receive OnSpawned before its Start runs
        private void Awake()
        {
            // Setup network variable
            _netFloat = new NetValue(_networkBridgeData, _dataIndex);

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
        /// Send value to all players
        /// </summary>
        public void SetValue(float value)
        {
            // Create the data to be sent
            BytesWriter data = new(1 + BytesWriter.FloatSize);
            data.AddByte(_dataIndex);
            data.AddFloat(value);

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
                ApplyFloat(_initialValue);
                return;
            }

            // Setup when network object got connected
            if (_networkBridgeData.Object.HasStateAuthority) // Simulate if OffLine
            {
                // Apply locally the default value
                ApplyFloat(_initialValue);
                // Save default value in synced variable
                _netFloat.SetFloat(_initialValue);
            }
            else
            {
                // Apply locally from network variable
                ApplyFloat(_netFloat.GetFloat());
            }
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            if (data.Length == 0)
            {
                Debug.LogError($"[{nameof(NetworkEventFloat)}.OnMessageToAll(...)] Received empty data in OnMessageToAll");
                return;
            }

            // Read values from data
            BytesReader reader = new(data);
            byte id = reader.NextByte();

            // Check if the index correspond
            if (id == _dataIndex)
            {
                float value = reader.NextFloat();
                // If owner, update network variable
                if (SceneNetworking.IsConnected && _networkBridgeData.Object.HasStateAuthority)
                    _netFloat.SetFloat(value);

                // Apply received float locally
                ApplyFloat(value);
            }
        }

        // Call the event with new value
        private void ApplyFloat(float value)
        {
            _onEvent?.Invoke(value);
            OnEvent?.Invoke(value);
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
