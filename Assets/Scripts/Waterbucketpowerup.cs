using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Ubiq.Messaging;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(ToolHighlight))]

public class WaterBucketPowerup : MonoBehaviour
{
    [Header("Tool References")]
    public XRGrabInteractable bucketGrab;

    [Header("Ghost (target indicator)")]
    public GameObject ghostPrefab;
    public bool scaleGhostToGridCell = true;
    public bool useMouseCursor = false;

    [Header("Ghost Colours")]
    private Color validColor = new Color(0.0f, 0.5f, 1.0f, 0.55f);
    private Color invalidColor = new Color(1.0f, 0.3f, 0.0f, 0.45f);

    [Header("VFX")]
    public ParticleSystem splashPrefab;
    public float splashYOffset = 0.1f;

    [Header("Audio")]
    public AudioClip splashClip;
    [Range(0f, 1f)]
    public float splashVolume = 0.85f;

    private bool isHeld = false;
    private bool hasBeenUsed = false;
    private GameObject ghostHighlight;
    private float topSurfaceY;
    private Collider surfaceCollider;
    private int[] cachedLayers;
    private Transform[] cachedTransforms;
    private NetworkContext context;

    private float gridSize => GridManager.Instance != null ? GridManager.Instance.gridSize : 0.2f;

    // Sent to all peers so they see the splash particle at the correct position
    private struct SplashMessage { public float x; public float y; public float z; }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<SplashMessage>();
        var worldPos = new Vector3(m.x, m.y, m.z);
        SpawnSplash(worldPos);

