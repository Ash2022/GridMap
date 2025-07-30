

using UnityEngine;

[RequireComponent(typeof(Collider))]
public class StationView : MonoBehaviour
{
    private GamePoint _pointModel;

    [SerializeField] Transform exits;

    /// <summary>
    /// Call this right after Instantiate to wire up the model.
    /// </summary>
    public void Initialize(GamePoint point, PlacedPartInstance part, float cellSize)
    {
        _pointModel = point;

        exits.transform.localEulerAngles = new Vector3(0,0,-part.rotation);
    }

}