using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_HuntRange_ABBandLocal
{
    private static MethodBase TargetMethod()
    {
        return ABCEHuntCompat.RangeLambda();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return ABCEHuntCompat.Rewrite(instructions);
    }
}
