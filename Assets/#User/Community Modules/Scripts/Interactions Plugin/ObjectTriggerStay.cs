using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class ObjectTriggerStay : MonoBehaviour
    {
        public UnityEvent OnStay;
        [Header("Optional (Ingored if empty)")]
        [SerializeField] private LayerMask _objectLayer = ~0;
        [SerializeField] private string _objectName = "";
        [SerializeField] private string _objectTag = "";

        private void OnTriggerStay(Collider other)
        {
            bool isLayerPass = (_objectLayer & 1 << other.gameObject.layer) != 0; 
            bool isNamePass = _objectName.Length == 0 || other.gameObject.name == _objectName;
            bool isTagPass = _objectTag.Length == 0 || other.gameObject.CompareTag(_objectTag);

            if (isLayerPass && isNamePass && isTagPass)
            {
                OnStay?.Invoke();
            }
        }
    }
}
