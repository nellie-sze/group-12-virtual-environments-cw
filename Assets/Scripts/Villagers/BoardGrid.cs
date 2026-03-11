using UnityEngine;

public class BoardGrid : MonoBehaviour
{
    public Collider surfaceCollider; // assign Game Surface collider
    public float extraClearance = 0.001f; // tiny epsilon to avoid z-fighting
    [Header("Grid Dimensions")]
    public int width = 8;
    public int height = 8;

    [Header("Cell Sizing")]
    public float cellSize = 0.25f;

    [Header("Origin")]
    public Transform origin; // bottom-left corner of grid on the surface

    public bool InBounds(Vector2Int cell)
        => cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;

    public Vector3 CellToWorld(Vector2Int cell, float objectHalfHeight = 0f)
{
    var o = origin != null ? origin.position : transform.position;

    float topY = o.y;
    if (surfaceCollider != null)
        topY = surfaceCollider.bounds.max.y;

    return new Vector3(
        o.x + (cell.x + 0.5f) * cellSize,
        topY + objectHalfHeight + extraClearance,
        o.z + (cell.y + 0.5f) * cellSize
    );
}

    public Vector2Int WorldToCell(Vector3 world)
{
    var o = origin != null ? origin.position : transform.position;
    var local = world - o;

    int x = Mathf.FloorToInt(local.x / cellSize);
    int y = Mathf.FloorToInt(local.z / cellSize);

    return new Vector2Int(x, y);
}

    public Vector2Int Clamp(Vector2Int cell)
        => new Vector2Int(Mathf.Clamp(cell.x, 0, width - 1), Mathf.Clamp(cell.y, 0, height - 1));

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            Gizmos.DrawWireCube(CellToWorld(new Vector2Int(x, y)), new Vector3(cellSize, 0.01f, cellSize));
        }
    }

    public float GetSurfaceTopY()
{
    var o = origin != null ? origin.position : transform.position;
    return surfaceCollider != null ? surfaceCollider.bounds.max.y : o.y;
}
}