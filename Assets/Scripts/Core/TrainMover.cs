using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrainMover : MonoBehaviour
{
    [Header("Motion")]
    public float moveSpeed = 4f;

    [Header("Capacity")]
    public int reservedCartSlots = 20;     // reserve back capacity (meters) for up to N carts on first leg
    public bool debugBack = false;

    // runtime state
    private Coroutine moveCoroutine;
    private bool isMoving;

    // forward path data (current leg)
    private List<Vector3> pathPts = new List<Vector3>();
    private List<float> cum = new List<float>(); // cumulative arc lengths
    private float totalLen;
    private int segIndex;

    // head arc-length
    private float sHead;

    // start tangent (first segment dir of current leg)
    private Vector3 startFwd = Vector3.right;

    // carts & geometry
    private List<GameObject> carts = new List<GameObject>();
    private List<float> offsets = new List<float>(); // cart center lags behind head center (meters)
    private float cellSize;
    private float cartHalfLen; // ≈ cellSize/6
    private float headHalfLen; // ≈ cellSize/2

    // back path tape (persists across legs)
    private PathTape tape = new PathTape();


    [Header("Collision (simple)")]
    public bool collisionsEnabled = true;
    public float safetyGap = 0.0f;          // meters added behind stationary trains (along tape)
    public float collisionSampleStep = 0.0f; // if 0, we'll default to cellSize/8 at runtime
    public float collisionEps = 1e-4f;      // ~ 1e-4 * cellSize; set in MoveAlongPath from cellSize

    // ─────────────────────────────────────────────────────────────────────────────
    // PUBLIC API

    // Start moving along a leg; will invoke onArrivedStation once at the end.
    public void MoveAlongPath(List<Vector3> worldPoints, List<GameObject> currCarts, float currCellSize, System.Action onArrivedStation = null)
    {
        if (isMoving) return;
        if (worldPoints == null || worldPoints.Count < 2)
        {
            Debug.LogWarning("TrainMover: Invalid path");
            return;
        }

        // cache inputs
        carts = currCarts;
        cellSize = currCellSize;
        cartHalfLen = (cellSize / 3f) * 0.5f;
        headHalfLen = cellSize * 0.5f;

        // build forward path metrics
        BuildCum(worldPoints);

        // derive start direction from path
        startFwd = (pathPts[1] - pathPts[0]).normalized;

        // Offsets: keep existing if counts match; otherwise rebuild from current transforms (spawn look == follow spacing)
        if (offsets == null || offsets.Count != carts.Count)
        {
            if (offsets == null) offsets = new List<float>();
            offsets.Clear();

            Vector3 headPos = transform.position;
            for (int i = 0; i < carts.Count; i++)
            {
                float off = Vector3.Dot(headPos - carts[i].transform.position, startFwd);
                if (off < 0f) off = 0f;
                offsets.Add(off);
            }
        }

        // Reserve back capacity for N carts on the very first leg only if tape is empty.
        // reservedBackMeters = distance from head center to the Nth cart center behind it (straight line).
        if (tape.IsEmpty)
        {
            float cartLength = cellSize / 3f;
            float gap = cellSize / 10f;
            float firstOffset = headHalfLen + gap + cartHalfLen;
            int slots = Mathf.Max(1, reservedCartSlots);
            float reservedBackMeters = firstOffset + (cartLength + gap) * (slots - 1);

            // Initialize a straight prefix behind the start point for the first leg
            tape.EnsurePrefix(pathPts[0], startFwd, reservedBackMeters + 0.1f);
        }

        // init collision defaults once per leg
        if (collisionSampleStep <= 0f) collisionSampleStep = cellSize / 8f;
        if (collisionEps <= 0f) collisionEps = Mathf.Max(1e-5f, 1e-4f * cellSize);

        moveCoroutine = StartCoroutine(MoveRoutine(onArrivedStation));
    }

    // Provide a precise pose "backDistance" meters behind the head using the real back tape.
    // Falls back to the initial straight prefix only if the tape is shorter (first leg, early motion).
    public bool TryGetPoseAtBackDistance(float backDistance, out Vector3 pos, out Quaternion rot)
    {
        bool ok = tape.SampleBack(backDistance, out pos, out Vector3 tan, out float avail);
        if (!ok && debugBack)
        {
            Debug.LogError($"[TrainMover] BACK SHORT | need={backDistance:F3} avail={avail:F3} (tapeLen+prefix)");
        }

        rot = Quaternion.LookRotation(Vector3.forward, tan);
        return ok;
    }

    // Optional helper so controller can keep mover offsets in sync immediately after adding a cart
    public void AddCartOffset(float newCenterOffset)
    {
        if (offsets == null) offsets = new List<float>();
        offsets.Add(newCenterOffset);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MAIN LOOP

    private IEnumerator MoveRoutine(System.Action onArrivedStation)
    {
        isMoving = true;

        // Collision tunables (simple defaults; tweak if you like)
        float sampleStep = Mathf.Max(1e-5f, cellSize / 8f);     // resample ~every 1/8 cell
        float eps = Mathf.Max(1e-5f, 1e-4f * cellSize);  // numeric tolerance
        float safetyGap = 0f;                                   // along-tape gap behind other trains

        // initialize arc-length and segment index
        sHead = 0f;
        segIndex = 0;

        // PRE-SEED before first frame: place head at s=0, seed tape with that point, place carts from tape/prefix
        SampleForward(0f, out Vector3 headPos0, out Quaternion headRot0);
        transform.position = headPos0;
        transform.rotation = headRot0 * Quaternion.Euler(0, 0, -90f);

        // Seed tape with the actual head position (from now on we append every movement)
        tape.AppendPoint(headPos0);

        // place carts immediately from tape/prefix
        for (int i = 0; i < carts.Count; i++)
        {
            float sBack = offsets[i];
            bool ok = tape.SampleBack(sBack, out Vector3 cPos, out Vector3 cTan, out _);
            if (!ok && debugBack)
                Debug.LogWarning($"[TrainMover] Pre-seed cart {i} needed {sBack:F3} but back available is shorter.");
            carts[i].transform.position = cPos;
            carts[i].transform.rotation = Quaternion.LookRotation(Vector3.forward, cTan) * Quaternion.Euler(0, 0, -90f);
        }

        yield return null; // first rendered frame done

        // MAIN LOOP
        Vector3 prevHeadPos = headPos0;
        var myCtrl = GetComponent<TrainController>();

        while (sHead < totalLen)
        {
            float want = moveSpeed * Time.deltaTime;
            if (want > 0f)
            {
                // ---- collision cap (single mover): compare our moving slice vs each other train's occupied back slice ----
                float allowed = ComputeAllowedAdvance(want);

                if (allowed > 1e-6f)
                {
                    sHead = Mathf.Min(sHead + allowed, totalLen);

                    // head pose
                    SampleForward(sHead, out Vector3 headPos, out Quaternion headRot);
                    transform.position = headPos;
                    transform.rotation = headRot * Quaternion.Euler(0, 0, -90f);

                    // append actual movement to tape (real curve), then trim to capacity
                    tape.AppendSegment(prevHeadPos, headPos);
                    prevHeadPos = headPos;
                    tape.TrimToCapacity();

                    // carts from real tape
                    for (int i = 0; i < carts.Count; i++)
                    {
                        float sBack = offsets[i];
                        tape.SampleBack(sBack, out Vector3 cPos, out Vector3 cTan, out _);
                        carts[i].transform.position = cPos;
                        carts[i].transform.rotation = Quaternion.LookRotation(Vector3.forward, cTan) * Quaternion.Euler(0, 0, -90f);
                    }
                }
                // else: blocked this frame → idle; try again next frame
            }

            yield return null;
        }

        isMoving = false;
        moveCoroutine = null;

        // notify controller we reached the station
        onArrivedStation?.Invoke();
    }

    // ───────────────────────────── Samplers (forward path) ─────────────────────────────

    private void SampleForward(float s, out Vector3 pos, out Quaternion rot)
    {
        if (s <= 0f)
        {
            pos = pathPts[0];
            rot = Quaternion.LookRotation(Vector3.forward, startFwd);
            return;
        }
        if (s >= totalLen)
        {
            Vector3 dirEnd = (pathPts[pathPts.Count - 1] - pathPts[pathPts.Count - 2]).normalized;
            pos = pathPts[pathPts.Count - 1];
            rot = Quaternion.LookRotation(Vector3.forward, dirEnd);
            return;
        }

        // advance segIndex while s is beyond current segment
        while (segIndex < cum.Count - 2 && s > cum[segIndex + 1]) segIndex++;
        while (segIndex > 0 && s < cum[segIndex]) segIndex--;

        float segStart = cum[segIndex];
        float segEnd = cum[segIndex + 1];
        float t = (s - segStart) / (segEnd - segStart);

        Vector3 a = pathPts[segIndex];
        Vector3 b = pathPts[segIndex + 1];

        pos = Vector3.LerpUnclamped(a, b, t);
        Vector3 dir = (b - a).normalized;
        rot = Quaternion.LookRotation(Vector3.forward, dir);
    }

    private void BuildCum(List<Vector3> worldPoints)
    {
        pathPts.Clear();
        cum.Clear();
        pathPts.AddRange(worldPoints);

        float acc = 0f;
        cum.Add(0f);
        for (int i = 1; i < pathPts.Count; i++)
        {
            float d = Vector3.Distance(pathPts[i - 1], pathPts[i]);
            if (d <= 1e-6f)
            {
                // guard against zero-length segments
                pathPts[i] = pathPts[i] + new Vector3(1e-4f, 0f, 0f);
                d = 1e-4f;
            }
            acc += d;
            cum.Add(acc);
        }
        totalLen = acc;
        segIndex = 0;
    }

    // ───────────────────────────── PathTape (back path) ─────────────────────────────

    private class PathTape
    {
        private readonly List<Vector3> pts = new List<Vector3>(); // earliest .. latest (head)
        private readonly List<float> cum = new List<float>();   // cumulative from pts[0]
        private float maxLen = 0f;                                // capacity for trimming

        // initial straight prefix (only if tape is short; used on first leg)
        private Vector3 prefixDir = Vector3.right;
        private float prefixLen = 0f;

        public bool IsEmpty => pts.Count == 0;

        public void EnsurePrefix(Vector3 startPoint, Vector3 startForward, float length)
        {
            // Initialize only if we have no points yet (first leg)
            if (IsEmpty)
            {
                pts.Add(startPoint);
                cum.Add(0f);
            }
            prefixDir = startForward.normalized;
            if (length > prefixLen) prefixLen = length;
        }

        public void SetMaxLen(float lenMeters)
        {
            if (lenMeters > maxLen) maxLen = lenMeters;
        }

        public void AppendPoint(Vector3 p)
        {
            if (IsEmpty)
            {
                pts.Add(p);
                cum.Add(0f);
                return;
            }
            Vector3 last = pts[pts.Count - 1];
            float d = Vector3.Distance(last, p);
            if (d <= 1e-6f) return;
            pts.Add(p);
            cum.Add(cum[cum.Count - 1] + d);
        }

        public void AppendSegment(Vector3 a, Vector3 b)
        {
            if (IsEmpty) { AppendPoint(a); }
            AppendPoint(b);
        }

        public void TrimToCapacity()
        {
            if (pts.Count < 2 || maxLen <= 0f) return;

            float totalSpan = cum[cum.Count - 1] - cum[0];
            while (pts.Count > 2 && totalSpan > maxLen)
            {
                // remove front point
                pts.RemoveAt(0);
                float baseCum = cum[0];
                cum.RemoveAt(0);
                // rebase remaining cumulative to keep numbers small
                for (int i = 0; i < cum.Count; i++) cum[i] -= baseCum;
                totalSpan = cum[cum.Count - 1] - cum[0];
            }
        }

        // Returns true if we could sample (either on tape or in prefix).
        // 'available' returns total available back distance (tape length + prefix length).
        public bool SampleBack(float sBack, out Vector3 pos, out Vector3 tan, out float available)
        {
            sBack = Mathf.Max(0f, sBack);
            float tapeLen = (pts.Count > 1) ? (cum[cum.Count - 1] - cum[0]) : 0f;
            available = tapeLen + prefixLen;

            if (pts.Count == 0)
            {
                // No points at all: cannot sample
                pos = Vector3.zero; tan = Vector3.right; return false;
            }

            if (sBack <= tapeLen)
            {
                // target cumulative along tape
                float target = cum[cum.Count - 1] - sBack;
                // binary search (could also roll an index)
                int lo = 0, hi = cum.Count - 1;
                while (lo + 1 < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cum[mid] <= target) lo = mid; else hi = mid;
                }
                float segStart = cum[lo];
                float segEnd = cum[hi];
                float t = (segEnd > segStart) ? (target - segStart) / (segEnd - segStart) : 0f;
                Vector3 a = pts[lo];
                Vector3 b = pts[hi];
                pos = Vector3.LerpUnclamped(a, b, t);
                tan = (b - a).normalized;
                if (tan.sqrMagnitude < 1e-8f)
                {
                    // fallback tangent if tiny
                    tan = (lo > 0 ? (pts[lo] - pts[lo - 1]) : (pts[hi + 1 < pts.Count ? hi + 1 : hi] - pts[lo])).normalized;
                    if (tan.sqrMagnitude < 1e-8f) tan = Vector3.right;
                }
                return true;
            }

            // Need beyond tape: use initial straight prefix only if we have one
            float needPrefix = sBack - tapeLen;
            if (needPrefix <= prefixLen)
            {
                pos = pts[0] - prefixDir * needPrefix;
                tan = prefixDir;
                return true;
            }

            pos = pts[pts.Count - 1];
            tan = (pts.Count > 1 ? (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized : prefixDir);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns how far we can advance (≤ want) before intersecting any other train's occupied back-slice.
    /// Uses the mover's forward path for the moving-slice, and each other train's back tape for occupancy.
    /// </summary>
    private float ComputeAllowedAdvance(float want)
    {
        // Substep loop to avoid tunneling: clamp per-iter ≤ sampleStep/2
        float remaining = Mathf.Min(want, totalLen - sHead);
        float advanced = 0f;

        while (remaining > 1e-6f)
        {
            float iter = Mathf.Min(remaining, collisionSampleStep * 0.5f, totalLen - (sHead + advanced));
            if (iter <= 1e-6f) break;

            // Build moving slice polyline from sHead+advanced .. +advanced+iter
            var movingSlice = BuildForwardSlice(sHead + advanced, sHead + advanced + iter, collisionSampleStep);

            // Test against every other (stationary) train's occupied slice
            float cap = iter; // how much of 'iter' we can actually take
            var trains = GameManager.Instance.trains; // your global list of TrainController
            for (int i = 0; i < trains.Count; i++)
            {
                var other = trains[i];
                if (other == null) continue;
                if (other.gameObject == this.gameObject) continue; // same train
                if (!other.TryGetOccupiedBackSlice(safetyGap, collisionSampleStep, out var occupiedSlice)) continue;

                // Intersect movingSlice (this frame) with other train's occupied back slice
                if (IntersectPolylines(movingSlice, occupiedSlice, collisionEps, out float alongMoving))
                {
                    // Contact within this iter; allow up to just before contact
                    cap = Mathf.Min(cap, Mathf.Max(0f, alongMoving));
                    // Early exit: can't get better than 0
                    if (cap <= 1e-6f) break;
                }
            }

            advanced += cap;

            // If we were blocked inside this iter, stop substepping this frame
            if (cap + 1e-6f < iter) break;

            remaining -= iter;
        }

        return advanced;
    }

    /// <summary>Builds a world-space polyline along our forward path between s0..s1 (inclusive), sampled ~ every 'step' meters.</summary>
    private List<Vector3> BuildForwardSlice(float s0, float s1, float step)
    {
        if (s1 < s0) (s0, s1) = (s1, s0);
        float len = Mathf.Max(0f, s1 - s0);
        int count = Mathf.Max(2, Mathf.CeilToInt(len / Mathf.Max(1e-5f, step)) + 1);
        var pts = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float u = (count == 1) ? 0f : i / (float)(count - 1);
            float s = Mathf.Lerp(s0, s1, u);
            SampleForward(s, out Vector3 pos, out _);
            pts.Add(pos);
        }
        return pts;
    }

    /// <summary>
    /// Polyline–polyline intersection. Detects both proper crossings and colinear overlaps.
    /// Returns the first hit distance along 'A' (moving slice) in meters.
    /// </summary>
    private static bool IntersectPolylines(List<Vector3> A, List<Vector3> B, float eps, out float alongA)
    {
        alongA = 0f;
        float accA = 0f;
        for (int i = 0; i < A.Count - 1; i++)
        {
            Vector2 a0 = A[i]; Vector2 a1 = A[i + 1];
            float aLen = Vector2.Distance(a0, a1);
            if (aLen <= eps) { accA += aLen; continue; }

            float accB = 0f;
            for (int j = 0; j < B.Count - 1; j++)
            {
                Vector2 b0 = B[j]; Vector2 b1 = B[j + 1];
                float bLen = Vector2.Distance(b0, b1);
                if (bLen <= eps) { accB += bLen; continue; }

                if (TryIntersectSegments2D(a0, a1, b0, b1, eps, out float ta, out float tb, out bool colinear, out _))
                {
                    if (!colinear)
                    {
                        alongA = accA + Mathf.Clamp01(ta) * aLen;
                        return true;
                    }
                    else
                    {
                        // Colinear overlap: if there is any positive-length overlap, treat as hit at start of this a-segment
                        // More accurate: compute overlap start along A; for puzzles this early exit is fine
                        alongA = accA;
                        return true;
                    }
                }
                accB += bLen;
            }
            accA += aLen;
        }
        return false;
    }

    // Robust 2D segment intersection with colinearity detection
    private static bool TryIntersectSegments2D(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, float eps,
                                               out float tP, out float tQ, out bool colinear, out Vector2 inter)
    {
        tP = tQ = 0f; inter = Vector2.zero; colinear = false;
        Vector2 r = p2 - p;
        Vector2 s = q2 - q;
        float rxs = r.x * s.y - r.y * s.x;
        float q_pxr = (q.x - p.x) * r.y - (q.y - p.y) * r.x;

        if (Mathf.Abs(rxs) < eps && Mathf.Abs(q_pxr) < eps)
        {
            // Colinear – check overlap
            colinear = true;
            float rr = Vector2.Dot(r, r);
            if (rr < eps) return false;
            float t0 = Vector2.Dot(q - p, r) / rr;
            float t1 = Vector2.Dot(q2 - p, r) / rr;
            float tmin = Mathf.Max(0f, Mathf.Min(t0, t1));
            float tmax = Mathf.Min(1f, Mathf.Max(t0, t1));
            if (tmax - tmin < eps) return false; // no overlap
            tP = tmin; tQ = 0f; inter = p + r * ((tmin + tmax) * 0.5f);
            return true;
        }

        if (Mathf.Abs(rxs) < eps) return false; // parallel, non-intersecting

        float t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / rxs;
        float u = ((q.x - p.x) * r.y - (q.y - p.y) * r.x) / rxs;

        if (t >= -eps && t <= 1f + eps && u >= -eps && u <= 1f + eps)
        {
            t = Mathf.Clamp01(t);
            u = Mathf.Clamp01(u);
            inter = p + t * r;
            tP = t; tQ = u;
            return true;
        }
        return false;
    }

}
