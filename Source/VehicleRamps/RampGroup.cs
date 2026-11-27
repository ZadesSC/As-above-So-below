using UnityEngine;
using Verse;

namespace AASBVehicleRamps;

/// <summary>The two mouths of a paired ramp, in one travel direction.</summary>
public sealed class RampGroup
{
    internal RampGroup(IntVec3 entryMouth, IntVec3 entryOutward, int fromFloor, Vector3 entryRampCenter,
        IntVec3 exitMouth, IntVec3 exitOutward, int toFloor, Vector3 exitRampCenter, int rampWidth, bool climbs)
    {
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
