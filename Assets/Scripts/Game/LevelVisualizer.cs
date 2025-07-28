using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;

public class LevelVisualizer : MonoBehaviour
{
    public static LevelVisualizer Instance { get; private set; }

    [SerializeField] private TextAsset partsJson;
    private List<TrackPart> partsLibrary;
    [SerializeField] public List<Sprite> partSprites;  // must match partsLibrary order

    [HideInInspector] private float cellSize;

    [Header("Data")]
    [SerializeField] private TextAsset levelJson;

    [Header("Prefabs & Parents")]
    [SerializeField] private GameObject partPrefab;
    [SerializeField] private GameObject stationPrefab;
    [SerializeField] private GameObject trainPrefab;
    [SerializeField] private GameObject cartPrefab;
    [SerializeField] private Transform mainHolder;

    [Header("Frame & Build Settings")]
    [SerializeField] private SpriteRenderer frameRenderer;
    [SerializeField] private float tileDelay = 0.05f;

    //[SerializeField] float frameWidthUnits = 9f;
    //[SerializeField] float frameHeightUnits = 16f;

    [SerializeField] LineRenderer globalPathRenderer;

    LevelData currLevel;

    public float CellSize { get => cellSize; set => cellSize = value; }
    public float MAX_CELL_SIZE = 100;

    void Awake()
    {
        Instance = this;
        partsLibrary = JsonConvert.DeserializeObject<List<TrackPart>>(partsJson.text);
    }

    void Start()
    {
        Build();
    }


