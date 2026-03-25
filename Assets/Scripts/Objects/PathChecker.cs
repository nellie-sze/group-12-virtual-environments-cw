using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class PathDirections
{
    public static readonly Vector2Int North = new Vector2Int(0, 1);
    public static readonly Vector2Int South = new Vector2Int(0, -1);
    public static readonly Vector2Int East = new Vector2Int(1, 0);
    public static readonly Vector2Int West = new Vector2Int(-1, 0);

    public static readonly Vector2Int[] All = { North, South, East, West };

    public static Vector2Int Opposite(Vector2Int dir) => new Vector2Int(-dir.x, -dir.y);
}

public class PathNode
{
    public HashSet<Vector2Int> openSides = new HashSet<Vector2Int>();

    public static PathNode Straight(int rotationY)
    {
        var node = new PathNode();
        int r = ((rotationY % 360) + 360) % 360;

        if (r == 0 || r == 180) { node.openSides.Add(PathDirections.East); node.openSides.Add(PathDirections.West); }
        else { node.openSides.Add(PathDirections.North); node.openSides.Add(PathDirections.South); }

        return node;
    }

    public static PathNode Corner(int rotationY)
    {
        var node = new PathNode();
        int r = ((rotationY % 360) + 360) % 360;

        switch (r)
        {
            case 0: node.openSides.Add(PathDirections.West); node.openSides.Add(PathDirections.South); break;
            case 90: node.openSides.Add(PathDirections.West); node.openSides.Add(PathDirections.North); break;
            case 180: node.openSides.Add(PathDirections.East); node.openSides.Add(PathDirections.North); break;
            case 270: node.openSides.Add(PathDirections.East); node.openSides.Add(PathDirections.South); break;
        }

        return node;
    }

    public static PathNode Omnidirectional()
    {
        var node = new PathNode();
        foreach (var dir in PathDirections.All) node.openSides.Add(dir);
        return node;
    }

    public bool HasExit(Vector2Int direction) => openSides.Contains(direction);
}

public class PathChecker : MonoBehaviour
{
    public static PathChecker Instance { get; private set; }

    [Header("Placement Feedback Colours")]
    [Tooltip("Applied to a block when it is placed with a valid connection.")]
    public Color validPlacementColor = new Color(0.27f, 0.53f, 1.00f, 1f); // blue

    [Tooltip("Flashed on the ghost when a placement is rejected.")]
    public Color invalidPlacementColor = new Color(1.00f, 0.85f, 0.00f, 1f); // yellow

    [Tooltip("Applied to every block on the winning path.")]
    public Color pathCompleteColor = new Color(0.00f, 1.00f, 0.50f, 1f); // green

    [Tooltip("Seconds the yellow flash stays on the ghost. 0 = don't flash.")]
    public float invalidFlashDuration = 1.5f;

