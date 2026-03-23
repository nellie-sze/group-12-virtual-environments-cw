using UnityEngine;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public static CountdownTimer Instance { get; private set; }

    [Header("Settings")]
    public float totalTime = 120f; // 2 minutes

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
        Debug.Log($"[CountdownTimer] Start. totalTime={totalTime:0.00}, timerText={(timerText != null ? timerText.name : "null")}");

        // Keep the display blank until the game starts — StartTimer() reveals it.
        if (timerText != null) timerText.text = "";
    }

    void Update()
    {
        if (!isRunning) return;

        timeRemaining -= Time.deltaTime;

        AudioManager.Instance?.UpdateTimerWarning(timeRemaining);

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
        ShowEndGame();
        Debug.Log("[CountdownTimer] Time's up!");

        // Delegate to GameManager — it decides whether to trigger lose state
        if (GameManager.Instance != null)
            GameManager.Instance.OnTimerEnd();
        else
            Debug.LogWarning("[CountdownTimer] GameManager.Instance is null - add a GameManager to the scene.");
    }

    public void StartTimer()
    {
        isRunning = true;
        UpdateDisplay();
        Debug.Log($"[CountdownTimer] StartTimer called. timeRemaining={timeRemaining:0.00}, timeScale={Time.timeScale:0.00}, enabled={enabled}, gameObjectActive={gameObject.activeInHierarchy}");
    }

    public void StopTimer()
    {
        isRunning = false;
        Debug.Log($"[CountdownTimer] StopTimer called. timeRemaining={timeRemaining:0.00}, timeScale={Time.timeScale:0.00}");
    }

    public void ResetTimer()
    {
        timeRemaining = totalTime;
        isRunning = false;
        UpdateDisplay();
        Debug.Log($"[CountdownTimer] ResetTimer called. timeRemaining reset to {timeRemaining:0.00}");
    }

    public void ShowEndGame()
    {
        isRunning = false;
        if (timerText != null) timerText.text = "END GAME";
        Debug.Log("[CountdownTimer] ShowEndGame called.");
    }

    // Remaining time as a 0–1 fraction.
    public float FractionRemaining => totalTime > 0f ? timeRemaining / totalTime : 0f;
}