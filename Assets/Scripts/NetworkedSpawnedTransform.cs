using UnityEngine;
using Ubiq.Geometry;
using Ubiq.Messaging;
using Ubiq.Spawning;

/// <summary>
/// Attach to a prefab in the Ubiq Prefab Catalogue. Because it implements
/// INetworkSpawnable, NetworkSpawnManager assigns a consistent NetworkId at
/// spawn time. This lets us send an initial pose/scale so all peers see the
/// same transform (Ubiq spawn messages do not include transform).
/// </summary>
public class NetworkedSpawnedTransform : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }

    private NetworkContext context;
    private bool owner;
    public bool IsOwner => owner;
    private bool pendingInitialSend;

    // Resend the initial position several times to survive the race condition
    // where the remote peer's Start() hasn't run yet when the first message arrives.
    private int resendRemaining;
    private float nextResendTime;
    private const int   ResendCount    = 5;
    private const float ResendInterval = 0.2f;

    private struct Message
    {
        public Pose pose;
        public Vector3 scale;
    }

    private void Start()
    {
        context = NetworkScene.Register(this);
    }

    public void SetOwner(bool isOwner)
    {
        owner = isOwner;
    }

    public void RequestInitialSend()
    {
        pendingInitialSend = true;
        resendRemaining    = ResendCount;
        nextResendTime     = 0f;
    }

    private void LateUpdate()
    {
        if (!owner || !pendingInitialSend || context.Scene == null)
            return;

        if (Time.time < nextResendTime)
            return;

        nextResendTime = Time.time + ResendInterval;

        var pose = Transforms.ToLocal(transform, context.Scene.transform);
        context.SendJson(new Message
        {
            pose  = pose,
            scale = transform.localScale
        });

        resendRemaining--;
        if (resendRemaining <= 0)
            pendingInitialSend = false;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (context.Scene == null)
            return;

        var m    = message.FromJson<Message>();
        var pose = Transforms.ToWorld(m.pose, context.Scene.transform);
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        transform.localScale = m.scale;
    }
}
