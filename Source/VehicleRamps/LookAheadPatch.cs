using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SmashTools;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Transpiler on <c>VehiclePathFollower.NeedNewPath</c>. Its collision look-ahead orients the vehicle footprint at
/// each upcoming node by <c>Ext_Map.DirectionToCell(previous, node)</c>. Across a ramp crossing that direction runs
/// from the entry on one floor to the exit on another (roughly north or south, even for an east/west ramp), so
/// another vehicle parked beside the exit can be "seen" in a footprint the crossing vehicle never occupies. The
/// look-ahead then answers NeedNew on every tick, re-planning the same crossing forever without reaching the
/// crossing check. The single <c>DirectionToCell</c> call is replaced by <see cref="Direction"/>, which returns
/// the ramp's exit rotation for a crossing and the native direction otherwise.
/// <para>The look-ahead waits behind a vehicle that is moving and not waiting, and plans around one that is parked
/// or waiting. A vehicle stopped in line for a ramp is still moving to Vehicle Framework, so the vehicle holding
/// that ramp would wait for it forever after crossing towards it. The single <c>Waiting</c> read is replaced by
/// <see cref="BlockerWaiting"/>, which also reports the queued vehicle as waiting to the ramp's holder only.</para>
/// </summary>
public static class LookAheadPatch
{
    /// <summary>Exactly one <c>Ext_Map.DirectionToCell(IntVec3, IntVec3)</c> call; null on success.</summary>
    public static string FindAnchor(List<CodeInstruction> codes, out int index)
    {
        index = -1;
        var found = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(Native))
            {
                found++;
                index = i;
            }
        }

        if (found != 1)
        {
            return "expected 1 DirectionToCell call in VehiclePathFollower.NeedNewPath, found " + found;
        }

        return FindWaitingAnchor(codes, out _);
    }

    /// <summary>Exactly one <c>VehiclePathFollower.Waiting</c> read; null on success.</summary>
    public static string FindWaitingAnchor(List<CodeInstruction> codes, out int index)
    {
        index = -1;
        var found = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(NativeWaiting))
            {
                found++;
                index = i;
            }
        }

        return found == 1 ? null : "expected 1 Waiting read in VehiclePathFollower.NeedNewPath, found " + found;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var failure = FindAnchor(codes, out var index);
        if (failure != null)
        {
            throw new InvalidOperationException(failure);
        }

        // Stack before the call: from, to. Push the follower and call Direction(from, to, follower).
        var loadFollower = new CodeInstruction(OpCodes.Ldarg_0);
        loadFollower.labels.AddRange(codes[index].labels);
        loadFollower.blocks.AddRange(codes[index].blocks);
        codes[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LookAheadPatch), nameof(Direction)));
        codes.Insert(index, loadFollower);

        // Stack before the read: the blocking vehicle's follower. Push this follower and call BlockerWaiting.
        FindWaitingAnchor(codes, out var waiting);
        var loadSelf = new CodeInstruction(OpCodes.Ldarg_0);
        loadSelf.labels.AddRange(codes[waiting].labels);
        loadSelf.blocks.AddRange(codes[waiting].blocks);
        codes[waiting] = new CodeInstruction(OpCodes.Call,
            AccessTools.Method(typeof(LookAheadPatch), nameof(BlockerWaiting)));
        codes.Insert(waiting, loadSelf);
        return codes;
    }

    public static MethodInfo NativeWaiting => AccessTools.PropertyGetter(typeof(VehiclePathFollower),
        nameof(VehiclePathFollower.Waiting));

    /// <summary>
    /// <paramref name="blocker"/>'s native <c>Waiting</c>, or true when it is in line for the ramp that
    /// <paramref name="follower"/>'s vehicle holds.
    /// </summary>
    public static bool BlockerWaiting(VehiclePathFollower blocker, VehiclePathFollower follower) =>
        blocker.Waiting || RampTraffic.YieldsTo(blocker.vehicle, follower.vehicle);

    public static MethodInfo Native => AccessTools.Method(typeof(Ext_Map), nameof(Ext_Map.DirectionToCell),
        new[] { typeof(IntVec3), typeof(IntVec3) });

    public static Rot8 Direction(IntVec3 from, IntVec3 to, VehiclePathFollower follower)
    {
        if (from == to || to.AdjacentTo8Way(from))
        {
            return Ext_Map.DirectionToCell(from, to);
        }

        var vehicle = follower.vehicle;
        var map = vehicle?.Map;
        if (map != null && RampRegistry.For(map).Published.TryGetEdge(vehicle.VehicleDef.size, from, to, out var edge))
        {
            return edge.ExitRotation;
        }

        return Ext_Map.DirectionToCell(from, to);
    }
}
