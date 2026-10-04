using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;

namespace RD.Core
{
    [RequireComponent(typeof(ARPlaneManager), typeof(ARRaycastManager))]
    public class GameStartHelper : MonoBehaviour
    {
        [Header("UI (single selection)")]
        [SerializeField] private RectTransform worldUI;
        [SerializeField] private Button gameStartButtons;
        [SerializeField] private bool autoManageGameUI = true;

        [Header("Plane filter (size only)")]
        [Tooltip("Minimum plane area in m^2. ARPlane.size.x * ARPlane.size.y")]
        [SerializeField] private float minPlaneArea = 0.25f;

        [Tooltip("Minimum short side in metres. Shortest edge of the plane's bounding box (min of size.x/size.y). Rejects long thin slivers.")]
        [SerializeField] private float minShortSide = 0.3f;

        [Header("World UI (indication)")]
        [Tooltip("World-space offset applied to the UI position above the plane hit point.")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.02f, 0f);
        [SerializeField] private float rotationSpeed;

        [Header("Proximity gate")]
        [Tooltip("Show world UI only when camera is closer than this (metres, slant 3D).")]
        [SerializeField] private float showDistance = 1.5f;
        
        [Tooltip("Hide when farther than this. Keep > showDistance for hysteresis (avoids flicker).")]
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
        public GameObject SpawnedGameBase => spawnedGameBase;
        public IReadOnlyCollection<ARPlane> SuitablePlanes => suitablePlanes.Values;

        private ARPlaneManager planeManager;
        private ARRaycastManager raycastManager;
        private Camera arCamera;

        private readonly Dictionary<TrackableId, ARPlane> suitablePlanes = new();
        private readonly List<ARRaycastHit> centerScreenHits = new();
        private Pose selectedPose;
        private bool gameStarted;
        private bool indicationVisible = false;
        private GameObject spawnedGameBase;


        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            raycastManager = GetComponent<ARRaycastManager>();
            arCamera = GetComponentInChildren<Camera>();
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
            if(gameStarted) return;

            if (SuitablePlaneCount > 0 && IsCameraLookingAtPlane(arCamera, 5f) && IsCameraCloseToPlane())
            {
                SpawnIndicationUI();
            }
            else
            {
                HideWorldUI();
            }
        }

