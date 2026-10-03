using UnityEngine;
using UnityEngine.UI;

namespace RD.Core
{
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private RectTransform selectPlanePanel;
        [SerializeField] private RectTransform placePlanePanel;
        [SerializeField] private RectTransform canonUIPanel;
        void OnEnable()
        {
            GameEvent.OnPlaneSelectionUI += ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI += ShowPlacePanel;
            GameEvent.OnCanonInit        += ShowCanonPanel;
        }
        void OnDisable()
        {
            GameEvent.OnPlaneSelectionUI -= ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI -= ShowPlacePanel;
            GameEvent.OnCanonInit        -= ShowCanonPanel;
        }

        private void ShowPlacePanel(bool state)
        {
            placePlanePanel.gameObject.SetActive(state);
        }
        private void ShowCanonPanel()
        {
            if(!canonUIPanel.gameObject.activeSelf)
            {
                canonUIPanel.gameObject.SetActive(true);
            }
        }

        private void ShowSelectPlaneButton(bool state)
        {
            selectPlanePanel.gameObject.SetActive(state);
        }

        public void TurnOFFPlacementUI()
        {
            placePlanePanel.gameObject.SetActive(false);
        }
    }
}
