using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>Supplies the actual vehicle to the follower's reachability check on maps with ramps.</summary>
public static class PathStartPatch
{
    internal static int FindAnchor(List<CodeInstruction> codes, MethodInfo native)
    {
        var index = -1;
        for (var i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(native))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new InvalidOperationException("multiple CanReachVehicle calls in VehiclePathFollower.StartPath");
            }

            index = i;
        }

        if (index < 0 || codes[index].blocks.Count != 0)
        {
            throw new InvalidOperationException("no compatible CanReachVehicle call in VehiclePathFollower.StartPath");
        }

        return index;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var native = AccessTools.Method(typeof(VehicleReachability), nameof(VehicleReachability.CanReachVehicle),
            new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) });
        var index = FindAnchor(codes, native);
        var loadFollower = new CodeInstruction(OpCodes.Ldarg_0);
        loadFollower.labels.AddRange(codes[index].labels);
        codes[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PathStartPatch), nameof(CanReach)));
        codes.Insert(index, loadFollower);
        return codes;
    }

    public static bool CanReach(VehicleReachability reachability, IntVec3 start, LocalTargetInfo destination,
        PathEndMode endMode, TraverseParms traverseParms, VehiclePathFollower follower)
        => CanReachForPawn(reachability, start, destination, endMode, traverseParms, follower.vehicle);

    internal static bool CanReachForPawn(VehicleReachability reachability, IntVec3 start, LocalTargetInfo destination,
        PathEndMode endMode, TraverseParms traverseParms, VehiclePawn vehicle)
    {
        // VF builds these parameters without a pawn, but its shared region owner can have a different length.
        if (NeedsVehicleDef(reachability, vehicle?.VehicleDef))
        {
            traverseParms.pawn = vehicle;
        }

        return reachability.CanReachVehicle(start, destination, endMode, traverseParms);
    }

    internal static bool NeedsVehicleDef(VehicleReachability reachability, VehicleDef def) =>
        def != null && def != reachability.CreatedFor && RampRegistry.HasPendingOrUsableRamps(reachability.map);
}
