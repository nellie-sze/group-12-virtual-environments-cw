using System.Collections.Generic;
using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;
using Ubiq.Messaging;

public class LavaSpawner : MonoBehaviour
{
    public static LavaSpawner Instance { get; private set; }

    [Header("Lava Settings")]
    [Tooltip("Lava tile prefab — must be in the Ubiq Prefab Catalogue")]
    public GameObject lavaPrefab;

    [Tooltip("How many separate lava patches to spawn across the grid")]
    public int lavaCellCount = 1;

    [Tooltip("Small vertical lift so the tile sits visibly on top of the surface")]
    public float yOffset = 0.02f;

    NetworkSpawnManager spawnManager;

    private NetworkContext context;
    private RoomClient roomClient;
    private bool hasSpawned;
    private string lastRequestId;

    // Extended message — now handles both spawn-all and remove, matching ObstacleSpawner.
    private struct NetMessage
    {
        public string type; // "spawnAll" | "remove"
        public string requestId; // spawnAll only
        public int cellX; // remove only
        public int cellY; // remove only
    }

    // Possible patch sizes: (width, height) in grid cells
    private static readonly Vector2Int[] patchSizes = new Vector2Int[]
    {
        new Vector2Int(4, 2),
        new Vector2Int(4, 1),
        new Vector2Int(5, 2),
        new Vector2Int(5, 3),
    };

    void Awake()
    {
        // Singleton so WaterBucketPowerup can call LavaSpawner.Instance.RequestRemove
        if (Instance != null && Instance != this) 
        { 
            Destroy(gameObject); 
            return; 
        }
        Instance = this;
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        roomClient = FindFirstObjectByType<RoomClient>();

        spawnManager = NetworkSpawnManager.Find(this);

        var localManager = GetComponent<NetworkSpawnManager>();
        if (spawnManager != null && localManager != null && localManager != spawnManager)
        {
            localManager.enabled = false;
            Debug.LogWarning($"LavaSpawner: Disabled extra NetworkSpawnManager on {gameObject.name}. Using {spawnManager.gameObject.name}.");
        }
        else if (spawnManager == null && localManager != null)
        {
            spawnManager = localManager;
        }

        if (spawnManager != null)
            spawnManager.OnSpawned.AddListener(OnNetworkSpawned);
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
        }
    }

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        if (obj == null || lavaPrefab == null) return;

        var name = obj.name;
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix))
            name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();

        if (name != lavaPrefab.name) return;

        ScaleToCell(obj);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
    }

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
        if (lavaPrefab == null)
        {
            Debug.LogWarning("LavaSpawner: lavaPrefab not assigned.");
            return;
        }

        if (spawnManager == null)
        {
            Debug.LogError("LavaSpawner: NetworkSpawnManager not found in scene.");
            return;
        }

        if (spawnManager.catalogue == null)
        {
            Debug.LogError("LavaSpawner: NetworkSpawnManager has no Prefab Catalogue assigned.");
            return;
        }

        if (spawnManager.catalogue.IndexOf(lavaPrefab) < 0)
        {
            Debug.LogError($"LavaSpawner: Prefab '{lavaPrefab.name}' not in Prefab Catalogue.");
            return;
        }

        int placed = 0;
        for (int i = 0; i < lavaCellCount; i++)
        {
            Vector2Int size = patchSizes[Random.Range(0, patchSizes.Length)];
            bool success = TrySpawnPatch(size);
            if (success) placed++;
            else Debug.LogWarning($"LavaSpawner: Could not place patch {i + 1} — no valid position found.");
        }

        Debug.Log($"LavaSpawner: Placed {placed}/{lavaCellCount} lava patches.");
    }

    // Remove (networked) — mirrors ObstacleSpawner.RequestRemove 
    // Broadcasts removal to all peers so GridManager + visuals stay in sync.
    public void RequestRemove(Vector2Int cell)
    {
        HandleRemove(cell);
        context.SendJson(new NetMessage { type = "remove", cellX = cell.x, cellY = cell.y });
    }

    private void HandleRemove(Vector2Int cell)
    {
        if (GridManager.Instance == null) return;
        if (!GridManager.Instance.TryGetCell(cell, out var data)) return;
        if (data.type != CellType.Lava) return; // safety — only remove lava cells

        GameObject obj = data.placedObject;

        // Clear GridManager entry first (same pattern as ObstacleSpawner.HandleRemove)
        GridManager.Instance.ClearCellsForObject(obj);

        // Leader uses NSM.Despawn; non-leaders destroy directly
        if (IsLeaderPeer() && spawnManager != null)
            spawnManager.Despawn(obj);
        else
            Destroy(obj);
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

    bool TrySpawnPatch(Vector2Int size)
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;

        var candidates = new List<Vector2Int>();
        for (int x = gridMin.x + 1; x <= gridMax.x - size.x; x++)
            for (int y = gridMin.y + 1; y <= gridMax.y - size.y; y++)
                candidates.Add(new Vector2Int(x, y));

        Shuffle(candidates);

        foreach (Vector2Int origin in candidates)
        {
            if (!PatchIsFree(origin, size)) continue;
            if (PatchAdjacentToStartFinish(origin, size)) continue;
            PlacePatch(origin, size);
            return true;
        }

        return false;
    }

    bool PatchIsFree(Vector2Int origin, Vector2Int size)
    {
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
                if (GridManager.Instance.IsOccupied(new Vector2Int(x, y)))
                    return false;
        return true;
    }

    bool PatchAdjacentToStartFinish(Vector2Int origin, Vector2Int size)
    {
        if (GridManager.Instance == null) return false;

        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        for (int x = origin.x; x < origin.x + size.x; x++)
        {
            for (int y = origin.y; y < origin.y + size.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                foreach (Vector2Int dir in directions)
                {
                    Vector2Int neighbor = cell + dir;
                    if (!GridManager.Instance.TryGetCell(neighbor, out var data)) continue;

                    if (data.type == CellType.Start || data.type == CellType.Finish)
                        return true;
                }
            }
        }
        return false;
    }

    void PlacePatch(Vector2Int origin, Vector2Int size)
    {
        for (int x = origin.x; x < origin.x + size.x; x++)
        {
            for (int y = origin.y; y < origin.y + size.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Vector3 pos = GridManager.Instance.GridToWorld(cell);
                pos.y += yOffset;

                GameObject obj = spawnManager.SpawnWithPeerScope(lavaPrefab);
                if (obj == null)
                {
                    Debug.LogError($"LavaSpawner: Failed to spawn lava tile. Is it in the Prefab Catalogue?");
                    continue;
                }

                obj.transform.SetPositionAndRotation(pos, Quaternion.identity);
                ScaleToCell(obj);

                var sync = obj.GetComponent<NetworkedSpawnedTransform>();
                if (sync != null)
                {
                    sync.SetOwner(true);
                    sync.RequestInitialSend();
                }

                if (GridManager.Instance.TryPlace(cell, CellType.Lava, obj))
                    obj.GetComponent<ObstacleAgent>()?.MarkAsRegisteredByLeader();
                else
                {
                    Debug.LogWarning($"LavaSpawner: TryPlace failed at {cell} — despawning.");
                    spawnManager.Despawn(obj);
                }
            }
        }
    }

    void ScaleToCell(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        float footprint = Mathf.Max(combined.size.x, combined.size.z);
        if (footprint <= 0.0001f) return;

        float scaleFactor = GridManager.Instance.gridSize / footprint;
        obj.transform.localScale *= scaleFactor;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
