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
    /// <summary>
    /// Tracks ALL horizontal planes, but keeps a SINGLE selected plane for playing.
    /// - ARPlaneManager is already set to Horizontal only, so no alignment check.
    /// - Size filter uses ARPlane.size (full dimensions, metres).
    /// - Tap-to-place via ARRaycastManager selects which plane to play on.
    /// - Single screen-space Start UI (gameUI) + single GameBase spawn.
    /// - Per-plane visuals come from ARPlaneManager.planePrefab.
    ///   Plane detection stays on after Start.
    /// </summary>
    public class GameStartHelper : MonoBehaviour
    {
        [Header("GameBase")]
        [SerializeField] private GameObject gameBasePrefab;
        [SerializeField] private float baseHeightOffset;

        [Header("UI (single selection)")]
        [Tooltip("Existing screen-space object (e.g. Build Base button). Toggled, not duplicated.")]
        [SerializeField] private RectTransform gameUI;
        [SerializeField] private bool autoManageGameUI = true;

        [Header("Plane filter (size only)")]
        [Tooltip("Minimum plane area in m^2. ARPlane.size.x * ARPlane.size.y")]
        [SerializeField] private float minPlaneArea = 0.25f;
        [Tooltip("Minimum short side in metres. Shortest edge of the plane's bounding box (min of size.x/size.y). Rejects long thin slivers.")]
        [SerializeField] private float minShortSide = 0.3f;

        [Header("Events")]
        public UnityEvent onGameStart;
        public UnityEvent<ARPlane> onPlaneSelected;

        // Single selection for playing (kept public for backwards compat + debugging).
        public ARPlane trackedPlane;
        public ARPlane visiblePlane;

        public bool HasSelection => trackedPlane != null;
        public int SuitablePlaneCount => suitablePlanes.Count;
        public Pose SelectedPose => selectedPose;
        public bool IsGameStarted => gameStarted;
        public GameObject SpawnedGameBase => spawnedGameBase;
        public IReadOnlyCollection<ARPlane> SuitablePlanes => suitablePlanes.Values;

        [Header("World UI (indication)")]
        [Tooltip("World-space canvas hosting gameUI. Auto-created at runtime if null (scene file untouched).")]
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.02f, 0f);
        [SerializeField] private bool billboardYOnly = true;
        [SerializeField] private float rotationSpeed;

        [Header("Proximity gate")]
        [Tooltip("Show world UI only when camera is closer than this (metres, slant 3D).")]
        [SerializeField] private float showDistance = 1.5f;
        [Tooltip("Hide when farther than this. Keep > showDistance for hysteresis (avoids flicker).")]
        [SerializeField] private float hideDistance = 1.8f;

        private ARPlaneManager planeManager;
        private ARRaycastManager raycastManager;
        private Camera arCamera;

        private readonly Dictionary<TrackableId, ARPlane> suitablePlanes = new();
        private readonly List<ARRaycastHit> centerScreenHits = new();
        private Pose selectedPose;
        private bool gameStarted;
        private bool uiAdded = false;
        private GameObject spawnedGameBase;


        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            raycastManager = GetComponent<ARRaycastManager>();
            arCamera = GetComponentInChildren<Camera>();
        }

        void OnEnable()
        {
            if (planeManager != null)
                planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
        }

        void OnDisable()
        {
            if (planeManager != null)
                planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
        }

        void OnValidate()
        {
            if (hideDistance < showDistance)
                hideDistance = showDistance + 0.3f;
            if (rotationSpeed < 0f)
                rotationSpeed = 0f;
        }

        void Update()
        {
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
            if (visiblePlane == null)
                return false;

            Camera cam = arCamera;
            if (cam == null)
                return false;

            Vector3 anchor = selectedPose.position != Vector3.zero
                ? selectedPose.position
                : visiblePlane.transform.TransformPoint(visiblePlane.center);

            float dist = Vector3.Distance(cam.transform.position, anchor);

            // Hysteresis: use wider hide threshold while already showing.
            bool showing = worldCanvas != null && worldCanvas.gameObject.activeSelf;
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
                        Debug.Log($"Plane removed (too small): {updated.trackableId}");
                    }
                    else
                    {
                        ARPlane existingPlane = suitablePlanes[updated.trackableId];

                        // Use built-in Equals or property checks to verify if the plane has genuinely changed
                        // ARPlane inherits from IEquatable or provides direct state/dimension comparisons
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
        }
        public bool IsCameraLookingAtPlane(Camera arCamera, float maxDistance = 5f)
        {
            // Primary: AR raycast from screen center. Does not require Camera ref,
            // so a null arCamera (e.g. GetComponent<Camera>() on XR Origin) no longer blocks this.
            if (raycastManager != null)
            {
                centerScreenHits.Clear();
                Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (raycastManager.Raycast(screenCenter, centerScreenHits, TrackableType.PlaneWithinPolygon))
                {
                    foreach (var arHit in centerScreenHits)
                    {
                        if (arHit.distance > maxDistance)
                            continue;

                        ARPlane hitPlane = planeManager != null ? planeManager.GetPlane(arHit.trackableId) : null;
                        if (hitPlane != null)
                        {
                            visiblePlane = hitPlane;
                            selectedPose = arHit.pose;
                            return true;
                        }
                    }

                }

            }

            return false;
        }
        private void SpawnIndicationUI()
        {
            if (gameStarted || visiblePlane == null || worldCanvas == null)
                return;

            // Position: cached AR-hit pose.position (Fix 3). Fallback to plane center.
            Vector3 pos = selectedPose.position != Vector3.zero
                ? selectedPose.position
                : visiblePlane.transform.TransformPoint(visiblePlane.center);

            worldCanvas.transform.position = pos;

            RotateWorldUI(gameUI);

            if (autoManageGameUI)
            {
                if (!worldCanvas.gameObject.activeSelf)
                    worldCanvas.gameObject.SetActive(true);
            }

            uiAdded = true;
            Debug.Log($" plane Visible at {pos}");
        }
        private void RotateWorldUI(RectTransform info)
        {
            if (worldCanvas == null)
                return;
            Camera cam = arCamera != null ? arCamera : Camera.main;
            if (cam == null)
                return;
            Vector3 lookDirection = info.transform.position - cam.transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f)
                return;
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);

            // If deltaTime is provided as 0, use Time.deltaTime
            float step = rotationSpeed * Time.deltaTime;

            // Smoothly interpolate current rotation toward target
            info.transform.rotation = Quaternion.Slerp(info.transform.rotation, targetRotation, step);     
        }
        private void HideWorldUI()
        {
            if (!autoManageGameUI)
                return;
            if (worldCanvas == null)
                return;
            // Hide while not looking at a plane; keep instance for reuse (no per-frame alloc).
            // Called from Update's else branch, so hide unconditionally here.
            if (worldCanvas.gameObject.activeSelf)
            {
                worldCanvas.gameObject.SetActive(false);
            }
        }
        private void StoreSuitablePlane(TrackableId id, ARPlane plane)
        {
            if (!suitablePlanes.ContainsKey(id))
            {
                suitablePlanes.Add(id, plane);
                Debug.Log($"Plane added: {id}");
            }
        }
        private bool IsLargeEnough(ARPlane plane)
        {
            if (plane == null || plane.subsumedBy != null)
                return false;
            float area = GetPlaneArea(plane);
            float shortSide = Mathf.Min(plane.size.x, plane.size.y);
            return area >= minPlaneArea && shortSide >= minShortSide;
        }

        public static float GetPlaneArea(ARPlane plane)
        {
            return plane.size.x * plane.size.y;
        }

    }
}

