using System.Collections.Generic;

/// <summary>All data needed to expand states quickly.</summary>
public class RouteModel
{
    // A state = “I am in partId, I entered through entryPin”.
    // entryPin == -1 is the synthetic start entry.
    public struct State
    {
        public string partId;
        public int entryPin;
        public State(string pid, int pin) { partId = pid; entryPin = pin; }
        public override int GetHashCode() => partId.GetHashCode() ^ entryPin;
        public override bool Equals(object obj)
        {
            if (!(obj is State s)) return false;
            return s.partId == partId && s.entryPin == entryPin;
        }
        public override string ToString() => $"{partId}@in{entryPin}";
    }

    public class PartCache
    {
        public PlacedPartInstance part;

        // Allowed internal transitions for this part:
        // entryPin -> list of (exitPin, internalLen, splineIdx, t0, t1)
        public Dictionary<int, List<AllowedEdge>> allowed = new();

        // Where each exitPin goes outside (pre-resolved)
        // exitPin -> neighbor (partId, neighborPin, externalLen)
        public Dictionary<int, NeighborLink> neighborByExit = new();
    }

    public struct AllowedEdge
    {
        public int exitPin;
        public float internalLen;
        public int splineIndex;
        public float t0;
        public float t1;
    }

    public struct NeighborLink
    {
        public string neighborPartId;
        public int neighborPin;
        public float externalLen;
    }

    // Master lookup
    public Dictionary<string, PartCache> parts = new();
}
