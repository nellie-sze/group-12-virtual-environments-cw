using System;
using UnityEngine;
using Ubiq.Messaging;

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
    public ObstacleSpawner    obstacleSpawner;
    public LavaSpawner        lavaSpawner;
    public VillagerSpawner    villagerSpawner;
    public StartFinishSpawner startFinishSpawner;

    private NetworkContext _net;

    [Serializable]
    private struct GameMessage { public string type; public bool includesSpawn; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _net = NetworkScene.Register(this);

        // Auto-find systems if not assigned in Inspector
        if (livesManager       == null) livesManager       = FindFirstObjectByType<LivesManager>();
        if (obstacleSpawner    == null) obstacleSpawner    = FindFirstObjectByType<ObstacleSpawner>();
        if (lavaSpawner        == null) lavaSpawner        = FindFirstObjectByType<LavaSpawner>();
        if (villagerSpawner    == null) villagerSpawner    = FindFirstObjectByType<VillagerSpawner>();
        if (startFinishSpawner == null) startFinishSpawner = FindFirstObjectByType<StartFinishSpawner>();

        // Begin in Waiting state — instructions UI shows itself via its own Start()
        EnterState(GameState.Waiting);
    }

    // Ubiq message handler — called when another peer sends a GameMessage.
    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var msg = message.FromJson<GameMessage>();
        if (msg.type == "StartGame")
            DoStartGame(spawnObjects: msg.includesSpawn);
    }

    void EnterState(GameState next)
    {
        CurrentState = next;
        Debug.Log($"[GameManager] -> {next}");

        switch (next)
        {
            case GameState.Waiting:
                break;

            case GameState.Playing:
                if (countdownTimer != null) countdownTimer.StartTimer();
                break;

            case GameState.Won:
                if (countdownTimer != null) countdownTimer.StopTimer();
                if (endGameAnimator != null) endGameAnimator.PlayWinSequence();
                AudioManager.Instance?.PlayWinSound();
                Debug.Log("[GameManager] Players WIN — path complete!");
                break;

            case GameState.Lost:
                if (countdownTimer != null) countdownTimer.ShowEndGame();
                if (endGameAnimator != null) endGameAnimator.PlayLoseSequence();
                AudioManager.Instance?.PlayLoseSound();
                Debug.Log("[GameManager] Players LOSE — time ran out!");
                break;
        }
    }

    // All peers call this. Each spawner uses leader election so only one peer actually spawns.
    private void DoStartGame(bool spawnObjects)
    {
        if (CurrentState != GameState.Waiting) return;

        // Dismiss instructions panel on this peer if still visible
        if (instructionsUI != null && instructionsUI.gameObject.activeInHierarchy)
            instructionsUI.ForceHide();

        EnterState(GameState.Playing);

        // All peers initialise the lives display locally (hearts are non-networked objects)
        if (livesManager != null) livesManager.InitLives();

        if (spawnObjects)
        {
            // All peers call SpawnAll/Spawn. Leader election inside each spawner
            // ensures only the leader (lowest UUID) actually runs the spawn logic.
            if (startFinishSpawner != null) startFinishSpawner.SpawnAll();
            if (obstacleSpawner    != null) obstacleSpawner.SpawnAll();
            if (lavaSpawner        != null) lavaSpawner.SpawnAll();
            if (villagerSpawner    != null) villagerSpawner.Spawn();
        }
    }

    // Called by InstructionsUI when the local player presses "Start".
    // Broadcasts to all peers so everyone starts simultaneously.
    public void OnInstructionsDismissed()
    {
        if (CurrentState != GameState.Waiting) return;
        // Tell all other peers to start — they each run leader election to decide who actually spawns
        _net.SendJson(new GameMessage { type = "StartGame", includesSpawn = true });
        // The presser handles spawning locally — objects replicate to peers via Ubiq
        DoStartGame(spawnObjects: true);
    }

    // Called by PathChecker.OnPathComplete() when BFS confirms the full path.
    public void OnPathComplete()
    {
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Won);
    }

    // Called by CountdownTimer when time reaches zero.
    public void OnTimerEnd()
    {
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Lost);
    }

    public bool IsPlaying  => CurrentState == GameState.Playing;
    public bool IsGameOver => CurrentState == GameState.Lost || CurrentState == GameState.Won;

    // Called by VillagerAgent when a villager dies in lava.
    // Delegates to LivesManager which deducts a life and triggers game over
    // only when the last life is gone.
    public void OnVillagerDied()
    {
        if (CurrentState != GameState.Playing) return;

        if (livesManager != null)
            livesManager.LoseLife();
        else
            EnterState(GameState.Lost); // fallback if no LivesManager in scene
    }

    // Called by LivesManager when all lives have been consumed.
    public void OnAllLivesLost()
    {
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Lost);
    }

}
