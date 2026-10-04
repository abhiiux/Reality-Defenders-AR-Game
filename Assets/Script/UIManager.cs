using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RD.Core
{
    public class UIManager : MonoBehaviour
    {
        [Header("Screen Panels")]
        [SerializeField] private RectTransform selectPlanePanel;
        [SerializeField] private RectTransform placePlanePanel;
        [SerializeField] private RectTransform cannonUIPanel;

        [Header("World UI")]
        [SerializeField] private RectTransform worldUIPanel;
        [SerializeField] private TMP_Text worldUIText;
        [SerializeField] private Vector3 worldUIOffset;
        [SerializeField] private float worldUIFontSizeOnEnd;
        [SerializeField] private float defaultworldUIFontSize;

        [Header("Score")]
        [SerializeField] private TMP_Text scoreNumber;

        [Header("Settings")]
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float popUpDuration = 1f;

        [Header("Quit")]
        [SerializeField] private Button quitButton;

        private const string WorldScorePopUp = "+10";
        private const string GameEndPopup = "Wave Cleared!";
        private const string ThankPopup = "Thank You For Playing! :]";
        private const int MaxScore = 30;

        private int scoreInt;
        private Camera cam;
        private Coroutine popUpRoutine;

        void Awake() => cam = Camera.main;

        void OnEnable()
        {
            GameEvent.OnPlaneSelectionUI += ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI += ShowPlacePanel;
            GameEvent.OnBasePlacement    += ShowCannonPanel;
            GameEvent.OnScoreAdd         += HandleUpdateScore;

            quitButton.onClick.AddListener( QuitGame );
        }

        void OnDisable()
        {
            GameEvent.OnPlaneSelectionUI -= ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI -= ShowPlacePanel;
            GameEvent.OnBasePlacement    -= ShowCannonPanel;
            GameEvent.OnScoreAdd         -= HandleUpdateScore;

            quitButton.onClick.RemoveListener( QuitGame );
            popUpRoutine = null; // coroutines stop automatically on disable
        }

        void LateUpdate()
        {
            if (worldUIPanel.gameObject.activeInHierarchy)
                RotateWorldUI(worldUIPanel);
        }

        public void ShowPlacePanel(bool state) => placePlanePanel.gameObject.SetActive(state);
        public void ShowSelectPlaneButton(bool state) => selectPlanePanel.gameObject.SetActive(state);
        public void TurnOFFPlacementUI() => ShowPlacePanel(false);
        public void HideCanonPanel() => cannonUIPanel.gameObject.SetActive(true);

        private void ShowCannonPanel(Vector3 pos)
        {
            if(!cannonUIPanel.gameObject.activeSelf)
                cannonUIPanel.gameObject.SetActive(true);
                
            HandleWorldUIPlacement(pos);

            scoreInt = 0;
            scoreNumber.text = scoreInt.ToString();

            worldUIText.fontSize = defaultworldUIFontSize;
        }

        private void HandleWorldUIPlacement(Vector3 pos)
        {
            worldUIPanel.position = pos + worldUIOffset;
            worldUIPanel.gameObject.SetActive(false);
        }

        private void HandleUpdateScore()
        {
            scoreInt += 10;
            scoreNumber.text = scoreInt.ToString();

            if (popUpRoutine != null)
                StopCoroutine(popUpRoutine);
            popUpRoutine = StartCoroutine(UpdateWorldUI());
        }

        private IEnumerator UpdateWorldUI()
        {
            if( scoreInt >= MaxScore)
            {
                worldUIText.text = GameEndPopup;
                worldUIText.fontSize = worldUIFontSizeOnEnd;
            }
            else
            {
                worldUIText.text = WorldScorePopUp;                
            }
            worldUIPanel.gameObject.SetActive(true);

            yield return new WaitForSeconds(popUpDuration);

            if( scoreInt >= MaxScore )
            {
                worldUIText.text = ThankPopup;
                worldUIText.fontSize = worldUIFontSizeOnEnd;

                yield return new WaitForSeconds(popUpDuration);
            }
            worldUIPanel.gameObject.SetActive(false);
            popUpRoutine = null;
        }

        private void RotateWorldUI(RectTransform info)
        {
            if (cam == null) return;

            Vector3 lookDirection = info.position - cam.transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(lookDirection);
            float t = Mathf.Clamp01(rotationSpeed * Time.deltaTime);
            info.rotation = Quaternion.Slerp(info.rotation, target, t);
        }


        //------Quit
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}