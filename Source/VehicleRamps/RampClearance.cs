using System;
using SmashTools.PathFinder;
using Unity.Mathematics;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>Vehicle occupancy and the swept lane to each ramp mouth.</summary>
internal static class RampClearance
{
    internal const int ImpassableCost = 10000;

    private static readonly IPathGridCalculator s_calculator = new PathGridCalculator();

    internal static CellRect Lane(VehicleDef def, IntVec3 crossing, IntVec3 mouth, Rot4 rotation)
    {
        var size = def.size;
        var occupied = def.VehicleRect(crossing, rotation);
        var costFootprint = SumTableCostProvider.GetCoords(new int2(size.x, size.z), RampEdge.HeadingOf(rotation));
        costFootprint.GetRect(crossing.x, crossing.z, out var minX, out var minZ, out var maxX, out var maxZ);

        // Both VF footprints must fit. Extend the rectangle along the travel axis through the outside mouth row.
        minX = Math.Min(Math.Min(minX, occupied.minX), mouth.x);
        minZ = Math.Min(Math.Min(minZ, occupied.minZ), mouth.z);
        maxX = Math.Max(Math.Max(maxX, occupied.maxX), mouth.x);
        maxZ = Math.Max(Math.Max(maxZ, occupied.maxZ), mouth.z);
        return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
    }

    /// <summary>Reads VF's cost rules against current map things and terrain, bypassing its debounced grid.</summary>
    internal static bool IsClear(Map map, VehicleDef def, CellRect lane, bool noWater = false)
    {
        foreach (var cell in lane)
        {
            if ((noWater && cell.GetTerrain(map).IsWater) || s_calculator.PathCostAt(map, cell, def) >= ImpassableCost)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when a pawn or vehicle other than the crossing vehicle is in the lane. Vehicles for which
    /// <paramref name="ignore"/> returns true are skipped.
    /// </summary>
    internal static bool IsOccupied(Map map, CellRect lane, VehiclePawn crossingVehicle,
        Func<VehiclePawn, bool> ignore = null)
    {
        foreach (var cell in lane)
        {
            // VF's diagonal footprint can claim cells beyond the pawn entries in RimWorld's thing grid.
            var claimant = PathingHelper.AnyVehicleBlockingPathAt(cell, crossingVehicle);
            if (claimant != null && (ignore == null || !ignore(claimant)))
            {
                return true;
            }

            var things = map.thingGrid.ThingsListAtFast(cell);
            for (var i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != crossingVehicle
                    && (ignore == null || other is not VehiclePawn vehicle || !ignore(vehicle)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
