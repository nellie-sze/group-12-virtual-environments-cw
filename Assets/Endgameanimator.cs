using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndGameAnimator : MonoBehaviour
{
    [Header("Glow / Pulse")]
    [Tooltip("How many seconds each block takes to reach peak glow.")]
    public float glowRampDuration = 0.4f;

    [Tooltip("Seconds between each successive block lighting up along the path.")]
    public float glowStaggerDelay = 0.12f;

    [Tooltip("The HDR emission colour used for the win glow (bright cyan-green).")]
    public Color winGlowColor = new Color(0.0f, 4.0f, 2.0f, 1f);

    [Tooltip("The HDR emission colour used for the lose pulse (red).")]
    public Color loseGlowColor = new Color(4.0f, 0.2f, 0.0f, 1f);

    [Header("Water Flow Particles")]
    [Tooltip("Particle System prefab used as the 'water drop' travelling along the path.")]
    public ParticleSystem waterParticlePrefab;

    [Tooltip("Y offset above the grid surface at which water particles spawn.")]
    public float waterParticleYOffset = 0.15f;

    [Tooltip("Seconds the water particle effect lingers at each cell before moving on.")]
    public float waterFlowStepDelay = 0.18f;

    [Header("Fireworks")]
    [Tooltip("Particle System prefab for the fireworks burst at the Finish cell.")]
    public ParticleSystem fireworksPrefab;

    [Tooltip("Number of firework bursts fired in sequence at the Finish cell.")]
    public int fireworksBurstCount = 4;

    [Tooltip("Seconds between each firework burst.")]
    public float fireworksBurstInterval = 0.5f;

    [Tooltip("Height above the grid surface at which fireworks explode.")]
    public float fireworksHeight = 1.5f;

    [Header("Lose — Fire Spread")]
    [Tooltip("Fire particle prefab spawned per grid cell as fire spreads outward from lava. " + "Use a looping flame Particle System with orange/red colours, Simulation Space = World.")]
    public ParticleSystem firePrefab;

    [Tooltip("Seconds between each wave of fire spreading one cell outward.")]
    public float fireSpreadInterval = 0.08f;

    [Tooltip("How long fire burns before the lava flood arrives.")]
    public float fireDuration = 3.5f;

    [Tooltip("Y offset at which fire particles spawn above the surface.")]
    public float fireYOffset = 0.05f;

    [Tooltip("How dark blocks char as fire passes over them (0 = no char, 1 = full black).")]
    [Range(0f, 1f)]
    public float charAmount = 0.85f;

    [Header("Lose — Lava Flood")]
    [Tooltip("Lava particle prefab that floods across the whole grid after fire. " + "Use a looping flat orange/red Particle System, Simulation Space = World.")]
    public ParticleSystem lavaPrefab;

    [Tooltip("Seconds for the lava flood to fill the whole grid.")]
    public float lavaFloodDuration = 2.5f;

    [Header("Lose — Directional Light")]
    [Tooltip("Assign the scene Directional Light — it fades to red during the lose sequence.")]
    public Light directionalLight;

    [Tooltip("Colour the light transitions to.")]
    public Color loseLightColor = new Color(1.0f, 0.15f, 0.0f);

    [Tooltip("Seconds the light takes to transition.")]
    public float lightTransitionDuration = 1.5f;

    private List<Vector2Int> winPath = new List<Vector2Int>();
    private Coroutine winCoroutine;
    private Coroutine loseCoroutine;
    private List<ParticleSystem> activeFireParticles = new List<ParticleSystem>();


    public void PlayWinSequence()
    {
        if (winCoroutine != null) StopCoroutine(winCoroutine);
        winCoroutine = StartCoroutine(WinSequence());
    }

    public void PlayLoseSequence()
    {
        if (loseCoroutine != null) StopCoroutine(loseCoroutine);
        loseCoroutine = StartCoroutine(LoseSequence());
    }

    IEnumerator WinSequence()
    {
        Debug.Log("[EndGameAnimator] Win sequence started.");
        BuildWinPath();

        if (winPath.Count == 0)
        {
            Debug.LogWarning("[EndGameAnimator] Win path is empty — cannot animate.");
            yield break;
        }

        yield return StartCoroutine(GlowPathBlocks(winGlowColor));
        yield return StartCoroutine(FlowWaterAlongPath());
        yield return StartCoroutine(FireFireworks());

        Debug.Log("[EndGameAnimator] Win sequence complete.");
    }

    //  LOSE sequence
    //  1. Light turns red simultaneously with fire spreading from lava outward
    //  2. Every cell chars to black as fire reaches it
    //  3. Lava floods the whole grid

    IEnumerator LoseSequence()
    {
        Debug.Log("[EndGameAnimator] Lose sequence started.");

        // Light transition runs in parallel — don't yield on it
        StartCoroutine(TransitionLightToRed());

        // Fire spreads outward from lava cells across the whole grid
        yield return StartCoroutine(SpreadFireAcrossGrid());

        // Short dramatic pause at peak fire
        yield return new WaitForSeconds(0.4f);

        // Lava rises and floods everything
        yield return StartCoroutine(FloodLava());

        Debug.Log("[EndGameAnimator] Lose sequence complete.");
    }

    IEnumerator TransitionLightToRed()
    {
        // Auto-find if not assigned in Inspector
        if (directionalLight == null)
            directionalLight = FindFirstObjectByType<Light>();

        if (directionalLight == null) yield break;

        Color startColor = directionalLight.color;
        float elapsed = 0f;

        while (elapsed < lightTransitionDuration)
        {
            elapsed += Time.deltaTime;
            directionalLight.color = Color.Lerp(startColor, loseLightColor,
                Mathf.Clamp01(elapsed / lightTransitionDuration));
            yield return null;
        }
        directionalLight.color = loseLightColor;
    }

    IEnumerator SpreadFireAcrossGrid()
    {
        var visited = new HashSet<Vector2Int>();
        var frontier = new Queue<Vector2Int>();

        // Seed the BFS from every existing lava cell
        foreach (var kvp in GridManager.Instance.GetAllCells())
        {
            if (kvp.Value.type == CellType.Lava)
            {
                frontier.Enqueue(kvp.Key);
                visited.Add(kvp.Key);
                SpawnFireAt(kvp.Key);
            }
        }

        // If no lava exists, start from the centre of the grid
        if (frontier.Count == 0)
        {
            Vector2Int centre = new Vector2Int(
                (GridManager.Instance.gridMin.x + GridManager.Instance.gridMax.x) / 2,
                (GridManager.Instance.gridMin.y + GridManager.Instance.gridMax.y) / 2);
            frontier.Enqueue(centre);
            visited.Add(centre);
            SpawnFireAt(centre);
        }

        // BFS wave expansion — one ring per interval gives a visible outward spread
        while (frontier.Count > 0)
        {
            int waveSize = frontier.Count;

            for (int i = 0; i < waveSize; i++)
            {
                Vector2Int current = frontier.Dequeue();

                foreach (Vector2Int dir in PathDirections.All)
                {
                    Vector2Int neighbour = current + dir;
                    if (visited.Contains(neighbour)) continue;
                    if (!GridManager.Instance.IsInBounds(neighbour)) continue;

                    visited.Add(neighbour);
                    frontier.Enqueue(neighbour);

                    SpawnFireAt(neighbour);
                    CharCellObject(neighbour); // char whatever object sits here
                }
            }

            yield return new WaitForSeconds(fireSpreadInterval);
        }

        // Hold fire at full coverage before lava arrives
        yield return new WaitForSeconds(fireDuration);
    }

    // Spawns a looping fire particle at the given grid cell
    void SpawnFireAt(Vector2Int cell)
    {
        if (firePrefab == null) return;

        Vector3 pos = GetCellWorldPos(cell) + new Vector3(0f, fireYOffset, 0f);
        ParticleSystem ps = Instantiate(firePrefab, pos, Quaternion.identity);
        ps.Play();
        activeFireParticles.Add(ps);

        // Auto-destroy well after lava flood completes
        Destroy(ps.gameObject, fireDuration + lavaFloodDuration + 2f);
    }

    // Chars (darkens) the object at the given cell as fire passes over it
    void CharCellObject(Vector2Int cell)
    {
        if (!GridManager.Instance.TryGetCell(cell, out GridCell data)) return;
        if (data.placedObject == null) return;
        StartCoroutine(CharObject(data.placedObject));
    }

    IEnumerator CharObject(GameObject obj)
    {
        if (obj == null) yield break;

        var renderers = obj.GetComponentsInChildren<Renderer>();
        var origColors = new List<Color>();

        // Cache original surface colours
        foreach (var rend in renderers)
            foreach (var mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) origColors.Add(mat.GetColor("_BaseColor"));
                else if (mat.HasProperty("_Color")) origColors.Add(mat.GetColor("_Color"));
                else origColors.Add(mat.color);
            }

        Color charColor = new Color(0.08f, 0.04f, 0.02f, 1f); // dark charcoal
        float charTime = glowRampDuration * 2f;
        float elapsed = 0f;

        while (elapsed < charTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / charTime);

            int idx = 0;
            foreach (var rend in renderers)
                foreach (var mat in rend.materials)
                {
                    // Surface colour lerps toward charcoal
                    Color orig = idx < origColors.Count ? origColors[idx] : Color.white;
                    Color c = Color.Lerp(orig, charColor, t * charAmount);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                    mat.color = c;

                    // Emission: brief orange flash then fades to black
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        // Bell curve — peaks at 50% through the char time
                        float emitT = Mathf.Sin(Mathf.Clamp01(elapsed / (charTime * 0.5f)) * Mathf.PI);
                        mat.SetColor("_EmissionColor", Color.Lerp(Color.black, loseGlowColor * 2f, emitT));
                    }
                    idx++;
                }

            yield return null;
        }

        // Settle to full char, emission off
        foreach (var rend in renderers)
            foreach (var mat in rend.materials)
            {
                Color c = Color.Lerp(Color.white, charColor, charAmount);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                mat.color = c;
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.black);
            }
    }

    IEnumerator FloodLava()
    {
        if (lavaPrefab == null)
        {
            Debug.LogWarning("[EndGameAnimator] lavaPrefab not assigned — skipping lava flood.");
            CleanUpFire();
            yield break;
        }

        Vector2Int gridMin = GridManager.Instance.gridMin;
        Vector2Int gridMax = GridManager.Instance.gridMax;
        float gs = GridManager.Instance.gridSize;

        // Centre of the grid in world space
        Vector3 gridCentre = new Vector3(
            (gridMin.x + gridMax.x) * 0.5f * gs,
            GetCellWorldPos(gridMin).y + fireYOffset,
            (gridMin.y + gridMax.y) * 0.5f * gs);

        float gridWidth = (gridMax.x - gridMin.x + 1) * gs;
        float gridDepth = (gridMax.y - gridMin.y + 1) * gs;

        // Spawn lava system rotated flat, shaped to the grid's footprint
        ParticleSystem lavaPs = Instantiate(lavaPrefab, gridCentre, Quaternion.Euler(90f, 0f, 0f));

        var shape = lavaPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Rectangle;
        shape.scale = new Vector3(gridWidth, gridDepth, 1f);

        var emission = lavaPs.emission;
        emission.rateOverTime = 0f;
        lavaPs.Play();

        // Ramp emission from 0 -> 200 with a quadratic curve (slow start, fast flood)
        float elapsed = 0f;
        while (elapsed < lavaFloodDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lavaFloodDuration);
            emission.rateOverTime = Mathf.Lerp(0f, 200f, t * t);
            yield return null;
        }

        // Kill fire now that lava has covered everything
        CleanUpFire();

        // Let lava linger then clean up
        Destroy(lavaPs.gameObject, 8f);
    }

    void CleanUpFire()
    {
        foreach (var ps in activeFireParticles)
            if (ps != null) ps.Stop();
        activeFireParticles.Clear();
    }

    IEnumerator GlowPathBlocks(Color targetGlow)
    {
        foreach (Vector2Int cell in winPath)
        {
            if (!GridManager.Instance.TryGetCell(cell, out GridCell data)) continue;
            if (data.placedObject == null) continue;
            StartCoroutine(RampEmission(data.placedObject, Color.black, targetGlow, glowRampDuration));
            yield return new WaitForSeconds(glowStaggerDelay);
        }
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
            Vector3 scatter = new Vector3(
                Random.Range(-0.4f, 0.4f), 0f,
                Random.Range(-0.4f, 0.4f));
            SpawnParticleAt(fireworksPrefab, basePos + scatter, 0f);
            yield return new WaitForSeconds(fireworksBurstInterval);
        }
    }

    void BuildWinPath()
    {
        winPath.Clear();
        Vector2Int startCell = GetStartCell();
        Vector2Int finishCell = GetFinishCell();
        if (startCell == finishCell) return;

        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new HashSet<Vector2Int> { startCell };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(startCell);
        bool reached = false;

        while (queue.Count > 0 && !reached)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var dir in PathDirections.All)
            {
                Vector2Int neighbour = current + dir;
                if (visited.Contains(neighbour)) continue;
                if (PathChecker.Instance != null && !PathChecker.Instance.AreMutuallyConnected(current, neighbour)) continue;
                if (!GridManager.Instance.TryGetCell(neighbour, out GridCell data)) continue;
                if (data.type != CellType.Path && data.type != CellType.Start && data.type != CellType.Finish) continue;

                parent[neighbour] = current;
                visited.Add(neighbour);
                queue.Enqueue(neighbour);
                if (neighbour == finishCell) { reached = true; break; }
            }
        }

        if (!reached) { Debug.LogWarning("[EndGameAnimator] BuildWinPath: BFS did not reach Finish."); return; }

        Vector2Int step = finishCell;
        while (step != startCell) { winPath.Add(step); step = parent[step]; }
        winPath.Add(startCell);
        winPath.Reverse();
    }

    Vector2Int GetStartCell()
    {
        foreach (var kvp in GridManager.Instance.GetAllCells())
            if (kvp.Value.type == CellType.Start) return kvp.Key;
        return GridManager.Instance.gridMin;
    }

    Vector2Int GetFinishCell()
    {
        foreach (var kvp in GridManager.Instance.GetAllCells())
            if (kvp.Value.type == CellType.Finish) return kvp.Key;
        return GridManager.Instance.gridMax;
    }

    Vector3 GetCellWorldPos(Vector2Int cell) => GridManager.Instance.GridToWorld(cell);

    static void SetEmission(GameObject obj, Color emissionColor)
    {
        if (obj == null) return;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            foreach (Material mat in rend.materials)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", emissionColor);
            }
    }

    static void SetSurfaceColor(GameObject obj, Color color)
    {
        if (obj == null) return;
        foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            foreach (Material mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                mat.color = color;
            }
    }

    static void SpawnParticleAt(ParticleSystem prefab, Vector3 worldPos, float yOffset)
    {
        if (prefab == null) return;
        Vector3 pos = worldPos + new Vector3(0f, yOffset, 0f);
        ParticleSystem ps = Instantiate(prefab, pos, Quaternion.identity);
        ps.Play();
        float destroyDelay = ps.main.duration + ps.main.startLifetime.constantMax + 0.5f;
        Destroy(ps.gameObject, destroyDelay);
    }
}