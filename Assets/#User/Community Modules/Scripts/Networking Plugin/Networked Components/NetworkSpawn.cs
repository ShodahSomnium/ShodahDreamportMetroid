using Fusion;
using UnityEngine;

namespace CommunityModules
{
    /// <summary>
    /// Simple component to instantiate network prefab
    /// </summary>
    public class NetworkSpawn : MonoBehaviour
    {
        [Header("Version [2026, 05, 01]")]
        [Tooltip("The prefab must be listed in the SceneNetworking component")]
        [SerializeField] NetworkObject _networkPrefab;
        [SerializeField] private Transform _targetLocation;

        /// <summary>
        /// Spawn an instance of the network prefab
        /// - The network prefab must be listed in the SceneNetworking component
        /// </summary>
        public void Spawn()
        {
            NetworkPrefabId _prefabId = SceneNetworking.Instance.NetworkPrefabs[_networkPrefab];
            NetworkObject no = SceneNetworking.NetworkRunnerRef.Spawn(_prefabId, _targetLocation.transform.position);
        }

    }
}
