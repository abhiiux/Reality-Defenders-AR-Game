using UnityEngine;

namespace RD.Core
{
    /// <summary>
    /// Spawns a single prefab instance at the snapped pose reported by GridSnapPreview.
    /// After spawning, the marker is hidden permanently (single-instance flow).
    /// Existing BaseBuilder flow is left untouched.
    /// </summary>
    public class PrefabSnapPlacer : MonoBehaviour
    {
        [Header("Sources (scene references, no instantiation here except the prefab)")]
        [Tooltip("Preview that reports SnappedPose via OnConfirmed.")]
        [SerializeField] private GridSnapPreview preview;
        [Tooltip("Prefab to place at runtime. Set to GameBase.prefab.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("Optional parent for the spawned instance. Null spawns at scene root.")]
        [SerializeField] private Transform parent;

        /// <summary>Current spawned instance, or null if nothing spawned yet.</summary>
        public GameObject SpawnedInstance { get; private set; }

        private void Awake()
        {
            if (preview == null)
            {
                Debug.LogError("PrefabSnapPlacer: preview is not assigned. Assign the GridSnapPreview component.", this);
                enabled = false;
                return;
            }

            if (prefab == null)
            {
                Debug.LogError("PrefabSnapPlacer: prefab is not assigned. Assign GameBase.prefab.", this);
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (preview != null)
            {
                preview.OnConfirmed += HandleConfirmed;
            }
        }

        private void OnDisable()
        {
            if (preview != null)
            {
                preview.OnConfirmed -= HandleConfirmed;
            }
        }

        private void HandleConfirmed(Pose pose)
        {
            if (SpawnedInstance == null)
            {
                SpawnedInstance = Instantiate(prefab, pose.position, pose.rotation, parent);
            }
            else
            {
                SpawnedInstance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            // Lock the preview so the marker stays gone after the spawn.
            preview.NotifyPlacementComplete();
            GameEvent.TriggerCanonInit();
        }

        /// <summary>Destroys the spawned instance and lets the preview aim again.</summary>
        public void Clear()
        {
            if (SpawnedInstance != null)
            {
                Destroy(SpawnedInstance);
                SpawnedInstance = null;
            }

            if (preview != null)
            {
                preview.ResetPlacement();
                preview.ResumeAiming();
            }
        }
    }
}
