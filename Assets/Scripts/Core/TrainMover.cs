using RailSimCore; // <- your pure core namespace
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static RailSimCore.Types;

public class TrainMover : MonoBehaviour
{
    [Header("Motion")]
    public float moveSpeed = 4f;

    [Header("Capacity")]
    public int reservedCartSlots = 20;     // used only as a fallback on first leg
    public bool debugBack = false;

    [Header("Collision (simple)")]
    public bool collisionsEnabled = true;
    public bool collisionDebug = true;
    public float safetyGap = 0.0f;           // meters added behind stationary trains (along tape)
    public float collisionSampleStep = 0.0f; // if 0, defaults to cellSize/8 per leg
    public float collisionEps = 0.0f;        // if 0, defaults to 1e-4 * cellSize per leg

    // Runtime state (adapter)
    private Coroutine moveCoroutine;
    private bool isMoving;
    private readonly SimpleTrainSim sim = new SimpleTrainSim();

    // Geometry/cache for adapter work
    private List<GameObject> carts = new List<GameObject>();
    private readonly List<float> offsets = new List<float>(); // cart center lags behind head center (meters)
    private float cellSize;
    private float cartHalfLen; // ≈ cellSize/6
    private float headHalfLen; // ≈ cellSize/2

    // Debug (gizmos)
    private List<Vector3> dbgMovingSlice, dbgBlockedSlice;

    // ─────────────────────────────────────────────────────────────────────────────
    // PUBLIC API (unchanged signatures)

    /// <summary>
    /// Start moving along a leg; will invoke onArrivedStation once at the end.
    /// </summary>
    public void MoveAlongPath(List<Vector3> worldPoints, List<GameObject> currCarts, float currCellSize, System.Action<MoveCompletion> onCompleted = null)

    {
        if (isMoving) return;
        if (worldPoints == null || worldPoints.Count < 2)
        {
            Debug.LogWarning("TrainMover: Invalid path");
            return;
        }

        // Cache inputs
        carts = currCarts ?? new List<GameObject>();
        cellSize = currCellSize;
        cartHalfLen = SimTuning.CartHalfLen(cellSize);
        headHalfLen = SimTuning.HeadHalfLen(cellSize);

        // Build cart center offsets from current world poses (keep exact visual spacing at start of leg)
        offsets.Clear();
        Vector3 headPos = transform.position;
        Vector3 legFwd = (worldPoints[1] - worldPoints[0]).normalized;
        for (int i = 0; i < carts.Count; i++)
        {
            float off = Vector3.Dot(headPos - carts[i].transform.position, legFwd);
            if (off < 0f) off = 0f;
            offsets.Add(off);
        }

        // Configure core sim for this leg
        float step = (collisionSampleStep > 0f) ? collisionSampleStep : SimTuning.SampleStep(cellSize);
        float eps = (collisionEps > 0f) ? collisionEps : SimTuning.Eps(cellSize);
        sim.Configure(cellSize, sampleStep: step, eps: eps, safetyGap: Mathf.Max(0f, safetyGap));
        sim.LoadLeg(worldPoints);
        sim.SetCartOffsets(offsets);

        // Ensure tape capacity at least tail + small margin
        float gap = SimTuning.Gap(cellSize);
        float tailBehind;
        if (offsets.Count > 0)
        {
            int lastIdx = offsets.Count - 1;
            tailBehind = offsets[lastIdx] + cartHalfLen;
        }
        else
        {
            tailBehind = headHalfLen; // fallback to head size if no carts
        }
        sim.TapeCapacityMeters = tailBehind + gap + SimTuning.TapeMarginMeters;

        // Fallback seeding (in case controller didn't pre-seed at spawn)
        // Reserve a straight prefix behind start, enough for 'reservedCartSlots'
        if (!TryGetPoseAtBackDistance(0.01f, out _, out _)) // tape not seeded yet
        {
            float cartLen = SimTuning.CartLen(cellSize);
            float firstOffset = headHalfLen + gap + cartHalfLen;
            int slots = Mathf.Max(1, reservedCartSlots);
            float reservedBackMeters = firstOffset + (cartLen + gap) * (slots - 1);
            // seed behind the current head pose
            sim.SeedTapePrefixStraight(worldPoints[0], legFwd, reservedBackMeters + SimTuning.TapeMarginMeters);
        }

        MirrorManager.Instance?.StartLeg(GetComponent<TrainController>(), worldPoints);

        moveCoroutine = StartCoroutine(MoveRoutine(onCompleted));
    }

