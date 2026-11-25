using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.LabStubs
{
    /// <summary>
    /// Stand-in for the client's VRTablet/Tablet/TabletControl.cs, reduced to what MpTabletLink reads and calls (it finds the real one by TYPE NAME): IsOpened and the
    /// public DeActivate(). For the tablet lab only (MpLabTabletRig.cs). This folder is its own assembly with the define constraint UNITY_EDITOR: it is never compiled
    /// outside the editor, and it is a runtime-kind assembly because Unity refuses AddComponent for a MonoBehaviour of an editor-only assembly ("it is an editor script").
    /// </summary>
    public sealed class TabletControl : MonoBehaviour
    {
        public bool IsOpened { get; set; }
        public CanvasGroup Group;
        public int DeactivateCalls;

        public void Activate() { IsOpened = true; Group.alpha = 1f; Group.blocksRaycasts = true; }
        public void DeActivate() { DeactivateCalls++; IsOpened = false; Group.alpha = 0f; Group.blocksRaycasts = false; }
    }
}
