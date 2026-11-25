using UnityEngine;
using UnityEngine.Events;

namespace CommunityModules
{
    /// <summary>
    /// Ecevute event when the main camera is created
    /// </summary>
    public class WaitForCamera : MonoBehaviour
    {
        public UnityEvent OnMainCameraReady;

        void Start()
        {
            InvokeRepeating(nameof(WaitForCameraLoop), 0.5f, 0.5f);
        }

        private void WaitForCameraLoop()
        {
            if (Camera.main != null)
            { 
                OnMainCameraReady?.Invoke();
                CancelInvoke(nameof(WaitForCameraLoop));
            }
        }
    }
}
