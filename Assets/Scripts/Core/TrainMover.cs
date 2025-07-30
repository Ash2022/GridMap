using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class TrainMover : MonoBehaviour
{
    public float moveSpeed = 5f; // Units per second
    public bool isMoving { get; private set; }

    [SerializeField] private List<GameObject> carts;
    float cellSize;

    private Coroutine moveCoroutine;

    /// <summary>
    /// Starts moving the train along a given world-space path.
    /// </summary>
    public void MoveAlongPath(List<Vector3> worldPoints, List<GameObject> currCarts,float currCellSize)
    {
        carts = currCarts;
        cellSize = currCellSize;

        if (isMoving)
            return;

        if (worldPoints == null || worldPoints.Count < 2)
        {
            Debug.LogWarning("TrainMover: Invalid path");
            return;
        }

        moveCoroutine = StartCoroutine(MoveRoutine(worldPoints));
    }
    /*
    private IEnumerator MoveRoutine(List<Vector3> points)
    {
        isMoving = true;

        for (int i = 1; i < points.Count; i++)
        {
            Vector3 start = points[i - 1];
            Vector3 end = points[i];
            float dist = Vector3.Distance(start, end);
            float duration = dist / moveSpeed;
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                t = Mathf.Min(t, 1f); // Clamp to avoid overshoot

                transform.position = Vector3.Lerp(start, end, t);

                Vector3 dir = end - start;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(Vector3.forward, dir.normalized);
                    transform.rotation = targetRot * Quaternion.Euler(0, 0, -90f);
                }

                yield return null;
            }
        }

        isMoving = false;
        moveCoroutine = null;
    }
    */

    private IEnumerator MoveRoutine(List<Vector3> points)
    {
        isMoving = true;

        float cartSize = cellSize / 3f;
        float gap = cellSize / 10f;
        float headBack = cellSize * 0.5f;
        float firstOffset = headBack + gap + cartSize * 0.5f;

        float[] cartOffsets = new float[carts.Count];
        for (int i = 0; i < carts.Count; i++)
        {
            cartOffsets[i] = firstOffset + (cartSize + gap) * i;
        }

        float totalDist = 0f;
        List<float> segmentLengths = new List<float>();
        for (int i = 1; i < points.Count; i++)
        {
            float dist = Vector3.Distance(points[i - 1], points[i]);
            segmentLengths.Add(dist);
            totalDist += dist;
        }

        float trainDistance = 0f;
        int segIndex = 0;
        float t = 0f;

        Vector3 currStart = points[0];
        Vector3 currEnd = points[1];
        float currSegLen = segmentLengths[0];

        while (segIndex < points.Count - 1)
        {
            t += Time.deltaTime * moveSpeed / currSegLen;
            t = Mathf.Min(t, 1f);

            Vector3 headPos = Vector3.Lerp(currStart, currEnd, t);
            transform.position = headPos;

            Vector3 dir = currEnd - currStart;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(Vector3.forward, dir.normalized);
                transform.rotation = targetRot * Quaternion.Euler(0, 0, -90f);
            }

            trainDistance += Time.deltaTime * moveSpeed;

            // Update carts
            for (int i = 0; i < carts.Count; i++)
            {
                float cartDist = trainDistance - cartOffsets[i];
                if (cartDist < 0f) continue; // Don't show yet

                Vector3 cartPos;
                Quaternion cartRot;
                GetPositionAndRotationAtDistance(points, segmentLengths, cartDist, out cartPos, out cartRot);

                carts[i].transform.position = cartPos;
                carts[i].transform.rotation = cartRot * Quaternion.Euler(0, 0, -90f);
            }

            if (t >= 1f)
            {
                segIndex++;
                if (segIndex < points.Count - 1)
                {
                    currStart = points[segIndex];
                    currEnd = points[segIndex + 1];
                    currSegLen = segmentLengths[segIndex];
                    t = 0f;
                }
            }

            yield return null;
        }

        isMoving = false;
        moveCoroutine = null;
    }

    private void GetPositionAndRotationAtDistance(List<Vector3> points, List<float> lengths, float targetDist, out Vector3 pos, out Quaternion rot)
    {
        float distSoFar = 0f;

        for (int i = 0; i < lengths.Count; i++)
        {
            float segLen = lengths[i];
            if (distSoFar + segLen >= targetDist)
            {
                float t = (targetDist - distSoFar) / segLen;
                Vector3 a = points[i];
                Vector3 b = points[i + 1];
                pos = Vector3.Lerp(a, b, t);
                Vector3 dir = b - a;
                rot = Quaternion.LookRotation(Vector3.forward, dir.normalized);
                return;
            }
            distSoFar += segLen;
        }

        // If past end of path
        pos = points[^1];
        rot = Quaternion.identity;
    }


    public void Stop()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }
        isMoving = false;
    }
}
