using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ShovelTool : MonoBehaviour
{
    public enum ToolMode { Straight, Corner }

    [Header("Tool")]
    public XRGrabInteractable shovelGrab;
    public float gridSize;
    public InputActionReference rotateAction;
    public InputActionReference switchModeAction;

    [Tooltip("Small vertical offset to keep the placed object above the surface and avoid z-fighting.")]
    public float surfaceEpsilon = 0.001f;

    [Header("Ghost Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    [Header("Ghost")]
    private GameObject ghostHighlight;
    public bool scaleGhostToGridCell = true;
    [Tooltip("If false, ghost is cast from tool to surface. Set true if needed for desktop.")]
    public bool useMouseCursor = false;
    public Color invalidColor = new Color(1f, 0.3f, 0f, 0.5f); 

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;
    private bool isHeld         = false;
    private int  currentRotationY = 0;

    private int[]       cachedLayers;
    private Transform[] cachedTransforms;
    private float       topSurfaceY;
    private Collider    surfaceCollider;

    private readonly List<Color> ghostBaseColors = new List<Color>();

    void Start()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnGrab);
            shovelGrab.selectExited.AddListener(OnRelease);
            shovelGrab.activated.AddListener(OnActivated);
        }

        rotateAction?.action.Enable();
        switchModeAction?.action.Enable();

        CreateGhost();

        if (GridManager.Instance != null && GridManager.Instance.gridSurfaceRenderer != null)
        {
            surfaceCollider = GridManager.Instance.gridSurfaceRenderer.GetComponent<Collider>();
            if (surfaceCollider != null)
                topSurfaceY = surfaceCollider.bounds.max.y;
        }
    }

    void OnEnable()
    {
        if (rotateAction != null && rotateAction.action != null)
        {
            rotateAction.action.Enable();
            rotateAction.action.performed += OnRotatePerformed;
        }
        if (switchModeAction != null && switchModeAction.action != null)
        {
            switchModeAction.action.Enable();
            switchModeAction.action.performed += OnSwitchModePerformed;
        }
    }

    void OnDisable()
    {
        if (rotateAction != null && rotateAction.action != null)
        {
            rotateAction.action.performed -= OnRotatePerformed;
            rotateAction.action.Disable();
        }
        if (switchModeAction != null && switchModeAction.action != null)
        {
            switchModeAction.action.performed -= OnSwitchModePerformed;
            switchModeAction.action.Disable();
        }
    }

    void OnDestroy()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.RemoveListener(OnGrab);
            shovelGrab.selectExited.RemoveListener(OnRelease);
            shovelGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostHighlight != null) Destroy(ghostHighlight);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        SetHeldRaycastIgnored(true);
        ghostHighlight.SetActive(true);
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        SetHeldRaycastIgnored(false);
        ghostHighlight.SetActive(false);
    }

    void OnActivated(ActivateEventArgs args) => TryBuild();

    void SetHeldRaycastIgnored(bool ignored)
    {
        if (ignored)
        {
            cachedTransforms = GetComponentsInChildren<Transform>(true);
            cachedLayers     = new int[cachedTransforms.Length];
            for (int i = 0; i < cachedTransforms.Length; i++)
            {
                cachedLayers[i] = cachedTransforms[i].gameObject.layer;
                cachedTransforms[i].gameObject.layer = 2; // Ignore Raycast
            }
        }
        else
        {
            if (cachedTransforms == null || cachedLayers == null) return;
            for (int i = 0; i < cachedTransforms.Length; i++)
            {
                if (cachedTransforms[i] != null)
                    cachedTransforms[i].gameObject.layer = cachedLayers[i];
            }
        }
    }

    void Update()
    {
        if (!isHeld) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.pKey.wasPressedThisFrame) SwitchMode();
            if (Keyboard.current.tKey.wasPressedThisFrame) TryBuild();
            if (Keyboard.current.mKey.wasPressedThisFrame) RotateGhost();
        }

        UpdateGhostPosition();
    }

    void OnRotatePerformed(InputAction.CallbackContext ctx)     { if (isHeld) RotateGhost();  }
    void OnSwitchModePerformed(InputAction.CallbackContext ctx) { if (isHeld) SwitchMode();   }

    void UpdateGhostPosition()
    {
        Ray ray;
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

        if (surfaceCollider != null && surfaceCollider.Raycast(ray, out RaycastHit hit, 100f))
        {
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / gridSize) * gridSize,
                topSurfaceY,
                Mathf.Round(hit.point.z / gridSize) * gridSize
            );

            ghostHighlight.transform.position = snapped; 
            ghostHighlight.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            Vector2Int cell = GridManager.Instance.WorldToGrid(snapped);

            if (!GridManager.Instance.IsWithinGridSurfaceBuffered(snapped, gridSize / 2f))
                ApplyGhostBaseTransparency(0.0f);
            else if (GridManager.Instance.IsInBounds(cell) && !GridManager.Instance.IsOccupied(cell) && CanConnect(cell))
                ApplyGhostBaseTransparency(0.5f);
            else
                SetGhostColor(invalidColor);
        }
        else if (ghostHighlight != null)
        {
            ApplyGhostBaseTransparency(0.0f);
        }
    }

    bool CanConnect(Vector2Int cell, int rotationY)
    {
        if (PathChecker.Instance == null) return true; // fail-open if PathChecker missing

        PathNode candidate = currentMode == ToolMode.Straight
            ? PathNode.Straight(rotationY)
            : PathNode.Corner(rotationY);

        foreach (Vector2Int dir in PathDirections.All)
        {
            Vector2Int neighbour = cell + dir;

            if (!GridManager.Instance.TryGetCell(neighbour, out GridCell data)) continue;
            if (data.type != CellType.Path && data.type != CellType.Start)      continue;

            // Candidate must face the neighbour
            if (!candidate.HasExit(dir)) continue;

            // Neighbour must face back
            if (!PathChecker.Instance.HasExitToward(neighbour, PathDirections.Opposite(dir))) continue;

            return true;
        }

        return false;
    }

    bool CanConnect(Vector2Int cell) => CanConnect(cell, currentRotationY);

    void TryBuild()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector3 placementPosition = ghostHighlight.transform.position;
        Vector2Int cell = GridManager.Instance.WorldToGrid(placementPosition);

        if (!GridManager.Instance.IsWithinGridSurfaceBuffered(placementPosition, gridSize / 2f))
        {
            Debug.LogError($"[ShovelTool] Rejected out-of-buffer path placement at cell {cell}, worldPos {placementPosition}. Grid buffer bounds would be exceeded.");
            return;
        }

        if (!GridManager.Instance.IsInBounds(cell) || GridManager.Instance.IsOccupied(cell)) return;

        Debug.Log($"Trying to build at cell {cell}");
        
        if (!CanConnect(cell))
        {
            // Yellow flash feedback + console log
            if (PathChecker.Instance != null)
                PathChecker.Instance.ReportInvalidPlacement(cell, ghostHighlight);
            else
                Debug.LogWarning($"INVALID placement at {cell} — no valid connection.");

            // Sound: invalid placement
            AudioManager.Instance?.PlayInvalidPlacementSound();
            return;
        }

        bool isStraight = currentMode == ToolMode.Straight;
        Quaternion placementRotation = Quaternion.Euler(0f, currentRotationY, 0f);
        Debug.Log($"Placing path at position={placementPosition}, cell={cell}");

        GameObject placedObject = PathBlockManager.Instance.RequestPlace(placementPosition, placementRotation, isStraight);
        if (placedObject == null) return;

        GridManager.Instance.TryPlace(cell, CellType.Path, placedObject);

        PathNode node = currentMode == ToolMode.Straight
            ? PathNode.Straight(currentRotationY)
            : PathNode.Corner(currentRotationY);

        if (PathChecker.Instance != null)
        {
            PathChecker.Instance.RegisterNode(cell, node, placedObject);

            // Run BFS — if Start -> Finish is now connected, triggers win condition
            PathChecker.Instance.CheckPath();
        }
        // Sound: path block successfully placed
        AudioManager.Instance?.PlayPathBuiltSound();

        Debug.Log($"[ShovelTool] Placed {currentMode} at cell {cell}, rotation {currentRotationY}°");
    }

    void RotateGhost()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf || GridManager.Instance == null)
            return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        int originalRotationY = currentRotationY;
        currentRotationY = (currentRotationY + 90) % 360;
        
        if (!CanConnect(cell, currentRotationY))
        {
            if (currentMode == ToolMode.Straight)
            {
                currentRotationY = originalRotationY;
                return;
            }
            else if (currentMode == ToolMode.Corner)
            {
                // Try all 3 other rotations to find a valid one
                for (int i = 0; i < 3; i++)
                {
                    currentRotationY = (currentRotationY + 90) % 360;
                    if (CanConnect(cell, currentRotationY))
                    {
                        ghostHighlight.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
                        Debug.Log("[ShovelTool] Rotated ghost to " + currentRotationY + "°");
                        return;
                    }
                }
            }
        }
        else if (CanConnect(cell, currentRotationY))
        {
            ghostHighlight.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            Debug.Log("[ShovelTool] Rotated ghost to " + currentRotationY + "°");
            return;
        }
        currentRotationY = originalRotationY;
        return;
    }

    void SwitchMode()
    {
        currentMode = currentMode == ToolMode.Straight ? ToolMode.Corner : ToolMode.Straight;

        FindAnyObjectByType<ToolModeUI>()?.UpdateModeText();

        if (ghostHighlight != null) Destroy(ghostHighlight);

        CreateGhost();
        UpdateGhostPosition();
        ghostHighlight.SetActive(isHeld);

        Debug.Log("[ShovelTool] Switched mode to " + currentMode);
    }

    private void SwitchMode(InputAction.CallbackContext ctx) => SwitchMode();

    void CreateGhost()
    {
        GameObject ghostPrefab = currentMode == ToolMode.Straight ? straightPrefab : cornerPrefab;
        ghostHighlight = CreateCenteredWrapper(ghostPrefab, $"{ghostPrefab.name}_GhostRoot");

        foreach (Collider col in ghostHighlight.GetComponentsInChildren<Collider>())
            col.enabled = false;

        foreach (Renderer r in ghostHighlight.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows    = false;
        }

        ScaleGhostToCell(ghostHighlight);
        CacheGhostBaseColours();
        ApplyGhostBaseTransparency(0.5f);
        ghostHighlight.SetActive(false);
    }

    GameObject CreateCenteredWrapper(GameObject prefab, string wrapperName)
    {
        GameObject wrapper = new GameObject(wrapperName);
        GameObject visual  = Instantiate(prefab, wrapper.transform);
        StripGhostOnlyComponents(visual);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        CenterVisualUnderRoot(wrapper.transform, visual.transform);
        return wrapper;
    }

    void StripGhostOnlyComponents(GameObject ghostVisual)
    {
        if (ghostVisual == null) return;

        foreach (var networked in ghostVisual.GetComponentsInChildren<NetworkedSpawnedTransform>(true))
            Destroy(networked);

        foreach (var agent in ghostVisual.GetComponentsInChildren<PathBlockAgent>(true))
            Destroy(agent);
    }

    void CenterVisualUnderRoot(Transform root, Transform visual)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds    combined   = new Bounds();
        bool      hasBounds  = false;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            Bounds  bounds  = renderer.bounds;
            Vector3 center  = bounds.center;
            Vector3 extents = bounds.extents;

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

            foreach (Vector3 corner in corners)
            {
                Vector3 localPoint = worldToRoot.MultiplyPoint3x4(corner);
                if (!hasBounds) { combined = new Bounds(localPoint, Vector3.zero); hasBounds = true; }
                else            { combined.Encapsulate(localPoint); }
            }
        }

        if (!hasBounds) return;
        Vector3 centerOffset = combined.center;
        visual.localPosition -= new Vector3(centerOffset.x, 0f, centerOffset.z);
    }

    void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null) return;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float maxWidthXZ = Mathf.Max(bounds.size.x, bounds.size.z);
        float cellSize   = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f) return;

        float targetMaxWidth = cellSize * 1f;
        if (maxWidthXZ <= targetMaxWidth) return;

        obj.transform.localScale *= targetMaxWidth / maxWidthXZ;
    }

    void ScaleGhostToCell(GameObject target)
    {
        Debug.Log("GridSize: " + gridSize);
        if (target == null || !scaleGhostToGridCell || gridSize <= 0.0001f) return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds    combined       = new Bounds();
        bool      hasBounds      = false;
        Matrix4x4 rootWorldToLocal = target.transform.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            Bounds  lb      = renderer.localBounds;
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

            Matrix4x4 toRoot = rootWorldToLocal * renderer.transform.localToWorldMatrix;
            foreach (Vector3 corner in corners)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (!hasBounds) { combined = new Bounds(p, Vector3.zero); hasBounds = true; }
                else            { combined.Encapsulate(p); }
            }
        }

        if (!hasBounds) return;
        float footprint = Mathf.Max(combined.size.x, combined.size.z);
        if (footprint <= 0.0001f) return;

        target.transform.localScale *= gridSize / footprint;
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

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    void ApplyGhostColour(Color color)
    {
        if (ghostHighlight == null) return;
        foreach (Renderer renderer in ghostHighlight.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                ConfigureMaterialForTransparency(mat);
                SetMaterialColor(mat, color);
            }
        }
    }

    void CacheGhostBaseColours()
    {
        ghostBaseColors.Clear();
        if (ghostHighlight == null) return;

        foreach (Renderer renderer in ghostHighlight.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                ghostBaseColors.Add(mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : mat.color);
            }
        }
    }

    void ApplyGhostBaseTransparency(float alpha)
    {
        if (ghostHighlight == null) return;

        int colorIndex = 0;
        foreach (Renderer renderer in ghostHighlight.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                ConfigureMaterialForTransparency(mat);
                Color baseColor = colorIndex < ghostBaseColors.Count ? ghostBaseColors[colorIndex] : mat.color;
                baseColor.a = alpha;
                SetMaterialColor(mat, baseColor);
                colorIndex++;
            }
        }
    }

    void SetGhostColor(Color color) => ApplyGhostColour(color);
}
