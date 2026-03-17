using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Ubiq.Messaging;
using Ubiq.Geometry;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class ScaleAndResetOnGrab : MonoBehaviour
{
    public Transform visualMesh;
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    [Range(0.01f, 1f)]
    public float grabScaleMultiplier = 0.1f; 

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
    private Rigidbody body;

    // Networking
    private NetworkContext context;
    private Pose lastPose;
    private Vector3 lastScale;
    private bool forceSend;

    private struct Message
    {
        public Pose pose;
        public Vector3 scale;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();
        var pose = Transforms.ToWorld(m.pose, context.Scene.transform);

        if (body)
        {
            body.position = pose.position;
            body.rotation = pose.rotation;
        }
        else
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        transform.localScale = m.scale;

        // Prevent echoing received updates.
        lastPose = m.pose;
        lastScale = m.scale;
    }

    private void SendNetworkUpdates()
    {
        if (!grab)
        {
            return;
        }

        // Only the peer currently grabbing this object should publish motion.
        // Also allow one send right after release/scale changes via forceSend.
        if (!grab.isSelected && !forceSend)
        {
            return;
        }

        var pose = Transforms.ToLocal(transform, context.Scene.transform);
        var scale = transform.localScale;

        if (!forceSend && pose.Equals(lastPose) && scale == lastScale)
        {
            return;
        }

        forceSend = false;
        lastPose = pose;
        lastScale = scale;

        context.SendJson(new Message
        {
            pose = pose,
            scale = scale
        });
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        
        originalScale = transform.localScale;
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        body = GetComponent<Rigidbody>();
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.activated.AddListener(OnGrab);
        grab.deactivated.AddListener(OnRelease);

        grab.selectEntered.AddListener(XRGrabInteractable_SelectEntered);
        grab.selectExited.AddListener(XRGrabInteractable_SelectExited);

        Debug.Log("shovel positions saved");
    }   

    private void LateUpdate()
    {
        SendNetworkUpdates();
    }

    void OnGrab(ActivateEventArgs eventArgs)
    {

    }

    void OnRelease(DeactivateEventArgs eventArgs)
    {

    }

    private void XRGrabInteractable_SelectEntered(SelectEnterEventArgs eventArgs)
    {
        transform.localScale = originalScale * grabScaleMultiplier;
        forceSend = true; // replicate scale change immediately
        Debug.Log("shovel grabbed");
    }

    private void XRGrabInteractable_SelectExited(SelectExitEventArgs eventArgs)
    {
        transform.localScale = originalScale;
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        forceSend = true; // replicate reset after release
        Debug.Log("shovel released");
    }
}
