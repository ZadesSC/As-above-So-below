using System.Collections.Generic;
using HarmonyLib;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(CastPositionFinder), "CastPositionPreference")]
public static class Patch_CastPositionFinder_ABPreferenceBandLocal
{
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(CastPositionFinder), "CastPositionPreference") != null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return ABCombatPosition.RewritePositionReads(instructions, 2, "CastPositionPreference");
    }
}
