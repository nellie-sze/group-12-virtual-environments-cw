using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;

public class GridSystem : MonoBehaviour
{
    public enum ToolMode
    {
        Straight,
        Corner
    }

    public enum PathTileType
    {
        Start,
        Finish,
        Straight,
        Corner
    }

    public enum Direction
    {
        Up,
        Right,
        Down,
        Left
    }

    public class PathTileData
    {
        public PathTileType tileType;
        public int rotationY;

        public PathTileData(PathTileType tileType, int rotationY)
        {
            this.tileType = tileType;
            this.rotationY = rotationY;
        }
    }

    [Header("Placement")]
    public float gridSize = 1f;

    [Header("Tool")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable shovelGrab;
    private bool isShovelHeld = false;

    [Header("Mode")]
    public ToolMode currentMode = ToolMode.Straight;

    [Header("Mode Colours")]
    public Color straightGhostColor = new Color(0f, 0.6f, 1f, 0.5f);
    public Color cornerGhostColor = new Color(0f, 1f, 0.3f, 0.5f);
    public Color invalidGhostColor = new Color(1f, 0f, 0f, 0.5f);

    public Color straightPlacedColor = new Color(0f, 0.6f, 1f, 1f);
    public Color cornerPlacedColor = new Color(0f, 1f, 0.3f, 1f);

    [Header("Prefabs")]
    public GameObject straightPrefab;
    public GameObject cornerPrefab;
    public GameObject startPrefab;
    public GameObject finishPrefab;

    private GameObject ghostObject;
    private HashSet<Vector3> occupiedPositions = new HashSet<Vector3>();

    // Rotation for player-placed block
    private int currentRotationY = 0;

    // Logical tile map for pathfinding
    private Dictionary<Vector3, PathTileData> placedTiles = new Dictionary<Vector3, PathTileData>();

    private Vector3 startPosition;
    private Vector3 finishPosition;

    void Start()
    {
        Debug.Log("GridSystem Start");

        if (straightPrefab == null)
        {
            Debug.LogError("GridSystem: straightPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (cornerPrefab == null)
        {
            Debug.LogError("GridSystem: cornerPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (startPrefab == null)
        {
            Debug.LogError("GridSystem: startPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (finishPrefab == null)
        {
            Debug.LogError("GridSystem: finishPrefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.AddListener(OnShovelGrab);
            shovelGrab.selectExited.AddListener(OnShovelRelease);
            shovelGrab.activated.AddListener(OnGrabActivated);
        }

        SpawnStartAndFinish();

        CreateGhostObject();
        ghostObject.SetActive(false);
        UpdateGhostVisual();
        
    }

    void OnDestroy()
    {
        if (shovelGrab != null)
        {
            shovelGrab.selectEntered.RemoveListener(OnShovelGrab);
            shovelGrab.selectExited.RemoveListener(OnShovelRelease);
            shovelGrab.activated.RemoveListener(OnGrabActivated);
        }
    }

    void Update()
    {
        if (!isShovelHeld)
            return;

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            SwitchMode();
        }

        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
        {
            Debug.Log("Placing object");
            PlaceObject();
        }

        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            RotateGhost();
        }

        UpdateGhostPosition();
    }

    void CreateGhostObject()
    {
        GameObject prefab = currentMode == ToolMode.Straight
            ? straightPrefab
            : cornerPrefab;

        ghostObject = Instantiate(prefab);
        ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

        Collider rootCollider = ghostObject.GetComponent<Collider>();
        if (rootCollider != null)
            rootCollider.enabled = false;

        foreach (Collider col in ghostObject.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material mat = renderer.material;
            Color color = mat.color;
            color.a = 0.5f;
            mat.color = color;

            mat.SetFloat("_Mode", 2);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            //mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, 0.5f);
        }
    }
    Vector3 SnapToGrid(Vector3 worldPos, GameObject prefab = null)
    {
        return new Vector3(
            Mathf.Round(worldPos.x / gridSize) * gridSize,
            Mathf.Round(worldPos.y / gridSize) * gridSize,
            Mathf.Round(worldPos.z / gridSize) * gridSize
        );
    }

    void SpawnStartAndFinish()
    {
        Renderer r = GetComponent<Renderer>();
        Bounds bounds = r.bounds;

        int minX = Mathf.RoundToInt(bounds.min.x / gridSize) + 2;
        int maxX = Mathf.RoundToInt(bounds.max.x / gridSize) - 2;
        int minZ = Mathf.RoundToInt(bounds.min.z / gridSize) + 1;
        int maxZ = Mathf.RoundToInt(bounds.max.z / gridSize) - 1;

        int side = UnityEngine.Random.Range(0, 4);

        switch (side)
        {
            // LEFT - RIGHT
            case 0:
            {
                int startZ = UnityEngine.Random.Range(minZ, maxZ + 1);
                int finishZ = UnityEngine.Random.Range(minZ, maxZ + 1);

                startPosition = SnapToGrid(new Vector3(minX * gridSize, transform.position.y, startZ * gridSize));
                finishPosition = SnapToGrid(new Vector3(maxX * gridSize, transform.position.y, finishZ * gridSize));

                // Start points RIGHT
                Instantiate(startPrefab, startPosition, Quaternion.Euler(0f, 0f, 0f));
                placedTiles[startPosition] = new PathTileData(PathTileType.Start, 0);

                // Finish expects LEFT
                Instantiate(finishPrefab, finishPosition, Quaternion.Euler(0f, 180f, 0f));
                placedTiles[finishPosition] = new PathTileData(PathTileType.Finish, 180);

                break;
            }

            // RIGHT - LEFT
            case 1:
            {
                int startZ = UnityEngine.Random.Range(minZ, maxZ + 1);
                int finishZ = UnityEngine.Random.Range(minZ, maxZ + 1);

                startPosition = SnapToGrid(new Vector3(maxX * gridSize, transform.position.y, startZ * gridSize));
                finishPosition = SnapToGrid(new Vector3(minX * gridSize, transform.position.y, finishZ * gridSize));

                // Start points LEFT
                Instantiate(startPrefab, startPosition, Quaternion.Euler(0f, 180f, 0f));
                placedTiles[startPosition] = new PathTileData(PathTileType.Start, 180);

                // Finish expects RIGHT
                Instantiate(finishPrefab, finishPosition, Quaternion.Euler(0f, 0f, 0f));
                placedTiles[finishPosition] = new PathTileData(PathTileType.Finish, 0);

                break;
            }

            // BOTTOM - TOP
            case 2:
            {
                int startX = UnityEngine.Random.Range(minX, maxX + 1);
                int finishX = UnityEngine.Random.Range(minX, maxX + 1);

                startPosition = SnapToGrid(new Vector3(startX * gridSize, transform.position.y, minZ * gridSize));
                finishPosition = SnapToGrid(new Vector3(finishX * gridSize, transform.position.y, maxZ * gridSize));

                // Start points UP
                Instantiate(startPrefab, startPosition, Quaternion.Euler(0f, 270f, 0f));
                placedTiles[startPosition] = new PathTileData(PathTileType.Start, 270);

                // Finish expects DOWN
                Instantiate(finishPrefab, finishPosition, Quaternion.Euler(0f, 90f, 0f));
                placedTiles[finishPosition] = new PathTileData(PathTileType.Finish, 90);

                break;
            }

            // TOP - BOTTOM
            case 3:
            {
                int startX = UnityEngine.Random.Range(minX, maxX + 1);
                int finishX = UnityEngine.Random.Range(minX, maxX + 1);

                startPosition = SnapToGrid(new Vector3(startX * gridSize, transform.position.y, maxZ * gridSize));
                finishPosition = SnapToGrid(new Vector3(finishX * gridSize, transform.position.y, minZ * gridSize));

                // Start points DOWN
                Instantiate(startPrefab, startPosition, Quaternion.Euler(0f, 90f, 0f));
                placedTiles[startPosition] = new PathTileData(PathTileType.Start, 90);

                // Finish expects UP
                Instantiate(finishPrefab, finishPosition, Quaternion.Euler(0f, 270f, 0f));
                placedTiles[finishPosition] = new PathTileData(PathTileType.Finish, 270);

                break;
            }
        }

        occupiedPositions.Add(startPosition);
        occupiedPositions.Add(finishPosition);

        Debug.Log($"Start spawned at {startPosition}, Finish spawned at {finishPosition}");
    }

    bool IsWithinGrid(Vector3 pos)
    {
        Renderer r = GetComponent<Renderer>();
        Bounds bounds = r.bounds;

        return pos.x >= bounds.min.x &&
               pos.x <= bounds.max.x &&
               pos.z >= bounds.min.z &&
               pos.z <= bounds.max.z;
    }

    void UpdateGhostPosition()
    {
        if (Mouse.current == null || Camera.main == null || ghostObject == null)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 point = hit.point;
            float radius = 0.5f;

            if (IsWithinGrid(point) &&
                IsWithinGrid(point + new Vector3(radius, 0f, 0f)) &&
                IsWithinGrid(point + new Vector3(0f, 0f, radius)) &&
                IsWithinGrid(point + new Vector3(-radius, 0f, 0f)) &&
                IsWithinGrid(point + new Vector3(0f, 0f, -radius)))
            {
                // Vector3 snappedPosition = new Vector3(
                //     Mathf.Round(point.x / gridSize) * gridSize,
                //     Mathf.Round(point.y / gridSize) * gridSize, // transform.position.y + 0.15f
                //     Mathf.Round(point.z / gridSize) * gridSize
                // );
                Vector3 snappedPosition = SnapToGrid(point, ghostObject);

                ghostObject.transform.position = snappedPosition;
                ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

                if (CanPlaceGhost())
                    UpdateGhostVisual();
                else
                    SetGhostColor(invalidGhostColor);
            }
        }
    }

    void UpdateGhostVisual()
    {
        if (ghostObject == null)
            return;

        switch (currentMode)
        {
            case ToolMode.Straight:
                SetGhostColor(straightGhostColor);
                break;

            case ToolMode.Corner:
                SetGhostColor(cornerGhostColor);
                break;
        }
    }

    void SetGhostColor(Color color)
    {
        if (ghostObject == null)
            return;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            renderer.material.color = color;
        }
    }

    void PlaceObject()
    {
        if (ghostObject == null)
            return;

        if (!CanPlaceGhost())
        {
            Debug.Log("Cannot place object: not matching indicators.");
            return;
        }

        GameObject prefab = currentMode == ToolMode.Straight
            ? straightPrefab
            : cornerPrefab;
        
        //Vector3 placementPosition = ghostObject.transform.position;
        Vector3 placementPosition = SnapToGrid(ghostObject.transform.position);

        if (occupiedPositions.Contains(placementPosition))
        {
            Debug.Log("Cannot place here: already occupied.");
            return;
        }

        Quaternion placementRotation = Quaternion.Euler(0f, currentRotationY, 0f);
        GameObject placedObject = Instantiate(prefab, placementPosition, placementRotation);
        ApplyPlacedModeColour(placedObject);
        
        List<Vector3> cells = GetCellsForPlacedObject(placedObject);
        foreach (Vector3 cell in cells)
        {
            occupiedPositions.Add(cell);
        }
        Debug.Log("Placed object in mode: " + currentMode);

        PathTileType tileType = currentMode == ToolMode.Straight
            ? PathTileType.Straight
            : PathTileType.Corner;

        PathTileData newTile = new PathTileData(tileType, currentRotationY);
        placedTiles[placementPosition] = newTile;

        List<Direction> exits = GetOpenDirections(newTile);
        Debug.Log($"Placed {tileType} at {placementPosition}. Exits: {string.Join(", ", exits)}");

        CheckForValidPath();
    }

    void ApplyPlacedModeColour(GameObject placedObject)
    {
        Color placedColor = straightPlacedColor;

        switch (currentMode)
        {
            case ToolMode.Straight:
                placedColor = straightPlacedColor;
                break;

            case ToolMode.Corner:
                placedColor = cornerPlacedColor;
                break;
        }

        Renderer[] renderers = placedObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.material.color = placedColor;
        }
    }

    List<Direction> GetOpenDirections(PathTileData tile)
    {
        List<Direction> dirs = new List<Direction>();
        int rot = ((tile.rotationY % 360) + 360) % 360;

        switch (tile.tileType)
        {
            case PathTileType.Straight:
                if (rot == 0 || rot == 180)
                {
                    dirs.Add(Direction.Left);
                    dirs.Add(Direction.Right);
                }
                else if (rot == 90 || rot == 270)
                {
                    dirs.Add(Direction.Up);
                    dirs.Add(Direction.Down);
                }
                break;

            case PathTileType.Corner:
                if (rot == 0)
                {
                    dirs.Add(Direction.Up);
                    dirs.Add(Direction.Left);
                }
                else if (rot == 90)
                {
                    dirs.Add(Direction.Right);
                    dirs.Add(Direction.Up);
                }
                else if (rot == 180)
                {
                    dirs.Add(Direction.Down);
                    dirs.Add(Direction.Right);
                }
                else if (rot == 270)
                {
                    dirs.Add(Direction.Left);
                    dirs.Add(Direction.Down);
                }
                break;

            case PathTileType.Start:
            case PathTileType.Finish:
                if (rot == 0) dirs.Add(Direction.Right);
                else if (rot == 90) dirs.Add(Direction.Down);
                else if (rot == 180) dirs.Add(Direction.Left);
                else if (rot == 270) dirs.Add(Direction.Up);
                break;
        }

        return dirs;
    }

    Vector3 DirectionToOffset(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return new Vector3(0f, 0f, gridSize);
            case Direction.Right: return new Vector3(gridSize, 0f, 0f);
            case Direction.Down: return new Vector3(0f, 0f, -gridSize);
            case Direction.Left: return new Vector3(-gridSize, 0f, 0f);
            default: return Vector3.zero;
        }
    }

    Direction Opposite(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return Direction.Down;
            case Direction.Right: return Direction.Left;
            case Direction.Down: return Direction.Up;
            case Direction.Left: return Direction.Right;
            default: return Direction.Up;
        }
    }

    void CheckForValidPath()
    {
        if (!placedTiles.ContainsKey(startPosition) || !placedTiles.ContainsKey(finishPosition))
            return;

        HashSet<Vector3> visited = new HashSet<Vector3>();
        Queue<Vector3> queue = new Queue<Vector3>();

        queue.Enqueue(startPosition);
        visited.Add(startPosition);

        while (queue.Count > 0)
        {
            Vector3 current = queue.Dequeue();

            if (current == finishPosition)
            {
                Debug.Log("Valid path found from start to finish!");
                return;
            }

            PathTileData currentTile = placedTiles[current];
            List<Direction> openDirs = GetOpenDirections(currentTile);
            Debug.Log($"At {current}");

            foreach (Direction dir in openDirs)
            {
                Vector3 neighborPos = SnapToGrid(current + DirectionToOffset(dir));
                Debug.Log($"Checking neighbor at {neighborPos} in direction {dir}");

                if (!placedTiles.ContainsKey(neighborPos))
                    continue;

                PathTileData neighborTile = placedTiles[neighborPos];
                List<Direction> neighborDirs = GetOpenDirections(neighborTile);

                if (!neighborDirs.Contains(Opposite(dir)))
                    continue;

                if (!visited.Contains(neighborPos))
                {
                    visited.Add(neighborPos);
                    queue.Enqueue(neighborPos);
                }
            }
        }

        Debug.Log("No valid path yet.");
    }

    List<Vector3> GetCellsForPlacedObject(GameObject placedObject)
    {
        List<Vector3> cells = new List<Vector3>();

        Renderer[] renderers = placedObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;

            // Vector3 snapped = new Vector3(
            //     Mathf.Round(p.x / gridSize) * gridSize,
            //     Mathf.Round(p.y / gridSize) * gridSize,
            //     Mathf.Round(p.z / gridSize) * gridSize
            // );
            Vector3 snapped = SnapToGrid(p);

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        if (cells.Count == 0)
        {
            // Vector3 rootPos = new Vector3(
            //     Mathf.Round(placedObject.transform.position.x / gridSize) * gridSize,
            //     Mathf.Round(placedObject.transform.position.y / gridSize) * gridSize,
            //     Mathf.Round(placedObject.transform.position.z / gridSize) * gridSize
            // );
            Vector3 rootPos = SnapToGrid(placedObject.transform.position);
            cells.Add(rootPos);
        }

        return cells;
    }

    // Gets every grid cell occupied by the current ghost
    List<Vector3> GetGhostOccupiedCells()
    {
        List<Vector3> cells = new List<Vector3>();

        if (ghostObject == null)
            return cells;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Vector3 p = renderer.transform.position;

            Vector3 snapped = new Vector3(
                Mathf.Round(p.x / gridSize) * gridSize,
                Mathf.Round(p.y / gridSize) * gridSize,
                Mathf.Round(p.z / gridSize) * gridSize
            );

            if (!cells.Contains(snapped))
                cells.Add(snapped);
        }

        // Fallback if prefab has no child renderers except root
        if (cells.Count == 0)
        {
            Vector3 rootPos = new Vector3(
                Mathf.Round(ghostObject.transform.position.x / gridSize) * gridSize,
                Mathf.Round(ghostObject.transform.position.y / gridSize) * gridSize,
                Mathf.Round(ghostObject.transform.position.z / gridSize) * gridSize
            );
            cells.Add(rootPos);
        }

        return cells;
    }

//     bool HasAnyPlayerTiles()
// {
//     foreach (var tile in placedTiles.Values)
//     {
//         if (tile.tileType == PathTileType.Straight || tile.tileType == PathTileType.Corner)
//             return true;
//     }
//     return false;
// }
    bool HasValidIndicatorConnection(Vector3 position, PathTileData candidateTile)
    {
        List<Direction> candidateOpenDirs = GetOpenDirections(candidateTile);

        foreach (Direction dir in candidateOpenDirs)
        {
            Vector3 neighborPos = position + DirectionToOffset(dir);

            if (!placedTiles.ContainsKey(neighborPos))
                continue;

            PathTileData neighborTile = placedTiles[neighborPos];
            List<Direction> neighborOpenDirs = GetOpenDirections(neighborTile);

            if (neighborOpenDirs.Contains(Opposite(dir)))
                return true;
        }

        return false;
    }
//     bool IsValidFirstBlockPlacement(Vector3 position, PathTileData candidateTile)
//     {
//         if (!placedTiles.ContainsKey(startPosition))
//             return false;

//         List<Direction> candidateOpenDirs = GetOpenDirections(candidateTile);
//         PathTileData startTile = placedTiles[startPosition];
//         List<Direction> startOpenDirs = GetOpenDirections(startTile);

//         foreach (Direction dir in candidateOpenDirs)
//         {
//             Vector3 neighborPos = position + DirectionToOffset(dir);

//             if (neighborPos != startPosition)
//                 continue;

//             if (startOpenDirs.Contains(Opposite(dir)))
//                 return true;
//         }

//         return false;
//     }

