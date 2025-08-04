// Editor/AutoTunerWindow.cs
#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class AutoTunerWindow : EditorWindow
{
    // --- Inputs ---
    [Header("Inputs")]
    public LevelData levelData;
    public UnityEngine.Object pathProviderObject; // must implement IEditorPathProvider

    [Header("Grid/World (if needed for track build)")]
    public Vector2 worldOrigin = Vector2.zero;
    public int minX = 0;
    public int minY = 0;
    public int gridH = 10;     // number of vertical cells
    public float cellSize = 1f;

    [Header("Simulation")]
    public float metersPerTick = 0.25f;
    public int runsPerCandidate = 100;
    public int maxCandidates = 50;
    public int kMin = 3;           // min stations chosen
    public int kMax = 6;           // max stations chosen
    public float winRateMax = 0.75f;
    public float winRateMin = 0.0f;   // optional lower bound (0 = ignored)
    public int rngSeed = 12345;
    public int maxLegsPerRun = 64;
    public int maxTicksPerLeg = 2000;
    public float globalTimeCapSec = 60f;

    [Header("Output")]
    public string outputFolder = "AutoTunerReports";
    public bool createScenarioAsset = true;
    public LogVerbosity verbosity = LogVerbosity.Normal;

    // Internal
    private SimController sim;
    private ScenarioAutoTunerRunner runner;
    private bool isRunning;
    private bool cancelRequested;
    private string lastReportFolder;

    [MenuItem("Tools/Rail/Auto Tuner (Single Train)")]
    public static void Open() => GetWindow<AutoTunerWindow>("Auto Tuner");

    private void OnEnable()
    {
        sim = new SimController();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Auto Tuner (Single Train)", EditorStyles.boldLabel);
        levelData = (LevelData)EditorGUILayout.ObjectField("LevelData", levelData, typeof(LevelData), false);
        pathProviderObject = EditorGUILayout.ObjectField("Path Provider", pathProviderObject, typeof(UnityEngine.Object), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Grid / World (Editor)", EditorStyles.boldLabel);
        worldOrigin = EditorGUILayout.Vector2Field("World Origin", worldOrigin);
        minX = EditorGUILayout.IntField("Min X (cells)", minX);
        minY = EditorGUILayout.IntField("Min Y (cells)", minY);
        gridH = EditorGUILayout.IntField("Grid Height (cells)", gridH);
        cellSize = EditorGUILayout.FloatField("Cell Size", cellSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);
        metersPerTick = EditorGUILayout.FloatField("Meters / Tick", metersPerTick);
        runsPerCandidate = EditorGUILayout.IntField("Runs / Candidate", runsPerCandidate);
        maxCandidates = EditorGUILayout.IntField("Max Candidates", maxCandidates);
        using (new EditorGUILayout.HorizontalScope())
        {
            kMin = EditorGUILayout.IntField("K min", kMin);
            kMax = EditorGUILayout.IntField("K max", kMax);
        }
        winRateMax = EditorGUILayout.Slider("Win Rate Max", winRateMax, 0f, 1f);
        winRateMin = EditorGUILayout.Slider("Win Rate Min", winRateMin, 0f, 1f);
        rngSeed = EditorGUILayout.IntField("RNG Seed", rngSeed);
        maxLegsPerRun = EditorGUILayout.IntField("Max Legs / Run", maxLegsPerRun);
        maxTicksPerLeg = EditorGUILayout.IntField("Max Ticks / Leg", maxTicksPerLeg);
        globalTimeCapSec = EditorGUILayout.FloatField("Global Time Cap (sec)", globalTimeCapSec);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        createScenarioAsset = EditorGUILayout.Toggle("Create Scenario Asset", createScenarioAsset);
        verbosity = (LogVerbosity)EditorGUILayout.EnumPopup("Verbosity", verbosity);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build & Validate"))
            {
                TryBuildAndValidate();
            }
            EditorGUI.BeginDisabledGroup(isRunning);
            if (GUILayout.Button("Run Auto-Tune"))
            {
                StartRun();
            }
            EditorGUI.EndDisabledGroup();

            if (isRunning)
            {
                if (GUILayout.Button("Cancel"))
                    cancelRequested = true;
            }

            if (GUILayout.Button("Open Last Report") && !string.IsNullOrEmpty(lastReportFolder))
            {
                EditorUtility.RevealInFinder(lastReportFolder);
            }
        }
    }

    private void TryBuildAndValidate()
    {
        if (!CheckInputs()) return;

        // Build track from LevelData (prefers worldSplines; falls back to baked via grid params if your SimController supports it)
        try
        {
            // If your SimController has a special editor builder for baked splines, call that here instead.
            sim.BuildTrackDto(levelData);
        }
        catch (Exception ex)
        {
            Debug.LogError("BuildTrack failed: " + ex.Message);
            return;
        }

        // Quick validation
        var ok = ScenarioAutoTunerRunner.ValidateLevel(levelData, out string msg);
        if (!ok) Debug.LogError("[Validate] " + msg);
        else Debug.Log("[Validate] OK - " + msg);
    }

    private bool CheckInputs()
    {
        if (levelData == null) { Debug.LogError("LevelData is null."); return false; }
        if (pathProviderObject == null) { Debug.LogError("Path Provider is not assigned."); return false; }
        if (!(pathProviderObject is IEditorPathProvider))
        {
            Debug.LogError("Path Provider must implement IEditorPathProvider.");
            return false;
        }
        if (kMax < kMin) kMax = kMin;
        if (runsPerCandidate <= 0) { Debug.LogError("Runs per candidate must be > 0."); return false; }
        if (maxCandidates <= 0) { Debug.LogError("Max candidates must be > 0."); return false; }
        if (metersPerTick <= 0f) { Debug.LogError("Meters per tick must be > 0."); return false; }
        if (maxTicksPerLeg <= 0) { Debug.LogError("Max ticks per leg must be > 0."); return false; }
        if (maxLegsPerRun <= 0) { Debug.LogError("Max legs per run must be > 0."); return false; }
        return true;
    }

    private void StartRun()
    {
        if (!CheckInputs()) return;

        cancelRequested = false;
        isRunning = true;

        var provider = (IEditorPathProvider)pathProviderObject;

        // Prepare runner config
        var cfg = new AutoTunerConfig
        {
            RunsPerCandidate = runsPerCandidate,
            MaxCandidates = maxCandidates,
            KMin = kMin,
            KMax = kMax,
            WinRateMax = winRateMax,
            WinRateMin = winRateMin,
            Seed = rngSeed,
            MetersPerTick = metersPerTick,
            MaxLegsPerRun = maxLegsPerRun,
            MaxTicksPerLeg = maxTicksPerLeg,
            GlobalTimeCapSec = Mathf.Max(1f, globalTimeCapSec),
            Verbosity = verbosity
        };

        // Output folder
        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        lastReportFolder = Path.Combine(Application.dataPath, outputFolder, "Report_" + ts);
        Directory.CreateDirectory(lastReportFolder);

        // Build & run (blocking in editor with progress bar; cancelable)
        try
        {
            var sw = Stopwatch.StartNew();
            EditorUtility.DisplayProgressBar("Auto Tuner", "Initializing...", 0f);

            // Construct a fresh SimController each candidate inside the runner (the runner will do it)
            runner = new ScenarioAutoTunerRunner(provider);

            var report = runner.Run(levelData, cfg, lastReportFolder, () => cancelRequested, (p, info) =>
            {
                // update progress bar
                EditorUtility.DisplayProgressBar("Auto Tuner",
                    string.Format("Candidate {0}/{1}  |  Run {2}/{3}",
                                  info.CandidateIndex + 1, info.TotalCandidates,
                                  info.RunIndex + 1, cfg.RunsPerCandidate),
                    p);
            });

            sw.Stop();
            EditorUtility.ClearProgressBar();

            // Save report summary (already saved inside runner, but log here)
            Debug.Log($"[AutoTuner] Finished in {sw.Elapsed.TotalSeconds:F1}s. Accepted: {report.Accepted}. Folder: {lastReportFolder}");

            if (report.Accepted && createScenarioAsset && report.AcceptedScenario != null)
            {
                // Persist the ScenarioModel to JSON beside the report (simple dump)
                string scenJson = JsonUtility.ToJson(report.AcceptedScenario, true);
                File.WriteAllText(Path.Combine(lastReportFolder, "Scenario_Selected.json"), scenJson);
                Debug.Log("[AutoTuner] Saved Scenario_Selected.json");
            }
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("[AutoTuner] Error: " + ex);
        }
        finally
        {
            isRunning = false;
            cancelRequested = false;
        }
    }
}
#endif
