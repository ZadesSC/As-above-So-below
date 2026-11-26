using System;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(CoverUtility), "CalculateCoverGiverSet")]
public static class Patch_CoverUtility_ABCrossBandCoverGivers
{
    [HarmonyPriority(800)]
    private static void Prefix(LocalTargetInfo target, ref IntVec3 shooterLoc, Map map, out ABCombatCover.Scope __state)
    {
        __state = ABCombatCover.Snapshot();
        try
        {
            ABCombatCover.Enter(target, ref shooterLoc, map);
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "V2 cross-band cover givers");
        }
    }

    private static void Finalizer(ABCombatCover.Scope __state)
    {
        ABCombatCover.Exit(__state);
    }
}
