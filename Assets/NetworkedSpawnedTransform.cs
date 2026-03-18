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
    private bool pendingInitialSend;

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
    }

    private void LateUpdate()
    {
        if (!owner || !pendingInitialSend || context.Scene == null)
        {
            return;
        }

        pendingInitialSend = false;

        var pose = Transforms.ToLocal(transform, context.Scene.transform);
        context.SendJson(new Message
        {
            pose = pose,
            scale = transform.localScale
        });
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (context.Scene == null)
        {
            return;
        }

        var m = message.FromJson<Message>();
        var pose = Transforms.ToWorld(m.pose, context.Scene.transform);
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        transform.localScale = m.scale;
    }
}
