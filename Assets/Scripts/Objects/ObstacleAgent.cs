using System.Collections;
using UnityEngine;
using Ubiq.Spawning;

public class ObstacleAgent : MonoBehaviour
{
    [Tooltip("Set this to Tree, Rock, or Flower in the prefab inspector!")]
    public CellType obstacleType;

    private bool isRegisteredLocally = false;

    IEnumerator Start()
    {
        // Check if we are a remote peer receiving this object
        var networkOrigin = GetComponent<NetworkedSpawnedTransform>();
        if (networkOrigin != null && !networkOrigin.IsOwner)
        {
            // Remote peers receive the object at (0,0,0) initially. 
            // We wait half a second for Ubiq to sync the real position over the network.
            yield return new WaitForSeconds(0.5f);

            if (this == null) yield break; // destroyed during the wait (e.g. synced removal)

            if (GridManager.Instance != null && !isRegisteredLocally)
            {
                // Calculate our cell based on the newly synced network position
                Vector2Int cell = GridManager.Instance.WorldToGrid(transform.position);
                GridManager.Instance.TryPlace(cell, obstacleType, gameObject);
                isRegisteredLocally = true;
            }
        }
    }

    public void MarkAsRegisteredByLeader()
    {
        // The leader already placed this immediately upon spawning, so skip the delay.
        isRegisteredLocally = true;
    }
}