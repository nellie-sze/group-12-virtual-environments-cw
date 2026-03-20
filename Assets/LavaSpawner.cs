using System.Collections.Generic;
using UnityEngine;
using Ubiq.Spawning;
using Ubiq.Rooms;

public class LavaSpawner : MonoBehaviour
{
    [Header("Lava Settings")]
    [Tooltip("Lava tile prefab — must be in the Ubiq Prefab Catalogue")]
    public GameObject lavaPrefab;

    [Tooltip("How many separate lava patches to spawn across the grid")]
    public int lavaCellCount = 1;

    [Tooltip("Small vertical lift so the tile sits visibly on top of the surface")]
    public float yOffset = 0.02f;

    NetworkSpawnManager spawnManager;

    // Possible patch sizes: (width, height) in grid cells
    private static readonly Vector2Int[] patchSizes = new Vector2Int[]
    {
        new Vector2Int(4, 2),
        new Vector2Int(4, 1),
        new Vector2Int(5, 2),
        new Vector2Int(5, 3),
    };

    void Start()
    {
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
        {
            spawnManager.OnSpawned.AddListener(OnNetworkSpawned);
        }
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

        // Only handle lava prefab instances
        var name = obj.name;
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix))
            name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();

        if (name != lavaPrefab.name) return;

        ScaleToCell(obj);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
        {
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
        }
    }

    public void SpawnAll()
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

    bool TrySpawnPatch(Vector2Int size)
    {
        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;

        // +1 / -1 inner buffer: tiles are centered on their cell, so an edge cell's
        // mesh extends half a cell outside the surface. Keep patches 1 cell away from
        // every edge to ensure nothing overhangs.
        var candidates = new List<Vector2Int>();
        for (int x = gridMin.x + 1; x <= gridMax.x - size.x; x++)
            for (int y = gridMin.y + 1; y <= gridMax.y - size.y; y++)
                candidates.Add(new Vector2Int(x, y));

        Shuffle(candidates);

        foreach (Vector2Int origin in candidates)
        {
            if (!PatchIsFree(origin, size)) continue;
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
                    sync.RequestInitialSend();
                }

                if (!GridManager.Instance.TryPlace(cell, CellType.Lava, obj))
                {
                    Debug.LogWarning($"LavaSpawner: TryPlace failed at {cell} — despawning.");
                    spawnManager.Despawn(obj);
                }
            }
        }
    }

    // Scale the tile so its XZ footprint matches exactly one grid cell,
    // regardless of the prefab mesh size (Plane=10x10, Quad=1x1, etc.)
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
