

using UnityEngine;

[RequireComponent(typeof(Collider))]
public class StationView : MonoBehaviour
{
    private GamePoint _pointModel;

    /// <summary>
    /// Call this right after Instantiate to wire up the model.
    /// </summary>
    public void Initialize(GamePoint pointModel)
    {
        _pointModel = pointModel;
    }

    // Alternatively, you could use OnMouseDown directly here,
    // but we'll let GameManager handle the raycast in Update().
    // This is just a marker.
}