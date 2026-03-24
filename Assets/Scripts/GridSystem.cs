using UnityEngine;

public class GridSystem : MonoBehaviour
{
    public enum ToolMode { Straight, Corner }

    [Header("Placement")]
    public float gridSize = 1f;

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;

    public void RegisterSpecialCells()
    {
        if (GridManager.Instance == null)
        {
            Debug.LogWarning("[GridSystem] GridManager not found - cannot register special cells.");
            return;
        }

        if (PathChecker.Instance == null)
        {
            Debug.LogWarning("[GridSystem] PathChecker not found - cannot register special cells.");
            return;
        }

        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type != CellType.Start && kvp.Value.type != CellType.Finish)
                continue;

            PathChecker.Instance.RegisterNode(kvp.Key, PathNode.Omnidirectional(), kvp.Value.placedObject);
            Debug.Log($"[GridSystem] Registered {kvp.Value.type} cell at {kvp.Key} as omnidirectional node");
        }
    }
}
