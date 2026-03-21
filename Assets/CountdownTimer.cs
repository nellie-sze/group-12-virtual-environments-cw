using UnityEngine;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public static CountdownTimer Instance { get; private set; }

    [Header("Settings")]
    public float totalTime = 300f; // 5 minutes

    [Header("UI")]
    public TMP_Text timerText;

    private float timeRemaining;
    private bool  isRunning = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        timeRemaining = totalTime;
        UpdateDisplay();

        // Do NOT call StartTimer() here.
        // GameManager.OnInstructionsDismissed() -> EnterState(Playing) -> StartTimer()
        // This ensures the timer only starts once the instructions are dismissed.
    }

    void Update()
    {
        if (!isRunning) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            isRunning = false;
            UpdateDisplay();
            OnTimerEnd();
            return;
        }

        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(timeRemaining / 60f);
        int seconds = Mathf.FloorToInt(timeRemaining % 60f);
        timerText.text = string.Format("{0}:{1:00}", minutes, seconds);
    }

    void OnTimerEnd()
    {
        if (timerText != null) timerText.text = "0:00";
        Debug.Log("[CountdownTimer] Time's up!");

        // Delegate to GameManager — it decides whether to trigger lose state
        if (GameManager.Instance != null)
            GameManager.Instance.OnTimerEnd();
        else
            Debug.LogWarning("[CountdownTimer] GameManager.Instance is null — add a GameManager to the scene.");
    }

    public void StartTimer() => isRunning = true;
    public void StopTimer()  => isRunning = false;
    public void ResetTimer() { timeRemaining = totalTime; isRunning = false; UpdateDisplay(); }

    /// Remaining time as a 0–1 fraction.
    public float FractionRemaining => totalTime > 0f ? timeRemaining / totalTime : 0f;
}