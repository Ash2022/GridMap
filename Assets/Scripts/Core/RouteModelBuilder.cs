using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Builds the compact, stateful route model (no graph nodes/edges).</summary>
public static class RouteModelBuilder
{
    public static RouteModel Build(List<PlacedPartInstance> placedParts)
    {
        var model = new RouteModel();

        // -------- cell -> part lookup --------
        var cellToPart = new Dictionary<Vector2Int, PlacedPartInstance>();
        foreach (var p in placedParts)
            foreach (var cell in p.occupyingCells)
                cellToPart[cell] = p;

        // -------- helpers --------
        RouteModel.PartCache GetOrCreate(string pid, PlacedPartInstance src)
        {
            if (!model.parts.TryGetValue(pid, out var pc))
            {
                pc = new RouteModel.PartCache
                {
                    part = src,
                    allowed = new Dictionary<int, List<RouteModel.AllowedEdge>>(),
                    neighborByExit = new Dictionary<int, RouteModel.NeighborLink>()
                };
                model.parts.Add(pid, pc);
            }
            return pc;
        }

        PlacedPartInstance.ExitDetails GetExit(PlacedPartInstance part, int pin)
        {
            for (int i = 0; i < part.exits.Count; i++)
                if (part.exits[i].exitIndex == pin) return part.exits[i];
            return default;
        }

        bool IsOpposite(PlacedPartInstance.ExitDetails a, PlacedPartInstance.ExitDetails b)
        {
            return a.neighborCell == b.worldCell && b.neighborCell == a.worldCell;
        }

        void AddAllowed(RouteModel.PartCache pc,
                        int entryPin, int exitPin, float len,
                        int splineIdx = 0, float t0 = 0f, float t1 = 1f)
        {
            if (!pc.allowed.TryGetValue(entryPin, out var list))
            {
                list = new List<RouteModel.AllowedEdge>();
                pc.allowed[entryPin] = list;
            }
            list.Add(new RouteModel.AllowedEdge
            {
                exitPin = exitPin,
                internalLen = len,
                splineIndex = splineIdx,
                t0 = t0,
                t1 = t1
            });
        }

        // -------- 1) internal allowed transitions --------
        foreach (var p in placedParts)
        {
            var pc = GetOrCreate(p.partId, p);

            if (p.allowedPathsGroup != null && p.allowedPathsGroup.Count > 0)
            {
                foreach (var grp in p.allowedPathsGroup)
                {
                    if (grp?.allowedPaths == null) continue;
                    foreach (var ap in grp.allowedPaths)
                    {
                        // Your level data has no spline/t info – use defaults for now.
                        AddAllowed(pc, ap.entryConnectionId, ap.exitConnectionId, ap.length);
                    }
                }
            }
            else
            {
                // Fallback: permit all directed pairs (i -> j, i != j)
                var exits = p.exits;
                for (int i = 0; i < exits.Count; i++)
                    for (int j = 0; j < exits.Count; j++)
                        if (i != j)
                            AddAllowed(pc, exits[i].exitIndex, exits[j].exitIndex, 1f);
            }
        }

        // -------- 2) external neighbor links --------
        foreach (var p in placedParts)
        {
            var pc = model.parts[p.partId];

            foreach (var exit in p.exits)
            {
                if (!cellToPart.TryGetValue(exit.neighborCell, out var neighbor) || neighbor == p)
                    continue;

                bool found = false;
                PlacedPartInstance.ExitDetails nExit = default;
                for (int i = 0; i < neighbor.exits.Count; i++)
                {
                    var cand = neighbor.exits[i];
                    if (IsOpposite(exit, cand))
                    {
                        nExit = cand;
                        found = true;
                        break;
                    }
                }
                if (!found) continue;

                float extLen = Vector2Int.Distance(exit.worldCell, nExit.worldCell);

                pc.neighborByExit[exit.exitIndex] = new RouteModel.NeighborLink
                {
                    neighborPartId = neighbor.partId,
                    neighborPin = nExit.exitIndex,
                    externalLen = extLen
                };
            }
        }

        // -------- 3) debug dump --------
        var sb = new StringBuilder();
        sb.AppendLine("=== RouteModelBuilder DEBUG ===");
        sb.AppendLine($"Parts: {model.parts.Count}");
        foreach (var kv in model.parts)
        {
            var pc = kv.Value;
            sb.AppendLine($"Part {pc.part.partId}");
            foreach (var kv2 in pc.allowed)
            {
                int entry = kv2.Key;
                foreach (var a in kv2.Value)
                {
                    pc.neighborByExit.TryGetValue(a.exitPin, out var nb);
                    sb.AppendLine($"  {entry} -> {a.exitPin}  intLen={a.internalLen}  -> [{nb.neighborPartId}@{nb.neighborPin}] extLen={nb.externalLen}");
                }
            }
        }
        Debug.Log(sb.ToString());

        return model;
    }
}
