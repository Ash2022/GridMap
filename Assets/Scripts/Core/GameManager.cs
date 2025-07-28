using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Tooltip("Drag your LevelData asset or fill at runtime.")]
    public LevelData level;

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
        // grab the model
        GamePoint target = stationView.GetComponent<StationView>()
                                      .GetType()
                                      .GetField("_pointModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                      ?.GetValue(stationView) as GamePoint;

        if (target == null)
        {
            Debug.LogError("StationView has no GamePoint assigned!");
            return;
        }

        // Find the path from the first train to this station:
        PathModel path = PathService.FindPathTo(level, target);

        if (!path.Success)
        {
            Debug.LogWarning("No path found to station " + target.id);
            return;
        }

        // TODO: hand the PathModel to whatever system moves your train.
        Debug.Log($"Path found with {path.Traversals.Count} steps, cost={path.TotalCost}");

        LevelVisualizer.Instance.DrawGlobalSplinePath(path);

    }
}
