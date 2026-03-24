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
public class IcePowerup : MonoBehaviour
{
    [Header("Tool References")]
    public XRGrabInteractable iceGrab;

    [Header("Ghost (target indicator)")]
    public GameObject ghostPrefab;
    public bool scaleGhostToGridCell = true;
    public bool useMouseCursor = false;

    [Header("Freeze Settings")]
    public float freezeDuration = 20f;

    [Header("Ghost Colours")]
    private Color validColor   = new Color(0.3f, 0.8f, 1.0f, 0.55f);
    private Color invalidColor = new Color(0.4f, 0.4f, 0.4f, 0.3f);

    private bool        isHeld      = false;
    private bool        hasBeenUsed = false;
    private GameObject  ghostHighlight;
    private float       topSurfaceY;
    private Collider    surfaceCollider;
    private int[]       cachedLayers;
    private Transform[] cachedTransforms;
    private NetworkContext context;

    private float gridSize => GridManager.Instance != null ? GridManager.Instance.gridSize : 0.2f;

    // Sent to all peers so they also freeze villagers
    private struct IceMessage { public float duration; }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<IceMessage>();
        VillagerAgent.FreezeAll(m.duration);
    }

    void Start()
    {
        context = NetworkScene.Register(this);

        if (iceGrab == null)
            iceGrab = GetComponent<XRGrabInteractable>();

        if (iceGrab != null)
        {
            iceGrab.selectEntered.AddListener(OnGrab);
            iceGrab.selectExited.AddListener(OnRelease);
            iceGrab.activated.AddListener(OnActivated);
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
        if (iceGrab != null)
        {
            iceGrab.selectEntered.RemoveListener(OnGrab);
            iceGrab.selectExited.RemoveListener(OnRelease);
            iceGrab.activated.RemoveListener(OnActivated);
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

    void OnActivated(ActivateEventArgs args) => TryActivateIce();

    void Update()
    {
        if (!isHeld) return;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            TryActivateIce();
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

    void TryActivateIce()
    {
        if (hasBeenUsed) return;

        hasBeenUsed = true;

        if (ghostHighlight != null) ghostHighlight.SetActive(false);

        // Freeze all villagers locally
        VillagerAgent.FreezeAll(freezeDuration);

        // Tell all peers to freeze
        context.SendJson(new IceMessage { duration = freezeDuration });

        AudioManager.Instance?.PlayIceSound(transform.position);

        Debug.Log($"[Ice] Activated — all villagers frozen for {freezeDuration}s.");

        StartCoroutine(FadeOutAndDestroy());
    }

    IEnumerator FadeOutAndDestroy()
    {
        yield return new WaitForSeconds(0.3f);

        var renderers = GetComponentsInChildren<Renderer>();
        var materials = new List<Material>();
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

        ghostHighlight.transform.localScale = new Vector3(gridSize, 0.01f, gridSize);

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
