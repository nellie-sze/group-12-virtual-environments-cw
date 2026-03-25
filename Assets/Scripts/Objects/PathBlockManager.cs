using UnityEngine;
using System.Collections.Generic;
using Ubiq.Spawning;
using Ubiq.Rooms;
using Ubiq.Messaging;

/// <summary>
/// Singleton that handles networked placement and removal of path blocks.
/// GridSystem calls RequestPlace; DeleteTool calls RequestRemove.
/// </summary>
public class PathBlockManager : MonoBehaviour
{
    public static PathBlockManager Instance { get; private set; }

    [Header("Prefabs (must also be in NSM Prefab Catalogue)")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;

    private NetworkContext context;
    private NetworkSpawnManager spawnManager;

    private struct NetMessage
    {
        public string type;   // "remove"
        public int cellX, cellY;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        context      = NetworkScene.Register(this);
        spawnManager = NetworkSpawnManager.Find(this);

        var localManager = GetComponent<NetworkSpawnManager>();
        if (spawnManager != null && localManager != null && localManager != spawnManager)
            localManager.enabled = false;
        else if (spawnManager == null && localManager != null)
            spawnManager = localManager;

        if (spawnManager != null)
            spawnManager.OnSpawned.AddListener(OnNetworkSpawned);
    }

    void OnDestroy()
    {
        if (spawnManager != null)
            spawnManager.OnSpawned.RemoveListener(OnNetworkSpawned);
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<NetMessage>();
        if (m.type == "remove")
            HandleRemove(new Vector2Int(m.cellX, m.cellY));
    }

    // ── Placement ────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a path block over the network and returns the local GameObject.
    /// GridSystem is responsible for registering cells and PathChecker nodes
    /// after this call returns.
    /// </summary>
    public GameObject RequestPlace(Vector3 worldPos, Quaternion rotation, bool isStraight)
    {
        if (spawnManager == null || spawnManager.catalogue == null) return null;

        GameObject prefab = isStraight ? straightPrefab : cornerPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[PathBlockManager] Prefab not assigned.");
            return null;
        }
        if (spawnManager.catalogue.IndexOf(prefab) < 0)
        {
            Debug.LogWarning($"[PathBlockManager] '{prefab.name}' not in NSM catalogue.");
            return null;
        }

        GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
        if (obj == null) return null;

        obj.transform.SetPositionAndRotation(worldPos, rotation);
        FitToSingleGridCell(obj);
        CenterOnPosition(obj, worldPos);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null) { sync.SetOwner(true); sync.RequestInitialSend(); }

        // Mark so this peer knows it can Despawn this object later.
        var agent = obj.GetComponent<PathBlockAgent>();
        if (agent != null)
        {
            Vector2Int cell = GridManager.Instance.WorldToGrid(worldPos);
            int rotationY = Mathf.RoundToInt(rotation.eulerAngles.y / 90f) * 90;

            agent.isLocallySpawned = true;
            agent.SetOwner(true);
            agent.SetAuthoritativePlacement(cell, rotationY);
            agent.RequestInitialRegistrationSend();
        }

        return obj;
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    /// <summary>Called by DeleteTool. Removes locally and broadcasts to all peers.</summary>
    public void RequestRemove(Vector2Int cell)
    {
        HandleRemove(cell);
        context.SendJson(new NetMessage { type = "remove", cellX = cell.x, cellY = cell.y });
    }

    public void RemoveAllPathBlocks()
    {
        if (GridManager.Instance == null) return;

        var pathCells = new List<Vector2Int>();
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Path)
            {
                pathCells.Add(kvp.Key);
            }
        }

        foreach (var cell in pathCells)
        {
            RequestRemove(cell);
        }

        Debug.Log($"[PathBlockManager] RemoveAllPathBlocks: removed {pathCells.Count} path cell(s).");
    }

    private void HandleRemove(Vector2Int cell)
    {
        if (GridManager.Instance == null) return;
        if (!GridManager.Instance.TryGetCell(cell, out var data)) return;
        if (data.type != CellType.Path) return;

        GameObject obj = data.placedObject;

        if (obj != null)
            GridManager.Instance.ClearCellsForObject(obj);
        else
            GridManager.Instance.RemoveCell(cell);

        if (PathChecker.Instance != null)
            PathChecker.Instance.UnregisterNode(cell);

        var agent = obj != null ? obj.GetComponent<PathBlockAgent>() : null;
        if (agent != null && agent.isLocallySpawned && spawnManager != null)
            spawnManager.Despawn(obj);
        else
            Destroy(obj);
    }

    // ── NSM callback ─────────────────────────────────────────────────────────

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        if (obj == null) return;
        var agent = obj.GetComponent<PathBlockAgent>();
        if (agent != null)
            agent.SetOwner(origin == NetworkSpawnOrigin.Local);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
    }

    // Shifts the object so its XZ visual centre sits exactly at targetPos,
    // replicating what ShovelTool's CreateCenteredWrapper used to do.
    private void CenterOnPosition(GameObject obj, Vector3 targetPos)
    {
        Renderer[] rends = obj.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        Vector3 offset = b.center - obj.transform.position;
        obj.transform.position -= new Vector3(offset.x, 0f, offset.z);
    }

    private void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null) return;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float maxWidthXZ = Mathf.Max(bounds.size.x, bounds.size.z);
        float cellSize   = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f) return;

        float targetMaxWidth = cellSize * 1f;
        if (maxWidthXZ <= targetMaxWidth) return;

        obj.transform.localScale *= targetMaxWidth / maxWidthXZ;
    }
}
