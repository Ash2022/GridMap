// Editor-side adapter over RailSimCore.SimWorld
// Place in an Editor assembly or a shared assembly (no UnityEditor refs)

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailSimCore; // SimWorld, TrackDto, TrackSegmentKey, Polyline, AdvanceResult, SimpleTrainSim

public sealed class SimController
{
    private readonly SimWorld _world = new SimWorld();
    private TrackDto _track = new TrackDto();
    private readonly Dictionary<int, int> _pointIdToTrainId = new Dictionary<int, int>(); // GamePoint.id -> trainId

    public float DefaultMetersPerTick = 0.25f;

    public struct GridContext
    {
        public Vector2 worldOriginBL; // world coords of the bottom-left of [minX,minY]
        public int minX, minY, gridH;
        public float cellSize;
    }

    public void Reset()
    {
        _pointIdToTrainId.Clear();
        _track = new TrackDto();
        _world.LoadTrack(_track); // empty
    }

    /// <summary>
    /// Build TrackDto from runtime world-space splines (Game side).
    /// Uses PlacedPartInstance.worldSplines (Vector3 in game world units, XY plane).
    /// </summary>
    public void BuildTrackDtoFromWorld(LevelData level, System.Func<PlacedPartInstance, bool> isConsumable = null)
    {
        if (level == null || level.parts == null)
            throw new System.ArgumentNullException("level or level.parts is null");

        var segments = new System.Collections.Generic.Dictionary<TrackSegmentKey, Polyline>();
        var consumable = new System.Collections.Generic.HashSet<TrackSegmentKey>();

        for (int i = 0; i < level.parts.Count; i++)
        {
            var part = level.parts[i];
            if (part == null || part.worldSplines == null || part.worldSplines.Count == 0)
                continue;

            for (int s = 0; s < part.worldSplines.Count; s++)
            {
                var pts = part.worldSplines[s];
                if (pts == null || pts.Count < 2) continue;

                // Normalize Z to 0 (sim is 2D XY)
                var worldPts = new System.Collections.Generic.List<UnityEngine.Vector3>(pts.Count);
                for (int k = 0; k < pts.Count; k++)
                {
                    var v = pts[k];
                    worldPts.Add(new UnityEngine.Vector3(v.x, v.y, 0f));
                }

                var key = new TrackSegmentKey
                {
                    PartId = string.IsNullOrEmpty(part.partId) ? ("part_" + i) : part.partId,
                    SplineIdx = s,
                    T0 = 0f,
                    T1 = 1f
                };

                segments[key] = new Polyline(worldPts);

                if (isConsumable != null && isConsumable(part))
                    consumable.Add(key);
            }
        }

        _track = new TrackDto { Segments = segments, Consumable = consumable };
        _world.LoadTrack(_track);
    }

    /// <summary>
    /// Build TrackDto from LevelData placed parts. Assumes each PlacedPartInstance has worldSplines (world-space).
    /// If you need consumable segments, pass a predicate; otherwise all non-consumable.
    /// </summary>
    public void BuildTrackDtoFromBaked(LevelData level, GridContext g, Func<PlacedPartInstance, bool> isConsumable = null)
    {
        if (level == null || level.parts == null)
            throw new ArgumentNullException("level or level.parts is null");

        var segments = new Dictionary<TrackSegmentKey, Polyline>();
        var consumable = new HashSet<TrackSegmentKey>();

        for (int i = 0; i < level.parts.Count; i++)
        {
            var part = level.parts[i];
            if (part == null || part.bakedSplines == null) continue;

            for (int s = 0; s < part.bakedSplines.Count; s++)
            {
                var baked = part.bakedSplines[s];
                var gridPts = baked.gridPts;           // << use grid-space baked points
                if (gridPts == null || gridPts.Count < 2) continue;

                // Convert each baked grid point -> world point
                var worldPts = new List<Vector3>(gridPts.Count);
                for (int k = 0; k < gridPts.Count; k++)
                {
                    var gp = gridPts[k]; // in cell units (continuous)
                    float wx = g.worldOriginBL.x + (gp.x - g.minX) * g.cellSize;
                    float wy = g.worldOriginBL.y + (g.gridH - gp.y) * g.cellSize; // Y flip
                    worldPts.Add(new Vector3(wx, wy, 0f));
                }

                var key = new TrackSegmentKey
                {
                    PartId = part.partId,
                    SplineIdx = s,
                    T0 = 0f,
                    T1 = 1f
                };
                segments[key] = new Polyline(worldPts);

                if (isConsumable != null && isConsumable(part))
                    consumable.Add(key);
            }
        }

        _track = new TrackDto
        {
            Segments = segments,
            Consumable = consumable
        };

        _world.LoadTrack(_track);
    }

