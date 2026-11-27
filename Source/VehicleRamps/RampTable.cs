using System;
using System.Collections.Generic;
using AsAboveSoBelow;
using RimWorld;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Immutable snapshot of one map's usable ramp pairs and their crossings by vehicle size. Path jobs read only
/// precomputed crossings and floor rows. The main thread also uses the map's live band component.
/// </summary>
public sealed class RampTable
{
    private readonly Dictionary<IntVec2, RampCrossings> _bySize;
    private readonly int[] _floorComponent;
    private readonly int[] _floorByRow;
    private readonly int _mapWidth;
    private readonly ABBandMap _bands;

    private RampTable(List<RampGroup> groups, Dictionary<IntVec2, RampCrossings> bySize, int edgeCount,
        int[] floorComponent, int[] floorByRow, int mapWidth, ABBandMap bands, int version, int skippedForbidden)
    {
        BandCount = bands?.bandCount ?? 0;
        Groups = groups.AsReadOnly();
        _bySize = bySize;
        EdgeCount = edgeCount;
        _floorComponent = floorComponent;
        _floorByRow = floorByRow;
        _mapWidth = mapWidth;
        _bands = bands;
        Version = version;
        SkippedForbidden = skippedForbidden;
    }

    public static RampTable Empty { get; } = new RampTable(new List<RampGroup>(),
        new Dictionary<IntVec2, RampCrossings>(), 0, Array.Empty<int>(), Array.Empty<int>(), 0, null, 0, 0);

    public bool IsEmpty => Groups.Count == 0;

    /// <summary>Number of crossings across all represented vehicle sizes.</summary>
    public int EdgeCount { get; }

    public IReadOnlyList<RampGroup> Groups { get; }

    public int Version { get; }

    /// <summary>AASB floor count when the table was built; a new floor makes the table stale.</summary>
    public int BandCount { get; }

    public int SkippedForbidden { get; }

    public IReadOnlyList<RampEdge> EdgesFor(IntVec2 size) => ForSize(size)?.View ?? Array.Empty<RampEdge>();

    public bool TryGetEdge(IntVec2 size, IntVec3 entry, IntVec3 exit, out RampEdge edge)
    {
        var crossings = ForSize(size);
        if (crossings != null)
        {
            foreach (ref readonly var candidate in crossings.At(entry.x + entry.z * _mapWidth))
            {
                if (candidate.Entry == entry && candidate.Exit == exit)
                {
                    edge = candidate;
                    return true;
                }
            }
        }

        edge = default;
        return false;
    }

    internal RampCrossings ForSize(IntVec2 size) => _bySize.GetValueOrDefault(size);

    /// <summary>Main thread only.</summary>
    public int FloorOf(IntVec3 cell) => _bands == null ? -1 : _bands.BandOf(cell);

    /// <summary>Worker-readable floor of a map row, or -1 outside any floor or the map.</summary>
    public int FloorOfRow(int z) => z >= 0 && z < _floorByRow.Length ? _floorByRow[z] : -1;

    /// <summary>True when usable ramp pairs join the two floors, possibly through intermediate floors.</summary>
    public bool FloorsConnected(int from, int to)
    {
        if (from == to)
        {
            return true;
        }

        if (from < 0 || to < 0 || from >= _floorComponent.Length || to >= _floorComponent.Length)
        {
            return false;
        }

        return _floorComponent[from] == _floorComponent[to];
    }

    /// <summary>Main thread only.</summary>
    public static RampTable Build(Map map, int version)
    {
        var bands = ABBands.CompOf(map);
        if (bands == null || !bands.Banded)
        {
            return Empty;
        }

        var groups = new List<RampGroup>();
        var parent = new int[bands.bandCount];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        var skippedForbidden = 0;
        foreach (var def in RampDefs.UpRamps)
        {
            var things = map.listerThings.ThingsOfDef(def);
            for (var i = 0; i < things.Count; i++)
            {
                if (things[i] is not Building_ABStairs2 up || !up.Spawned)
                {
                    continue;
                }

                var counterparts = up.Counterparts;
                for (var c = 0; c < counterparts.Count; c++)
                {
                    var down = counterparts[c];
                    if (down == null || !down.Spawned || down.Map != map || !RampDefs.IsRamp(down.def)
                        || down.LevelDelta >= 0)
                    {
                        continue;
                    }

                    if (up.IsForbidden(Faction.OfPlayer) || down.IsForbidden(Faction.OfPlayer))
                    {
                        skippedForbidden++;
                        continue;
                    }

                    AddPair(map, bands, up, down, groups, parent);
                }
            }
        }

        var component = new int[parent.Length];
        for (var i = 0; i < component.Length; i++)
        {
            component[i] = Find(parent, i);
        }

        var floorByRow = new int[map.Size.z];
        for (var z = 0; z < floorByRow.Length; z++)
        {
            floorByRow[z] = bands.BandOf(new IntVec3(0, 0, z));
        }

        var crossings = BuildCrossings(map, floorByRow, groups, out var edgeCount);
        return new RampTable(groups, crossings, edgeCount, component, floorByRow, map.Size.x, bands, version,
            skippedForbidden);
    }

