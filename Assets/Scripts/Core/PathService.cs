using System.Linq;
using UnityEngine;

public static class PathService
{
    
    public static PathModel FindPath(LevelData level, GamePoint start, GamePoint target)
    {
        // 1) Resolve the start part from the anchor
        var startPart = level.parts.FirstOrDefault(p => p.partId == start.anchor.partId);
        if (startPart == null)
        {
            Debug.LogError("Train anchor.partId not found in level.parts: " + start.anchor.partId);
            return new PathModel { Success = false };
        }

        // 2) Resolve end part from target anchor.partId
        var endPart = level.parts.FirstOrDefault(p => p.partId == target.anchor.partId);
        if (endPart == null)
        {
            Debug.LogError("Target anchor.partId not found in level.parts: " + target.anchor.partId);
            return new PathModel { Success = false };
        }

        // 3) Determine direction
        int trainDir = (int)start.direction % 4;
        int moveDir = trainDir;
        //int trainBackDir = (trainDir) % 4;

        var log = new System.Text.StringBuilder();
        log.AppendLine($"TrainDir: {trainDir} → MoveDir: {moveDir}");
        log.AppendLine($"Resolved Start PartId: {startPart.partId}, rotation: {startPart.rotation}°");

        // 4) Find the correct exit pin based on the train's direction
        int startExitPin = -1;
        foreach (var exit in startPart.exits)
        {
            int worldDir = exit.direction;
            log.AppendLine($"  ExitID: {exit.exitIndex}  WorldDir: {worldDir}");

            if (worldDir == moveDir)
            {
                startExitPin = exit.exitIndex;
                log.AppendLine($"  ✅ Match found: ExitID {startExitPin}");
                break;
            }
        }

        if (startExitPin == -1)
        {
            log.AppendLine("❌ No matching exit found. Using first available.");
            startExitPin = startPart.exits.FirstOrDefault()?.exitIndex ?? 0;
        }

        // 5) Determine end entry pin
        int endEntryPin = target.anchor.exitPin >= 0 ? target.anchor.exitPin : -1;

        UnityEngine.Debug.Log(log.ToString());

        // 6) Run pathfinder
        var pf = new PathFinder();
        pf.Init(level.routeModelData);
        return pf.GetPath(startPart, endPart, startExitPin);
        //return pf.GetPath(startPart, endPart);
    }

}
