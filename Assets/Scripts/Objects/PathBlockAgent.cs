using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to straight and corner path block prefabs.
/// On remote peers, waits for NetworkedSpawnedTransform to sync the position
/// then registers the block in GridManager and PathChecker.
/// </summary>
public class PathBlockAgent : MonoBehaviour
{
    [Tooltip("True for straight pieces, false for corner pieces. Set in the prefab Inspector.")]
    public bool isStraight = true;

    /// Set to true by PathBlockManager on the peer that placed this block.
    [HideInInspector] public bool isLocallySpawned = false;

    IEnumerator Start()
    {
        var sync = GetComponent<NetworkedSpawnedTransform>();
        if (sync != null && !sync.IsOwner)
        {
            // Wait for NetworkedSpawnedTransform to receive the real position.
            yield return new WaitForSeconds(0.5f);
            if (this == null) yield break;

            if (GridManager.Instance == null) yield break;

            // Collect all grid cells this object occupies (mirrors GridSystem logic).
            float gs = GridManager.Instance.gridSize;
            var occupiedCells = new List<Vector2Int>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                Vector3 snapped = new Vector3(
                    Mathf.Round(r.transform.position.x / gs) * gs,
                    Mathf.Round(r.transform.position.y / gs) * gs,
                    Mathf.Round(r.transform.position.z / gs) * gs);
                Vector2Int cell = GridManager.Instance.WorldToGrid(snapped);
                if (!occupiedCells.Contains(cell))
                    occupiedCells.Add(cell);
            }
            if (occupiedCells.Count == 0)
                occupiedCells.Add(GridManager.Instance.WorldToGrid(transform.position));

            foreach (var cell in occupiedCells)
                GridManager.Instance.TryPlace(cell, CellType.Path, gameObject);

            // Register primary cell in PathChecker.
            if (PathChecker.Instance != null && occupiedCells.Count > 0)
            {
                int rotY = Mathf.RoundToInt(transform.eulerAngles.y / 90f) * 90;
                PathNode node = isStraight ? PathNode.Straight(rotY) : PathNode.Corner(rotY);
                PathChecker.Instance.RegisterNode(occupiedCells[0], node, gameObject);
                PathChecker.Instance.CheckPath();
            }
        }
    }
}
