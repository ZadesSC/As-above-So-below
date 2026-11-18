using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(FoodUtility), "FoodOptimality")]
public static class Patch_FoodUtility_ABFoodOptimalityDistance
{
    private static void Prefix(Thing foodSource, ref float dist)
    {
        try
        {
            ABBandDistance.IslandMemo memo = Patch_FoodUtility_ABInnerScanContext.Memo;
            if (memo != null && memo.Active && foodSource != null && foodSource.Spawned && memo.TryDistance(foodSource.Position, manhattan: true, out var length))
            {
                dist = length;
            }
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] V2: food distance threw: " + ex.Message, 11218689);
        }
    }
}
