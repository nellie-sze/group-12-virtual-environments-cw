using System.Collections;
using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;
using Ubiq.Messaging;

public class StartFinishSpawner : MonoBehaviour
{
    [Header("Flag Markers")]
    [Tooltip("Unique prefab for the start flag (must have a different name to the finish prefab).")]
    public GameObject startPrefab;
    public Color      startColor = Color.green;

    [Tooltip("Unique prefab for the finish flag (must have a different name to the start prefab).")]
    public GameObject finishPrefab;
    public Color      finishColor = Color.red;

    [Header("Base Blocks")]
    public GameObject startBlockPrefab;
    public Color      startBlockColor = new Color(0.0f, 0.8f, 0.2f, 1f);

    public GameObject finishBlockPrefab;
    public Color      finishBlockColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Header("References")]
    public GridSystem gridSystem;
    public float blockYOffset = -1f;

    private NetworkContext context;
    private RoomClient roomClient;
    private NetworkSpawnManager spawnManager;
    private bool hasSpawned;
    private string lastRequestId;

    private struct NetMessage
    {
        public string requestId;
        public int startX, startY;
        public int finishX, finishY;
    }

    void Start()
    {
        context      = NetworkScene.Register(this);
        roomClient   = FindFirstObjectByType<RoomClient>();
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

        Vector2Int startCell, finishCell;
        if (Random.value < 0.5f)
        {
            startCell  = new Vector2Int(gridMin.x, Random.Range(gridMin.y, gridMax.y + 1));
            finishCell = new Vector2Int(gridMax.x, Random.Range(gridMin.y, gridMax.y + 1));
        }
        else
        {
            startCell  = new Vector2Int(Random.Range(gridMin.x, gridMax.x + 1), gridMin.y);
            finishCell = new Vector2Int(Random.Range(gridMin.x, gridMax.x + 1), gridMax.y);
        }

        var msg = new NetMessage
        {
            requestId = System.Guid.NewGuid().ToString("N"),
            startX  = startCell.x,  startY  = startCell.y,
            finishX = finishCell.x, finishY = finishCell.y
        };

        HandleSpawnRequest(msg);
        context.SendJson(msg);
    }

    private void HandleSpawnRequest(NetMessage m)
    {
        if (hasSpawned) return;
        if (!string.IsNullOrEmpty(lastRequestId) && lastRequestId == m.requestId) return;
        lastRequestId = m.requestId;

        StartCoroutine(RegisterSpecialCellsDelayed(1f));

        if (!IsLeaderPeer()) return;

        hasSpawned = true;
        DoSpawn(new Vector2Int(m.startX, m.startY), new Vector2Int(m.finishX, m.finishY));
    }

    private void DoSpawn(Vector2Int startCell, Vector2Int finishCell)
    {
        SpawnMarker(startPrefab,     startColor,      startCell,  CellType.Start);
        SpawnMarker(finishPrefab,    finishColor,     finishCell, CellType.Finish);
        SpawnBaseBlock(startBlockPrefab,  startBlockColor,  startCell);
        SpawnBaseBlock(finishBlockPrefab, finishBlockColor, finishCell);
    }

    void SpawnMarker(GameObject prefab, Color color, Vector2Int cell, CellType type)
    {
        if (prefab == null || spawnManager == null) return;

        if (spawnManager.catalogue == null || spawnManager.catalogue.IndexOf(prefab) < 0)
        {
            Debug.LogWarning($"[StartFinishSpawner] {type} prefab not in NSM catalogue — using local Instantiate.");
            var fb = Instantiate(prefab, GridManager.Instance.GridToWorld(cell), Quaternion.identity);
            ApplyColor(fb, color);
            GridManager.Instance.TryPlace(cell, type, fb);
            return;
        }

        Vector3    pos = GridManager.Instance.GridToWorld(cell);
        GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
        if (obj == null) return;

        obj.transform.SetPositionAndRotation(pos, Quaternion.identity);
        ApplyColor(obj, color);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null) { sync.SetOwner(true); sync.RequestInitialSend(); }

        if (GridManager.Instance.TryPlace(cell, type, obj))
            obj.GetComponent<ObstacleAgent>()?.MarkAsRegisteredByLeader();
        else
            spawnManager.Despawn(obj);
    }

    void SpawnBaseBlock(GameObject prefab, Color color, Vector2Int cell)
    {
        if (prefab == null || spawnManager == null) return;

        if (spawnManager.catalogue == null || spawnManager.catalogue.IndexOf(prefab) < 0)
        {
            Debug.LogWarning($"[StartFinishSpawner] Block prefab not in NSM catalogue — using local Instantiate.");
            var fb = Instantiate(prefab, GridManager.Instance.GridToWorld(cell) + new Vector3(0f, blockYOffset, 0f), Quaternion.identity);
            ApplyColor(fb, color);
            return;
        }

        Vector3    pos = GridManager.Instance.GridToWorld(cell) + new Vector3(0f, blockYOffset, 0f);
        GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
        if (obj == null) return;

        obj.transform.SetPositionAndRotation(pos, Quaternion.identity);
        ApplyColor(obj, color);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null) { sync.SetOwner(true); sync.RequestInitialSend(); }
    }

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        if (obj == null) return;

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);

        // Re-apply colours on remote peers. Because startPrefab and finishPrefab now have
        // unique names (e.g. Flag_Start / Flag_Finish) the name check reliably identifies each.
        if (origin == NetworkSpawnOrigin.Remote)
        {
            string n = obj.name.Replace("(Clone)", "").Trim();
            if      (startPrefab       != null && n == startPrefab.name)       ApplyColor(obj, startColor);
            else if (finishPrefab      != null && n == finishPrefab.name)      ApplyColor(obj, finishColor);
            else if (startBlockPrefab  != null && n == startBlockPrefab.name)  ApplyColor(obj, startBlockColor);
            else if (finishBlockPrefab != null && n == finishBlockPrefab.name) ApplyColor(obj, finishBlockColor);
        }
    }

    private IEnumerator RegisterSpecialCellsDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gridSystem != null)
            gridSystem.RegisterSpecialCells();
        else
            Debug.LogWarning("[StartFinishSpawner] GridSystem not assigned — Start/Finish path nodes not registered.");
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

    static void ApplyColor(GameObject obj, Color color)
    {
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            }
    }
}
