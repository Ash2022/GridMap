using System.Collections.Generic;


public enum TrainDir { Up, Right, Down, Left }
public enum GamePointType
{
    Station,
    Depot,   // was DropStation
    Train
}

/// <summary>
/// Where on the track this point is anchored.
/// You can use either exitPin or (splineIndex,t). For now we fill exitPin.
/// </summary>
[System.Serializable]
public struct Anchor
{
    public string partId;     // PlacedPartInstance.partId
    public int exitPin;    // which connection on that part we “snap” to (-1 if not used)
    public int splineIndex;// which spline inside the part (optional, -1 if N/A)
    public float t;          // 0..1 along that spline (optional)

    public static Anchor FromPin(string pid, int pin)
    {
        return new Anchor { partId = pid, exitPin = pin, splineIndex = -1, t = 0f };
    }
}

[System.Serializable]
public class GamePoint
{
    private static int NextID = 1;

    public int gridX;
    public int gridY;
    public GamePointType type;
    public int colorIndex;
    public int id;

    public PlacedPartInstance part;   // editor-side convenience
    public Anchor anchor;             // runtime-precise attachment

    // Only meaningful for stations
    public List<int> waitingPeople = new List<int>();

    // Only meaningful for trains
    public List<int> initialCarts = new List<int>();   // each int = color index

    public TrainDir direction = TrainDir.Left;   // NEW

    public GamePoint(PlacedPartInstance placedPartInstance, int x, int y,
                     GamePointType type, int colorIndex = 0, Anchor anchor = default)
    {
        part = placedPartInstance;
        gridX = x;
        gridY = y;
        this.type = type;
        this.colorIndex = colorIndex;
        this.anchor = anchor;
        this.id = NextID++;
    }

    public string Letter => type switch
    {
        GamePointType.Station => "S",
        GamePointType.Depot => "D",
        GamePointType.Train => "T",
        _ => "?"
    };

    public static void ResetIds(int startAt = 1) => NextID = startAt;
    public bool HasWaitingPeople => waitingPeople.Count > 0;
}

public class ScenarioModel
{
    public List<GamePoint> points = new List<GamePoint>();
}
