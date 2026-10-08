using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RD.Core
{
    [RequireComponent(typeof(ARPlaneManager), typeof(ARRaycastManager))]
    public class GameStartHelper : MonoBehaviour
    {
        [Header("UI (single selection)")]
        [SerializeField] private RectTransform worldUI;
        [SerializeField] private Button gameStartButtons;
        [SerializeField] private bool autoManageGameUI = true;

        [Header("Plane filter")]
        [Tooltip("Minimum real plane area in m^2 (computed from the boundary polygon).")]
        [SerializeField] private float minPlaneArea = 0.25f;

        [Tooltip("Minimum short side in metres (bounding box). Rejects long thin slivers.")]
        [SerializeField] private float minShortSide = 0.3f;

        [Header("World UI (indication)")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.02f, 0f);
        [SerializeField] private float rotationSpeed = 8f;
        [Tooltip("Higher = follows the raycast hit faster, lower = smoother.")]
        [SerializeField] private float positionSmoothing = 12f;

        [Header("Proximity gate")]
        [SerializeField] private float showDistance = 1.5f;
        [Tooltip("Keep > showDistance for hysteresis (avoids flicker).")]
        [SerializeField] private float hideDistance = 1.8f;

        [Header("Events")]
        public UnityEvent<ARPlane> onPlaneSelected;

        [Header("Restart")]
        [SerializeField] private GridSnapPreview preview;
        [SerializeField] private PrefabSnapPlacer placer;

        public ARPlane trackedPlane;
        public ARPlane visiblePlane;

        public bool HasSelection => trackedPlane != null;
        public int SuitablePlaneCount => suitablePlanes.Count;
        public Pose SelectedPose => selectedPose;
        public bool IsGameStarted => gameStarted;
        public GameObject SpawnedGameBase => placer != null ? placer.SpawnedInstance : null;
        public IReadOnlyCollection<ARPlane> SuitablePlanes => suitablePlanes.Values;

        /// <summary>Anchor created at selection time. Parent the base to this. May be null if anchoring failed.</summary>
        public ARAnchor BaseAnchor => baseAnchor;

        private ARPlaneManager planeManager;
        private ARRaycastManager raycastManager;
        private ARAnchorManager anchorManager;
        private Camera arCamera;

        private readonly Dictionary<TrackableId, ARPlane> suitablePlanes = new();
        private readonly List<ARRaycastHit> centerScreenHits = new();
        private Pose selectedPose;
        private bool gameStarted;
        private bool indicationVisible;
        private ARAnchor baseAnchor;

        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            raycastManager = GetComponent<ARRaycastManager>();
            anchorManager = GetComponent<ARAnchorManager>();
            arCamera = GetComponentInChildren<Camera>();

            if (anchorManager == null)
                Debug.LogWarning("GameStartHelper: no ARAnchorManager on this object. Add one to the XR Origin so the base can be anchored.", this);
        }

        void OnEnable()
        {
            planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
            gameStartButtons.onClick.AddListener(GivePlaneSelection);
        }

        void OnDisable()
        {
            planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
            gameStartButtons.onClick.RemoveListener(GivePlaneSelection);
        }

        void OnValidate()
        {
            if (hideDistance < showDistance)
                hideDistance = showDistance + 0.3f;
        }

        void Update()
        {
            if (gameStarted) return;

            if (SuitablePlaneCount > 0 && IsCameraLookingAtPlane(5f) && IsCameraCloseToPlane())
                SpawnIndicationUI();
            else
                HideWorldUI();
        }

        // ---------- Proximity ----------

        private bool IsCameraCloseToPlane()
        {
            // selectedPose is refreshed by IsCameraLookingAtPlane right before this runs.
            float dist = Vector3.Distance(arCamera.transform.position, selectedPose.position);

            // Hysteresis: wider threshold while the indicator is already showing.
            float threshold = indicationVisible ? hideDistance : showDistance;
            return dist <= threshold;
        }

        // ---------- Plane tracking ----------

        private void HandlePlanesChanged(ARTrackablesChangedEventArgs<ARPlane> eventArgs)
        {
            if (gameStarted) return;

            foreach (var added in eventArgs.added)
                if (IsLargeEnough(added))
                    suitablePlanes[added.trackableId] = added;

            foreach (var updated in eventArgs.updated)
            {
                if (IsLargeEnough(updated))
                {
                    suitablePlanes[updated.trackableId] = updated;
                }
                else if (suitablePlanes.Remove(updated.trackableId))
                {
                    ClearVisibleIfMatches(updated.trackableId);
                }
            }

            foreach (var removed in eventArgs.removed)
            {
                suitablePlanes.Remove(removed.Key);
                ClearVisibleIfMatches(removed.Key);
            }
        }

        private void ClearVisibleIfMatches(TrackableId id)
        {
            if (visiblePlane != null && visiblePlane.trackableId == id)
            {
                visiblePlane = null;
                HideWorldUI();
            }
        }

        public bool IsCameraLookingAtPlane(float maxDistance = 5f)
        {
            centerScreenHits.Clear();
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(screenCenter, centerScreenHits, TrackableType.PlaneWithinPolygon))
                return false;

            foreach (var arHit in centerScreenHits)
            {
                if (arHit.distance > maxDistance) continue;

                ARPlane hitPlane = planeManager.GetPlane(arHit.trackableId);
                if (hitPlane != null && IsLargeEnough(hitPlane))
                {
                    visiblePlane = hitPlane;
                    selectedPose = arHit.pose;
                    return true;
                }
            }

            return false;
        }

        // ---------- Indicator UI ----------

        private void SpawnIndicationUI()
        {
            Vector3 target = selectedPose.position + worldOffset;

            // Snap on first show, smooth afterwards so it doesn't jitter as the plane refines.
            if (!indicationVisible)
                worldUI.position = target;
            else
                worldUI.position = Vector3.Lerp(worldUI.position, target, Mathf.Clamp01(positionSmoothing * Time.deltaTime));

            RotateWorldUI(worldUI);

            if (autoManageGameUI && !worldUI.gameObject.activeSelf)
                worldUI.gameObject.SetActive(true);

            if (!indicationVisible)
            {
                indicationVisible = true;
                GameEvent.TriggerPlaneSelection(true);
            }
        }

        private void RotateWorldUI(RectTransform info)
        {
            Vector3 lookDirection = info.position - arCamera.transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            float step = Mathf.Clamp01(rotationSpeed * Time.deltaTime);
            info.rotation = Quaternion.Slerp(info.rotation, targetRotation, step);
        }

        private void HideWorldUI()
        {
            if (!autoManageGameUI) return;

            if (worldUI.gameObject.activeSelf)
                worldUI.gameObject.SetActive(false);

            if (indicationVisible)
            {
                indicationVisible = false;
                GameEvent.TriggerPlaneSelection(false);
            }
        }

        // ---------- Plane filter ----------

        private bool IsLargeEnough(ARPlane plane)
        {
            if (plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null)
                return false;

            // Floors and tables only. Rejects ceilings.
            if (plane.alignment != PlaneAlignment.HorizontalUp)
                return false;

            // Cheap bounding-box checks first, polygon area last.
            float shortSide = Mathf.Min(plane.size.x, plane.size.y);
            if (shortSide < minShortSide) return false;
            if (plane.size.x * plane.size.y < minPlaneArea) return false;

            return GetPlaneArea(plane) >= minPlaneArea;
        }

        /// <summary>Real area from the boundary polygon (shoelace). size.x * size.y only gives the bounding box.</summary>
        public static float GetPlaneArea(ARPlane plane)
        {
            var b = plane.boundary;
            if (!b.IsCreated || b.Length < 3)
                return plane.size.x * plane.size.y;

            float area = 0f;
            for (int i = 0, j = b.Length - 1; i < b.Length; j = i++)
                area += (b[j].x * b[i].y) - (b[i].x * b[j].y);
            return Mathf.Abs(area) * 0.5f;
        }

        // ---------- Selection flow ----------

        private void StopARPlaneDetection()
        {
            if (planeManager.currentDetectionMode == PlaneDetectionMode.None) return;
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;
        }

        private async void GivePlaneSelection()
        {
            if (gameStarted) return; // blocks double taps while the anchor is being created
            if (visiblePlane == null || !IsLargeEnough(visiblePlane)) return;

            trackedPlane = visiblePlane;
            Pose pose = selectedPose; // capture before the await
            gameStarted = true;
            HideWorldUI();

            // Anchor the picked point so the base stays put after plane detection stops.
            if (anchorManager != null)
            {
                var result = await anchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess())
                    baseAnchor = result.value;
                else
                    Debug.LogWarning("GameStartHelper: anchor creation failed. Base will not be anchored.", this);
            }

            StopARPlaneDetection();
            onPlaneSelected?.Invoke(trackedPlane);
        }

        public void ReturnToPlaneSelection()
        {
            // 1. Remove the base first (it is parented to the anchor), then the anchor.
            if (placer != null) placer.Clear();
            if (preview != null) preview.ClearTargetPlane();

            if (baseAnchor != null)
            {
                if (anchorManager != null) anchorManager.TryRemoveAnchor(baseAnchor);
                else Destroy(baseAnchor.gameObject);
                baseAnchor = null;
            }

            // 2. Reset selection state.
            trackedPlane = null;
            visiblePlane = null;
            selectedPose = default;
            gameStarted = false;
            HideWorldUI();

            // 3. Rebuild the suitable-plane list from planes that already exist.
            suitablePlanes.Clear();
            foreach (ARPlane plane in planeManager.trackables)
                if (IsLargeEnough(plane))
                    suitablePlanes[plane.trackableId] = plane;

            // 4. Resume plane detection.
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        }
    }
}