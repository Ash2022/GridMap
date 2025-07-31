using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Dijkstra on RouteModel “states”. Returns your old PathModel.</summary>
public class PathFinder
{
    private RouteModel _model;

    public void Init(RouteModel model) => _model = model;




    public PathModel GetPath(PlacedPartInstance startPart, PlacedPartInstance endPart, int startExitPin = -999)
    {
        var log = new StringBuilder();
        log.AppendLine("=== RoutePathFinder ===");
        log.AppendLine($"Start: {startPart.partId}  End: {endPart.partId}");

        // --- build start state set (try all entry pins for startPart) ---
        var startStates = new List<RouteModel.State>();

        if (startExitPin != -999)
        {
            // Flip the start exit to determine the proper entry pin
            int requiredEntryPin = startExitPin == 0 ? 1 : 0;
            startStates.Add(new RouteModel.State(startPart.partId, requiredEntryPin));
        }
        else if (startPart.exits != null && startPart.exits.Count > 0)
        {
            foreach (var ex in startPart.exits)
                startStates.Add(new RouteModel.State(startPart.partId, ex.exitIndex));
        }
        else
        {
            // fallback synthetic entry
            startStates.Add(new RouteModel.State(startPart.partId, -1));
        }
        // goal predicate
        bool IsGoal(RouteModel.State s) => s.partId == endPart.partId;

        // Dijkstra containers
        var dist = new Dictionary<RouteModel.State, float>();
        var prev = new Dictionary<RouteModel.State, PrevRec>();
        var open = new List<RouteModel.State>();
        var closed = new HashSet<RouteModel.State>();

        foreach (var s in startStates)
        {
            dist[s] = 0f;
            open.Add(s);
        }

        var foundGoals = new List<(RouteModel.State s, float c)>();

        // ---- main loop ----
        while (open.Count > 0)
        {
            // ─── Extract-Min ─────────────────────────────────────────────
            RouteModel.State u = default;
            float best = float.PositiveInfinity;
            int idx = -1;
            for (int i = 0; i < open.Count; i++)
            {
                float d = dist[open[i]];
                if (d < best)
                {
                    best = d;
                    u = open[i];
                    idx = i;
                }
            }

            open.RemoveAt(idx);
            if (!closed.Add(u))
            {
                log.AppendLine($"[Skip] State already closed: {u}");
                continue;
            }

            log.AppendLine($"[Visit] State={u}  CostSoFar={best}");

            // ─── Goal Check ─────────────────────────────────────────────
            if (IsGoal(u))
            {
                foundGoals.Add((u, best));
                log.AppendLine($"✅ Reached goal state: {u}  totalCost={best}");
                continue;
            }

            // ─── Expansion ─────────────────────────────────────────────
            if (!_model.parts.TryGetValue(u.partId, out var pc))
            {
                log.AppendLine($"❌ Part not found: {u.partId}");
                continue;
            }

            if (!pc.allowed.TryGetValue(u.entryPin, out var internalList))
            {
                log.AppendLine($"❌ No allowed paths from entryPin={u.entryPin} on part {u.partId}");
                continue;
            }

            if (internalList.Count == 0)
            {
                log.AppendLine($"⚠️ No internal paths from state: {u}");
                continue;
            }

            for (int i = 0; i < internalList.Count; i++)
            {
                var a = internalList[i];

                if (!pc.neighborByExit.TryGetValue(a.exitPin, out var nb))
                {
                    log.AppendLine($"❌ ExitPin {a.exitPin} from part {u.partId} has no neighbor. Dangling?");
                    continue;
                }

                var v = new RouteModel.State(nb.neighborPartId, nb.neighborPin);
                if (closed.Contains(v))
                {
                    log.AppendLine($"[Skip] Neighbor state already closed: {v}");
                    continue;
                }

                float edgeCost = a.internalLen + nb.externalLen;
                float nd = best + edgeCost;

                if (!dist.TryGetValue(v, out var old) || nd < old)
                {
                    dist[v] = nd;
                    prev[v] = new PrevRec
                    {
                        prevState = u,
                        exitPin = a.exitPin,
                        edgeCost = edgeCost
                    };

                    if (!open.Contains(v))
                    {
                        open.Add(v);
                        log.AppendLine($"→ Added to open: {v} via ExitPin {a.exitPin} (cost={edgeCost}, total={nd})");
                    }
                    else
                    {
                        log.AppendLine($"↻ Updated cost for {v} to {nd} via ExitPin {a.exitPin}");
                    }
                }
            }
        }

        if (foundGoals.Count == 0)
        {
            log.AppendLine("No path found.");
            Debug.Log(log.ToString());
            return new PathModel(); // failed
        }

        // filter based on startExitPin if specified
        if (startExitPin != -999)
        {
            foundGoals = foundGoals
                .Where(g =>
                {
                    var edgePath = ReconstructEdgePath(g.s, prev);
                    return edgePath.Count > 0 && edgePath[0].exitPin == startExitPin;
                })
                .ToList();

            if (foundGoals.Count == 0)
            {
                log.AppendLine($"No path matched the required startExitPin={startExitPin}.");
                Debug.Log(log.ToString());
                return new PathModel(); // failed
            }
        }

        // pick best goal
        foundGoals.Sort((a, b) => a.c.CompareTo(b.c));
        var goal = foundGoals[0].s;
        float totalCost = foundGoals[0].c;

        // reconstruct edge list
        var edgePathFinal = ReconstructEdgePath(goal, prev);

        // dump chosen path
        log.AppendLine("=== Chosen path ===");
        DumpEdges(edgePathFinal, log);
        log.AppendLine("TotalCost: " + totalCost);
        Debug.Log(log.ToString());

        // build traversal output
        var traversals = BuildTraversals(edgePathFinal);

        return new PathModel
        {
            Success = true,
            Traversals = traversals,
            TotalCost = totalCost
        };
    }



private struct PrevRec
    {
        public RouteModel.State prevState;
        public int exitPin;
        public float edgeCost;
    }

