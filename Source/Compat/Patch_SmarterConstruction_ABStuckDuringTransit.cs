using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmarterConstruction_ABStuckDuringTransit
{
    private static MethodBase TargetMethod()
    {
        return SmarterConstructionCompat.IsPawnStuckMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    private static void Postfix(Pawn __0, ref bool __result)
    {
        if (__result && SmarterConstructionCompat.HeldByTransit(__0))
        {
            __result = false;
            SmarterConstructionCompat.totalStuckSuppressed++;
            ABLog.Dev("Smarter Construction: suppressed a stuck verdict for " + ((__0 != null) ? __0.LabelShortCap : "?") + ", who is mid-transit.");
        }
    }
}
