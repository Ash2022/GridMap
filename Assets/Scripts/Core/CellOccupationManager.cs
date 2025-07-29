using System.Collections.Generic;
using UnityEngine;

public class CellOccupationManager
{
    public Dictionary<Vector2Int, PlacedPartInstance> cellToPart = new Dictionary<Vector2Int, PlacedPartInstance>();
    private List<TrackPart> partsLibrary;

    public CellOccupationManager(List<TrackPart> partsLibrary)
    {
        this.partsLibrary = partsLibrary;
    }

    public void AddOrUpdatePart(PlacedPartInstance inst)
    {
        RemovePart(inst);                           // clear old footprint
        inst.RecomputeOccupancy(partsLibrary);      // make sure inst.occupyingCells is fresh
        foreach (var cell in inst.occupyingCells)
            cellToPart[cell] = inst;
    }

    public void RemovePart(PlacedPartInstance inst)
    {
        // assumes inst.occupyingCells still holds the last footprint
        foreach (var cell in inst.occupyingCells)
            if (cellToPart.TryGetValue(cell, out var who) && who == inst)
                cellToPart.Remove(cell);
    }

    public void BuildFromLevel(List<PlacedPartInstance> all)
    {
        cellToPart.Clear();
        foreach (var inst in all)
        {
            inst.RecomputeOccupancy(partsLibrary);
            foreach (var cell in inst.occupyingCells)
                cellToPart[cell] = inst;
        }
    }
}