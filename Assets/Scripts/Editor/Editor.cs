using EditorUtils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static PlacedPartInstance;
using static SimController;


public class TrackLevelEditorWindow : EditorWindow
{
    private enum EditMode { Track, Game }
    private EditMode currentMode = EditMode.Track;

    private List<TrackPart> partsLibrary = new List<TrackPart>();
    private int selectedPartIndex = 0;

    private const int gridWidth = 7*2;//30;
    private const int gridHeight = 9*2;//50;

    private const int cellSize = 50/2;//15; // Use this everywhere

    private Vector2 gridScroll;

    private PlacedPartInstance draggedPart = null;
    private Vector2 dragOffset;
    private bool isDragging = false;
    private Rect gridRect;

    Vector2 gridOrigin = new Vector2(0, 0);

    LevelData levelData = new LevelData();
    ScenarioEditor scenarioEditor;

    
    
    RouteModel routeModel;
    PathFinder pathFinder;
    PathModel currPath;
    
    //List<PathStep> currPath;

    private int partCounter = 1;

    CellOccupationManager cellManager;

    private int fromIdx = 0;
    private int toIdx = 1;

    [MenuItem("Tools/Track Level Editor")]
    public static void ShowWindow()
    {
        GetWindow<TrackLevelEditorWindow>("Track Level Editor");
    }

    private void OnEnable()
    {
        LoadPartsLibrary();
    }

    private void LoadPartsLibrary()
    {
        levelData.parts = new List<PlacedPartInstance>();
        // GameEditor only needs gameData now
        

        partsLibrary.Clear();
        TextAsset jsonText = Resources.Load<TextAsset>("parts");
        if (jsonText != null)
        {
            partsLibrary = JsonConvert.DeserializeObject<List<TrackPart>>(jsonText.text);

            cellManager = new CellOccupationManager(partsLibrary);
        }
        else
        {
            Debug.LogError("Could not find parts.json in Resources.");
        }

        scenarioEditor = new ScenarioEditor(levelData.gameData, cellManager);

        pathFinder = new PathFinder();

        
    }

