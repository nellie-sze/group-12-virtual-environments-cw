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

        SpawnN(treePrefabs,   CellType.Tree,   treeCount);
        SpawnN(rockPrefabs,   CellType.Rock,   rockCount);
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

        int placed = 0;
        int attempts = 0;

        while (placed < count && attempts < 200)
        {
            attempts++;
            Vector2Int cell = GridManager.Instance.GetRandomCell();

            if (!GridManager.Instance.IsOccupied(cell))
            {
                Vector3 pos = GridManager.Instance.GridToWorld(cell);
                if (!GridManager.Instance.IsWithinGridSurfaceBuffered(pos, edgeBufferRadius))
                {
                    continue;
                }

                // Recompute the cell from the final world position to ensure consistent occupancy checks.
                Vector2Int snappedCell = GridManager.Instance.WorldToGrid(pos);
                if (GridManager.Instance.IsOccupied(snappedCell))
                {
                    Debug.Log($"ObstacleSpawner: Skip {type} (cell occupied) cell={snappedCell} worldPos={pos}");
                    continue;
                }

                GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
                if (spawnManager.catalogue.IndexOf(prefab) < 0)
                {
                    Debug.LogError($"ObstacleSpawner: Prefab not in Prefab Catalogue: {prefab.name}. Add it to the catalogue used by the active NetworkSpawnManager.");
                    continue;
                }

                // IMPORTANT: In this Ubiq version, SpawnWithRoomScope returns void and does not return the instance.
                // SpawnWithPeerScope returns the spawned instance, so all peers will see it and we can position it.
                GameObject obj = spawnManager.SpawnWithPeerScope(prefab);
                if (obj == null)
                {
                    Debug.LogError($"ObstacleSpawner: Failed to spawn {prefab.name}. Is it added to the Prefab Catalogue?");
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
                if (GridManager.Instance.TryPlace(snappedCell, type, obj))
                {
                    Debug.Log($"ObstacleSpawner: Spawned {type} prefab={prefab.name} cell={snappedCell} worldPos={pos}");
                    placed++;
                }
                else
                {
                    // If we failed to reserve the cell, remove the spawned object to prevent overlaps.
                    Debug.LogWarning($"ObstacleSpawner: Despawn {type} prefab={prefab.name} (TryPlace failed) cell={snappedCell} worldPos={pos}");
                    spawnManager.Despawn(obj);
                }
            }
        }

        if (placed < count)
            Debug.LogWarning($"ObstacleSpawner: Only placed {placed}/{count} {type} obstacles after 200 attempts.");
    }
}