    /// <summary>
    /// Call this to (re)build the entire level.
    /// </summary>
    public void Build()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new Vector2Converter() },
            Formatting = Formatting.Indented
        };

        // clear out any previously spawned parts
        for (int i = mainHolder.childCount - 1; i >= 0; i--)
            DestroyImmediate(mainHolder.GetChild(i).gameObject);

        if (levelJson == null || partPrefab == null || mainHolder == null)
        {
            Debug.LogError("LevelVisualizer: missing references.");
            return;
        }

        
        try
        {
            currLevel = JsonConvert.DeserializeObject<LevelData>(levelJson.text, settings);
        }
        catch
        {
            Debug.LogError("LevelVisualizer: failed to parse LevelData JSON.");
            return;
        }

        if (currLevel.parts == null || currLevel.parts.Count == 0)
        {
            Debug.LogWarning("LevelVisualizer: no parts in level.");
            return;
        }

        StartCoroutine(BuildCoroutine(currLevel));
    }

    private IEnumerator BuildCoroutine(LevelData level)
    {
        // compute grid bounds

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var inst in level.parts)
            foreach (var cell in inst.occupyingCells)
            {
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }
        int gridW = maxX - minX + 1;
        int gridH = maxY - minY + 1;
        /*
        Vector2 worldOrigin;
        {
            // use fixed logical frame size
            float sizeX2 = frameWidthUnits / gridW;
            float sizeY2 = frameHeightUnits / gridH;

            // pick the larger so we fill (and potentially overflow) one axis
            cellSize = Mathf.Max(sizeX2, sizeY2);

            cellSize = Mathf.Min(MAX_CELL_SIZE, cellSize);  

            // compute grid's half‑size in world units
            Vector2 halfGrid = new Vector2(gridW, gridH) * cellSize * 0.5f;

            // assume the frame's center == this.transform.position
            Vector2 frameCenter = (Vector2)transform.position;

            // origin is bottom‑left of grid: center minus halfGrid, plus half a cell
            worldOrigin = frameCenter - halfGrid + Vector2.one * (cellSize * 0.5f);
        }
        */

        // determine cellSize and worldOrigin so that the grid is centered in the frame
        Bounds fb = frameRenderer.bounds;
        float frameW = fb.size.x;
        float frameH = fb.size.y;

        // how big each cell would be to exactly fill width or height
        float sizeX = frameW / gridW;
        float sizeY = frameH / gridH;

        // pick the *smaller* so that the entire grid fits inside the frame
        cellSize = Mathf.Min(sizeX, sizeY, MAX_CELL_SIZE);

        Debug.Log("CellSize: " + cellSize);

        // now compute the *actual* size the grid will occupy
        float gridWorldW = cellSize * gridW;
        float gridWorldH = cellSize * gridH;

        // find the bottom‑left corner of the grid inside the frame
        Vector3 frameMin = fb.min; // bottom‑left corner of the frame in world coords
                                   // inset so that the grid is centered: we leave half of (frameSize − gridSize) as margin on each side
        float marginX = (frameW - gridWorldW) * 0.5f;
        float marginY = (frameH - gridWorldH) * 0.5f;

        // worldOrigin is the world position of grid cell (0,0)
        Vector2 worldOrigin = new Vector2(frameMin.x + marginX,
                                  frameMin.y + marginY);

        foreach (var inst in level.parts)
        {
            // 1) find the bounding box of the occupied cells
            int minCX = int.MaxValue, minCY = int.MaxValue;
            int maxCX = int.MinValue, maxCY = int.MinValue;
            foreach (var c in inst.occupyingCells)
            {
                minCX = Mathf.Min(minCX, c.x);
                minCY = Mathf.Min(minCY, c.y);
                maxCX = Mathf.Max(maxCX, c.x);
                maxCY = Mathf.Max(maxCY, c.y);
            }

            // 2) compute the true geometric center of that box (cells are inclusive)
            //    +1 so that, e.g., min=1,max=2 → 2 cells wide, centre at (1+2+1)/2 = 2
            float centerX = (minCX + maxCX + 1) * 0.5f - minX;
            float centerY = (minCY + maxCY + 1) * 0.5f - minY;

            // 3) flip Y (so world (0,0) is bottom‑left of grid)
            Vector2 flipped = new Vector2(centerX, gridH - centerY);

            // 4) convert to world coords
            Vector3 pos = new Vector3(
                worldOrigin.x + flipped.x * cellSize,
                worldOrigin.y + flipped.y * cellSize,
                0f
            );

            // spawn & orient
            var go = Instantiate(partPrefab, mainHolder);
            go.name = inst.partId;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, -inst.rotation);

            // copy spline templates into the instance for the view
            TrackPart trackPart = partsLibrary.Find(x => x.partName == inst.partType);
            inst.splines = trackPart.splineTemplates;

            // hand off to view
            if (go.TryGetComponent<TrackPartView>(out var view))
                view.Setup(inst);

            yield return new WaitForSeconds(tileDelay);
        }

        foreach (var pt in level.gameData.points.Where(p => p.type == GamePointType.Station))
        {
            // 1) translate grid coords into [0..W)×[0..H) + 0.5 to center in cell
            float cellX = pt.gridX - minX + 0.5f;
            float cellY = pt.gridY - minY + 0.5f;

            // 2) flip Y so (0,0) is bottom‐left
            Vector2 flipped = new Vector2(cellX, gridH - cellY);

            // 3) to world‐space
            Vector3 worldPos = new Vector3(
                worldOrigin.x + flipped.x * cellSize,
                worldOrigin.y + flipped.y * cellSize,
                0f
            );

            // 4) instantiate
            var go = Instantiate(stationPrefab, mainHolder);
            go.name = $"Station_{pt.id}";
            go.transform.position = worldPos;
            go.GetComponent<StationView>().Initialize(pt);
        }

        foreach (var p in level.gameData.points.Where(x => x.type == GamePointType.Train))
        {
            // 1) figure out WHICH cell to snap to:
            Vector2 worldCell;
            if (p.anchor.exitPin >= 0)
            {
                var inst = p.part;
                worldCell = new Vector2(
                    inst.exits[p.anchor.exitPin].worldCell.x,
                    inst.exits[p.anchor.exitPin].worldCell.y
                );
            }
            else
            {
                worldCell = new Vector2(p.gridX, p.gridY);
            }

            // 2) convert that worldCell into world‐space exactly like stations:
            float cellX = worldCell.x - minX + 0.5f;
            float cellY = worldCell.y - minY + 0.5f;
            Vector2 flipped = new Vector2(cellX, gridH - cellY);
            // this is where the TRAIN HEAD should sit:
            Vector3 trainPos = new Vector3(
                worldOrigin.x + flipped.x * cellSize,
                worldOrigin.y + flipped.y * cellSize,
                0f
            );

            // 2.5) compute forward/backward in world‐space
            Vector3 forward = p.direction switch
            {
                TrainDir.Up => Vector3.up,
                TrainDir.Right => Vector3.right,
                TrainDir.Down => Vector3.down,
                TrainDir.Left => Vector3.left,
                _ => Vector3.up
            };
            Vector3 backward = -forward;

            // 2.6) Center the head on the point by shifting it back by half a cellSize:
            Vector3 centerPos = trainPos;// - forward * (cellSize * 0.5f);

            // 3) instantiate & rotate the train at centerPos:
            var trainGO = Instantiate(trainPrefab, mainHolder);
            trainGO.name = $"Train_{p.id}";
            trainGO.transform.position = centerPos;
            float angleZ = p.direction switch
            {
                TrainDir.Up => 270f,
                TrainDir.Right => 180f,
                TrainDir.Down => 90f,
                TrainDir.Left => 0f,
                _ => 0f
            };
            trainGO.transform.rotation = Quaternion.Euler(0f, 0f, angleZ);

            float cartSize = cellSize / 3f;
            float gap = cellSize / 10f;
            float headBack = cellSize * 0.5f;
            float firstOffset = headBack + gap + cartSize * 0.5f;

            for (int j = 0; j < p.initialCarts.Count; j++)
            {
                var cartGO = Instantiate(cartPrefab, mainHolder);
                cartGO.name = $"Train_{p.id}_Cart_{j + 1}";

                float offset = firstOffset + (cartSize + gap) * j;
                cartGO.transform.position = centerPos + backward * offset;
                cartGO.transform.rotation = trainGO.transform.rotation;
            }
        }
        //DrawGlobalSplinePath(level.parts);

        GameManager.Instance.level = level;
    }

    /// <summary>  
    /// Returns the sprite for the given partType, or null if not found.  
    /// </summary>
    public Sprite GetSpriteFor(string partType)
    {
        int idx = partsLibrary.FindIndex(p => p.partName == partType);
        return (idx >= 0 && idx < partSprites.Count) ? partSprites[idx] : null;
    }
    

    public void DrawGlobalSplinePath(PathModel pathModel)
    {
        var worldPts = new List<Vector3>();

        for (int pi = 0; pi < pathModel.Traversals.Count; pi++)
        {
            var trav = pathModel.Traversals[pi];
            var inst = currLevel.parts.First(p => p.partId == trav.partId);

            // pick sub‑spline index (forward or reverse)
            int splineIndex;
            if (inst.allowedPathsGroup?.Count > 0 && inst.worldSplines != null)
            {
                var grp = inst.allowedPathsGroup[0];
                int idx = grp.allowedPaths.FindIndex(ap =>
                    (ap.entryConnectionId == trav.entryExit && ap.exitConnectionId == trav.exitExit) ||
                    (ap.entryConnectionId == trav.exitExit && ap.exitConnectionId == trav.entryExit)
                );

                if (idx >= 0 && idx < inst.worldSplines.Count)
                {
                    splineIndex = idx;
                }
                else if (inst.worldSplines.Count == 1)
                {
                    // only one curve to choose from
                    splineIndex = 0;
                }
                else
                {
                    // multi‐exit part and no matching allowedPath → skip drawing this traversal
                    continue;
                }
            }
            else
            {
                // no allowedPathsGroup → must be a simple part
                splineIndex = 0;
            }

            // grab world‑space spline
            var full = inst.worldSplines?[splineIndex] ?? new List<Vector3>();

            // determine tStart/tEnd exactly like editor
            bool simple = inst.exits.Count <= 2;
            bool first = pi == 0;
            bool last = pi == pathModel.Traversals.Count - 1;
            float t0, t1;

            if (simple)
            {
                if (!first && !last)
                {
                    // fully draw any simple part in the middle
                    t0 = 0f;
                    t1 = 1f;
                }
                else
                {
                    // first or last simple part: clamp one side to 0.5, the other to its exit T
                    float te = trav.entryExit < 0
                        ? 0.5f
                        : GetExitT(inst, trav.entryExit);
                    float tx = trav.exitExit < 0
                        ? 0.5f
                        : GetExitT(inst, trav.exitExit);

                    t0 = Mathf.Min(te, tx);
                    t1 = Mathf.Max(te, tx);

                    // avoid t0 == t1 (zero‑length)
                    if (Mathf.Approximately(t0, t1))
                    {
                        const float eps = 0.001f;
                        if (t1 + eps <= 1f) t1 += eps;
                        else t0 = Mathf.Max(0f, t0 - eps);
                    }
                }
            }
            else
            {
                // multi‐exit parts
                if (last)
                {
                    // final part: clamp one end at 0.5, the other at its exit T
                    float tx = trav.exitExit < 0 ? 0.5f : GetExitT(inst, trav.exitExit);

                    t0 = Mathf.Min(0.5f, tx);
                    t1 = Mathf.Max(0.5f, tx);

                    // avoid zero‑length
                    if (Mathf.Approximately(t0, t1))
                    {
                        const float eps = 0.001f;
                        if (t1 + eps <= 1f) t1 += eps;
                        else t0 = Mathf.Max(0f, t0 - eps);
                    }
                }
                else
                {
                    // middle multi-exit parts are full
                    t0 = 0f;
                    t1 = 1f;
                }
            }

            // extract that segment
            var seg = ExtractSegmentWorld(full, t0, t1);

            // if we came in on the “far” end, reverse the segment so entry→exit is forwards
            if (inst.allowedPathsGroup?.Count > 0)
            {
                var ap = inst.allowedPathsGroup[0].allowedPaths[splineIndex];
                if (trav.entryExit != ap.entryConnectionId)
                    seg.Reverse();
            }

            // append, skipping only exact duplicate at join
            foreach (var w in seg)
            {
                if (worldPts.Count == 0 || worldPts[worldPts.Count - 1] != w)
                    worldPts.Add(w);
            }


        }

        globalPathRenderer.positionCount = worldPts.Count;
        globalPathRenderer.SetPositions(worldPts.ToArray());
    }

    // maps exitIndex to normalized t along its simple spline
    static float GetExitT(PlacedPartInstance part, int exitIndex)
    {
        var dir = part.exits.First(e => e.exitIndex == exitIndex).direction;
        // Up/Right → t=0; Down/Left → t=1
        return (dir == 2 || dir == 3) ? 1f : 0f;
    }

    // identical to your editor’s ExtractSegmentScreen but for Vector3 lists
    static List<Vector3> ExtractSegmentWorld(List<Vector3> pts, float tStart, float tEnd)
    {
        int n = pts.Count;
        if (n < 2) return new List<Vector3>(pts);

        // build cumulative lengths
        var cum = new float[n];
        float total = 0f;
        for (int i = 1; i < n; i++)
        {
            total += Vector3.Distance(pts[i - 1], pts[i]);
            cum[i] = total;
        }
        if (total <= 0f) return new List<Vector3> { pts[0], pts[n - 1] };

        float sLen = tStart * total;
        float eLen = tEnd * total;

        Vector3 PointAt(float d)
        {
            for (int i = 1; i < n; i++)
            {
                if (d <= cum[i])
                {
                    float u = Mathf.InverseLerp(cum[i - 1], cum[i], d);
                    return Vector3.Lerp(pts[i - 1], pts[i], u);
                }
            }
            return pts[n - 1];
        }

        var outPts = new List<Vector3>();
        outPts.Add(PointAt(sLen));
        for (int i = 1; i < n - 1; i++)
            if (cum[i] > sLen && cum[i] < eLen)
                outPts.Add(pts[i]);
        outPts.Add(PointAt(eLen));
        return outPts;
    }

}

