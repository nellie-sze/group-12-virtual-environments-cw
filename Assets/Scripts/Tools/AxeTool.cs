using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class AxeTool : MonoBehaviour
{
    [Header("Tool")]
    public XRGrabInteractable axeGrab;
    public float gridSize;

    [Header("Ghost Prefab")]
    public GameObject ghostPrefab;
    public bool scaleGhostToGridCell = true;
    [Tooltip("If false, ghost is cast from tool to surface. Set true if needed for desktop.")]
    public bool useMouseCursor = false;

    [Header("Ghost Colours")]
    private Color validColor = new Color(0f, 1f, 0f, 0.5f); // green = hovering a tree
    private Color invalidColor = new Color(1f, 0.3f, 0f, 0.5f); // orange = not a tree

    private bool isHeld = false;
    private GameObject ghostHighlight;
    private int[] cachedLayers;
    private Transform[] cachedTransforms;
    private float topSurfaceY;

    void Start()
    {
        if (axeGrab != null)
        {
            axeGrab.selectEntered.AddListener(OnGrab);
            axeGrab.selectExited.AddListener(OnRelease);
            axeGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();
        Collider surfaceCollider = GridManager.Instance.gridSurfaceRenderer.GetComponent<Collider>();
        topSurfaceY = surfaceCollider.bounds.max.y;
    }

    void OnDestroy()
    {
        if (axeGrab != null)
        {
            axeGrab.selectEntered.RemoveListener(OnGrab);
            axeGrab.selectExited.RemoveListener(OnRelease);
            axeGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostHighlight != null) Destroy(ghostHighlight);
    }

    void OnGrab(SelectEnterEventArgs args) { isHeld = true; SetHeldRaycastIgnored(true); ghostHighlight.SetActive(true);  }
    void OnRelease(SelectExitEventArgs args) { isHeld = false; SetHeldRaycastIgnored(false); ghostHighlight.SetActive(false); }
    void OnActivated(ActivateEventArgs args) => TryChop();

    void SetHeldRaycastIgnored(bool ignored)
    {
        if (ignored)
        {
            cachedTransforms = GetComponentsInChildren<Transform>(true);
            cachedLayers = new int[cachedTransforms.Length];
            for (int i = 0; i < cachedTransforms.Length; i++)
            {
                cachedLayers[i] = cachedTransforms[i].gameObject.layer;
                cachedTransforms[i].gameObject.layer = 2;
            }
        }
        else
        {
            if (cachedTransforms == null || cachedLayers == null) return;
            for (int i = 0; i < cachedTransforms.Length; i++)
                if (cachedTransforms[i] != null)
                    cachedTransforms[i].gameObject.layer = cachedLayers[i];
        }
    }

    void Update()
    {
        if (!isHeld) return;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) TryChop();
        UpdateGhostPosition();
    }

    void UpdateGhostPosition()
    {
        if (Mouse.current == null || Camera.main == null || ghostHighlight == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if(!useMouseCursor && ghostHighlight != null && GridManager.Instance != null && Camera.main != null)
        {
            Vector3 origin = Camera.main.transform.position;
            Vector3 direction = (transform.position - origin).normalized;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            ray = new Ray(origin, direction);
        }
        else if (Mouse.current != null && Camera.main != null && ghostHighlight != null)
        {
            ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        }

        else
        {
            return;
        }
        
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / gridSize) * gridSize,
                topSurfaceY,
                Mathf.Round(hit.point.z / gridSize) * gridSize);

            ghostHighlight.transform.position = snapped;

            Vector2Int cell = GridManager.Instance.WorldToGrid(hit.point);
            if (!GridManager.Instance.IsWithinGridSurfaceBuffered(ghostHighlight.transform.position, gridSize / 2f))
                SetGhostColor(new Color(1f, 1f, 1f, 0f));
            else if (GridManager.Instance.IsInBounds(cell)
                  && GridManager.Instance.TryGetCell(cell, out var data)
                  && (data.type == CellType.Tree || data.type == CellType.Flower))
                SetGhostColor(validColor);
            else
                SetGhostColor(invalidColor);
        }
    }

    void TryChop()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        Debug.Log($"Trying to chop at cell {cell}");
        string targetCellDescription = DescribeTargetCell(cell);
        Debug.Log($"[AxeTool] Target cell state at {cell}: {targetCellDescription}");

        if (GridManager.Instance.TryGetCell(cell, out var data)
            && (data.type == CellType.Tree || data.type == CellType.Flower))
        {
            // Play destroy sound at the cell's world position before removing it
            AudioManager.Instance?.PlayTreeDestroySound(GridManager.Instance.GridToWorld(cell));
            ObstacleSpawner.Instance.RequestRemove(cell);
        }
        else
        {
            Debug.LogWarning($"[AxeTool] Chop failed at cell {cell}: {targetCellDescription}");
        }
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.color = color;
    }

    static void ConfigureMaterialForTransparency(Material mat)
    {
        if (mat == null) return;
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f);
            if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    void ScaleGhostToCell(GameObject target)
    {
        if (target == null || !scaleGhostToGridCell || gridSize <= 0.0001f) return;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds combined = new Bounds();
        bool hasBounds = false;
        Matrix4x4 rootWorldToLocal = target.transform.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            Bounds lb = renderer.localBounds;
            Vector3 center = lb.center;
            Vector3 extents = lb.extents;
            Vector3[] corners =
            {
                new Vector3(center.x-extents.x, center.y-extents.y, center.z-extents.z),
                new Vector3(center.x-extents.x, center.y-extents.y, center.z+extents.z),
                new Vector3(center.x-extents.x, center.y+extents.y, center.z-extents.z),
                new Vector3(center.x-extents.x, center.y+extents.y, center.z+extents.z),
                new Vector3(center.x+extents.x, center.y-extents.y, center.z-extents.z),
                new Vector3(center.x+extents.x, center.y-extents.y, center.z+extents.z),
                new Vector3(center.x+extents.x, center.y+extents.y, center.z-extents.z),
                new Vector3(center.x+extents.x, center.y+extents.y, center.z+extents.z)
            };
            Matrix4x4 toRoot = rootWorldToLocal * renderer.transform.localToWorldMatrix;
            foreach (Vector3 corner in corners)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (!hasBounds) { combined = new Bounds(p, Vector3.zero); hasBounds = true; }
                else { combined.Encapsulate(p); }
            }
        }

        if (!hasBounds) return;
        float footprint = Mathf.Max(combined.size.x, combined.size.z);
        if (footprint <= 0.0001f) return;
        target.transform.localScale *= gridSize / footprint;
    }

    void ApplyGhostColour(Color color)
    {
        if (ghostHighlight == null) return;
        foreach (Renderer renderer in ghostHighlight.GetComponentsInChildren<Renderer>())
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                ConfigureMaterialForTransparency(mat);
                SetMaterialColor(mat, color);
            }
    }

    void CreateGhost()
    {
        ghostHighlight = ghostPrefab != null
            ? Instantiate(ghostPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        foreach (Collider col in ghostHighlight.GetComponentsInChildren<Collider>())
            col.enabled = false;

        ScaleGhostToCell(ghostHighlight);
        ApplyGhostColour(invalidColor);
        ghostHighlight.SetActive(false);
    }

    string DescribeTargetCell(Vector2Int cell)
    {
        if (GridManager.Instance == null)
            return "GridManager is missing.";

        if (!GridManager.Instance.IsInBounds(cell))
            return "target cell is out of bounds.";

        if (GridManager.Instance.TryGetCell(cell, out var data))
        {
            string objectName = data.placedObject != null ? data.placedObject.name : "null object";
            return $"target cell contains {data.type} ({objectName}).";
        }

        return "target cell is empty.";
    }

    void SetGhostColor(Color color) => ApplyGhostColour(color);
}
