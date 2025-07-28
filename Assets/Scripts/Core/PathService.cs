using System.Linq;

public static class PathService
{
    /// <summary>
    /// Find a route from the first train in the level to the given target point.
    /// </summary>
    public static PathModel FindPathTo(LevelData level, GamePoint target)
    {
        // 1) first train
        var trainPoint = level.gameData.points
                             .FirstOrDefault(p => p.type == GamePointType.Train);
        if (trainPoint == null)
            return new PathModel { Success = false };

        var startPart = trainPoint.part;

        // 2) train→exitPin
        // compute the “back” direction (so facing Up = use the Down pin, etc.)
        int backDir = ((int)trainPoint.direction + 2) % 4;
        int startExitPin = startPart.exits
            .Where(e => e.direction == backDir)
            .Select(e => e.exitIndex)
            .DefaultIfEmpty().First();

        // 3) target part & entryPin
        var endPart = target.part;
        int endEntryPin = target.anchor.exitPin >= 0
            ? target.anchor.exitPin
            : -1;

        // 4) run it
        var pf = new PathFinder();
        pf.Init(level.routeModelData);
        return pf.GetDirectedPath(startPart, startExitPin, endPart, endEntryPin);
    }
}