    private void OnGUI()
    {

        //Debug.Log($"OnGUI event: {Event.current.type}, frame: {Time.frameCount}");

        DrawPartPicker();
        GUILayout.Space(8);

        // Handle mouse wheel for zoom in grid area
        gridRect = new Rect(0, 0, gridWidth * cellSize + 16, gridHeight * cellSize + 16);

        DrawGrid();

        // Determine which cell was clicked (if any), and handle based on currentMode
        if (Event.current.type == EventType.MouseDown && gridRect.Contains(Event.current.mousePosition))
        {
            // Convert mouse position to grid cell coordinates
            Vector2 localMouse = (Event.current.mousePosition - gridOrigin);
            int gx = Mathf.FloorToInt((Event.current.mousePosition.x - gridRect.x) / cellSize);
            int gy = Mathf.FloorToInt((Event.current.mousePosition.y - gridRect.y) / cellSize);

            // Check bounds
            if (gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight)
            {
                if (currentMode == EditMode.Track)
                {
                    HandleGridMouse(gridRect);
                }
                // Find the PlacedPartInstance for the clicked cell (gx, gy)
                PlacedPartInstance clickedPart = null;
                Vector2Int clickedCell = new Vector2Int(gx, gy);

                foreach (var partInstance in levelData.parts)
                {
                    partInstance.RecomputeOccupancy(partsLibrary);

                    //var occupiedCells = GetOccupiedCells(partInstance,partsLibrary);
                    if (partInstance.occupyingCells.Contains(clickedCell))
                    {
                        clickedPart = partInstance;
                        break; // Found the part, exit the loop
                    }
                }

                // You can now use 'clickedPart' for further logic
                // For example, you could pass it to the station placement or store association

                // Proceed with the original call

                scenarioEditor.OnGridCellClicked(clickedPart,gx, gy, Event.current.button, GamePointType.Station, 0);



                // Optional: Debug log
                if (clickedPart != null)
                {
                    Debug.Log($"Station placed on part: {clickedPart.partType} at {clickedPart.position}");
                }
                else
                {
                    Debug.LogWarning($"No PlacedPartInstance found for cell ({gx},{gy})");
                }

                Event.current.Use();
                Repaint();
            }
        }

        if (currentMode == EditMode.Game)
        {
            scenarioEditor.DrawStationsUI(gridRect, scenarioEditor.GetPoints(),cellManager,colors,cellSize);
            scenarioEditor.DrawGamePoints(gridRect, cellSize, colors);
            Repaint();
            
            if (currPath != null)
            {
                GuiDrawHelpers.DrawPath(currPath,levelData.parts);
            }
        }

        GUILayout.Space(100);

        DrawSaveLoadButtons();

        if (GUILayout.Button("Clear Grid"))
        {
            ClearGrid();
            Repaint(); // If needed, to update the editor display
        }

        string[] modeNames = { "Track Editing", "Game Editing" };
        currentMode = (EditMode)GUILayout.SelectionGrid((int)currentMode, modeNames, modeNames.Length);


        // Get stations from GameData
        var stations = levelData.gameData.points;
        if (stations != null && stations.Count >= 2)
        {
            // Display dropdowns for start/end station
            string[] stationNames = new string[stations.Count];
            for (int i = 0; i < stations.Count; i++)
            {
                stationNames[i] = $"Station {i} ({stations[i].gridX}, {stations[i].gridY})";
            }

            // Store dropdown selection indexes in the editor class
            if (fromIdx >= stations.Count) fromIdx = 0;
            if (toIdx >= stations.Count) toIdx = 1;

            fromIdx = EditorGUILayout.Popup("From station", fromIdx, stationNames);
            toIdx = EditorGUILayout.Popup("To station", toIdx, stationNames);

            EditorGUI.BeginDisabledGroup(fromIdx == toIdx);

            if (GUILayout.Button("BuildGraph"))
            {
                BuildGameGraph();
            }

            if (GUILayout.Button("Show Path"))
            {
                var fromStation = stations[fromIdx];
                var toStation = stations[toIdx];

                var startPart = fromStation.part;
                var endPart = toStation.part;

                // If you want pathfinder to pick any exit, you can pass -1 or 0, depending on your implementation.
                // Or, update your FindPath method to allow "any exit" at the destination.

                if (startPart != null && endPart != null)
                {
                    currPath = pathFinder.GetPath(startPart, endPart);
                    
                }
                else
                {
                    Debug.LogWarning("Cannot find start/end station on track.");
                }
            }
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            EditorGUILayout.HelpBox("You need at least two stations (points) in GameData.", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Build Track"))
        {
            var g = new GridContext
            {
                worldOriginBL = Vector2.zero, // or your actual world offset
                minX = levelData.parts.Min(p => p.position.x),
                minY = levelData.parts.Min(p => p.position.y),
                gridH = gridHeight,
                cellSize = cellSize
            };


            scenarioEditor.Sim_BuildTrack(levelData, g /*, isConsumablePredicate(optional)*/);
        }

        if (GUILayout.Button("Validate"))
        {
            
            var report = scenarioEditor.Sim_Validate(levelData); // simple wrapper that calls SimController.ValidateFromBaked
            Debug.Log(report.ToString());
        }


        if (GUILayout.Button("Spawn Trains"))
        {
            scenarioEditor.Sim_SpawnTrains(levelData, gridOrigin, 0, 0, gridHeight*cellSize, cellSize);
        }
        if (GUILayout.Button("Run to Next"))
        {
            // pick a train GamePoint (e.g., the first train or one the user selected)
            var trainPt = levelData.gameData.points.FirstOrDefault(p => p.type == GamePointType.Train);
            if (trainPt != null)
            {
                var ev = scenarioEditor.Sim_RunToNextEvent(trainPt.id, scenarioEditor.SimMetersPerTick);
                Debug.Log($"Sim result: {ev.Kind} train={ev.TrainId} blocker={ev.BlockerId} hit={ev.HitPos}");
            }
            else
            {
                Debug.LogWarning("No train points to run.");
            }
        }
        if (GUILayout.Button("Reset"))
        {
            scenarioEditor.Sim_Reset();
        }
        EditorGUILayout.EndHorizontal();

    }

    private void DrawPartPicker()
    {
        GUILayout.Label("Pick a Part", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < partsLibrary.Count; i++)
        {
            TrackPart part = partsLibrary[i];
            Texture2D img = Resources.Load<Texture2D>("Images/" + System.IO.Path.GetFileNameWithoutExtension(part.displaySprite));
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.fixedWidth = 36;
            style.fixedHeight = 36;
            if (GUILayout.Toggle(selectedPartIndex == i, new GUIContent(img), style))
                selectedPartIndex = i;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawGrid()
    {
        GUILayout.Label("Level Grid", EditorStyles.boldLabel);

        gridRect = GUILayoutUtility.GetRect(gridWidth * cellSize + 16, gridHeight * cellSize + 16, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        GUI.Box(gridRect, "");

        // Draw cells
        Handles.BeginGUI();
        Handles.color = Color.gray;
        for (int x = 0; x <= gridWidth; x++)
        {
            float xPos = gridRect.x + x * cellSize;
            Handles.DrawLine(new Vector3(xPos, gridRect.y), new Vector3(xPos, gridRect.y + gridHeight * cellSize));
        }
        for (int y = 0; y <= gridHeight; y++)
        {
            float yPos = gridRect.y + y * cellSize;
            Handles.DrawLine(new Vector3(gridRect.x, yPos), new Vector3(gridRect.x + gridWidth * cellSize, yPos));
        }

        // Draw placed parts
        for (int i = 0; i < levelData.parts.Count; i++)
        {
            var placed = levelData.parts[i];
            TrackPart part = partsLibrary.Find(p => p.partName == placed.partType);
            if (part == null) continue;

            float px = gridRect.x + placed.position.x * cellSize;
            float py = gridRect.y + placed.position.y * cellSize;
            float pw = part.gridWidth * cellSize;
            float ph = part.gridHeight * cellSize;

            int w = part.gridWidth;
            int h = part.gridHeight;
            int rotation = placed.rotation % 360;

            float offsetX = 0f, offsetY = 0f;

            if (w != h)
            {
                if (rotation == 90)
                {
                    offsetX = (h - w) / -2f * cellSize;
                    offsetY = (w - h) / -2f * cellSize;
                }
                else if (rotation == 270)
                {
                    offsetX = (h - w) / 2f * cellSize;
                    offsetY = (w - h) / 2f * cellSize;
                }
            }

            Vector2 pivot = new Vector2(px + pw / 2f, py + ph / 2f);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(placed.rotation, pivot);

            // Apply offsets only to texture rendering
            Texture2D img = Resources.Load<Texture2D>("Images/" + System.IO.Path.GetFileNameWithoutExtension(part.displaySprite));
            if (img != null)
                GUI.DrawTexture(new Rect(px + offsetX, py - offsetY, pw, ph), img, ScaleMode.ScaleToFit);
            GUI.matrix = oldMatrix;

            // Draw splines without offsets to ensure consistency for pathfinding
            DrawSplinesForPart(part, placed, new Rect(px, py, pw, ph));
        }
        Handles.EndGUI();

        // Handle mouse events for placing, rotating, deleting
        HandleGridMouse(gridRect);
    }

    private static List<BakedSpline> DrawSplinesForPart(TrackPart part,
                                             PlacedPartInstance placed,
                                             Rect partRect,
                                             bool buildOnly = false)
    {
        float offsetX2 = 0f, offsetY2 = 0f;
        if (part.gridWidth != part.gridHeight)
        {
            int rot = placed.rotation % 360;
            if (rot == 90)
            {
                offsetX2 = (part.gridHeight - part.gridWidth) * cellSize / -2f;
                offsetY2 = (part.gridWidth - part.gridHeight) * cellSize / 2f;
            }
            else if (rot == 270)
            {
                offsetX2 = (part.gridHeight - part.gridWidth) * cellSize / 2f;
                offsetY2 = (part.gridHeight - part.gridWidth) * cellSize / 2f;
            }
        }

        var results = new List<BakedSpline>();
        var splines = part.GetSplinesAsVector2();

        for (int i = 0; i < splines.Count; i++)
        {
            var spline = splines[i];
            Vector3[] pts = new Vector3[spline.Count];

            var guiPts = new List<Vector2>(spline.Count);
            var gridPts = new List<Vector2>(spline.Count);

            for (int j = 0; j < spline.Count; j++)
            {
                Vector2 pt = spline[j];
                float gx = pt.x * cellSize;
                float gy = pt.y * cellSize;

                Vector2 gridPt = new Vector2(partRect.x + gx + offsetX2, partRect.y + gy + offsetY2);
                Vector2 partCent = new Vector2(partRect.x + partRect.width / 2f, partRect.y + partRect.height / 2f);

                Vector2 rotatedPt = RotatePointAround(gridPt, partCent, placed.rotation);
                pts[j] = rotatedPt;

                guiPts.Add(rotatedPt);                            // pixels for direct drawing later
                gridPts.Add(new Vector2(rotatedPt.x / cellSize,rotatedPt.y / cellSize));
            }

            results.Add(new BakedSpline { guiPts = guiPts, gridPts = gridPts });

            if (!buildOnly)
            {
                Handles.DrawAAPolyLine(4f, pts);
                Handles.DrawSolidDisc((Vector2)pts[0], Vector3.forward, 4f);
                Handles.DrawSolidDisc((Vector2)pts[pts.Length - 1], Vector3.forward, 4f);
            }
        }

        return results;
    }


    private static Vector2 RotatePointAround(Vector2 pt, Vector2 pivot, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rad);
        float sinA = Mathf.Sin(rad);
        Vector2 d = pt - pivot;
        return pivot + new Vector2(
            cosA * d.x - sinA * d.y,
            sinA * d.x + cosA * d.y
        );
    }

    private void HandleGridMouse(Rect gridRect)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;
        if (!gridRect.Contains(mousePos)) return;

        int gx = Mathf.FloorToInt((mousePos.x - gridRect.x) / cellSize);
        int gy = Mathf.FloorToInt((mousePos.y - gridRect.y) / cellSize);

        if (currentMode == EditMode.Track)
        {
            var cell = new Vector2Int(gx, gy);

            PlacedPartInstance clickedPart = null;
            TrackPart clickedTrackPart = null;

            if (cellManager != null && cellManager.cellToPart.TryGetValue(cell, out clickedPart))
            {
                // If you keep a direct reference to the TrackPart inside the instance, use that.
                // Otherwise look it up:
                clickedTrackPart = partsLibrary.Find(p => p.partName == clickedPart.partType);
            }

            // Mouse button logic
            if (e.type == EventType.MouseDown)
            {
                if (clickedPart != null)
                {
                    if (e.button == 0) // Left: start drag
                    {
                        draggedPart = clickedPart;
                        dragOffset = new Vector2(gx - draggedPart.position.x, gy - draggedPart.position.y);
                        isDragging = true;
                        e.Use();
                    }
                    else if (e.button == 1) // Right: rotate
                    {
                        clickedPart.rotation = (clickedPart.rotation + 90) % 360;
                        
                        cellManager.AddOrUpdatePart(clickedPart);

                        PopulatePlacedPartData(clickedPart, clickedTrackPart); // Print after rotate

                        e.Use();
                        Repaint();
                    }
                    else if (e.button == 2) // Middle: delete
                    {
                        levelData.parts.Remove(clickedPart);

                        cellManager.RemovePart(clickedPart);

                        e.Use();
                        Repaint();
                    }
                }
                else if (e.button == 0) // Left: place new part
                {
                    TrackPart selectedPart = partsLibrary[selectedPartIndex];
                    int placeWidth = selectedPart.gridWidth;
                    int placeHeight = selectedPart.gridHeight;

                   
                    
                    var newInstance = new PlacedPartInstance
                    {
                        allowedPathsGroup = selectedPart.allowedPathsGroups,
                        partType = selectedPart.partName,         // Reference the part name from TrackPart
                        partId = GenerateUniquePartId(selectedPart.partName),
                        position = new Vector2Int(gx, gy),
                        rotation = 0,
                        splines = new List<List<float[]>>()
                    };
                    levelData.parts.Add(newInstance);

                    cellManager.AddOrUpdatePart(newInstance);

                    PopulatePlacedPartData(newInstance, selectedPart); // Print after placement

                    e.Use();
                    Repaint();
                }
            }

            // Dragging part
            if (isDragging && draggedPart != null)
            {
                if (e.type == EventType.MouseDrag)
                {
                    draggedPart.position.x = gx - (int)dragOffset.x;
                    draggedPart.position.y = gy - (int)dragOffset.y;

                    cellManager.AddOrUpdatePart(draggedPart);
                    
                    PopulatePlacedPartData(draggedPart, partsLibrary.Find(p => p.partName == draggedPart.partType)); // Print after move

                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseUp)
                {
                    isDragging = false;
                    draggedPart = null;
                    e.Use();
                    Repaint();
                }
            }
        }
        else if (currentMode == EditMode.Game)
        {
            if (Event.current.type == EventType.MouseDown)
            {
                string btn = Event.current.button switch
                {
                    0 => "Left",
                    1 => "Right",
                    2 => "Middle",
                    _ => $"Button {Event.current.button}"
                };
                //Debug.Log($"Game Edit: Clicked grid cell ({gx}, {gy}) with {btn} mouse button.");
            }
        }
    }

    // Add this helper method to your class
    private void PopulatePlacedPartData(PlacedPartInstance instance, TrackPart model)
    {
        // Use the GetOccupiedCells method to retrieve all occupied cells

        instance.RecomputeOccupancy(partsLibrary);

        List<Vector2Int> occupiedCells = instance.occupyingCells;

        Debug.Log($"--- Part Details ---");
        Debug.Log($"Name: {instance.partId}");
        Debug.Log($"Type: {instance.partType}");
        Debug.Log($"ID: {instance.partId}");
        Debug.Log($"Position: {instance.position}");
        Debug.Log($"Rotation: {instance.rotation} degrees");

        // Print the occupied cells
        Debug.Log($"Occupied Cells:");
        foreach (Vector2Int cell in occupiedCells)
        {
            Debug.Log($"  - Cell at {cell}");
        }

        // Initialize the exits list
        instance.exits = new List<PlacedPartInstance.ExitDetails>();

        // Populate the exits list and print the details
        Debug.Log($"Exits and Neighbor Cells:");
        foreach (var exit in model.connections)
        {
            // Calculate the rotated exit position and direction
            Vector2Int exitLocalCell = new Vector2Int(exit.gridOffset[0], exit.gridOffset[1]); // Local exit cell relative to the part
            Vector2Int rotatedExitCell = GuiDrawHelpers.RotateGridPart(exitLocalCell, instance.rotation, model.gridWidth, model.gridHeight); // Rotated cell after applying rotation
            Vector2Int exitWorldCell = instance.position + rotatedExitCell; // World cell in the grid
            int rotatedExitDirection = (exit.direction + instance.rotation / 90) % 4; // Rotated direction after rotation

            // Calculate neighbor cell
            Vector2Int neighborCell = instance.position + rotatedExitCell + GuiDrawHelpers.DirectionToOffset(rotatedExitDirection);

            // Create and add the exit details to the list
            PlacedPartInstance.ExitDetails exitDetails = new PlacedPartInstance.ExitDetails
            {
                exitIndex = exit.id,
                localCell = exitLocalCell,
                rotatedCell = rotatedExitCell,
                worldCell = exitWorldCell,
                direction = rotatedExitDirection,
                neighborCell = neighborCell
            };
            instance.exits.Add(exitDetails);
            instance.occupyingCells = occupiedCells;

            // Print exit details
            Debug.Log($"  - Exit Index: {exitDetails.exitIndex}");
            Debug.Log($"    Local Cell: {exitDetails.localCell}");
            Debug.Log($"    Rotated Cell: {exitDetails.rotatedCell}");
            Debug.Log($"    World Cell: {exitDetails.worldCell}");
            Debug.Log($"    Direction: {GetHumanReadableDirection(exitDetails.direction)}");
            Debug.Log($"    Neighbor Cell to Search: {exitDetails.neighborCell}");
        }

        

        // Update the spline values in grid coordinates

        float px = gridRect.x + instance.position.x * cellSize;
        float py = gridRect.y + instance.position.y * cellSize;
        float pw = model.gridWidth * cellSize;
        float ph = model.gridHeight * cellSize;

        Rect partRect = new Rect(px, py, pw, ph); // Assume CalculatePartRect gives the rectangle for the part
        instance.bakedSplines = DrawSplinesForPart(model, instance, partRect, true);

        // Print spline points
        Debug.Log($"Spline Points:");
        for (int i = 0; i < instance.splines.Count; i++)
        {
            Debug.Log($"  Spline {i}:");
            foreach (var point in instance.splines[i])
            {
                Debug.Log($"    - Point: {point}");
            }
        }

        // Map (entry,exit) -> spline index (group index, as you decided earlier)
        int FindSplineIndex(int entryId, int exitId)
        {
            if (instance.allowedPathsGroup == null) return 0;
            for (int g = 0; g < instance.allowedPathsGroup.Count; g++)
            {
                var grp = instance.allowedPathsGroup[g];
                if (grp.allowedPaths == null) continue;
                for (int a = 0; a < grp.allowedPaths.Count; a++)
                {
                    var ap = grp.allowedPaths[a];
                    if (ap.entryConnectionId == entryId && ap.exitConnectionId == exitId)
                        return g; // your convention: group index == spline index
                }
            }
            return 0;
        }

        // Fill length for each AllowedPath
        if (instance.allowedPathsGroup != null && instance.bakedSplines != null)
        {
            for (int g = 0; g < instance.allowedPathsGroup.Count; g++)
            {
                var grp = instance.allowedPathsGroup[g];
                if (grp.allowedPaths == null) continue;

                foreach (var ap in grp.allowedPaths)
                {
                    int splIdx = FindSplineIndex(ap.entryConnectionId, ap.exitConnectionId);

                    // Use gridPts to stay in grid units; multiply by cellSize later if needed
                    var poly = instance.bakedSplines[splIdx].gridPts;
                    ap.length = PolylineLength(poly);
                    // Debug
                    Debug.Log($"Path {instance.partId} {ap.entryConnectionId}->{ap.exitConnectionId} len={ap.length}");
                }
            }
        }


        Debug.Log($"--- End of Part Details ---");

        Debug.Log($"Spline values updated successfully in PlacedPartInstance.");
    }

    private float PolylineLength(List<Vector2> pts)
    {
        float len = 0f;
        for (int i = 1; i < pts.Count; i++)
            len += Vector2.Distance(pts[i - 1], pts[i]);
        return len;
    }


    // Helper method to convert direction into human-readable format
    private string GetHumanReadableDirection(int direction)
    {
        switch (direction)
        {
            case 0: return "North";
            case 1: return "East";
            case 2: return "South";
            case 3: return "West";
            default: return "Unknown";
        }
    }


    private string GenerateUniquePartId(string partName)
    {
        return $"{partName}_{partCounter++}";
    }

    private void DrawSaveLoadButtons()
    {
        var settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
        {
            new Vector2Converter(),
            new Vector2IntConverter(),
            new Vector3Converter()
        },
            Formatting = Formatting.Indented
        };

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Level"))
        {
            string path = EditorUtility.SaveFilePanel("Save Level JSON", Application.dataPath, "level.json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                if (levelData.gameData == null)
                    levelData.gameData = new ScenarioModel();

                levelData.gameData.points = scenarioEditor.GetPoints();

                //generate world splines from baked for simulations to be similar to game

                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

                foreach (var inst in levelData.parts)
                    foreach (var cell in inst.occupyingCells)
                    {
                        minX = Mathf.Min(minX, cell.x);
                        minY = Mathf.Min(minY, cell.y);
                        maxX = Mathf.Max(maxX, cell.x);
                        maxY = Mathf.Max(maxY, cell.y);
                    }

                int gridW = maxX - minX + 1;
                int gridH = maxY - minY + 1;
                float sizeX = 9f / gridW;
                float sizeY = 16f / gridH;

                // pick the *smaller* so that the entire grid fits inside the frame
                float cellSize = Mathf.Min(sizeX, sizeY, 100);

                BakeSplinesIntoWorld(levelData, minX, minY, gridH, cellSize);


                File.WriteAllText(path, JsonConvert.SerializeObject(levelData, settings));
                AssetDatabase.Refresh();
            }
        }
        if (GUILayout.Button("Load Level"))
        {
            string path = EditorUtility.OpenFilePanel("Load Level JSON", Application.dataPath, "json");
            if (!string.IsNullOrEmpty(path))
            {
                string json = File.ReadAllText(path);

                levelData = JsonConvert.DeserializeObject<LevelData>(json, settings);

                // ADD THIS LINE:
                scenarioEditor.SetPoints(levelData.gameData.points);

                // When editor loads a level:
                cellManager = new CellOccupationManager(partsLibrary); // Create new cell manager
                cellManager.BuildFromLevel(levelData.parts);           // Fill dictionary from all placed parts

                OnLoadLevel(levelData.parts);


                Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    void BakeSplinesIntoWorld(LevelData level, int minX, int minY, int gridH, float cellSize)
    {
        foreach (var part in level.parts)
        {
            part.worldSplines = new List<List<Vector3>>();
            if (part.bakedSplines == null) continue;

            foreach (var baked in part.bakedSplines)
            {
                var gp = baked.gridPts;
                if (gp == null || gp.Count < 2) continue;

                var poly = new List<Vector3>(gp.Count);
                for (int i = 0; i < gp.Count; i++)
                {
                    float cellX = gp[i].x - minX + 0.5f;
                    float cellY = gp[i].y - minY + 0.5f;
                    float wx = cellX * cellSize;                 // origin = 0
                    float wy = (gridH - cellY) * cellSize;       // flipped Y
                    poly.Add(new Vector3(wx, wy, 0f));
                }
                part.worldSplines.Add(poly);
            }
        }
        // Also do either: save mapping constants OR save per-train head world positions here.
    }

    private void BuildGameGraph()
    {

        currPath = null;

        routeModel = RouteModelBuilder.Build(levelData.parts);

        levelData.routeModelData = routeModel;

        pathFinder.Init(routeModel);


    }

    //ensures i can edit loaded levels
    private void OnLoadLevel(List<PlacedPartInstance> loadedParts)
    {
        currPath=null;

        int maxIndex = 0;
        foreach (var part in loadedParts)
        {
            // IDs are like "NameAAA_12"
            string[] tokens = part.partId.Split('_');
            if (tokens.Length > 1 && int.TryParse(tokens[tokens.Length - 1], out int idx))
            {
                if (idx > maxIndex)
                    maxIndex = idx;
            }
        }

        partCounter = maxIndex + 1; // Next generated part will get a unique ID

        //loop over all the loaded parts and feed them with their current data 
        foreach (var placedPart in levelData.parts)
        {
            TrackPart model = partsLibrary.Find(x => x.partName == placedPart.partType);

            placedPart.allowedPathsGroup = model.allowedPathsGroups;

            PopulatePlacedPartData(placedPart, model);
        }

    }


    private void ClearGrid()
    {
        if (levelData != null && levelData.parts != null)
        {
            levelData.parts.Clear();
        }

        cellManager.cellToPart.Clear(); // Remove all cell occupation mappings

        scenarioEditor.ClearAll();
    }

    private static readonly Color[] colors = new Color[]
    {
        new Color(0.4f, 0.6f, 0.8f),  // Dark Blue
        new Color(0.2f, 0.7f, 0.4f),  // Dark Green
        new Color(0.8f, 0.3f, 0.3f) // Dark Red
    };

    


    

    


    

    

}