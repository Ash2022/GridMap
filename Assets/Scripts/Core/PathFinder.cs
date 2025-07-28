using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Dijkstra on RouteModel “states”. Returns your old PathModel.</summary>
public class PathFinder
{
    private RouteModel _model;

    public void Init(RouteModel model) => _model = model;


    

    public PathModel GetPath(PlacedPartInstance startPart, PlacedPartInstance endPart)
    {
        var log = new StringBuilder();
        log.AppendLine("=== RoutePathFinder ===");
        log.AppendLine($"Start: {startPart.partId}  End: {endPart.partId}");

        // --- build start state set (one per possible entry pin) ---
        var startStates = new List<RouteModel.State>();

        if (_model.parts.TryGetValue(startPart.partId, out var spc) && spc.allowed.Count > 0)
        {
            foreach (var entryPin in spc.allowed.Keys)
                startStates.Add(new RouteModel.State(startPart.partId, entryPin));
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
            // extract-min
            RouteModel.State u = default;
            float best = float.PositiveInfinity;
            int idx = -1;
            for (int i = 0; i < open.Count; i++)
            {
                float d = dist[open[i]];
                if (d < best) { best = d; u = open[i]; idx = i; }
            }
            open.RemoveAt(idx);
            if (!closed.Add(u)) continue;

            if (IsGoal(u))
            {
                foundGoals.Add((u, best));
                log.AppendLine($"Reached goal state: {u} cost={best}");
                // don't break – we might still find cheaper goal states
            }

            // expand
            if (!_model.parts.TryGetValue(u.partId, out var pc)) continue;
            if (!pc.allowed.TryGetValue(u.entryPin, out var internalList)) continue;

            for (int i = 0; i < internalList.Count; i++)
            {
                var a = internalList[i];

                if (!pc.neighborByExit.TryGetValue(a.exitPin, out var nb))
                    continue; // dangling exit, ignore

                var v = new RouteModel.State(nb.neighborPartId, nb.neighborPin);
                if (closed.Contains(v)) continue;

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
                    if (!open.Contains(v)) open.Add(v);
                }
            }
        }

        if (foundGoals.Count == 0)
        {
            log.AppendLine("No path found.");
            Debug.Log(log.ToString());
            return new PathModel(); // failed
        }

        // pick best goal
        foundGoals.Sort((a, b) => a.c.CompareTo(b.c));
        var goal = foundGoals[0].s;
        float totalCost = foundGoals[0].c;

        // reconstruct edge list
        var edgePath = ReconstructEdgePath(goal, prev);

        // dump candidates? (we stored only best)
        log.AppendLine("=== Chosen path ===");
        DumpEdges(edgePath, log);
        log.AppendLine("TotalCost: " + totalCost);
        Debug.Log(log.ToString());

        // build traversal output like before
        var traversals = BuildTraversals(edgePath);

        return new PathModel
        {
            Success = true,
            Traversals = traversals,
            TotalCost = totalCost
        };
    }

    // ---------- internals ----------

    public PathModel GetDirectedPath(
        PlacedPartInstance startPart,
        int startExitPin,
        PlacedPartInstance endPart,
        int endEntryPin = -1)
    {
        // --- build the single start‐state ---
        var startState = new RouteModel.State(startPart.partId, startExitPin);

        // goal predicate
        bool IsGoal(RouteModel.State s)
            => s.partId == endPart.partId
               && (endEntryPin < 0 || s.entryPin == endEntryPin);

        // Dijkstra containers
        var dist = new Dictionary<RouteModel.State, float> { [startState] = 0f };
        var prev = new Dictionary<RouteModel.State, PrevRec>();
        var open = new List<RouteModel.State> { startState };
        var closed = new HashSet<RouteModel.State>();
        var foundGoals = new List<(RouteModel.State s, float cost)>();

        // --- main loop ---
        while (open.Count > 0)
        {
            // extract‐min
            float best = float.PositiveInfinity;
            int bestIdx = 0;
            for (int i = 0; i < open.Count; i++)
            {
                float d = dist[open[i]];
                if (d < best) { best = d; bestIdx = i; }
            }
            var u = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (!closed.Add(u))
                continue;

            if (IsGoal(u))
                foundGoals.Add((u, best));

            // expand neighbors
            if (!_model.parts.TryGetValue(u.partId, out var pc))
                continue;
            if (!pc.allowed.TryGetValue(u.entryPin, out var internals))
                continue;

            foreach (var edge in internals)
            {
                if (!pc.neighborByExit.TryGetValue(edge.exitPin, out var nl))
                    continue;

                var v = new RouteModel.State(nl.neighborPartId, nl.neighborPin);
                if (closed.Contains(v))
                    continue;

                float cost = best + edge.internalLen + nl.externalLen;
                if (!dist.TryGetValue(v, out var old) || cost < old)
                {
                    dist[v] = cost;
                    prev[v] = new PrevRec { prevState = u, exitPin = edge.exitPin, edgeCost = edge.internalLen + nl.externalLen };
                    if (!open.Contains(v))
                        open.Add(v);
                }
            }
        }

        if (foundGoals.Count == 0)
            return new PathModel { Success = false };

        // pick best goal
        foundGoals.Sort((a, b) => a.cost.CompareTo(b.cost));
        var bestGoal = foundGoals[0].s;
        float totalCost = foundGoals[0].cost;

        // reconstruct
        var steps = ReconstructEdgePath(bestGoal, prev);
        var traversals = BuildTraversals(steps);

        return new PathModel
        {
            Success = true,
            TotalCost = totalCost,
            Traversals = traversals
        };
    }

    // (Keep your existing PrevRec, ReconstructEdgePath, BuildTraversals, etc., unchanged.)

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

            // 1) pick the sub‐spline index
            int splineIndex = 0;
            if (placed.allowedPathsGroup != null && placed.allowedPathsGroup.Count > 0)
            {
                var grp = placed.allowedPathsGroup[0];
                int idx = grp.allowedPaths.FindIndex(ap =>
                    ap.entryConnectionId == entryPin &&
                    ap.exitConnectionId == exitPin);
                if (idx >= 0 && idx < placed.splines.Count) splineIndex = idx;
            }

            // 2) compute tStart/tEnd
            bool simple = placed.exits.Count <= 2;
            float tStart, tEnd;

            if (simple)
            {
                if (entryPin != -1 && exitPin != -1)
                {
                    // middle simple part
                    tStart = 0f;
                    tEnd = 1f;
                }
                else
                {
                    // first or last simple part: clamp at center
                    float te = ExitT(placed, entryPin);
                    float tx = ExitT(placed, exitPin);
                    tStart = Mathf.Min(te, tx);
                    tEnd = Mathf.Max(te, tx);
                    if (Mathf.Approximately(tStart, tEnd))
                    {
                        const float eps = 0.001f;
                        if (tEnd + eps <= 1f) tEnd += eps;
                        else tStart = Mathf.Max(0f, tStart - eps);
                    }
                }
            }
            else
            {
                // multi‐exit always full spline
                tStart = 0f;
                tEnd = 1f;
            }

            result.Add(new PathModel.PartTraversal
            {
                partId = curPartId,
                entryExit = entryPin,
                exitExit = exitPin,
                splineIndex = splineIndex,
                tStart = tStart,
                tEnd = tEnd
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
                exitExit = -1,
                splineIndex = 0,
                tStart = 0.5f,
                tEnd = 0.5f
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
