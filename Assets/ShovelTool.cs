using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ShovelTool : MonoBehaviour
{
    public enum ToolMode
    {
        Straight,
        Corner
    }

    [Header("Tool")]
    public XRGrabInteractable shovelGrab;
    public float gridSize = 0.2f;

    [Header("Held Raycast")]
    public bool ignoreRaycastWhileHeld = true;

    [Header("Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    [Header("Ghost Prefab")]
    public GameObject ghostPrefab;
    private GameObject ghostObject;
    public bool scaleGhostToGridCell = true;

    [Header("Ghost Colours")]
    public Color validColor   = new Color(0f,   1f,   0f,   0.5f); // green  = hovering a tree
    public Color invalidColor = new Color(1f,   0.3f, 0f,   0.5f); // orange = not a tree

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;
    private bool isHeld = false;
    private int currentRotationY = 0;
    private int[] cachedLayers;
    private Transform[] cachedTransforms;

    float EffectiveGridSize => GridManager.Instance != null ? GridManager.Instance.gridSize : gridSize;

    void Start()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnGrab);
            shovelGrab.selectExited.AddListener(OnRelease);
            shovelGrab.activated.AddListener(OnActivated);
        }

        if (GridManager.Instance != null)
            gridSize = GridManager.Instance.gridSize;

        CreateGhost();
    }

    void OnDestroy()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.RemoveListener(OnGrab);
            shovelGrab.selectExited.RemoveListener(OnRelease);
            shovelGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostObject != null) Destroy(ghostObject);
    }


    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        SetHeldRaycastIgnored(true);
        if (ghostObject != null) ghostObject.SetActive(true);
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        SetHeldRaycastIgnored(false);
        if (ghostObject != null) ghostObject.SetActive(false);
    }
    void OnActivated(ActivateEventArgs args) => TryBuild();

    void SetHeldRaycastIgnored(bool ignored)
    {
        if (!ignoreRaycastWhileHeld)
            return;

        if (ignored)
        {
            cachedTransforms = GetComponentsInChildren<Transform>(true);
            cachedLayers = new int[cachedTransforms.Length];
            for (int i = 0; i < cachedTransforms.Length; i++)
            {
                cachedLayers[i] = cachedTransforms[i].gameObject.layer;
                cachedTransforms[i].gameObject.layer = 2; // Ignore Raycast
            }
        }
        else
        {
            if (cachedTransforms == null || cachedLayers == null)
                return;

            for (int i = 0; i < cachedTransforms.Length; i++)
            {
                if (cachedTransforms[i] == null)
                    continue;

                cachedTransforms[i].gameObject.layer = cachedLayers[i];
            }
        }
    }

    void Update()
    {
        if (!isHeld)
            return;

        // Desktop prototype mode switching
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            SwitchMode();
            // ToggleMode();
        }
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
        {
            Debug.Log("Building Path");
            TryBuild();
        }
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            RotateGhost();
        }

        UpdateGhostPosition();
    }

    // ── same mouse-ray pattern as GridSystem.UpdateGhostPosition ──────────────
    void UpdateGhostPosition()
    {
        if (Mouse.current == null || Camera.main == null || ghostObject == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            float s = gridSize;
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / s) * s,
                Mathf.Round(hit.point.y / s) * s,
                Mathf.Round(hit.point.z / s) * s
            );

            ghostObject.transform.position = snapped;

            Vector2Int cell = GridManager.Instance.WorldToGrid(hit.point);
            bool isValid = GridManager.Instance.TryGetCell(cell, out var data)
                           && (data.type == CellType.Tree || data.type == CellType.Flower);

            SetGhostColor(isValid ? validColor : invalidColor);
        }
    }

    void RotateGhost()
    {
        currentRotationY = (currentRotationY + 90) % 360;

        if (ghostObject != null)
        {
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            if (CanPlaceGhost())
                UpdateGhostVisual();
            else
                ApplyGhostInvalidVisual();
        }

        Debug.Log("Rotated ghost to " + currentRotationY + " degrees");
    }

    void TryBuild()
    {
        if (ghostObject == null || !ghostObject.activeSelf || GridManager.Instance == null)
            return;

        if (!CanPlaceGhost())
            return;

        GameObject prefab = currentMode == ToolMode.Straight ? straightPrefab : cornerPrefab;
        Vector3 placementPosition = ghostObject.transform.position;
        Quaternion placementRotation = Quaternion.Euler(0f, currentRotationY, 0f);

        GameObject placedObject = Instantiate(prefab, placementPosition, placementRotation);
        FitToSingleGridCell(placedObject);
        foreach (Vector3 cellWorldPos in GetCellsForPlacedObject(placedObject))
        {
            GridManager.Instance.TryPlace(GridManager.Instance.WorldToGrid(cellWorldPos), CellType.Path, placedObject);
        }
    }

    void SwitchMode()
    {
        currentMode = currentMode == ToolMode.Straight
            ? ToolMode.Corner
            : ToolMode.Straight;

        FindAnyObjectByType<ToolModeUI>()?.UpdateModeText();

        if (ghostObject != null)
            Destroy(ghostObject);

        CreateGhost();
        UpdateGhostPosition();
        ghostObject.SetActive(isHeld);

        // if (!isHeld)
        //     ghostObject.SetActive(false);

        Debug.Log("Switched mode to " + currentMode);
    }

        private void SwitchMode(InputAction.CallbackContext ctx)
    {
        SwitchMode();
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);

        mat.color = color;
    }

    static void ConfigureMaterialForTransparency(Material mat)
    {
        if (mat == null)
            return;

        // URP Lit: set Surface Type to Transparent (Alpha blend).
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f); // Alpha
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_QueueControl"))
                mat.SetFloat("_QueueControl", 1f); // UserOverride
            if (mat.HasProperty("_ZWriteControl"))
                mat.SetFloat("_ZWriteControl", 0f);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha"))
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha"))
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    private void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null)
        {
            return;
        }

        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float maxWidthXZ = Mathf.Max(bounds.size.x, bounds.size.z);
        float cellSize = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f)
        {
            return;
        }

        // Keep a small margin so it doesn't touch the cell edges visually.
        float targetMaxWidth = cellSize * 0.95f;
        if (maxWidthXZ <= targetMaxWidth)
        {
            return;
        }

        float scaleFactor = targetMaxWidth / maxWidthXZ;
        obj.transform.localScale = obj.transform.localScale * scaleFactor;
    }
    void ScaleGhostToCell(GameObject target)
    {
        float s = EffectiveGridSize;
        if (target == null || !scaleGhostToGridCell || s <= 0.0001f)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds combined = new Bounds();
        bool hasBounds = false;

        Matrix4x4 rootWorldToLocal = target.transform.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Bounds lb = renderer.localBounds;
            Vector3 center = lb.center;
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

            Matrix4x4 rendererLocalToRootLocal = rootWorldToLocal * renderer.transform.localToWorldMatrix;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 p = rendererLocalToRootLocal.MultiplyPoint3x4(corners[i]);
                if (!hasBounds)
                {
                    combined = new Bounds(p, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(p);
                }
            }
        }

        if (!hasBounds)
            return;

        float footprint = Mathf.Max(combined.size.x, combined.size.z);
        if (footprint <= 0.0001f)
            return;

        float scale = s / footprint;
        target.transform.localScale = target.transform.localScale * scale;
    }

    void ApplyGhostColour(Color color)
    {
        if (ghostObject == null)
            return;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                    continue;

                ConfigureMaterialForTransparency(mat);
                Color c = color;
                c.a = color.a; // keep inspector alpha
                SetMaterialColor(mat, c);
            }
        }
    }

    // ── ghost setup ──
    void UpdateGhostVisual() => SetGhostColor(validColor);

    void ApplyGhostInvalidVisual() => SetGhostColor(invalidColor);

    List<Vector3> GetGhostOccupiedCells()
    {
        List<Vector3> cells = new List<Vector3>();

        if (ghostObject == null)
            return cells;

        float s = EffectiveGridSize;
        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;
            Vector3 snapped = new Vector3(
                Mathf.Round(p.x / gridSize) * gridSize,
                Mathf.Round(p.y / gridSize) * gridSize,
                Mathf.Round(p.z / gridSize) * gridSize
            );

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        if (cells.Count == 0)
        {
            Vector3 rootPos = new Vector3(
                Mathf.Round(ghostObject.transform.position.x / s) * s,
                Mathf.Round(ghostObject.transform.position.y / s) * s,
                Mathf.Round(ghostObject.transform.position.z / s) * s
            );
            cells.Add(rootPos);
        }

        return cells;
    }

    bool CanPlaceGhost()
    {
        if (ghostObject == null)
            return false;

        if (GridManager.Instance == null)
            return true;

        float radius = EffectiveGridSize * 0.5f;
        if (!GridManager.Instance.IsWithinGridSurfaceBuffered(ghostObject.transform.position, radius))
            return false;

        foreach (Vector3 cellWorldPos in GetGhostOccupiedCells())
        {
            Vector2Int cell = GridManager.Instance.WorldToGrid(cellWorldPos);
            if (!GridManager.Instance.IsInBounds(cell))
                return false;
            if (GridManager.Instance.IsOccupied(cell))
                return false;
        }

        return true;
    }

    List<Vector3> GetCellsForPlacedObject(GameObject placedObject)
    {
        List<Vector3> cells = new List<Vector3>();

        if (placedObject == null)
            return cells;

        float s = EffectiveGridSize;
        Renderer[] renderers = placedObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;
            Vector3 snapped = new Vector3(
                Mathf.Round(p.x / s) * s,
                Mathf.Round(p.y / s) * s,
                Mathf.Round(p.z / s) * s
            );

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        if (cells.Count == 0)
        {
            Vector3 rootPos = new Vector3(
                Mathf.Round(placedObject.transform.position.x / s) * s,
                Mathf.Round(placedObject.transform.position.y / s) * s,
                Mathf.Round(placedObject.transform.position.z / s) * s
            );
            cells.Add(rootPos);
        }

        return cells;
    }

    void CreateGhost()
    {
        GameObject prefab = currentMode == ToolMode.Straight
        ? straightPrefab
        : cornerPrefab;

        ghostObject = Instantiate(prefab);
        ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        
        Collider rootCollider = ghostObject.GetComponent<Collider>();
        if (rootCollider != null)
            rootCollider.enabled = false;

        foreach (Collider col in ghostObject.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        ScaleGhostToCell(ghostObject);
        ApplyGhostColour(invalidColor);

        ghostObject.SetActive(false);
    }

    void SetGhostColor(Color color)
    {
        ApplyGhostColour(color);
    }
}
