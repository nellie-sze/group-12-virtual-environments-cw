using UnityEngine;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    [Header("Settings")]
    public float totalTime = 120f; // 2 minutes in seconds

    [Header("UI")]
    public TMP_Text timerText;

    private float timeRemaining;
    private bool isRunning = false;

    void Start()
    {
        timeRemaining = totalTime;
        UpdateDisplay();
        StartTimer();
    }

    void Update()
    {
        if (!isRunning) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            isRunning = false;
            OnTimerEnd();
        }

        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        int minutes = Mathf.FloorToInt(timeRemaining / 60f);
        int seconds = Mathf.FloorToInt(timeRemaining % 60f);
        timerText.text = string.Format("{0}:{1:00}", minutes, seconds);
    }

    void OnTimerEnd()
    {
        timerText.text = "0:00";
        Debug.Log("Timer finished!");
        // Add any game-over logic here
    }

    public void StartTimer() => isRunning = true;
    public void StopTimer()  => isRunning = false;
    public void ResetTimer() { timeRemaining = totalTime; isRunning = false; UpdateDisplay(); }
}
