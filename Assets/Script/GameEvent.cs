using System;
using UnityEngine;

public static class GameEvent
{
    // Plane Managment
    public static event Action<bool> OnPlaneSelectionUI;
    public static void TriggerPlaneSelection(bool state) => OnPlaneSelectionUI?.Invoke(state);
    public static event Action<bool> OnPlanePlacementUI;
    public static void TriggerPlanePlacement(bool state) => OnPlanePlacementUI?.Invoke(state);

    //Score System
    public static event Action OnScoreAdd;
    public static void TriggerAddScore() => OnScoreAdd?.Invoke();

    // On Game Base Placed
    public static event Action<Vector3> OnBasePlacement;
    public static void TriggerBasePlacement(Vector3 pos) => OnBasePlacement?.Invoke(pos);
}