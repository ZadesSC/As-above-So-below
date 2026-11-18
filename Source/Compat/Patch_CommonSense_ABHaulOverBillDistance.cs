using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CommonSense_ABHaulOverBillDistance
{
    private static MethodBase TargetMethod()
    {
        return CommonSenseCompat.HaulingOpportunityMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && CommonSenseCompat.CanRewrite;
    }

    private static void Prefix(Pawn __1)
    {
        CommonSenseCompat.Push(__1);
    }

    private static void Finalizer()
    {
        CommonSenseCompat.Pop();
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return CommonSenseCompat.Rewrite(instructions, "Hauling_Opportunity", 2);
    }
}
