// Editor/ScenarioAutoTunerCore.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using RailSimCore;

public enum LogVerbosity { Minimal, Normal, Verbose }

/* ---------------- Config / DTOs ---------------- */

[Serializable]
public class AutoTunerConfig
{
    public int RunsPerCandidate = 100;
    public int MaxCandidates = 50;
    public int KMin = 3;
    public int KMax = 6;
    public float WinRateMax = 0.75f;
    public float WinRateMin = 0.0f;       // 0 disables lower bound
    public int Seed = 12345;
    public float MetersPerTick = 0.25f;
    public int MaxLegsPerRun = 64;
    public int MaxTicksPerLeg = 2000;
    public float GlobalTimeCapSec = 60f;
    public LogVerbosity Verbosity = LogVerbosity.Normal;
}

[Serializable]
public class CandidateStats
{
    public List<int> StationIds = new List<int>();
    public int Wins;
    public int Losses;
    public float WinRate;
    public RunReplay SuccessReplay; // example success
    public RunReplay FailureReplay; // example failure
    public string Notes;
}

[Serializable]
public class RunReplay
{
    public bool Success;
    public List<int> VisitOrder = new List<int>();  // station ids in visit order
    public List<float> LegLengths = new List<float>();
    public string FailReason;    // if any
}

public class ProgressInfo
{
    public int CandidateIndex;
    public int TotalCandidates;
    public int RunIndex;
}

/* ---------------- Runner ---------------- */

public sealed class ScenarioAutoTunerRunner
{
    private readonly IEditorPathProvider pathProvider;

    public ScenarioAutoTunerRunner(IEditorPathProvider provider)
    {
        pathProvider = provider;
    }

    public static bool ValidateLevel(LevelData level, out string message)
    {
        if (level == null) { message = "LevelData is null"; return false; }
        if (level.gameData == null || level.gameData.points == null) { message = "ScenarioModel points missing"; return false; }

        int trains = 0, stations = 0;
        foreach (var p in level.gameData.points)
        {
            if (p.type == GamePointType.Train) trains++;
            else if (p.type == GamePointType.Station) stations++;
        }
        if (trains != 1) { message = "Exactly one Train point required (found " + trains + ")"; return false; }
        if (stations < 2) { message = "Need at least 2 stations (found " + stations + ")"; return false; }

        message = $"1 train, {stations} stations.";
        return true;
    }

    public AutoTunerReport Run(LevelData baseLevel,
                               AutoTunerConfig cfg,
                               string outFolder,
                               Func<bool> cancelRequested,
                               Action<float, ProgressInfo> onProgress)
    {
        var report = new AutoTunerReport
        {
            Seed = cfg.Seed,
            RunsPerCandidate = cfg.RunsPerCandidate,
            MaxCandidates = cfg.MaxCandidates,
            KMin = cfg.KMin,
            KMax = cfg.KMax,
            WinRateMax = cfg.WinRateMax,
            WinRateMin = cfg.WinRateMin
        };

        var rng = new System.Random(cfg.Seed);
        var sampler = new StationSubsetSampler();
        var evaluator = new SingleTrainEvaluator(pathProvider);

        var stations = new List<GamePoint>();
        GamePoint trainPoint = null;
        foreach (var p in baseLevel.gameData.points)
        {
            if (p.type == GamePointType.Station) stations.Add(p);
            else if (p.type == GamePointType.Train) trainPoint = p;
        }

        var swGlobal = Stopwatch.StartNew();
        for (int ci = 0; ci < cfg.MaxCandidates; ci++)
        {
            if (cancelRequested != null && cancelRequested()) break;
            if (swGlobal.Elapsed.TotalSeconds > cfg.GlobalTimeCapSec) { report.Notes = "Global time cap reached."; break; }

            int k = (cfg.KMax == cfg.KMin) ? cfg.KMin : (cfg.KMin + rng.Next(0, Math.Max(1, cfg.KMax - cfg.KMin + 1)));
            var subset = sampler.SampleDistinct(stations, k, rng);

            // Build candidate scenario
            var candidateScenario = BuildScenarioVariant(baseLevel.gameData, trainPoint, subset);

            // Evaluate
            var info = new ProgressInfo { CandidateIndex = ci, TotalCandidates = cfg.MaxCandidates, RunIndex = 0 };
            var stats = evaluator.EvaluateCandidate(baseLevel, candidateScenario, cfg, outFolder, rng.Next(), cancelRequested, (r) =>
            {
                info.RunIndex = r;
                onProgress?.Invoke(ComputeProgress(ci, cfg.MaxCandidates, r, cfg.RunsPerCandidate), info);
            });

            report.Candidates.Add(stats);

            // Decide acceptance
            bool passUpper = stats.WinRate <= cfg.WinRateMax + 1e-6f;
            bool passLower = (cfg.WinRateMin <= 0f) || (stats.WinRate + 1e-6f >= cfg.WinRateMin);
            if (passUpper && passLower && stats.SuccessReplay != null && stats.FailureReplay != null)
            {
                report.Accepted = true;
                report.AcceptedCandidate = stats;
                report.AcceptedScenario = candidateScenario;
                break;
            }
        }
        swGlobal.Stop();

        // Save report files
        SaveReport(outFolder, report);

        return report;
    }

