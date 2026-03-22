using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

public class GridSystem : MonoBehaviour
{
    public enum ToolMode { Straight, Corner }

    [Header("Placement")]
    public float gridSize = 1f;

    [Header("Tool")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable shovelGrab;
    private bool isShovelHeld = false;

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;

    [Header("Ghost Visuals")]
    [Range(0f, 1f)]
    public float ghostOpacity = 0.5f;
    public Color invalidGhostColor = new Color(1f, 0f, 0f, 1f);

    [Header("Ghost Sizing")]
    public bool scaleGhostToGridCells = true;

    public Color straightPlacedColor = new Color(0f, 0.6f, 1f, 1f);
    public Color cornerPlacedColor   = new Color(0f, 1f,   0.3f, 1f);

    private GameObject ghostObject;
    private readonly List<Material> ghostMaterials = new List<Material>();
    private readonly List<Color>    ghostBaseColors = new List<Color>();

    // Current ghost rotation in degrees (0 / 90 / 180 / 270)
    private int currentRotationY = 0;

    [Header("Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    static Color GetMaterialColor(Material mat)
    {
        if (mat == null) return Color.white;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color"))     return mat.GetColor("_Color");
        return mat.color;
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
        mat.color = color;
    }

    static void ConfigureMaterialForTransparency(Material mat)
    {
        if (mat == null) return;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))         mat.SetFloat("_Blend",         0f);
            if (mat.HasProperty("_AlphaClip"))     mat.SetFloat("_AlphaClip",     0f);
            if (mat.HasProperty("_QueueControl"))  mat.SetFloat("_QueueControl",  1f);
            if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWrite"))        mat.SetFloat("_ZWrite",        0f);
            if (mat.HasProperty("_SrcBlend"))      mat.SetFloat("_SrcBlend",      (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))      mat.SetFloat("_DstBlend",      (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_BlendOp"))       mat.SetFloat("_BlendOp",       (float)BlendOp.Add);
            if (mat.HasProperty("_BlendOpAlpha"))  mat.SetFloat("_BlendOpAlpha",  (float)BlendOp.Add);

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return;
        }

        // Built-in Standard shader fallback
        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",   0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    void CacheGhostMaterials()
    {
        ghostMaterials.Clear();
        ghostBaseColors.Clear();
        if (ghostObject == null) return;

        foreach (Renderer renderer in ghostObject.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                ConfigureMaterialForTransparency(mat);
                ghostMaterials.Add(mat);
                ghostBaseColors.Add(GetMaterialColor(mat));
            }
        }
    }

    void ApplyGhostAlphaFromBase(float alpha)
    {
        for (int i = 0; i < ghostMaterials.Count; i++)
        {
            if (ghostMaterials[i] == null) continue;
            Color c = i < ghostBaseColors.Count ? ghostBaseColors[i] : GetMaterialColor(ghostMaterials[i]);
            c.a = alpha;
            SetMaterialColor(ghostMaterials[i], c);
        }
    }

    void ApplyGhostInvalidVisual()
    {
        // Red tint on the ghost — connection or bounds check failed
        Color c = invalidGhostColor;
        c.a = ghostOpacity;
        foreach (Material mat in ghostMaterials)
            if (mat != null) SetMaterialColor(mat, c);
    }

    void ScaleGhostToGridCell()
    {
        if (ghostObject == null || !scaleGhostToGridCells) return;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds    combined       = new Bounds();
        bool      hasBounds      = false;
        Matrix4x4 rootWorldToLocal = ghostObject.transform.worldToLocalMatrix;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            Bounds  lb      = r.localBounds;
            Vector3 center  = lb.center;
            Vector3 extents = lb.extents;

            Vector3[] corners =
            {
                new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z),
                new Vector3(center.x - extents.x, center.y - extents.y, center.z + extents.z),
                new Vector3(center.x - extents.x, center.y + extents.y, center.z - extents.z),
                new Vector3(center.x - extents.x, center.y + extents.y, center.z + extents.z),
                new Vector3(center.x + extents.x, center.y - extents.y, center.z - extents.z),
                new Vector3(center.x + extents.x, center.y - extents.y, center.z + extents.z),
                new Vector3(center.x + extents.x, center.y + extents.y, center.z - extents.z),
                new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z)
            };

            Matrix4x4 toRoot = rootWorldToLocal * r.transform.localToWorldMatrix;
            foreach (Vector3 corner in corners)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (!hasBounds) { combined = new Bounds(p, Vector3.zero); hasBounds = true; }
                else            { combined.Encapsulate(p); }
            }
        }

        if (!hasBounds) return;
        float footprint = Mathf.Max(combined.size.x, combined.size.z);
        if (footprint <= 0.0001f || gridSize <= 0.0001f) return;

        ghostObject.transform.localScale *= gridSize / footprint;
    }

    void CreateGhostObject()
    {
        GameObject prefab = currentMode == ToolMode.Straight ? straightPrefab : cornerPrefab;
        ghostObject = Instantiate(prefab);
        ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

        // Disable physics on the ghost — it's purely visual
        if (ghostObject.TryGetComponent<Collider>(out var rootCol)) rootCol.enabled = false;
        foreach (Collider col in ghostObject.GetComponentsInChildren<Collider>())
            col.enabled = false;

        ScaleGhostToGridCell();
        CacheGhostMaterials();
        ApplyGhostAlphaFromBase(ghostOpacity);
    }

    bool IsWithinGrid(Vector3 pos)
    {
        Bounds b = GetComponent<Renderer>().bounds;
        return pos.x >= b.min.x && pos.x <= b.max.x &&
               pos.z >= b.min.z && pos.z <= b.max.z;
    }

    // Snaps a world position to the nearest grid cell centre.
    Vector3 SnapToGrid(Vector3 pos) => new Vector3(
        Mathf.Round(pos.x / gridSize) * gridSize,
        Mathf.Round(pos.y / gridSize) * gridSize,
        Mathf.Round(pos.z / gridSize) * gridSize);

    List<Vector3> GetGhostOccupiedCells()
    {
        var cells = new List<Vector3>();
        if (ghostObject == null) return cells;

        foreach (Renderer r in ghostObject.GetComponentsInChildren<Renderer>())
        {
            Vector3 s = SnapToGrid(r.transform.position);
            if (!cells.Contains(s)) cells.Add(s);
        }
        // Fallback to root position if no child renderers found
        if (cells.Count == 0) cells.Add(SnapToGrid(ghostObject.transform.position));
        return cells;
    }

    List<Vector3> GetCellsForPlacedObject(GameObject obj)
    {
        var cells = new List<Vector3>();

        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            Vector3 s = SnapToGrid(r.transform.position);
            if (!cells.Contains(s)) cells.Add(s);
        }
        if (cells.Count == 0) cells.Add(SnapToGrid(obj.transform.position));
        return cells;
    }

    void UpdateGhostPosition()
    {
        if (Mouse.current == null || Camera.main == null || ghostObject == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 point  = hit.point;
            float   radius = 0.1f;

            bool inGrid = IsWithinGrid(point)
                       && IsWithinGrid(point + new Vector3( radius, 0f,  0f))
                       && IsWithinGrid(point + new Vector3(-radius, 0f,  0f))
                       && IsWithinGrid(point + new Vector3( 0f, 0f,  radius))
                       && IsWithinGrid(point + new Vector3( 0f, 0f, -radius));

            if (!inGrid) return;

            Vector3 snapped = SnapToGrid(point);
            ghostObject.transform.position = snapped;
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            // Red ghost = cannot place here; normal semi-transparent ghost = valid
            if (CanPlaceGhost()) ApplyGhostAlphaFromBase(ghostOpacity);
            else                 ApplyGhostInvalidVisual();
        }
    }

    // Builds the PathNode the ghost would create if placed now.
    PathNode BuildNodeForGhost() =>
        currentMode == ToolMode.Straight
            ? PathNode.Straight(currentRotationY)
            : PathNode.Corner(currentRotationY);

    // Returns true if the candidate cell has at least one mutually-connected
    // neighbour that is a Path or Start cell.
    bool IsConnectedToExistingPath(Vector2Int gridCell, PathNode candidateNode)
    {
        foreach (Vector2Int dir in PathDirections.All)
        {
            Vector2Int neighbourCell = gridCell + dir;

            // Only connect to Path or Start cells
            if (!GridManager.Instance.TryGetCell(neighbourCell, out GridCell data)) continue;
            if (data.type != CellType.Path && data.type != CellType.Start)          continue;

            if (!candidateNode.HasExit(dir)) continue;

            if (!PathChecker.Instance.HasExitToward(neighbourCell, PathDirections.Opposite(dir))) continue;

            return true; // valid mutual connection found
        }

        return false;
    }

    bool CanPlaceGhost()
    {
        List<Vector3> ghostCells = GetGhostOccupiedCells();

        foreach (Vector3 worldPos in ghostCells)
        {
            if (!IsWithinGrid(worldPos))                                              return false;
            if (GridManager.Instance.IsOccupied(GridManager.Instance.WorldToGrid(worldPos))) return false;
        }

        // Connection check — primary cell is [0] (the root / anchor of the piece)
        Vector2Int primaryCell    = GridManager.Instance.WorldToGrid(ghostCells[0]);
        PathNode   candidateNode  = BuildNodeForGhost();

        return IsConnectedToExistingPath(primaryCell, candidateNode);
    }

    void PlaceObject()
    {
        // Block placement until the game is actually running
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
        {
            Debug.Log("[GridSystem] Cannot place — game not started yet.");
            return;
        }
        if (ghostObject == null) return;

        List<Vector3> ghostCells = GetGhostOccupiedCells();
        Vector2Int    primaryCell = GridManager.Instance.WorldToGrid(ghostCells[0]);

        if (!CanPlaceGhost())
        {
            // Yellow flash on the ghost + console log with grid/world coords
            PathChecker.Instance.ReportInvalidPlacement(primaryCell, ghostObject);
            return;
        }

        // Spawn the real block over the network via PathBlockManager.
        bool isStraight = currentMode == ToolMode.Straight;
        GameObject placed = PathBlockManager.Instance.RequestPlace(
            ghostObject.transform.position,
            Quaternion.Euler(0f, currentRotationY, 0f),
            isStraight);
        if (placed == null) return;

        // Register all cells occupied by this block in GridManager
        List<Vector3> cellWorldPositions = GetCellsForPlacedObject(placed);
        foreach (Vector3 cellPos in cellWorldPositions)
            GridManager.Instance.TryPlace(GridManager.Instance.WorldToGrid(cellPos), CellType.Path, placed);

        // Build the directional node for this piece
        PathNode node = currentMode == ToolMode.Straight
            ? PathNode.Straight(currentRotationY)
            : PathNode.Corner(currentRotationY);

        PathChecker.Instance.RegisterNode(primaryCell, node, placed);

        // Run BFS to check if Start -> Finish is now fully connected
        PathChecker.Instance.CheckPath();
    }

    public void RegisterSpecialCells()
    {
        if (PathChecker.Instance == null)
        {
            Debug.LogWarning("[GridSystem] PathChecker not found — cannot register special cells.");
            return;
        }

        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Start || kvp.Value.type == CellType.Finish)
            {
                PathChecker.Instance.RegisterNode(kvp.Key, PathNode.Omnidirectional(), kvp.Value.placedObject);
                Debug.Log($"[GridSystem] Registered {kvp.Value.type} cell at {kvp.Key} as omnidirectional node");
            }
        }
    }

    private void OnShovelGrab(SelectEnterEventArgs args)
    {
        isShovelHeld = true;
        if (ghostObject != null) { ghostObject.SetActive(true); ApplyGhostAlphaFromBase(ghostOpacity); }
        Debug.Log("[GridSystem] Shovel grabbed");
    }

    private void OnShovelRelease(SelectExitEventArgs args)
    {
        isShovelHeld = false;
        if (ghostObject != null) ghostObject.SetActive(false);
        Debug.Log("[GridSystem] Shovel released");
    }

    private void OnGrabActivated(ActivateEventArgs args)
    {
        Debug.Log("[GridSystem] Placing via XR activate");
        PlaceObject();
    }

    void Start()
    {
        if (straightPrefab == null) { Debug.LogError("[GridSystem] straightPrefab not assigned."); enabled = false; return; }
        if (cornerPrefab   == null) { Debug.LogError("[GridSystem] cornerPrefab not assigned.");   enabled = false; return; }
        if (PathChecker.Instance == null)
            Debug.LogWarning("[GridSystem] PathChecker not found in scene — add it as a component.");

        // XR events — uncomment when moving from desktop prototype to XR
        // if (shovelGrab != null)
        // {
        //     shovelGrab.selectEntered.AddListener(OnShovelGrab);
        //     shovelGrab.selectExited.AddListener(OnShovelRelease);
        //     shovelGrab.activated.AddListener(OnGrabActivated);
        // }

        // NOTE: Do NOT call RegisterSpecialCells() here.
        // StartFinishSpawner.Start() runs after this and calls it explicitly once
        // Start/Finish cells are written into GridManager.

        CreateGhostObject();
        ghostObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.RemoveListener(OnShovelGrab);
            shovelGrab.selectExited.RemoveListener(OnShovelRelease);
            shovelGrab.activated.RemoveListener(OnGrabActivated);
        }
    }

    void Update()
    {
        if (!isShovelHeld) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.bKey.wasPressedThisFrame) PlaceObject();  // B = place
            if (Keyboard.current.mKey.wasPressedThisFrame) RotateGhost();  // M = rotate
        }

        UpdateGhostPosition();
    }

    void RotateGhost()
    {
        currentRotationY = (currentRotationY + 90) % 360;

        if (ghostObject != null)
        {
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            if (CanPlaceGhost()) ApplyGhostAlphaFromBase(ghostOpacity);
            else                 ApplyGhostInvalidVisual();
        }

        Debug.Log($"[GridSystem] Ghost rotated to {currentRotationY}°");
    }

    private void SwitchMode(InputAction.CallbackContext ctx) { }
}