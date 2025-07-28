using UnityEngine;
using UnityEditor;

public class OnlyYellowLineWindow : EditorWindow
{
    [MenuItem("Tools/Only Yellow Line222222")]
    public static void ShowWindow()
    {
        GetWindow<OnlyYellowLineWindow>("Yellow Line");
    }

    private void OnGUI()
    {
        if (Event.current.type == EventType.Repaint)
        {
            DrawGUILine(new Vector2(10, 10), new Vector2(100, 100), Color.yellow, 4f);
        }
    }

    void DrawGUILine(Vector2 pointA, Vector2 pointB, Color color, float thickness)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color savedColor = GUI.color;
        GUI.color = color;

        Vector2 delta = pointB - pointA;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float length = delta.magnitude;

        GUIUtility.RotateAroundPivot(angle, pointA);
        GUI.DrawTexture(new Rect(pointA.x, pointA.y, length, thickness), EditorGUIUtility.whiteTexture);

        GUI.matrix = matrix;
        GUI.color = savedColor;
    }
}