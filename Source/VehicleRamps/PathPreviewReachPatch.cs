using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>Uses the debug preview's selected vehicle or def for its preliminary reachability check.</summary>
public static class PathPreviewReachPatch
{
    [ThreadStatic] private static ChunkSearch s_search;
    [ThreadStatic] private static VehicleDef s_def;

    internal static MethodInfo ResolveTarget(MethodInfo nativeReach)
    {
        var preview = MemberBinding.RequireType(AccessTools.TypeByName("Vehicles.Editor.PathPreview"),
            "Vehicles.Editor.PathPreview");
        MemberBinding.RequireField(preview, "settings", typeof(PathSettings));
        MemberBinding.RequireField(typeof(PathSettings), "vehicle", typeof(VehiclePawn));
        MemberBinding.RequireField(typeof(VehicleReachability), "chunkSearch", typeof(ChunkSearch));
        var target = MemberBinding.RequireMethod(preview, "PrimaryMouseDown", new[] { typeof(IntVec3) }, typeof(void));
        if (target.IsStatic)
        {
            throw new InvalidOperationException("PathPreview.PrimaryMouseDown no longer has a preview instance");
        }

        PathStartPatch.FindAnchor(PatchProcessor.GetOriginalInstructions(target).ToList(), nativeReach);
        return target;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var codes = new List<CodeInstruction>(instructions);
        var nativeReach = AccessTools.Method(typeof(VehicleReachability), nameof(VehicleReachability.CanReachVehicle),
            new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) });
        var index = PathStartPatch.FindAnchor(codes, nativeReach);
        var settings = MemberBinding.RequireField(original.DeclaringType, "settings", typeof(PathSettings));
        var loadPreview = new CodeInstruction(OpCodes.Ldarg_0);
        loadPreview.labels.AddRange(codes[index].labels);
        codes[index] = new CodeInstruction(OpCodes.Call,
            AccessTools.Method(typeof(PathPreviewReachPatch), nameof(CanReach)));
        codes.InsertRange(index, new[] { loadPreview, new CodeInstruction(OpCodes.Ldfld, settings) });
        return codes;
    }

    public static bool CanReach(VehicleReachability reachability, IntVec3 start, LocalTargetInfo destination,
        PathEndMode endMode, TraverseParms traverseParms, PathSettings settings)
    {
        if (settings.vehicle != null)
        {
            return PathStartPatch.CanReachForPawn(reachability, start, destination, endMode, traverseParms,
                settings.vehicle);
        }

        if (!PathStartPatch.NeedsVehicleDef(reachability, settings.vehicleDef) || reachability.chunkSearch == null)
        {
            return reachability.CanReachVehicle(start, destination, endMode, traverseParms);
        }

        // The debug menu selects a def, not a pawn. Scope that choice to the native search for this query.
        var previousSearch = s_search;
        var previousDef = s_def;
        s_search = reachability.chunkSearch;
        s_def = settings.vehicleDef;
        try
        {
            return reachability.CanReachVehicle(start, destination, endMode, traverseParms);
        }
        finally
        {
            s_search = previousSearch;
            s_def = previousDef;
        }
    }

    internal static VehicleDef SelectedDef(ChunkSearch search) => s_search == search ? s_def : null;
}
