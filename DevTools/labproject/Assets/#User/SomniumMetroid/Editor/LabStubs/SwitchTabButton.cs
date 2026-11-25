using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.LabStubs
{
    /// <summary>A client script on the home buttons (SwitchTabButton): the clone must lose it before it can wake. Counts how often it woke. For the tablet lab only; see TabletControl.cs.</summary>
    public sealed class SwitchTabButton : MonoBehaviour
    {
        public static int Woke;
        private void OnEnable() { Woke++; }
    }
}
