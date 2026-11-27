using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>Associates a VF path-grid buffer with its map and vehicle size.</summary>
public sealed class GridBinding
{
    public GridBinding(MapRamps ramps, nint key, VehicleDef vehicleDef)
    {
        Ramps = ramps;
        Key = key;
        VehicleDef = vehicleDef;
        VehicleSize = vehicleDef.size;
    }

    public MapRamps Ramps { get; }

    public nint Key { get; }

    public IntVec2 VehicleSize { get; }

    /// <summary>Used by synchronous path jobs on the main thread only.</summary>
    internal VehicleDef VehicleDef { get; }
}
