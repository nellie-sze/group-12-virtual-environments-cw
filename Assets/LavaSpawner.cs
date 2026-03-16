using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LavaSpawner : MonoBehaviour
{
    [Header("Lava Settings")]
    [Tooltip("Lava tile prefab — should be a flat 1x1 quad/plane, same scale as tree/rock prefabs")]
    public GameObject lavaPrefab;

    [Tooltip("How many connected lava cells to spawn as one pool")]
    public int lavaCellCount = 6;

    [Tooltip("Small vertical lift so the tile sits visibly on top of the surface")]
    public float yOffset = 0.02f;

    void Start()
    {
        StartCoroutine(SpawnAfterOtherSpawners());
    }

    // Wait one frame so ObstacleSpawner and StartFinishSpawner have finished
    // their Start() calls and registered all trees, rocks, and markers first
    IEnumerator SpawnAfterOtherSpawners()
    {
        yield return null;

        if (lavaPrefab == null)
        {
            Debug.LogWarning("LavaSpawner: lavaPrefab not assigned.");
            yield break;
        }

        List<Vector2Int> cluster = GrowCluster(lavaCellCount);

        if (cluster.Count == 0)
        {
            Debug.LogWarning("LavaSpawner: Could not place lava — no valid starting cell found.");
            yield break;
        }

        float size = GridManager.Instance.gridSize;
        int placed = 0;

        foreach (Vector2Int cell in cluster)
        {
            if (GridManager.Instance.IsOccupied(cell)) continue;

            Vector3 pos = GridManager.Instance.GridToWorld(cell);
            pos.y += yOffset;
            GameObject obj = Instantiate(lavaPrefab, pos, Quaternion.identity);

            Vector3 s = obj.transform.localScale;
            s.x = size;
            s.z = size;
            obj.transform.localScale = s;

            GridManager.Instance.TryPlace(cell, CellType.Lava, obj);
            placed++;
        }

        Debug.Log($"LavaSpawner: Placed {placed} lava cells (skipped {cluster.Count - placed} occupied).");
    }

    // Grows one connected blob from a random origin, same as flood-fill
    List<Vector2Int> GrowCluster(int targetSize)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        for (int attempt = 0; attempt < 100; attempt++)
        {
            Vector2Int origin = GridManager.Instance.GetRandomCell();
            if (GridManager.Instance.IsOccupied(origin)) continue;

            var cluster  = new List<Vector2Int> { origin };
            var frontier = new List<Vector2Int> { origin };

            while (cluster.Count < targetSize && frontier.Count > 0)
            {
                int fi = Random.Range(0, frontier.Count);
                Vector2Int current = frontier[fi];

                var freeNeighbours = new List<Vector2Int>();
                foreach (var dir in dirs)
                {
                    Vector2Int n = current + dir;
                    if (GridManager.Instance.IsInBounds(n) &&
                        !GridManager.Instance.IsOccupied(n) &&
                        !cluster.Contains(n))
                        freeNeighbours.Add(n);
                }

                if (freeNeighbours.Count > 0)
                {
                    Vector2Int next = freeNeighbours[Random.Range(0, freeNeighbours.Count)];
                    cluster.Add(next);
                    frontier.Add(next);
                }
                else
                {
                    frontier.RemoveAt(fi);
                }
            }

            if (cluster.Count == targetSize)
                return cluster;
        }

        return new List<Vector2Int>();
    }
}
