using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;
using Ubiq.Messaging;
using System.Collections.Generic;

public class ObstacleSpawner : MonoBehaviour
{
    public static ObstacleSpawner Instance { get; private set; }

    [Header("Placement")]
    [Tooltip("Buffer from the grid edge (matches GridSystem's placement radius check)")]
    public float edgeBufferRadius = 0.2f;
    private NetworkSpawnManager spawnManager;
    private bool isSpawning;

    private NetworkContext context;
    private RoomClient roomClient;
    private bool hasSpawned;
    private string lastRequestId;

    // Single message type handles both spawn-all requests and remove requests.
    private struct NetMessage
    {
        public string type;      // "spawnAll" | "remove"
        public string requestId; // spawnAll only
        public int    cellX;     // remove only
        public int    cellY;     // remove only
    }

    [Header("Trees")]
    public GameObject[] treePrefabs;
    public int treeCount = 5;

    [Header("Rocks")]
    public GameObject[] rockPrefabs;
    public int rockCount = 5;

    [Header("Flowers")]
    public GameObject[] flowerPrefabs;
    public int flowerCount = 5;

    public bool spawnOnStart;

    private const float GridFitMargin = 0.95f;
    private const float FlowerScaleMultiplier = 0.33f;
    private const float TreePostFitMultiplier = 1.6f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        context    = NetworkScene.Register(this);
        roomClient = FindFirstObjectByType<RoomClient>();
        spawnManager = NetworkSpawnManager.Find(this);

        var localManager = GetComponent<NetworkSpawnManager>();
        if (spawnManager != null && localManager != null && localManager != spawnManager)
        {
            localManager.enabled = false;
            Debug.LogWarning($"ObstacleSpawner: Disabled extra NetworkSpawnManager on {gameObject.name}.");
        }
        else if (spawnManager == null && localManager != null)
        {
            spawnManager = localManager;
        }

        if (spawnManager != null)
        {
            spawnManager.OnSpawned.AddListener(OnNetworkSpawned);
            spawnManager.OnDespawned.AddListener(OnNetworkDespawned);
        }

