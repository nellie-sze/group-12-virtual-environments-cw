using System.Collections.Generic;
using UnityEngine;

// Top-level enum so all scripts can use CellType.X without a prefix
public enum CellType { Empty, Path, Lava, Tree, Rock, Flower, Start, Finish }

public class GridCell
{
    public CellType type;
    public GameObject placedObject;
}

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Settings")]
    [Tooltip("Optional explicit reference. If unset, GridManager will try to find a GridSystem in the scene.")]
    [SerializeField] private GridSystem gridSystem;

    // Read-only: sourced from GridSystem.gridSize (single source of truth).
    public float gridSize { get; private set; } = 1f;

    [Header("Grid Surface")]
    [Tooltip("Assign the Renderer on the plane/table surface")]
    public Renderer gridSurfaceRenderer;

    // Read-only grid bounds in grid coordinates
    public Vector2Int gridMin { get; private set; }
    public Vector2Int gridMax { get; private set; }
    private float surfaceY;

    private Dictionary<Vector2Int, GridCell> cells = new Dictionary<Vector2Int, GridCell>();

    private void SyncGridSizeFromGridSystem()
    {
        if (!gridSystem)
        {
            gridSystem = FindAnyObjectByType<GridSystem>();
        }

        if (gridSystem)
        {
            gridSize = gridSystem.gridSize;
        }
        else
        {
            Debug.LogWarning("GridManager: no GridSystem found - using default gridSize=1. Assign a GridSystem reference to GridManager or add one to the scene.");
            gridSize = 1f;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SyncGridSizeFromGridSystem();

        // Calculate bounds in Awake so spawners can use them in Start()
        if (gridSurfaceRenderer != null)
        {
            Bounds bounds = gridSurfaceRenderer.bounds;
            surfaceY = bounds.max.y;
            gridMin = new Vector2Int(
                Mathf.CeilToInt(bounds.min.x / gridSize),
                Mathf.CeilToInt(bounds.min.z / gridSize)
            );
            gridMax = new Vector2Int(
                Mathf.FloorToInt(bounds.max.x / gridSize),
                Mathf.FloorToInt(bounds.max.z / gridSize)
            );
        }
        else
        {
            Debug.LogWarning("GridManager: gridSurfaceRenderer not assigned — grid bounds defaulting to 0.");
        }
    }

    private void OnValidate()
    {
        // Keep bounds math consistent in edit mode when GridSystem.gridSize changes.
        SyncGridSizeFromGridSystem();

        if (gridSurfaceRenderer != null)
        {
            Bounds bounds = gridSurfaceRenderer.bounds;
            surfaceY = bounds.max.y;
            gridMin = new Vector2Int(
                Mathf.CeilToInt(bounds.min.x / gridSize),
                Mathf.CeilToInt(bounds.min.z / gridSize)
            );
            gridMax = new Vector2Int(
                Mathf.FloorToInt(bounds.max.x / gridSize),
                Mathf.FloorToInt(bounds.max.z / gridSize)
            );
        }
    }

    // World XZ position -> grid coordinate
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / gridSize),
            Mathf.RoundToInt(worldPos.z / gridSize)
        );
    }

    // Grid coordinate -> world position at surface height
    public Vector3 GridToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * gridSize, surfaceY, cell.y * gridSize);
    }

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= gridMin.x && cell.x <= gridMax.x && cell.y >= gridMin.y && cell.y <= gridMax.y;
    }

    // World position check against the surface bounds (XZ only).
    public bool IsWithinGridSurface(Vector3 worldPos)
    {
        if (gridSurfaceRenderer == null)
        {
            return true;
        }

        Bounds bounds = gridSurfaceRenderer.bounds;
        return worldPos.x >= bounds.min.x && worldPos.x <= bounds.max.x &&
            worldPos.z >= bounds.min.z && worldPos.z <= bounds.max.z;
    }

    // Matches GridSystem's "radius" placement buffer (prevents overhang at edges).
    public bool IsWithinGridSurfaceBuffered(Vector3 worldPos, float radius)
    {
        return IsWithinGridSurface(worldPos) &&
               IsWithinGridSurface(worldPos + new Vector3(radius, 0.0f, 0.0f)) &&
               IsWithinGridSurface(worldPos + new Vector3(0.0f, 0.0f, radius)) &&
               IsWithinGridSurface(worldPos + new Vector3(-radius, 0.0f, 0.0f)) &&
               IsWithinGridSurface(worldPos + new Vector3(0.0f, 0.0f, -radius));
    }

    // Returns false if cell is already occupied
    public bool TryPlace(Vector2Int cell, CellType type, GameObject obj)
    {
        if (cells.ContainsKey(cell)) return false;
        cells[cell] = new GridCell { type = type, placedObject = obj };
        return true;
    }

    // Destroys the GameObject and removes ALL cells that reference it (handles multi-cell objects)
    public bool TryRemove(Vector2Int cell)
    {
        if (!cells.TryGetValue(cell, out var data)) return false;

        if (data.placedObject != null)
        {
            // Clean up every cell that points to the same object (e.g. 2-cell corner pieces)
            var toRemove = new List<Vector2Int>();
            foreach (var kvp in cells)
            {
                if (kvp.Value.placedObject == data.placedObject)
                    toRemove.Add(kvp.Key);
            }
            foreach (var k in toRemove)
                cells.Remove(k);

            Destroy(data.placedObject);
        }
        else
        {
            cells.Remove(cell);
        }

        return true;
    }

    public bool IsOccupied(Vector2Int cell) => cells.ContainsKey(cell);

    // Removes all GridManager entries that point to this object WITHOUT destroying it.
    // Used by ObstacleSpawner.HandleRemove — destruction is handled separately by NSM.Despawn or Destroy.
    public void ClearCellsForObject(GameObject obj)
    {
        if (obj == null) return;
        var toRemove = new List<Vector2Int>();
        foreach (var kvp in cells)
            if (kvp.Value.placedObject == obj)
                toRemove.Add(kvp.Key);
        foreach (var k in toRemove)
            cells.Remove(k);
    }

    public bool TryGetCell(Vector2Int cell, out GridCell data) =>
        cells.TryGetValue(cell, out data);

    // Used by PathChecker to iterate all placed cells
    public IEnumerable<KeyValuePair<Vector2Int, GridCell>> GetAllCells() => cells;

    // Returns a random in-bounds cell (may or may not be occupied — callers should check)
    public Vector2Int GetRandomCell()
    {
        return new Vector2Int(
            Random.Range(gridMin.x, gridMax.x + 1),
            Random.Range(gridMin.y, gridMax.y + 1)
        );
    }
}
