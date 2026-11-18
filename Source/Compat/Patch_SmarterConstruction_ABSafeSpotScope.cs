using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmarterConstruction_ABSafeSpotScope
{
    private static MethodBase TargetMethod()
    {
        return SmarterConstructionCompat.FindSafeSpotsMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && SmarterConstructionCompat.ClosedRegionMethod() != null;
    }

    private static void Prefix(Thing __0)
    {
        SmarterConstructionCompat.PushFor(__0);
    }

    private static void Finalizer()
    {
        SmarterConstructionCompat.Pop();
    }
}
