using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

public class GridSystem : MonoBehaviour
{
    public GameObject objectToPlace;
    public float gridSize = 1f;
    private GameObject ghostObject;
    private HashSet<Vector3> occupiedPositions = new HashSet<Vector3>();
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable shovelGrab; 
    private bool isShovelHeld = false;    // tracks if the shovel is grabbed

    void CreateGhostObject()
    {
        ghostObject = Instantiate(objectToPlace);
        ghostObject.GetComponent<Collider>().enabled = false;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material mat = renderer.material;
            Color color = mat.color;
            color.a = 0.5f;
            mat.color = color;

            mat.SetFloat("_Mode",2);
            mat.SetInt("_ScrBlend",(int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
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
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        
        if(Physics.Raycast(ray, out RaycastHit hit))
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
                    Mathf.Round(point.x/gridSize)*gridSize,
                    Mathf.Round(point.y/gridSize)*gridSize,
                    Mathf.Round(point.z/gridSize)*gridSize
                );
                ghostObject.transform.position = snappedPosition;

                if(occupiedPositions.Contains(snappedPosition))
                    SetGhostColor(Color.red);
                else
                    SetGhostColor(new Color(1f, 1f, 1f, 0.5f));
                }
        }
    }

    void SetGhostColor(Color color)
    {
        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();
        
        foreach (Renderer renderer in renderers)
        {
            Material mat = renderer.material;
            mat.color = color;
        }
    }

    void PlaceObject()
    {
        Vector3 placementPosition = ghostObject.transform.position;

        if (!occupiedPositions.Contains(placementPosition))
        {
            Instantiate(objectToPlace, placementPosition, Quaternion.identity);
            occupiedPositions.Add(placementPosition);
        }
    }

    private void OnShovelGrab(SelectEnterEventArgs args)
    {
        isShovelHeld = true;
        Debug.Log("grid detects shovel held");
        ghostObject.SetActive(true);
    }

    private void OnShovelRelease(SelectExitEventArgs args)
    {
        isShovelHeld = false;
        Debug.Log("grid detects shovel dropped");
        ghostObject.SetActive(false);
    }
    private void OnGrabActivated(ActivateEventArgs args)
    {
        Debug.Log("placing object");
        PlaceObject(); // called when trigger is pressed while grabbing
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnShovelGrab);
            shovelGrab.selectExited.AddListener(OnShovelRelease);
            shovelGrab.activated.AddListener(OnGrabActivated);
        }
        CreateGhostObject();
        ghostObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (isShovelHeld)
        {
            UpdateGhostPosition();
        }
    }
}
