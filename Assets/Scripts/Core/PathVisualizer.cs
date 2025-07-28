using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class PathVisualizer
{
    // Draws path in the editor

    /*
    public void DrawPath(List<PathSegment> segments)
    {
        if (segments == null || segments.Count == 0) return;

        Handles.color = Color.red;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg.part == null) continue;
            Vector2 startPos = seg.part.GetPositionOnSpline(seg.tStart);
            Vector2 endPos = seg.part.GetPositionOnSpline(seg.tEnd);

            Handles.DrawLine(startPos, endPos, 4f);
        }
    }*/
}