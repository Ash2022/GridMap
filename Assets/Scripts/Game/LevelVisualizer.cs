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

    

    [Header("Data")]
    [SerializeField] private TextAsset levelJson;

    [Header("Prefabs & Parents")]
    [SerializeField] private GameObject partPrefab;
    [SerializeField] private GameObject stationPrefab;
    [SerializeField] private GameObject trainPrefab;
    [SerializeField] private GameObject cartPrefab;
    [SerializeField] private Transform levelHolder;
    [SerializeField] private Transform dynamicHolder;

    [Header("Frame & Build Settings")]
    [SerializeField] private SpriteRenderer frameRenderer;
    [SerializeField] private float tileDelay = 0.05f;

    //[SerializeField] float frameWidthUnits = 9f;
    //[SerializeField] float frameHeightUnits = 16f;

    [SerializeField] LineRenderer globalPathRenderer;

    public TrainMover trainMover;

    LevelData currLevel;

    //level dynamic params
    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
    Vector2 worldOrigin;
    int gridW;
    int gridH;
    [HideInInspector] private float cellSize;

    public float CellSize { get => cellSize; set => cellSize = value; }
    public GameObject CartPrefab { get => cartPrefab; set => cartPrefab = value; }

    public float MAX_CELL_SIZE = 100;

    

    void Awake()
    {
        Instance = this;
        partsLibrary = JsonConvert.DeserializeObject<List<TrackPart>>(partsJson.text);
    }

    void Start()
    {
        LoadLevel();
        BuildCurrLevel();
    }


    /// <summary>
    /// Call this to (re)build the entire level.
    /// </summary>
    public void LoadLevel()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector2Converter(),
                new Vector2IntConverter(),
                new Vector3Converter()
            },
            Formatting = Formatting.Indented
        };

        

        if (levelJson == null || partPrefab == null || levelHolder == null)
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
    }


    private void BuildCurrLevel()
    {
        StartCoroutine(BuildCoroutine(currLevel));
    }

    private IEnumerator BuildCoroutine(LevelData level)
    {
        // clear out any previously spawned parts
        for (int i = levelHolder.childCount - 1; i >= 0; i--)
            DestroyImmediate(levelHolder.GetChild(i).gameObject);

        // compute grid bounds


        foreach (var inst in level.parts)
            foreach (var cell in inst.occupyingCells)
            {
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }
        gridW = maxX - minX + 1;
        gridH = maxY - minY + 1;
       

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
        worldOrigin = new Vector2(frameMin.x + marginX,
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
            var go = Instantiate(partPrefab, levelHolder);
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
       


        GameManager.Instance.level = currLevel;

        GenerateDynamic();
        
    }


    public void GenerateDynamic()
    {
        ScenarioModel scenarioModel = CloneScenarioModel(currLevel);

        // clear out any previously spawned parts
        for (int i = dynamicHolder.childCount - 1; i >= 0; i--)
            DestroyImmediate(dynamicHolder.GetChild(i).gameObject);

        ClearGlobalPathRenderer();

        foreach (var pt in scenarioModel.points.Where(p => p.type == GamePointType.Station))
        {
            float cellX = pt.gridX - minX + 0.5f;
            float cellY = pt.gridY - minY + 0.5f;
            Vector2 flipped = new Vector2(cellX, gridH - cellY);
            Vector3 worldPos = new Vector3(
                worldOrigin.x + flipped.x * cellSize,
                worldOrigin.y + flipped.y * cellSize,
                0f
            );

            var go = Instantiate(stationPrefab, dynamicHolder);
            go.name = $"Station_{pt.id}";
            go.transform.position = worldPos;

            var stationView = go.GetComponent<StationView>();

            var part = currLevel.parts.FirstOrDefault(p => p.partId == pt.anchor.partId);
            stationView.Initialize(pt, part, cellSize);
        }

        foreach (var p in scenarioModel.points.Where(x => x.type == GamePointType.Train))
        {
            var trainGO = Instantiate(trainPrefab, dynamicHolder);
            trainGO.name = $"Train_{p.id}";

            var trainController = trainGO.GetComponent<TrainController>();
            trainController.Init(p, currLevel, worldOrigin, minX, minY, gridH, cellSize, cartPrefab);
        }

        MirrorManager.Instance?.InitFromLevel(currLevel, cellSize);
    }

    /// <summary>  
    /// Returns the sprite for the given partType, or null if not found.  
    /// </summary>
    public Sprite GetSpriteFor(string partType)
    {
        int idx = partsLibrary.FindIndex(p => p.partName == partType);
        return (idx >= 0 && idx < partSprites.Count) ? partSprites[idx] : null;
    }


    public void DrawGlobalSplinePath(PathModel pathModel,List<Vector3> worldPts)
    {
        

        for (int pi = 0; pi < pathModel.Traversals.Count; pi++)
        {
            var trav = pathModel.Traversals[pi];
            var inst = currLevel.parts.First(p => p.partId == trav.partId);

            // pick sub‑spline index (forward or reverse)
            int splineIndex = -1;
            int groupIndex = -1;
            int pathIndex = -1;

            if (inst.allowedPathsGroup?.Count > 0 && inst.worldSplines != null)
            {
                bool found = false;

                for (int gi = 0; gi < inst.allowedPathsGroup.Count; gi++)
                {
                    var grp = inst.allowedPathsGroup[gi];
                    int idx = grp.allowedPaths.FindIndex(ap =>
                        (ap.entryConnectionId == trav.entryExit && ap.exitConnectionId == trav.exitExit) ||
                        (ap.entryConnectionId == trav.exitExit && ap.exitConnectionId == trav.entryExit)
                    );

                    if (idx >= 0)
                    {
                        if (gi < inst.worldSplines.Count)
                        {
                            groupIndex = gi;
                            pathIndex = idx;
                            splineIndex = gi; // assuming 1:1 between groupIndex and splineIndex
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    if (inst.worldSplines.Count == 1)
                    {
                        splineIndex = 0;
                    }
                    else
                    {
                        continue; // skip drawing this traversal
                    }
                }
            }
            else
            {
                // simple part
                splineIndex = 0;
            }

            var full = inst.worldSplines?[splineIndex] ?? new List<Vector3>();

            bool simple = inst.exits.Count <= 2;
            bool first = pi == 0;
            bool last = pi == pathModel.Traversals.Count - 1;
            float t0, t1;

            if (simple)
            {
                if (!first && !last)
                {
                    t0 = 0f;
                    t1 = 1f;
                }
                else
                {
                    //float te = trav.entryExit < 0 ? 0.5f : GetExitT(inst, trav.entryExit);
                    //float tx = trav.exitExit < 0 ? 0.5f : GetExitT(inst, trav.exitExit);

                    t0 = trav.entryExit < 0 ? 0.5f : GetExitT(inst, trav.entryExit);
                    t1 = trav.exitExit < 0 ? 0.5f : GetExitT(inst, trav.exitExit);

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
                if (last)
                {
                    float tx = trav.exitExit < 0 ? 0.5f : GetExitT(inst, trav.exitExit);

                    t0 = Mathf.Min(0.5f, tx);
                    t1 = Mathf.Max(0.5f, tx);

                    if (Mathf.Approximately(t0, t1))
                    {
                        const float eps = 0.001f;
                        if (t1 + eps <= 1f) t1 += eps;
                        else t0 = Mathf.Max(0f, t0 - eps);
                    }
                }
                else
                {
                    t0 = 0f;
                    t1 = 1f;
                }
            }

            var seg = ExtractSegmentWorld(full, t0, t1);

            // reverse if entry is on the far side
            if (groupIndex >= 0 && pathIndex >= 0)
            {
                var ap = inst.allowedPathsGroup[groupIndex].allowedPaths[pathIndex];
                if (trav.entryExit != ap.entryConnectionId)
                    seg.Reverse();
            }

            foreach (var w in seg)
            {
                if (worldPts.Count == 0 || worldPts[worldPts.Count - 1] != w)
                    worldPts.Add(w);
            }
        }

        globalPathRenderer.positionCount = worldPts.Count;
        globalPathRenderer.SetPositions(worldPts.ToArray());
    }

    private void ClearGlobalPathRenderer()
    {
        globalPathRenderer.positionCount = 0;
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

    public List<Vector3> ExtractWorldPointsFromPath(PathModel pathModel)
    {
        var pts = new List<Vector3>();
        DrawGlobalSplinePath(pathModel, pts);
        return pts;
    }


    public static ScenarioModel CloneScenarioModel(LevelData source)
    {
        var clone = new ScenarioModel();

        foreach (var point in source.gameData.points)
        {
            var newPoint = new GamePoint(
                point.part,
                point.gridX,
                point.gridY,
                point.type,
                point.colorIndex,
                point.anchor
            );

            newPoint.direction = point.direction;

            if (point.waitingPeople != null)
                newPoint.waitingPeople = new List<int>(point.waitingPeople);

            if (point.initialCarts != null)
                newPoint.initialCarts = new List<int>(point.initialCarts);

            clone.points.Add(newPoint);
        }

        return clone;
    }
}

