using RailSimCore;
using System.Collections.Generic;
using UnityEngine;
using static RailSimCore.Types;

public class TrainController : MonoBehaviour
{
    [SerializeField] TrainMover mover;
    [SerializeField] Transform cartHolder;
    [SerializeField] Transform trainVisuals;
    [SerializeField] TrainClickView trainClickView;

    List<float> cartCenterOffsets; // lag of each cart center behind the head center (meters)
    Vector3 initialForward; // world forward at spawn (from p.direction)
    float headHalfLength; // = cellSize * 0.5f
    float cartHalfLength; // = cellSize / 6f
    float requiredTapeLength; // >= tail offset + small margin

    private List<GameObject> currCarts = new List<GameObject>();

    public TrainDir direction;
    public GamePoint CurrentPointModel;
    float currCellSize;

    [Header("Capacity")]
    public int reservedCartSlots = 20;

    // Quick facts (handy for logs/debug)
    public int TrainId => CurrentPointModel?.id ?? 0;
    public Vector3 HeadWorldPos => transform.position;

    public void Init(GamePoint p, LevelData level, Vector2 worldOrigin, int minX, int minY, int gridH, float cellSize, GameObject cartPrefab)
    {
        currCellSize = cellSize;
        currCarts.Clear();

        if (cartCenterOffsets == null) cartCenterOffsets = new List<float>();
        cartCenterOffsets.Clear();

        CurrentPointModel = p;

        // 1) Snapped world cell
        Vector2 worldCell = p.anchor.exitPin >= 0
            ? new Vector2(p.part.exits[p.anchor.exitPin].worldCell.x, p.part.exits[p.anchor.exitPin].worldCell.y)
            : new Vector2(p.gridX, p.gridY);

        // 2) Cell -> world
        float cellX = worldCell.x - minX + 0.5f;
        float cellY = worldCell.y - minY + 0.5f;
        Vector2 flipped = new Vector2(cellX, gridH - cellY);
        Vector3 centerPos = new Vector3(worldOrigin.x + flipped.x * cellSize,
                                        worldOrigin.y + flipped.y * cellSize, 0f);
        transform.position = centerPos;

        // 3) Apply rotation based on direction
        float angleZ = p.direction switch
        {
            TrainDir.Up => 270f,
            TrainDir.Right => 180f,
            TrainDir.Down => 90f,
            TrainDir.Left => 0f,
            _ => 0f
        };
        transform.rotation = Quaternion.Euler(0f, 0f, angleZ);

        // 3.1) Scale visuals using LOCAL bounds (axis-stable)
        if (trainVisuals != null)
        {
            float targetLen = cellSize;          // local +X (forward/back)
            float targetWid = SimTuning.CartLen(cellSize);   // local +Y (side)
            float targetHgt = SimTuning.CartLen(cellSize);     // local +Z (up)

            var mr = trainVisuals.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var size = mr.localBounds.size;  // local, unaffected by root rotation
                if (size.x > 0f && size.y > 0f && size.z > 0f)
                {
                    float scaleX = targetLen / size.x;
                    float scaleY = targetWid / size.y;
                    float scaleZ = targetHgt / size.z;
                    trainVisuals.localScale = new Vector3(scaleX, scaleY, scaleZ);
                }
            }
        }

        // 4) Carts
        Vector3 forwardWS = p.direction switch
        {
            TrainDir.Up => Vector3.up,
            TrainDir.Right => Vector3.right,
            TrainDir.Down => Vector3.down,
            TrainDir.Left => Vector3.left,
            _ => Vector3.up
        };
        initialForward = forwardWS;

        // Geometry cache
        float cartSize = SimTuning.CartLen(cellSize);
        float gap = SimTuning.Gap(cellSize);
        headHalfLength = SimTuning.HeadHalfLen(cellSize);
        cartHalfLength = SimTuning.CartHalfLen(cellSize);

        float headBack = headHalfLength;
        float firstOffset = headBack + gap + cartHalfLength;

        if (cartHolder == null)
        {
            Debug.LogError("Train prefab missing 'cartHolder' reference");
            return;
        }

        // IMPORTANT: place carts in LOCAL space along lopcal back -X (backwards along the train's length)
        Vector3 localBackward = Vector3.right;

        for (int j = 0; j < p.initialCarts.Count; j++)
        {
            var cartGO = Instantiate(cartPrefab, cartHolder, false); // keep local space
            cartGO.name = $"Train_{p.id}_Cart_{j + 1}";

            float offset = firstOffset + (cartSize + gap) * j; // center distance behind head center
            cartGO.transform.localPosition = localBackward * offset;
            cartGO.transform.localRotation = Quaternion.identity;
            cartGO.transform.localScale = new Vector3(cartSize, cartSize, cartSize);

            // Keep world pose but make them siblings (matches your previous setup)
            cartGO.transform.SetParent(transform.parent, true);

            currCarts.Add(cartGO);
            cartCenterOffsets.Add(offset);
        }

        // Align initial cart rotations to head
        for (int i = 0; i < currCarts.Count; i++)
            currCarts[i].transform.rotation = transform.rotation;

        // Tape length requirement (tail offset + small margin)
        float tailBehind;
        if (currCarts.Count > 0 && cartCenterOffsets != null && cartCenterOffsets.Count > 0)
        {
            int lastIdx = cartCenterOffsets.Count - 1;
            tailBehind = cartCenterOffsets[lastIdx] + cartHalfLength;
        }
        else
        {
            tailBehind = headHalfLength; // engine body when no carts
        }

        requiredTapeLength = tailBehind + gap + SimTuning.TapeMarginMeters;

        // Seed back tape so this train can be collided with before it ever moves

