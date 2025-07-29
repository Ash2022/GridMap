using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Tooltip("Drag your LevelData asset or fill at runtime.")]
    public LevelData level;
    private GamePoint _lastTarget;
    private PathModel _lastPath;
    private void Awake()
    {
        if (Instance != null && Instance != this)
            Destroy(gameObject);
        else
            Instance = this;
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            HandleClick();
    }

    private void HandleClick()
    {
        var cam = Camera.main;
        var ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out var hit))
        {
            var stationView = hit.collider.GetComponent<StationView>();
            if (stationView != null)
            {
                OnStationClicked(stationView);
            }
        }
    }

    private void OnStationClicked(StationView stationView)
    {
        var target = stationView.GetType()
                                .GetField("_pointModel", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?.GetValue(stationView) as GamePoint;

        if (target == null)
        {
            Debug.LogError("StationView has no GamePoint assigned!");
            return;
        }

        // Second click on same station → move the train
        if (_lastTarget == target && _lastPath?.Success == true)
        {
            Debug.Log("Second click: moving train");

            var worldPoints = LevelVisualizer.Instance.ExtractWorldPointsFromPath(_lastPath);

            LevelVisualizer.Instance.trainMover.MoveAlongPath(worldPoints);

            // Reset to avoid re-triggering movement repeatedly
            _lastTarget = null;
            _lastPath = null;
            return;
        }

        // First click → compute path
        PathModel path = PathService.FindPathTo(level, target);

        if (!path.Success)
        {
            Debug.LogWarning("No path found to station " + target.id);
            return;
        }

        Debug.Log($"Path found with {path.Traversals.Count} steps, cost={path.TotalCost}");

        LevelVisualizer.Instance.DrawGlobalSplinePath(path, new List<Vector3>());

        // Store for next click
        _lastTarget = target;
        _lastPath = path;
    }
}
