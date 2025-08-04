// Editor/DummyGridPathProvider.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "RailSim/Path Providers/Dummy Grid Path Provider")]
public class DummyGridPathProvider : ScriptableObject, IEditorPathProvider
{
    [Header("Grid → World (match your editor values)")]
    public Vector2 worldOrigin = Vector2.zero;
    public int minX = 0;
    public int minY = 0;
    public int gridH = 10;   // vertical cell count
    public float cellSize = 1f;

    [Header("Shape")]
    public bool useLShape = true;           // if false → single straight segment
    public bool deterministicDogleg = true; // L-shape: X-then-Y or Y-then-X chosen deterministically

    public List<Vector3> GetPath(LevelData level, GamePoint from, GamePoint to)
    {
        var a = CellCenterToWorld(from.gridX, from.gridY);
        var b = CellCenterToWorld(to.gridX, to.gridY);

        // Same cell: return a tiny 2-point segment to satisfy “≥ 2 points”
        if ((a - b).sqrMagnitude < 1e-12f)
            return new List<Vector3> { a, a + Vector3.right * Mathf.Max(1e-3f, cellSize * 0.01f) };

        if (!useLShape)
            return new List<Vector3> { a, b };

        // L-shaped dogleg (axis-aligned). Order is deterministic by ids.
        bool xThenY = deterministicDogleg
                      ? (from.id <= to.id)    // stable choice
                      : (Random.value < 0.5f);

        if (xThenY)
        {
            var mid = new Vector3(b.x, a.y, 0f);
            if ((mid - a).sqrMagnitude < 1e-12f || (b - mid).sqrMagnitude < 1e-12f)
                return new List<Vector3> { a, b };
            return new List<Vector3> { a, mid, b };
        }
        else
        {
            var mid = new Vector3(a.x, b.y, 0f);
            if ((mid - a).sqrMagnitude < 1e-12f || (b - mid).sqrMagnitude < 1e-12f)
                return new List<Vector3> { a, b };
            return new List<Vector3> { a, mid, b };
        }
    }

    private Vector3 CellCenterToWorld(int gx, int gy)
    {
        // Same mapping you use elsewhere:
        // cellX = gx - minX + 0.5
        // cellY = gy - minY + 0.5
        // flippedY = gridH - cellY
        float cellX = gx - minX + 0.5f;
        float cellY = gy - minY + 0.5f;
        float fx = worldOrigin.x + cellX * cellSize;
        float fy = worldOrigin.y + (gridH - cellY) * cellSize;
        return new Vector3(fx, fy, 0f);
    }
}
#endif
