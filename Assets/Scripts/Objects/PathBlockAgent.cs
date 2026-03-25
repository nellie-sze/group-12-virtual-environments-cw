using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Spawning;

/// <summary>
/// Attach to straight and corner path block prefabs.
/// Registers the exact authoritative grid cell and path-node rotation on every peer.
/// </summary>
public class PathBlockAgent : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }

    [Tooltip("True for straight pieces, false for corner pieces. Set in the prefab Inspector.")]
    public bool isStraight = true;

    [HideInInspector] public bool isLocallySpawned = false;

    private NetworkContext context;
    private bool owner;
    private bool hasAuthoritativePlacement;
    private bool isRegisteredLocally;
    private Vector2Int authoritativeCell;
    private int authoritativeRotationY;
    private bool pendingInitialRegistrationSend;
    private int resendRemaining;
    private float nextResendTime;

    private const int ResendCount = 10;
    private const float ResendInterval = 0.2f;

    private struct Message
    {
        public int cellX;
        public int cellY;
        public int rotationY;
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        TryRegisterAuthoritativePlacement();
    }

    void LateUpdate()
    {
        if (!owner && hasAuthoritativePlacement && !isRegisteredLocally)
            TryRegisterAuthoritativePlacement();

        if (!owner || !pendingInitialRegistrationSend || !hasAuthoritativePlacement || context.Scene == null)
            return;

        if (Time.time < nextResendTime)
            return;

        nextResendTime = Time.time + ResendInterval;
        context.SendJson(new Message
        {
            cellX = authoritativeCell.x,
            cellY = authoritativeCell.y,
            rotationY = authoritativeRotationY
        });

        resendRemaining--;
        if (resendRemaining <= 0)
            pendingInitialRegistrationSend = false;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();
        authoritativeCell = new Vector2Int(m.cellX, m.cellY);
        authoritativeRotationY = NormalizeRotation(m.rotationY);
        hasAuthoritativePlacement = true;
        TryRegisterAuthoritativePlacement();
    }

    public void SetOwner(bool isOwner)
    {
        owner = isOwner;
    }

    public void SetAuthoritativePlacement(Vector2Int cell, int rotationY)
    {
        authoritativeCell = cell;
        authoritativeRotationY = NormalizeRotation(rotationY);
        hasAuthoritativePlacement = true;
    }

    public void RequestInitialRegistrationSend()
    {
        if (!owner || !hasAuthoritativePlacement)
            return;

        pendingInitialRegistrationSend = true;
        resendRemaining = ResendCount;
        nextResendTime = 0f;
    }

    private void TryRegisterAuthoritativePlacement()
    {
        if (GridManager.Instance == null || !hasAuthoritativePlacement || isRegisteredLocally)
            return;

        if (GridManager.Instance.TryPlace(authoritativeCell, CellType.Path, gameObject))
        {
            RegisterNode();
            isRegisteredLocally = true;
            return;
        }

        if (GridManager.Instance.TryGetCell(authoritativeCell, out var data) &&
            data.type == CellType.Path &&
            (data.placedObject == gameObject ||
             (data.placedObject == null && GridManager.Instance.TrySetPlacedObject(authoritativeCell, gameObject))))
        {
            RegisterNode();
            PathChecker.Instance?.SetNodeObject(authoritativeCell, gameObject);
            isRegisteredLocally = true;
            return;
        }

        string occupant = GridManager.Instance.TryGetCell(authoritativeCell, out data) && data.placedObject != null
            ? $"{data.type} ({data.placedObject.name})"
            : "empty or unknown occupant";

        Debug.LogWarning($"[PathBlockAgent] Failed to register authoritative path cell {authoritativeCell} for '{name}'. Existing occupant: {occupant}");
    }

    private void RegisterNode()
    {
        if (PathChecker.Instance == null)
            return;

        PathNode node = isStraight
            ? PathNode.Straight(authoritativeRotationY)
            : PathNode.Corner(authoritativeRotationY);

        PathChecker.Instance.RegisterNode(authoritativeCell, node, gameObject);
        PathChecker.Instance.CheckPath();
    }

    private static int NormalizeRotation(int rotationY)
    {
        int normalized = rotationY % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized;
    }
}
