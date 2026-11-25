using Fusion;
using SomniumSpace.Network.Bridge;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CommunityModules
{
    public class NetworkedScriptExample : MonoBehaviour
    {
        [Header("Alpha V2.0 [2026, 04, 23]")]
        [SerializeField] private NetworkObject _networkObject;
        [SerializeField] private NetworkBridgeEvents _networkBridge;
        [SerializeField] private TextMeshProUGUI _networkReady;
        [SerializeField] private TextMeshProUGUI _networkObjectOK;
        [SerializeField] private TextMeshProUGUI _networkBridgeOK;
        [SerializeField] private TextMeshProUGUI _haveAuthority;
        [SerializeField] private TextMeshProUGUI _MessageToMaster;
        [SerializeField] private TextMeshProUGUI _MessageToProxies;
        [SerializeField] private TextMeshProUGUI _MessageToAll;
        [SerializeField] private TextMeshProUGUI _variableA;
        [SerializeField] private TextMeshProUGUI _variableB;
        [SerializeField] private TextMeshProUGUI _variableC;
        [SerializeField] private TextMeshProUGUI _variableD;
        [SerializeField] private Button _btnRequestAuthority;
        [SerializeField] private Button _btnSentToMaster;
        [SerializeField] private Button _btnSentToProxies;
        [SerializeField] private Button _btnSentToAll;
        [SerializeField] private Button _btnWriteVariables;
        [SerializeField] private Button _btnReadVariables;

        [SerializeField] private TextMeshProUGUI _events;

        // Awake, not Start: a spawned prefab can receive OnSpawned before its Start runs
        private void Awake()
        {
            _events.text = "Script Start";

            _btnRequestAuthority.onClick.AddListener(OnRequestAuthority);
            _btnSentToMaster.onClick.AddListener(OnToMasterClick);
            _btnSentToProxies.onClick.AddListener(OnToProxiesClick);
            _btnSentToAll.onClick.AddListener(OnToAllClick);

            _networkBridge.OnSpawned += OnSpawned;
            _networkBridge.OnStateAuthorityChanged += OnAuthorityChanged;

            _networkBridge.OnMessageToController += OnMessageToControler;
            _networkBridge.OnMessageToProxies += OnMessageToProxies;
            _networkBridge.OnMessageToAll += OnMessageToAll;
            _networkBridge.OnStateAuthorityChanged += HaveAuthority;

            SceneNetworking.OnLocalPlayerJoined += OnLocalPlayerJoined;
            SceneNetworking.OnOtherPlayerJoined += OnPlayerJoin;
            SceneNetworking.OnOtherPlayerLeft += OnPlayerLeft;
        }

        private void Init()
        {
            _networkReady.text = "Ok";
            if (_networkObject.IsValid && _networkObject.isActiveAndEnabled)
                _networkObjectOK.text = "Ok";
            else
                _networkObjectOK.text = "Error";

            if (_networkBridge.isActiveAndEnabled)
                _networkBridgeOK.text = "Ok";
            else
                _networkBridgeOK.text = "Error";
            HaveAuthority();
        }

        private void OnSpawned()
        {
            _events.text += "\nComponent Bridge Spawned";
            Init();
        }

        private void OnLocalPlayerJoined()
        {
            _events.text += "\nLocal Player Joined Room";
        }

        private void OnPlayerJoin(PlayerRef player)
        {
            _events.text += "\nPlayer Joined, ID: " + player.PlayerId;
        }

        private void OnPlayerLeft(PlayerRef player)
        {
            _events.text += "\nPlayer Left, ID: " + player.PlayerId;
        }

        private void OnAuthorityChanged(bool haveAuthority)
        {
            _events.text += "\nStateAuthorityChanged: " + haveAuthority;
            HaveAuthority();
        }

        private void OnRequestAuthority()
        {
            _events.text += "\nRequest Authority";

            if (_networkObject.HasStateAuthority == false)
            {
                if (!_networkObject.RequestStateAuthority())
                    Debug.LogWarning("State authority request was not sent");
            }
        }

        private void OnToMasterClick()
        {
            int number = Random.Range(0, 99999);
            _events.text += $"\nSend Message To Master {number}";
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(number.ToString());
            _networkBridge.RPC_SendMessageToController(messageBytes);
        }

        private void OnToProxiesClick()
        {
            int number = Random.Range(0, 99999);
            _events.text += $"\nSend Message To Proxies {number}";
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(number.ToString());
            _networkBridge.RPC_SendMessageToProxies(messageBytes);
        }

        private void OnToAllClick()
        {
            int number = Random.Range(0, 99999);
            _events.text += $"\nSend Message To All {number}";
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(number.ToString());
            _networkBridge.RPC_SendMessageToAll(messageBytes);
        }

        private void OnMessageToControler(byte[] data)
        { 
            string message = System.Text.Encoding.UTF8.GetString(data);
            _MessageToMaster.text = message;
        }

        private void OnMessageToProxies(byte[] data)
        {
            string message = System.Text.Encoding.UTF8.GetString(data);
            _MessageToProxies.text = message;
        }

        private void OnMessageToAll(byte[] data)
        {
            string message = System.Text.Encoding.UTF8.GetString(data);
            _MessageToAll.text = message;
        }

        private void HaveAuthority(bool _)
        {
            HaveAuthority();
        }

        private void HaveAuthority()
        {
            _haveAuthority.text = _networkObject.HasStateAuthority ? "Yes" : "No";
        }
    }
}