    bool CanPlaceGhost()
    {
        List<Vector3> cells = GetGhostOccupiedCells();

        foreach (Vector3 cell in cells)
        {
            if (!IsWithinGrid(cell))
                return false;

            if (occupiedPositions.Contains(cell))
                return false;
        }

         // Logical position for the tile root
        Vector3 rootPos = SnapToGrid(ghostObject.transform.position);

        PathTileType tileType = currentMode == ToolMode.Straight
            ? PathTileType.Straight
            : PathTileType.Corner;

        PathTileData candidateTile = new PathTileData(tileType, currentRotationY);

        bool hasAnyPlayerTiles = false;

        foreach (var tile in placedTiles.Values)
        {
            if (tile.tileType == PathTileType.Straight || tile.tileType == PathTileType.Corner)
            {
                hasAnyPlayerTiles = true;
                break;
            }
        }
        // if (!hasAnyPlayerTiles)
        // {
        //     return IsValidFirstBlockPlacement(rootPos, candidateTile);
        // }
        // return HasValidIndicatorConnection(rootPos, candidateTile);

        //If no player tiles yet, allow placement near start/finish
        if (hasAnyPlayerTiles)
        {
            if (!HasValidIndicatorConnection(rootPos, candidateTile))
                return false;
        }
        return true;

    }

