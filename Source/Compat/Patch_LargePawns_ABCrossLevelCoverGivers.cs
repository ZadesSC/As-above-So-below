using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_LargePawns_ABCrossLevelCoverGivers
{
    private static MethodBase TargetMethod()
    {
        return LargePawnsCompat.PostfixOf("LargePawns.Patches.CoverUtility_CalculateCoverGiverSet_LargePatch");
    }

    private static bool Prepare()
    {
        return LargePawnsCompat.Active && LargePawnsCompat.VanillaLos != null && TargetMethod() != null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return LargePawnsCompat.Rewrite(instructions, "the cover list postfix");
    }
}
