using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// This is your PARTS CATALOG. It defines all parts the editor/game can use.

[Serializable]
public class ModelData
{
    public List<TrackPart> parts;
}

// Definition of a single part type (catalog entry)

// Represents a connection point on the part
[Serializable]
public class PartConnection
{
    public int id;
    public int direction;
    public int[] gridOffset;
}

// Represents a single allowed path direction in a group
[Serializable]
public class AllowedPath
{
    public int entryConnectionId;
    public int exitConnectionId;
    public float length;
}

// Represents a group of allowed paths (e.g. 0→1 and 1→0)
[Serializable]
public class AllowedPathGroup
{
    public List<AllowedPath> allowedPaths = new List<AllowedPath>();
}

// The main track part class
[Serializable]
public class TrackPart
{
    public string partName;
    public int gridWidth;
    public int gridHeight;
    public string displaySprite;

    public List<PartConnection> connections = new List<PartConnection>();

    // Grouped allowed paths, each group can include bidirectional (or multi-directional) paths
    public List<AllowedPathGroup> allowedPathsGroups = new List<AllowedPathGroup>();

    // Spline templates: index matches allowedPaths group index
    public List<List<float[]>> originalSplineTemplates = new List<List<float[]>>();
    public List<List<float[]>> splineTemplates = new List<List<float[]>>();

    public List<Vector2Int> solidCells = new List<Vector2Int>();

    // Converts splineTemplates to Vector2 format for usage
    public List<List<Vector2>> GetSplinesAsVector2()
    {
        var result = new List<List<Vector2>>();
        foreach (var spline in splineTemplates)
        {
            var list = new List<Vector2>();
            foreach (var arr in spline)
                list.Add(new Vector2(arr[0], arr[1]));
            result.Add(list);
        }
        return result;
    }

    

    // Sets the modified splines from Vector2 format
    public void SetSplinesFromVector2(List<List<Vector2>> splines)
    {
        splineTemplates = new List<List<float[]>>();
        foreach (var spline in splines)
        {
            var list = new List<float[]>();
            foreach (var v in spline)
            {
                list.Add(new float[] { v.x, v.y });
            }
            splineTemplates.Add(list);
        }
    }

}