    private static float ComputeProgress(int ci, int totalCandidates, int runIdx, int runsPerCand)
    {
        float c = totalCandidates <= 0 ? 0f : (float)ci / (float)totalCandidates;
        float r = runsPerCand <= 0 ? 0f : (float)runIdx / (float)runsPerCand;
        return Mathf.Clamp01(c + r / Mathf.Max(1, totalCandidates));
    }

    private static ScenarioModel BuildScenarioVariant(ScenarioModel original, GamePoint train, List<GamePoint> chosenStations)
    {
        var scen = new ScenarioModel();
        scen.points = new List<GamePoint>();

        // Keep the single train as-is (position/direction/carts)
        if (train != null)
        {
            scen.points.Add(ClonePoint(train));
        }

        // Keep only chosen stations
        for (int i = 0; i < chosenStations.Count; i++)
        {
            scen.points.Add(ClonePoint(chosenStations[i]));
        }

        return scen;
    }

    private static GamePoint ClonePoint(GamePoint p)
    {
        // Shallow copy fields you use (adjust to your type)
        var gp = new GamePoint(p.part, p.gridX, p.gridY, p.type, p.colorIndex, p.anchor);
        gp.id = p.id; // keep original id for readability
        gp.initialCarts = (p.initialCarts != null) ? new List<int>(p.initialCarts) : new List<int>();
        if (p.waitingPeople != null) gp.waitingPeople = new List<int>(p.waitingPeople);
        gp.direction = p.direction;
        return gp;
    }

    private static void SaveReport(string folder, AutoTunerReport report)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var summary = BuildSummary(report);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary);

            // JsonUtility can’t handle dictionaries well; our DTOs are lists, so it’s fine.
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(folder, "report.json"), json);
        }
        catch (Exception ex)
        {
            Debug.LogError("[AutoTuner] Failed to save report: " + ex);
        }
    }

    private static string BuildSummary(AutoTunerReport r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Auto Tuner Report ===");
        sb.AppendLine("Seed: " + r.Seed);
        sb.AppendLine("Runs/Candidate: " + r.RunsPerCandidate);
        sb.AppendLine("MaxCandidates: " + r.MaxCandidates);
        sb.AppendLine(string.Format("WinRate bounds: [{0:P0}, {1:P0}]", r.WinRateMin, r.WinRateMax));
        sb.AppendLine("Accepted: " + r.Accepted);
        if (r.Accepted && r.AcceptedCandidate != null)
        {
            sb.AppendLine("Accepted Stations: " + string.Join(",", r.AcceptedCandidate.StationIds));
            sb.AppendLine("WinRate: " + (r.AcceptedCandidate.WinRate.ToString("P1")));
        }
        sb.AppendLine();
        sb.AppendLine("Candidates tested: " + r.Candidates.Count);
        for (int i = 0; i < r.Candidates.Count; i++)
        {
            var c = r.Candidates[i];
            sb.AppendLine(string.Format("#{0}  K={1}  Stations=[{2}]  WinRate={3:P1}  W={4} L={5}",
                i + 1, c.StationIds.Count, string.Join(",", c.StationIds), c.WinRate, c.Wins, c.Losses));
        }
        if (!string.IsNullOrEmpty(r.Notes))
        {
            sb.AppendLine();
            sb.AppendLine("Notes: " + r.Notes);
        }
        return sb.ToString();
    }
}

/* ---------------- Report container ---------------- */

[Serializable]
public class AutoTunerReport
{
    public int Seed;
    public int RunsPerCandidate;
    public int MaxCandidates;
    public int KMin;
    public int KMax;
    public float WinRateMax;
    public float WinRateMin;

    public bool Accepted;
    public CandidateStats AcceptedCandidate;
    public ScenarioModel AcceptedScenario;
    public List<CandidateStats> Candidates = new List<CandidateStats>();
    public string Notes;
}

/* ---------------- Sampler ---------------- */

public sealed class StationSubsetSampler
{
    public List<GamePoint> SampleDistinct(List<GamePoint> pool, int k, System.Random rng)
    {
        var result = new List<GamePoint>(k);
        if (pool == null || pool.Count == 0 || k <= 0) return result;
        if (k >= pool.Count) { result.AddRange(pool); return result; }

        // Fisher–Yates partial shuffle
        var idx = new List<int>(pool.Count);
        for (int i = 0; i < pool.Count; i++) idx.Add(i);
        for (int i = 0; i < k; i++)
        {
            int j = i + rng.Next(pool.Count - i);
            int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
        }
        for (int i = 0; i < k; i++) result.Add(pool[idx[i]]);
        return result;
    }
}

