using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmarterConstruction_ABRankingDistance
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return SmarterConstructionCompat.DistanceLambdaMethods();
    }

    private static bool Prepare()
    {
        if (!SmarterConstructionCompat.CanRewrite || SmarterConstructionCompat.ClosestCustomMethod() == null)
        {
            return false;
        }

        using (IEnumerator<MethodBase> enumerator = SmarterConstructionCompat.DistanceLambdaMethods().GetEnumerator())
        {
            if (enumerator.MoveNext())
            {
                MethodBase current = enumerator.Current;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return SmarterConstructionCompat.Rewrite(instructions, "ClosestThing_Global_Reachable_Custom projection (" + ((__originalMethod != null) ? __originalMethod.Name : "?") + ")");
    }
}
