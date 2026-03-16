using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class PickaxeTool : MonoBehaviour
{
    [Header("Tool")]
    public XRGrabInteractable pickaxeGrab;
    public float gridSize = 1f;

    [Header("Ghost Colours")]
    public Color validColor   = new Color(0f,   1f,   0f,   0.5f); // green = hovering a rock
    public Color invalidColor = new Color(0.4f, 0.4f, 1f,   0.5f); // blue  = not a rock

    private bool isHeld = false;
    private GameObject ghostHighlight;

    void Start()
    {
        if (pickaxeGrab != null)
        {
            pickaxeGrab.selectEntered.AddListener(OnGrab);
            pickaxeGrab.selectExited.AddListener(OnRelease);
            pickaxeGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();
    }

    void OnDestroy()
    {
        if (pickaxeGrab != null)
        {
            pickaxeGrab.selectEntered.RemoveListener(OnGrab);
            pickaxeGrab.selectExited.RemoveListener(OnRelease);
            pickaxeGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostHighlight != null) Destroy(ghostHighlight);
    }

    void OnGrab(SelectEnterEventArgs args)   { isHeld = true;  ghostHighlight.SetActive(true); }
    void OnRelease(SelectExitEventArgs args) { isHeld = false; ghostHighlight.SetActive(false); }
    void OnActivated(ActivateEventArgs args) => TryBreak();

    void Update()
    {
        if (!isHeld) return;

        // Desktop fallback: X key
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
            TryBreak();

        UpdateGhostPosition();
    }

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
            bool isRock = GridManager.Instance.TryGetCell(cell, out var data)
                          && data.type == CellType.Rock;

            SetGhostColor(isRock ? validColor : invalidColor);
        }
    }

    void TryBreak()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        if (GridManager.Instance.TryGetCell(cell, out var data) && data.type == CellType.Rock)
            GridManager.Instance.TryRemove(cell);
    }

    void CreateGhost()
    {
        ghostHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghostHighlight.transform.localScale = Vector3.one * gridSize;

        Destroy(ghostHighlight.GetComponent<Collider>());

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
