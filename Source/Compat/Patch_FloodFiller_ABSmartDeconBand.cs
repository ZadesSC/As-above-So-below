using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(FloodFiller), "FloodFill", new Type[] { typeof(IntVec3), typeof(Predicate<IntVec3>), typeof(Func<IntVec3, int, bool>), typeof(int), typeof(bool), typeof(IEnumerable<IntVec3>) })]
public static class Patch_FloodFiller_ABSmartDeconBand
{
    private static bool Prepare()
    {
        return SmarterDeconstructionCompat.Present;
    }

    private static void Prefix(FloodFiller __instance, IntVec3 root, ref Predicate<IntVec3> passCheck)
    {
        SmarterDeconstructionCompat.ClampToBand(__instance, root, ref passCheck);
    }
}
