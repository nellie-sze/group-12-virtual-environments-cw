using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;

public class GridSystem : MonoBehaviour
{
    public enum ToolMode
    {
        Straight,
        Corner
    }

    [Header("Placement")]
    // public GameObject objectToPlace;
    public float gridSize = 1f;

    [Header("Tool")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable shovelGrab;
    private bool isShovelHeld = false;

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;

    [Header("Mode Colours")]
    public Color straightGhostColor = new Color(0f, 0.6f, 1f, 0.5f);
    public Color cornerGhostColor = new Color(0f, 1f, 0.3f, 0.5f);
    public Color invalidGhostColor = new Color(1f, 0f, 0f, 0.5f);

    public Color straightPlacedColor = new Color(0f, 0.6f, 1f, 1f);
    public Color cornerPlacedColor = new Color(0f, 1f, 0.3f, 1f);

    private GameObject ghostObject;
    // occupiedPositions removed — GridManager.Instance is now the single source of truth
    // 0, 90, 180, 270
    private int currentRotationY = 0;

    [Header("Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    void CreateGhostObject()
    {
        GameObject prefab = currentMode == ToolMode.Straight
        ? straightPrefab
        : cornerPrefab;

        ghostObject = Instantiate(prefab);
        ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

        ghostObject.GetComponent<Collider>().enabled = false;

        foreach (Collider col in ghostObject.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material mat = renderer.material;
            Color color = mat.color;
            color.a = 0.5f;
            mat.color = color;

            mat.SetFloat("_Mode", 2);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    bool IsWithinGrid(Vector3 pos)
    {
        Renderer r = GetComponent<Renderer>();
        Bounds bounds = r.bounds;

        return pos.x >= bounds.min.x &&
               pos.x <= bounds.max.x &&
               pos.z >= bounds.min.z &&
               pos.z <= bounds.max.z;
    }

    void UpdateGhostPosition()
    {
        if (Mouse.current == null || Camera.main == null || ghostObject == null)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 point = hit.point;
            float radius = 0.5f;

            if (IsWithinGrid(point) &&
                IsWithinGrid(point + new Vector3(radius, 0.0f, 0.0f)) &&
                IsWithinGrid(point + new Vector3(0.0f, 0.0f, radius)) &&
                IsWithinGrid(point + new Vector3(-radius, 0.0f, 0.0f)) &&
                IsWithinGrid(point + new Vector3(0.0f, 0.0f, -radius)))
            {
                Vector3 snappedPosition = new Vector3(
                    Mathf.Round(point.x / gridSize) * gridSize,
                    Mathf.Round(point.y / gridSize) * gridSize,
                    Mathf.Round(point.z / gridSize) * gridSize
                );

                ghostObject.transform.position = snappedPosition;
                ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

                if (CanPlaceGhost())
                    UpdateGhostVisual();
                else
                    SetGhostColor(invalidGhostColor);

                // if (occupiedPositions.Contains(snappedPosition))
                //     SetGhostColor(invalidGhostColor);
                // else
                //     UpdateGhostVisual();
            }
        }
    }

    void UpdateGhostVisual()
    {
        if (ghostObject == null)
            return;

        switch (currentMode)
        {
            case ToolMode.Straight:
                SetGhostColor(straightGhostColor);
                break;

            case ToolMode.Corner:
                SetGhostColor(cornerGhostColor);
                break;
        }
    }

    void SetGhostColor(Color color)
    {
        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            renderer.material.color = color;
        }
    }

    // Gets every grid cell occupied by the current ghost
    List<Vector3> GetGhostOccupiedCells()
    {
        List<Vector3> cells = new List<Vector3>();

        if (ghostObject == null)
            return cells;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;

            Vector3 snapped = new Vector3(
                Mathf.Round(p.x / gridSize) * gridSize,
                Mathf.Round(p.y / gridSize) * gridSize,
                Mathf.Round(p.z / gridSize) * gridSize
            );

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        // Fallback if prefab has no child renderers except root
        if (cells.Count == 0)
        {
            Vector3 rootPos = new Vector3(
                Mathf.Round(ghostObject.transform.position.x / gridSize) * gridSize,
                Mathf.Round(ghostObject.transform.position.y / gridSize) * gridSize,
                Mathf.Round(ghostObject.transform.position.z / gridSize) * gridSize
            );
            cells.Add(rootPos);
        }

        return cells;
    }

    bool CanPlaceGhost()
    {
        List<Vector3> cells = GetGhostOccupiedCells();

        foreach (Vector3 cell in cells)
        {
            if (!IsWithinGrid(cell))
                return false;

            if (GridManager.Instance != null && GridManager.Instance.IsOccupied(GridManager.Instance.WorldToGrid(cell)))
                return false;
        }

        return true;
    }


    void PlaceObject()
    {
        if (ghostObject == null)
            return;

        if (!CanPlaceGhost()) // if (occupiedPositions.Contains(placementPosition))
        {
            Debug.Log("Cannot place here: already occupied.");
            return;
        }

        Vector3 placementPosition = ghostObject.transform.position;
        Quaternion placementRotation = Quaternion.Euler(0f, currentRotationY, 0f);

        GameObject prefab = currentMode == ToolMode.Straight
        ? straightPrefab
        : cornerPrefab;

        
        GameObject placedObject = Instantiate(prefab, placementPosition, placementRotation);
        //ApplyPlacedModeColour(placedObject);

        List<Vector3> cellPositions = GetCellsForPlacedObject(placedObject);
        foreach (Vector3 cellPos in cellPositions)
        {
            GridManager.Instance.TryPlace(GridManager.Instance.WorldToGrid(cellPos), CellType.Path, placedObject);
        }
        Debug.Log("Placed object in mode: " + currentMode);
    }

    List<Vector3> GetCellsForPlacedObject(GameObject placedObject)
    {
        List<Vector3> cells = new List<Vector3>();

        Renderer[] renderers = placedObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;

            Vector3 snapped = new Vector3(
                Mathf.Round(p.x / gridSize) * gridSize,
                Mathf.Round(p.y / gridSize) * gridSize,
                Mathf.Round(p.z / gridSize) * gridSize
            );

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        if (cells.Count == 0)
        {
            Vector3 rootPos = new Vector3(
                Mathf.Round(placedObject.transform.position.x / gridSize) * gridSize,
                Mathf.Round(placedObject.transform.position.y / gridSize) * gridSize,
                Mathf.Round(placedObject.transform.position.z / gridSize) * gridSize
            );
            cells.Add(rootPos);
        }

        return cells;
    }

    void ApplyPlacedModeColour(GameObject placedObject)
    {
        Color placedColor = straightPlacedColor;

        switch (currentMode)
        {
            case ToolMode.Straight:
                placedColor = straightPlacedColor;
                break;

            case ToolMode.Corner:
                placedColor = cornerPlacedColor;
                break;
        }

        Renderer[] renderers = placedObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.material.color = placedColor;
        }
    }

    private void OnShovelGrab(SelectEnterEventArgs args)
    {
        isShovelHeld = true;
        Debug.Log("Grid detects shovel held");
        if (ghostObject != null)
        {
            ghostObject.SetActive(true);
            UpdateGhostVisual();
        }
    }
    private void OnShovelRelease(SelectExitEventArgs args)
    {
        isShovelHeld = false;
        Debug.Log("Grid detects shovel dropped");
        if (ghostObject != null)
        {
            ghostObject.SetActive(false);
        }
    }

    private void OnGrabActivated(ActivateEventArgs args)
    {
        Debug.Log("Placing object");
        PlaceObject();
    }

        void Start()
    {
        Debug.Log("GridSystem Start");
        if (straightPrefab == null)
        {
            Debug.LogError("GridSystem: straightPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (cornerPrefab == null)
        {
            Debug.LogError("GridSystem: cornerPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnShovelGrab);
            shovelGrab.selectExited.AddListener(OnShovelRelease);
            shovelGrab.activated.AddListener(OnGrabActivated);
        }

        CreateGhostObject();
        ghostObject.SetActive(false);
        UpdateGhostVisual();
    }

    void OnDestroy()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.RemoveListener(OnShovelGrab);
            shovelGrab.selectExited.RemoveListener(OnShovelRelease);
            shovelGrab.activated.RemoveListener(OnGrabActivated);
        }
    }

    void Update()
    {
        if (!isShovelHeld)
            return;

        // Desktop prototype mode switching
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            SwitchMode();
            // ToggleMode();
        }
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
        {
            Debug.Log("Placing object");
            PlaceObject();
        }
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            RotateGhost();
        }

        UpdateGhostPosition();
    }

    void RotateGhost()
    {
        currentRotationY = (currentRotationY + 90) % 360;

        if (ghostObject != null)
        {
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            if (CanPlaceGhost())
                UpdateGhostVisual();
            else
                SetGhostColor(invalidGhostColor);
        }

        Debug.Log("Rotated ghost to " + currentRotationY + " degrees");
    }

    void SwitchMode()
    {
        currentMode = currentMode == ToolMode.Straight
            ? ToolMode.Corner
            : ToolMode.Straight;

        FindAnyObjectByType<ToolModeUI>()?.UpdateModeText();

        if (ghostObject != null)
            Destroy(ghostObject);

        CreateGhostObject();
        UpdateGhostPosition();
        ghostObject.SetActive(isShovelHeld);

        // if (!isShovelHeld)
        //     ghostObject.SetActive(false);

        Debug.Log("Switched mode to " + currentMode);
    }

    private void SwitchMode(InputAction.CallbackContext ctx)
    {
        SwitchMode();
    }
}