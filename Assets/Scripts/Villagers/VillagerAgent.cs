using UnityEngine;
using System.Collections;

public class VillagerAgent : MonoBehaviour
{
    public enum VillagerState { Idle, Moving, Held, Dead }

    [Header("References")]
    public BoardGrid grid;

    [Header("Movement")]
    public float moveSpeedCellsPerSecond = 1f;
    public float minWait = 0.8f;
    public float maxWait = 2.0f;

    [Header("Held Behavior")]
    public float heldHoverHeight = 0.01f;

    [Header("Drop Preview")]
    public GameObject dropPreview;
    public float previewThickness = 0.002f;
    public float previewYOffset = 0.002f;

    [Header("State")]
    public Vector2Int cell;
    public VillagerState state = VillagerState.Idle;

    public bool isAuthority = true;

    private Coroutine loop;
    private Coroutine releaseRoutine;
    private Rigidbody rb;
    private Collider cachedCollider;

    private static readonly Vector2Int[] dirs =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cachedCollider = GetComponent<Collider>();
    }

    void Start()
    {
        if (!grid) grid = FindFirstObjectByType<BoardGrid>();
        SnapToCell(cell);
        StartWanderLoop();

        if (dropPreview != null)
            dropPreview.SetActive(false);
    }

    void LateUpdate()
    {
        if (grid == null)
            return;

        if (state == VillagerState.Held)
        {
            ConstrainHeldToSurface();
            UpdateDropPreview();
        }
        else
        {
            HideDropPreview();
        }
    }

    void StartWanderLoop()
    {
        if (loop == null && isAuthority && state != VillagerState.Dead)
            loop = StartCoroutine(WanderLoop());
    }

    IEnumerator WanderLoop()
    {
        while (state != VillagerState.Dead)
        {
            if (!isAuthority || state == VillagerState.Held)
            {
                yield return null;
                continue;
            }

            state = VillagerState.Idle;
            yield return new WaitForSeconds(Random.Range(minWait, maxWait));

            if (state == VillagerState.Held || state == VillagerState.Dead || !isAuthority)
                continue;

            var next = PickNeighborCell();
            if (next != cell)
                yield return MoveToCell(next);
        }

        loop = null;
    }

    Vector2Int PickNeighborCell()
    {
        for (int i = 0; i < 8; i++)
        {
            var d = dirs[Random.Range(0, dirs.Length)];
            var c = cell + d;
            if (grid.InBounds(c))
                return c;
        }
        return cell;
    }

    IEnumerator MoveToCell(Vector2Int target)
    {
        state = VillagerState.Moving;

        Vector3 start = transform.position;
        float hh = GetHalfHeight();
        Vector3 end = grid.CellToWorld(target, hh);

        Vector3 dir = end - start;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.forward = dir.normalized;

        float dist = Vector3.Distance(start, end);
        float duration = dist / (grid.cellSize * Mathf.Max(0.01f, moveSpeedCellsPerSecond));
        float t = 0f;

        while (t < 1f)
        {
            if (state == VillagerState.Held || state == VillagerState.Dead)
                yield break;

            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        cell = target;
        SnapToCell(cell);
        state = VillagerState.Idle;
    }

    public void BeginHold()
    {
        if (state == VillagerState.Dead) return;

        state = VillagerState.Held;

        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }

        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }

        // Avoid warnings with kinematic rigidbodies.
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void EndHold(Vector3 releaseWorldPos)
    {
        if (state == VillagerState.Dead) return;

        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);

        releaseRoutine = StartCoroutine(ReleaseAndResume(releaseWorldPos));
    }

    IEnumerator ReleaseAndResume(Vector3 releaseWorldPos)
    {
        yield return new WaitForFixedUpdate();

        var dropCell = grid.Clamp(grid.WorldToCell(releaseWorldPos));
        cell = dropCell;
        SnapToCell(cell);

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        state = VillagerState.Idle;
        releaseRoutine = null;
        HideDropPreview();
        StartWanderLoop();
    }

    public void SnapToCell(Vector2Int c)
    {
        float hh = cachedCollider != null ? cachedCollider.bounds.extents.y : 0.05f;
        transform.position = grid.CellToWorld(c, hh);
    }

    public void Die()
    {
        state = VillagerState.Dead;
        HideDropPreview();
        gameObject.SetActive(false);
    }

    float GetHalfHeight()
    {
        return cachedCollider != null ? cachedCollider.bounds.extents.y : 0.05f;
    }

    void ConstrainHeldToSurface()
{
    float hh = GetHalfHeight();

    float minY = grid.GetSurfaceTopY() + hh + grid.extraClearance + heldHoverHeight;

    Vector3 p = transform.position;
    p.y = Mathf.Max(p.y, minY);   // allow lifting up, block pushing down into table
    transform.position = p;
    Vector3 e = transform.eulerAngles;
    transform.rotation = Quaternion.Euler(0f, e.y, 0f);
}
    void UpdateDropPreview()
    {
        if (dropPreview == null)
            return;

        Vector2Int previewCell = grid.Clamp(grid.WorldToCell(transform.position));

        Vector3 previewPos = grid.CellToWorld(previewCell, 0f);
        previewPos.y = grid.GetSurfaceTopY() + previewYOffset;

        dropPreview.SetActive(true);
        dropPreview.transform.position = previewPos;
        dropPreview.transform.localScale = new Vector3(grid.cellSize, previewThickness, grid.cellSize);
    }

    void HideDropPreview()
    {
        if (dropPreview != null && dropPreview.activeSelf)
            dropPreview.SetActive(false);
    }
}