    /// <summary>
    /// Spawn trains in the SimWorld from ScenarioModel train points. Uses the same grid->world & geometry
    /// as the Game (your TrainController.Init) so visuals match.
    /// </summary>
    public void SpawnFromScenario(ScenarioModel scenario, LevelData level,
                                  Vector2 worldOrigin, int minX, int minY, int gridH, float cellSize)
    {
        if (scenario == null) throw new ArgumentNullException("scenario");
        if (level == null) throw new ArgumentNullException("level");
        if (_track.Segments == null || _track.Segments.Count == 0)
        {
            Debug.LogWarning("SimController: Track is empty. Call BuildTrackDto() first.");
            return;
        }
        if (scenario.points == null) return;

        _pointIdToTrainId.Clear();

        // geometry constants (shared with Game)
        float cartSize = SimTuning.CartLen(cellSize);
        float gap = SimTuning.Gap(cellSize);
        float headHalf = SimTuning.HeadHalfLen(cellSize);
        float cartHalf = SimTuning.CartHalfLen(cellSize);

        // Helper: compute world head position from a GamePoint (exactly as in Game)
        Func<GamePoint, Vector3> headWorldFromPoint = (p) =>
        {
            Vector2 worldCell;
            if (p.anchor.exitPin >= 0 && p.part != null && p.part.exits != null && p.anchor.exitPin < p.part.exits.Count)
            {
                var exCell = p.part.exits[p.anchor.exitPin].worldCell;
                worldCell = new Vector2(exCell.x, exCell.y);
            }
            else
            {
                worldCell = new Vector2(p.gridX, p.gridY);
            }

            float cellX = worldCell.x - minX + 0.5f;
            float cellY = worldCell.y - minY + 0.5f;
            Vector2 flipped = new Vector2(cellX, gridH - cellY);
            return new Vector3(
                worldOrigin.x + flipped.x * cellSize,
                worldOrigin.y + flipped.y * cellSize,
                0f
            );
        };

        // Helper: forward vector from TrainDir
        Func<TrainDir, Vector3> forwardFromDir = (d) =>
        {
            switch (d)
            {
                case TrainDir.Up: return Vector3.up;
                case TrainDir.Right: return Vector3.right;
                case TrainDir.Down: return Vector3.down;
                case TrainDir.Left: return Vector3.left;
                default: return Vector3.up;
            }
        };

        // For each Train point, spawn a SimpleTrainSim in SimWorld
        for (int i = 0; i < scenario.points.Count; i++)
        {
            var p = scenario.points[i];
            if (p == null || p.type != GamePointType.Train) continue;

            // 1) Head pose in world (match Game)
            Vector3 headPos = headWorldFromPoint(p);
            Vector3 headFwd = forwardFromDir(p.direction);

            // 2) Cart offsets (center distances behind head center) — match Game
            var cartOffsets = new List<float>();
            float firstOffset = headHalf + gap + cartHalf;
            int cartCount = (p.initialCarts != null) ? p.initialCarts.Count : 0;
            for (int c = 0; c < cartCount; c++)
            {
                float off = firstOffset + (cartSize + gap) * c;
                cartOffsets.Add(off);
            }

            // 3) Tape seed length (tail center + margin)
            float tailOffset = (cartOffsets.Count > 0) ? (cartOffsets[cartOffsets.Count - 1] + cartHalf) : 0f;
            float seedLen = tailOffset + gap + SimTuning.TapeMarginMeters;

            // 4) Choose a polyline for the first leg
            Polyline path = null;
            TrackSegmentKey? segKey = null;

            // Prefer the anchor's part id; fall back to nearest segment globally
            string anchorPartId = !string.IsNullOrEmpty(p.anchor.partId)
                ? p.anchor.partId
                : (p.part != null ? p.part.partId : null);

            if (!string.IsNullOrEmpty(anchorPartId))
            {
                TrackSegmentKey k;
                Polyline pl;
                if (TryPickBestSegmentForSpawn(anchorPartId, headPos, out k, out pl))
                {
                    segKey = k;
                    path = pl;
                }
            }

            // Fallback: pick the nearest segment across the entire track
            if (path == null && _track.Segments.Count > 0)
            {
                float best = float.PositiveInfinity;
                foreach (var kv in _track.Segments)
                {
                    float d = DistanceToPolyline2D(kv.Value, headPos);
                    if (d < best)
                    {
                        best = d;
                        segKey = kv.Key;
                        path = kv.Value;
                    }
                }
            }

            if (path == null)
            {
                Debug.LogWarning("SimController: No track polyline found for train spawn; skipping train " + p.id);
                continue;
            }

            // 5) Build spawn spec
            var spec = new SpawnSpec
            {
                Path = path,
                HeadPos = headPos,
                HeadForward = headFwd,
                CartOffsets = cartOffsets,
                TapeSeedLen = seedLen,
                CellSizeHint = cellSize, // so SampleStep/Eps match Game feel
                SafetyGap = 0f
            };

            // 6) Spawn in world & map ids
            int trainId = _world.SpawnTrain(spec, segKey);
            _pointIdToTrainId[p.id] = trainId;
        }
    }

