using UnityEngine;

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

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Begin in Waiting state — instructions UI shows itself
        EnterState(GameState.Waiting);
    }

    void EnterState(GameState next)
    {
        CurrentState = next;
        Debug.Log($"[GameManager] → {next}");

        switch (next)
        {
            case GameState.Waiting:
                // InstructionsUI.Start() handles showing the panel;
                break;

            case GameState.Playing:
                if (countdownTimer != null) countdownTimer.StartTimer();
                break;

            case GameState.Won:
                if (countdownTimer != null) countdownTimer.StopTimer();
                if (endGameAnimator != null) endGameAnimator.PlayWinSequence();
                Debug.Log("[GameManager] 🎉 Players WIN — path complete!");
                break;

            case GameState.Lost:
                if (countdownTimer != null) countdownTimer.StopTimer();
                if (endGameAnimator != null) endGameAnimator.PlayLoseSequence();
                Debug.Log("[GameManager] 💀 Players LOSE — time ran out!");
                break;
        }
    }

    // Called by InstructionsUI when the player presses "Play".
    public void OnInstructionsDismissed()
    {
        if (CurrentState != GameState.Waiting) return;
        EnterState(GameState.Playing);
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

    // is the game currently accepting input?
    public bool IsPlaying => CurrentState == GameState.Playing;

    // True once the game has reached a terminal state (won or lost).
    public bool IsGameOver => CurrentState == GameState.Lost || CurrentState == GameState.Won;

    // Called by VillagerAgent when a villager dies in lava.
    public void OnVillagerDied()
    {
        if (CurrentState != GameState.Playing) return;
        EnterState(GameState.Lost);
    }
}