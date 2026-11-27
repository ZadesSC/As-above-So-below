using UnityEngine;
using Verse;

namespace AASBVehicleRamps;

/// <summary>The two mouths of a paired ramp, in one travel direction.</summary>
public sealed class RampGroup
{
    internal RampGroup(long pairKey, IntVec3 entryMouth, IntVec3 entryOutward, int fromFloor,
        Vector3 entryRampCenter, IntVec3 exitMouth, IntVec3 exitOutward, int toFloor, Vector3 exitRampCenter,
        int rampWidth, bool climbs)
    {
        PairKey = pairKey;
        TrafficKey = pairKey;
        EntryMouth = entryMouth;
        EntryOutward = entryOutward;
        FromFloor = fromFloor;
        EntryRampCenter = entryRampCenter;
        ExitMouth = exitMouth;
        ExitOutward = exitOutward;
        ToFloor = toFloor;
        ExitRampCenter = exitRampCenter;
        RampWidth = rampWidth;
        Climbs = climbs;
    }

    /// <summary>Identifies the physical ramp pair; both travel directions share it.</summary>
    public long PairKey { get; }

    /// <summary>
    /// The ramp claim this direction uses (<see cref="RampTraffic"/>). Pairs whose mouth areas overlap share one key,
    /// so only one vehicle at a time drives into the shared area.
    /// </summary>
    public long TrafficKey { get; internal set; }

    /// <summary>
    /// Every cell a vehicle of any size uses at the entry mouth: the union of the entry lanes of this direction and
    /// the exit lanes of the reverse direction. Empty when no vehicle size fits the ramp.
    /// </summary>
    public CellRect EntryApron { get; internal set; }

    /// <summary>The same area at the exit mouth.</summary>
    public CellRect ExitApron { get; internal set; }

    public IntVec3 EntryMouth { get; }

    public IntVec3 EntryOutward { get; }

    public int FromFloor { get; }

    public IntVec3 ExitMouth { get; }

    public IntVec3 ExitOutward { get; }

    public int ToFloor { get; }

    public int RampWidth { get; }

    public Rot4 EntryRotation => Rot4.FromIntVec3(-EntryOutward);

    public Rot4 ExitRotation => Rot4.FromIntVec3(ExitOutward);

    internal Vector3 EntryRampCenter { get; }

    internal Vector3 ExitRampCenter { get; }

    internal bool Climbs { get; }
}