    public bool TryGetTrainIdForPoint(int gamePointId, out int trainId)
    {
        return _pointIdToTrainId.TryGetValue(gamePointId, out trainId);
    }

    public AdvanceResult StepByPointId(int gamePointId, float meters)
    {
        int trainId;
        if (!_pointIdToTrainId.TryGetValue(gamePointId, out trainId))
            throw new ArgumentException("Unknown GamePoint id (no spawned train): " + gamePointId);

        return _world.Step(trainId, meters);
    }

    public SimEvent RunToNextEventByPointId(int gamePointId, float metersPerTick)
    {
        int trainId;
        if (!_pointIdToTrainId.TryGetValue(gamePointId, out trainId))
            throw new ArgumentException("Unknown GamePoint id (no spawned train): " + gamePointId);

        return _world.RunToNextEvent(trainId, metersPerTick);
    }

    public List<TrainStateDto> GetStateSnapshot()
    {
        return _world.GetState();
    }

    public void RemoveConsumableSegment(TrackSegmentKey key)
    {
        _world.RemoveSegment(key);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers

    private bool TryPickBestSegmentForSpawn(string partId, Vector3 headPos,
                                            out TrackSegmentKey key, out Polyline path)
    {
        key = default(TrackSegmentKey);
        path = null;
        float best = float.PositiveInfinity;

        foreach (var kv in _track.Segments)
        {
            if (kv.Key.PartId != partId) continue;

            float d = DistanceToPolyline2D(kv.Value, headPos);
            if (d < best)
            {
                best = d;
                key = kv.Key;
                path = kv.Value;
            }
        }
        return path != null;
    }

    /// <summary>Distance from point to a polyline in XY (treat Z as 0). Exact segment distance; no heavy sampling needed.</summary>
    private static float DistanceToPolyline2D(Polyline pl, Vector3 pWorld)
    {
        var pts = pl.Points; // assumes Polyline exposes Points (List<Vector3> or IReadOnlyList<Vector3>)
        if (pts == null || pts.Count == 0) return float.PositiveInfinity;
        if (pts.Count == 1) return Vector2.Distance((Vector2)pts[0], (Vector2)pWorld);

        Vector2 p = new Vector2(pWorld.x, pWorld.y);
        float bestSq = float.PositiveInfinity;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = (Vector2)pts[i];
            Vector2 b = (Vector2)pts[i + 1];
            float sq = DistancePointToSegmentSq(p, a, b);
            if (sq < bestSq) bestSq = sq;
        }
        return Mathf.Sqrt(bestSq);
    }

