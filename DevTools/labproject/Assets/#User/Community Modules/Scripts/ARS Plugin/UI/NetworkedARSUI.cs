using SomniumSpace.Network.Bridge;
using System;
using System.ComponentModel;
using System.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CommunityModules
{
    public class NetworkedARSUI : MonoBehaviour
    {
        [SerializeField] private NetworkBridgeData8 _networkBridge;
        [SerializeField] private ARSController _arsController;

        [SerializeField] private ARSVideoPlayer _arsVideoPlayer;
        [SerializeField] private ARSMicrophone _arsMicrophone;
        [Header("UI")]
        [SerializeField] private Slider _arsShaderGain;
        [SerializeField] private Slider _arsGain1;
        [SerializeField] private Slider _arsGain2;
        [SerializeField] private Slider _arsGain3;
        [SerializeField] private Slider _arsGain4;

        [SerializeField] private Button _setSourceVideoPlayer;
        [SerializeField] private Button _setSourceMicrophone;
        [SerializeField] private TextMeshProUGUI _textARSSource;

        [SerializeField] private Button _requestControl;

        [SerializeField] private ARSSource _currentSource = ARSSource.VideoPlayer;

        enum ARSSource
        {
            VideoPlayer=0,
            Microphone=1,
        }

        enum NetID 
        {
            SetARSShaderGain = 0,
            SetARSGain1 = 1,
            SetARSGain2 = 2,
            SetARSGain3 = 3,
            SetARSGain4 = 4,
            SetSourceVideoPlayer = 5,
            SetSourceMic = 6,
        }

        enum NetDataIndex
        {
            SetARSShaderGain = 0,
            SetARSGain1 = 1,
            SetARSGain2 = 2,
            SetARSGain3 = 3,
            SetARSGain4 = 4,
            States = 5
        }

        enum NetByteID
        {
            Initialized = 0,
            ARSSource = 1
        }

        void Start()
        {
            _networkBridge.OnMessageToAll += OnNetworkMessage;
            _networkBridge.OnSpawned += OnNetworkReady;
            _networkBridge.OnStateAuthorityChanged += OnReceiveStateAuthority;
            _arsShaderGain.onValueChanged.AddListener(OnARSShaderGain);
            _arsGain1.onValueChanged.AddListener(OnARSGain1);
            _arsGain2.onValueChanged.AddListener(OnARSGain2);
            _arsGain3.onValueChanged.AddListener(OnARSGain3);
            _arsGain4.onValueChanged.AddListener(OnARSGain4);
            _setSourceVideoPlayer.onClick.AddListener(OnARSSourceVideo);
            _setSourceMicrophone.onClick.AddListener(OnARSSourceMic);
            _requestControl.onClick.AddListener(OnRequestControl);

            if (_arsVideoPlayer != null)
                _arsVideoPlayer.OnAudioSourceChanged += OnVideoPlayerAudioSourceChanged;
            else
                _setSourceVideoPlayer.interactable = false;
            if (_arsMicrophone != null)
                _arsMicrophone.OnAudioSourceChanged += OnMicrophoneAudioSourceChanged;
            else
                _setSourceMicrophone.interactable = false;

            // If only one source, disable buttons
            if (_arsVideoPlayer == null || _arsMicrophone == null)
            {
                _setSourceVideoPlayer.gameObject.SetActive(false);
                _setSourceMicrophone.gameObject.SetActive(false);
            }

            // Microphone only setup: start on the source that exists
            if (_arsVideoPlayer == null && _arsMicrophone != null)
                _currentSource = ARSSource.Microphone;

            // Set default Audio Source
            UpdateAudioSource();
        }

        private void OnNetworkReady()
        {
            Debug.Log($"[{nameof(NetworkedARSUI)}] OnSpawned");

            if (_networkBridge.Object.HasStateAuthority)
            {
                Debug.Log($"[{nameof(NetworkedARSUI)}] Local have state authority");

                if (GetNetworkByte(NetDataIndex.States, NetByteID.Initialized) == 0)
                {
                    Debug.Log($"[{nameof(NetworkedARSUI)}] Not initialized, set default variables");
                    SetNetworkByte(NetDataIndex.States, NetByteID.Initialized, 1);
                    SetNetworkByte(NetDataIndex.States, NetByteID.ARSSource, (byte)_currentSource);
                    // Init variables
                    SetNetworkFloat(NetDataIndex.SetARSShaderGain, 1.0f);
                    SetNetworkFloat(NetDataIndex.SetARSGain1, 1.0f);
                    SetNetworkFloat(NetDataIndex.SetARSGain2, 1.0f);
                    SetNetworkFloat(NetDataIndex.SetARSGain3, 1.0f);
                    SetNetworkFloat(NetDataIndex.SetARSGain4, 1.0f);
                }
            }

            float gain = GetNetworkFloat(NetDataIndex.SetARSShaderGain);
            Debug.Log($"[{nameof(NetworkedARSUI)}] Get Sync ARS ShaderGain: {gain}");
            SetShaderGain(gain);
            gain = GetNetworkFloat(NetDataIndex.SetARSGain1);
            Debug.Log($"[{nameof(NetworkedARSUI)}] Get Sync ARSGain1: {gain}");
            SetARSGain1(gain);
            gain = GetNetworkFloat(NetDataIndex.SetARSGain2);
            Debug.Log($"[{nameof(NetworkedARSUI)}] Get Sync ARSGain2: {gain}");
            SetARSGain2(gain);
            gain = GetNetworkFloat(NetDataIndex.SetARSGain3);
            Debug.Log($"[{nameof(NetworkedARSUI)}] Get Sync ARSGain3: {gain}");
            SetARSGain3(gain);
            gain = GetNetworkFloat(NetDataIndex.SetARSGain4);
            Debug.Log($"[{nameof(NetworkedARSUI)}] Get Sync ARSGain4: {gain}");
            SetARSGain4(gain);
            byte arsSource = GetNetworkByte(NetDataIndex.States, NetByteID.ARSSource);
            Debug.Log($"[{nameof(NetworkedARSUI)}] ARS Source is: {(ARSSource)arsSource}");

            _currentSource = (ARSSource)arsSource;
            UpdateAudioSource(); // Late joiners: apply the synced source, not the inspector default
            SetInteractable(_networkBridge.Object.HasStateAuthority);
        }

        /////////////// Events

        private void OnRequestControl()
        {
            if (!_networkBridge.Object.RequestStateAuthority())
                Debug.LogWarning($"[{nameof(NetworkedARSUI)}] State authority request was not sent");
        }

        private void OnVideoPlayerAudioSourceChanged(AudioSource audioSource)
        {
            UpdateAudioSource();
        }

        private void OnMicrophoneAudioSourceChanged(AudioSource audioSource)
        {
            UpdateAudioSource();
        }

        private void OnARSShaderGain(float value)
        {
            SetNetworkFloat(NetDataIndex.SetARSShaderGain, value);
            SendARSShaderGain(value);
        }

        private void OnARSGain1(float value)
        {
            SetNetworkFloat(NetDataIndex.SetARSGain1, value);
            SendARSGain1(value);
        }

        private void OnARSGain2(float value)
        {
            SetNetworkFloat(NetDataIndex.SetARSGain2, value);
            SendARSGain2(value);
        }

        private void OnARSGain3(float value)
        {
            SetNetworkFloat(NetDataIndex.SetARSGain3, value);
            SendARSGain3(value);
        }

        private void OnARSGain4(float value)
        {
            SetNetworkFloat(NetDataIndex.SetARSGain4, value);
            SendARSGain4(value);
        }

        private void OnARSSourceVideo()
        {
            SetNetworkByte(NetDataIndex.States, NetByteID.ARSSource, (byte)ARSSource.VideoPlayer);
            _networkBridge.RPC_SendMessageToAll(new byte[1] { (byte)NetID.SetSourceVideoPlayer });
        }

        private void OnARSSourceMic()
        {
            SetNetworkByte(NetDataIndex.States, NetByteID.ARSSource, (byte)ARSSource.Microphone);
            _networkBridge.RPC_SendMessageToAll(new byte[1] { (byte)NetID.SetSourceMic });
        }

        ///////// network events

        private void OnReceiveStateAuthority(bool hasAuthority)
        {
            Debug.Log($"[{nameof(NetworkedARSUI)}] StateAuthorityChanged: {hasAuthority}");
            SetInteractable(hasAuthority);
        }

        private void OnNetworkMessage(byte[] message)
        {
            BytesReader bytes = new(message);
            NetID bid = (NetID)bytes.NextByte();

            if (bid == NetID.SetSourceVideoPlayer)
            {
                SetARSSourceVideoPlayer();
            }
            else if (bid == NetID.SetSourceMic)
            {
                SetARSSourceMicrophone();
            }
            else if (bid == NetID.SetARSShaderGain)
            {   
                if (message.Length == 5)
                {
                    SetShaderGain(bytes.NextFloat());
                }
            }
            else if (bid == NetID.SetARSGain1)
            {
                if (message.Length == 5)
                {
                    SetARSGain1(bytes.NextFloat());
                }
            }
            else if (bid == NetID.SetARSGain2)
            {
                if (message.Length == 5)
                {
                    SetARSGain2(bytes.NextFloat());
                }
            }
            else if (bid == NetID.SetARSGain3)
            {
                if (message.Length == 5)
                {
                    SetARSGain3(bytes.NextFloat());
                }
            }
            else if (bid == NetID.SetARSGain4)
            {
                if (message.Length == 5)
                {
                    SetARSGain4(bytes.NextFloat());
                }
            }
        }

        /////////////// Setters

        private void SendARSShaderGain(float value)
        {
            BytesWriter bytes = new(1+sizeof(float));
            bytes.AddByte((byte)NetID.SetARSShaderGain);
            bytes.AddFloat(value);
            _networkBridge.RPC_SendMessageToAll(bytes.Data);
        }
        private void SendARSGain1(float value)
        {
            BytesWriter bytes = new(1 + sizeof(float));
            bytes.AddByte((byte)NetID.SetARSGain1);
            bytes.AddFloat(value);
            _networkBridge.RPC_SendMessageToAll(bytes.Data);
        }

        private void SendARSGain2(float value)
        {
            BytesWriter bytes = new(1 + sizeof(float));
            bytes.AddByte((byte)NetID.SetARSGain2);
            bytes.AddFloat(value);
            _networkBridge.RPC_SendMessageToAll(bytes.Data);
        }

        private void SendARSGain3(float value)
        {
            BytesWriter bytes = new(1 + sizeof(float));
            bytes.AddByte((byte)NetID.SetARSGain3);
            bytes.AddFloat(value);
            _networkBridge.RPC_SendMessageToAll(bytes.Data);
        }

        private void SendARSGain4(float value)
        {
            BytesWriter bytes = new(1 + sizeof(float));
            bytes.AddByte((byte)NetID.SetARSGain4);
            bytes.AddFloat(value);
            _networkBridge.RPC_SendMessageToAll(bytes.Data);
        }

        ///////////////

        private void SetInteractable(bool interactable)
        {
            _arsShaderGain.interactable = interactable;
            _arsGain1.interactable = interactable;
            _arsGain2.interactable = interactable;
            _arsGain3.interactable = interactable;
            _arsGain4.interactable = interactable;
            _setSourceVideoPlayer.interactable = interactable;
            _setSourceMicrophone.interactable = interactable;
        }

        private void SetShaderGain(float value)
        {
            _arsController.SetARSTextureGain(value);
            _arsShaderGain.SetValueWithoutNotify(value);
        }

        private void SetARSGain1(float value)
        {
            _arsController.SetChannel1Gain(value);
            _arsGain1.SetValueWithoutNotify(value);
        }

        private void SetARSGain2(float value)
        {
            _arsController.SetChannel2Gain(value);
            _arsGain2.SetValueWithoutNotify(value);
        }

        private void SetARSGain3(float value)
        {
            _arsController.SetChannel3Gain(value);
            _arsGain3.SetValueWithoutNotify(value);
        }

        private void SetARSGain4(float value)
        {
            _arsController.SetChannel4Gain(value);
            _arsGain4.SetValueWithoutNotify(value);
        }

        private void SetARSSourceVideoPlayer()
        {
            if (_arsVideoPlayer == null)
            {
                Debug.LogError($"[{nameof(NetworkedARSUI)}]: Cant set ARS to VideoPlayer, no ARSVideoPlayer assigned");
                return;
            }
            if (_arsVideoPlayer.AudioSource == null)
            {
                Debug.LogError($"[{nameof(NetworkedARSUI)}]: Cant set ARS to VideoPlayer, audio source is null");
                return;
            }
            Debug.Log($"[{nameof(NetworkedARSUI)}]: Setting ARS to AudioSource {_arsVideoPlayer.AudioSource.name}");
            _currentSource = ARSSource.VideoPlayer;
            UpdateAudioSource();
        }

        private void SetARSSourceMicrophone()
        {
            if (_arsMicrophone == null)
            {
                Debug.LogError($"[{nameof(NetworkedARSUI)}]: Cant set ARS to Microphone, no ARSMicrophone assigned");
                return;
            }
            if (_arsMicrophone.AudioSource != null)
                Debug.Log($"[{nameof(NetworkedARSUI)}]: Setting ARS to Mic AudioSource {_arsMicrophone.AudioSource.name}");
            else
                Debug.Log($"[{nameof(NetworkedARSUI)}]: Setting ARS to Mic AudioSource, no player present");

            _currentSource = ARSSource.Microphone;
            UpdateAudioSource();
        }

        private void UpdateAudioSource()
        {
            if (_currentSource == ARSSource.VideoPlayer)
            {
                if (_arsVideoPlayer == null || _arsVideoPlayer.AudioSource == null) // Ok to be null
                    return;

                ARSController.SetAudioSource(_arsVideoPlayer.AudioSource);
                _textARSSource.text = "VideoPlayer";
            }
            else
            {
                if (_arsMicrophone == null || _arsMicrophone.AmplifiedPlayer == null || _arsMicrophone.AudioSource == null) // Ok to be null
                {
                    _textARSSource.text = "Microphone: No Performer";
                    ARSController.SetAudioSource(null);
                    return;
                }

                if (_arsMicrophone.AmplifiedPlayer.Properties.IsLocal) 
                    _textARSSource.text = "Microphone: Player local";
                else
                    _textARSSource.text = "Microphone: Player " + _arsMicrophone.AmplifiedPlayer.Properties.NickName;
                ARSController.SetAudioSource(_arsMicrophone.AudioSource);
            }
        }

        private float GetNetworkFloat(NetDataIndex id)
        {
            int data = _networkBridge.IntArray.Get((int)id);
            return BitConverter.ToSingle(BitConverter.GetBytes(data), 0);
        }

        private void SetNetworkFloat(NetDataIndex id, float value)
        {
            int data = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            _networkBridge.IntArray.Set((int)id, data); 
        }

        private byte GetNetworkByte(NetDataIndex id, NetByteID byteId)
        {
            return GetByte((int)id, (int)byteId);
        }

        private void SetNetworkByte(NetDataIndex id, NetByteID byteId, byte value)
        {
            SetByte((int)id, (int)byteId, value);
        }

        public byte GetByte(int shortIndex, int byteIndex)
        {
            short shortValue = (short)_networkBridge.IntArray.Get(shortIndex);
            return (byte)((shortValue >> (byteIndex * 8)) & 0xFF);
        }

        public void SetByte(int shortIndex, int byteIndex, byte byteValue)
        {
            short shortValue = (short)_networkBridge.IntArray.Get(shortIndex);
            int shift = byteIndex * 8;
            shortValue = (short)((shortValue & ~(0xFF << shift)) | (byteValue << shift));
            _networkBridge.IntArray.Set(shortIndex, shortValue);
        }
    }
}
