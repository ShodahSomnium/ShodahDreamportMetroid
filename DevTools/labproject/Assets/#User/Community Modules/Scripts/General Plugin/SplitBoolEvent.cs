using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class SplitBoolEvent : MonoBehaviour
    {
        [SerializeField] UnityEvent _onTrue;
        [SerializeField] UnityEvent _onFalse;
        [SerializeField] UnityEvent<bool> _onChangeInverted;

        public void SetValue(bool value)
        { 
            if (value)
                _onTrue?.Invoke();
            else 
                _onFalse?.Invoke();

            _onChangeInverted?.Invoke(!value);
        }
    }
}
