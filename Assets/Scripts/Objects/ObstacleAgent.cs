using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Spawning;

public class ObstacleAgent : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }

    [Tooltip("Set this to Tree, Rock, or Flower in the prefab inspector!")]
    public CellType obstacleType;

    private NetworkContext context;
    private bool owner;
    private bool isRegisteredLocally = false;
    private bool hasAuthoritativeCell = false;
    private Vector2Int authoritativeCell;
    private bool pendingInitialCellSend;
    private int resendRemaining;
    private float nextResendTime;

    private const int ResendCount = 5;
    private const float ResendInterval = 0.2f;

    private struct Message
    {
        public int cellX;
        public int cellY;
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
            cellY = authoritativeCell.y
        });

        resendRemaining--;
        if (resendRemaining <= 0)
            pendingInitialCellSend = false;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();
        authoritativeCell = new Vector2Int(m.cellX, m.cellY);
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
            isRegisteredLocally = true;
            return;
        }

        if (GridManager.Instance.TryGetCell(authoritativeCell, out var data) && data.placedObject == gameObject)
        {
            isRegisteredLocally = true;
            return;
        }

        string occupant = GridManager.Instance.TryGetCell(authoritativeCell, out data) && data.placedObject != null
            ? $"{data.type} ({data.placedObject.name})"
            : "empty or unknown occupant";

        Debug.LogWarning($"[ObstacleAgent] Failed to register authoritative cell {authoritativeCell} for '{name}'. Existing occupant: {occupant}");
    }
}
