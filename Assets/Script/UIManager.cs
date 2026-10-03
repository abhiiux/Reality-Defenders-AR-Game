using UnityEngine;
using UnityEngine.UI;

namespace RD.Core
{
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private RectTransform selectPlanePanel;
        [SerializeField] private RectTransform placePlanePanel;
        void OnEnable()
        {
            GameEvent.OnPlaneSelectionUI += ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI += ShowPlacePanel;
        }
        void OnDisable()
        {
            GameEvent.OnPlaneSelectionUI -= ShowSelectPlaneButton;
            GameEvent.OnPlanePlacementUI -= ShowPlacePanel;
        }

        private void ShowPlacePanel(bool state)
        {
            placePlanePanel.gameObject.SetActive(state);
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
