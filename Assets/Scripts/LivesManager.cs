using System.Collections.Generic;
using UnityEngine;
using Ubiq.Messaging;

/// <summary>
/// Manages the shared team lives (hearts). Lives are global — not per player.
/// When a villager dies the authority peer calls LoseLife(), which broadcasts
/// the event so every peer removes a heart. When the last life is lost the
/// game transitions to the Lost state and the timer shows "END GAME".
/// </summary>
public class LivesManager : MonoBehaviour
{
    public static LivesManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Total lives shared by all players.")]
    public int maxLives = 3;

    [Header("Heart Display")]
    [Tooltip("The White Heart prefab to instantiate for each life.")]
    public GameObject heartPrefab;

    [Tooltip("Where the row of hearts is placed. If left empty the hearts " +
             "are placed alongside the countdown timer text automatically.")]
    public Transform heartsAnchor;

    [Tooltip("World-space gap between consecutive heart objects.")]
    public float heartSpacing = 0.5f;

    // ── runtime state ──────────────────────────────────────────────────────
    private int _currentLives;
    private readonly List<GameObject> _heartObjects = new();
    private NetworkContext _net;

    [System.Serializable]
    private struct LivesMsg { public string type; }

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _net = NetworkScene.Register(this);
    }

    // ── Ubiq networking ────────────────────────────────────────────────────

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var msg = message.FromJson<LivesMsg>();
        if (msg.type == "LoseLife")
            ApplyLoseLife();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Called by GameManager when the game starts on every peer.</summary>
    public void InitLives()
    {
        // Destroy any leftover hearts from a previous run
        foreach (var h in _heartObjects)
            if (h != null) Destroy(h);
        _heartObjects.Clear();

        _currentLives = maxLives;

        if (heartPrefab == null)
        {
            Debug.LogWarning("[LivesManager] heartPrefab is not assigned.");
            return;
        }

        // Determine the world-space anchor for the hearts row
        Transform anchor = ResolveAnchor();

        for (int i = 0; i < maxLives; i++)
        {
            Vector3 pos = anchor.position + anchor.right * (i * heartSpacing);
            GameObject heart = Instantiate(heartPrefab, pos, anchor.rotation);
            _heartObjects.Add(heart);
        }
    }

    /// <summary>
    /// Called by the authority peer (via GameManager.OnVillagerDied).
    /// Broadcasts the life-loss event to all peers and applies it locally.
    /// </summary>
    public void LoseLife()
    {
        _net.SendJson(new LivesMsg { type = "LoseLife" });
        ApplyLoseLife();
    }

    // ── Internal helpers ───────────────────────────────────────────────────

    private void ApplyLoseLife()
    {
        if (_currentLives <= 0) return;

        _currentLives--;

        // Remove the rightmost (last) heart
        int last = _heartObjects.Count - 1;
        if (last >= 0)
        {
            if (_heartObjects[last] != null) Destroy(_heartObjects[last]);
            _heartObjects.RemoveAt(last);
        }

        Debug.Log($"[LivesManager] Life lost — {_currentLives}/{maxLives} remaining.");

        if (_currentLives <= 0)
            OnAllLivesGone();
    }

    private void OnAllLivesGone()
    {
        // Show END GAME on the timer immediately
        if (CountdownTimer.Instance != null)
            CountdownTimer.Instance.ShowEndGame();

        // Tell GameManager to enter the Lost state
        if (GameManager.Instance != null)
            GameManager.Instance.OnAllLivesLost();
        else
            Debug.LogWarning("[LivesManager] GameManager.Instance is null.");
    }

    /// <summary>
    /// Returns the Transform to use as the starting point for the hearts row.
    /// Priority: explicitly assigned heartsAnchor → timer text position → self.
    /// </summary>
    private Transform ResolveAnchor()
    {
        if (heartsAnchor != null)
            return heartsAnchor;

        if (CountdownTimer.Instance != null && CountdownTimer.Instance.timerText != null)
            return CountdownTimer.Instance.timerText.transform;

        return transform;
    }
}
