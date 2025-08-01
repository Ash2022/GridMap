using System.Collections.Generic;
using UnityEngine;

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

    public void Init(GamePoint p, LevelData level, Vector2 worldOrigin, int minX, int minY, int gridH, float cellSize, GameObject cartPrefab)
    {
        currCellSize = cellSize;
        currCarts.Clear();

        // NEW: prep offsets list
        if (cartCenterOffsets == null) cartCenterOffsets = new List<float>();
        cartCenterOffsets.Clear();

        CurrentPointModel = p;

        // 1. Determine the snapped world cell
        Vector2 worldCell = p.anchor.exitPin >= 0
            ? new Vector2(
                p.part.exits[p.anchor.exitPin].worldCell.x,
                p.part.exits[p.anchor.exitPin].worldCell.y
              )
            : new Vector2(p.gridX, p.gridY);

        // 2. Convert cell to world space
        float cellX = worldCell.x - minX + 0.5f;
        float cellY = worldCell.y - minY + 0.5f;
        Vector2 flipped = new Vector2(cellX, gridH - cellY);
        Vector3 centerPos = new Vector3(
            worldOrigin.x + flipped.x * cellSize,
            worldOrigin.y + flipped.y * cellSize,
            0f
        );
        transform.position = centerPos;

        // 3. Apply rotation based on direction
        float angleZ = p.direction switch
        {
            TrainDir.Up => 270f,
            TrainDir.Right => 180f,
            TrainDir.Down => 90f,
            TrainDir.Left => 0f,
            _ => 0f
        };
        transform.rotation = Quaternion.Euler(0f, 0f, angleZ);

        if (trainVisuals != null)
        {
            float length = cellSize;         // X axis → forward/back
            float width = cellSize / 3f;     // Y axis → side-to-side
            float height = cellSize / 3f;    // Z axis → vertical

            var meshRenderer = trainVisuals.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                var size = meshRenderer.bounds.size;

                if (size.x > 0f && size.y > 0f && size.z > 0f)
                {
                    float scaleX = length / size.x;   // forward length
                    float scaleY = scaleX / 3f;         // width
                    float scaleZ = scaleX / 3f;         // height

                    trainVisuals.localScale = new Vector3(scaleX, scaleY, scaleZ);
                }
            }
        }

        // 4. Carts
        Vector3 forward = p.direction switch
        {
            TrainDir.Up => Vector3.up,
            TrainDir.Right => Vector3.right,
            TrainDir.Down => Vector3.down,
            TrainDir.Left => Vector3.left,
            _ => Vector3.up
        };
        Vector3 backward = -forward;

        // NEW: cache geometry for later math (no inspector)
        headHalfLength = cellSize * 0.5f;    // head center → rear/front face (we use rear later)
        cartHalfLength = (cellSize / 3f) * 0.5f;

        float cartSize = cellSize / 3f;
        float gap = cellSize / 10f;
        float headBack = headHalfLength;
        float firstOffset = headBack + gap + cartHalfLength;

        if (cartHolder == null)
        {
            Debug.LogError("Train prefab missing 'cartHolder' reference");
            return;
        }

        for (int j = 0; j < p.initialCarts.Count; j++)
        {
            var cartGO = Instantiate(cartPrefab, cartHolder);
            cartGO.name = $"Train_{p.id}_Cart_{j + 1}";

            float offset = firstOffset + (cartSize + gap) * j;     // distance of CART CENTER behind HEAD CENTER
            cartGO.transform.localPosition = backward * offset;
            cartGO.transform.localRotation = Quaternion.identity;
            cartGO.transform.localScale = new Vector3(cartSize, cartSize, cartSize);

            // keep world pose
            cartGO.transform.SetParent(transform.parent);

            currCarts.Add(cartGO);

            // NEW: record the exact path-space center offset we used
            cartCenterOffsets.Add(offset);
        }

        // NEW: align initial cart rotations to head to avoid first-frame pop
        for (int i = 0; i < currCarts.Count; i++)
            currCarts[i].transform.rotation = transform.rotation;

        // NEW: expose forward and required tape length for the mover
        initialForward = forward;

        // tail is last cart center offset + half cart length
        float tailOffsetFromHeadCenter = (currCarts.Count > 0) ? cartCenterOffsets[cartCenterOffsets.Count - 1] + cartHalfLength : 0f;

        // small margin so the sampler has room
        requiredTapeLength = tailOffsetFromHeadCenter + gap + 0.1f;

        GameManager.Instance.trains.Add(this);
        trainClickView.Init(TrainWasClicked);
    }

    private void TrainWasClicked()
    {
        GameManager.Instance.SelectTrain(this);
    }

    public void MoveAlongPath(List<Vector3> worldPoints)
    {
        if (mover != null)
            mover.MoveAlongPath(worldPoints,currCarts, currCellSize, TrainReachedDestination);
    }

    private void TrainReachedDestination()
    {
        OnArrivedStation_AddCart();
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
        float cartLength = currCellSize / 3f;   // cart 'size' along the path
        float gap = currCellSize / 10f;
        float halfCart = cartLength * 0.5f;

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
        requiredTapeLength = newCenterOffset + halfCart + gap + 0.1f;

        // Tell mover about the new offset so it will drive this cart on the next leg
        mover.AddCartOffset(newCenterOffset);
    }

}
