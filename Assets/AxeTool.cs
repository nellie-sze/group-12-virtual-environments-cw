using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class AxeTool : MonoBehaviour
{
    [Header("Tool")]
    public XRGrabInteractable axeGrab;
    public float gridSize = 1f;

    [Header("Held Raycast")]
    public bool ignoreRaycastWhileHeld = true;

    [Header("Ghost Prefab")]
    public GameObject ghostPrefab;
    public bool scaleGhostToGridCell = true;

    [Header("Ghost Colours")]
    public Color validColor   = new Color(0f,   1f,   0f,   0.5f); // green  = hovering a tree
    public Color invalidColor = new Color(1f,   0.3f, 0f,   0.5f); // orange = not a tree

    private bool isHeld = false;
    private GameObject ghostHighlight;
    private int[] cachedLayers;
    private Transform[] cachedTransforms;

    void Start()
    {
        if (axeGrab != null)
        {
            axeGrab.selectEntered.AddListener(OnGrab);
            axeGrab.selectExited.AddListener(OnRelease);
            axeGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();
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
    void OnActivated(ActivateEventArgs args) => TryChop();

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
        if (!isHeld) return;

        // Desktop fallback: T key
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            TryChop();

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
            bool isValid = GridManager.Instance.TryGetCell(cell, out var data)
                           && (data.type == CellType.Tree || data.type == CellType.Flower);

            SetGhostColor(isValid ? validColor : invalidColor);
        }
    }

    void TryChop()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        if (GridManager.Instance.TryGetCell(cell, out var data)
            && (data.type == CellType.Tree || data.type == CellType.Flower))
            GridManager.Instance.TryRemove(cell);
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
