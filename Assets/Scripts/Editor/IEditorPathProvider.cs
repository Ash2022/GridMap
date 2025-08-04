// Editor/IEditorPathProvider.cs
using System.Collections.Generic;
using UnityEngine;

public interface IEditorPathProvider
{
    /// Returns a world-space polyline from 'from' station to 'to' station.
    /// Must return at least 2 points; null or <2 => no path.
    List<Vector3> GetPath(LevelData level, GamePoint from, GamePoint to);
}
