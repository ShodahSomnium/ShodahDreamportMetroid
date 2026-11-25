using UnityEngine;

namespace CommunityModules
{
    [HelpURL("https://incrediworlds.gitbook.io/somnium-space-dendoc/worlds-creation/community-modules/community-networking")]
    public class AnimatorParameterSetter : MonoBehaviour
    {
        [Header("Alpha V2.1 [2026, 07, 19]")]
        [SerializeField] private Animator _animator;
        [Header("Used for setting parameter")]
        [SerializeField] private string _parameterName = "Value";

        // Use with unity events to change the current parameter name
        public void ChangeParameterName(string parameterName)
        {
            _parameterName = parameterName;
        }

        public void SetTrigger()
        {
            if (_animator != null)
            {
                _animator.SetTrigger(_parameterName);
            }
        }

        public void SetBool(bool value)
        {
            if (_animator != null)
            {
                _animator.SetBool(_parameterName, value);
            }
        }

        public void SetInt(int value)
        {
            if (_animator != null)
            {
                _animator.SetInteger(_parameterName, value);
            }
        }

        public void SetFloat(float value)
        {
            if (_animator != null)
            {
                _animator.SetFloat(_parameterName, value);
            }
        }
    }
}