    public struct ValidationReport
    {
        public bool Ok;
        public List<string> Lines;

        public override string ToString()
        {
            return string.Join("\n", Lines ?? new List<string>());
        }
    }

    /// <summary>
    /// Validates: (1) track health, (2) train spawn alignment, (3) a basic motion step.
    /// This method builds from baked splines to avoid relying on scene-time worldSplines.
    /// It resets and re-spawns internally so results are deterministic.
    /// </summary>
    public ValidationReport ValidateFromBaked(LevelData level, GridContext g, ScenarioModel scenario, float metersPerTick = -1f)
    {
        var rep = new ValidationReport { Ok = true, Lines = new List<string>(64) };

        // 0) Inputs & tuning
        if (level == null) { rep.Ok = false; rep.Lines.Add("❌ LevelData is null."); return rep; }
        if (scenario == null) { rep.Ok = false; rep.Lines.Add("❌ ScenarioModel is null."); return rep; }
        float cellSize = g.cellSize;
        float step = SimTuning.SampleStep(cellSize);
        float eps = SimTuning.Eps(cellSize);
        float tick = (metersPerTick > 0f) ? metersPerTick : DefaultMetersPerTick;

        // 1) Reset, build, spawn
        Reset();
        BuildTrackDtoFromBaked(level, g);
        if (_track.Segments == null || _track.Segments.Count == 0)
        {
            rep.Ok = false; rep.Lines.Add("❌ Track build produced 0 segments.");
            return rep;
        }
        SpawnFromScenario(scenario, level, g.worldOriginBL, g.minX, g.minY, g.gridH, g.cellSize);
        rep.Lines.Add($"✔ Track: {_track.Segments.Count} segments, consumable: {_track.Consumable?.Count ?? 0}");

        // 2) Track health (segment stats)
        int zeroLenSegs = 0;
        float minSegLen = float.PositiveInfinity, maxSegLen = 0f, totalLen = 0f;
        foreach (var kv in _track.Segments)
        {
            var pts = kv.Value.Points; // Polyline points in world space
            if (pts == null || pts.Count < 2) { zeroLenSegs++; continue; }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = Vector3.Distance(pts[i], pts[i + 1]);
                if (d <= 1e-6f) zeroLenSegs++;
                minSegLen = Mathf.Min(minSegLen, d);
                maxSegLen = Mathf.Max(maxSegLen, d);
                totalLen += d;
            }
        }
        rep.Lines.Add($"✔ Track length ~ {totalLen:F3} m | seg step min {minSegLen:F5} max {maxSegLen:F3} | zero-ish segments: {zeroLenSegs}");
        if (zeroLenSegs > 0) { rep.Ok = false; rep.Lines.Add("❌ Found degenerate (≈0 length) segments."); }

        // Helper (same mapping used by Game)
        Vector3 HeadWorldFromPoint(GamePoint p)
        {
            Vector2 worldCell;
            if (p.anchor.exitPin >= 0 && p.part != null && p.part.exits != null && p.anchor.exitPin < p.part.exits.Count)
            {
                var exCell = p.part.exits[p.anchor.exitPin].worldCell;
                worldCell = new Vector2(exCell.x, exCell.y);
            }
            else
            {
                worldCell = new Vector2(p.gridX, p.gridY);
            }
            float cellX = worldCell.x - g.minX + 0.5f;
            float cellY = worldCell.y - g.minY + 0.5f;
            Vector2 flipped = new Vector2(cellX, g.gridH - cellY);
            return new Vector3(g.worldOriginBL.x + flipped.x * g.cellSize,
                               g.worldOriginBL.y + flipped.y * g.cellSize,
                               0f);
        }

