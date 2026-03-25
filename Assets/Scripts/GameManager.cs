using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Waiting, Playing, Won, Lost }
    public GameState CurrentState { get; private set; } = GameState.Waiting;

    [Header("Core Systems")]
    [Tooltip("The countdown timer component in the scene.")]
    public CountdownTimer countdownTimer;

    [Tooltip("The end-game animator that drives water flow, glow, and fireworks.")]
    public EndGameAnimator endGameAnimator;

    [Tooltip("The instructions UI controller shown at game start.")]
    public InstructionsUI instructionsUI;

    [Header("Lives")]
    [Tooltip("Manages the shared heart lives display. Auto-found if left empty.")]
    public LivesManager livesManager;

    [Header("Spawners")]
    [Tooltip("Auto-found if left empty.")]
    public ObstacleSpawner obstacleSpawner;
    public LavaSpawner lavaSpawner;
    public VillagerSpawner villagerSpawner;
    public StartFinishSpawner startFinishSpawner;

    [Header("Block Decay")]
    [Tooltip("Seconds between each random path-block removal during gameplay.")]
    public float blockDecayInterval = 20f;

    [Header("Interactables")]
    [Tooltip("Tools and powerups to keep deactivate until the game starts.")]
    public bool activateOnStartOnly = true;
    public XRGrabInteractable[] gatedInteractables;

    private NetworkContext _net;
    private RoomClient _roomClient;
    private Coroutine _decayCoroutine;
    private bool _restartInProgress;

    [Serializable]
    private struct GameMessage
    {
        public string type;
        public bool includesSpawn;
        public string nextRoomGuid;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _net        = NetworkScene.Register(this);
        _roomClient = FindFirstObjectByType<RoomClient>();

        // Auto-find systems if not assigned in Inspector
        if (livesManager == null) livesManager = FindFirstObjectByType<LivesManager>();
        if (obstacleSpawner == null) obstacleSpawner = FindFirstObjectByType<ObstacleSpawner>();
        if (lavaSpawner == null) lavaSpawner = FindFirstObjectByType<LavaSpawner>();
        if (villagerSpawner == null) villagerSpawner = FindFirstObjectByType<VillagerSpawner>();
        if (startFinishSpawner == null) startFinishSpawner = FindFirstObjectByType<StartFinishSpawner>();

        if (activateOnStartOnly) SetInteractablesEnabled(false);

        Debug.Log($"[GameManager] Start. countdownTimer={(countdownTimer != null ? countdownTimer.name : "null")}, instructionsUI={(instructionsUI != null ? instructionsUI.name : "null")}, livesManager={(livesManager != null ? livesManager.name : "null")}, obstacleSpawner={(obstacleSpawner != null ? obstacleSpawner.name : "null")}, lavaSpawner={(lavaSpawner != null ? lavaSpawner.name : "null")}, villagerSpawner={(villagerSpawner != null ? villagerSpawner.name : "null")}, startFinishSpawner={(startFinishSpawner != null ? startFinishSpawner.name : "null")}");

        EnterState(GameState.Waiting);
    }

    // Ubiq message handler — called when another peer sends a GameMessage.
    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var msg = message.FromJson<GameMessage>();
        Debug.Log($"[GameManager] ProcessMessage type={msg.type}, includesSpawn={msg.includesSpawn}, nextRoomGuid={msg.nextRoomGuid}, currentState={CurrentState}");
        if (msg.type == "StartGame")
        {
            DoStartGame(spawnObjects: msg.includesSpawn);
        }
        else if (msg.type == "RestartGame")
        {
            BeginNetworkRestart(msg.nextRoomGuid);
        }
    }

    void EnterState(GameState next)
    {
        Debug.Log($"[GameManager] EnterState request {CurrentState} -> {next}. timeScale={Time.timeScale:0.00}");
        CurrentState = next;
        Debug.Log($"[GameManager] State is now {next}");

        switch (next)
        {
            case GameState.Waiting:
                SetInteractablesEnabled(false);
                break;

            case GameState.Playing:
                SetInteractablesEnabled(true);
                ActivateGrabGates();
                Debug.Log($"[GameManager] Entering Playing. countdownTimer={(countdownTimer != null ? countdownTimer.name : "null")}, livesManager={(livesManager != null ? livesManager.name : "null")}");
                if (countdownTimer != null) countdownTimer.StartTimer();
                _decayCoroutine = StartCoroutine(BlockDecayLoop());
                break;

            case GameState.Won:
                Debug.Log("[GameManager] Entering Won. Stopping timer and playing win sequence.");
                SetInteractablesEnabled(false);
                DeactivateGrabGates();
                if (_decayCoroutine != null) { StopCoroutine(_decayCoroutine); _decayCoroutine = null; }
                if (countdownTimer != null) countdownTimer.StopTimer();
                if (endGameAnimator != null) endGameAnimator.PlayWinSequence();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayWinSound();
                if (lavaSpawner != null) lavaSpawner.RemoveAllLava();
                Debug.Log("[GameManager] Players WIN - path complete!");
                break;

            case GameState.Lost:
                Debug.Log("[GameManager] Entering Lost. Stopping timer / showing end game / playing lose sequence.");
                SetInteractablesEnabled(false);
                DeactivateGrabGates();
                if (_decayCoroutine != null) { StopCoroutine(_decayCoroutine); _decayCoroutine = null; }
                if (countdownTimer != null) countdownTimer.ShowEndGame();
                if (endGameAnimator != null) endGameAnimator.PlayLoseSequence();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLoseSound();
                if (villagerSpawner != null) villagerSpawner.RemoveAllVillagers();
                Debug.Log("[GameManager] Players LOSE - time ran out or lives depleted!");
                break;
        }
    }

    // All peers call this. Each spawner uses leader election so only one peer actually spawns.
    private void DoStartGame(bool spawnObjects)
    {
        Debug.Log($"[GameManager] DoStartGame called. currentState={CurrentState}, spawnObjects={spawnObjects}, instructionsUIActive={(instructionsUI != null ? instructionsUI.gameObject.activeInHierarchy.ToString() : "null")}");
        if (CurrentState != GameState.Waiting)
        {
            Debug.Log("[GameManager] DoStartGame ignored because state is not Waiting.");
            return;
        }

        // Dismiss instructions panel on this peer if still visible
        if (instructionsUI != null && instructionsUI.gameObject.activeInHierarchy)
        {
            Debug.Log("[GameManager] DoStartGame calling instructionsUI.ForceHide().");
            instructionsUI.ForceHide();
        }
        else
        {
            Debug.Log("[GameManager] DoStartGame skipped ForceHide because instructions UI is null or already inactive.");
        }

        EnterState(GameState.Playing);

        // All peers initialise the lives display locally (hearts are non-networked objects)
        Debug.Log($"[GameManager] Initialising lives. livesManagerAssigned={(livesManager != null)}");
        if (livesManager != null) livesManager.InitLives();

        if (spawnObjects)
        {
            Debug.Log("[GameManager] DoStartGame spawning scene objects.");
            // All peers call SpawnAll/Spawn. Leader election inside each spawner
            // ensures only the leader (lowest UUID) actually runs the spawn logic.
            if (startFinishSpawner != null) startFinishSpawner.SpawnAll();
            if (obstacleSpawner != null) obstacleSpawner.SpawnAll();
            if (lavaSpawner != null) lavaSpawner.SpawnAll();
            if (villagerSpawner != null) villagerSpawner.Spawn();
        }
        else
        {
            Debug.Log("[GameManager] DoStartGame skipping object spawn on this peer.");
        }
    }

    // Called by InstructionsUI when the local player presses "Start".
    // Broadcasts to all peers so everyone starts simultaneously.
    public void OnInstructionsDismissed()
    {
        Debug.Log($"[GameManager] OnInstructionsDismissed called. currentState={CurrentState}");
        if (CurrentState != GameState.Waiting)
        {
            Debug.Log("[GameManager] OnInstructionsDismissed ignored because state is not Waiting.");
            return;
        }
        // Tell all other peers to start - they each run leader election to decide who actually spawns
        Debug.Log("[GameManager] Broadcasting StartGame message to peers.");
        _net.SendJson(new GameMessage { type = "StartGame", includesSpawn = true });
        // The presser handles spawning locally - objects replicate to peers via Ubiq
        DoStartGame(spawnObjects: true);
    }

    // Called by PathChecker.OnPathComplete() when BFS confirms the full path.
    public void OnPathComplete()
    {
        Debug.Log($"[GameManager] OnPathComplete called. currentState={CurrentState}");
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Won);
    }

    // Called by CountdownTimer when time reaches zero.
    public void OnTimerEnd()
    {
        Debug.Log($"[GameManager] OnTimerEnd called. currentState={CurrentState}");
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Lost);
    }

    public bool IsPlaying => CurrentState == GameState.Playing;
    public bool IsGameOver => CurrentState == GameState.Lost || CurrentState == GameState.Won;

    public void ReloadCurrentScene()
    {
        var scene = SceneManager.GetActiveScene();
        Debug.Log($"[GameManager] ReloadCurrentScene called. Reloading scene '{scene.name}' ({scene.buildIndex}).");
        SceneManager.LoadScene(scene.buildIndex);
    }

    public void RestartToAlternateRoom()
    {
        if (_restartInProgress)
        {
            Debug.Log("[GameManager] RestartToAlternateRoom ignored because restart is already in progress.");
            return;
        }

        var autoJoin = FindFirstObjectByType<AutoJoinRoom>();
        if (autoJoin == null)
        {
            Debug.LogError("[GameManager] RestartToAlternateRoom failed: no AutoJoinRoom found in scene.");
            ReloadCurrentScene();
            return;
        }

        string currentRoomGuid = _roomClient != null && _roomClient.Room != null ? _roomClient.Room.UUID : autoJoin.primaryRoomGuid;
        string nextRoomGuid = autoJoin.GetAlternateRoomGuid(currentRoomGuid);

        Debug.Log($"[GameManager] RestartToAlternateRoom current='{currentRoomGuid}' next='{nextRoomGuid}'.");
        _net.SendJson(new GameMessage { type = "RestartGame", nextRoomGuid = nextRoomGuid });
        BeginNetworkRestart(nextRoomGuid);
    }

    private void BeginNetworkRestart(string nextRoomGuid)
    {
        if (_restartInProgress)
        {
            Debug.Log($"[GameManager] BeginNetworkRestart ignored because restart is already in progress. nextRoomGuid='{nextRoomGuid}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(nextRoomGuid))
        {
            Debug.LogError("[GameManager] BeginNetworkRestart failed: nextRoomGuid was empty.");
            return;
        }

        _restartInProgress = true;
        Debug.Log($"[GameManager] BeginNetworkRestart scheduling restart into room '{nextRoomGuid}'.");
        AutoJoinRoom.SetNextRoomGuid(nextRoomGuid);
        StartCoroutine(CleanupAndReloadScene());
    }

    private IEnumerator CleanupAndReloadScene()
    {
        Debug.Log("[GameManager] CleanupAndReloadScene: removing spawned round objects before reload.");

        if (PathBlockManager.Instance != null)
            PathBlockManager.Instance.RemoveAllPathBlocks();

        if (obstacleSpawner != null)
            obstacleSpawner.RemoveAllObstacles();

        if (lavaSpawner != null)
            lavaSpawner.RemoveAllLava();

        if (villagerSpawner != null)
            villagerSpawner.RemoveAllVillagers();

        if (startFinishSpawner != null)
            startFinishSpawner.RemoveAllMarkers();

        if (PathChecker.Instance != null)
            PathChecker.Instance.ClearAllNodes();

        if (GridManager.Instance != null)
            GridManager.Instance.ClearAllCells();

        // Give NSM/property-based despawns a short moment to flush before the room is left.
        yield return null;
        yield return new WaitForSeconds(0.2f);

        ReloadCurrentScene();
    }

    // ── Block decay ───────────────────────────────────────────────────────────

    private IEnumerator BlockDecayLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(blockDecayInterval);

            // Only the leader peer picks and broadcasts the removal.
            if (!IsLeaderPeer()) continue;
            if (GridManager.Instance == null || PathBlockManager.Instance == null) continue;

            var pathCells = new List<Vector2Int>();
            foreach (var kvp in GridManager.Instance.GetAllCells())
                if (kvp.Value.type == CellType.Path)
                    pathCells.Add(kvp.Key);

            if (pathCells.Count == 0) continue;

            var cell = pathCells[UnityEngine.Random.Range(0, pathCells.Count)];
            Debug.Log($"[GameManager] Block decay - removing path block at {cell}");
            PathBlockManager.Instance.RequestRemove(cell);
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayPathDeleteSound(GridManager.Instance.GridToWorld(cell));
        }
    }

    private bool IsLeaderPeer()
    {
        if (_roomClient == null || _roomClient.Me == null) return true;
        var leaderId = _roomClient.Me.uuid;
        foreach (var p in _roomClient.Peers)
            if (string.CompareOrdinal(p.uuid, leaderId) < 0)
                leaderId = p.uuid;
        return _roomClient.Me.uuid == leaderId;
    }

    private void ActivateGrabGates()
    {
        var gates = FindObjectsByType<GrabGate>(FindObjectsSortMode.None);
        Debug.Log($"[GameManager] Activating {gates.Length} GrabGate component(s).");
        foreach (var gate in gates)
            gate.OnGameStart();
    }

    private void DeactivateGrabGates()
    {
        var gates = FindObjectsByType<GrabGate>(FindObjectsSortMode.None);
        Debug.Log($"[GameManager] Deactivating {gates.Length} GrabGate component(s).");
        foreach (var gate in gates)
            gate.OnGameEnd();
    }

    // Called by VillagerAgent when a villager dies in lava.
    // Delegates to LivesManager which deducts a life and triggers game over
    // only when the last life is gone.
    public void OnVillagerDied()
    {
        Debug.Log($"[GameManager] OnVillagerDied called. currentState={CurrentState}, livesManagerAssigned={(livesManager != null)}");
        if (CurrentState != GameState.Playing) return;

        if (livesManager != null)
            livesManager.LoseLife();
        else
            EnterState(GameState.Lost); // fallback if no LivesManager in scene
    }

    // Called by LivesManager when all lives have been consumed.
    public void OnAllLivesLost()
    {
        Debug.Log($"[GameManager] OnAllLivesLost called. currentState={CurrentState}");
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Lost);
    }

    private void SetInteractablesEnabled(bool enabled)
    {
        if (gatedInteractables == null) return;

        foreach (var interactable in gatedInteractables)
        {
            if (interactable == null) continue;
            interactable.enabled = enabled;
        }
    }
}
