using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InstructionsUI : MonoBehaviour
{
    [Header("Panel Root")]
    [Tooltip("The root GameObject of the instructions panel. " +
             "Will be enabled/disabled to show/hide the whole UI.")]
    public GameObject panelRoot;

    [Header("Text Fields")]
    [Tooltip("Large title text — e.g. 'Flood Town'")]
    public TMP_Text titleText;

    [Tooltip("Multi-line objective description.")]
    public TMP_Text objectiveText;

    [Tooltip("Multi-line controls reference.")]
    public TMP_Text controlsText;

    [Tooltip("Optional countdown label shown while the panel is up, e.g. 'You have 2:00'.")]
    public TMP_Text timeLimitText;

    [Header("Start Button")]
    [Tooltip("The 'Start Game' button. Attach OnStartButtonPressed() to its OnClick event " +
             "in the Inspector, or leave this script to wire it up at runtime.")]
    public Button startButton;

    [Header("Fade Settings")]
    [Tooltip("Canvas Group on the panel root used for alpha fading.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Seconds the panel takes to fade in when the scene loads.")]
    public float fadeInDuration  = 0.8f;

    [Tooltip("Seconds the panel takes to fade out after Start is pressed.")]
    public float fadeOutDuration = 0.5f;

    [Header("Content — edit here or override in Inspector")]
    [TextArea(2, 4)]
    public string title = "FLOOD TOWN";

    [TextArea(4, 8)]
    public string objective =
        "A river is rising!\n\n" +
        "Work together to dig a waterway through town.\n" +
        "Connect the START block to the FINISH block before time runs out.\n\n" +
        "Keep villagers safe — lift them to high ground if water gets close!";

    [TextArea(6, 12)]
    public string controls =
        "LEFT CONTROLLER\n" +
        "  Grip          — Pick up a villager\n" +
        "  Thumbstick    — Rotate the town view\n\n" +
        "RIGHT CONTROLLER\n" +
        "  Trigger       — Use selected tool\n" +
        "  A / B         — Cycle tools\n\n" +
        "TOOLS  (shared — only one set!)\n" +
        "  Shovel        — Dig straight or corner waterway\n" +
        "  Axe           — Clear trees\n" +
        "  Pickaxe       — Smash boulders\n" +
        "  Bucket        — Bail rising water\n\n" +
        "POWERUPS  (found while digging)\n" +
        "  Ice Cube    — Freeze water 10 s\n" +
        "  TNT        — Instant clear\n" +
        "  Life Buoy  — Villagers safe 30 s\n" +
        "  Speed      — Tools 2× faster 20 s";

    private Coroutine fadeCoroutine;

    void Start()
    {
        if (titleText != null && !string.IsNullOrEmpty(title)) titleText.text = title;
        if (objectiveText != null && !string.IsNullOrEmpty(objective)) objectiveText.text = objective;
        if (controlsText != null && !string.IsNullOrEmpty(controls))  controlsText.text  = controls;

        // Show the time limit if CountdownTimer is in the scene
        if (timeLimitText != null && CountdownTimer.Instance != null)
        {
            int totalSecs = Mathf.RoundToInt(CountdownTimer.Instance.totalTime);
            int mins = totalSecs / 60;
            int secs = totalSecs % 60;
            timeLimitText.text = secs == 0
                ? $"You have {mins} minute{(mins == 1 ? "" : "s")}!"
                : $"You have {mins}:{secs:00} minutes!";
        }

        if (startButton != null)
            startButton.onClick.AddListener(OnStartButtonPressed);

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (panelRoot != null) panelRoot.SetActive(true);

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTo(1f, fadeInDuration));
    }


    public void OnStartButtonPressed()
    {
        // Disable button immediately so it can't be pressed twice during fade
        if (startButton != null) startButton.interactable = false;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeOutThenNotify());
    }

    /// <summary>
    /// Called by GameManager on peers that did not press the button themselves,
    /// so their instructions panel is also dismissed when the game starts.
    /// </summary>
    public void ForceHide()
    {
        if (!isActiveAndEnabled)
        {
            CompleteHideImmediate();
            return;
        }

        if (startButton != null) startButton.interactable = false;
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTo(0f, fadeOutDuration));
        StartCoroutine(HideAfterFade());
    }

    IEnumerator HideAfterFade()
    {
        yield return new WaitForSeconds(fadeOutDuration);
        CompleteHideImmediate();
    }

    IEnumerator FadeOutThenNotify()
    {
        yield return StartCoroutine(FadeTo(0f, fadeOutDuration));

        CompleteHideImmediate();

        if (GameManager.Instance != null)
            GameManager.Instance.OnInstructionsDismissed();
        else
            Debug.LogWarning("[InstructionsUI] GameManager.Instance is null — " +
                             "timer will not start. Add a GameManager to the scene.");
    }

    IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (canvasGroup == null) yield break;

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    void CompleteHideImmediate()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (panelRoot != null) panelRoot.SetActive(false);
    }
}
