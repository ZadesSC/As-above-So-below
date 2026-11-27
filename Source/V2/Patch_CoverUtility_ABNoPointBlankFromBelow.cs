using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(CoverUtility), "TryFindAdjustedCoverInCell")]
public static class Patch_CoverUtility_ABNoPointBlankFromBelow
{
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(CoverUtility), "TryFindAdjustedCoverInCell") != null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo methodInfo = AccessTools.PropertyGetter(typeof(IntVec3), "LengthHorizontal");
        MethodInfo operand = AccessTools.Method(typeof(ABCombatCover), "PointBlankDistance");
        List<CodeInstruction> list = new List<CodeInstruction>(instructions);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && list[i].operand as MethodInfo == methodInfo)
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = operand;
                num++;
            }
        }

        ABCombatCover.pointBlankRuleArmed = num >= 1;
        if (num != 1)
        {
            Log.Warning("[As above, So below] cover: expected exactly one shooter-to-cover distance read in CoverUtility.TryFindAdjustedCoverInCell, found " + num + ". " + ((num == 0) ? "Fire from a lower level keeps vanilla's point-blank cover discount." : "All of them now skip the discount for fire from below; report this."));
        }

        return list;
    }
}
