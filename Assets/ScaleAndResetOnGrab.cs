using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Ubiq.Messaging;
using Ubiq.Geometry;
using Ubiq.Rooms;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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

    [Header("Networking (Ownership)")]
    [Tooltip("How long a grab 'lock' lasts without renewal (seconds).")]
    public float leaseSeconds = 1.0f;

    [Tooltip("How often the owner renews the lease while held (seconds).")]
    public float heartbeatInterval = 0.25f;

    private RoomClient roomClient;
    private string ownerUuid;
    private double lockUntilLocalTime;
    private double lastHeartbeatSentLocalTime;

    private enum MessageType : byte
    {
        Transform = 0,
        Claim = 1,
        Heartbeat = 2,
        Release = 3
    }

    private struct Message
    {
        public MessageType type;
        public Pose pose;
        public Vector3 scale;
        public string ownerUuid;
        public float leaseSeconds;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();

        switch (m.type)
        {
            case MessageType.Transform:
            {
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
                break;
            }
            case MessageType.Claim:
            case MessageType.Heartbeat:
                ApplyLease(m.ownerUuid, m.leaseSeconds);
                break;
            case MessageType.Release:
                if (!string.IsNullOrEmpty(m.ownerUuid) && m.ownerUuid == ownerUuid)
                {
                    ownerUuid = null;
                    lockUntilLocalTime = 0;
                }
                break;
        }
    }

    private string LocalUuid => roomClient != null && roomClient.Me != null ? roomClient.Me.uuid : null;

    private bool IsLeaseValid()
    {
        return !string.IsNullOrEmpty(ownerUuid) && Time.timeAsDouble <= lockUntilLocalTime;
    }

    private bool IsLocalOwner()
    {
        var me = LocalUuid;
        return !string.IsNullOrEmpty(me) && ownerUuid == me && IsLeaseValid();
    }

    private void ApplyLease(string incomingOwnerUuid, float incomingLeaseSeconds)
    {
        if (string.IsNullOrEmpty(incomingOwnerUuid))
        {
            return;
        }

        // Clamp to avoid a malicious/buggy peer pinning the lock for too long.
        var clampedLease = Mathf.Clamp(incomingLeaseSeconds, 0.1f, 5.0f);

        if (!IsLeaseValid())
        {
            ownerUuid = incomingOwnerUuid;
            lockUntilLocalTime = Time.timeAsDouble + clampedLease;
            return;
        }

        if (incomingOwnerUuid == ownerUuid)
        {
            lockUntilLocalTime = Time.timeAsDouble + clampedLease;
            return;
        }

        // Tie-break: lowest uuid wins (converges if two peers grab at the same time).
        var winner = string.CompareOrdinal(incomingOwnerUuid, ownerUuid) < 0 ? incomingOwnerUuid : ownerUuid;
        ownerUuid = winner;
        lockUntilLocalTime = Time.timeAsDouble + clampedLease;
    }

    private void SendLease(MessageType type)
    {
        var me = LocalUuid;
        if (string.IsNullOrEmpty(me))
        {
            return;
        }

        context.SendJson(new Message
        {
            type = type,
            ownerUuid = me,
            leaseSeconds = leaseSeconds
        });
    }

    private void SendNetworkUpdates()
    {
        if (!grab)
        {
            return;
        }

        // Only the current owner should publish motion.
        // Also allow one send right after release/scale changes via forceSend (owner only).
        if (!IsLocalOwner())
        {
            return;
        }

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
            type = MessageType.Transform,
            pose = pose,
            scale = scale
        });
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        roomClient = FindFirstObjectByType<RoomClient>();
        
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
        // If we lose ownership (e.g., conflict resolution), force release locally so the lock winner controls motion.
        if (grab != null && grab.isSelected && IsLeaseValid() && !IsLocalOwner())
        {
            var interactionManager = grab.interactionManager;
            var interactor = grab.firstInteractorSelecting as IXRSelectInteractor;
            if (interactionManager != null && interactor != null)
            {
                interactionManager.SelectExit(interactor, grab);
            }
        }

        // Heartbeat while held to keep the lease alive.
        if (grab != null && grab.isSelected && IsLocalOwner())
        {
            if (Time.timeAsDouble - lastHeartbeatSentLocalTime >= heartbeatInterval)
            {
                lastHeartbeatSentLocalTime = Time.timeAsDouble;
                SendLease(MessageType.Heartbeat);
                lockUntilLocalTime = Time.timeAsDouble + Mathf.Clamp(leaseSeconds, 0.1f, 5.0f);
            }
        }

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
        // If we don't have a RoomClient (singleplayer/offline), skip ownership gating.
        var me = LocalUuid;
        if (!string.IsNullOrEmpty(me))
        {
            // If locked by someone else and not expired, deny the grab.
            if (IsLeaseValid() && ownerUuid != me)
            {
                var interactionManager = grab != null ? grab.interactionManager : null;
                var interactor = eventArgs.interactorObject;
                if (interactionManager != null && interactor != null)
                {
                    interactionManager.SelectExit(interactor, grab);
                }
                return;
            }

            // Claim / refresh the lock.
            ownerUuid = me;
            lockUntilLocalTime = Time.timeAsDouble + Mathf.Clamp(leaseSeconds, 0.1f, 5.0f);
            lastHeartbeatSentLocalTime = Time.timeAsDouble;
            SendLease(MessageType.Claim);
        }

        transform.localScale = originalScale * grabScaleMultiplier;
        forceSend = true; // replicate scale change immediately
        Debug.Log("shovel grabbed");
    }

    private void XRGrabInteractable_SelectExited(SelectExitEventArgs eventArgs)
    {
        transform.localScale = originalScale;
        transform.position = originalPosition;
        transform.rotation = originalRotation;

        // Replicate reset after release (while we still own the lock).
        forceSend = true;
        SendNetworkUpdates();

        // Release the lock after publishing the reset state.
        if (IsLocalOwner())
        {
            SendLease(MessageType.Release);
            ownerUuid = null;
            lockUntilLocalTime = 0;
        }

        Debug.Log("shovel released");
    }
}
