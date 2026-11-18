using System.Collections.Generic;
using HarmonyLib;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(CastPositionFinder), "EvaluateCell")]
public static class Patch_CastPositionFinder_ABEvaluateCellBandLocal
{
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(CastPositionFinder), "EvaluateCell") != null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return ABCombatPosition.RewritePositionReads(instructions, 1, "EvaluateCell");
    }
}
