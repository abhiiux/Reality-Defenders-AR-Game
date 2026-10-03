using System;

public static class GameEvent
{
    // If no parameters are needed:
    public static event Action<bool> OnPlaneSelectionUI;
    public static void TriggerPlaneSelection(bool state) => OnPlaneSelectionUI?.Invoke(state);
    public static event Action<bool> OnPlanePlacementUI;
    public static void TriggerPlanePlacement(bool state) => OnPlanePlacementUI?.Invoke(state);

    // Canon Awake Event 
    public static event Action OnCanonInit;
    public static void TriggerCanonInit() => OnCanonInit?.Invoke();

}