    void RotateGhost()
    {
        currentRotationY = (currentRotationY + 90) % 360;

        if (ghostObject != null)
        {
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

            if (CanPlaceGhost())
                UpdateGhostVisual();
            else
                SetGhostColor(invalidGhostColor);
        }

        Debug.Log("Rotated ghost to " + currentRotationY + " degrees");
    }

    void SwitchMode()
    {
        currentMode = currentMode == ToolMode.Straight
            ? ToolMode.Corner
            : ToolMode.Straight;

        FindAnyObjectByType<ToolModeUI>()?.UpdateModeText();

        if (ghostObject != null)
            Destroy(ghostObject);

        CreateGhostObject();
        UpdateGhostPosition();
        ghostObject.SetActive(isShovelHeld);

        Debug.Log("Switched mode to " + currentMode);
    }

    private void OnShovelGrab(SelectEnterEventArgs args)
    {
        isShovelHeld = true;
        Debug.Log("Grid detects shovel held");
        if (ghostObject != null)
        {
            ghostObject.SetActive(true);
            UpdateGhostVisual();
        }
    }

    private void OnShovelRelease(SelectExitEventArgs args)
    {
        isShovelHeld = false;
        Debug.Log("Grid detects shovel dropped");
        if (ghostObject != null)
        {
            ghostObject.SetActive(false);
        }
    }

    private void OnGrabActivated(ActivateEventArgs args)
    {
        Debug.Log("Placing object");
        PlaceObject();
    }

    private void SwitchMode(InputAction.CallbackContext ctx)
    {
        SwitchMode();
    }
}