    /// <summary>Return world pose at a given distance behind the head using the (seeded) back tape.</summary>
    public bool TryGetPoseAtBackDistance(float backDistance, out Vector3 pos, out Quaternion rot)
    {
        if (sim.TryGetBackPose(Mathf.Max(0f, backDistance), out pos, out var tan))
        {
            rot = Quaternion.LookRotation(Vector3.forward, tan);
            return true;
        }
        rot = Quaternion.identity;
        return false;
    }

    /// <summary>Controller calls this right after adding a cart so the sim follows it on the next leg.</summary>
    public void AddCartOffset(float newCenterOffset)
    {
        offsets.Add(newCenterOffset);
        sim.SetCartOffsets(offsets);
        // Also expand tape capacity a bit if needed
        float gap = SimTuning.Gap(cellSize);
        sim.TapeCapacityMeters = Mathf.Max(sim.TapeCapacityMeters, newCenterOffset + SimTuning.CartHalfLen(cellSize) + gap + SimTuning.TapeMarginMeters);
    }

    /// <summary>Seed a straight back tape at spawn so a stationary train is collidable before moving.</summary>
    public void SeedTapePrefixStraight(Vector3 headPos, Vector3 forwardWorld, float length, float sampleStep)
    {
        // Ensure sim has matching sampling before seeding
        float assumedCell = cellSize > 0f ? cellSize : Mathf.Max(1e-5f, sampleStep * 8f);
        float eps = SimTuning.Eps(assumedCell);
        float step = Mathf.Max(1e-5f, sampleStep); // keep caller’s step if provided
        sim.Configure(assumedCell, sampleStep: step, eps: eps, safetyGap: Mathf.Max(0f, safetyGap));
        sim.SeedTapePrefixStraight(headPos, forwardWorld, Mathf.Max(0f, length));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MAIN LOOP (adapter over SimpleTrainSim)

    private IEnumerator MoveRoutine(System.Action<MoveCompletion> onCompleted)
    {
        isMoving = true;

        // Place head and carts at s=0 using the core
        sim.SampleHead(0f, out var headPos0, out var headTan0);
        transform.position = headPos0;
        transform.rotation = Quaternion.LookRotation(Vector3.forward, headTan0) * Quaternion.Euler(0, 0, -90f);

        // Initial cart placement from tape/prefix
        var cartPos = ListPool<Vector3>.Get();
        var cartTan = ListPool<Vector3>.Get();
        sim.GetCartPoses(cartPos, cartTan);
        for (int i = 0; i < carts.Count && i < cartPos.Count; i++)
        {
            carts[i].transform.position = cartPos[i];
            carts[i].transform.rotation = Quaternion.LookRotation(Vector3.forward, cartTan[i]) * Quaternion.Euler(0, 0, -90f);
        }
        ListPool<Vector3>.Release(cartPos);
        ListPool<Vector3>.Release(cartTan);

        AdvanceResult lastRes = new AdvanceResult { Kind = AdvanceResultKind.EndOfPath };
        var myCtrl = GetComponent<TrainController>();

        yield return null; // first rendered frame

        // MAIN LOOP
        while (true)
        {
            float want = moveSpeed * Time.deltaTime;

            if (want > 0f)
            {
                // Build "others" list of core sims (stationary) and an id map for debug
                IList<SimpleTrainSim> others = null;
                Dictionary<SimpleTrainSim, int> idMap = null;

                if (collisionsEnabled)
                {
                    var trains = GameManager.Instance.trains;
                    var list = new List<SimpleTrainSim>(trains.Count);
                    idMap = new Dictionary<SimpleTrainSim, int>(trains.Count);
                    for (int i = 0; i < trains.Count; i++)
                    {
                        var tc = trains[i];
                        if (tc == null) continue;
                        var mv = tc.GetComponent<TrainMover>();
                        if (mv == null) continue;
                        if (ReferenceEquals(mv, this)) continue;
                        list.Add(mv.sim);
                        idMap[mv.sim] = tc.TrainId;
                    }
                    others = list;
                }

                int GetId(SimpleTrainSim s) => (idMap != null && idMap.TryGetValue(s, out var id)) ? id : 0;

                // Game-side computation
                var res = sim.ComputeAllowedAdvance(want, others, getId: GetId);
                float allowed = res.Allowed;

                // Mirror preview + compare (editor/dev only manager)
                var mirrorRes = MirrorManager.Instance != null ? MirrorManager.Instance.Preview(myCtrl, want) : default;
                MirrorManager.Instance?.CompareTickResults($"T{myCtrl.TrainId}", res, mirrorRes);

                // Debug gizmos
                if (collisionDebug)
                {
                    dbgMovingSlice = sim.LastMovingSlice;
                    dbgBlockedSlice = sim.LastBlockedSlice;
                    if (others != null && sim.LastBlockedSlice != null)
                        Debug.Log($"[TrainMover] BLOCKED by Train {sim.LastBlockerId}  allowed={allowed:F3} m");
                }

                // Apply movement in game
                if (allowed > 1e-6f)
                {
                    sim.CommitAdvance(allowed, out var headPos, out var headTan);
                    transform.position = headPos;
                    transform.rotation = Quaternion.LookRotation(Vector3.forward, headTan) * Quaternion.Euler(0, 0, -90f);

                    cartPos = ListPool<Vector3>.Get();
                    cartTan = ListPool<Vector3>.Get();
                    sim.GetCartPoses(cartPos, cartTan);
                    for (int i = 0; i < carts.Count && i < cartPos.Count; i++)
                    {
                        carts[i].transform.position = cartPos[i];
                        carts[i].transform.rotation = Quaternion.LookRotation(Vector3.forward, cartTan[i]) * Quaternion.Euler(0, 0, -90f);
                    }
                    ListPool<Vector3>.Release(cartPos);
                    ListPool<Vector3>.Release(cartTan);
                }

                // Commit same advance to mirror so it stays in lockstep
                MirrorManager.Instance?.Commit(myCtrl, allowed, out _, out _);

                lastRes = res;

                // End-of-leg?
                if (res.Kind == AdvanceResultKind.EndOfPath || res.Kind == AdvanceResultKind.Blocked || sim.AtEnd())
                    break;
            }

            yield return null;
        }

        isMoving = false;
        moveCoroutine = null;

        if (onCompleted != null)
        {
            if (lastRes.Kind == AdvanceResultKind.EndOfPath || sim.AtEnd())
                onCompleted(new MoveCompletion { Outcome = MoveOutcome.Arrived });
            else if (lastRes.Kind == AdvanceResultKind.Blocked)
                onCompleted(new MoveCompletion { Outcome = MoveOutcome.Blocked, BlockerId = lastRes.BlockerId, HitPos = lastRes.HitPos });
            else
                onCompleted(new MoveCompletion { Outcome = MoveOutcome.Arrived });
        }
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // Gizmos

    private void OnDrawGizmosSelected()
    {
        if (!collisionDebug) return;
        if (dbgMovingSlice != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < dbgMovingSlice.Count - 1; i++)
                Gizmos.DrawLine(dbgMovingSlice[i], dbgMovingSlice[i + 1]);
        }
        if (dbgBlockedSlice != null)
        {
            Gizmos.color = Color.magenta;
            for (int i = 0; i < dbgBlockedSlice.Count - 1; i++)
                Gizmos.DrawLine(dbgBlockedSlice[i], dbgBlockedSlice[i + 1]);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Small pooled list helper to avoid GC (optional)
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> pool = new Stack<List<T>>();
        public static List<T> Get()
        {
            if (pool.Count > 0) return pool.Pop();
            return new List<T>(16);
        }
        public static void Release(List<T> list)
        {
            list.Clear();
            pool.Push(list);
        }
    }

    public void SetInitialCartOffsetsAndCapacity(IList<float> offs, float currCellSize)
    {
        cellSize = currCellSize;
        offsets.Clear();
        if (offs != null) offsets.AddRange(offs);

        // configure core so it can answer immediately
        float step = SimTuning.SampleStep(cellSize);
        float eps = SimTuning.Eps(cellSize);
        sim.Configure(cellSize, sampleStep: step, eps: eps, safetyGap: Mathf.Max(0f, safetyGap));
        sim.SetCartOffsets(offsets);

        // ensure tape capacity covers the tail
        float gap = SimTuning.Gap(cellSize);
        float headHalf = SimTuning.HeadHalfLen(cellSize);
        float cartHalf = SimTuning.CartHalfLen(cellSize);

        float tailBehind;
        if (offsets.Count > 0)
        {
            int lastIdx = offsets.Count - 1;
            tailBehind = offsets[lastIdx] + cartHalf;
        }
        else
        {
            tailBehind = headHalf;
        }
        sim.TapeCapacityMeters = tailBehind + gap + SimTuning.TapeMarginMeters;
    }
}

