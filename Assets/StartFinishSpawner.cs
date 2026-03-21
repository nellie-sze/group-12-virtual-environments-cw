using UnityEngine;

public class StartFinishSpawner : MonoBehaviour
{
    [Header("Flag Markers (existing)")]
    public GameObject startPrefab;
    public Color      startColor  = Color.green;
    public GameObject finishPrefab;
    public Color      finishColor = Color.red;

    [Header("Base Blocks (new — placed directly under each flag)")]
    [Tooltip("Prefab for the solid block that sits under the start flag. " +
             "Can be the same prefab as the flag or a dedicated block prefab.")]
    public GameObject startBlockPrefab;

    [Tooltip("Prefab for the solid block that sits under the finish flag.")]
    public GameObject finishBlockPrefab;

    public Color startBlockColor  = new Color(0.0f, 0.8f, 0.2f, 1f); // green tint
    public Color finishBlockColor = new Color(0.9f, 0.2f, 0.2f, 1f); // red tint

    [Header("References")]
    [Tooltip("Assign the GridSystem in the scene so we can register Start/Finish nodes after spawning.")]
    public GridSystem gridSystem;

    // How far below the flag the base block sits (usually one grid cell height).
    [Tooltip("Y offset applied to the flag's world position to position the base block underneath it.")]
    public float blockYOffset = -1f;

    void Start()
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;

        Vector2Int startCell, finishCell;

        // Randomly place start and finish on opposite edges of the grid
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
            startCell  = new Vector2Int(startX, gridMin.y);
            finishCell = new Vector2Int(finishX, gridMax.y);
        }

        PlaceMarker(startPrefab,  startColor,  startCell,  CellType.Start);
        PlaceMarker(finishPrefab, finishColor, finishCell, CellType.Finish);

        PlaceBaseBlock(startBlockPrefab,  startBlockColor,  startCell,  CellType.Start,  "START");
        PlaceBaseBlock(finishBlockPrefab, finishBlockColor, finishCell, CellType.Finish, "FINISH");

        if (gridSystem != null)
            gridSystem.RegisterSpecialCells();
        else
            Debug.LogWarning("[StartFinishSpawner] GridSystem reference not assigned — " +
                             "PathChecker nodes for Start/Finish won't be registered. " +
                             "Assign GridSystem in the Inspector.");
    }

    void PlaceMarker(GameObject prefab, Color color, Vector2Int cell, CellType type)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[StartFinishSpawner] {type} flag prefab not assigned.");
            return;
        }

        Vector3    pos = GridManager.Instance.GridToWorld(cell);
        GameObject obj = Instantiate(prefab, pos, Quaternion.identity);
        ApplyColor(obj, color);

        // Register flag in GridManager so PathChecker/GridSystem know this cell is occupied
        GridManager.Instance.TryPlace(cell, type, obj);
    }

    //base block placed directly under the flag at the same grid cell
    void PlaceBaseBlock(GameObject prefab, Color color, Vector2Int cell, CellType type, string label)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[StartFinishSpawner] {label} block prefab not assigned — " +
                             "skipping base block. Assign startBlockPrefab / finishBlockPrefab in the Inspector.");
            return;
        }

        // Position: same XZ as the flag, shifted down by blockYOffset so it sits beneath it
        Vector3 flagWorldPos  = GridManager.Instance.GridToWorld(cell);
        Vector3 blockWorldPos = flagWorldPos + new Vector3(0f, blockYOffset, 0f);

        GameObject block = Instantiate(prefab, blockWorldPos, Quaternion.identity);
        ApplyColor(block, color);

        Debug.Log($"[StartFinishSpawner] {label} block placed.\n" +
                  $"  Grid coordinate : {cell}\n" +
                  $"  World position  : {blockWorldPos}");
    }

    static void ApplyColor(GameObject obj, Color color)
    {
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            }
        }
    }
}