using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(FloodFiller), "FloodFill", new Type[] { typeof(IntVec3), typeof(Predicate<IntVec3>), typeof(Func<IntVec3, int, bool>), typeof(int), typeof(bool), typeof(IEnumerable<IntVec3>) })]
public static class Patch_FloodFiller_ABGLAnchorClamp
{
    private static readonly AccessTools.FieldRef<FloodFiller, Map> MapRef = AccessTools.FieldRefAccess<FloodFiller, Map>("map");
    private static bool Prepare()
    {
        return GeologicalLandformsCompat.PostProcessBiomeGridTarget != null;
    }

    private static void Prefix(FloodFiller __instance, ref Predicate<IntVec3> passCheck)
    {
        try
        {
            if (passCheck == null)
            {
                return;
            }

            int rows = Patch_GL_ABBiomeTransitionScope.ClampRowsFor(MapRef(__instance));
            if (rows > 0)
            {
                Predicate<IntVec3> inner = passCheck;
                passCheck = (IntVec3 c) => c.z >= 0 && c.z < rows && inner(c);
                Patch_GL_ABBiomeTransitionScope.fillsClamped++;
            }
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] V2: GL biome transition flood clamp threw: " + ex, 762197003);
        }
    }
}
