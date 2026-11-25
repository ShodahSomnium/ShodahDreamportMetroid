using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CommunityModules
{
    public class TogglesSelector : MonoBehaviour
    {
        /// <summary>
        /// Get the value from here, use SetValueNoNotify if you just want to set the UI without calling the event.
        /// </summary>
        public int Value 
        {
            get
            {
                return _value;
            }
            set
            {
                SetActiveToggle(value);
            }
        }

        [SerializeField] private Toggle[] _toggles;
        [SerializeField] private int _initialIndex = 0;
        [SerializeField] private UnityEvent<int> _onChange;

        private UnityAction<bool>[] _events;

        private int _value = 0;

        private void Start()
        {
            // Connect events to toggles
            _events = new UnityAction<bool>[_toggles.Length];
            for (int n = 0; n < _events.Length; n++)
            {
                int index = n;
                _events[n] = (isOn) => OnToggleChange(isOn, index);
                if (_toggles[n] != null)
                {
                    _toggles[n].onValueChanged.AddListener(_events[n]);
                }
                else
                    Debug.LogError($"[{nameof(TogglesSelector)}.Start()] A toggle is null, id:{n}");
            }
        }

        private void OnDestroy()
        {
            // Clean events from toggles, (not very necesarry but this is a good practice)
            for (int n = 0; n < _events.Length; n++)
            {
                if (_toggles[n] != null)
                    _toggles[n].onValueChanged.RemoveListener(_events[n]);
            }
        }

        /// <summary>
        /// Only change the UI, do not call OnChange event.
        /// </summary>
        public void SetValueNoNotify(int value)
        {
            _value = value;
            for (int n = 0; n < _toggles.Length; n++)
            {
                if (_toggles[n] != null)
                    _toggles[n].SetIsOnWithoutNotify(n == value);
                else
                    Debug.LogError($"[{nameof(TogglesSelector)}.Start()] A toggle is null, id:{n}");
            }
        }

        private void SetActiveToggle(int value)
        {
            SetValueNoNotify(value);
            _onChange?.Invoke(value);
        }

        private void OnToggleChange(bool isOn, int index)
        {
            if (isOn)
            {
                SetActiveToggle(index);
            }
        }
    }
}
