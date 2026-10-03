using UnityEngine;

namespace RD.Core
{
    /// <summary>
    /// Logs snapped poses reported by GridSnapPreview. Spawns nothing.
    /// </summary>
    public class ConfirmLogger : MonoBehaviour
    {
        [Tooltip("Preview to listen to. Logs every confirmed snapped pose.")]
        [SerializeField] private GridSnapPreview preview;

        private void Awake()
        {
            if (preview == null)
            {
                Debug.LogError("ConfirmLogger: preview is not assigned. Assign the GridSnapPreview component.", this);
                enabled = false;
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
            Debug.Log($"GridSnap confirmed at position={pose.position:F3} rotation={pose.rotation.eulerAngles:F1}", this);
        }
    }
}