        // make the train collidable BEFORE any movement
        mover.SetInitialCartOffsetsAndCapacity(cartCenterOffsets, currCellSize);
        mover.SeedTapePrefixStraight(transform.position, initialForward, requiredTapeLength, SimTuning.SampleStep(currCellSize));

        GameManager.Instance.trains.Add(this);
        trainClickView.Init(TrainWasClicked);

        MirrorManager.Instance?.RegisterTrain(this, transform.position, initialForward, cartCenterOffsets, requiredTapeLength, safetyGap: 0f);
    }

    private void TrainWasClicked()
    {
        GameManager.Instance.SelectTrain(this);
    }

    public void MoveAlongPath(List<Vector3> worldPoints)
    {
        if (mover != null)
            mover.MoveAlongPath(worldPoints,currCarts, currCellSize, OnMoveCompleted);
    }

    private void OnMoveCompleted(MoveCompletion r)
    {
        if (r.Outcome == MoveOutcome.Arrived)
        {
            //OnArrivedStation_AddCart(); // your existing arrival logic
        }
        else if (r.Outcome == MoveOutcome.Blocked)
        {
            // Collision handling: freeze input, show UI, log, etc.
            Debug.Log($"Train {CurrentPointModel.id} blocked by Train {r.BlockerId} at {r.HitPos}");
            // TODO: puzzle fail/retry flow
        }
    }

    public void OnArrivedStation_AddCart()
    {
        var mover = GetComponent<TrainMover>();
        if (mover == null)
        {
            Debug.LogError("TrainController: TrainMover not found.");
            return;
        }

        // Geometry (match your Init rules)
        float cartLength = SimTuning.CartLen(currCellSize);   // cart 'size' along the path
        float gap = SimTuning.Gap(currCellSize);
        float halfCart = SimTuning.CartHalfLen(currCellSize);

        // Compute new cart center offset from head center
        float lastOffset = 0f;
        // with this (controller is source of truth):
        lastOffset = (cartCenterOffsets != null && cartCenterOffsets.Count > 0)
            ? cartCenterOffsets[cartCenterOffsets.Count - 1]: 0f;


        float newCenterOffset = lastOffset + cartLength + gap;


        // Ask mover for the exact pose on the back path
        if (!mover.TryGetPoseAtBackDistance(newCenterOffset, out Vector3 pos, out Quaternion rot))
        {
            Debug.Log("TrainController: Not enough back-path to add a new cart at this station.");
            return;
        }

        // Spawn new cart as a sibling of the train (same as your Init end-state)
        var newCart = Instantiate(LevelVisualizer.Instance.CartPrefab, transform.parent);
        newCart.name = $"Train_{CurrentPointModel.id}_Cart_{currCarts.Count + 1}";
        newCart.transform.position = pos;
        newCart.transform.rotation = rot * Quaternion.Euler(0, 0, -90f);
        newCart.transform.localScale = new Vector3(cartLength, cartLength, cartLength);

        // Update controller data
        if (currCarts == null) currCarts = new List<GameObject>();
        currCarts.Add(newCart);

        if (cartCenterOffsets == null) cartCenterOffsets = new List<float>();
        cartCenterOffsets.Add(newCenterOffset);

        // Keep required tape length up-to-date for the next leg start
        requiredTapeLength = newCenterOffset + halfCart + gap + SimTuning.TapeMarginMeters;

        // Tell mover about the new offset so it will drive this cart on the next leg
        mover.AddCartOffset(newCenterOffset);
    }

    // Already present; keep it public so others can read it
    public float GetTrainLengthMeters()
    {
        if (cartCenterOffsets != null && cartCenterOffsets.Count > 0)
            return cartCenterOffsets[cartCenterOffsets.Count - 1] + cartHalfLength; // head->tail
        return headHalfLength; // no carts yet
    }

    /// <summary>
    /// Sample a single point on this train's back tape at 'backDistance' meters behind the head.
    /// Returns false if the tape isn't long enough yet (e.g., just spawned).
    /// </summary>
    public bool TryGetBackPoint(float backDistance, out Vector3 pos)
    {
        pos = default;
        var mv = mover != null ? mover : GetComponent<TrainMover>();
        if (mv == null) return false;

        if (!mv.TryGetPoseAtBackDistance(Mathf.Max(0f, backDistance), out var p, out _))
            return false;

        pos = p;
        return true;
    }

    /// <summary>
    /// Build the occupied slice polyline for this (stationary) train:
    /// the last (trainLength + safetyGap) meters of its back tape.
    /// Returns false if the tape is shorter (e.g., very early in the level).
    /// Points are in world space, ordered from nearest-to-head to farthest-back.
    /// </summary>
    public bool TryGetOccupiedBackSlice(float safetyGap, float sampleStep, out List<Vector3> points)
    {
        points = null;

        var mv = mover != null ? mover : GetComponent<TrainMover>();
        if (mv == null) return false;

        float backLen = Mathf.Max(0f, GetTrainLengthMeters() + Mathf.Max(0f, safetyGap));
        if (backLen <= 1e-6f) return false;

        // Sample along the tape every ~sampleStep (include endpoints).
        int count = Mathf.Max(2, Mathf.CeilToInt(backLen / Mathf.Max(1e-5f, sampleStep)) + 1);
        float step = backLen / (count - 1);

        var pts = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float d = i * step; // 0..backLen behind head
            if (!mv.TryGetPoseAtBackDistance(d, out var p, out _))
            {
                // Tape not long enough: abort (caller can treat as "no reliable data yet")
                return false;
            }
            pts.Add(p);
        }

        points = pts;
        return true;
    }
}
