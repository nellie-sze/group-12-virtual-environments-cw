using System.Collections.Generic;
using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;
using Ubiq.Messaging;

public class StartFinishSpawner : MonoBehaviour
{
    [Header("Flag Markers")]
    [Tooltip("Unique prefab for the start flag (must have a different name to the finish prefab).")]
    public GameObject startPrefab;

    [Tooltip("Unique prefab for the finish flag (must have a different name to the start prefab).")]
    public GameObject finishPrefab;

    [Header("Base Blocks")]
    public GameObject startBlockPrefab;
    public Color startBlockColor = new Color(0.0f, 0.8f, 0.2f, 1f);

    public GameObject finishBlockPrefab;
    public Color finishBlockColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Header("References")]
    public GridSystem gridSystem;
    public float blockYOffset = -1f;

    private NetworkContext context;
    private RoomClient roomClient;
    private NetworkSpawnManager spawnManager;
    private bool hasSpawned;
    private string lastRequestId;
    private bool hasCurrentLayout;
    private Vector2Int currentStartCell;
    private Vector2Int currentFinishCell;

    private struct NetMessage
    {
        public string requestId;
        public int startX;
        public int startY;
        public int finishX;
        public int finishY;
    }

    void Start()
    {
        context = NetworkScene.Register(this);
        roomClient = FindFirstObjectByType<RoomClient>();
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
        HandleSpawnRequest(m);
    }

    public void SpawnAll()
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;

        Vector2Int startCell;
        Vector2Int finishCell;
        do
        {
            if (Random.value < 0.5f)
            {
                startCell = new Vector2Int(gridMin.x, Random.Range(gridMin.y + 1, gridMax.y));
                finishCell = new Vector2Int(gridMax.x, Random.Range(gridMin.y + 1, gridMax.y));
            }
            else
            {
                startCell = new Vector2Int(Random.Range(gridMin.x + 1, gridMax.x), gridMin.y);
                finishCell = new Vector2Int(Random.Range(gridMin.x + 1, gridMax.x), gridMax.y);
            }
        }
        while (IsCornerCell(startCell, gridMin, gridMax) || IsCornerCell(finishCell, gridMin, gridMax));

        var msg = new NetMessage
        {
            requestId = System.Guid.NewGuid().ToString("N"),
            startX = startCell.x,
            startY = startCell.y,
            finishX = finishCell.x,
            finishY = finishCell.y
        };

        HandleSpawnRequest(msg);
        context.SendJson(msg);
    }

    private void HandleSpawnRequest(NetMessage m)
    {
        if (hasSpawned)
            return;

        if (!string.IsNullOrEmpty(lastRequestId) && lastRequestId == m.requestId)
            return;

        lastRequestId = m.requestId;
        currentStartCell = new Vector2Int(m.startX, m.startY);
        currentFinishCell = new Vector2Int(m.finishX, m.finishY);
        hasCurrentLayout = true;

        if (!IsLeaderPeer())
        {
            RegisterSpecialCell(currentStartCell, CellType.Start);
            RegisterSpecialCell(currentFinishCell, CellType.Finish);
            return;
        }

        hasSpawned = true;
        DoSpawn(currentStartCell, currentFinishCell);
    }

    private void DoSpawn(Vector2Int startCell, Vector2Int finishCell)
    {
        if (IsCornerCell(startCell, GridManager.Instance.gridMin, GridManager.Instance.gridMax))
            Debug.LogError($"[StartFinishSpawner] Start spawned on forbidden corner cell {startCell}.");

        if (IsCornerCell(finishCell, GridManager.Instance.gridMin, GridManager.Instance.gridMax))
            Debug.LogError($"[StartFinishSpawner] Finish spawned on forbidden corner cell {finishCell}.");

        SpawnMarker(startPrefab, startCell, CellType.Start);
        SpawnMarker(finishPrefab, finishCell, CellType.Finish);
    }

    void SpawnMarker(GameObject prefab, Vector2Int cell, CellType type)
    {
        if (prefab == null || spawnManager == null)
            return;

        if (spawnManager.catalogue == null || spawnManager.catalogue.IndexOf(prefab) < 0)
        {
            Debug.LogWarning($"[StartFinishSpawner] {type} prefab not in NSM catalogue - using local Instantiate.");
            var fallback = Instantiate(prefab, GridManager.Instance.GridToWorld(cell), Quaternion.identity);
            if (GridManager.Instance.TryPlace(cell, type, fallback))
                RegisterSpecialCellNode(cell, fallback);
            return;
        }

        Vector3 pos = GridManager.Instance.GridToWorld(cell);
        GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
        if (obj == null)
            return;

        var agent = obj.GetComponent<ObstacleAgent>();
        if (agent != null)
        {
            agent.obstacleType = type;
            agent.SetOwner(true);
            agent.SetAuthoritativeCell(cell);
        }

        obj.transform.SetPositionAndRotation(pos, Quaternion.identity);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
        {
            sync.SetOwner(true);
            sync.RequestInitialSend();
        }

        if (GridManager.Instance.TryPlace(cell, type, obj))
        {
            RegisterSpecialCellNode(cell, obj);
            if (agent != null)
            {
                agent.MarkAsRegisteredByLeader();
                agent.RequestInitialCellSend();
            }
        }
        else
        {
            spawnManager.Despawn(obj);
        }
    }

    public void RemoveAllMarkers()
    {
        if (GridManager.Instance == null)
            return;

        var markerCells = new List<Vector2Int>();
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Start || kvp.Value.type == CellType.Finish)
                markerCells.Add(kvp.Key);
        }

        foreach (var cell in markerCells)
        {
            if (!GridManager.Instance.TryGetCell(cell, out var data))
                continue;

            var obj = data.placedObject;
            if (obj != null)
                GridManager.Instance.ClearCellsForObject(obj);
            else
                GridManager.Instance.RemoveCell(cell);

            PathChecker.Instance?.UnregisterNode(cell);

            if (IsLeaderPeer() && spawnManager != null && obj != null)
                spawnManager.Despawn(obj);
            else if (obj != null)
                Destroy(obj);
        }

        hasSpawned = false;
        lastRequestId = null;
        hasCurrentLayout = false;

        Debug.Log($"[StartFinishSpawner] RemoveAllMarkers: removed {markerCells.Count} start/finish marker cell(s).");
    }

    void SpawnBaseBlock(GameObject prefab, Vector2Int cell)
    {
        if (prefab == null || spawnManager == null)
            return;

        if (spawnManager.catalogue == null || spawnManager.catalogue.IndexOf(prefab) < 0)
        {
            Debug.LogWarning("[StartFinishSpawner] Block prefab not in NSM catalogue - using local Instantiate.");
            Instantiate(prefab, GridManager.Instance.GridToWorld(cell) + new Vector3(0f, blockYOffset, 0f), Quaternion.identity);
            return;
        }

        Vector3 pos = GridManager.Instance.GridToWorld(cell) + new Vector3(0f, blockYOffset, 0f);
        GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
        if (obj == null)
            return;

        obj.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
        {
            sync.SetOwner(true);
            sync.RequestInitialSend();
        }
    }

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        if (obj == null)
            return;

        var agent = obj.GetComponent<ObstacleAgent>();
        if (agent != null)
        {
            agent.SetOwner(origin == NetworkSpawnOrigin.Local);

            if (hasCurrentLayout)
            {
                if (startPrefab != null && obj.name.StartsWith(startPrefab.name))
                {
                    agent.obstacleType = CellType.Start;
                    BindSpecialCellObject(currentStartCell, obj);
                }
                else if (finishPrefab != null && obj.name.StartsWith(finishPrefab.name))
                {
                    agent.obstacleType = CellType.Finish;
                    BindSpecialCellObject(currentFinishCell, obj);
                }
            }
        }

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
    }

    private bool IsLeaderPeer()
    {
        if (roomClient == null || roomClient.Me == null)
            return true;

        var leaderUuid = roomClient.Me.uuid;
        foreach (var p in roomClient.Peers)
            if (string.CompareOrdinal(p.uuid, leaderUuid) < 0)
                leaderUuid = p.uuid;

        return roomClient.Me.uuid == leaderUuid;
    }

    private static bool IsCornerCell(Vector2Int cell, Vector2Int gridMin, Vector2Int gridMax)
    {
        return (cell.x == gridMin.x || cell.x == gridMax.x) &&
               (cell.y == gridMin.y || cell.y == gridMax.y);
    }

    private void RegisterSpecialCell(Vector2Int cell, CellType type)
    {
        if (GridManager.Instance == null)
            return;

        if (!GridManager.Instance.TryGetCell(cell, out var existing))
        {
            GridManager.Instance.TryPlace(cell, type, null);
        }
        else if (existing.type != type)
        {
            Debug.LogWarning($"[StartFinishSpawner] Special cell mismatch at {cell}. Existing type {existing.type}, incoming type {type}.");
        }

        RegisterSpecialCellNode(cell, null);
    }

    private void RegisterSpecialCellNode(Vector2Int cell, GameObject obj)
    {
        if (PathChecker.Instance == null)
            return;

        PathChecker.Instance.RegisterNode(cell, PathNode.Omnidirectional(), obj);
    }

    private void BindSpecialCellObject(Vector2Int cell, GameObject obj)
    {
        if (GridManager.Instance == null || obj == null)
            return;

        GridManager.Instance.TrySetPlacedObject(cell, obj);
        PathChecker.Instance?.SetNodeObject(cell, obj);
    }
}
