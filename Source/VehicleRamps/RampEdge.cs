using UnityEngine;
using Verse;

namespace AASBVehicleRamps;

/// <summary>One directed crossing for a vehicle size, with the lane that must fit at each mouth.</summary>
public readonly struct RampEdge
{
    internal RampEdge(RampGroup group, IntVec3 entry, IntVec3 exit, CellRect entryLane, CellRect exitLane,
        int approachDistance, int mapWidth)
    {
        Group = group;
        Entry = entry;
        Exit = exit;
        EntryIndex = entry.x + entry.z * mapWidth;
        ExitIndex = exit.x + exit.z * mapWidth;
        EntryLane = entryLane;
        ExitLane = exitLane;
        ApproachDistance = approachDistance;
    }

    public RampGroup Group { get; }

    public IntVec3 Entry { get; }

    public IntVec3 Exit { get; }

    public int EntryIndex { get; }

    public int ExitIndex { get; }

    public CellRect EntryLane { get; }

    public CellRect ExitLane { get; }

    public Rot4 EntryRotation => Group.EntryRotation;

    public Rot4 ExitRotation => Group.ExitRotation;

    public int ExitHeading => HeadingOf(ExitRotation);

    public int ApproachDistance { get; }

    public int EntryFloor => Group.FromFloor;

    public int ExitFloor => Group.ToFloor;

    public Vector3 EntryRampCenter => Group.EntryRampCenter;

    public Vector3 ExitRampCenter => Group.ExitRampCenter;

    public bool Climbs => Group.Climbs;

    // SmashTools.PathFinder.Rotation octants: North 0, East 2, South 4, West 6.
    internal static int HeadingOf(Rot4 rotation) => rotation.AsInt * 2;
}
