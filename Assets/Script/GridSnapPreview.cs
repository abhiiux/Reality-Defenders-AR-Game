using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RD.Core
{
    /// <summary>
    /// Grid-snapped AR placement preview. Raycasts from the screen center (crosshair)
    /// against the selected plane, snaps the hit to a plane-local grid, and drives a
    /// scene marker (preview only). A separate placer (see PrefabSnapPlacer) spawns the
    /// real prefab on Confirm.
    /// </summary>
    public class GridSnapPreview : MonoBehaviour
    {
        [Header("AR references (reuse scene managers, do not duplicate)")]
        [Tooltip("ARRaycastManager on XR Origin. Reused, never created here.")]
        [SerializeField] private ARRaycastManager raycastManager;
        [Tooltip("ARPlaneManager on XR Origin. Used for GetPlane + boundary checks.")]
        [SerializeField] private ARPlaneManager planeManager;

        [Header("Preview marker (scene object, not a prefab asset)")]
        [Tooltip("Scene Quad driven as the snapped preview. Hidden when pose is invalid or after placement.")]
        [SerializeField] private Transform marker;

        [Header("Grid")]
        [Tooltip("Cell size in meters. Must match the visible grid lines.")]
        [SerializeField] private float cellSize = 0.15f;
        [Tooltip("Marker footprint in cells. Odd centers on a cell, even on an intersection.")]
        [SerializeField] private Vector2Int footprint = new Vector2Int(1, 1);

        [Header("Marker feedback")]
        [Tooltip("Tint marker red when any footprint corner falls outside plane.boundary, green otherwise.")]
        [SerializeField] private bool tintRedWhenOutside = true;
        [Tooltip("Hide the marker permanently after a successful Confirm (single-spawn flow).")]
        [SerializeField] private bool hideMarkerAfterConfirm = true;
        [Tooltip("Lift above the plane in meters to avoid z-fighting.")]
        [SerializeField] private float lift = 0.002f;

        /// <summary>True when the marker shows a valid snapped pose.</summary>
        public bool HasValidPose { get; private set; }

        /// <summary>Latest snapped pose (upright rotation, for spawning).</summary>
        public Pose SnappedPose { get; private set; }

        /// <summary>Fired by Confirm() only when the pose is valid. Nothing is instantiated here.</summary>
        public event Action<Pose> OnConfirmed;

        /// <summary>Selected plane this preview is locked to. Set via SetTargetPlane (e.g. GameStartHelper.onPlaneSelected).</summary>
        public ARPlane TargetPlane => targetPlane;

        // Runtime-selected plane. Null until GivePlaneSelection fires onPlaneSelected.
        private ARPlane targetPlane;

        // Quad default faces +Z; flatten to face +Y (lie on horizontal plane).
        private static readonly Quaternion FlatOffset = Quaternion.Euler(90f, 0f, 0f);

        // Reused across all raycasts / corner checks: no per-frame allocations in Update.
        private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();
        private static readonly Vector2[] Corners = new Vector2[4];

        private static readonly Color ValidColor = new Color(0f, 1f, 0f, 0.4f);
        private static readonly Color OutsideColor = new Color(1f, 0f, 0f, 0.4f);

        private Renderer markerRenderer;
        private MaterialPropertyBlock markerBlock;
        private bool hasPlaced;

        private void Awake()
        {
            markerRenderer = marker.GetComponent<Renderer>();

            markerBlock = new MaterialPropertyBlock();
            ApplyMarkerScale();
            SetMarkerColor(ValidColor);
            marker.gameObject.SetActive(false);
            HasValidPose = false;
        }

        private void OnValidate()
        {
            if (cellSize <= 0f)
            {
                cellSize = 0.15f;
            }

            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);
            lift = Mathf.Max(0f, lift);
        }

        private void Update()
        {
            // After a successful spawn the marker stays gone (single-instance flow).
            if (hasPlaced)
            {
                return;
            }

            // Quad placement only runs after GivePlaneSelection supplies the ARPlane.
            if (targetPlane == null)
            {
                return;
            }

            if (targetPlane.subsumedBy != null || !targetPlane.gameObject.activeInHierarchy)
            {
                SetInvalid();
                return;
            }

            AimAtScreenPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        /// <summary>Invokes OnConfirmed only when the pose is valid. Wire to a UI button.</summary>
        public void Confirm()
        {
            if (!HasValidPose || hasPlaced || targetPlane == null)
            {
                return;
            }

            OnConfirmed?.Invoke(SnappedPose);

            if (hideMarkerAfterConfirm)
            {
                NotifyPlacementComplete();
            }
        }

        /// <summary>Called by the placer after spawning so the preview locks even if Confirm hide logic changes.</summary>
        public void NotifyPlacementComplete()
        {
            hasPlaced = true;
            HasValidPose = false;
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }
        }

        /// <summary>Clears the placed lock (e.g. placer destroyed its instance) so aiming can resume.</summary>
        public void ResetPlacement()
        {
            hasPlaced = false;
        }

        /// <summary>
        /// Locks the preview to the selected plane. Wire to GameStartHelper.onPlaneSelected
        /// so quad placement only runs after GivePlaneSelection. Null is ignored.
        /// </summary>
        public void SetTargetPlane(ARPlane plane)
        {
            if (plane == null)
            {
                return;
            }

            targetPlane = plane;
            hasPlaced = false;
            HasValidPose = false;

            GameEvent.TriggerPlanePlacement(true);
        }

        /// <summary>Releases the target plane and hides the marker.</summary>
        public void ClearTargetPlane()
        {
            targetPlane = null;
            SetInvalid();
        }

        private void AimAtScreenPoint(Vector2 screenPoint)
        {
            RaycastHits.Clear();
            bool hit = raycastManager.Raycast(screenPoint, RaycastHits, TrackableType.PlaneWithinPolygon);
            if (!hit || RaycastHits.Count == 0)
            {
                SetInvalid();
                return;
            }

            ARRaycastHit rayHit = RaycastHits[0];

            // Only the selected plane drives quad placement.
            if (rayHit.trackableId != targetPlane.trackableId)
            {
                SetInvalid();
                return;
            }

            ARPlane plane = planeManager.GetPlane(rayHit.trackableId);
            if (plane == null)
            {
                SetInvalid();
                return;
            }

            Pose snapped = SnapPlaneLocal(rayHit, plane, out bool inside);

            SnappedPose = snapped;
            HasValidPose = true;

            marker.position = snapped.position;
            // Marker visual lies flat; SnappedPose itself stays upright for spawning.
            marker.rotation = snapped.rotation * FlatOffset;
            marker.gameObject.SetActive(true);

            SetMarkerColor(tintRedWhenOutside && !inside ? OutsideColor : ValidColor);
        }

        // Plane-local grid: snap in plane space so tilted/vertical planes work,
        // then convert back. Rotation follows the plane (upright, not flattened).
        private Pose SnapPlaneLocal(ARRaycastHit rayHit, ARPlane plane, out bool inside)
        {
            Vector3 local = plane.transform.InverseTransformPoint(rayHit.pose.position);
            float offsetX = (footprint.x % 2 == 1) ? cellSize * 0.5f : 0f;
            float offsetZ = (footprint.y % 2 == 1) ? cellSize * 0.5f : 0f;
            float snappedLocalX = Mathf.Round((local.x - offsetX) / cellSize) * cellSize + offsetX;
            float snappedLocalZ = Mathf.Round((local.z - offsetZ) / cellSize) * cellSize + offsetZ;
            Vector3 snappedLocal = new Vector3(snappedLocalX, local.y + lift, snappedLocalZ);
            Vector3 snappedPos = plane.transform.TransformPoint(snappedLocal);
            Pose snapped = new Pose(snappedPos, plane.transform.rotation);

            inside = IsFootprintInsidePlane(plane, snappedPos);
            return snapped;
        }

        // Tests every footprint corner in plane local space. Corners outside boundary => red tint.
        private bool IsFootprintInsidePlane(ARPlane plane, Vector3 worldPos)
        {
            if (plane.boundary.IsCreated && plane.boundary.Length == 0)
            {
                return false;
            }

            float halfX = footprint.x * cellSize * 0.5f;
            float halfZ = footprint.y * cellSize * 0.5f;

            Corners[0] = PlaneLocalXZ(plane, worldPos + new Vector3(-halfX, 0f, -halfZ));
            Corners[1] = PlaneLocalXZ(plane, worldPos + new Vector3(halfX, 0f, -halfZ));
            Corners[2] = PlaneLocalXZ(plane, worldPos + new Vector3(halfX, 0f, halfZ));
            Corners[3] = PlaneLocalXZ(plane, worldPos + new Vector3(-halfX, 0f, halfZ));

            for (int i = 0; i < Corners.Length; i++)
            {
                if (!IsPointInPolygon(Corners[i], plane.boundary))
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2 PlaneLocalXZ(ARPlane plane, Vector3 worldPos)
        {
            Vector3 local = plane.transform.InverseTransformPoint(worldPos);
            return new Vector2(local.x, local.z);
        }

        // Standard ray-casting point-in-polygon over the plane boundary loop.
        private static bool IsPointInPolygon(Vector2 point, NativeArray<Vector2> polygon)
        {
            bool inside = false;
            int count = polygon.Length;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool intersects = ((a.y > point.y) != (b.y > point.y)) &&
                    (point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private void SetInvalid()
        {
            HasValidPose = false;
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }
        }

        private void ApplyMarkerScale()
        {
            // Quad is 1x1m in XY; after the flat rotation local Y maps to world Z.
            marker.localScale = new Vector3(footprint.x * cellSize, footprint.y * cellSize, 1f);
        }

        private void SetMarkerColor(Color color)
        {
            if (markerRenderer == null || markerBlock == null)
            {
                return;
            }

            markerRenderer.GetPropertyBlock(markerBlock);
            markerBlock.SetColor("_BaseColor", color);
            markerBlock.SetColor("_Color", color);
            markerRenderer.SetPropertyBlock(markerBlock);
        }
    }
}