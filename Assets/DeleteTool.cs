using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class DeleteTool : MonoBehaviour
{
    [Header("Tool")]
    public XRGrabInteractable deleteGrab;
    public float gridSize = 1f;

    [Header("Ghost Colours")]
    public Color validColor   = new Color(0f, 1f,  0f,  0.5f); // green = hovering a path tile
    public Color invalidColor = new Color(1f, 0f,  0f,  0.5f); // red   = not a path tile

    private bool isHeld = false;
    private GameObject ghostHighlight;

    void Start()
    {
        if (deleteGrab != null)
        {
            deleteGrab.selectEntered.AddListener(OnGrab);
            deleteGrab.selectExited.AddListener(OnRelease);
            deleteGrab.activated.AddListener(OnActivated);
        }

        CreateGhost();
    }

    void OnDestroy()
    {
        if (deleteGrab != null)
        {
            deleteGrab.selectEntered.RemoveListener(OnGrab);
            deleteGrab.selectExited.RemoveListener(OnRelease);
            deleteGrab.activated.RemoveListener(OnActivated);
        }
        if (ghostHighlight != null) Destroy(ghostHighlight);
    }

    void OnGrab(SelectEnterEventArgs args)   { isHeld = true;  ghostHighlight.SetActive(true); }
    void OnRelease(SelectExitEventArgs args) { isHeld = false; ghostHighlight.SetActive(false); }
    void OnActivated(ActivateEventArgs args) => TryDelete();

    void Update()
    {
        if (!isHeld) return;

        // Desktop fallback: D key
        if (Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame)
            TryDelete();

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
            bool isPath = GridManager.Instance.TryGetCell(cell, out var data)
                          && data.type == CellType.Path;

            SetGhostColor(isPath ? validColor : invalidColor);
        }
    }

    void TryDelete()
    {
        if (ghostHighlight == null || !ghostHighlight.activeSelf) return;

        Vector2Int cell = GridManager.Instance.WorldToGrid(ghostHighlight.transform.position);
        if (GridManager.Instance.TryGetCell(cell, out var data) && data.type == CellType.Path)
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
