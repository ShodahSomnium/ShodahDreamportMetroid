using UnityEngine;

namespace CommunityModules
{
    /// <summary>
    /// Used to disable or destroy a GameObject when the world start
    /// </summary>
    public class DisableOnStart : MonoBehaviour
    {
        [SerializeField] private GameObject _targetObject;
        [SerializeField] private bool _destroyOnStart = false;

        void Start()
        {
            if (_destroyOnStart)
            {
                Destroy(this.gameObject);
            }
            else
            {
                _targetObject.SetActive(false);
            }
        }
    }
}
