using SomniumSpace.Network.Bridge;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class NetworkOwnershipEvent : MonoBehaviour
    {
        [SerializeField] private NetworkBridgeEvents _networkBridgeEvents;
        [Header("Events")]
        [SerializeField] private UnityEvent _onGainAuthority;
        [SerializeField] private UnityEvent _onLoseAuthority;
        [Header("Bool Event")]
        [SerializeField] private bool _onAuthorityChangedInvert = false;
        [SerializeField] private UnityEvent<bool> _onAuthorityChanged;

        // Awake, not Start: a spawned prefab can receive OnSpawned before its Start runs
        private void Awake()
        {
            _networkBridgeEvents.OnSpawned += Spawned;
            _networkBridgeEvents.OnStateAuthorityChanged += OnStateAuthorityChanged;
        }

        private void Spawned()
        {
            OnStateAuthorityChanged(_networkBridgeEvents.HasStateAuthority);
        }

        private void OnStateAuthorityChanged(bool haveAuthority)
        {
            if (_onAuthorityChangedInvert) 
                _onAuthorityChanged?.Invoke(!haveAuthority);
            else
                _onAuthorityChanged?.Invoke(haveAuthority);

            if (haveAuthority)
                _onGainAuthority?.Invoke();
            else
                _onLoseAuthority?.Invoke();
        }

        private void OnValidate()
        {
            if(_networkBridgeEvents == null)
                _networkBridgeEvents = GetComponent<NetworkBridgeEvents>();
        }
    }
}
