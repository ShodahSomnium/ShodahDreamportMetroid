using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CommunityModules
{
    [DefaultExecutionOrder(999999)]
    public class SPSTrigger : MonoBehaviour
    {
        public const string VERSION = "V2.1 [2026-07-18]";

        private Material[] _materials;
        private Transform _spsPivot;
        private Transform _spsEnd;
        private readonly HashSet<Transform> _targets = new();
        private bool _wasActive = false;
        Transform _activeTarget = null;

        private const string SPS_RECEIVER_NAME = "[SPS_Target]";
        private const string SHADER_PIVOT = "_Pivot";
        private const string SHADER_END = "_End";
        private const string SHADER_TARGET = "_Target";
        private const string SHADER_ENABLE = "_Enable";
        private const string SHADER_ENABLE_KEYWORD = "_ENABLE_ON";

        public void Setup(Material[] materials, Transform spsPivot, Transform spsEnd)
        {
            _materials = materials;
            _spsPivot = spsPivot;
            _spsEnd = spsEnd;

            // Setup materials
            UpdateMaterials(false, true);
        }

        private void LateUpdate()
        {
            if (_spsPivot == null || _spsEnd == null || _materials == null)
                return; // Silent error

            // Check null
            if (_targets.Count != 0)
                CleanNullTargets();
            else
                _activeTarget = null;

            // Sellect closest target
            if (_targets.Count != 0)
                _activeTarget = SelectClosestTarget();
            else
                _activeTarget = null;

            // Update materials
            UpdateMaterials(_activeTarget != null);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.name.StartsWith(SPS_RECEIVER_NAME))
            {
                _targets.Add(other.gameObject.transform);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.name.StartsWith(SPS_RECEIVER_NAME))
            {
                _targets.Remove(other.gameObject.transform);
            }
        }

        private Transform SelectClosestTarget()
        {
            float closestDistance = float.MaxValue;
            Transform closestTransform = null;
            foreach (Transform target in _targets)
            {
                if (target != null)
                {
                    float distance = Vector3.Distance(transform.position, target.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestTransform = target;
                    }
                }
            }

            return closestTransform;
        }

        private void CleanNullTargets()
        {
            _targets.RemoveWhere(x => x == null);
        }

        private void UpdateMaterials(bool isActive, bool force=false)
        {
            foreach (Material mat in _materials)
            {
                // Avoid doing every frame
                if (mat != null && (isActive != _wasActive || force))
                {
                    if (isActive)
                    {
                        mat.SetFloat(SHADER_ENABLE, 1.0f);
                        mat.EnableKeyword(SHADER_ENABLE_KEYWORD);
                    }
                    else
                    {
                        mat.SetFloat(SHADER_ENABLE, 0.0f);
                        mat.DisableKeyword(SHADER_ENABLE_KEYWORD);
                    }
                }

                // Update target, base and end if enabled
                if (mat != null && isActive)
                {
                    mat.SetVector(SHADER_TARGET, _activeTarget.position);
                    mat.SetVector(SHADER_PIVOT, _spsPivot.position);
                    mat.SetVector(SHADER_END, _spsEnd.position);
                }
            }

            _wasActive = isActive;
        }
    }

    #if UNITY_EDITOR
    [CustomEditor(typeof(SPSTrigger))]
    public class YourScriptEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.LabelField(SPSController.VERSION, EditorStyles.boldLabel);
        }
    }
    #endif
}