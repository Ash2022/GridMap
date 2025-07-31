using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utils 
{
    public static List<Vector3> GenerateSmoothSpline(List<Vector3> keyPoints, int samplesPerSegment = 10)
    {
        var result = new List<Vector3>();
        if (keyPoints == null || keyPoints.Count < 2)
            return result;

        for (int i = 0; i < keyPoints.Count - 1; i++)
        {
            Vector3 p0 = keyPoints[i];
            Vector3 p1 = keyPoints[i + 1];

            // Infer tangents
            Vector3 dir = (p1 - p0).normalized;
            Vector3 forward = (i > 0) ? (keyPoints[i + 1] - keyPoints[i - 1]).normalized : dir;
            Vector3 nextDir = (i < keyPoints.Count - 2) ? (keyPoints[i + 2] - p0).normalized : dir;

            // Tangents for cubic Bezier
            float distance = Vector3.Distance(p0, p1);
            Vector3 t0 = dir * distance * 0.25f;
            Vector3 t1 = -dir * distance * 0.25f;

            Vector3 control0 = p0 + t0;
            Vector3 control1 = p1 + t1;

            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = j / (float)samplesPerSegment;
                Vector3 point = EvaluateCubicBezier(p0, control0, control1, p1, t);
                result.Add(point);
            }
        }

        // Ensure final point is included
        result.Add(keyPoints[keyPoints.Count - 1]);
        return result;
    }

    private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 c0, Vector3 c1, Vector3 p1, float t)
    {
        float u = 1f - t;
        return
            u * u * u * p0 +
            3f * u * u * t * c0 +
            3f * u * t * t * c1 +
            t * t * t * p1;
    }
}
