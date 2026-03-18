using UnityEngine;
using System.Collections;
using Ubiq.Geometry;
using Ubiq.Messaging;
using Ubiq.Spawning;

public class VillagerAgent : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }

    public enum VillagerState { Idle, Moving, Held, Dead }

    [Header("References")]
    public BoardGrid grid;

    [Header("Movement")]
    public float moveSpeedCellsPerSecond = 1f;
    public float minWait = 0.8f;
    public float maxWait = 2.0f;

    [Header("Held Behavior")]
    public float heldHoverHeight = 0.01f;

    [Header("Drop Preview")]
    public GameObject dropPreview;
    public float previewThickness = 0.002f;
    public float previewYOffset = 0.002f;

    [Header("State")]
    public Vector2Int cell;
    public VillagerState state = VillagerState.Idle;

    public bool isAuthority = true;

    private Coroutine loop;
    private Coroutine releaseRoutine;
    private Coroutine remoteMoveRoutine;
    private Rigidbody rb;
    private Collider cachedCollider;

    private static readonly Vector2Int[] dirs =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    // Networking (Ubiq)
    private NetworkContext context;
    private Pose lastHeldPose;
    private bool forceHeldSend;
    private Vector2Int lastSentCell;
    private VillagerState lastSentState;
    private float lastStateSendTime;
    private int localMoveSeq;
    private int remoteMoveSeq;

    private const float StateResendIntervalSeconds = 1.0f;

    private enum MessageType
    {
        State = 0,
        MoveStart = 1,
        MoveEnd = 2,
        HeldPose = 3
    }

    private struct Message
    {
        public MessageType type;

        // common
        public int state;
        public int cellX;
        public int cellY;
        public int moveSeq;

        // movement
        public int fromX;
        public int fromY;
        public int toX;
        public int toY;
        public float startTime;
        public float duration;

        // held pose (local to the network scene)
        public Pose pose;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        if (grid == null)
        {
            grid = FindFirstObjectByType<BoardGrid>();
            if (grid == null)
            {
                return;
            }
        }

        if (context.Scene == null)
        {
            return;
        }

        var m = message.FromJson<Message>();
        switch (m.type)
        {
            case MessageType.State:
                ApplyRemoteState(m);
                break;
            case MessageType.MoveStart:
                ApplyRemoteMoveStart(m);
                break;
            case MessageType.MoveEnd:
                ApplyRemoteMoveEnd(m);
                break;
            case MessageType.HeldPose:
                ApplyRemoteHeldPose(m);
                break;
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cachedCollider = GetComponent<Collider>();
    }

    void Start()
    {
        if (!NetworkId.Valid)
        {
            // Scene-placed villagers (not spawned by NetworkSpawnManager) won't have a NetworkId assigned.
            // Fall back to a deterministic id derived from the scene graph location.
            NetworkId = NetworkId.Create(this);
        }

        context = NetworkScene.Register(this);
        if (!grid) grid = FindFirstObjectByType<BoardGrid>();
        SnapToCell(cell);
        StartWanderLoop();

        if (dropPreview != null)
            dropPreview.SetActive(false);

        lastSentCell = cell;
        lastSentState = state;
        lastStateSendTime = Time.time;
        lastHeldPose = new Pose(new Vector3(float.NaN, float.NaN, float.NaN), Quaternion.identity);

        if (isAuthority)
        {
            SendState(force: true);
        }
    }

    void LateUpdate()
    {
        if (grid == null)
            return;

        if (state == VillagerState.Held)
        {
            ConstrainHeldToSurface();
            UpdateDropPreview();

            if (isAuthority)
            {
                SendHeldPoseIfNeeded();
            }
        }
        else
        {
            HideDropPreview();
        }

        if (isAuthority)
        {
            SendStateIfNeeded();
        }
    }

    void StartWanderLoop()
    {
        if (loop == null && isAuthority && state != VillagerState.Dead)
            loop = StartCoroutine(WanderLoop());
    }

    IEnumerator WanderLoop()
    {
        while (state != VillagerState.Dead)
        {
            if (!isAuthority || state == VillagerState.Held)
            {
                yield return null;
                continue;
            }

            state = VillagerState.Idle;
            yield return new WaitForSeconds(Random.Range(minWait, maxWait));

            if (state == VillagerState.Held || state == VillagerState.Dead || !isAuthority)
                continue;

            var next = PickNeighborCell();
            if (next != cell)
                yield return MoveToCell(next);
        }

        loop = null;
    }

    Vector2Int PickNeighborCell()
    {
        for (int i = 0; i < 8; i++)
        {
            var d = dirs[Random.Range(0, dirs.Length)];
            var c = cell + d;
            if (grid.InBounds(c))
                return c;
        }
        return cell;
    }

    IEnumerator MoveToCell(Vector2Int target)
    {
        state = VillagerState.Moving;

        Vector3 start = transform.position;
        float hh = GetHalfHeight();
        Vector3 end = grid.CellToWorld(target, hh);

        Vector3 dir = end - start;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.forward = dir.normalized;

        float dist = Vector3.Distance(start, end);
        float duration = dist / (grid.cellSize * Mathf.Max(0.01f, moveSpeedCellsPerSecond));
        float t = 0f;

        if (isAuthority)
        {
            localMoveSeq++;
            SendMoveStart(localMoveSeq, cell, target, duration);
            SendState(force: true);
        }

        while (t < 1f)
        {
            if (state == VillagerState.Held || state == VillagerState.Dead)
                yield break;

            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        cell = target;
        SnapToCell(cell);
        state = VillagerState.Idle;

        if (isAuthority)
        {
            SendMoveEnd(localMoveSeq, cell);
            SendState(force: true);
        }
    }

    public void BeginHold()
    {
        if (state == VillagerState.Dead) return;

        state = VillagerState.Held;
        forceHeldSend = true;

        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }

        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }

        // Avoid warnings with kinematic rigidbodies.
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (isAuthority)
        {
            SendState(force: true);
        }
    }

    public void EndHold(Vector3 releaseWorldPos)
    {
        if (state == VillagerState.Dead) return;

        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);

        releaseRoutine = StartCoroutine(ReleaseAndResume(releaseWorldPos));
    }

    IEnumerator ReleaseAndResume(Vector3 releaseWorldPos)
    {
        yield return new WaitForFixedUpdate();

        var dropCell = grid.Clamp(grid.WorldToCell(releaseWorldPos));
        cell = dropCell;
        SnapToCell(cell);

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        state = VillagerState.Idle;
        releaseRoutine = null;
        HideDropPreview();
        StartWanderLoop();

        if (isAuthority)
        {
            SendState(force: true);
        }
    }

    public void SnapToCell(Vector2Int c)
    {
        float hh = cachedCollider != null ? cachedCollider.bounds.extents.y : 0.05f;
        transform.position = grid.CellToWorld(c, hh);
    }

    public void Die()
    {
        state = VillagerState.Dead;
        HideDropPreview();
        gameObject.SetActive(false);
    }

    float GetHalfHeight()
    {
        return cachedCollider != null ? cachedCollider.bounds.extents.y : 0.05f;
    }

    void ConstrainHeldToSurface()
    {
        float hh = GetHalfHeight();

        float minY = grid.GetSurfaceTopY() + hh + grid.extraClearance + heldHoverHeight;

        Vector3 p = transform.position;
        p.y = Mathf.Max(p.y, minY);   // allow lifting up, block pushing down into table
        transform.position = p;
        Vector3 e = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, e.y, 0f);
    }
    void UpdateDropPreview()
    {
        if (dropPreview == null)
            return;

        Vector2Int previewCell = grid.Clamp(grid.WorldToCell(transform.position));

        Vector3 previewPos = grid.CellToWorld(previewCell, 0f);
        previewPos.y = grid.GetSurfaceTopY() + previewYOffset;

        dropPreview.SetActive(true);
        dropPreview.transform.position = previewPos;
        dropPreview.transform.localScale = new Vector3(grid.cellSize, previewThickness, grid.cellSize);
    }

    void HideDropPreview()
    {
        if (dropPreview != null && dropPreview.activeSelf)
            dropPreview.SetActive(false);
    }

    private void SendStateIfNeeded()
    {
        if ((Time.time - lastStateSendTime) >= StateResendIntervalSeconds || cell != lastSentCell || state != lastSentState)
        {
            SendState(force: false);
        }
    }

    private void SendState(bool force)
    {
        lastStateSendTime = Time.time;

        if (force || cell != lastSentCell || state != lastSentState)
        {
            lastSentCell = cell;
            lastSentState = state;
        }

        context.SendJson(new Message
        {
            type = MessageType.State,
            state = (int)state,
            cellX = cell.x,
            cellY = cell.y,
            moveSeq = localMoveSeq
        });
    }

    private void SendMoveStart(int moveSeq, Vector2Int from, Vector2Int to, float durationSeconds)
    {
        context.SendJson(new Message
        {
            type = MessageType.MoveStart,
            moveSeq = moveSeq,
            fromX = from.x,
            fromY = from.y,
            toX = to.x,
            toY = to.y,
            startTime = Time.time,
            duration = Mathf.Max(0.01f, durationSeconds)
        });
    }

    private void SendMoveEnd(int moveSeq, Vector2Int at)
    {
        context.SendJson(new Message
        {
            type = MessageType.MoveEnd,
            moveSeq = moveSeq,
            cellX = at.x,
            cellY = at.y
        });
    }

    private void SendHeldPoseIfNeeded()
    {
        if (context.Scene == null)
        {
            return;
        }

        var pose = Transforms.ToLocal(transform, context.Scene.transform);
        if (!forceHeldSend && pose.Equals(lastHeldPose))
        {
            return;
        }

        forceHeldSend = false;
        lastHeldPose = pose;

        context.SendJson(new Message
        {
            type = MessageType.HeldPose,
            pose = pose,
            moveSeq = localMoveSeq
        });
    }

    private void ApplyRemoteState(Message m)
    {
        var remoteState = (VillagerState)m.state;
        var remoteCell = new Vector2Int(m.cellX, m.cellY);

        // Ignore out-of-order state updates.
        if (m.moveSeq < remoteMoveSeq)
        {
            return;
        }

        remoteMoveSeq = m.moveSeq;

        if (remoteMoveRoutine != null && remoteState != VillagerState.Moving)
        {
            StopCoroutine(remoteMoveRoutine);
            remoteMoveRoutine = null;
        }

        state = remoteState;
        cell = remoteCell;

        if (state != VillagerState.Moving && state != VillagerState.Held && state != VillagerState.Dead)
        {
            SnapToCell(cell);
        }
    }

    private void ApplyRemoteMoveStart(Message m)
    {
        if (m.moveSeq < remoteMoveSeq)
        {
            return;
        }

        remoteMoveSeq = m.moveSeq;

        var from = new Vector2Int(m.fromX, m.fromY);
        var to = new Vector2Int(m.toX, m.toY);
        var durationSeconds = Mathf.Max(0.01f, m.duration);

        if (remoteMoveRoutine != null)
        {
            StopCoroutine(remoteMoveRoutine);
            remoteMoveRoutine = null;
        }

        state = VillagerState.Moving;
        cell = from;

        // Use local receive time for smooth motion (no shared clock assumption).
        remoteMoveRoutine = StartCoroutine(RemoteMoveRoutine(m.moveSeq, from, to, Time.time, durationSeconds));
    }

    private void ApplyRemoteMoveEnd(Message m)
    {
        if (m.moveSeq < remoteMoveSeq)
        {
            return;
        }

        remoteMoveSeq = m.moveSeq;

        if (remoteMoveRoutine != null)
        {
            StopCoroutine(remoteMoveRoutine);
            remoteMoveRoutine = null;
        }

        cell = new Vector2Int(m.cellX, m.cellY);
        SnapToCell(cell);
        state = VillagerState.Idle;
    }

    private void ApplyRemoteHeldPose(Message m)
    {
        if (m.moveSeq < remoteMoveSeq)
        {
            return;
        }

        remoteMoveSeq = m.moveSeq;

        if (context.Scene == null)
        {
            return;
        }

        if (remoteMoveRoutine != null)
        {
            StopCoroutine(remoteMoveRoutine);
            remoteMoveRoutine = null;
        }

        state = VillagerState.Held;

        var worldPose = Transforms.ToWorld(m.pose, context.Scene.transform);
        transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);

        lastHeldPose = m.pose;
        forceHeldSend = false;
    }

    private IEnumerator RemoteMoveRoutine(int moveSeq, Vector2Int from, Vector2Int to, float startTime, float durationSeconds)
    {
        float hh = GetHalfHeight();
        Vector3 start = grid.CellToWorld(from, hh);
        Vector3 end = grid.CellToWorld(to, hh);

        Vector3 dir = end - start;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.forward = dir.normalized;

        while (state == VillagerState.Moving && remoteMoveSeq == moveSeq)
        {
            var t = Mathf.Clamp01((Time.time - startTime) / durationSeconds);
            transform.position = Vector3.Lerp(start, end, t);

            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        cell = to;
        SnapToCell(cell);
        state = VillagerState.Idle;
        remoteMoveRoutine = null;
    }
}
