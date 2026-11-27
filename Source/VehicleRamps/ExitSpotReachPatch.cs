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

/// <summary>Passes the actual vehicle to the pawn-less reachability calls in VF's exit-spot finders.</summary>
public static class ExitSpotReachPatch
{
    private static readonly MethodInfo s_nativeReach = AccessTools.Method(typeof(VehicleReachability),
        nameof(VehicleReachability.CanReachVehicle),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseMode), typeof(Danger) });

    internal static (MethodInfo Best, MethodInfo RandomValidator) ResolveTargets()
    {
        var finder = typeof(CellFinderExtended);
        var parameters = new[] { typeof(VehiclePawn), typeof(IntVec3).MakeByRefType(), typeof(TraverseMode) };
        var best = MemberBinding.RequireMethod(finder, nameof(CellFinderExtended.TryFindBestExitSpot), parameters,
            typeof(bool));
        var random = MemberBinding.RequireMethod(finder, nameof(CellFinderExtended.TryFindRandomExitSpot), parameters,
            typeof(bool));
        var validators = PatchProcessor.GetOriginalInstructions(random)
            .Select(code => code.operand)
            .OfType<MethodInfo>()
            .Where(method => method.DeclaringType == finder && method.Name.StartsWith("<TryFindRandomExitSpot>",
                StringComparison.Ordinal)
                && PatchProcessor.GetOriginalInstructions(method).Any(code => code.Calls(s_nativeReach)))
            .Distinct()
            .ToArray();
        if (validators.Length != 1)
        {
            throw new InvalidOperationException("expected one pawn-less reachability validator in TryFindRandomExitSpot");
        }

        var randomValidator = validators[0];
        BestVehicleLoads(best);
        RandomVehicleLoads(randomValidator);
        FindAnchor(PatchProcessor.GetOriginalInstructions(best).ToList(), best);
        FindAnchor(PatchProcessor.GetOriginalInstructions(randomValidator).ToList(), randomValidator);
        return (best, randomValidator);
    }

    public static IEnumerable<CodeInstruction> BestTranspiler(IEnumerable<CodeInstruction> instructions,
        MethodBase original) => RewriteCall(instructions, original, BestVehicleLoads(original));

    public static IEnumerable<CodeInstruction> RandomValidatorTranspiler(IEnumerable<CodeInstruction> instructions,
        MethodBase original) => RewriteCall(instructions, original, RandomVehicleLoads(original));

    public static bool CanReach(VehicleReachability reachability, IntVec3 start, LocalTargetInfo destination,
        PathEndMode endMode, TraverseMode mode, Danger maxDanger, VehiclePawn vehicle)
    {
        if (!PathStartPatch.NeedsVehicleDef(reachability, vehicle?.VehicleDef))
        {
            return reachability.CanReachVehicle(start, destination, endMode, mode, maxDanger);
        }

        // Match the parameters of VF's pawn-less overload, then supply the actual vehicle.
        var traverseParms = TraverseParms.For(mode, maxDanger, false, false, false, true, false);
        traverseParms.pawn = vehicle;
        return reachability.CanReachVehicle(start, destination, endMode, traverseParms);
    }

    private static CodeInstruction[] BestVehicleLoads(MethodBase method)
    {
        if (!method.IsStatic || method.GetParameters()[0].ParameterType != typeof(VehiclePawn))
        {
            throw new InvalidOperationException("TryFindBestExitSpot no longer receives the vehicle as argument zero");
        }

        return new[] { new CodeInstruction(OpCodes.Ldarg_0) };
    }

    private static CodeInstruction[] RandomVehicleLoads(MethodBase method)
    {
        var parameters = method.GetParameters();
        if (!method.IsStatic || parameters.Length != 2 || !parameters[1].ParameterType.IsByRef
            || parameters[0].ParameterType != typeof(IntVec3) || ((MethodInfo)method).ReturnType != typeof(bool))
        {
            throw new InvalidOperationException("TryFindRandomExitSpot validator no longer has a byref closure");
        }

        var closure = parameters[1].ParameterType.GetElementType();
        var vehicle = MemberBinding.RequireField(closure, "vehicle", typeof(VehiclePawn));
        return new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldfld, vehicle) };
    }

    private static int FindAnchor(List<CodeInstruction> codes, MethodBase method)
    {
        var index = -1;
        for (var i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(s_nativeReach))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new InvalidOperationException("multiple pawn-less reachability calls in " + method.Name);
            }

            index = i;
        }

        if (index < 0 || codes[index].blocks.Count != 0)
        {
            throw new InvalidOperationException("no compatible pawn-less reachability call in " + method.Name);
        }

        return index;
    }

    private static IEnumerable<CodeInstruction> RewriteCall(IEnumerable<CodeInstruction> instructions,
        MethodBase original, CodeInstruction[] vehicleLoads)
    {
        var codes = new List<CodeInstruction>(instructions);
        var index = FindAnchor(codes, original);
        vehicleLoads[0].labels.AddRange(codes[index].labels);
        codes[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ExitSpotReachPatch), nameof(CanReach)));
        codes.InsertRange(index, vehicleLoads);
        return codes;
    }
}
