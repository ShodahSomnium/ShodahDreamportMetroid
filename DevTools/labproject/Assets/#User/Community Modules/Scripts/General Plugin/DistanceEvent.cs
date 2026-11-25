using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    public class DistanceEvent : MonoBehaviour
    {
        [SerializeField] float _maxDistance = 20.0f;
        [Tooltip("In meters")]
        [SerializeField] float _checkDelay = 1.0f;

        [SerializeField] UnityEvent _onFar;
        [SerializeField] UnityEvent _onClose;

        [Header("Close = True, Far = False")]
        [Tooltip("Inverte the bool of _onChange")]
        [SerializeField] bool _onChangeInvert = false;
        [SerializeField] UnityEvent<bool> _onChange;

        private State _lastState = State.Null;

        private enum State
        {
            Null = 0,
            Far = 1,
            Close = 2
        }

        void Start()
        {
            InvokeRepeating(nameof(Check), Random.Range(0.0f, _checkDelay), _checkDelay);
        }

        void Check()
        {
            if (Camera.main == null)
                return;

            float distance = Vector3.Distance(transform.position, Camera.main.transform.position);
            if (distance < _maxDistance && _lastState != State.Close)
            {
                _lastState = State.Close;
                _onClose.Invoke();
                _onChange.Invoke(!_onChangeInvert);
            }
            else  if (distance >= _maxDistance && _lastState != State.Far)
            { 
                _lastState = State.Far;
                _onFar.Invoke();
                _onChange.Invoke(_onChangeInvert);
            }
        }
    }
}
