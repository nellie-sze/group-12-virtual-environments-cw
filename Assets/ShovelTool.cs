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
    public float gridSize;

    [Tooltip("Small vertical offset to keep the placed object above the surface and avoid z-fighting.")]
    public float surfaceEpsilon = 0.001f;

    [Header("Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    [Header("Ghost")]
    private GameObject ghostHighlight;
    public bool scaleGhostToGridCell = true;
    public Color invalidColor = new Color(1f,   0.3f, 0f,   0.5f); // orange = not a tree

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;
    private bool isHeld = false;
    private int currentRotationY = 0;
    private int[] cachedLayers;
    private Transform[] cachedTransforms;
    private float topSurfaceY;
    private readonly List<Color> ghostBaseColors = new List<Color>();

    void Start()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnGrab);
            shovelGrab.selectExited.AddListener(OnRelease);
            shovelGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();
        Collider surfaceCollider = GridManager.Instance.gridSurfaceRenderer.GetComponent<Collider>();
        topSurfaceY = surfaceCollider.bounds.max.y;
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
        }
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
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
        if (Mouse.current == null || Camera.main == null || ghostHighlight == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / gridSize) * gridSize,
                topSurfaceY,
                Mathf.Round(hit.point.z / gridSize) * gridSize
            );
            //ghostHighlight.transform.position = snapped;
            Vector3 offset = new Vector3(gridSize / 2f, 0f, gridSize / 2f);
            //ghostHighlight.transform.position = snapped + offset; // offset needed for path prefabs only
            ghostHighlight.transform.position = snapped; // offset needed for path prefabs only

            ghostHighlight.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            Vector2Int cell = GridManager.Instance.WorldToGrid(snapped);

            if (!GridManager.Instance.IsWithinGridSurfaceBuffered(snapped, gridSize / 2f))
                ApplyGhostBaseTransparency(0.0f);
            else if (GridManager.Instance.IsInBounds(cell) && !GridManager.Instance.IsOccupied(cell))
                ApplyGhostBaseTransparency(0.5f);
            else
                SetGhostColor(invalidColor);
        }
    }

    void RotateGhost()
    {
        currentRotationY = (currentRotationY + 90) % 360;

        if (ghostHighlight != null)
        {
            ghostHighlight.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        }

        Debug.Log("Rotated ghost to " + currentRotationY + " degrees");
    }

    GameObject CreateCenteredWrapper(GameObject prefab, string wrapperName)
    {
        GameObject wrapper = new GameObject(wrapperName);
        GameObject visual = Instantiate(prefab, wrapper.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        CenterVisualUnderRoot(wrapper.transform, visual.transform);
        return wrapper;
    }

    void CenterVisualUnderRoot(Transform root, Transform visual)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds combined = new Bounds();
        bool hasBounds = false;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            Vector3 center = bounds.center;
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

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 localPoint = worldToRoot.MultiplyPoint3x4(corners[i]);
                if (!hasBounds)
                {
                    combined = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(localPoint);
                }
            }
        }

        if (!hasBounds)
            return;

        Vector3 centerOffset = combined.center;
        visual.localPosition -= new Vector3(centerOffset.x, 0f, centerOffset.z);
    }

    void TryBuild()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        //Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position - new Vector3(gridSize / 2f, 0f, gridSize / 2f)); // reverse offset to get correct cell
        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position); 
        Debug.Log($"Trying to build at cell {cell}");
        if (GridManager.Instance.IsInBounds(cell) && !GridManager.Instance.IsOccupied(cell))
        {
            GameObject prefab = currentMode == ToolMode.Straight
            ? straightPrefab
            : cornerPrefab;
            Vector3 placementPosition = ghostHighlight.transform.position;
            Quaternion placementRotation = Quaternion.Euler(0f, currentRotationY, 0f);
            Debug.Log($"Placing path at position={placementPosition}, cell={cell}");

            GameObject placedObject = CreateCenteredWrapper(prefab, $"{prefab.name}_PlacedRoot");
            FitToSingleGridCell(placedObject);
            placedObject.transform.rotation = placementRotation;
            placedObject.transform.position = placementPosition;
            GridManager.Instance.TryPlace(cell, CellType.Path, placedObject);
        }
    }
    void SwitchMode()
    {
        currentMode = currentMode == ToolMode.Straight
            ? ToolMode.Corner
            : ToolMode.Straight;

        FindAnyObjectByType<ToolModeUI>()?.UpdateModeText();

        if (ghostHighlight != null)
            Destroy(ghostHighlight);

        CreateGhost();
        UpdateGhostPosition();
        ghostHighlight.SetActive(isHeld);

        // if (!isHeld)
        //     ghostHighlight.SetActive(false);

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

    void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null)
            return;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float maxWidthXZ = Mathf.Max(bounds.size.x, bounds.size.z);
        float cellSize = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f)
            return;

        float targetMaxWidth = cellSize * 1f;
        if (maxWidthXZ <= targetMaxWidth)
            return;

        float scaleFactor = targetMaxWidth / maxWidthXZ;
        obj.transform.localScale = obj.transform.localScale * scaleFactor;
    }

    void ScaleGhostToCell(GameObject target)
    {
        Debug.Log("GridSize: " + gridSize);
        if (target == null || !scaleGhostToGridCell || gridSize <= 0.0001f)
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

        float scale = gridSize / footprint;
        target.transform.localScale = target.transform.localScale * scale;
    }

    void ApplyGhostColour(Color color)
    {
        if (ghostHighlight == null)
            return;

        Renderer[] renderers = ghostHighlight.GetComponentsInChildren<Renderer>();
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

    void CacheGhostBaseColours()
    {
        ghostBaseColors.Clear();

        if (ghostHighlight == null)
            return;

        Renderer[] renderers = ghostHighlight.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                    continue;

                Color baseColor = mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : mat.color;
                ghostBaseColors.Add(baseColor);
            }
        }
    }

    void ApplyGhostBaseTransparency(float alpha)
    {
        if (ghostHighlight == null)
            return;

        Renderer[] renderers = ghostHighlight.GetComponentsInChildren<Renderer>();
        int colorIndex = 0;

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                    continue;

                ConfigureMaterialForTransparency(mat);

                Color baseColor = colorIndex < ghostBaseColors.Count ? ghostBaseColors[colorIndex] : mat.color;
                baseColor.a = alpha;
                SetMaterialColor(mat, baseColor);
                colorIndex++;
            }
        }
    }

    // ── ghost setup ──
    void CreateGhost()
    {
        GameObject ghostPrefab = currentMode == ToolMode.Straight ? straightPrefab : cornerPrefab;
        ghostHighlight = CreateCenteredWrapper(ghostPrefab, $"{ghostPrefab.name}_GhostRoot");

        foreach (Collider col in ghostHighlight.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        Renderer[] renderers = ghostHighlight.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        ScaleGhostToCell(ghostHighlight);
        CacheGhostBaseColours();
        ApplyGhostBaseTransparency(0.5f);

        ghostHighlight.SetActive(false);
    }

    void SetGhostColor(Color color)
    {
        ApplyGhostColour(color);
    }
}
