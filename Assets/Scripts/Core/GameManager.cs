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

        if (Input.GetKeyDown(KeyCode.R))
            LevelVisualizer.Instance.GenerateDynamic();
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

            int entryExitID = _lastPath.Traversals.Last().entryExit;

            Debug.Log("Entering from ExitID: " +  entryExitID);

            var newDirection = GetTrainDirectionAfterEntering(target.part, entryExitID);
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

    public static TrainDir GetTrainDirectionAfterEntering(PlacedPartInstance part, int enteredExitPin)
    {
        if (part == null || part.exits == null || part.exits.Count != 2)
        {
            Debug.LogError("Part must have exactly 2 exits.");
            return TrainDir.Right;
        }

        // Normalize rotation to 0, 90, 180, 270
        int rot = ((part.rotation % 360) + 360) % 360;

        // Determine direction based on rotation and which exit we entered from
        TrainDir facingDir = TrainDir.Right;

        if (rot == 0)
            facingDir = enteredExitPin == 0 ? TrainDir.Down : TrainDir.Up;
        else if (rot == 90)
            facingDir = enteredExitPin == 0 ? TrainDir.Left : TrainDir.Right;
        else if (rot == 180)
            facingDir = enteredExitPin == 0 ? TrainDir.Up : TrainDir.Down;
        else if (rot == 270)
            facingDir = enteredExitPin == 0 ? TrainDir.Right : TrainDir.Left;
        else
        {
            Debug.LogError("Unexpected rotation: " + part.rotation);
        }

        Debug.Log($"[TrainDirCalc] EnteredExitPin={enteredExitPin}, Rotation={part.rotation} → FinalDir={facingDir}");

        return facingDir;
    }




}
