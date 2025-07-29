using System.Collections.Generic;
using UnityEngine;

public class TrainController : MonoBehaviour
{
    [SerializeField] TrainMover mover;
    [SerializeField] Transform cartHolder;
    [SerializeField] Transform trainVisuals;
    [SerializeField] TrainClickView trainClickView;

    
    public TrainDir direction;
    public GamePoint CurrentPointModel;

    public void Init(GamePoint p, LevelData level, Vector2 worldOrigin, int minX,int minY, int gridH, float cellSize, GameObject cartPrefab)
    {

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

        // Assume the visual is directly on this GameObject, or adjust to child if needed


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
                    float scaleY = scaleX/3f;    // width
                    float scaleZ = scaleX/3f;   // height

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


        float cartSize = cellSize / 3f;
        float gap = cellSize / 10f;
        float headBack = cellSize * 0.5f;
        float firstOffset = headBack + gap + cartSize * 0.5f;

        if (cartHolder == null)
        {
            Debug.LogError("Train prefab missing 'cartHolder' reference");
            return;
        }

        for (int j = 0; j < p.initialCarts.Count; j++)
        {
            var cartGO = Instantiate(cartPrefab, cartHolder);
            cartGO.name = $"Train_{p.id}_Cart_{j + 1}";

            float offset = firstOffset + (cartSize + gap) * j;
            cartGO.transform.localPosition = backward * offset;
            cartGO.transform.localRotation = Quaternion.identity;
        }


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
            mover.MoveAlongPath(worldPoints);
    }
}
