using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Spawning;

public class ObstacleAgent : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }

    [Tooltip("Logical grid cell type for this network-spawned object.")]
    public CellType obstacleType;

    private NetworkContext context;
    private bool owner;
    private bool isRegisteredLocally = false;
    private bool hasAuthoritativeCell = false;
    private Vector2Int authoritativeCell;
    private bool pendingInitialCellSend;
    private int resendRemaining;
    private float nextResendTime;

    private const int ResendCount = 15;
    private const float ResendInterval = 0.2f;

    private struct Message
    {
        public int cellX;
        public int cellY;
        public int cellType;
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        TryRegisterAuthoritativeCell();
    }

    void LateUpdate()
    {
        if (!owner && hasAuthoritativeCell && !isRegisteredLocally)
        {
            TryRegisterAuthoritativeCell();
        }

        if (!owner || !pendingInitialCellSend || !hasAuthoritativeCell || context.Scene == null)
            return;

        if (Time.time < nextResendTime)
            return;

        nextResendTime = Time.time + ResendInterval;
        context.SendJson(new Message
        {
            cellX = authoritativeCell.x,
            cellY = authoritativeCell.y,
            cellType = (int)obstacleType
        });

        resendRemaining--;
        if (resendRemaining <= 0)
            pendingInitialCellSend = false;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();
        authoritativeCell = new Vector2Int(m.cellX, m.cellY);
        obstacleType = (CellType)m.cellType;
        hasAuthoritativeCell = true;
        TryRegisterAuthoritativeCell();
    }

    public void SetOwner(bool isOwner)
    {
        owner = isOwner;
    }

    public void SetAuthoritativeCell(Vector2Int cell)
    {
        authoritativeCell = cell;
        hasAuthoritativeCell = true;
    }

    public void RequestInitialCellSend()
    {
        if (!owner || !hasAuthoritativeCell)
            return;

        pendingInitialCellSend = true;
        resendRemaining = ResendCount;
        nextResendTime = 0f;
    }

    public void MarkAsRegisteredByLeader()
    {
        // The leader already placed this immediately upon spawning, so skip the delay.
        isRegisteredLocally = true;
    }

    private void TryRegisterAuthoritativeCell()
    {
        if (GridManager.Instance == null || !hasAuthoritativeCell || isRegisteredLocally)
            return;

        if (GridManager.Instance.TryPlace(authoritativeCell, obstacleType, gameObject))
        {
            RegisterSpecialNodeIfNeeded();
            isRegisteredLocally = true;
            return;
        }

        if (GridManager.Instance.TryGetCell(authoritativeCell, out var data) && data.placedObject == gameObject)
        {
            RegisterSpecialNodeIfNeeded();
            isRegisteredLocally = true;
            return;
        }

        if (GridManager.Instance.TryGetCell(authoritativeCell, out data) &&
            data.type == obstacleType &&
            data.placedObject == null &&
            GridManager.Instance.TrySetPlacedObject(authoritativeCell, gameObject))
        {
            RegisterSpecialNodeIfNeeded();
            PathChecker.Instance?.SetNodeObject(authoritativeCell, gameObject);
            isRegisteredLocally = true;
            return;
        }

        string occupant = GridManager.Instance.TryGetCell(authoritativeCell, out data) && data.placedObject != null
            ? $"{data.type} ({data.placedObject.name})"
            : "empty or unknown occupant";

        Debug.LogWarning($"[ObstacleAgent] Failed to register authoritative cell {authoritativeCell} for '{name}'. Existing occupant: {occupant}");
    }

    private void RegisterSpecialNodeIfNeeded()
    {
        if (PathChecker.Instance == null)
            return;

        if (obstacleType == CellType.Start || obstacleType == CellType.Finish)
        {
            PathChecker.Instance.RegisterNode(authoritativeCell, PathNode.Omnidirectional(), gameObject);
        }
    }
}
