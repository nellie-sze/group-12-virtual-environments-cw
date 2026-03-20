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

    [Header("Ghost Prefab")]
    public GameObject ghostPrefab;
    private GameObject ghostHighlight;
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

    void Start()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnGrab);
            shovelGrab.selectExited.AddListener(OnRelease);
            shovelGrab.activated.AddListener(OnActivated);
        }

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
        if (Mouse.current == null || Camera.main == null || ghostHighlight == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / gridSize) * gridSize,
                Mathf.Round(hit.point.y / gridSize) * gridSize,
                Mathf.Round(hit.point.z / gridSize) * gridSize
            );

            ghostHighlight.transform.position = snapped;

            Vector2Int cell = GridManager.Instance.WorldToGrid(hit.point);
            bool isValid = GridManager.Instance.IsInBounds(cell) && !GridManager.Instance.IsOccupied(cell)
                        && GridManager.Instance.IsWithinGridSurfaceBuffered(ghostHighlight.transform.position, gridSize / 2f);

            SetGhostColor(isValid ? validColor : invalidColor);
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

    void TryBuild()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        if (GridManager.Instance.IsInBounds(cell) && !GridManager.Instance.IsOccupied(cell))
        {
            GameObject prefab = straightPrefab;
            Vector3 placementPosition = ghostHighlight.transform.position;
            Quaternion placementRotation = Quaternion.Euler(0f, 0f, 0f);
            Debug.Log($"Placing at y={placementPosition.y} from ghost y={ghostHighlight.transform.position.y}");
            GameObject placedObject = Instantiate(prefab, placementPosition, placementRotation);
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

    // ── ghost setup ──
    void CreateGhost()
    {
        ghostHighlight = ghostPrefab != null
            ? Instantiate(ghostPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        ghostHighlight.transform.localScale = Vector3.one; // start neutral; ScaleGhostToCell handles sizing

        foreach (Collider col in ghostHighlight.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        ScaleGhostToCell(ghostHighlight);
        ApplyGhostColour(invalidColor);

        ghostHighlight.SetActive(false);
    }

    void SetGhostColor(Color color)
    {
        ApplyGhostColour(color);
    }
}
