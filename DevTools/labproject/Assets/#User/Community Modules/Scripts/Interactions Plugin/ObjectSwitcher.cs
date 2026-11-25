using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class ObjectSwitcher : MonoBehaviour
    {
        public bool IsInTransition => _inTransition;

        [Header("Version [2026, 05, 01]")]
        [Header("Switchable Objects")]
        [SerializeField] private byte _initialIndex = 0;
        [SerializeField] private GameObject[] _switchableObjects;
        [Header("Transition Effect (Optional)")]
        [SerializeField] private float _fadeInDuration = 0.5f;
        [SerializeField] private float _fadeOutDuration = 0.5f;
        [SerializeField] private UnityEvent _fadeInEvent;

        private bool _inTransition = false;
        private int _currentIndex = 0;

        void Start()
        {
            _currentIndex = _initialIndex;
            SetSelectedObject(_initialIndex);
        }

        /// <summary>
        /// Set the selected object for all players
        /// </summary>
        public void ChangeActiveIndex(int index)
        {
            if (_inTransition)
                return;
            if (index == _currentIndex)
                return;

            // Apply locally
            _currentIndex = index;
            StartCoroutine(FadeIn());
        }

        // Call event for optional fade transition
        private IEnumerator FadeIn()
        {
            _inTransition = true;
            _fadeInEvent?.Invoke();
            yield return new WaitForSeconds(_fadeInDuration);
            SetSelectedObject(_currentIndex);
            yield return new WaitForSeconds(_fadeOutDuration);
            _inTransition = false;
        }

        // Change the selected object (enable it and disable the others)
        private void SetSelectedObject(int index)
        {
            if (_switchableObjects != null)
            {
                for (int i = 0; i < _switchableObjects.Length; i++)
                {
                    if (_switchableObjects[i] != null)
                        _switchableObjects[i].SetActive(i == index);
                }
            }
        }
    }
}
