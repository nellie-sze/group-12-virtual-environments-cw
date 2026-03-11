using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

public class GridSystemNew : MonoBehaviour
{
    public enum ToolMode
    {
        Straight,
        Corner
    }

    [Header("Placement")]
    public GameObject objectToPlace;
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
    private HashSet<Vector3> occupiedPositions = new HashSet<Vector3>();

    void CreateGhostObject()
    {
        ghostObject = Instantiate(objectToPlace);

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
        if (Mouse.current == null)
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

                if (occupiedPositions.Contains(snappedPosition))
                    SetGhostColor(invalidGhostColor);
                else
                    UpdateGhostVisual();
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

    void PlaceObject()
    {
        Vector3 placementPosition = ghostObject.transform.position;

        if (occupiedPositions.Contains(placementPosition))
        {
            Debug.Log("Cannot place here: already occupied.");
            return;
        }

        GameObject placedObject = Instantiate(objectToPlace, placementPosition, Quaternion.identity);
        ApplyPlacedModeColour(placedObject);

        occupiedPositions.Add(placementPosition);
        Debug.Log("Placed object in mode: " + currentMode);
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
        ghostObject.SetActive(true);
        UpdateGhostVisual();
    }

    private void OnShovelRelease(SelectExitEventArgs args)
    {
        isShovelHeld = false;
        Debug.Log("Grid detects shovel dropped");
        ghostObject.SetActive(false);
    }

    private void OnGrabActivated(ActivateEventArgs args)
    {
        Debug.Log("Placing object");
        PlaceObject();
    }

        void Start()
    {
        Debug.Log("GridSystem Start - setting up shovel listeners");
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
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            ToggleMode();
        }

        UpdateGhostPosition();
    }

    void ToggleMode()
    {
        if (currentMode == ToolMode.Straight)
            currentMode = ToolMode.Corner;
        else
            currentMode = ToolMode.Straight;

        Debug.Log("Switched mode to: " + currentMode);
        UpdateGhostVisual();
    }
}