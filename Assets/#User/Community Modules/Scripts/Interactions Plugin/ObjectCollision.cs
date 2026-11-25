using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class ObjectCollision : MonoBehaviour
    {
        public UnityEvent OnEnter;
        public UnityEvent OnExit;
        [Header("Optional (Ingored if empty)")]
        [SerializeField] private string _objectName = "";
        [SerializeField] private string _objectTag = "";

        private void OnCollisionEnter(Collision other)
        {
            bool isNamePass = _objectName.Length == 0 || other.gameObject.name == _objectName;
            bool isTagPass = _objectTag.Length == 0 || other.gameObject.CompareTag(_objectTag);

            if (isNamePass && isTagPass)
            {
                OnEnter?.Invoke();
            }
        }

        private void OnCollisionExit(Collision other)
        {
            bool isNamePass = _objectName.Length == 0 || other.gameObject.name == _objectName;
            bool isTagPass = _objectTag.Length == 0 || other.gameObject.CompareTag(_objectTag);

            if (isNamePass && isTagPass)
            {
                OnExit?.Invoke();
            }
        }
    }
}
