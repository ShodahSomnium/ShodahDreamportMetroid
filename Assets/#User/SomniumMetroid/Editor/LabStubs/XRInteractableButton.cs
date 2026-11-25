using UnityEngine;
using UnityEngine.Events;

namespace SomniumSpace.Worlds.SomniumMetroid.LabStubs
{
    /// <summary>The mesh Home button's XRInteractableButton: a public UnityEvent field ButtonPressed (MpTabletLink reads it by reflection). For the tablet lab only; see TabletControl.cs.</summary>
    public sealed class XRInteractableButton : MonoBehaviour
    {
        public UnityEvent ButtonPressed = new UnityEvent();
    }
}
