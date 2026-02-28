using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class ScaleAndResetOnGrab : MonoBehaviour
{
    public Transform visualMesh;
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    [Range(0.01f, 1f)]
    public float grabScaleMultiplier = 0.05f; // 25% of original size

    void Start()
    {
        originalScale = visualMesh.localScale;
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.activated.AddListener(OnGrab);
        grab.deactivated.AddListener(OnRelease);

        grab.selectEntered.AddListener(XRGrabInteractable_SelectEntered);
        grab.selectExited.AddListener(XRGrabInteractable_SelectExited);

        Debug.Log("shovel positions saved");
    }   

    void OnGrab(ActivateEventArgs eventArgs)
    {

    }

    void OnRelease(DeactivateEventArgs eventArgs)
    {

    }

    private void XRGrabInteractable_SelectEntered(SelectEnterEventArgs eventArgs)
    {
        Debug.Log("shovel grabbed");
    }

    private void XRGrabInteractable_SelectExited(SelectExitEventArgs eventArgs)
    {
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        Debug.Log("shovel released");
    }
}