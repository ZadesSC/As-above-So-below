using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(MapGenUtility), "BiomeAt")]
[HarmonyPriority(700)]
public static class Patch_MapGenUtility_ABBandBiomeAt
{
    private static bool Prefix(Map map, IntVec3 c, ref BiomeDef __result)
    {
        try
        {
            if (map == null)
            {
                return true;
            }

            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded || !c.InBounds(map))
            {
                return true;
            }

            if (aBBandMap.LevelOf(c) == 0)
            {
                return true;
            }

            BiomeDef biomeDef = ABBandEnv.BiomeOf(map, aBBandMap, c);
            if (biomeDef == null)
            {
                return true;
            }

            __result = biomeDef;
            return false;
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.LevelGen, e, "V2 per-band MapGenUtility.BiomeAt");
        }

        return true;
    }
}