    private static void AddPair(Map map, ABBandMap bands, Building_ABStairs2 up, Building_ABStairs2 down,
        List<RampGroup> groups, int[] parent)
    {
        if (!ABLinkApproach.TryGet(up, out var upMouth) || !ABLinkApproach.TryGet(down, out var downMouth)
            || !upMouth.outside.InBounds(map) || !downMouth.outside.InBounds(map))
        {
            return;
        }

        var lowerFloor = bands.BandOf(up.Position);
        var upperFloor = bands.BandOf(down.Position);
        if (lowerFloor < 0 || upperFloor < 0 || lowerFloor == upperFloor
            || bands.BandOf(upMouth.outside) != lowerFloor || bands.BandOf(downMouth.outside) != upperFloor)
        {
            return;
        }

        var width = Math.Min(WidthAcross(up, upMouth.outward), WidthAcross(down, downMouth.outward));
        if (RampDiagnostics.RampWidthLimit > 0)
        {
            width = Math.Min(width, RampDiagnostics.RampWidthLimit);
        }

        var upCenter = up.TrueCenter();
        var downCenter = down.TrueCenter();
        groups.Add(new RampGroup(upMouth.outside, upMouth.outward, lowerFloor, upCenter,
            downMouth.outside, downMouth.outward, upperFloor, downCenter, width, true));
        groups.Add(new RampGroup(downMouth.outside, downMouth.outward, upperFloor, downCenter,
            upMouth.outside, upMouth.outward, lowerFloor, upCenter, width, false));
        Union(parent, lowerFloor, upperFloor);
    }

    private static Dictionary<IntVec2, RampCrossings> BuildCrossings(Map map, int[] floorByRow,
        List<RampGroup> groups, out int edgeCount)
    {
        var crossings = new Dictionary<IntVec2, RampCrossings>();
        edgeCount = 0;
        if (groups.Count == 0)
        {
            return crossings;
        }

        var defs = DefDatabase<VehicleDef>.AllDefsListForReading;
        var edges = new List<RampEdge>(groups.Count);
        foreach (var def in defs)
        {
            var size = def.size;
            if (crossings.ContainsKey(size))
            {
                continue;
            }

            edges.Clear();
            var approachDistance = size.z / 2;
            foreach (var group in groups)
            {
                if (size.x > group.RampWidth)
                {
                    continue;
                }

                var entry = group.EntryMouth + group.EntryOutward * approachDistance;
                var exit = group.ExitMouth + group.ExitOutward * approachDistance;
                var entryLane = RampClearance.Lane(def, entry, group.EntryMouth, group.EntryRotation);
                var exitLane = RampClearance.Lane(def, exit, group.ExitMouth, group.ExitRotation);
                if (OnFloor(entryLane, group.FromFloor, map.Size.x, floorByRow)
                    && OnFloor(exitLane, group.ToFloor, map.Size.x, floorByRow))
                {
                    edges.Add(new RampEdge(group, entry, exit, entryLane, exitLane, approachDistance, map.Size.x));
                }
            }

            crossings.Add(size, new RampCrossings(edges));
            edgeCount += edges.Count;
        }

        return crossings;
    }

    private static bool OnFloor(CellRect lane, int floor, int mapWidth, int[] floorByRow)
    {
        if (lane.minX < 0 || lane.maxX >= mapWidth || lane.minZ < 0 || lane.maxZ >= floorByRow.Length)
        {
            return false;
        }

        for (var z = lane.minZ; z <= lane.maxZ; z++)
        {
            if (floorByRow[z] != floor)
            {
                return false;
            }
        }

        return true;
    }

    private static int WidthAcross(Thing ramp, IntVec3 outward)
    {
        var rect = ramp.OccupiedRect();
        return outward.x != 0 ? rect.Height : rect.Width;
    }

    private static int Find(int[] parent, int floor)
    {
        while (parent[floor] != floor)
        {
            parent[floor] = parent[parent[floor]];
            floor = parent[floor];
        }

        return floor;
    }

    private static void Union(int[] parent, int a, int b)
    {
        if (a < parent.Length && b < parent.Length)
        {
            parent[Find(parent, a)] = Find(parent, b);
        }
    }
}
