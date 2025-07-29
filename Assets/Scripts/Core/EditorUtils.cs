
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EditorUtils
{
    /// <summary>Small, editor-only helpers to draw game points on the grid.</summary>
    public static class GuiDrawHelpers
    {
        // ---------- Grid math ----------

        public static Vector2 CellCenter(Rect gridRect, float cellSize, int gx, int gy)
        {
            return new Vector2(
                gridRect.x + gx * cellSize + cellSize * 0.5f,
                gridRect.y + gy * cellSize + cellSize * 0.5f
            );
        }

        public static Rect CellRect(Rect gridRect, float cellSize, int gx, int gy, int w = 1, int h = 1)
        {
            return new Rect(
                gridRect.x + gx * cellSize,
                gridRect.y + gy * cellSize,
                w * cellSize,
                h * cellSize
            );
        }

        public static Rect CellRect(Rect gridRect, float cellSize,int gx, int gy,float wCells, float hCells)
        {
            float x = gridRect.x + gx * cellSize;
            float y = gridRect.y + gy * cellSize;
            return new Rect(x, y, wCells * cellSize, hCells * cellSize);
        }

        // ---------- Drawing primitives ----------

        public static void DrawStationDisc(Vector2 center, float radius, Color fill, Color outline)
        {
            Handles.color = fill;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = outline;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        /// <summary>Draws a 3×1 train rectangle aligned to the grid (horizontal). Pass vertical=true to rotate 1×3.</summary>
        public static Rect CellRectCentered(Rect gridRect, float cellSize,
                                        int gx, int gy,
                                        float wCells, float hCells)
        {
            float cx = gridRect.x + gx * cellSize + cellSize * 0.5f;
            float cy = gridRect.y + gy * cellSize + cellSize * 0.5f;
            float w = wCells * cellSize;
            float h = hCells * cellSize;
            return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }

        public static Rect TrainRectFromHead(Rect gridRect, float cellSize,
                                         int gx, int gy, TrainDir dir,
                                         float lengthCells = 3f, float thickCells = 0.35f)
        {
            // head world center
            float cx = gridRect.x + gx * cellSize + cellSize * 0.5f;
            float cy = gridRect.y + gy * cellSize + cellSize * 0.5f;

            float L = lengthCells * cellSize;
            float T = thickCells * cellSize;

            switch (dir)
            {
                case TrainDir.Up:    // head at clicked cell, body extends upward
                    return new Rect(cx - T * 0.5f, cy, T, L);
                case TrainDir.Down:  // extends downward
                    return new Rect(cx - T * 0.5f, cy - L, T, L);
                case TrainDir.Right: // extends right
                    return new Rect(cx - L, cy - T * 0.5f, L, T);
                case TrainDir.Left:  // extends left
                    return new Rect(cx, cy - T * 0.5f, L, T);
                default:
                    return Rect.zero;
            }
        }

        public static void DrawTrainRect(Rect r, Color fill, Color outline, int segments = 3)
        {
            // face
            EditorGUI.DrawRect(r, fill);
            // outline
            Handles.color = outline;
            Handles.DrawLine(new Vector3(r.x, r.y), new Vector3(r.xMax, r.y));
            Handles.DrawLine(new Vector3(r.xMax, r.y), new Vector3(r.xMax, r.yMax));
            Handles.DrawLine(new Vector3(r.xMax, r.yMax), new Vector3(r.x, r.yMax));
            Handles.DrawLine(new Vector3(r.x, r.yMax), new Vector3(r.x, r.y));

            // segments
            float step = r.width > r.height ? r.width / segments : r.height / segments;
            for (int i = 1; i < segments; i++)
            {
                if (r.width > r.height)
                {
                    float x = r.x + step * i;
                    Handles.DrawLine(new Vector3(x, r.y), new Vector3(x, r.yMax));
                }
                else
                {
                    float y = r.y + step * i;
                    Handles.DrawLine(new Vector3(r.x, y), new Vector3(r.xMax, y));
                }
            }
        }

        /// <summary>Draw a simple depot polygon (house-like pentagon).</summary>
        public static void DrawDepotPoly(Rect r, Color fill, Color outline)
        {
            // House shape: bottom rectangle + roof triangle
            Vector3 p0 = new Vector3(r.x,      r.yMax);
            Vector3 p1 = new Vector3(r.xMax,   r.yMax);
            Vector3 p2 = new Vector3(r.xMax,   r.y + r.height * 0.4f);
            Vector3 p3 = new Vector3(r.x + r.width * 0.5f, r.y);
            Vector3 p4 = new Vector3(r.x,      r.y + r.height * 0.4f);

            var poly = new[] { p0, p1, p2, p3, p4 };

            Handles.DrawAAConvexPolygon(poly);
            Handles.DrawSolidRectangleWithOutline(Rect.zero, Color.clear, Color.clear); // force outline next
            Handles.color = outline;
            for (int i = 0; i < poly.Length; i++)
                Handles.DrawLine(poly[i], poly[(i + 1) % poly.Length]);

            // Fill hack (since DrawAAConvexPolygon uses current color)
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(poly);
        }

        // ---------- Labels ----------

        public static void DrawCenteredLabel(Vector2 center, string text, int fontSize = 12, Color? color = null)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize
            };
            if (color.HasValue) style.normal.textColor = Color.black;

            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y), text, style);
        }

        // Convenience overload if you already have a rect
        public static void DrawCenteredLabel(Rect rect, string text, int fontSize = 14, Color? color = null)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize
            };
            if (color.HasValue) style.normal.textColor = Color.black;

            GUI.Label(rect, text, style);
        }

        public static List<Vector3> ExtractSegmentScreen(List<Vector2> pts, float tStart, float tEnd)
        {
            float total = 0f;
            float[] cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++)
            {
                total += Vector2.Distance(pts[i - 1], pts[i]);
                cum[i] = total;
            }
            if (total <= 0f) return new List<Vector3> { pts[0], pts[pts.Count - 1] };

            float sLen = tStart * total;
            float eLen = tEnd * total;

            Vector2 PointAt(float d)
            {
                for (int i = 1; i < pts.Count; i++)
                {
                    float a = cum[i - 1], b = cum[i];
                    if (d <= b)
                    {
                        float u = Mathf.InverseLerp(a, b, d);
                        return Vector2.Lerp(pts[i - 1], pts[i], u);
                    }
                }
                return pts[pts.Count - 1];
            }

            var outPts = new List<Vector3>();
            outPts.Add(PointAt(sLen));
            for (int i = 1; i < pts.Count - 1; i++)
                if (cum[i] > sLen && cum[i] < eLen) outPts.Add(pts[i]);
            outPts.Add(PointAt(eLen));
            return outPts;
        }

        // === DRAW USING SAVED GRID-SPACE SPLINES ===
        // Assumes placed.splines[splineIndex] points are already in *grid coordinates*
        // (what you stored in gridCoordinatesSpline: rotated, offset, /cellSize).
        // We only convert grid -> GUI pixels: screen = gridRect.position + p * cellSize.

        public static void DrawPathPreviewForPlacedPart2(PlacedPartInstance placed,
                                               int splineIndex,
                                               float tStart,
                                               float tEnd)
        {



            // Basic guards
            if (placed == null)
            {
                Debug.LogWarning("DrawPathPreviewForPlacedPart2: placed is null");
                return;
            }
            if (placed.bakedSplines == null)
            {
                Debug.LogWarning($"DrawPathPreviewForPlacedPart2: bakedSplines null for part {placed.partId}");
                return;
            }
            if (splineIndex < 0 || splineIndex >= placed.bakedSplines.Count)
            {
                Debug.LogWarning($"DrawPathPreviewForPlacedPart2: bad splineIndex {splineIndex} for part {placed.partId} (count {placed.bakedSplines.Count})");
                return;
            }

            var guiPts = placed.bakedSplines[splineIndex].guiPts;
            if (guiPts == null || guiPts.Count < 2)
            {
                Debug.LogWarning($"DrawPathPreviewForPlacedPart2: guiPts invalid (null or <2) for part {placed.partId}, spline {splineIndex}");
                return;
            }

            // Clamp/order
            float rawStart = tStart, rawEnd = tEnd;
            tStart = Mathf.Clamp01(tStart);
            tEnd = Mathf.Clamp01(tEnd);
            if (tEnd < tStart) { float tmp = tStart; tStart = tEnd; tEnd = tmp; }

            // Build segment
            var seg = GuiDrawHelpers.ExtractSegmentScreen(guiPts, tStart, tEnd);
            if (seg.Count < 2)
            {
                Debug.LogWarning($"DrawPathPreviewForPlacedPart2: seg.Count < 2 for part {placed.partId}, spline {splineIndex} (tStart={tStart}, tEnd={tEnd})");
                return;
            }

            // ---- DEBUG DUMP ----
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== DrawPathPreviewForPlacedPart2 DEBUG ===");
            sb.AppendLine($"Part: {placed.partId}   SplineIndex: {splineIndex}");
            sb.AppendLine($"Raw tStart/tEnd: {rawStart} / {rawEnd}   Clamped: {tStart} / {tEnd}");
            sb.AppendLine($"guiPts.Count: {guiPts.Count}");
            sb.AppendLine($"First guiPt: {guiPts[0]}   Last guiPt: {guiPts[guiPts.Count - 1]}");
            sb.AppendLine($"seg.Count: {seg.Count}");
            sb.AppendLine($"seg[0]: {seg[0]}   seg[last]: {seg[seg.Count - 1]}");
            // Print a few mid points (limit to avoid spam)
            int toPrint = Mathf.Min(seg.Count, 6);
            for (int i = 0; i < toPrint; i++)
                sb.AppendLine($" seg[{i}]: {seg[i]}");
            //Debug.Log(sb.ToString());
            // ---------------------

            // Draw
            Handles.color = new Color(Color.yellow.r, Color.yellow.g, Color.yellow.b, 0.25f);
            Handles.DrawAAPolyLine(20f, seg.ToArray());
            Handles.DrawSolidDisc(seg[0], Vector3.forward, 4f);
            Handles.DrawSolidDisc(seg[seg.Count - 1], Vector3.forward, 4f);
        }

        // Helper method to rotate a cell based on part rotation and grid dimensions
        public static Vector2Int RotateGridPart(Vector2Int cell, int rotation, int partWidth, int partHeight)
        {
            // For 1x1 parts, no rotation is necessary (trivial case)
            if (partWidth == 1 && partHeight == 1)
            {
                return cell;
            }

            Vector2Int rotatedCell;

            switch (rotation % 360)
            {
                case 90:
                    // 90° Rotation for 2x2 part
                    rotatedCell = new Vector2Int(
                        partHeight - cell.y - 1,       // X = inverted Y
                        cell.x                         // Y = original X
                    );
                    break;

                case 180:
                    // 180° Rotation
                    rotatedCell = new Vector2Int(
                        partWidth - cell.x - 1,        // X = inverted X
                        partHeight - cell.y - 1        // Y = inverted Y
                    );
                    break;

                case 270:
                    // 270° Rotation
                    rotatedCell = new Vector2Int(
                        cell.y,                        // X = original Y
                        partWidth - cell.x - 1         // Y = inverted X
                    );
                    break;

                default:
                    // No rotation: Return the local cell unchanged
                    rotatedCell = cell;
                    break;
            }

            return rotatedCell;
        }

        // Helper method to calculate direction offsets
        public static Vector2Int DirectionToOffset(int direction)
        {
            switch (direction)
            {
                case 0: return new Vector2Int(0, -1); // North
                case 1: return new Vector2Int(1, 0);  // East
                case 2: return new Vector2Int(0, 1);  // South
                case 3: return new Vector2Int(-1, 0); // West
                default: return Vector2Int.zero; // Invalid direction
            }
        }

        public static void DrawPath(PathModel pathModel, List<PlacedPartInstance> levelParts)
        {
            if (pathModel == null || !pathModel.Success) return;

            //Debug.Log("Start Path drawing");

            for (int i = 0; i < pathModel.Traversals.Count; i++)
            {
                var trav = pathModel.Traversals[i];
                PlacedPartInstance part = GetPartById(trav.partId, levelParts);
                if (part == null) continue;

                bool simplePart = part.exits.Count <= 2;

                int splineIndex;
                float tStart;
                float tEnd;

                if (simplePart)
                {
                    // SIMPLE PART
                    if (trav.entryExit != -1 && trav.exitExit != -1)
                    {
                        // Middle simple part → full spline
                        splineIndex = 0;
                        tStart = 0f;
                        tEnd = 1f;
                    }
                    else
                    {
                        // First or last simple part → clamp one side at center (0.5), other by exit
                        splineIndex = 0;
                        float tEntry = (trav.entryExit == -1) ? 0.5f : GetExitT(part, trav.entryExit);
                        float tExit = (trav.exitExit == -1) ? 0.5f : GetExitT(part, trav.exitExit);

                        //Debug.Log("Path has no connection " + (trav.entryExit == -1 || trav.exitExit == -1));

                        tStart = Mathf.Min(tEntry, tExit);
                        tEnd = Mathf.Max(tEntry, tExit);

                        if (Mathf.Approximately(tStart, tEnd))
                        {
                            const float eps = 0.001f;
                            if (tEnd + eps <= 1f) tEnd += eps; else tStart = Mathf.Max(0f, tStart - eps);
                        }
                    }
                }
                else
                {
                    // MULTI-EXIT PART
                    splineIndex = FindAllowedPathIndex(part, trav.entryExit, trav.exitExit);

                    // Always draw full sub-spline (we already chose the correct one)
                    tStart = 0f;
                    tEnd = 1f;
                }

                //Debug.Log($"Draw {part.partId} spl:{splineIndex} t[{tStart},{tEnd}] entry:{trav.entryExit} exit:{trav.exitExit}");
                GuiDrawHelpers.DrawPathPreviewForPlacedPart2(part, splineIndex, tStart, tEnd);
            }

            //Debug.Log("End Path drawing");
        }


        public static float GetExitT(PlacedPartInstance part, int exitIndex)
        {
            if (exitIndex < 0) return 0.5f;

            int dir = 0;
            for (int i = 0; i < part.exits.Count; i++)
                if (part.exits[i].exitIndex == exitIndex) { dir = part.exits[i].direction; break; }

            // Flipped: Up/Right = 0f, Down/Left = 1f
            switch (dir)
            {
                case 0: // Up
                case 1: // Right
                    return 0f;
                case 2: // Down
                case 3: // Left
                    return 1f;
                default:
                    return 0.5f;
            }
        }

        // helper
        public static int FindAllowedPathIndex(PlacedPartInstance part, int entryExit, int exitExit)
        {
            if (part.allowedPathsGroup == null) return 0;

            for (int g = 0; g < part.allowedPathsGroup.Count; g++)
            {
                var group = part.allowedPathsGroup[g];
                if (group.allowedPaths == null) continue;

                for (int a = 0; a < group.allowedPaths.Count; a++)
                {
                    var ap = group.allowedPaths[a];
                    if (ap.entryConnectionId == entryExit && ap.exitConnectionId == exitExit)
                        return g;   // <-- returns 1 for your (0,2) pair
                }
            }
            return 0;
        }

        public static PlacedPartInstance GetPartById(string id, List<PlacedPartInstance> levelParts)
        {
            for (int i = 0; i < levelParts.Count; i++)
                if (levelParts[i].partId == id)
                    return levelParts[i];
            return null; // or throw/log
        }
    }
}

