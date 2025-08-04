using RailSimCore;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RailSimCore;
using static SimController;

/// <summary>
/// Editor-side manager for placing/cycling/removing GamePoints,
/// and (new) a thin host for SimWorld via SimController.
/// </summary>
public class ScenarioEditor
{
    private readonly ScenarioModel _data;
    private readonly CellOccupationManager _cellMgr;
    private readonly int _colorCount;

    // ───────── NEW: simulation adapter ─────────
    private readonly SimController _sim = new SimController();
    public bool SimBuilt { get; private set; }
    public bool TrainsSpawned { get; private set; }
    public float SimMetersPerTick = 0.25f;

    GridContext gridContext;
    


    public ScenarioEditor(ScenarioModel gameData, CellOccupationManager cellMgr, int colorCount = 3)
    {
        _data = gameData;
        _cellMgr = cellMgr;
        _colorCount = colorCount;
    }

    public List<GamePoint> GetPoints() => _data.points;
    public void SetPoints(List<GamePoint> pts) => _data.points = pts;

    /// <summary>
    /// Handle a click on the grid in “game” mode.
    /// mouseButton: 0=LMB add/cycle color, 1=RMB cycle type, 2=MMB delete
    /// </summary>
    public void OnGridCellClicked(PlacedPartInstance clickedPart,
                                  int gx, int gy,
                                  int mouseButton,
                                  GamePointType selectedType,
                                  int colorIndex = 0)
    {
        var point = _data.points.FirstOrDefault(p => p.gridX == gx && p.gridY == gy);

        if (mouseButton == 0) // Left click
        {
            if (point == null)
            {
                // Add new
                var anchor = BuildAnchor(clickedPart, gx, gy);
                _data.points.Add(new GamePoint(clickedPart, gx, gy, selectedType, colorIndex, anchor));
            }
            else
            {
                // Cycle color
                point.colorIndex = (point.colorIndex + 1) % _colorCount;
            }
        }
        else if (mouseButton == 1) // Right click - cycle type
        {
            if (point != null)
            {
                point.type = NextType(point.type);
            }
        }
        else if (mouseButton == 2) // Middle click - delete
        {
            if (point != null)
            {
                _data.points.Remove(point);
            }
        }
    }

    private GamePointType NextType(GamePointType current)
    {
        switch (current)
        {
            case GamePointType.Station: return GamePointType.Depot;
            case GamePointType.Depot: return GamePointType.Train;
            case GamePointType.Train: return GamePointType.Station;
            default: return GamePointType.Station;
        }
    }

    /// <summary>
    /// Build an Anchor from the clicked cell and part.
    /// For now: choose the closest exit pin on the part (or -1 if none).
    /// </summary>
    private Anchor BuildAnchor(PlacedPartInstance part, int gx, int gy)
    {
        if (part == null || part.exits == null || part.exits.Count == 0)
            return new Anchor { partId = part != null ? part.partId : "none", exitPin = -1, splineIndex = -1, t = 0f };

        // Find nearest exit pin by grid distance
        var clickCell = new UnityEngine.Vector2Int(gx, gy);
        int bestPin = part.exits[0].exitIndex;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < part.exits.Count; i++)
        {
            var ex = part.exits[i];
            float d = UnityEngine.Vector2Int.Distance(clickCell, ex.worldCell);
            if (d < bestDist)
            {
                bestDist = d;
                bestPin = ex.exitIndex;
            }
        }

