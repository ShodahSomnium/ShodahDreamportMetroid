using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CommunityModules
{
    /// <summary>
    /// Use this component to set some unity settings for your world
    /// </summary>
    public class UnitySettings : MonoBehaviour
    {
        [Range(1, 8)]
        [SerializeField] int _maxLightCount = 3;
        [Range(0.05f, 1.0f)]
        [SerializeField] float _lodCrossFadeDuration = 0.1f;

        private int _lightCountBefore = 1;
        private float _locCrossFadeDurationBefore = 0.1f;

        private void Awake()
        {
            Apply();
        }

        [ContextMenu("- Editor Aplly")]
        private void Apply()
        {
            UniversalRenderPipelineAsset urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset != null)
            {
                _lightCountBefore = urpAsset.maxAdditionalLightsCount;
                urpAsset.maxAdditionalLightsCount = _maxLightCount;
            }

            Debug.Log(" AA " + LODGroup.crossFadeAnimationDuration);

            // Cross fade
            if (_lodCrossFadeDuration > 0.0f)
            {
                _locCrossFadeDurationBefore = LODGroup.crossFadeAnimationDuration;
                LODGroup.crossFadeAnimationDuration = _lodCrossFadeDuration;
            }
        }

        private void OnDestroy()
        {
            UniversalRenderPipelineAsset urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset != null)
            {
                urpAsset.maxAdditionalLightsCount = _lightCountBefore;
            }

            if (_locCrossFadeDurationBefore > 0.0f)
            {
                LODGroup.crossFadeAnimationDuration = _locCrossFadeDurationBefore;
            }
        }
    }
}
