using System.Collections.Generic;
using UnityEngine;
using RailSimCore;

public sealed class MirrorManager : MonoBehaviour
{
    public static MirrorManager Instance { get; private set; }

    [SerializeField] bool enabledInPlay = true;   // toggle at runtime
    [SerializeField] float allowedTol = 1e-4f;    // meters
    [SerializeField] float hitTolMult = 3f;       // × SimTuning.Eps(cellSize)

    private SimController sim = new SimController();
    private readonly Dictionary<TrainController, int> map = new Dictionary<TrainController, int>();
    private readonly Dictionary<int, int> mirrorToGame = new();  // mirrorId -> gameTrainId

    [SerializeField] bool verboseParity = true;  // turn on in Inspector

    private float cellSize = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void InitFromLevel(LevelData level, float cellSizeMeters)
    {
        if (!enabledInPlay) return;
        this.cellSize = Mathf.Max(1e-6f, cellSizeMeters);
        sim.Reset();
        sim.BuildTrackDtoFromWorld(level); // uses PlacedPartInstance.worldSplines
    }

    public void RegisterTrain(TrainController tc, Vector3 headPos, Vector3 headForward,
                              IList<float> cartOffsets, float tapeSeedLen, float safetyGap = 0f)
    {
        if (!enabledInPlay) return;
        
        var spec = new SpawnSpec
        {
            Path = null,            // first leg will be provided at StartLeg
            HeadPos = headPos,
            HeadForward = headForward,
            CartOffsets = new List<float>(cartOffsets ?? new List<float>()),
            TapeSeedLen = tapeSeedLen,
            CellSizeHint = cellSize,
            SafetyGap = safetyGap
        };

        int id = sim.Mirror_SpawnTrain(spec);

        map[tc] = id;
        mirrorToGame[id] = tc.TrainId;

        map[tc] = id;
    }

    // Reverse lookup via the existing map (small N, linear is fine)
    private int MirrorToGameId(int mirrorId)
    {
        foreach (var kv in map)
            if (kv.Value == mirrorId)
                return kv.Key.TrainId;   // game-side ID
        return 0;
    }

    public void StartLeg(TrainController tc, IList<Vector3> worldPoints)
    {
        if (!enabledInPlay) return;
        if (!map.TryGetValue(tc, out var id)) return;
        sim.Mirror_StartLeg(id, worldPoints);
    }

    public AdvanceResult Preview(TrainController tc, float wantMeters)
    {
        if (!enabledInPlay) return default;
        if (!map.TryGetValue(tc, out var id)) return default;

        // other trains (mirror ids)
        var others = new List<int>();
        var all = GameManager.Instance.trains;
        for (int i = 0; i < all.Count; i++)
        {
            var o = all[i];
            if (o == null || o == tc) continue;
            if (map.TryGetValue(o, out var oid)) others.Add(oid);
        }
        return sim.Mirror_PreviewAdvance(id, wantMeters, others);
    }

    public void Commit(TrainController tc, float allowed, out Vector3 headPos, out Vector3 headTan)
    {
        headPos = default; headTan = default;
        if (!enabledInPlay) return;
        if (!map.TryGetValue(tc, out var id)) return;
        sim.Mirror_CommitAdvance(id, allowed, out headPos, out headTan);
    }

    public void CompareTickResults(string tag, AdvanceResult gameRes, AdvanceResult mirrorRes)
    {
        if (!enabledInPlay) return;

        // --- existing mismatch checks (keep) ---
        float diff = Mathf.Abs(gameRes.Allowed - mirrorRes.Allowed);
        if (diff > allowedTol)
            Debug.LogWarning($"[Mirror] {tag} allowed mismatch  game={gameRes.Allowed:F6}  mirror={mirrorRes.Allowed:F6}  Δ={diff:F6}");

        if (gameRes.Kind != mirrorRes.Kind)
            Debug.LogWarning($"[Mirror] {tag} reason mismatch  game={gameRes.Kind}  mirror={mirrorRes.Kind}");

        if (gameRes.Kind == AdvanceResultKind.Blocked && mirrorRes.Kind == AdvanceResultKind.Blocked)
        {
            int mirrorBlockerGameId = MirrorToGameId(mirrorRes.BlockerId);
            if (gameRes.BlockerId != mirrorBlockerGameId)
                Debug.LogWarning($"[Mirror] {tag} blocker mismatch  game={gameRes.BlockerId}  mirror(gameId)={mirrorBlockerGameId}");

            float eps = SimTuning.Eps(cellSize) * hitTolMult;
            float hitDelta = Vector3.Distance(gameRes.HitPos, mirrorRes.HitPos);
            if (hitDelta > eps)
                Debug.LogWarning($"[Mirror] {tag} hitPos mismatch  |Δ|={hitDelta:F6} > {eps:F6}");
            else if (verboseParity)
                Debug.Log($"[Mirror] {tag} OK Blocked by T{mirrorBlockerGameId}  Δallowed={diff:F6}  Δhit={hitDelta:F6}");
        }
        else if (gameRes.Kind == AdvanceResultKind.EndOfPath && mirrorRes.Kind == AdvanceResultKind.EndOfPath && verboseParity)
        {
            Debug.Log($"[Mirror] {tag} OK Arrived  Δallowed={diff:F6}");
        }
    }
}
