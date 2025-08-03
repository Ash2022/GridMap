using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// This is your LEVEL INSTANCE. It defines a specific arrangement of part instances and game data.

[Serializable]
public class LevelData
{
    public string levelName;                      // Level name
    public int width, height;                     // Grid dimensions
    public List<PlacedPartInstance> parts;        // All placed part instances
    public ScenarioModel gameData = new ScenarioModel();                  // still not game specific - but all the overlay on the tracks
                                                                        //mainly points 
    public RouteModel routeModelData = new RouteModel();
}

// A placed part instance on the grid
[Serializable]
public class PlacedPartInstance
{
    public string partType;                       // Matches TrackPart.partName
    public string partId;                         // Unique per instance (optional)
    public Vector2Int position;                   // Grid position (top-left cell)
    public int rotation;                          // Rotation in degrees (0, 90, etc.)

    // Spline(s) for this part instance (for each allowed path)
    public List<List<float[]>> splines;           // In local part space (0..w, 0..h)

    [JsonIgnore] public List<BakedSpline> bakedSplines;

    public List<List<Vector3>> worldSplines = new List<List<Vector3>>();

    public struct BakedSpline
    {
        public List<Vector2> guiPts;   // final pixels you draw (pts[])
        public List<Vector2> gridPts;  // rotated/offset grid coords (rotatedPt / cellSize)
    }


    // Exit details for the current rotation
    public List<ExitDetails> exits;               // List of exits for this part instance

    public List<Vector2Int> occupyingCells = new List<Vector2Int>();
    public List<AllowedPathGroup> allowedPathsGroup;

    // Define a class to hold exit-specific data
    public class ExitDetails
    {
        public int exitIndex;                     // Exit index (from model.connections)
        public Vector2Int localCell;              // Local exit cell
        public Vector2Int rotatedCell;            // Rotated cell after applying rotation
        public Vector2Int worldCell;              // World cell in the grid
        public int direction;                     // Direction after rotation
        public Vector2Int neighborCell;           // Neighbor cell to search
    }

    internal float InternalLengthCost(int a, int b)
    {
        return 1f;
    }

    public void RecomputeOccupancy(List<TrackPart> partsLib)
    {
        

        var model = partsLib.FirstOrDefault(p => p.partName == partType);
        if (model == null)
        {
            Debug.LogWarning($"TrackPart not found: {partType}");
            occupyingCells.Clear();
            return;
        }

        var locals = (model.solidCells != null && model.solidCells.Count > 0)
            ? model.solidCells
            : AllRectCells(model.gridWidth, model.gridHeight);

        occupyingCells.Clear();
        foreach (var local in locals)
        {
            var rot = RotatePart(local, rotation, model.gridWidth, model.gridHeight);
            occupyingCells.Add(position + rot);
        }
    }

    static List<Vector2Int> AllRectCells(int w, int h)
    {
        var list = new List<Vector2Int>(w * h);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                list.Add(new Vector2Int(x, y));
        return list;
    }

    public static Vector2Int RotatePart(Vector2Int offset, int rotation, int width, int height)
    {
        rotation = (rotation % 360 + 360) % 360;

        if (width != height)
        {
            switch (rotation)
            {
                case 0: return offset;
                case 90: return new Vector2Int(height - 1 - offset.y, offset.x);
                case 180: return new Vector2Int(width - 1 - offset.x, height - 1 - offset.y);
                case 270: return new Vector2Int(offset.y, width - 1 - offset.x);
                default:
                    Debug.LogWarning("Unexpected rotation value");
                    return offset;
            }
        }
        else
        {
            switch (rotation)
            {
                case 0: return offset;
                case 90: return new Vector2Int(height - 1 - offset.y, offset.x);
                case 180: return new Vector2Int(width - 1 - offset.x, height - 1 - offset.y);
                case 270: return new Vector2Int(offset.y, width - 1 - offset.x);
                default:
                    Debug.LogWarning("Unexpected rotation value");
                    return offset;
            }
        }
    }


}

public class PathModel
{
    public bool Success;
    public List<PartTraversal> Traversals;
    public float TotalCost;

    public PathModel()
    {
        Success = false;
        Traversals = new List<PartTraversal>();
        TotalCost = 0f;
    }

    public struct PartTraversal
    {
        public string partId;
        public int entryExit; // where we entered this part
        public int exitExit;  // where we left this part

    }
}