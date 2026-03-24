using UnityEngine;
using System.Collections;
using System.Collections.Generic;
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

    // Ice power-up freeze (shared across all villagers)
    private static float freezeEndTime = -1f;
    public static bool IsFrozen => Time.time < freezeEndTime;

    public static void FreezeAll(float duration)
    {
        freezeEndTime = Time.time + duration;
        Debug.Log($"[Ice] All villagers frozen for {duration}s");
    }

    private Coroutine loop;
    private Coroutine releaseRoutine;
    private Coroutine remoteMoveRoutine;
    private Coroutine pathFollowRoutine;
    private Rigidbody rb;
    private Collider cachedCollider;
    private Animator[] cachedAnimators;
    private MonoBehaviour cachedAutoPlayScript;
    private bool wasFrozen;

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
    private float initialTimerValue = float.NaN;

    private const float StateResendIntervalSeconds = 1.0f;
    private const float LavaDeathGraceSeconds = 5.0f;

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
        cachedAnimators = GetComponentsInChildren<Animator>();
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
        initialTimerValue = CountdownTimer.Instance != null ? CountdownTimer.Instance.TimeRemaining : float.NaN;

        if (isAuthority)
        {
            SendState(force: true);
        }
    }

    void LateUpdate()
    {
        // Freeze/unfreeze animation on transition only
        bool frozen = IsFrozen;
        if (frozen != wasFrozen)
        {
            wasFrozen = frozen;
            if (frozen)
                EnterFrozenVisualState();
            else
                ExitFrozenVisualState();
        }

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
            // Stop wandering when the game is over.
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            {
                state = VillagerState.Idle;
                break;
            }

            // Frozen by ice power-up — wait until thaw
            if (IsFrozen)
            {
                yield return null;
                continue;
            }

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
            if (IsCellWalkable(c))
                return c;
        }
        return cell; // stay put if all attempts hit blocked cells
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

        // Check if we arrived on lava — triggers death and game over.
        CheckLavaAndDie();
        if (state == VillagerState.Dead)
            yield break;

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

        AudioManager.Instance?.PlayVillagerPickupSound(transform.position);

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

        if (pathFollowRoutine != null)
        {
            StopCoroutine(pathFollowRoutine);
            pathFollowRoutine = null;
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

        var rawCell = grid.Clamp(grid.WorldToCell(releaseWorldPos));

        // Dropped onto lava — instant death.
        if (isAuthority && IsCellLava(rawCell))
        {
            Debug.Log("Game over! Villager dropped into lava.");
            cell = rawCell;
            SnapToCell(cell);
            releaseRoutine = null;
            HideDropPreview();

            AudioManager.Instance?.PlayVillagerDeathSound(transform.position);

            state = VillagerState.Dead;
            SendState(force: true);
            gameObject.SetActive(false);

            if (GameManager.Instance != null)
                GameManager.Instance.OnVillagerDied();

            yield break;
        }

        // Dropped onto a path cell — follow the path forward.
        if (isAuthority && IsPathCell(rawCell))
        {
            Vector3 worldPos = grid.CellToWorld(rawCell, 0f);
            Vector2Int gmCell = GridManager.Instance.WorldToGrid(worldPos);
            var gmPath = PathChecker.Instance.GetPathFrom(gmCell);

            if (gmPath.Count > 0)
            {
                // Convert GridManager path to BoardGrid cells
                var boardPath = new List<Vector2Int>();
                foreach (var gm in gmPath)
                {
                    Vector3 wp = GridManager.Instance.GridToWorld(gm);
                    boardPath.Add(grid.Clamp(grid.WorldToCell(wp)));
                }

                cell = boardPath[0];
                SnapToCell(cell);

                if (rb != null && !rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                state = VillagerState.Idle;
                releaseRoutine = null;
                HideDropPreview();

                if (isAuthority)
                    SendState(force: true);

                pathFollowRoutine = StartCoroutine(FollowPathCoroutine(boardPath));
                yield break;
            }
        }

        // Dropped onto a blocked cell (tree/rock/path) — redirect to nearest valid cell.
        if (!IsCellWalkable(rawCell))
            rawCell = FindNearestWalkableCell(rawCell);

        cell = rawCell;
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
        Debug.Log($"Villager Die() called on '{gameObject.name}' at cell {cell}");
        state = VillagerState.Dead;
        HideDropPreview();
        AudioManager.Instance?.PlayVillagerDeathSound(transform.position);
        gameObject.SetActive(false);
    }

    // Returns true if the BoardGrid cell can be entered by a villager.
    // Blocked: Tree, Rock, Path. Walkable: Empty, Flower, Start, Finish, Lava.
    // Lava is walkable (entering it triggers death separately).
    bool IsCellWalkable(Vector2Int boardCell)
    {
        if (!grid.InBounds(boardCell))
            return false;

        if (GridManager.Instance == null)
            return true;

        Vector3 worldPos = grid.CellToWorld(boardCell, 0f);
        Vector2Int gmCell = GridManager.Instance.WorldToGrid(worldPos);

        if (!GridManager.Instance.TryGetCell(gmCell, out GridCell data))
            return true; // no data = empty ground

        switch (data.type)
        {
            case CellType.Tree:
            case CellType.Rock:
            case CellType.Path:
                return false;
            default:
                return true;
        }
    }

    // Returns true if the BoardGrid cell overlaps a GridManager Lava cell.
    bool IsCellLava(Vector2Int boardCell)
    {
        if (GridManager.Instance == null)
            return false;

        Vector3 worldPos = grid.CellToWorld(boardCell, 0f);
        Vector2Int gmCell = GridManager.Instance.WorldToGrid(worldPos);

        if (GridManager.Instance.TryGetCell(gmCell, out GridCell data))
            return data.type == CellType.Lava;

        return false;
    }

    // Returns true if the BoardGrid cell is a path/start/finish cell with a
    // registered PathNode, meaning the villager can follow the path from here.
    bool IsPathCell(Vector2Int boardCell)
    {
        if (GridManager.Instance == null || PathChecker.Instance == null)
            return false;

        Vector3 worldPos = grid.CellToWorld(boardCell, 0f);
        Vector2Int gmCell = GridManager.Instance.WorldToGrid(worldPos);

        if (!GridManager.Instance.TryGetCell(gmCell, out GridCell data))
            return false;

        return (data.type == CellType.Start || data.type == CellType.Path || data.type == CellType.Finish)
            && PathChecker.Instance.HasNode(gmCell);
    }

    // BFS outward from 'from' to find the nearest walkable, non-lava cell.
    // Used when dropping a villager onto a blocked cell.
    Vector2Int FindNearestWalkableCell(Vector2Int from)
    {
        if (grid.InBounds(from) && IsCellWalkable(from) && !IsCellLava(from))
            return from;

        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        visited.Add(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            for (int i = 0; i < dirs.Length; i++)
            {
                var neighbor = current + dirs[i];
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);

                if (!grid.InBounds(neighbor)) continue;

                if (IsCellWalkable(neighbor) && !IsCellLava(neighbor))
                    return neighbor;

                queue.Enqueue(neighbor);
            }

            if (visited.Count > grid.width * grid.height)
                break;
        }

        return grid.Clamp(from);
    }

    // If the villager's current cell is lava, trigger death and game over.
    // Only runs on the authority peer; remotes receive the Dead state via network.
    void CheckLavaAndDie()
    {
        if (state == VillagerState.Dead) return;
        if (!isAuthority) return;
        if (IsWithinLavaDeathGracePeriod()) return;

        if (IsCellLava(cell))
        {
            Debug.Log("Game over! Villager fell into lava.");
            AudioManager.Instance?.PlayVillagerDeathSound(transform.position);

            // Send Dead state to remote peers BEFORE deactivating the GameObject.
            state = VillagerState.Dead;
            SendState(force: true);

            HideDropPreview();
            gameObject.SetActive(false);

            if (GameManager.Instance != null)
                GameManager.Instance.OnVillagerDied();
        }
    }

    bool IsWithinLavaDeathGracePeriod()
    {
        if (float.IsNaN(initialTimerValue))
            return false;

        if (CountdownTimer.Instance == null)
            return false;

        return CountdownTimer.Instance.TimeRemaining > initialTimerValue - LavaDeathGraceSeconds;
    }

    void EnterFrozenVisualState()
    {
        if (cachedAnimators == null || cachedAnimators.Length == 0)
            cachedAnimators = GetComponentsInChildren<Animator>();

        foreach (var anim in cachedAnimators)
            if (anim != null) anim.speed = 0f;

        if (cachedAutoPlayScript == null)
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "CityPeople")
                {
                    cachedAutoPlayScript = mb;
                    break;
                }
            }
        }
        if (cachedAutoPlayScript != null)
            cachedAutoPlayScript.StopAllCoroutines();
    }

    void ExitFrozenVisualState()
    {
        if (cachedAnimators != null)
            foreach (var anim in cachedAnimators)
                if (anim != null) anim.speed = 1f;
    }

    IEnumerator FollowPathCoroutine(List<Vector2Int> pathBoardCells)
    {
        for (int i = 0; i < pathBoardCells.Count; i++)
        {
            if (state == VillagerState.Held || state == VillagerState.Dead)
            {
                pathFollowRoutine = null;
                yield break;
            }

            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            {
                state = VillagerState.Idle;
                pathFollowRoutine = null;
                yield break;
            }

            // Frozen by ice power-up — wait until thaw
            while (IsFrozen)
                yield return null;

            var target = pathBoardCells[i];
            if (target == cell) continue; // skip current position

            yield return MoveToCell(target);

            if (state == VillagerState.Dead || state == VillagerState.Held)
            {
                pathFollowRoutine = null;
                yield break;
            }
        }

        // Path ended — resume normal wandering
        pathFollowRoutine = null;
        state = VillagerState.Idle;
        StartWanderLoop();
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

        // Color feedback: cyan = path (will follow), red = lava (death),
        // yellow = blocked (will redirect), white = safe.
        var renderer = dropPreview.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (IsPathCell(previewCell))
                renderer.material.color = Color.cyan;
            else if (IsCellLava(previewCell))
                renderer.material.color = Color.red;
            else if (!IsCellWalkable(previewCell))
                renderer.material.color = Color.yellow;
            else
                renderer.material.color = Color.white;
        }
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

        // Replicate death on remote peers.
        if (state == VillagerState.Dead)
        {
            Debug.Log($"Villager '{gameObject.name}' received Dead state from remote peer.");
            HideDropPreview();
            gameObject.SetActive(false);
            // Do NOT call OnVillagerDied here — the authority peer already called it,
            // and LivesManager broadcasts the life-loss to all peers via network message.
            return;
        }

        if (state != VillagerState.Moving && state != VillagerState.Held)
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