        private bool IsCameraCloseToPlane()
        {
            Camera cam = arCamera;

            Vector3 anchor = selectedPose.position != Vector3.zero
                ? selectedPose.position
                : visiblePlane.transform.TransformPoint(visiblePlane.center);

            float dist = Vector3.Distance(cam.transform.position, anchor);

            // Hysteresis: use wider hide threshold while already showing.
            bool showing = worldUI.gameObject.activeSelf;
            float threshold = showing ? hideDistance : showDistance;
            return dist <= threshold;
        }
        private void HandlePlanesChanged(ARTrackablesChangedEventArgs<ARPlane> eventArgs)
        {
            if (gameStarted)
                return;

            foreach (var added in eventArgs.added)
            {
                if (IsLargeEnough(added))
                {
                    StoreSuitablePlane(added.trackableId, added);
                }
            }

            foreach (var updated in eventArgs.updated)
            {
                bool isLarge = IsLargeEnough(updated);

                if (suitablePlanes.ContainsKey(updated.trackableId))
                {
                    if (!isLarge)
                    {
                        suitablePlanes.Remove(updated.trackableId);
                        if (visiblePlane != null && visiblePlane.trackableId == updated.trackableId)
                        {
                            visiblePlane = null;
                            HideWorldUI();
                        }
                        Debug.Log($"Plane removed (too small): {updated.trackableId}");
                    }
                    else
                    {
                        ARPlane existingPlane = suitablePlanes[updated.trackableId];

                        bool sizeChanged = !existingPlane.size.Equals(updated.size);
                        bool poseChanged = !existingPlane.transform.position.Equals(updated.transform.position);

                        if (sizeChanged || poseChanged)
                        {
                            suitablePlanes[updated.trackableId] = updated;
                            Debug.Log($"Plane data updated: {updated.trackableId}");
                        }
                    }
                }
                else
                {
                    if (isLarge)
                    {
                        StoreSuitablePlane(updated.trackableId, updated);
                    }
                }
            }

            foreach (var removed in eventArgs.removed)
            {
                TrackableId removedId = removed.Key;
                suitablePlanes.Remove(removedId);
                if (visiblePlane != null && visiblePlane.trackableId == removedId)
                {
                    visiblePlane = null;
                    HideWorldUI();
                }
            }
        }
        public bool IsCameraLookingAtPlane(Camera arCamera, float maxDistance = 5f)
        {
            // AR raycast from screen center; takes the first large-enough plane within maxDistance.
            centerScreenHits.Clear();
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (raycastManager.Raycast(screenCenter, centerScreenHits, TrackableType.PlaneWithinPolygon))
            {
                foreach (var arHit in centerScreenHits)
                {
                    if (arHit.distance > maxDistance)
                        continue;

                    ARPlane hitPlane = planeManager.GetPlane(arHit.trackableId);
                    if (hitPlane != null && IsLargeEnough(hitPlane))
                    {
                        visiblePlane = hitPlane;
                        selectedPose = arHit.pose;
                        return true;
                    }
                }

            }

            return false;
        }
        private void SpawnIndicationUI()
        {
            Vector3 pos = selectedPose.position + worldOffset;

            worldUI.transform.position = pos;

            RotateWorldUI(worldUI);

            if (autoManageGameUI)
            {
                if (!worldUI.gameObject.activeSelf)
                    worldUI.gameObject.SetActive(true);
            }

            if (!indicationVisible)
            {
                indicationVisible = true;
                GameEvent.TriggerPlaneSelection(indicationVisible);
            }
        }
        private void RotateWorldUI(RectTransform info)
        {
            Camera cam = arCamera;
            Vector3 lookDirection = info.transform.position - cam.transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f)
                return;
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);

            float step = rotationSpeed * Time.deltaTime;

            // Smoothly interpolate current rotation toward target
            info.transform.rotation = Quaternion.Slerp(info.transform.rotation, targetRotation, step);
        }
        private void HideWorldUI()
        {
            if (!autoManageGameUI)
                return;

            if (worldUI.gameObject.activeSelf)
            {
                worldUI.gameObject.SetActive(false);
            }

            // Fire once on the shown -> hidden transition
            if (indicationVisible)
            {
                indicationVisible = false;
                GameEvent.TriggerPlaneSelection(indicationVisible);
            }
        }
        private void StoreSuitablePlane(TrackableId id, ARPlane plane)
        {
            if (!suitablePlanes.ContainsKey(id))
            {
                suitablePlanes.Add(id, plane);
            }
        }
        private bool IsLargeEnough(ARPlane plane)
        {
            if (plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null)
                return false;
            float area = GetPlaneArea(plane);
            float shortSide = Mathf.Min(plane.size.x, plane.size.y);
            return area >= minPlaneArea && shortSide >= minShortSide;
        }

        public static float GetPlaneArea(ARPlane plane)
        {
            return plane.size.x * plane.size.y;
        }
        private void StopARPlaneDetection()
        {
            if(planeManager.currentDetectionMode == PlaneDetectionMode.None) return;

            planeManager.requestedDetectionMode = PlaneDetectionMode.None;
        }

        public void ReturnToPlaneSelection()
        {
            // 1. Remove the spawned base and release the preview.
            if (placer != null) placer.Clear();
            if (preview != null) preview.ClearTargetPlane();

            // 2. Reset selection state.
            trackedPlane = null;
            visiblePlane = null;
            selectedPose = default;
            gameStarted = false;
            HideWorldUI();

            // 3. Rebuild the suitable-plane list from planes that already exist,
            //    because no "added" event fires for them again.
            suitablePlanes.Clear();
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (IsLargeEnough(plane))
                    StoreSuitablePlane(plane.trackableId, plane);
            }

            // 4. Resume plane detection.
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        }
        private void GivePlaneSelection()
        {
            if (visiblePlane == null || !IsLargeEnough(visiblePlane))
                return;

            trackedPlane = visiblePlane;
            gameStarted = true;
            HideWorldUI();
            StopARPlaneDetection();
            onPlaneSelected?.Invoke(arg0: trackedPlane);
        }

    }
}