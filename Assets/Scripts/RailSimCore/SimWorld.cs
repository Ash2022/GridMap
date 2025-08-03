// RailSimCore/SimWorld.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailSimCore
{
    /* ============================================================
     * DTOs (neutral, editor/game friendly)
     * ============================================================ */

    public enum SimEventKind { Arrived, Blocked }

    public class SimEvent
    {
        public SimEventKind Kind { get; set; }
        public int TrainId { get; set; }
        public int BlockerId { get; set; }   // valid if Blocked
        public Vector3 HitPos { get; set; }  // approx world pos of first contact (if Blocked)
    }

    public class TrainStateDto
    {
        public int Id { get; set; }
        public float SHead { get; set; }
        public float PathLength { get; set; }
        public Vector3 HeadPos { get; set; }
        public Vector3 HeadTan { get; set; }
        public List<Vector3> CartCenters { get; set; }
    }

    /* ============================================================
     * Track model (zero-width rails; optional consumable segments)
     * ============================================================ */

    // Addressable piece of track; keep it simple/classic C# for Unity
    public struct TrackSegmentKey
    {
        public string PartId;  // e.g., placed-part GUID or name
        public int SplineIdx;
        public float T0;      // [0..1]
        public float T1;      // [0..1]
        public override string ToString() => $"{PartId}:{SplineIdx}[{T0:F2}-{T1:F2}]";
    }

    // Minimal polyline container (you can replace with your own type if you prefer)
    public sealed class Polyline
    {
        public List<Vector3> Points { get; private set; }
        public float Length { get; private set; }

        public Polyline(IEnumerable<Vector3> pts)
        {
            Points = new List<Vector3>(pts);
            Length = ComputeLength(Points);
        }

        private static float ComputeLength(List<Vector3> p)
        {
            float acc = 0f;
            for (int i = 1; i < p.Count; i++)
                acc += Vector3.Distance(p[i - 1], p[i]);
            return acc;
        }
    }

    public sealed class TrackDto
    {
        // Immutable maps are fine; Unity likes concrete types, so use plain collections.
        public Dictionary<TrackSegmentKey, Polyline> Segments = new Dictionary<TrackSegmentKey, Polyline>();
        public HashSet<TrackSegmentKey> Consumable = new HashSet<TrackSegmentKey>(); // segments removed after one traversal
    }

    /* ============================================================
     * Spawning specification
     * ============================================================ */

    public sealed class SpawnSpec
    {
        public Polyline Path;                    // first leg polyline
        public Vector3 HeadPos;                 // world
        public Vector3 HeadForward;             // world (unit)
        public List<float> CartOffsets = new List<float>(); // meters behind head (centers)
        public float TapeSeedLen;             // meters of straight tape behind head at spawn
        public float CellSizeHint = 1f;       // if you want to force cell size; otherwise auto-measured
        public float SafetyGap = 0f;          // optional safety used for this train (0 for puzzle feel)
    }

    /* ============================================================
     * SimWorld (engine): manages multiple SimpleTrainSim instances
     * ============================================================ */

    public sealed class SimWorld
    {
        // Internal record for a train in the world
        private sealed class TrainRecord
        {
            public int Id;
            public SimpleTrainSim Sim;
            public TrackSegmentKey? CurrentKey; // optional: which segment this leg corresponds to
            public float SafetyGap;
        }

        private readonly Dictionary<int, TrainRecord> trains = new Dictionary<int, TrainRecord>();
        private readonly List<SimpleTrainSim> scratchOthers = new List<SimpleTrainSim>(16);
        private readonly Dictionary<SimpleTrainSim, int> scratchIdMap = new Dictionary<SimpleTrainSim, int>(16);

        private TrackDto track;
        private int nextId = 1;

        /* --------------- Public API --------------- */

        public void LoadTrack(TrackDto t)
        {
            track = t;
        }

        /// <summary>Spawn a train on a given path. Returns its world-unique id.</summary>
        public int SpawnTrain(SpawnSpec spec, TrackSegmentKey? segmentKey = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            int id = nextId++;

            var sim = new SimpleTrainSim();
            float cellSize = (spec.CellSizeHint > 0f) ? spec.CellSizeHint : 1f;
            sim.Configure(cellSize, sampleStep: -1f, eps: -1f, safetyGap: spec.SafetyGap);

            // Defer leg load if Path is null (mirror workflow)
            if (spec.Path != null && spec.Path.Points != null && spec.Path.Points.Count >= 2)
                sim.LoadLeg(spec.Path.Points);

            sim.SetCartOffsets(spec.CartOffsets);
            if (spec.TapeSeedLen > 0f)
                sim.SeedTapePrefixStraight(spec.HeadPos, spec.HeadForward, spec.TapeSeedLen);

            trains[id] = new TrainRecord
            {
                Id = id,
                Sim = sim,
                CurrentKey = segmentKey,
                SafetyGap = spec.SafetyGap
            };

            return id;
        }

        /// <summary>
        /// Advance one train by up to 'meters' subject to collision capping.
        /// Returns the structured AdvanceResult from the core.
        /// </summary>
        public AdvanceResult Step(int trainId, float meters)
        {
            var tr = GetRecord(trainId);

            // Build others list + id map (single-mover: others are static)
            scratchOthers.Clear();
            scratchIdMap.Clear();
            foreach (var kv in trains)
            {
                if (kv.Key == trainId) continue;
                scratchOthers.Add(kv.Value.Sim);
                scratchIdMap[kv.Value.Sim] = kv.Key;
            }
            int GetId(SimpleTrainSim s) => scratchIdMap.TryGetValue(s, out var id) ? id : 0;

            var res = tr.Sim.ComputeAllowedAdvance(meters, scratchOthers, getId: GetId);
            tr.Sim.CommitAdvance(res.Allowed, out _, out _);

            // Optional: auto-consume segment at end (if provided & flagged)
            if (res.Kind == AdvanceResultKind.EndOfPath && tr.CurrentKey.HasValue && track != null)
            {
                var key = tr.CurrentKey.Value;
                if (track.Consumable != null && track.Consumable.Contains(key))
                {
                    // Remove this consumable segment from the track registry
                    track.Segments.Remove(key);
                    track.Consumable.Remove(key);
                }
            }

            return res;
        }

        /// <summary>Run until an event (Arrived or Blocked) occurs. metersPerTick must be &gt; 0.</summary>
        public SimEvent RunToNextEvent(int trainId, float metersPerTick, int maxTicks = 100000)
        {
            if (metersPerTick <= 0f) throw new ArgumentOutOfRangeException(nameof(metersPerTick), "metersPerTick must be > 0.");
            int ticks = 0;
            while (true)
            {
                var res = Step(trainId, metersPerTick);

                if (res.Kind == AdvanceResultKind.EndOfPath)
                {
                    return new SimEvent
                    {
                        Kind = SimEventKind.Arrived,
                        TrainId = trainId
                    };
                }
                if (res.Kind == AdvanceResultKind.Blocked)
                {
                    return new SimEvent
                    {
                        Kind = SimEventKind.Blocked,
                        TrainId = trainId,
                        BlockerId = res.BlockerId,
                        HitPos = res.HitPos
                    };
                }

                if (++ticks >= maxTicks)
                    throw new InvalidOperationException("RunToNextEvent exceeded maxTicks; check inputs or end conditions.");
            }
        }

        /// <summary>Builds a lightweight snapshot of current train states (for visuals/debug).</summary>
        public List<TrainStateDto> GetState()
        {
            var list = new List<TrainStateDto>(trains.Count);
            foreach (var kv in trains)
            {
                var id = kv.Key;
                var sim = kv.Value.Sim;

                // Sample head and carts
                sim.SampleHead(sim.SHead, out var headPos, out var headTan);

                var cartPos = new List<Vector3>(8);
                var cartTan = new List<Vector3>(8);
                sim.GetCartPoses(cartPos, cartTan);

                list.Add(new TrainStateDto
                {
                    Id = id,
                    SHead = sim.SHead,
                    PathLength = sim.PathLength,
                    HeadPos = headPos,
                    HeadTan = headTan,
                    CartCenters = cartPos
                });
            }
            return list;
        }

        /// <summary>Remove a track segment explicitly (for game rules).</summary>
        public void RemoveSegment(TrackSegmentKey key)
        {
            if (track == null) return;
            track.Segments.Remove(key);
            if (track.Consumable != null) track.Consumable.Remove(key);
        }

        /* ============================================================
         * Helpers
         * ============================================================ */

        private TrainRecord GetRecord(int trainId)
        {
            if (!trains.TryGetValue(trainId, out var tr))
                throw new ArgumentException($"Train id {trainId} not found.", nameof(trainId));
            return tr;
        }

        // Best-effort cell-size inference if caller doesn't supply it:
        private static float MeasureCell(Polyline p)
        {
            if (p == null || p.Points == null || p.Points.Count < 2) return 1f;
            // average segment length as a heuristic cell size
            float total = p.Length;
            int segs = Mathf.Max(1, p.Points.Count - 1);
            float avg = total / segs;
            return Mathf.Max(1e-3f, avg);
        }

        internal void SetLegPolyline(int trainId, Polyline polyline)
        {
            if (polyline == null || polyline.Points == null || polyline.Points.Count < 2)
                throw new ArgumentException("polyline must have at least 2 points.", nameof(polyline));

            var tr = GetRecord(trainId);
            tr.Sim.LoadLeg(polyline.Points);
            // Note: we don't touch tr.CurrentKey here (unknown for ad-hoc legs).
        }

        internal AdvanceResult StepPreview(int trainId, float wantMeters, IList<int> otherTrainIds)
        {
            var tr = GetRecord(trainId);

            // Build "others" from requested ids (or all except self if null)
            scratchOthers.Clear();
            scratchIdMap.Clear();

            if (otherTrainIds != null)
            {
                for (int i = 0; i < otherTrainIds.Count; i++)
                {
                    int oid = otherTrainIds[i];
                    if (oid == trainId) continue;
                    if (trains.TryGetValue(oid, out var rec))
                    {
                        scratchOthers.Add(rec.Sim);
                        scratchIdMap[rec.Sim] = oid;
                    }
                }
            }
            else
            {
                foreach (var kv in trains)
                {
                    if (kv.Key == trainId) continue;
                    scratchOthers.Add(kv.Value.Sim);
                    scratchIdMap[kv.Value.Sim] = kv.Key;
                }
            }

            int GetId(SimpleTrainSim s) => scratchIdMap.TryGetValue(s, out var id) ? id : 0;

            return tr.Sim.ComputeAllowedAdvance(Mathf.Max(0f, wantMeters), scratchOthers, getId: GetId);
        }

        internal void CommitAdvance(int trainId, float allowed, out Vector3 headPos, out Vector3 headTan)
        {
            var tr = GetRecord(trainId);
            tr.Sim.CommitAdvance(Mathf.Max(0f, allowed), out headPos, out headTan);
            // (No auto-consume here; Step(...) handles that when you run the full tick path.)
        }
    }
}