    // cell -> PathNode (open-side data)
    private readonly Dictionary<Vector2Int, PathNode> pathNodes = new Dictionary<Vector2Int, PathNode>();
    // cell -> placed GameObject (for recolouring)
    private readonly Dictionary<Vector2Int, GameObject> cellObjects = new Dictionary<Vector2Int, GameObject>();
    // cell -> cached original material colours (so we can restore after a flash)
    private readonly Dictionary<Vector2Int, List<Color>> originalColors = new Dictionary<Vector2Int, List<Color>>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterNode(Vector2Int cell, PathNode node, GameObject placedObject = null)
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameManager.GameState.Waiting &&
            GameManager.Instance.CurrentState != GameManager.GameState.Playing)
        {
            Debug.Log("[PathChecker] Registration ignored — game is no longer in progress.");
            return;
        }

        pathNodes[cell] = node;

        if (placedObject != null)
        {
            cellObjects[cell] = placedObject;
            originalColors[cell] = CacheColors(placedObject);
        }

        Vector3 worldPos = GridManager.Instance.GridToWorld(cell);
        Debug.Log($"[PathChecker] VALID block placed.\n" + $"Grid: {cell}\n" + $"World: {worldPos}\n" + $"Open sides: {string.Join(", ", node.openSides)}");
    }

    public void ReportInvalidPlacement(Vector2Int cell, GameObject ghostObject = null)
    {
        Vector3 worldPos = GridManager.Instance.GridToWorld(cell);
        Debug.LogWarning($"[PathChecker] INVALID placement at grid {cell} world {worldPos}\n" + $"Reason: block has no valid directional connection to an adjacent " + $"Start or Path cell in the current rotation.");
    }

    public void UnregisterNode(Vector2Int cell)
    {
        pathNodes.Remove(cell);
        cellObjects.Remove(cell);
        originalColors.Remove(cell);
        Debug.Log($"[PathChecker] Node unregistered at {cell}");
    }

    public void ClearAllNodes()
    {
        pathNodes.Clear();
        cellObjects.Clear();
        originalColors.Clear();
        Debug.Log("[PathChecker] Cleared all registered path nodes.");
    }

    public bool HasExitToward(Vector2Int cell, Vector2Int direction) =>
        pathNodes.TryGetValue(cell, out var node) && node.HasExit(direction);

    // Returns true if the cell has a registered PathNode (is part of the path network).
    public bool HasNode(Vector2Int cell) => pathNodes.ContainsKey(cell);

    // Chain-traverses the connected path from Start to the furthest reachable cell.
    // Returns an ordered list of GridManager cell coordinates.
    public List<Vector2Int> GetOrderedPath()
    {
        if (GridManager.Instance == null) return new List<Vector2Int>();

        Vector2Int startCell = default;
        bool foundStart = false;
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Start) { startCell = kvp.Key; foundStart = true; break; }
        }
        if (!foundStart) return new List<Vector2Int>();

        var path = new List<Vector2Int> { startCell };
        Vector2Int current = startCell;
        Vector2Int previous = new Vector2Int(int.MinValue, int.MinValue);

        while (true)
        {
            bool found = false;
            foreach (var dir in PathDirections.All)
            {
                Vector2Int next = current + dir;
                if (next == previous) continue;
                if (AreMutuallyConnected(current, next))
                {
                    path.Add(next);
                    previous = current;
                    current = next;
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }

        return path;
    }

    // Returns the subpath starting from fromCell to the end of the connected path.
    // If fromCell is not on the path, returns an empty list.
    public List<Vector2Int> GetPathFrom(Vector2Int fromCell)
    {
        var fullPath = GetOrderedPath();
        int index = fullPath.IndexOf(fromCell);
        if (index < 0) return new List<Vector2Int>();
        return fullPath.GetRange(index, fullPath.Count - index);
    }

    /// True if cellA faces cellB AND cellB faces back toward cellA (mutual connection).
    public bool AreMutuallyConnected(Vector2Int cellA, Vector2Int cellB)
    {
        Vector2Int dir = cellB - cellA;
        return HasExitToward(cellA, dir) && HasExitToward(cellB, PathDirections.Opposite(dir));
    }

    public bool CheckPath()
    {
        // Locate Start and Finish in GridManager
        Vector2Int startCell = default, finishCell = default;
        bool foundStart = false, foundFinish = false;

        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Start) { startCell = kvp.Key; foundStart = true; }
            if (kvp.Value.type == CellType.Finish) { finishCell = kvp.Key; foundFinish = true; }
            if (foundStart && foundFinish) break;
        }

        if (!foundStart || !foundFinish)
        {
            Debug.LogWarning("[PathChecker] Start or Finish cell not registered in GridManager yet.");
            return false;
        }

        // BFS — parent map lets us retrace the winning path for highlighting
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new HashSet<Vector2Int> { startCell };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (current == finishCell)
            {
                Debug.Log("[PathChecker] 🎉 COMPLETE PATH — Start -> Finish connected!");
                OnPathComplete(startCell, finishCell, parent);
                return true;
            }

            foreach (var dir in PathDirections.All)
            {
                Vector2Int neighbour = current + dir;
                if (!visited.Contains(neighbour) && AreMutuallyConnected(current, neighbour))
                {
                    parent[neighbour] = current;
                    visited.Add(neighbour);
                    queue.Enqueue(neighbour);
                }
            }
        }

        Debug.Log("[PathChecker] No complete path yet.");
        return false;
    }

    private void OnPathComplete(Vector2Int startCell, Vector2Int finishCell,  Dictionary<Vector2Int, Vector2Int> parent)
    {
        // Walk back from finish to start via the parent map
        Vector2Int step = finishCell;
        while (step != startCell)
        {
            HighlightCell(step, pathCompleteColor);
            if (!parent.TryGetValue(step, out step)) break; // safety guard
        }
        HighlightCell(startCell, pathCompleteColor);

        // Notify GameManager — it transitions to Won state and triggers EndGameAnimator
        if (GameManager.Instance != null)
            GameManager.Instance.OnPathComplete();
        else
            Debug.LogWarning("[PathChecker] GameManager.Instance is null — add a GameManager to the scene.");
    }

    public static void ApplyHighlight(GameObject obj, Color color)
    {
        if (obj == null) return;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            }
        }
    }

    // Highlight a registered grid cell's object by coordinate.
    private void HighlightCell(Vector2Int cell, Color color)
    {
        if (cellObjects.TryGetValue(cell, out var obj))
            ApplyHighlight(obj, color);
    }

    private static List<Color> CacheColors(GameObject obj)
    {
        var colors = new List<Color>();
        if (obj == null) return colors;

        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) colors.Add(mat.GetColor("_BaseColor"));
                else if (mat.HasProperty("_Color")) colors.Add(mat.GetColor("_Color"));
                else colors.Add(Color.white);
            }

        return colors;
    }

    private void RestoreColors(Vector2Int cell)
    {
        if (!cellObjects.TryGetValue(cell, out var obj)) return;
        if (!originalColors.TryGetValue(cell, out var cols)) return;
        if (obj == null) return;

        int idx = 0;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            foreach (Material mat in rend.materials)
            {
                if (idx >= cols.Count) break;
                Color c = cols[idx++];
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }
    }
    private IEnumerator FlashInvalidGhost(GameObject ghost, float duration)
    {
        ApplyHighlight(ghost, invalidPlacementColor);
        yield return new WaitForSeconds(duration);

    }
}