        if (spawnOnStart)
            SpawnAll();
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<NetMessage>();
        if (m.type == "spawnAll")
            HandleSpawnRequest(m.requestId);
        else if (m.type == "remove")
            HandleRemove(new Vector2Int(m.cellX, m.cellY));
    }

    void OnDestroy()
    {
        if (spawnManager != null)
        {
            spawnManager.OnSpawned.RemoveListener(OnNetworkSpawned);
            spawnManager.OnDespawned.RemoveListener(OnNetworkDespawned);
        }
    }

    // ── Spawn ──────────────────────────────────────────────────────────────────

    public void SpawnAll()
    {
        var requestId = System.Guid.NewGuid().ToString("N");
        HandleSpawnRequest(requestId);
        context.SendJson(new NetMessage { type = "spawnAll", requestId = requestId });
    }

    private void HandleSpawnRequest(string requestId)
    {
        if (hasSpawned) return;
        if (!string.IsNullOrEmpty(lastRequestId) && lastRequestId == requestId) return;

        lastRequestId = requestId;
        if (!IsLeaderPeer()) return;

        hasSpawned = true;
        DoSpawnAll();
    }

    private void DoSpawnAll()
    {
        if (isSpawning) return;
        isSpawning = true;
        Debug.Log("Spawning obstacles...");
        SpawnN(treePrefabs,   CellType.Tree,   treeCount);
        SpawnN(rockPrefabs,   CellType.Rock,   rockCount);
        SpawnN(flowerPrefabs, CellType.Flower, flowerCount);
        isSpawning = false;
    }

    // ── Remove (networked) ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by tools (AxeTool, PickaxeTool) instead of GridManager.TryRemove.
    /// Broadcasts removal to all peers so GridManager + visuals stay in sync.
    /// </summary>
    public void RequestRemove(Vector2Int cell)
    {
        HandleRemove(cell);
        context.SendJson(new NetMessage { type = "remove", cellX = cell.x, cellY = cell.y });
    }

    public void RemoveAllObstacles()
    {
        if (GridManager.Instance == null) return;

        var obstacleCells = new List<Vector2Int>();
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Tree || kvp.Value.type == CellType.Rock || kvp.Value.type == CellType.Flower)
            {
                obstacleCells.Add(kvp.Key);
            }
        }

        foreach (var cell in obstacleCells)
        {
            RequestRemove(cell);
        }

        Debug.Log($"[ObstacleSpawner] RemoveAllObstacles: removed {obstacleCells.Count} obstacle cell(s).");
    }

    private void HandleRemove(Vector2Int cell)
    {
        if (GridManager.Instance == null) return;
        if (!GridManager.Instance.TryGetCell(cell, out var data)) return;

        GameObject obj = data.placedObject;

        // Clear the GridManager entry first (no Destroy yet).
        GridManager.Instance.ClearCellsForObject(obj);

        // The peer that originally spawned the objects (the leader) uses NSM.Despawn —
        // this clears the peer-property so late-joining peers won't see the dead obstacle,
        // and also calls Destroy. Non-leader peers destroy the object directly.
        if (IsLeaderPeer() && spawnManager != null)
            spawnManager.Despawn(obj);
        else
            Destroy(obj);
    }

    // ── NSM callbacks ──────────────────────────────────────────────────────────

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        var agent = obj.GetComponent<ObstacleAgent>();
        if (agent == null) return;

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
    }

    // Safety: if the NSM despawn arrives after HandleRemove already ran, this is a no-op.
    private void OnNetworkDespawned(GameObject obj, IRoom room, IPeer peer)
    {
        var agent = obj != null ? obj.GetComponent<ObstacleAgent>() : null;
        if (agent == null) return;

        if (GridManager.Instance != null)
            GridManager.Instance.ClearCellsForObject(obj);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null) return;

        if (IsSpawnedFromPrefabSet(obj, flowerPrefabs))
        {
            obj.transform.localScale *= FlowerScaleMultiplier;
            return;
        }

        float maxWidthXZ = GetMaxWidthXZ(obj);
        float cellSize = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f) return;

        ShrinkToTargetWidth(obj, maxWidthXZ, cellSize * GridFitMargin);

        if (IsSpawnedFromPrefabSet(obj, treePrefabs))
        {
            obj.transform.localScale *= TreePostFitMultiplier;
        }
    }

    private float GetMaxWidthXZ(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return Mathf.Max(bounds.size.x, bounds.size.z);
    }

    private void ShrinkToTargetWidth(GameObject obj, float currentMaxWidthXZ, float targetMaxWidthXZ)
    {
        if (currentMaxWidthXZ <= targetMaxWidthXZ) return;

        float scaleFactor = targetMaxWidthXZ / currentMaxWidthXZ;
        obj.transform.localScale *= scaleFactor;
    }

    private bool IsSpawnedFromPrefabSet(GameObject obj, GameObject[] prefabSet)
    {
        if (obj == null || prefabSet == null) return false;

        foreach (var prefab in prefabSet)
        {
            if (prefab != null && obj.name.StartsWith(prefab.name))
                return true;
        }

        return false;
    }

    void SpawnN(GameObject[] prefabs, CellType type, int count)
    {
        if (prefabs == null || prefabs.Length == 0) return;
        if (spawnManager == null || spawnManager.catalogue == null) return;

        var freeCells = GetShuffledFreeCells();
        int placed = 0;

        foreach (Vector2Int cell in freeCells)
        {
            if (placed >= count) break;
            if (GridManager.Instance.IsOccupied(cell)) continue;

            Vector3    pos    = GridManager.Instance.GridToWorld(cell);
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];

            if (spawnManager.catalogue.IndexOf(prefab) < 0) continue;

            GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
            if (obj == null) continue;

            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            obj.transform.SetPositionAndRotation(pos, rotation);
            FitToSingleGridCell(obj);

            var sync = obj.GetComponent<NetworkedSpawnedTransform>();
            if (sync != null)
            {
                sync.SetOwner(true);
                sync.RequestInitialSend();
            }

            if (GridManager.Instance.TryPlace(cell, type, obj))
            {
                placed++;
                obj.GetComponent<ObstacleAgent>()?.MarkAsRegisteredByLeader();
            }
            else
            {
                spawnManager.Despawn(obj);
            }
        }
    }

    private bool IsLeaderPeer()
    {
        if (roomClient == null || roomClient.Me == null) return true;

        var leaderUuid = roomClient.Me.uuid;
        foreach (var p in roomClient.Peers)
            if (string.CompareOrdinal(p.uuid, leaderUuid) < 0)
                leaderUuid = p.uuid;

        return roomClient.Me.uuid == leaderUuid;
    }

    List<Vector2Int> GetShuffledFreeCells()
    {
        var cells = new List<Vector2Int>();
        Vector2Int min = GridManager.Instance.gridMin;
        Vector2Int max = GridManager.Instance.gridMax;

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (GridManager.Instance.IsOccupied(cell)) continue;

                Vector3 pos = GridManager.Instance.GridToWorld(cell);
                if (!GridManager.Instance.IsWithinGridSurfaceBuffered(pos, edgeBufferRadius)) continue;

                cells.Add(cell);
            }
        }

        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        return cells;
    }
}
