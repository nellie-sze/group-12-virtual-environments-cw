using UnityEngine;

public class StartFinishSpawner : MonoBehaviour
{
    [Header("Markers")]
    public GameObject startPrefab;
    public Color startColor = Color.green;

    public GameObject finishPrefab;
    public Color finishColor = Color.red;

    void Start()
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;

        Vector2Int startCell, finishCell;

        if (Random.value < 0.5f)
        {
            // Left / right edges — random Z for each
            int startZ  = Random.Range(gridMin.y, gridMax.y + 1);
            int finishZ = Random.Range(gridMin.y, gridMax.y + 1);
            startCell  = new Vector2Int(gridMin.x, startZ);
            finishCell = new Vector2Int(gridMax.x, finishZ);
        }
        else
        {
            // Top / bottom edges — random X for each
            int startX  = Random.Range(gridMin.x, gridMax.x + 1);
            int finishX = Random.Range(gridMin.x, gridMax.x + 1);
            startCell  = new Vector2Int(startX,  gridMin.y);
            finishCell = new Vector2Int(finishX, gridMax.y);
        }

        PlaceMarker(startPrefab,  startColor,  startCell,  CellType.Start);
        PlaceMarker(finishPrefab, finishColor, finishCell, CellType.Finish);
    }

    void PlaceMarker(GameObject prefab, Color color, Vector2Int cell, CellType type)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"StartFinishSpawner: {type} prefab not assigned.");
            return;
        }

        Vector3 pos = GridManager.Instance.GridToWorld(cell);
        GameObject obj = Instantiate(prefab, pos, Quaternion.identity);

        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", color);
            }
        }

        GridManager.Instance.TryPlace(cell, type, obj);
    }
}
