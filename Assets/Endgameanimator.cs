using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndGameAnimator : MonoBehaviour
{

    [Header("Glow / Pulse")]
    [Tooltip("How many seconds each block takes to reach peak glow.")]
    public float glowRampDuration  = 0.4f;

    [Tooltip("Seconds between each successive block lighting up along the path.")]
    public float glowStaggerDelay  = 0.12f;

    [Tooltip("The HDR emission colour used for the win glow (bright cyan-green).")]
    public Color winGlowColor  = new Color(0.0f, 4.0f, 2.0f, 1f); 

    [Tooltip("The HDR emission colour used for the lose pulse (red).")]
    public Color loseGlowColor = new Color(4.0f, 0.2f, 0.0f, 1f);

    [Header("Water Flow Particles")]
    [Tooltip("Particle System prefab used as the 'water drop' travelling along the path. " +
             "Should be a short-lived burst (e.g. 0.5s lifetime, looping OFF).")]
    public ParticleSystem waterParticlePrefab;

    [Tooltip("Y offset above the grid surface at which water particles spawn.")]
    public float waterParticleYOffset = 0.15f;

    [Tooltip("Seconds the water particle effect lingers at each cell before moving on.")]
    public float waterFlowStepDelay = 0.18f;

    [Header("Fireworks")]
    [Tooltip("Particle System prefab for the fireworks burst at the Finish cell. " +
             "Should be a one-shot burst with high start speed and colourful sub-emitters.")]
    public ParticleSystem fireworksPrefab;

    [Tooltip("Number of firework bursts fired in sequence at the Finish cell.")]
    public int fireworksBurstCount = 4;

    [Tooltip("Seconds between each firework burst.")]
    public float fireworksBurstInterval = 0.5f;

    [Tooltip("Height above the grid surface at which fireworks explode.")]
    public float fireworksHeight = 1.5f;

    [Header("Lose — Grey Fade")]
    [Tooltip("How quickly path blocks desaturate to grey after the lose pulse.")]
    public float loseFadeDuration = 1.2f;


    // Cached ordered list of cells from Start to Finish (filled by BuildPath)
    private List<Vector2Int> winPath = new List<Vector2Int>();

    // Running coroutines — kept so we can stop them if the scene restarts
    private Coroutine winCoroutine;
    private Coroutine loseCoroutine;

    // Start the full win animation sequence.
    public void PlayWinSequence()
    {
        if (winCoroutine != null) StopCoroutine(winCoroutine);
        winCoroutine = StartCoroutine(WinSequence());
    }

    // Start the lose animation sequence.
    public void PlayLoseSequence()
    {
        if (loseCoroutine != null) StopCoroutine(loseCoroutine);
        loseCoroutine = StartCoroutine(LoseSequence());
    }

    IEnumerator WinSequence()
    {
        Debug.Log("[EndGameAnimator] Win sequence started.");

        // 1 — build the ordered path so we know which cells to animate
        BuildWinPath();

        if (winPath.Count == 0)
        {
            Debug.LogWarning("[EndGameAnimator] Win path is empty — cannot animate.");
            yield break;
        }

        // 2 — glow each block along the path in order
        yield return StartCoroutine(GlowPathBlocks(winGlowColor));

        // 3 — water particles travel Start to Finish
        yield return StartCoroutine(FlowWaterAlongPath());

        // 4 — fireworks at the Finish cell
        yield return StartCoroutine(FireFireworks());

        Debug.Log("[EndGameAnimator] Win sequence complete.");
    }

    IEnumerator LoseSequence()
    {
        Debug.Log("[EndGameAnimator] Lose sequence started.");

        // Collect every placed path block (not just the winning path)
        var allPathObjects = new List<GameObject>();
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Path && kvp.Value.placedObject != null)
                allPathObjects.Add(kvp.Value.placedObject);
        }

        // Red pulse on all blocks simultaneously
        float elapsed = 0f;
        while (elapsed < glowRampDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / glowRampDuration);
            Color current = Color.Lerp(Color.black, loseGlowColor, t);
            foreach (var obj in allPathObjects) SetEmission(obj, current);
            yield return null;
        }

        // Short hold at peak red
        yield return new WaitForSeconds(0.3f);

        // Fade all blocks to grey
        elapsed = 0f;
        Color grey = new Color(0.35f, 0.35f, 0.35f, 1f);
        while (elapsed < loseFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / loseFadeDuration);
            foreach (var obj in allPathObjects)
            {
                SetEmission(obj, Color.Lerp(loseGlowColor, Color.black, t));
                SetSurfaceColor(obj, Color.Lerp(Color.white, grey, t));
            }
            yield return null;
        }

        // Small particle puff at Start cell to signal "water couldn't get through"
        SpawnParticleAt(waterParticlePrefab, GetCellWorldPos(GetStartCell()), waterParticleYOffset);

        Debug.Log("[EndGameAnimator] Lose sequence complete.");
    }

    IEnumerator GlowPathBlocks(Color targetGlow)
    {
        foreach (Vector2Int cell in winPath)
        {
            if (!GridManager.Instance.TryGetCell(cell, out GridCell data)) continue;
            if (data.placedObject == null) continue;

            // Ramp emission up on this block while the next stagger delay ticks
            StartCoroutine(RampEmission(data.placedObject, Color.black, targetGlow, glowRampDuration));
            yield return new WaitForSeconds(glowStaggerDelay);
        }

        // Wait for the last block's glow to finish before moving on
        yield return new WaitForSeconds(glowRampDuration);
    }

    IEnumerator RampEmission(GameObject obj, Color from, Color to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetEmission(obj, Color.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetEmission(obj, to);
    }

    IEnumerator FlowWaterAlongPath()
    {
        if (waterParticlePrefab == null)
        {
            Debug.LogWarning("[EndGameAnimator] waterParticlePrefab not assigned — skipping water flow.");
            yield break;
        }

        foreach (Vector2Int cell in winPath)
        {
            Vector3 spawnPos = GetCellWorldPos(cell) + new Vector3(0f, waterParticleYOffset, 0f);
            SpawnParticleAt(waterParticlePrefab, spawnPos, 0f);
            yield return new WaitForSeconds(waterFlowStepDelay);
        }
    }

    IEnumerator FireFireworks()
    {
        if (fireworksPrefab == null)
        {
            Debug.LogWarning("[EndGameAnimator] fireworksPrefab not assigned — skipping fireworks.");
            yield break;
        }

        Vector2Int finishCell = GetFinishCell();
        Vector3 basePos = GetCellWorldPos(finishCell) + new Vector3(0f, fireworksHeight, 0f);

        for (int i = 0; i < fireworksBurstCount; i++)
        {
            // Slight random XZ scatter so bursts don't all overlap
            Vector3 scatter = new Vector3(
                Random.Range(-0.4f, 0.4f), 0f,
                Random.Range(-0.4f, 0.4f));

            SpawnParticleAt(fireworksPrefab, basePos + scatter, 0f);
            yield return new WaitForSeconds(fireworksBurstInterval);
        }
    }

    // Rebuilds winPath as an ordered list of cells from Start to Finish.
    // same BFS + parent-map technique as PathChecker.CheckPath().
    void BuildWinPath()
    {
        winPath.Clear();

        Vector2Int startCell  = GetStartCell();
        Vector2Int finishCell = GetFinishCell();

        if (startCell == finishCell) return; // safety guard for degenerate grids

        var parent  = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new HashSet<Vector2Int> { startCell };
        var queue   = new Queue<Vector2Int>();
        queue.Enqueue(startCell);
        bool reached = false;

        while (queue.Count > 0 && !reached)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var dir in PathDirections.All)
            {
                Vector2Int neighbour = current + dir;
                if (visited.Contains(neighbour)) continue;

                // Only walk through mutually-connected cells (same rule as PathChecker)
                if (PathChecker.Instance != null && !PathChecker.Instance.AreMutuallyConnected(current, neighbour))
                    continue;

                // Also accept the finish cell even if PathChecker considers it terminal
                if (!GridManager.Instance.TryGetCell(neighbour, out GridCell data)) continue;
                if (data.type != CellType.Path && data.type != CellType.Start && data.type != CellType.Finish)
                    continue;

                parent[neighbour] = current;
                visited.Add(neighbour);
                queue.Enqueue(neighbour);

                if (neighbour == finishCell) { reached = true; break; }
            }
        }

        if (!reached)
        {
            Debug.LogWarning("[EndGameAnimator] BuildWinPath: could not retrace path — BFS did not reach Finish.");
            return;
        }

        // Walk the parent map backward from Finish to Start, then reverse
        Vector2Int step = finishCell;
        while (step != startCell)
        {
            winPath.Add(step);
            step = parent[step];
        }
        winPath.Add(startCell);
        winPath.Reverse(); // now ordered Start -> Finish
    }


    Vector2Int GetStartCell()
    {
        foreach (var kvp in GridManager.Instance.GetAllCells())
            if (kvp.Value.type == CellType.Start) return kvp.Key;
        return GridManager.Instance.gridMin; // fallback
    }

    Vector2Int GetFinishCell()
    {
        foreach (var kvp in GridManager.Instance.GetAllCells())
            if (kvp.Value.type == CellType.Finish) return kvp.Key;
        return GridManager.Instance.gridMax; // fallback
    }

    Vector3 GetCellWorldPos(Vector2Int cell) => GridManager.Instance.GridToWorld(cell);

    static void SetEmission(GameObject obj, Color emissionColor)
    {
        if (obj == null) return;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in rend.materials)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", emissionColor);
            }
        }
    }

    static void SetSurfaceColor(GameObject obj, Color color)
    {
        if (obj == null) return;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
                mat.color = color;
            }
        }
    }
    static void SpawnParticleAt(ParticleSystem prefab, Vector3 worldPos, float yOffset)
    {
        if (prefab == null) return;

        Vector3 pos = worldPos + new Vector3(0f, yOffset, 0f);
        ParticleSystem ps = Instantiate(prefab, pos, Quaternion.identity);
        ps.Play();

        // Auto-destroy after the system's duration + max particle lifetime
        float destroyDelay = ps.main.duration + ps.main.startLifetime.constantMax + 0.5f;
        Destroy(ps.gameObject, destroyDelay);
    }
}