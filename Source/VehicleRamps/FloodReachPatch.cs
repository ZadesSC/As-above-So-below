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

/// <summary>Uses the same vehicle def for flood-fill arrival as VF uses to validate the reachability request.</summary>
public static class FloodReachPatch
{
    private static readonly MethodInfo s_vehicleDef = AccessTools.PropertyGetter(typeof(VehiclePawn),
        nameof(VehiclePawn.VehicleDef));

    internal static MethodInfo ResolveTarget(List<CodeInstruction> floodCodes)
    {
        var arrival = MemberBinding.RequireMethod(typeof(VehicleReachabilityImmediate), "CanReachImmediateVehicle",
            new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Map), typeof(VehicleDef), typeof(PathEndMode) },
            typeof(bool));
        var candidates = floodCodes
            .Where(code => code.opcode == OpCodes.Ldftn)
            .Select(code => code.operand)
            .OfType<MethodInfo>()
            .Where(method => method.ReturnType == typeof(bool) && method.DeclaringType?.DeclaringType == typeof(ChunkSearch)
                && PatchProcessor.GetOriginalInstructions(method).Any(code => code.Calls(arrival)))
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException("ChunkSearch.CanReachByCell no longer has one arrival predicate");
        }

        FindAnchor(PatchProcessor.GetOriginalInstructions(candidates[0]).ToList(), candidates[0], out _);
        return candidates[0];
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var codes = instructions.ToList();
        var ownerField = FindAnchor(codes, original, out var index);
        var loadClosure = new CodeInstruction(OpCodes.Ldarg_0);
        loadClosure.labels.AddRange(codes[index].labels);
        codes[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloodReachPatch), nameof(VehicleDefFor)));
        codes.InsertRange(index, new[] { loadClosure, new CodeInstruction(OpCodes.Ldfld, ownerField) });
        return codes;
    }

    public static VehicleDef VehicleDefFor(VehiclePawn pawn, ChunkSearch search) => pawn?.VehicleDef ?? search.createdFor;

    private static FieldInfo FindAnchor(List<CodeInstruction> codes, MethodBase original, out int index)
    {
        index = -1;
        for (var i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(s_vehicleDef))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new InvalidOperationException("multiple vehicle-def reads in the flood-fill arrival predicate");
            }

            index = i;
        }

        if (index < 0 || codes[index].blocks.Count != 0)
        {
            throw new InvalidOperationException("no compatible vehicle-def read in the flood-fill arrival predicate");
        }

        return MemberBinding.RequireField(original.DeclaringType, "<>4__this", typeof(ChunkSearch));
    }
}
