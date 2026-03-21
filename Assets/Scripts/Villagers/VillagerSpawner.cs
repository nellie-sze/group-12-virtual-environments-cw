using UnityEngine;
using System;
using Ubiq.Messaging;
using Ubiq.Rooms;
using Ubiq.Spawning;

public class VillagerSpawner : MonoBehaviour
{
    public BoardGrid grid;
    public VillagerAgent villagerPrefab;
    public int count = 3;

    private NetworkSpawnManager spawnManager;
    private RoomClient roomClient;
    private NetworkContext context;

    private bool hasSpawned;
    private string lastRequestId;

    private struct SpawnRequestMessage
    {
        public string requestId;
    }

    private void Start()
    {
        context = NetworkScene.Register(this);

        roomClient = FindFirstObjectByType<RoomClient>();
        spawnManager = NetworkSpawnManager.Find(this);

        // Avoid duplicate spawns if a second NetworkSpawnManager exists on this GameObject.
        var localManager = GetComponent<NetworkSpawnManager>();
        if (spawnManager != null && localManager != null && localManager != spawnManager)
        {
            localManager.enabled = false;
            Debug.LogWarning($"VillagerSpawner: Disabled extra NetworkSpawnManager on {gameObject.name} to prevent duplicate spawns. Using {spawnManager.gameObject.name}.");
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

    private void OnDestroy()
    {
        if (spawnManager != null)
        {
            spawnManager.OnSpawned.RemoveListener(OnNetworkSpawned);
        }
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<SpawnRequestMessage>();
        HandleSpawnRequest(m.requestId);
    }

    public void Spawn()
    {
        // Any peer can click the button; the elected "leader" peer will perform the network spawn.
        var requestId = Guid.NewGuid().ToString("N");
        HandleSpawnRequest(requestId);
        context.SendJson(new SpawnRequestMessage { requestId = requestId });
    }

    private void HandleSpawnRequest(string requestId)
    {
        if (hasSpawned)
        {
            return;
        }

        if (!string.IsNullOrEmpty(lastRequestId) && lastRequestId == requestId)
        {
            return;
        }

        lastRequestId = requestId;

        if (!IsLeaderPeer())
        {
            return;
        }

        if (!grid) grid = FindFirstObjectByType<BoardGrid>();
        if (grid == null)
        {
            Debug.LogError("VillagerSpawner: BoardGrid not found in scene.");
            return;
        }

        if (spawnManager == null)
        {
            Debug.LogError("VillagerSpawner: NetworkSpawnManager not found in scene.");
            return;
        }
        if (spawnManager.catalogue == null)
        {
            Debug.LogError("VillagerSpawner: NetworkSpawnManager has no Prefab Catalogue assigned.");
            return;
        }
        if (villagerPrefab == null)
        {
            Debug.LogError("VillagerSpawner: Villager prefab not set.");
            return;
        }

        var prefabToSpawn = ResolveCataloguePrefab();
        if (prefabToSpawn == null)
        {
            Debug.LogError($"VillagerSpawner: Prefab not in Prefab Catalogue: {villagerPrefab.name}. Add the project prefab asset to the catalogue used by the active NetworkSpawnManager, or assign that catalogue prefab directly on this spawner.");
            return;
        }

        hasSpawned = true;

        for (int i = 0; i < count; i++)
        {
            var obj = spawnManager.SpawnWithPeerScope(prefabToSpawn);
            if (obj == null)
            {
                Debug.LogError($"VillagerSpawner: Failed to spawn {prefabToSpawn.name}. Is it added to the Prefab Catalogue?");
                continue;
            }

            var agent = obj.GetComponent<VillagerAgent>();
            if (agent != null)
            {
                agent.grid = grid;
                agent.isAuthority = true; // local leader controls AI + publishes motion
                agent.cell = new Vector2Int(UnityEngine.Random.Range(0, grid.width), UnityEngine.Random.Range(0, grid.height));
                agent.SnapToCell(agent.cell);
            }

            // Optional: if the prefab has NetworkedSpawnedTransform, publish an initial transform so
            // remotes don't see it at the prefab origin for a frame.
            var sync = obj.GetComponent<NetworkedSpawnedTransform>();
            if (sync != null)
            {
                sync.SetOwner(true);
                sync.RequestInitialSend();
            }
        }
    }

    private GameObject ResolveCataloguePrefab()
    {
        var prefabObject = villagerPrefab.gameObject;
        if (spawnManager.catalogue.IndexOf(prefabObject) >= 0)
        {
            return prefabObject;
        }

        if (spawnManager.catalogue.prefabs == null)
        {
            return null;
        }

        for (int i = 0; i < spawnManager.catalogue.prefabs.Count; i++)
        {
            var candidate = spawnManager.catalogue.prefabs[i];
            if (candidate == null || candidate.name != prefabObject.name)
            {
                continue;
            }

            if (candidate.GetComponent<VillagerAgent>() == null)
            {
                continue;
            }

            Debug.LogWarning($"VillagerSpawner: '{villagerPrefab.name}' is referencing a scene instance or non-catalogue object. Using catalogue prefab asset '{candidate.name}' instead.");
            return candidate;
        }

        return null;
    }

    private void OnNetworkSpawned(GameObject obj, IRoom room, IPeer peer, NetworkSpawnOrigin origin)
    {
        var agent = obj != null ? obj.GetComponent<VillagerAgent>() : null;
        if (agent == null)
        {
            return;
        }

        if (!grid) grid = FindFirstObjectByType<BoardGrid>();
        if (grid != null)
        {
            agent.grid = grid;
        }

        // Only the peer that initiated the spawn should drive the AI/motion.
        agent.isAuthority = (origin == NetworkSpawnOrigin.Local);

        var sync = obj.GetComponent<NetworkedSpawnedTransform>();
        if (sync != null)
        {
            sync.SetOwner(origin == NetworkSpawnOrigin.Local);
        }
    }

    private bool IsLeaderPeer()
    {
        if (roomClient == null || roomClient.Me == null)
        {
            // If there is no RoomClient, fall back to local-only behaviour.
            return true;
        }

        // Deterministic leader election: lowest uuid among all peers (including self).
        var leaderUuid = roomClient.Me.uuid;
        foreach (var p in roomClient.Peers)
        {
            if (string.CompareOrdinal(p.uuid, leaderUuid) < 0)
            {
                leaderUuid = p.uuid;
            }
        }

        return roomClient.Me.uuid == leaderUuid;
    }
}
