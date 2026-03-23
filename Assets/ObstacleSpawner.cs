using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;
using System.Collections.Generic;

public class ObstacleSpawner : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Buffer from the grid edge (matches GridSystem's placement radius check)")]
    public float edgeBufferRadius = 0.5f;
    NetworkSpawnManager spawnManager;
    private HashSet<string> obstaclePrefabNames;
    private bool isSpawning;

    [Header("Trees")]
    [Tooltip("Add as many tree prefab variants as you like — one is picked randomly per spawn")]
    public GameObject[] treePrefabs;
    public int treeCount = 5;

    [Header("Rocks")]
    [Tooltip("Add as many rock prefab variants as you like — one is picked randomly per spawn")]
    public GameObject[] rockPrefabs;
    public int rockCount = 5;

    [Header("Flowers")]
    [Tooltip("Flowers are removed by the axe tool, same as trees")]
    public GameObject[] flowerPrefabs;
    public int flowerCount = 5;
    public bool spawnOnStart;
    void Start()
    {
        spawnManager = NetworkSpawnManager.Find(this);

        // If this GameObject also has a NetworkSpawnManager component (common when experimenting),
        // and it's not the one Ubiq resolves via Find(), disable it. Otherwise two managers will
        // both listen to spawn events and you'll get duplicate spawned objects parented under
        // different transforms (e.g., under "Game Manager").
        var localManager = GetComponent<NetworkSpawnManager>();
        if (spawnManager != null && localManager != null && localManager != spawnManager)
        {
            localManager.enabled = false;
            Debug.LogWarning($"ObstacleSpawner: Disabled extra NetworkSpawnManager on {gameObject.name} to prevent duplicate spawns. Using {spawnManager.gameObject.name}.");
        }
        else if (spawnManager == null && localManager != null)
        {
            spawnManager = localManager;
        }

        if (spawnManager != null)
        {
            spawnManager.OnSpawned.AddListener(OnNetworkSpawned);
        }

        obstaclePrefabNames = BuildObstaclePrefabNameSet();
        
        if (spawnOnStart)
        {
            SpawnAll();
        }
    }

    void OnDestroy()
    {
        if (spawnManager != null)
        {
            spawnManager.OnSpawned.RemoveListener(OnNetworkSpawned);
        }
    }

    private HashSet<string> BuildObstaclePrefabNameSet()
    {
        var set = new HashSet<string>();
        AddPrefabNames(treePrefabs, set);
        AddPrefabNames(rockPrefabs, set);
        AddPrefabNames(flowerPrefabs, set);
        return set;
    }

    private static void AddPrefabNames(GameObject[] prefabs, HashSet<string> set)
    {
        if (prefabs == null)
        {
            return;
        }

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
            {
                set.Add(prefabs[i].name);
            }
        }
    }

    private bool IsKnownObstacleInstance(GameObject obj)
    {
        if (obj == null || obstaclePrefabNames == null)
        {
            return false;
        }

        var name = obj.name;
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix))
        {
            name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();
        }

        return obstaclePrefabNames.Contains(name);
    }

    private void FitToSingleGridCell(GameObject obj)
    {
        if (obj == null || GridManager.Instance == null)
        {
            return;
        }

        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float maxWidthXZ = Mathf.Max(bounds.size.x, bounds.size.z);
        float cellSize = GridManager.Instance.gridSize;
        if (cellSize <= 0f || maxWidthXZ <= 0f)
        {
            return;
        }

        // Keep a small margin so it doesn't touch the cell edges visually.
        float targetMaxWidth = cellSize * 0.95f;
        if (maxWidthXZ <= targetMaxWidth)
        {
            return;
        }

        float scaleFactor = targetMaxWidth / maxWidthXZ;
        obj.transform.localScale = obj.transform.localScale * scaleFactor;
    }

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        if (!IsKnownObstacleInstance(obj))
        {
            return;
        }

        FitToSingleGridCell(obj);

        // If the prefab has NetworkedSpawnedTransform, set ownership so it can publish the initial transform.
        // Note: adding this component at runtime won't work because it won't receive a NetworkId from the spawner.
        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
        {
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
        }
    }

    public void SpawnAll()
    {
        if (isSpawning)
        {
            return;
        }

        isSpawning = true;
        Debug.Log("Spawning obstacles...");

        SpawnN(treePrefabs, CellType.Tree, treeCount);
        SpawnN(rockPrefabs, CellType.Rock, rockCount);
        SpawnN(flowerPrefabs, CellType.Flower, flowerCount);

        isSpawning = false;
    }

    void SpawnN(GameObject[] prefabs, CellType type, int count)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning($"ObstacleSpawner: No prefabs assigned for {type}. Skipping.");
            return;
        }

        if (spawnManager == null)
        {
            Debug.LogError("ObstacleSpawner: NetworkSpawnManager not found in scene.");
            return;
        }
        if (spawnManager.catalogue == null)
        {
            Debug.LogError("ObstacleSpawner: NetworkSpawnManager has no Prefab Catalogue assigned.");
            return;
        }

        // Build a shuffled list of every free, in-bounds, buffered cell.
        // Picking from this list guarantees no two objects share a cell,
        // even across multiple SpawnN calls within the same SpawnAll.
        var freeCells = GetShuffledFreeCells();

        int placed = 0;
        foreach (Vector2Int cell in freeCells)
        {
            if (placed >= count) break;

            // Re-check: a previous type in this SpawnAll call may have taken this cell.
            if (GridManager.Instance.IsOccupied(cell)) continue;

            Vector3 pos = GridManager.Instance.GridToWorld(cell);

            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (spawnManager.catalogue.IndexOf(prefab) < 0)
            {
                Debug.LogError($"ObstacleSpawner: Prefab '{prefab.name}' not in Prefab Catalogue.");
                continue;
            }

            // IMPORTANT: In this Ubiq version, SpawnWithRoomScope returns void and does not return the instance.
            // SpawnWithPeerScope returns the spawned instance, so all peers will see it and we can position it.
            GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
            if (obj == null)
            {
                Debug.LogError($"ObstacleSpawner: Failed to spawn {prefab.name}. Is it in the Prefab Catalogue?");
                continue;
            }

            obj.transform.SetPositionAndRotation(pos, Quaternion.identity);
            FitToSingleGridCell(obj);

            var sync = obj.GetComponent<NetworkedSpawnedTransform>();
            if (sync != null)
            {
                // Spawn events fire before this method regains control, so request send after final transform is set.
                sync.RequestInitialSend();
            }

            if (GridManager.Instance.TryPlace(cell, type, obj))
            {
                placed++;
            }
            else
            {
                // Cell was taken between our check and TryPlace — shouldn't happen in single-player,
                // but handle defensively.
                Debug.LogWarning($"ObstacleSpawner: TryPlace failed for {type} at {cell} — despawning.");
                spawnManager.Despawn(obj);
            }
        }

        if (placed < count)
            Debug.LogWarning($"ObstacleSpawner: Only placed {placed}/{count} {type} — not enough free cells.");
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

        // Fisher-Yates shuffle
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        return cells;
    }
}
