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

    [Header("Ghost Colours")]
    public Color validColor   = new Color(0f,   1f,   0f,   0.5f); // green  = hovering a tree
    public Color invalidColor = new Color(1f,   0.3f, 0f,   0.5f); // orange = not a tree

    private bool isHeld = false;
    private GameObject ghostHighlight;

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

    void OnGrab(SelectEnterEventArgs args)   { isHeld = true;  ghostHighlight.SetActive(true); }
    void OnRelease(SelectExitEventArgs args) { isHeld = false; ghostHighlight.SetActive(false); }
    void OnActivated(ActivateEventArgs args) => TryChop();

    void Update()
    {
        if (!isHeld) return;

        // Desktop fallback: C key
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
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

    // ── ghost setup: same transparency trick as GridSystem.CreateGhostObject ──
    void CreateGhost()
    {
        ghostHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghostHighlight.transform.localScale = Vector3.one * gridSize;

        // Disable collider so it doesn't interfere with raycasts
        Destroy(ghostHighlight.GetComponent<Collider>());

        // Make semi-transparent (same material settings as GridSystem)
        Renderer r = ghostHighlight.GetComponent<Renderer>();
        Material mat = r.material;
        mat.SetFloat("_Mode", 2);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = invalidColor;

        ghostHighlight.SetActive(false);
    }

    void SetGhostColor(Color color)
    {
        if (ghostHighlight == null) return;
        ghostHighlight.GetComponent<Renderer>().material.color = color;
    }
}