/* ---------------- Evaluator (single train) ---------------- */

public sealed class SingleTrainEvaluator
{
    private readonly IEditorPathProvider pathProvider;

    public SingleTrainEvaluator(IEditorPathProvider provider)
    {
        pathProvider = provider;
    }

    public CandidateStats EvaluateCandidate(LevelData baseLevel,
                                            ScenarioModel scenario,
                                            AutoTunerConfig cfg,
                                            string outFolder,
                                            int seed,
                                            Func<bool> cancelRequested,
                                            Action<int> onRunProgress)
    {
        var rng = new System.Random(seed);
        var stats = new CandidateStats();
        var chosenStations = new List<GamePoint>();
        foreach (var p in scenario.points) if (p.type == GamePointType.Station) chosenStations.Add(p);
        for (int i = 0; i < chosenStations.Count; i++) stats.StationIds.Add(chosenStations[i].id);

        // Build sim track and spawn single train for each run (fresh)
        for (int run = 0; run < cfg.RunsPerCandidate; run++)
        {
            onRunProgress?.Invoke(run);
            if (cancelRequested != null && cancelRequested()) break;

            var sc = new SimController();
            sc.BuildTrackDto(baseLevel);
            // Spawn train
            // Need grid params; for game the LevelVisualizer uses worldOrigin/minX/minY/gridH/cellSize
            // In this evaluator we only need the sim track and the train's head pose/direction just for tape seed.
            // Reuse the editor wiring like in your ScenarioEditor: provide world params via LevelData or pass 0s if not needed.

            // Find the single train point:
            GamePoint train = null;
            foreach (var p in scenario.points) if (p.type == GamePointType.Train) { train = p; break; }
            if (train == null) { stats.Losses++; stats.FailureReplay = stats.FailureReplay ?? new RunReplay { Success = false, FailReason = "No train in scenario" }; continue; }

            // We don't need worldOrigin/min… here because SimController.BuildTrackDto() used worldSplines.
            // Spawn:
            sc.SpawnFromScenario(scenario, baseLevel, Vector2.zero, 0, 0, 1, 1f);

            // Map station visit set
            var unvisited = new HashSet<int>();
            foreach (var st in chosenStations) unvisited.Add(st.id);

            var replay = new RunReplay { Success = false };
            int legs = 0;
            bool fail = false;

            // Start location: train point may also be a station; if so, count as visited immediately
            if (unvisited.Contains(train.id)) { unvisited.Remove(train.id); replay.VisitOrder.Add(train.id); }

            GamePoint current = train;
            while (unvisited.Count > 0 && legs < cfg.MaxLegsPerRun)
            {
                legs++;
                // pick random target from unvisited
                int pickIdx = rng.Next(unvisited.Count);
                int targetId = -1;
                int it = 0; foreach (var sid in unvisited) { if (it++ == pickIdx) { targetId = sid; break; } }
                GamePoint target = null;
                for (int i = 0; i < chosenStations.Count; i++) if (chosenStations[i].id == targetId) { target = chosenStations[i]; break; }

                if (target == null) { fail = true; replay.FailReason = "Target station missing"; break; }

                // Path
                var path = pathProvider.GetPath(baseLevel, current, target);
                if (path == null || path.Count < 2) { fail = true; replay.FailReason = "NoPath"; break; }

                sc.SetLegPolylineByPointId(train.id, path);
                var ev = sc.RunToNextEventByPointId(train.id, cfg.MetersPerTick);

                if (ev.Kind != SimEventKind.Arrived)
                {
                    fail = true; replay.FailReason = "NotArrived";
                    break;
                }

                // assume arrival at target
                unvisited.Remove(target.id);
                replay.VisitOrder.Add(target.id);
                replay.LegLengths.Add(PolylineLength(path));
                current = target;

                if (legs >= cfg.MaxLegsPerRun) { fail = true; replay.FailReason = "ExceededLegs"; break; }
            }

            replay.Success = !fail && unvisited.Count == 0;

            if (replay.Success)
            {
                stats.Wins++;
                if (stats.SuccessReplay == null) stats.SuccessReplay = replay;
            }
            else
            {
                stats.Losses++;
                if (stats.FailureReplay == null) stats.FailureReplay = replay;
            }
        }

        int total = stats.Wins + stats.Losses;
        stats.WinRate = total > 0 ? (float)stats.Wins / (float)total : 0f;
        return stats;
    }

    private static float PolylineLength(List<Vector3> pts)
    {
        float acc = 0f; for (int i = 1; i < pts.Count; i++) acc += Vector3.Distance(pts[i - 1], pts[i]); return acc;
    }
}
