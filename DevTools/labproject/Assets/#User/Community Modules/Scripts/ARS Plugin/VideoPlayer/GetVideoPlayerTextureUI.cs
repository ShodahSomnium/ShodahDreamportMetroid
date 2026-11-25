using SomniumSpace.Bridge.Components;
using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

namespace CommunityModules
{
    public class GetVideoPlayerTextureUI : MonoBehaviour
    {
        [SerializeField] private GameObject _videoPlayerObject;
        [SerializeField] private RawImage _rawImage;

        private SomniumVideoPlayer _videoPlayerRef;
        private Texture _videoTexture = null;

        // Start is called before the first frame update
        void Start()
        {
            InvokeRepeating(nameof(GetTexture), 1.0f, 1.0f);
        }

        private void GetTexture()
        {
            if (_rawImage == null || 
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
            _rawImage.texture = _videoTexture;
        }

        private void OnValidate()
        {
            if(_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
        }
    }
}
