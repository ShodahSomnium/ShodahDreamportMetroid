using SomniumSpace.Bridge.Components;
using UnityEngine;
using WebSocketSharp;

namespace CommunityModules
{
    public class GetVideoPlayerTexture : MonoBehaviour
    {
        [SerializeField] private GameObject _videoPlayerObject;
        [SerializeField] private Material[] _materials = new Material[0];
        [SerializeField] private string _textureName = "_MainTex";

        private SomniumVideoPlayer _videoPlayerRef;
        private Texture _videoTexture = null;

        // Start is called before the first frame update
        void Start()
        {
            InvokeRepeating(nameof(GetTexture), 1.0f, 1.0f);
        }

        private void GetTexture()
        {
            if (_materials == null || 
                _videoPlayerObject == null)
                return;

            if (_videoPlayerRef == null) // _videoPlayerObject
                _videoPlayerRef = _videoPlayerObject.GetComponent<SomniumVideoPlayer>();

            if (_videoTexture == null)
            {
                _videoTexture = _videoPlayerRef.GetTexture();
                AssignTexture();
            }
        }

        private void AssignTexture()
        {
            if (_textureName.IsNullOrEmpty())
                return;

            foreach (var mat in _materials)
            {
                if (mat != null)
                    mat.SetTexture(_textureName, _videoTexture);
            }
        }
    }
}
