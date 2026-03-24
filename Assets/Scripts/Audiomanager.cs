using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Background Music")]
    public AudioClip natureMusic;
    [Range(0f, 1f)] public float natureMusicVolume = 0.35f;

    [Header("Footsteps")]
    public AudioClip[] footstepClips;
    [Range(0f, 1f)] public float footstepVolume = 0.6f;
    public float footstepInterval = 0.45f;
    public float footstepMoveThreshold = 0.2f;

    [Header("Destroy Sounds")]
    [Tooltip("Played when a tree or flower is chopped.")]
    public AudioClip treeDestroyClip;
    [Range(0f, 1f)] public float treeDestroyVolume = 0.8f;

    [Tooltip("Played when a rock is smashed.")]
    public AudioClip rockDestroyClip;
    [Range(0f, 1f)] public float rockDestroyVolume = 0.8f;

    [Tooltip("Played when a placed path block is deleted.")]
    public AudioClip pathDeleteClip;
    [Range(0f, 1f)] public float pathDeleteVolume = 0.8f;

    [Header("Path Building")]
    [Tooltip("Played when a path block is successfully placed.")]
    public AudioClip pathBuiltClip;
    [Range(0f, 1f)] public float pathBuiltVolume = 0.75f;

    [Tooltip("Played when a placement attempt is rejected (wrong orientation etc).")]
    public AudioClip invalidPlacementClip;
    [Range(0f, 1f)] public float invalidVolume = 0.7f;

    [Header("Pickup")]
    [Tooltip("Played when any XR grab interactable is picked up.")]
    public AudioClip pickupClip;
    [Range(0f, 1f)] public float pickupVolume = 0.7f;

    [Header("Villager")]
    [Tooltip("Played when a villager dies.")]
    public AudioClip villagerDeathClip;
    [Range(0f, 1f)] public float villagerDeathVolume = 0.9f;

    [Tooltip("Played when a villager is picked up.")]
    public AudioClip villagerPickupClip;
    [Range(0f, 1f)] public float villagerPickupVolume = 0.8f;

    [Header("Timer Warning")]
    [Tooltip("Short loopable clip played when ≤ timerWarningThreshold seconds remain.")]
    public AudioClip timerWarningClip;
    [Range(0f, 1f)] public float timerWarningVolume = 0.6f;
    [Tooltip("Seconds remaining at which the warning sound starts.")]
    public float timerWarningThreshold = 20f;

    [Header("End Game")]
    public AudioClip winClip;
    public AudioClip loseClip;
    [Range(0f, 1f)] public float endGameVolume = 1f;

    [Header("Powerups")]
    [Tooltip("Played when the water bucket douses a lava cell.")]
    public AudioClip waterBucketClip;
    [Range(0f, 1f)] public float waterBucketVolume = 0.85f;

    [Tooltip("Played when the bomb explodes.")]
    public AudioClip bombClip;
    [Range(0f, 1f)] public float bombVolume = 1f;

    [Tooltip("Played when the ice power-up freezes villagers.")]
    public AudioClip iceClip;
    [Range(0f, 1f)] public float iceVolume = 0.85f;

    private AudioSource musicSource;
    private AudioSource footstepSource;
    private AudioSource timerWarningSource; // looping warning tick

    private float footstepTimer = 0f;
    private int lastFootstepIndex = -1;
    private bool warningPlaying = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.clip = natureMusic;
        musicSource.loop = true;
        musicSource.volume = natureMusicVolume;
        musicSource.spatialBlend = 0f;
        musicSource.playOnAwake = false;
        if (natureMusic != null) musicSource.Play();
        else Debug.LogWarning("[AudioManager] natureMusic clip not assigned.");

        footstepSource = gameObject.AddComponent<AudioSource>();
        footstepSource.loop = false;
        footstepSource.volume = footstepVolume;
        footstepSource.spatialBlend = 0f;
        footstepSource.playOnAwake = false;

        // Timer warning source — 2D looping, starts silent
        timerWarningSource = gameObject.AddComponent<AudioSource>();
        timerWarningSource.clip = timerWarningClip;
        timerWarningSource.loop = true;
        timerWarningSource.volume = 0f;
        timerWarningSource.spatialBlend = 0f;
        timerWarningSource.playOnAwake = false;
        if (timerWarningClip != null) timerWarningSource.Play();
    }

    void Update()
    {
        HandleFootsteps();
    }

    void HandleFootsteps()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;

        float mag = GetThumbstickMagnitude();
        if (mag > footstepMoveThreshold)
        {
            footstepTimer -= Time.deltaTime;
            if (footstepTimer <= 0f) { PlayFootstep(); footstepTimer = footstepInterval; }
        }
        else
        {
            footstepTimer = 0f;
        }
    }

    float GetThumbstickMagnitude()
    {
        // Iterate all Input System devices for any XR controller thumbstick
        foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
        {
            if (device is UnityEngine.InputSystem.XR.XRController xr)
            {
                var axis = xr.TryGetChildControl("Primary2DAxis")
                    as UnityEngine.InputSystem.Controls.Vector2Control;
                if (axis != null)
                {
                    float mag = axis.ReadValue().magnitude;
                    if (mag > 0.01f) return mag;
                }
            }
        }

        // Keyboard fallback (WASD / arrows) for desktop testing
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            float x = 0f, y = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y = 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y = -1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x = -1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x = 1f;
            return new Vector2(x, y).magnitude;
        }
        return 0f;
    }

    void PlayFootstep()
    {
        if (footstepClips.Length == 0) return;
        int index;
        if (footstepClips.Length == 1) { index = 0; }
        else { do { index = Random.Range(0, footstepClips.Length); } while (index == lastFootstepIndex); }
        lastFootstepIndex = index;
        if (footstepClips[index] == null) return;
        footstepSource.clip = footstepClips[index];
        footstepSource.Play();
    }

    // Played by AxeTool when a tree/flower is successfully chopped.
    public void PlayTreeDestroySound(Vector3 worldPos)
        => PlayAtPoint(treeDestroyClip, worldPos, treeDestroyVolume);

    // Played by PickaxeTool when a rock is successfully smashed.
    public void PlayRockDestroySound(Vector3 worldPos)
        => PlayAtPoint(rockDestroyClip, worldPos, rockDestroyVolume);

    // Played by DeleteTool when a path block is deleted.
    public void PlayPathDeleteSound(Vector3 worldPos)
        => PlayAtPoint(pathDeleteClip, worldPos, pathDeleteVolume);

    // Played by ShovelTool after a path block is successfully placed.
    public void PlayPathBuiltSound()
        => PlayOnMusicSource(pathBuiltClip, pathBuiltVolume);

    // Played by ShovelTool / PathChecker when a placement is rejected.
    public void PlayInvalidPlacementSound()
        => PlayOnMusicSource(invalidPlacementClip, invalidVolume);

    // Played by ScaleAndResetOnGrab (or any tool) when grabbed.
    public void PlayPickupSound()
        => PlayOnMusicSource(pickupClip, pickupVolume);
    
    // Played when a villager is picked up by the player.
    public void PlayVillagerPickupSound(Vector3 worldPos)
        => PlayAtPoint(villagerPickupClip, worldPos, villagerPickupVolume);

    // Played by VillagerAgent.Die().
    public void PlayVillagerDeathSound(Vector3 worldPos)
        => PlayAtPoint(villagerDeathClip, worldPos, villagerDeathVolume);

    // Played when bucket is used.
    public void PlayWaterBucketSound(Vector3 worldPos)
        => PlayAtPoint(waterBucketClip, worldPos, waterBucketVolume);
    
    // Played by BombPowerup when the bomb explodes.
    public void PlayBombSound(Vector3 worldPos)
        => PlayAtPoint(bombClip, worldPos, bombVolume);

    // Played when the ice power-up activates.
    public void PlayIceSound(Vector3 worldPos)
        => PlayAtPoint(iceClip, worldPos, iceVolume);

    // Called by CountdownTimer every Update with the remaining seconds.
    public void UpdateTimerWarning(float timeRemaining)
    {
        if (timerWarningClip == null || timerWarningSource == null) return;

        if (timeRemaining <= timerWarningThreshold && timeRemaining > 0f)
        {
            // Fade volume in proportionally — loudest at 0 seconds remaining
            float t = 1f - (timeRemaining / timerWarningThreshold);
            timerWarningSource.volume = Mathf.Lerp(0f, timerWarningVolume, t);
            warningPlaying = true;
        }
        else if (warningPlaying)
        {
            timerWarningSource.volume = 0f;
            warningPlaying = false;
        }
    }

    // Stop the timer warning (called on game end).
    public void StopTimerWarning()
    {
        if (timerWarningSource != null) timerWarningSource.volume = 0f;
        warningPlaying = false;
    }

    // Played by GameManager on win.
    public void PlayWinSound()
    {
        StopTimerWarning();
        if (winClip != null) StartCoroutine(FadeMusicAndPlayClip(winClip));
        else Debug.LogWarning("[AudioManager] winClip not assigned.");
    }

    // Played by GameManager on lose.
    public void PlayLoseSound()
    {
        StopTimerWarning();
        if (loseClip != null) StartCoroutine(FadeMusicAndPlayClip(loseClip));
        else Debug.LogWarning("[AudioManager] loseClip not assigned.");
    }

    // Fire-and-forget 3D positional audio (uses a temporary AudioSource at worldPos).
    static void PlayAtPoint(AudioClip clip, Vector3 worldPos, float volume)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, worldPos, volume);
    }

    // 2D one-shot through the music source (UI/feedback sounds have no position).
    void PlayOnMusicSource(AudioClip clip, float volume)
    {
        if (clip == null || musicSource == null) return;
        musicSource.PlayOneShot(clip, volume);
    }

    System.Collections.IEnumerator FadeMusicAndPlayClip(AudioClip clip)
    {
        float startVol = musicSource.volume;
        float elapsed = 0f;
        float fadeDur = 1f;

        while (elapsed < fadeDur)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVol, startVol * 0.2f, elapsed / fadeDur);
            yield return null;
        }
        musicSource.PlayOneShot(clip, endGameVolume);
    }
}
