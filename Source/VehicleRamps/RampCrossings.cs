using System;
using System.Collections.Generic;

namespace AASBVehicleRamps;

/// <summary>Immutable worker-readable crossings for one vehicle size within a ramp table.</summary>
internal sealed class RampCrossings
{
    private readonly RampEdge[] _edges;
    private readonly Dictionary<int, RampEdge[]> _byEntry = new Dictionary<int, RampEdge[]>();
    private readonly Dictionary<int, int[]> _byFloor = new Dictionary<int, int[]>();

    public RampCrossings(List<RampEdge> edges)
    {
        _edges = edges.ToArray();
        View = Array.AsReadOnly(_edges);
        var byEntry = new Dictionary<int, List<RampEdge>>();
        var byFloor = new Dictionary<int, List<int>>();
        for (var index = 0; index < _edges.Length; index++)
        {
            var edge = _edges[index];
            if (!byEntry.TryGetValue(edge.EntryIndex, out var crossings))
            {
                crossings = new List<RampEdge>();
                byEntry.Add(edge.EntryIndex, crossings);
            }

            crossings.Add(edge);
            if (!byFloor.TryGetValue(edge.EntryFloor, out var floorEdges))
            {
                floorEdges = new List<int>();
                byFloor.Add(edge.EntryFloor, floorEdges);
            }

            floorEdges.Add(index);
        }

        foreach (var pair in byEntry)
        {
            _byEntry.Add(pair.Key, pair.Value.ToArray());
        }

        foreach (var pair in byFloor)
        {
            _byFloor.Add(pair.Key, pair.Value.ToArray());
        }
    }

    public IReadOnlyList<RampEdge> View { get; }

    public ReadOnlySpan<RampEdge> Edges => _edges;

    public ReadOnlySpan<RampEdge> At(int entryIndex) =>
        _byEntry.TryGetValue(entryIndex, out var crossings) ? crossings : default;

    public ReadOnlySpan<int> FromFloor(int floor) =>
        _byFloor.TryGetValue(floor, out var indices) ? indices : default;
}
