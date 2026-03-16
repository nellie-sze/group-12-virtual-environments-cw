using UnityEngine;

public class ObstacleSpawner : MonoBehaviour
{
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
        SpawnN(treePrefabs,   CellType.Tree,   treeCount);
        SpawnN(rockPrefabs,   CellType.Rock,   rockCount);
        SpawnN(flowerPrefabs, CellType.Flower, flowerCount);
    }

    void SpawnN(GameObject[] prefabs, CellType type, int count)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning($"ObstacleSpawner: No prefabs assigned for {type}. Skipping.");
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
                GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
                GameObject obj = Instantiate(prefab, pos, Quaternion.identity);
                GridManager.Instance.TryPlace(cell, type, obj);
                placed++;
            }
        }

        if (placed < count)
            Debug.LogWarning($"ObstacleSpawner: Only placed {placed}/{count} {type} obstacles after 200 attempts.");
    }
}
