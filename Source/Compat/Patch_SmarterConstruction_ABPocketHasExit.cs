using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmarterConstruction_ABPocketHasExit
{
    private static MethodBase TargetMethod()
    {
        return SmarterConstructionCompat.ClosedRegionMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && SmarterConstructionCompat.WouldEncloseThingsMethod() != null;
    }

    private static void Postfix(HashSet<IntVec3> __result)
    {
        SmarterConstructionCompat.PruneEscapablePockets(__result);
    }
}
