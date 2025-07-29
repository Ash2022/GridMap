using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrainMover : MonoBehaviour
{
    public float moveSpeed = 5f; // Units per second
    public bool isMoving { get; private set; }

    private Coroutine moveCoroutine;

    /// <summary>
    /// Starts moving the train along a given world-space path.
    /// </summary>
    public void MoveAlongPath(List<Vector3> worldPoints)
    {
        if (isMoving)
            return;

        if (worldPoints == null || worldPoints.Count < 2)
        {
            Debug.LogWarning("TrainMover: Invalid path");
            return;
        }

        moveCoroutine = StartCoroutine(MoveRoutine(worldPoints));
    }

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
