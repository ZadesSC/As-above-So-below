using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;

[HarmonyPatch]
public static class Patch_FoodUtility_ABInnerScanContext
{
    [ThreadStatic]
    private static ABBandDistance.IslandMemo reusableMemo;

    [ThreadStatic]
    private static ABBandDistance.IslandMemo activeMemo;

    internal static ABBandDistance.IslandMemo Memo => activeMemo;

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(FoodUtility), "SpawnedFoodSearchInnerScan");
    }

    private static void Prefix(Pawn eater, IntVec3 root, TraverseParms traverseParams,
        out ABBandDistance.IslandMemo __state)
    {
        __state = activeMemo;
        activeMemo = null;
        try
        {
            Pawn pawn = traverseParams.pawn ?? eater;
            Map map = pawn?.Map ?? eater?.Map;
            if (!UnityData.IsInMainThread || map == null || !ABBands.Banded(map)
                || ABWormhole.PairCount(map) == 0)
            {
                return;
            }

            reusableMemo ??= new ABBandDistance.IslandMemo();
            activeMemo = reusableMemo.Active ? new ABBandDistance.IslandMemo() : reusableMemo;
            activeMemo.Begin(map, pawn, root);
        }
        catch (Exception ex)
        {
            activeMemo?.End();
            activeMemo = null;
            Log.WarningOnce(ABLog.Tag + " V2: food scan context threw: " + ex.Message, 11218688);
        }
    }

    private static Exception Finalizer(Exception __exception, ABBandDistance.IslandMemo __state)
    {
        if (!ReferenceEquals(activeMemo, __state))
        {
            activeMemo?.End();
        }
        activeMemo = __state;
        return __exception;
    }
}
