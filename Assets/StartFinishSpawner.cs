using UnityEngine;

public class StartFinishSpawner : MonoBehaviour
{
    [Header("Markers")]
    public GameObject startPrefab;
    public GameObject finishPrefab;

    void Start()
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;
        int midZ = (gridMin.y + gridMax.y) / 2;

        // Start on the left edge (min X), finish on the right edge (max X)
        Vector2Int startCell  = new Vector2Int(gridMin.x, midZ);
        Vector2Int finishCell = new Vector2Int(gridMax.x, midZ);

        PlaceMarker(startPrefab,  startCell,  CellType.Start);
        PlaceMarker(finishPrefab, finishCell, CellType.Finish);
    }

    void PlaceMarker(GameObject prefab, Vector2Int cell, CellType type)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"StartFinishSpawner: {type} prefab not assigned.");
            return;
        }

        Vector3 pos = GridManager.Instance.GridToWorld(cell);
        GameObject obj = Instantiate(prefab, pos, Quaternion.identity);
        GridManager.Instance.TryPlace(cell, type, obj);
    }
}
