using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CommunityModules
{
    public class SPSController : MonoBehaviour
    {
        public const string VERSION = "V2.1 [2026-07-18]";

        [Header(VERSION)]
        [Space]
        [SerializeField] SPSTrigger _SPSMeshTriggerPrefab;
        [SerializeField] Transform _SPSTargetPrefab;

        private const string SPS_TAG = "Player";

        private const string SPS_TARGET_UNSET_PREFIX = "SPS_Target";
        private const string SPS_TARGET_NAME = "_SPS_Target";

        private const string SPS_MESH_UNSET_PREFIX = "SPS_Mesh";
        private const string SPS_MESH_NAME = "_SPS_Mesh";

        private const string SPS_RENDERER = "SPS_Renderer";
        private const string SPS_BASE = "SPS_Base";
        private const string SPS_END = "SPS_End";
        private const string SPS_SHADER = "Community/SPS";

        public void Start()
        {
            if (_SPSMeshTriggerPrefab == null)
            {
                Debug.LogError($"[{nameof(SPSController)}] _SPSMeshTriggerPrefab is null");
                return;
            }
            if (_SPSTargetPrefab == null)
            {
                Debug.LogError($"[{nameof(SPSController)}] _SPSTargetPrefab is null");
                return;
            }

            InvokeRepeating(nameof(SlowUpdate), 1.0f, 1.0f);
        }

        public void SlowUpdate()
        {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(SPS_TAG);

            foreach (GameObject objectRef in objects)
            {
                if(objectRef == null)
                    continue;

                if (objectRef.name.StartsWith(SPS_MESH_UNSET_PREFIX))
                {
                    SPSMeshSetup(objectRef);
                }
                else if (objectRef.name.StartsWith(SPS_TARGET_UNSET_PREFIX))
                {
                    SPSTargetSetup(objectRef);
                }
            }
        }

        private void SPSMeshSetup(GameObject spsMesh)
        {
            List<Renderer> spsRenderers = new();
            Transform spsBase = null;
            Transform spsEnd = null;
            bool skinMeshFound = false;
            bool meshFound = false;

            Debug.Log($"[{nameof(SPSController)}] Setupping SPS_Mesh: {spsMesh.name}");
            spsMesh.name = SPS_MESH_NAME;

            // Make sure all elements are present and valid
            Transform[] childen = spsMesh.GetComponentsInChildren<Transform>();
            foreach (Transform child in childen)
            {
                if (child.name.StartsWith(SPS_RENDERER))
                {
                    if (child.TryGetComponent<Renderer>(out var renderer))
                    {
                        if (renderer is SkinnedMeshRenderer)
                        {
                            skinMeshFound = true;
                            spsRenderers.Add(renderer);
                        }
                        else if (renderer is MeshRenderer)
                        {
                            meshFound = true;
                            spsRenderers.Add(renderer);
                        }
                        else
                            Debug.LogError($"[{nameof(SPSController)}] A SPS_Mesh have no renderer");
                    }
                }
                else if (child.name.StartsWith(SPS_BASE))
                {
                    spsBase = child;
                }
                else if (child.name.StartsWith(SPS_END))
                {
                    spsEnd = child;
                }
            }

            // Get SPS materials
            HashSet<Material> spsMaterials = new();
            foreach ( Renderer renderer in spsRenderers)
            {
                if(renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                    continue;

                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat && mat.shader != null && mat.shader.name.StartsWith(SPS_SHADER))
                    {
                        spsMaterials.Add(mat);
                    }
                }
            }

            // Checks
            bool isBaseFound = spsBase != null;
            bool isEndFound = spsEnd != null;
            bool haveRenderers = spsRenderers.Count > 0;
            bool isNotMixed = ! (skinMeshFound && meshFound);
            bool haveMaterials = spsMaterials.Count > 0;
            bool handlesHaveSameParent = false;
            if (isBaseFound && isEndFound)
                handlesHaveSameParent = spsBase.parent == spsEnd.parent;

            if (!isBaseFound)
                Debug.LogError($"[{nameof(SPSController)}] SPS_Base object not found");
            if (!isEndFound)
                Debug.LogError($"[{nameof(SPSController)}] SPS_End object not found");
            if (!haveRenderers)
                Debug.LogError($"[{nameof(SPSController)}] No SPS_Renderer found");
            if (!isNotMixed)
                Debug.LogError($"[{nameof(SPSController)}] SPS_Mesh contain both Mesh and Skimesh, this is not allowed");
            if (!haveMaterials)
                Debug.LogError($"[{nameof(SPSController)}] No SPS materials found");
            if (isBaseFound && isEndFound && !handlesHaveSameParent)
                Debug.LogError($"[{nameof(SPSController)}] SPS_Base and SPS_End have different parents");

            if (isBaseFound && isEndFound && haveRenderers && isNotMixed && haveMaterials && handlesHaveSameParent)
            {
                Transform parent = spsBase.parent;
                float length = Vector3.Distance(spsBase.localPosition, spsEnd.localPosition);

                SPSTrigger spstrigger = Instantiate(_SPSMeshTriggerPrefab, spsBase.parent);
                spstrigger.transform.localPosition = spsBase.localPosition;
                spstrigger.transform.localScale = Vector3.one * length; // Scale == size in meters
                spstrigger.Setup(spsMaterials.ToArray(), spsBase, spsEnd);
            }
            else
            {
                Debug.LogError($"[{nameof(SPSController)}] Setup is invalid, SPS_Mesh wont be initialized");
            }
        }

        private void SPSTargetSetup(GameObject spsTarget)
        {
            spsTarget.name = SPS_TARGET_NAME; // Rename so it wont be found again
            Instantiate(_SPSTargetPrefab, spsTarget.transform);
        }
    }
}
