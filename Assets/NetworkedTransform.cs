using UnityEngine;
using Ubiq.Messaging;

/// <summary>
/// Attach to tool GameObjects in the scene (axe, pickaxe, delete tool, shovel).
/// Detects local movement and broadcasts position/rotation to all peers.
/// Handles Rigidbody correctly — sets kinematic while under remote control so
/// physics does not fight the received positions.
/// </summary>
public class NetworkedTransform : MonoBehaviour
{
    private NetworkContext context;
    private Rigidbody rb;

    private Vector3    lastPosition;
    private Quaternion lastRotation;

    private bool  isMaster;
    private float masterTimeout;
    private float remoteTimeout;        // when remote control expires and physics should restore
    private float nextBroadcastTime;

    private const float BroadcastInterval   = 0.05f;  // 20 Hz
    private const float MasterHoldTime      = 0.3f;
    private const float RemoteControlExpiry = 0.5f;   // restore physics this long after last message

    private struct Message
    {
        public float px, py, pz;
        public float rx, ry, rz, rw;
    }

    void Start()
    {
        context      = NetworkScene.Register(this);
        rb           = GetComponent<Rigidbody>();
        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }

    void Update()
    {
        if (context.Scene == null) return;

        // Restore physics when remote control has expired
        if (!isMaster && rb != null && rb.isKinematic && Time.time > remoteTimeout)
            rb.isKinematic = false;

        bool moved = Vector3.SqrMagnitude(transform.position - lastPosition) > 0.00001f
                  || Quaternion.Angle(transform.rotation, lastRotation) > 0.1f;

        lastPosition = transform.position;
        lastRotation = transform.rotation;

        if (moved)
        {
            // Local movement — take ownership, release remote kinematic lock if set
            if (rb != null && rb.isKinematic && !isMaster)
                rb.isKinematic = false;

            isMaster      = true;
            masterTimeout = Time.time + MasterHoldTime;
        }
        else if (Time.time > masterTimeout)
        {
            isMaster = false;
        }

        if (!isMaster || Time.time < nextBroadcastTime) return;

        nextBroadcastTime = Time.time + BroadcastInterval;
        var pos = transform.position;
        var rot = transform.rotation;
        context.SendJson(new Message
        {
            px = pos.x, py = pos.y, pz = pos.z,
            rx = rot.x, ry = rot.y, rz = rot.z, rw = rot.w
        });
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (context.Scene == null || isMaster) return;

        var m   = message.FromJson<Message>();
        var pos = new Vector3(m.px, m.py, m.pz);
        var rot = new Quaternion(m.rx, m.ry, m.rz, m.rw);

        // Lock physics so gravity/collisions don't override the received position
        if (rb != null && !rb.isKinematic)
            rb.isKinematic = true;

        if (rb != null)
        {
            rb.MovePosition(pos);
            rb.MoveRotation(rot);
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }

        remoteTimeout = Time.time + RemoteControlExpiry;

        // Update baseline so we don't mistake the applied position as local movement
        lastPosition = pos;
        lastRotation = rot;
    }
}