        return Anchor.FromPin(part.partId, bestPin);
    }

    public void ClearAll(bool resetIds = true)
    {
        _data.points.Clear();
        if (resetIds) GamePoint.ResetIds();
    }

    public void DrawStationsUI(Rect gridRect, List<GamePoint> points, CellOccupationManager cellManager, Color[] colors, float cellSize)
    {
        const float panelW = 480f;
        float rowH = 22f;         // Total height per station (reduced)
        const float labelH = 18f;
        const float personSize = 16f;   // Smaller person icons
        const float spacing = 4f;
        const float iconSpacing = 4f;

        float y = gridRect.y;

        foreach (var p in points.Where(pt => pt.type == GamePointType.Station || pt.type == GamePointType.Train))
        {
            Vector2Int cell = new Vector2Int(p.gridX, p.gridY);
            string partId = "none";
            if (cellManager != null && cellManager.cellToPart != null && cellManager.cellToPart.TryGetValue(cell, out PlacedPartInstance partInst))
                partId = partInst.partId;

            if (p.type == GamePointType.Station)
            {
                Rect box = new Rect(gridRect.xMax + spacing, y, panelW, rowH);

                // Station label
                GUI.Label(
                    new Rect(box.x, box.y, panelW, labelH),
                    "St " + p.id + " | Cl " + cell + " | Part: " + partId,
                    new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold });

                // Add Person button
                Rect addBtn = new Rect(box.xMax, box.y, 75f, labelH);
                if (GUI.Button(addBtn, "Add Person"))
                {
                    p.waitingPeople.Add(0);
                    Event.current.Use();
                }

                // Draw people next to the label, wrapping if needed
                float px = box.x + 200f;
                float py = box.y;// + labelH + 2f;

                for (int j = 0; j < p.waitingPeople.Count; j++)
                {
                    int colorIdx = p.waitingPeople[j];
                    if (px + personSize > box.xMax)
                    {
                        px = box.x + 4f;
                        py += personSize + spacing;
                    }

                    Rect pr = new Rect(px, py, personSize, personSize);
                    EditorGUI.DrawRect(pr, colors[colorIdx % colors.Length]);
                    Handles.color = Color.black;
                    Handles.DrawSolidRectangleWithOutline(pr, Color.clear, Color.black);

                    if (Event.current.type == EventType.MouseDown && pr.Contains(Event.current.mousePosition))
                    {
                        p.waitingPeople[j] = (colorIdx + 1) % colors.Length;
                        Event.current.Use();
                    }

                    px += personSize + iconSpacing;
                }

                y = py + personSize + spacing;
            }
            else // Train
            {
                // --- sizes (all UI pixels, not grid) ---
                rowH = 18f;
                float cartSizePx = cellSize / 3f;
                float cartRowY = 42f;   // where the cart row starts (relative to box.y)
                float addBtnH = 18f;
                float spacingY = 6f;

                // Dynamic panel height (header + dir/color + carts + button)
                float trainH = cartRowY + cartSizePx + spacingY + addBtnH + 4f;

                Rect box = new Rect(gridRect.xMax + spacing, y, panelW, trainH);

                // Header
                GUI.Label(new Rect(box.x, box.y, box.width, rowH),
                          "Train " + p.id + " | Cell " + cell + " | Part: " + partId,
                          new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold });

                // Direction button
                Rect dirBtn = new Rect(box.x, box.y + rowH + 2f, 70f, rowH);
                int dir = (int)p.direction;
                string arrow = dir == 0 ? "↑" : (dir == 1 ? "→" : (dir == 2 ? "↓" : "←"));
                if (GUI.Button(dirBtn, "Dir " + arrow))
                {
                    dir = (dir + 1) % 4;
                    p.direction = (TrainDir)dir;
                }

                // Color cycle
                Rect colBtn = new Rect(dirBtn.xMax + 6f, dirBtn.y, 60f, rowH);
                if (GUI.Button(colBtn, "Color"))
                {
                    p.colorIndex = (p.colorIndex + 1) % colors.Length;
                }

                // --- carts row ---
                if (p.initialCarts == null) p.initialCarts = new List<int>();

                for (int j = 0; j < p.initialCarts.Count; j++)
                {
                    int cIdx = p.initialCarts[j];
                    Rect cartRect = new Rect(
                        box.x + j * (cartSizePx + 4f),
                        box.y + cartRowY,
                        cartSizePx,
                        cartSizePx
                    );

                    EditorGUI.DrawRect(cartRect, colors[cIdx % colors.Length]);
                    Handles.color = Color.black;
                    Handles.DrawSolidRectangleWithOutline(cartRect, Color.clear, Color.black);

                    if (Event.current.type == EventType.MouseDown && cartRect.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.button == 0)        // left: cycle color
                            p.initialCarts[j] = (cIdx + 1) % colors.Length;
                        else if (Event.current.button == 1)   // right: remove
                            p.initialCarts.RemoveAt(j);

                        Event.current.Use();
                        break; // list changed
                    }
                }

                // Add Cart button
                Rect addBtn2 = new Rect(box.x, box.y + cartRowY + cartSizePx + spacingY, 80f, addBtnH);
                if (GUI.Button(addBtn2, "Add Cart"))
                {
                    p.initialCarts.Add(0);
                }

                y += trainH + spacing;
            }
        }
    }

    public void DrawGamePoints(Rect gridRect, float cellSize, Color[] colors)
    {
        foreach (var p in GetPoints())
        {
            Color col = colors[p.colorIndex % colors.Length];

            switch (p.type)
            {
                case GamePointType.Station:
                    {
                        Vector2 c = EditorUtils.GuiDrawHelpers.CellCenter(gridRect, cellSize, p.gridX, p.gridY);
                        EditorUtils.GuiDrawHelpers.DrawStationDisc(c, cellSize * 0.35f, col, Color.black);
                        EditorUtils.GuiDrawHelpers.DrawCenteredLabel(c, "S_" + p.id, 12, Color.black);
                        break;
                    }

                case GamePointType.Train:
                    {
                        Color outline = Color.black;
                        Color headCol = colors[p.colorIndex % colors.Length];

                        // ---- sizes in cells ----
                        const float HEAD_LEN = 1.0f;    // exactly 1 cell long
                        const float THICKNESS = 0.5f;   // exactly 0.5 cell thick
                        const float CART_LEN = 1f / 3f; // exactly 1/3 cell
                        const float GAP_FRAC = 0.10f;   // 10% of cart length

                        // ---- convert to pixels ----
                        float headLenPx = HEAD_LEN * cellSize;
                        float thickPx = THICKNESS * cellSize;
                        float cartLenPx = CART_LEN * cellSize;
                        float gapPx = cartLenPx * GAP_FRAC;

                        // anchor: cell center = train FRONT
                        Vector2 cc = EditorUtils.GuiDrawHelpers.CellCenter(gridRect, cellSize, p.gridX, p.gridY);

                        // build head rect so FRONT edge sits on cc, body extends “behind”
                        Rect headRect;
                        Vector2 baseStep;    // how each cart shifts relative to the previous
                        bool vertical;

                        switch (p.direction)
                        {
                            case TrainDir.Up:
                                headRect = new Rect(cc.x - thickPx * 0.5f, cc.y, thickPx, headLenPx);
                                baseStep = new Vector2(0f, cartLenPx + gapPx);
                                vertical = true;
                                break;

                            case TrainDir.Right:
                                headRect = new Rect(cc.x - headLenPx, cc.y - thickPx * 0.5f, headLenPx, thickPx);
                                baseStep = new Vector2(-(cartLenPx + gapPx), 0f);
                                vertical = false;
                                break;

                            case TrainDir.Down:
                                headRect = new Rect(cc.x - thickPx * 0.5f, cc.y - headLenPx, thickPx, headLenPx);
                                baseStep = new Vector2(0f, -(cartLenPx + gapPx));
                                vertical = true;
                                break;

                            case TrainDir.Left:
                                headRect = new Rect(cc.x, cc.y - thickPx * 0.5f, headLenPx, thickPx);
                                baseStep = new Vector2(cartLenPx + gapPx, 0f);
                                vertical = false;
                                break;

                            default:
                                headRect = new Rect(cc.x - headLenPx * 0.5f, cc.y - thickPx * 0.5f, headLenPx, thickPx);
                                baseStep = Vector2.zero;
                                vertical = false;
                                break;
                        }

                        headRect.center = cc;

                        // draw train head
                        EditorUtils.GuiDrawHelpers.DrawTrainRect(headRect, headCol, outline);

                        // ---- now draw carts ----
                        float cartW = vertical ? thickPx : cartLenPx;
                        float cartH = vertical ? cartLenPx : thickPx;

                        // align carts to center of the head thickness
                        float alignDX = (headRect.width - cartW) * 0.5f;
                        float alignDY = (headRect.height - cartH) * 0.5f;

                        // compute starting (tail) point for the first cart
                        float tailX, tailY;
                        switch (p.direction)
                        {
                            case TrainDir.Up:
                                tailX = headRect.x + alignDX;
                                tailY = headRect.yMax + gapPx * 0.5f;
                                break;
                            case TrainDir.Right:
                                tailX = headRect.x - cartLenPx - gapPx * 0.5f;
                                tailY = headRect.y + alignDY;
                                break;
                            case TrainDir.Down:
                                tailX = headRect.x + alignDX;
                                tailY = headRect.y - cartLenPx - gapPx * 0.5f;
                                break;
                            case TrainDir.Left:
                                tailX = headRect.xMax + gapPx * 0.5f;
                                tailY = headRect.y + alignDY;
                                break;
                            default:
                                tailX = headRect.x + alignDX;
                                tailY = headRect.yMax + gapPx * 0.5f;
                                break;
                        }

                        for (int ci = 0; ci < p.initialCarts.Count; ci++)
                        {
                            Color cartCol = colors[p.initialCarts[ci] % colors.Length];
                            Rect cartRect = new Rect(
                                tailX + baseStep.x * ci,
                                tailY + baseStep.y * ci,
                                cartW,
                                cartH
                            );
                            EditorUtils.GuiDrawHelpers.DrawTrainRect(cartRect, cartCol, outline, 1);
                        }

                        // label the head with arrow + ID
                        string arrow = (p.direction == TrainDir.Up) ? "↑" :
                                       (p.direction == TrainDir.Right) ? "→" :
                                       (p.direction == TrainDir.Down) ? "↓" : "←";
                        EditorUtils.GuiDrawHelpers.DrawCenteredLabel(headRect, "T_" + p.id + " " + arrow, 12, Color.black);

                        break;
                    }

                case GamePointType.Depot:
                    {
                        Rect r = EditorUtils.GuiDrawHelpers.CellRectCentered(gridRect, cellSize, p.gridX, p.gridY, 1.0f, 1.0f);
                        EditorUtils.GuiDrawHelpers.DrawDepotPoly(r, col, Color.black);
                        EditorUtils.GuiDrawHelpers.DrawCenteredLabel(r, "D_" + p.id);
                        break;
                    }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // NEW: Simulation hooks (editor drives these)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Build SimWorld.Track from the current LevelData.</summary>
    public void Sim_BuildTrack(LevelData level,GridContext g,  System.Func<PlacedPartInstance, bool> isConsumable = null)
    {
        gridContext = g;

        _sim.BuildTrackDtoFromBaked(level, gridContext, isConsumable);
        SimBuilt = true;
        TrainsSpawned = false;
    }

    /// <summary>Spawn all trains from the Scenario into SimWorld.</summary>
    public void Sim_SpawnTrains(LevelData level, Vector2 worldOrigin, int minX, int minY, int gridH, float cellSize)
    {
        if (!SimBuilt)
        {
            Debug.LogWarning("ScenarioEditor: Sim track not built yet. Building with default settings.");
            _sim.BuildTrackDtoFromBaked(level, gridContext, null);
            SimBuilt = true;
        }

        _sim.SpawnFromScenario(_data, level, worldOrigin, minX, minY, gridH, cellSize);
        TrainsSpawned = true;
    }

    /// <summary>Advance a specific train (by GamePoint id) a fixed distance.</summary>
    public AdvanceResult Sim_StepTrain(int gamePointId, float meters)
    {
        return _sim.StepByPointId(gamePointId, meters);
    }

    /// <summary>Run a specific train to the next event (Arrived/Blocked).</summary>
    public SimEvent Sim_RunToNextEvent(int gamePointId, float metersPerTick)
    {
        if (metersPerTick <= 0f) metersPerTick = SimMetersPerTick;
        return _sim.RunToNextEventByPointId(gamePointId, metersPerTick);
    }

    /// <summary>Get current sim state for optional gizmo drawing.</summary>
    public List<TrainStateDto> Sim_GetState()
    {
        return _sim.GetStateSnapshot();
    }

    /// <summary>Reset the simulation (keeps scenario points untouched).</summary>
    public void Sim_Reset()
    {
        _sim.Reset();
        SimBuilt = false;
        TrainsSpawned = false;
    }

    /// <summary>
    /// Runs the SimController validator using the editor's ScenarioModel (_data).
    /// Pass the same GridContext you used to build/convert baked splines.
    /// </summary>
    /// <param name="level">Your LevelData (for parts/baked splines)</param>
    /// <param name="g">GridContext (worldOrigin/minX/minY/gridH-in-cells/cellSize)</param>
    /// <param name="metersPerTick">Optional override; if &lt;=0 uses SimController.DefaultMetersPerTick</param>
    public SimController.ValidationReport Sim_Validate(LevelData level, float metersPerTick = -1f)
    {
        float mpt = (metersPerTick > 0f) ? metersPerTick : _sim.DefaultMetersPerTick;
        return _sim.ValidateFromBaked(level, gridContext, _data, mpt);
    }
}