        // 3) Spawn alignment (nearest distance head→polyline)
        var trains = scenario.points?.FindAll(p => p != null && p.type == GamePointType.Train) ?? new List<GamePoint>();
        if (trains.Count == 0) rep.Lines.Add("ℹ No trains in scenario.");

        int misaligned = 0;
        foreach (var p in trains)
        {
            Vector3 head = HeadWorldFromPoint(p);
            float best = float.PositiveInfinity;
            TrackSegmentKey? bestKey = null;

            foreach (var kv in _track.Segments)
            {
                float d = DistanceToPolyline2D(kv.Value, head);
                if (d < best)
                {
                    best = d; bestKey = kv.Key;
                }
            }

            rep.Lines.Add($"• TrainPt {p.id} spawn dist→track = {best:F5} m {(bestKey.HasValue ? $"on {bestKey.Value.PartId}[{bestKey.Value.SplineIdx}]" : "")}");
            if (best > 3f * eps) { misaligned++; }
        }
        if (misaligned > 0)
        {
            rep.Ok = false; rep.Lines.Add($"❌ {misaligned} train spawns are > 3*eps ({3f * eps:F6}) away from any track. Check mapping/gridH units or +0.5 usage.");
        }
        else if (trains.Count > 0)
        {
            rep.Lines.Add("✔ All train heads are aligned to track within tolerance.");
        }

        // 4) Basic motion sanity (single-tick advance for the first train)
        if (trains.Count > 0)
        {
            var firstTrain = trains[0];
            if (_pointIdToTrainId.TryGetValue(firstTrain.id, out var tid))
            {
                var r = _world.Step(tid, tick);
                rep.Lines.Add($"• Move tick: allowed={r.Allowed:F4} kind={r.Kind} blocker={r.BlockerId}");
                // Not a failure if blocked/end-of-path; most important is we didn't throw.
            }
            else
            {
                rep.Ok = false; rep.Lines.Add($"❌ Could not map GamePoint {firstTrain.id} to a spawned trainId.");
            }
        }

        rep.Lines.Add(rep.Ok ? "✅ VALIDATION OK" : "⚠ VALIDATION HAS ISSUES");
        return rep;
    }

    /// <summary>Squared distance from point P to segment AB in 2D.</summary>
    private static float DistancePointToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denom = Vector2.Dot(ab, ab);
        if (denom <= 1e-12f) return (p - a).sqrMagnitude; // degenerate

        float t = Vector2.Dot(p - a, ab) / denom;
        t = Mathf.Clamp01(t);
        Vector2 proj = a + t * ab;
        return (p - proj).sqrMagnitude;
    }

    // Spawn a single train directly (Game mirror path)
    public int Mirror_SpawnTrain(SpawnSpec spec)
    {
        return _world.SpawnTrain(spec, /*segKey*/ null);
    }

    // Provide a leg (polyline) for a train by id
    public void Mirror_StartLeg(int trainId, IList<Vector3> worldPoints)
    {
        _world.SetLegPolyline(trainId, new Polyline(worldPoints));
    }

    // Preview (no commit) how far we can move this tick
    public AdvanceResult Mirror_PreviewAdvance(int trainId, float wantMeters, IList<int> otherTrainIds = null)
    {
        return _world.StepPreview(trainId, wantMeters, otherTrainIds);
    }

    // Commit movement (append to tape) and return new head pose
    public void Mirror_CommitAdvance(int trainId, float allowed, out Vector3 headPos, out Vector3 headTan)
    {
        _world.CommitAdvance(trainId, allowed, out headPos, out headTan);
    }

    //used for the game simulators
    public void SetLegPolylineByPointId(int gamePointId, List<Vector3> polyline)
    {
        int trainId;
        if (!TryGetTrainIdForPoint(gamePointId, out trainId))
            throw new ArgumentException("Unknown GamePoint id (train) " + gamePointId);

        // Build a Polyline dto and set it
        var pl = new Polyline(polyline);
        _world.SetLegPolyline(trainId, pl);
    }

}
