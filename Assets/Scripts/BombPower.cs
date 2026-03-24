using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Ubiq.Messaging;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(ToolHighlight))]
public class BombPower : MonoBehaviour
{
    [Header("Tool References")]
    public XRGrabInteractable bombGrab;

    [Header("Ghost (target indicator)")]
    public GameObject ghostPrefab;
    public bool scaleGhostToGridCell = true;
    public bool useMouseCursor = false;

    [Header("Blast Radius")]
    public int blastRadiusCells = 2;

    [Header("Fuse")]
    public float fuseTime = 1.5f;
    public float flashSpeed = 8f;

    [Header("VFX")]
    public ParticleSystem explosionPrefab;
    public float explosionYOffset = 0.1f;

    [Header("Ghost Colours")]
    private Color validColor   = new Color(1.0f, 0.4f, 0.0f, 0.45f);
    private Color invalidColor = new Color(0.4f, 0.4f, 0.4f, 0.3f);

    private bool        isHeld      = false;
    private bool        hasBeenUsed = false;
    private bool        fuseActive  = false;
    private GameObject  ghostHighlight;
    private float       topSurfaceY;
    private Collider    surfaceCollider;
    private int[]       cachedLayers;
    private Transform[] cachedTransforms;
    private NetworkContext context;

    private float gridSize => GridManager.Instance != null ? GridManager.Instance.gridSize : 0.2f;