    private List<EdgeStep> ReconstructEdgePath(RouteModel.State goal,
                                               Dictionary<RouteModel.State, PrevRec> prev)
    {
        var list = new List<EdgeStep>();
        var cur = goal;

        while (prev.TryGetValue(cur, out var pr))
        {
            list.Add(new EdgeStep
            {
                from = pr.prevState,
                to = cur,
                exitPin = pr.exitPin,
                cost = pr.edgeCost
            });
            cur = pr.prevState;
        }
        list.Reverse();
        return list;
    }

    private void DumpEdges(List<EdgeStep> steps, StringBuilder sb)
    {
        float sum = 0f;
        for (int i = 0; i < steps.Count; i++)
        {
            var e = steps[i];
            sum += e.cost;
            sb.AppendLine($"  {e.from.partId}@in{e.from.entryPin} --[{e.exitPin}]--> {e.to.partId}@in{e.to.entryPin}  cost={e.cost}");
        }
        sb.AppendLine($"  (sum={sum})");
    }

    private List<PathModel.PartTraversal> BuildTraversals(List<EdgeStep> steps)
    {
        var result = new List<PathModel.PartTraversal>();
        if (steps == null || steps.Count == 0) return result;

        // local inline: how far along a simple spline we enter/exit
        float ExitT(PlacedPartInstance part, int exitIndex)
        {
            if (exitIndex < 0) return 0.5f;
            // find the exit detail
            var ed = part.exits.FirstOrDefault(e => e.exitIndex == exitIndex);
            // 0=Up,1=Right => start (0f), 2=Down,3=Left => end (1f)
            return (ed.direction == 0 || ed.direction == 1) ? 0f : 1f;
        }

        int i = 0;
        while (i < steps.Count)
        {
            string curPartId = steps[i].from.partId;
            int entryPin = steps[i].from.entryPin;
            int exitPin = -1;

            // consume edges for this part
            while (i < steps.Count && steps[i].from.partId == curPartId)
            {
                exitPin = steps[i].exitPin;
                if (steps[i].to.partId != curPartId)
                {
                    i++;
                    break;
                }
                i++;
            }

            // grab the placed instance and its route‐cache
            var pc = _model.parts[curPartId];
            var placed = pc.part;

            result.Add(new PathModel.PartTraversal
            {
                partId = curPartId,
                entryExit = entryPin,
                exitExit = exitPin                
            });
        }

        // ensure goal part is present
        var goal = steps[steps.Count - 1].to;
        if (result.Count == 0 || result[^1].partId != goal.partId)
        {
            result.Add(new PathModel.PartTraversal
            {
                partId = goal.partId,
                entryExit = goal.entryPin,
                exitExit = -1                
            });
        }
        else
        {
            var last = result[^1];
            last.exitExit = -1;
            result[^1] = last;
        }

        // synthetic entry on first
        if (result.Count > 0)
        {
            var first = result[0];
            first.entryExit = -1;
            result[0] = first;
        }

        return result;
    }




    private struct EdgeStep
    {
        public RouteModel.State from;
        public RouteModel.State to;
        public int exitPin;
        public float cost;
    }
}
