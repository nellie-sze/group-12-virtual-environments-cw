using UnityEngine;

public class VillagerSpawner : MonoBehaviour
{
    public BoardGrid grid;
    public VillagerAgent villagerPrefab;
    public int count = 3;

    void Start()
    {
        if (!grid) grid = FindFirstObjectByType<BoardGrid>();
        for (int i = 0; i < count; i++)
        {
            var v = Instantiate(villagerPrefab);
            v.grid = grid;
            v.cell = new Vector2Int(Random.Range(0, grid.width), Random.Range(0, grid.height));
            v.SnapToCell(v.cell);
        }
    }
}