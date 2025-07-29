using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Tooltip("Drag your LevelData asset or fill at runtime.")]
    public LevelData level;
    private GamePoint _lastTarget;
    private PathModel _lastPath;

    public List<TrainController> trains = new List<TrainController>();
    public TrainController selectedTrain;

    

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
            // Check for station
            var stationView = hit.collider.GetComponent<StationView>();
            if (stationView != null)
            {
                OnStationClicked(stationView);
                return;
            }

            // Check for train click
            var trainClickView = hit.collider.GetComponent<TrainClickView>();
            if (trainClickView != null)
            {
                trainClickView.OnClickedByRaycast(); // this method should call back into TrainController
                return;
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

        if (selectedTrain == null)
        {
            Debug.LogWarning("No train selected.");
            return;
        }

        // Second click on same station → move the train
        if (_lastTarget == target && _lastPath?.Success == true)
        {
            Debug.Log("Second click: moving train");

            var worldPoints = LevelVisualizer.Instance.ExtractWorldPointsFromPath(_lastPath);
            selectedTrain.MoveAlongPath(worldPoints);

            var newDirection = GetTrainDirectionAfterEntering(target.part, target.anchor.exitPin);
            target.direction = newDirection;

            var trainPoint = selectedTrain.CurrentPointModel;

            trainPoint.direction = newDirection;
            trainPoint.gridX = target.gridX;
            trainPoint.gridY = target.gridY;
            trainPoint.anchor = target.anchor;
            trainPoint.part = target.part;

            _lastTarget = null;
            _lastPath = null;
            return;
        }

        // First click → compute path from the selected train's current point
        var startPoint = selectedTrain.CurrentPointModel;
        PathModel path = PathService.FindPath(level, startPoint, target);

        if (!path.Success)
        {
            Debug.LogWarning("No path found to station " + target.id);
            return;
        }

        Debug.Log($"Path found with {path.Traversals.Count} steps, cost={path.TotalCost}");
        LevelVisualizer.Instance.DrawGlobalSplinePath(path, new List<Vector3>());

        _lastTarget = target;
        _lastPath = path;
    }

    internal void SelectTrain(TrainController trainController)
    {
        selectedTrain = trainController;
    }

    private void UpdateTrainDirectionFromTraversal(LevelData level, TrainController selectedTrain, PathModel path)
    {
        if (path.Traversals.Count < 2)
        {
            Debug.Log("Not enough traversal steps to determine direction.");
            return;
        }

        var prevTraversal = path.Traversals[0];
        var currTraversal = path.Traversals[1];

        var prevPart = level.parts.FirstOrDefault(p => p.partId == prevTraversal.partId);
        var currPart = level.parts.FirstOrDefault(p => p.partId == currTraversal.partId);

        if (prevPart == null || currPart == null)
        {
            Debug.Log("Could not resolve part positions for direction calculation.");
            return;
        }

        int dx = currPart.position.x - prevPart.position.x;
        int dy = currPart.position.y - prevPart.position.y;

        if (dx == 1 && dy == 0) selectedTrain.direction = TrainDir.Right;
        else if (dx == -1 && dy == 0) selectedTrain.direction = TrainDir.Left;
        else if (dx == 0 && dy == 1) selectedTrain.direction = TrainDir.Up;
        else if (dx == 0 && dy == -1) selectedTrain.direction = TrainDir.Down;
        else Debug.Log($"Unhandled direction from delta ({dx},{dy}) → falling back");
    }


    public static TrainDir GetTrainDirectionAfterEntering(PlacedPartInstance part, int enteredExitPin)
    {
        if (part == null || part.exits == null || part.exits.Count < 2)
        {
            Debug.LogError("Invalid part or exit configuration.");
            return TrainDir.Right; // fallback
        }

        // 1. Get the other exit (the one we're now facing)
        var facingExit = part.exits.FirstOrDefault(e => e.exitIndex != enteredExitPin);
        if (facingExit == null)
        {
            Debug.LogError("Could not find the opposite exit.");
            return TrainDir.Right;
        }

        // 2. Get its local direction (0=Up, 1=Right, 2=Down, 3=Left)
        int localDir = facingExit.direction;

        // 3. Rotate it by part.rotation
        int worldDir = (localDir + (part.rotation / 90)) % 4;

        // 4. Return the final facing direction
        return (TrainDir)worldDir;
    }


}