    // Sent to all peers when the bomb is placed — they run fuse + explosion locally
    private struct BombMessage { public int cellX; public int cellY; }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m          = message.FromJson<BombMessage>();
        var centreCell = new Vector2Int(m.cellX, m.cellY);
        Vector3 bombPos = GridManager.Instance.GridToWorld(centreCell);
        bombPos.y = topSurfaceY;
        StartCoroutine(FuseAndExplode(centreCell, bombPos));
    }

    void Start()
    {
        context = NetworkScene.Register(this);

        if (bombGrab == null)
            bombGrab = GetComponent<XRGrabInteractable>();

        if (bombGrab != null)
        {
            bombGrab.selectEntered.AddListener(OnGrab);
            bombGrab.selectExited.AddListener(OnRelease);
            bombGrab.activated.AddListener(OnActivated);
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
        if (bombGrab != null)
        {
            bombGrab.selectEntered.RemoveListener(OnGrab);
            bombGrab.selectExited.RemoveListener(OnRelease);
            bombGrab.activated.RemoveListener(OnActivated);
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

    void OnActivated(ActivateEventArgs args) => TryPlaceBomb();

    void Update()
    {
        if (!isHeld) return;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            TryPlaceBomb();
        if (!fuseActive)
            UpdateGhostPosition();
    }

    void UpdateGhostPosition()
    {
        if (ghostHighlight == null) return;

        Ray ray;
        if (!useMouseCursor && GridManager.Instance != null && Camera.main != null)
        {
            Vector3 origin    = Camera.main.transform.position;
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
            if (GridManager.Instance.IsInBounds(cell)
             && GridManager.Instance.IsWithinGridSurfaceBuffered(snapped, gridSize / 2f))
                SetGhostColor(validColor);
            else
                SetGhostColor(invalidColor);
        }
        else if (ghostHighlight != null)
            SetGhostColor(new Color(1f, 1f, 1f, 0f));
    }

    void TryPlaceBomb()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;
        if (hasBeenUsed || fuseActive) return;

        Vector2Int centreCell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        if (!GridManager.Instance.IsInBounds(centreCell))
        {
            Debug.Log("[Bomb] Target cell out of bounds.");
            return;
        }

        hasBeenUsed = true;
        fuseActive  = true;

        if (ghostHighlight != null) ghostHighlight.SetActive(false);

        Vector3 bombPos = GridManager.Instance.GridToWorld(centreCell);
        bombPos.y = topSurfaceY;
        transform.position = bombPos;

        // Tell all peers to run fuse + explosion at this cell
        context.SendJson(new BombMessage { cellX = centreCell.x, cellY = centreCell.y });

        StartCoroutine(FuseAndExplode(centreCell, bombPos));
    }

    IEnumerator FuseAndExplode(Vector2Int centreCell, Vector3 explosionPos)
    {
        Debug.Log($"[Bomb] Fuse lit at {centreCell} — exploding in {fuseTime}s.");

        var renderers  = GetComponentsInChildren<Renderer>();
        var mats       = new List<Material>();
        var origColors = new List<Color>();

        foreach (var r in renderers)
            foreach (var m in r.materials)
            {
                m.EnableKeyword("_EMISSION");
                mats.Add(m);
                origColors.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
            }

        float fuseElapsed = 0f;
        while (fuseElapsed < fuseTime)
        {
            fuseElapsed += Time.deltaTime;
            float progress     = fuseElapsed / fuseTime;
            float currentSpeed = flashSpeed * (1f + progress * 3f);
            float pulse        = (Mathf.Sin(fuseElapsed * currentSpeed) + 1f) * 0.5f;
            Color flash        = Color.Lerp(Color.black, new Color(4f, 0.5f, 0f), pulse);

            foreach (var mat in mats)
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", flash);

            yield return null;
        }

        for (int i = 0; i < mats.Count; i++)
            if (mats[i].HasProperty("_EmissionColor"))
                mats[i].SetColor("_EmissionColor", Color.black);

        Explode(centreCell, explosionPos);
    }

    void Explode(Vector2Int centreCell, Vector3 explosionPos)
    {
        var cellsToDestroy = new List<Vector2Int>();

        for (int dx = -blastRadiusCells; dx <= blastRadiusCells; dx++)
        {
            for (int dz = -blastRadiusCells; dz <= blastRadiusCells; dz++)
            {
                Vector2Int cell = new Vector2Int(centreCell.x + dx, centreCell.y + dz);
                if (!GridManager.Instance.IsInBounds(cell)) continue;
                if (!GridManager.Instance.TryGetCell(cell, out var data)) continue;
                if (data.type == CellType.Tree || data.type == CellType.Rock || data.type == CellType.Flower)
                    cellsToDestroy.Add(cell);
            }
        }

        Debug.Log($"[Bomb] Exploding at {centreCell} — destroying {cellsToDestroy.Count} obstacles.");

        foreach (Vector2Int cell in cellsToDestroy)
        {
            if (ObstacleSpawner.Instance != null)
                ObstacleSpawner.Instance.RequestRemove(cell);
            else
                GridManager.Instance.TryRemove(cell);
        }

        if (explosionPrefab != null)
        {
            Vector3 spawnPos = explosionPos + new Vector3(0f, explosionYOffset, 0f);
            ParticleSystem ps = Instantiate(explosionPrefab, spawnPos, Quaternion.identity);
            float blastWorldSize = (blastRadiusCells * 2 + 1) * gridSize;
            ps.transform.localScale = Vector3.one * blastWorldSize;
            ps.Play();
            Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax + 0.5f);
        }

        AudioManager.Instance?.PlayBombSound(explosionPos);
        StartCoroutine(FadeOutAndDestroy());
    }

    IEnumerator FadeOutAndDestroy()
    {
        yield return new WaitForSeconds(0.2f);

        var renderers = GetComponentsInChildren<Renderer>();
        var materials = new List<Material>();
        foreach (var r in renderers)
            foreach (var m in r.materials)
            {
                ConfigureMaterialForTransparency(m);
                materials.Add(m);
            }

        float elapsed = 0f;
        float fadeDur = 0.3f;
        while (elapsed < fadeDur)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDur);
            foreach (var mat in materials)
            {
                if (mat.HasProperty("_BaseColor")) { var c = mat.GetColor("_BaseColor"); c.a = alpha; mat.SetColor("_BaseColor", c); }
                if (mat.HasProperty("_Color"))     { var c = mat.GetColor("_Color");     c.a = alpha; mat.SetColor("_Color",     c); }
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
            cachedLayers     = new int[cachedTransforms.Length];
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

        float blastWorldSize = (blastRadiusCells * 2 + 1) * gridSize;
        ghostHighlight.transform.localScale = new Vector3(blastWorldSize, 0.01f, blastWorldSize);

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
}
