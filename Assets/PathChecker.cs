using System.Collections.Generic;
using UnityEngine;

public class PathChecker : MonoBehaviour
{
    // Call this from a UI button, a timer, or after every path placement.
    // Returns true if there is a connected path of Path cells from Start to Finish.
    public bool IsPathComplete()
    {
        Vector2Int? startCell  = FindCellOfType(CellType.Start);
        Vector2Int? finishCell = FindCellOfType(CellType.Finish);

        if (startCell == null || finishCell == null)
        {
            Debug.LogWarning("PathChecker: Start or Finish cell not found in grid.");
            return false;
        }

        var visited = new HashSet<Vector2Int>();
        var queue   = new Queue<Vector2Int>();

        queue.Enqueue(startCell.Value);
        visited.Add(startCell.Value);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (current == finishCell.Value)
                return true;

            foreach (var dir in dirs)
            {
                Vector2Int neighbor = current + dir;
                if (visited.Contains(neighbor)) continue;
                if (!GridManager.Instance.TryGetCell(neighbor, out var data)) continue;
                // Only traverse Path cells and the Finish cell
                if (data.type != CellType.Path && data.type != CellType.Finish) continue;

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    Vector2Int? FindCellOfType(CellType type)
    {
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == type)
                return kvp.Key;
        }
        return null;
    }
}
