using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkEventColor : MonoBehaviour
    {
        /// <summary> Event to all players </summary>
        public event UnityAction<Color> OnEvent;

        [Header("Version [2026, 05, 01]")]
        [Header("Networking (Use 1 Data Slot)")]
        [SerializeField] private NetworkBridgeData _networkBridgeData;
        [Tooltip("Index of the networked data value used in the NetworkBridgeData component")]
        [SerializeField] private byte _dataIndex = 0;
        [Header("Network Event")]
        [Tooltip("Value when initialized")]
        [SerializeField] private Color32 _initialValue = new(255, 255, 255, 255);
        [Space]
        [SerializeField] private UnityEvent<Color32> _onEventColor32;
        [SerializeField] private UnityEvent<Color> _onEventColor;

        private NetValue _netColor32; // Networked variable

        // Awake, not Start: a spawned prefab can receive OnSpawned before its Start runs
        private void Awake()
        {
            // Setup network variable
            _netColor32 = new NetValue(_networkBridgeData, _dataIndex);

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
        /// Send color value, clamped between 0 and 1
        /// </summary>
        public void SetRedValue(float red)
        {
            Color32 color = _netColor32.GetColor32();
            color.r = (byte)(red * 255.0f);
            SetValue(color);
        }

        /// <summary>
        /// Send color value, clamped between 0 and 1
        /// </summary>
        public void SetGreenValue(float green)
        {
            Color32 color = _netColor32.GetColor32();
            color.g = (byte)(green * 255.0f);
            SetValue(color);
        }

        /// <summary>
        /// Send color value, clamped between 0 and 1
        /// </summary>
        public void SetBlueValue(float blue)
        {
            Color32 color = _netColor32.GetColor32();
            color.b = (byte)(blue * 255.0f);
            SetValue(color);
        }

        /// <summary>
        /// Send value to all players, color channels are clamped (0.0f, 1.0f)
        /// </summary>
        public void SetValue(Color color)
        {
            SetValue((Color32)color); // Explicit cast, otherwise this overload calls itself forever
        }

        /// <summary>
        /// Send value to all players
        /// </summary>
        public void SetValue(Color32 color)
        {
            // Create the data to be sent
            BytesWriter data = new(5);  // 1 byte + 1 Color32 = 1 + 4 bytes = 5 bytes
            data.AddByte(_dataIndex);
            data.AddColor32(color);

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
                ApplyColor(_initialValue);
                return;
            }

            // Setup when network object got connected
            if (_networkBridgeData.Object.HasStateAuthority) // Simulate if OffLine
            {
                // Apply locally the default value
                ApplyColor(_initialValue);
                // Save default value in synced variable
                _netColor32.SetColor32(_initialValue);
            }
            else
            {
                // Apply locally from network variable
                ApplyColor(_netColor32.GetColor32());
            }
        }

        // Receive network message
        private void OnMessageToAll(byte[] data)
        {
            if (data.Length == 0)
            {
                Debug.LogError($"[{nameof(NetworkEventColor)}.OnMessageToAll(...)] Received empty data in OnMessageToAll");
                return;
            }

            // Read values from data
            BytesReader reader = new(data);
            byte id = reader.NextByte();

            // Check if the index correspond
            if (id == _dataIndex && data.Length == 5)
            {
                // Read color from data
                Color32 color = reader.NextColor32();

                // If owner, update network variable
                if (SceneNetworking.IsConnected && _networkBridgeData.Object.HasStateAuthority)
                    _netColor32.SetColor32(color);

                // Apply received float locally
                ApplyColor(color);
            }
        }

        // Call the event with new value
        private void ApplyColor(Color32 value)
        {
            _onEventColor?.Invoke(value);
            _onEventColor32?.Invoke(value);
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