        // Mirror the local consume flow on remote peers so the bucket
        // disappears everywhere after a successful use.
        if (!hasBeenUsed)
        {
            hasBeenUsed = true;
            StartCoroutine(FadeOutAndDestroy());
        }
    }

    void Start()
    {
        context = NetworkScene.Register(this);

        if (bucketGrab == null)
            bucketGrab = GetComponent<XRGrabInteractable>();

        if (bucketGrab != null)
        {
            bucketGrab.selectEntered.AddListener(OnGrab);
            bucketGrab.selectExited.AddListener(OnRelease);
            bucketGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();

        if (GridManager.Instance != null && GridManager.Instance.gridSurfaceRenderer != null)
        {
            surfaceCollider = GridManager.Instance.gridSurfaceRenderer.GetComponent<Collider>();
            if (surfaceCollider != null)
                topSurfaceY = surfaceCollider.bounds.max.y;
        }
    }

    void OnDestroy()
    {
        if (bucketGrab != null)
        {
            bucketGrab.selectEntered.RemoveListener(OnGrab);
            bucketGrab.selectExited.RemoveListener(OnRelease);
            bucketGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostHighlight != null) Destroy(ghostHighlight);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        SetHeldRaycastIgnored(true);
        if (ghostHighlight != null) ghostHighlight.SetActive(true);
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        SetHeldRaycastIgnored(false);
        if (ghostHighlight != null) ghostHighlight.SetActive(false);
    }

    void OnActivated(ActivateEventArgs args) => TryDouseLava();

    void Update()
    {
        if (!isHeld) return;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            TryDouseLava();
        UpdateGhostPosition();
    }

    void UpdateGhostPosition()
    {
        if (ghostHighlight == null) return;

        Ray ray;
        if (!useMouseCursor && GridManager.Instance != null && Camera.main != null)
        {
            Vector3 origin = Camera.main.transform.position;
            Vector3 direction = (transform.position - origin).normalized;
            if (direction.sqrMagnitude < 0.0001f) return;
            ray = new Ray(origin, direction);
        }
        else if (Mouse.current != null && Camera.main != null)
            ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        else return;

        if (surfaceCollider != null && surfaceCollider.Raycast(ray, out RaycastHit hit, 100f))
        {
            Vector3 snapped = new Vector3(
                Mathf.Round(hit.point.x / gridSize) * gridSize,
                topSurfaceY,
                Mathf.Round(hit.point.z / gridSize) * gridSize);

            ghostHighlight.transform.position = snapped;

            Vector2Int cell = GridManager.Instance.WorldToGrid(snapped);

            if (!GridManager.Instance.IsWithinGridSurfaceBuffered(snapped, gridSize / 2f))
                SetGhostColor(new Color(1f, 1f, 1f, 0f));
            else if (GridManager.Instance.IsInBounds(cell)
                  && GridManager.Instance.TryGetCell(cell, out var data)
                  && data.type == CellType.Lava)
                SetGhostColor(validColor);
            else
                SetGhostColor(invalidColor);
        }
        else if (ghostHighlight != null)
            SetGhostColor(new Color(1f, 1f, 1f, 0f));
    }

    void TryDouseLava()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;
        if (hasBeenUsed) return;

        Vector2Int targetCell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);

        if (!GridManager.Instance.TryGetCell(targetCell, out var data) || data.type != CellType.Lava)
        {
            Debug.Log("[WaterBucket] No lava at target cell.");
            return;
        }

        hasBeenUsed = true;

        // BFS flood-fill to find all connected lava cells
        var connectedLavaCells = new System.Collections.Generic.List<Vector2Int>();
        var visited = new System.Collections.Generic.HashSet<Vector2Int>();
        var queue = new System.Collections.Generic.Queue<Vector2Int>();

        visited.Add(targetCell);
        queue.Enqueue(targetCell);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            connectedLavaCells.Add(current);

            foreach (Vector2Int dir in PathDirections.All)
            {
                Vector2Int neighbour = current + dir;
                if (visited.Contains(neighbour)) continue;
                visited.Add(neighbour);
                if (GridManager.Instance.TryGetCell(neighbour, out var neighbourData)
                    && neighbourData.type == CellType.Lava)
                    queue.Enqueue(neighbour);
            }
        }

        Debug.Log($"[WaterBucket] Dousing {connectedLavaCells.Count} connected lava cells.");

        Vector3 centreWorldPos = Vector3.zero;
        foreach (Vector2Int cell in connectedLavaCells)
        {
            centreWorldPos += GridManager.Instance.GridToWorld(cell);

            if (LavaSpawner.Instance != null)
                LavaSpawner.Instance.RequestRemove(cell);
            else
            {
                GridManager.Instance.TryRemove(cell);
                Debug.LogWarning("[WaterBucket] LavaSpawner not found — removed lava locally only.");
            }
        }

        centreWorldPos /= connectedLavaCells.Count;

        // Tell all peers to spawn the splash particle at the centre of the patch
        context.SendJson(new SplashMessage { x = centreWorldPos.x, y = centreWorldPos.y, z = centreWorldPos.z });

        // Also spawn locally
        SpawnSplash(centreWorldPos);

        AudioManager.Instance?.PlayWaterBucketSound(centreWorldPos);

        Debug.Log($"[WaterBucket] Lava patch doused — {connectedLavaCells.Count} cells removed.");

        StartCoroutine(FadeOutAndDestroy());
    }

    // Spawns the splash particle — called locally and by remote peers via ProcessMessage
    void SpawnSplash(Vector3 centreWorldPos)
    {
        if (splashPrefab != null)
        {
            ParticleSystem ps = Instantiate(splashPrefab,
                centreWorldPos + new Vector3(0f, splashYOffset, 0f),
                Quaternion.identity);
            ps.Play();
            Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax + 0.5f);
        }
    }

    IEnumerator FadeOutAndDestroy()
    {
        if (ghostHighlight != null) ghostHighlight.SetActive(false);

        yield return new WaitForSeconds(0.3f);

        var renderers = GetComponentsInChildren<Renderer>();
        var materials = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers)
            foreach (var m in r.materials)
            {
                ConfigureMaterialForTransparency(m);
                materials.Add(m);
            }

        float elapsed = 0f;
        float fadeDur = 1.0f;
        while (elapsed < fadeDur)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDur);
            foreach (var mat in materials)
            {
                if (mat.HasProperty("_BaseColor")) { var c = mat.GetColor("_BaseColor"); c.a = alpha; mat.SetColor("_BaseColor", c); }
                if (mat.HasProperty("_Color")) { var c = mat.GetColor("_Color"); c.a = alpha; mat.SetColor("_Color", c); }
                mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, alpha);
            }
            yield return null;
        }

        Destroy(gameObject);
    }

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

    void CreateGhost()
    {
        ghostHighlight = ghostPrefab != null
            ? Instantiate(ghostPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        foreach (Collider col in ghostHighlight.GetComponentsInChildren<Collider>())
            col.enabled = false;

        ScaleGhostToCell(ghostHighlight);
        SetGhostColor(invalidColor);
        ghostHighlight.SetActive(false);
    }

    void SetGhostColor(Color color)
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
}
