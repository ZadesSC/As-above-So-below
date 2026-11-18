using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(GenClosest), "ClosestThing_Global_Reachable")]
public static class Patch_GenClosest_ABGlobalReachableBandDistance
{
    [ThreadStatic]
    private static ABBandDistance.IslandMemo memo;
    private static bool Prefix(IntVec3 center, Map map, IEnumerable<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator, Func<Thing, float> priorityGetter, bool canLookInHaulableSources, ref Thing __result)
    {
        try
        {
            if (searchSet == null || map == null || !UnityData.IsInMainThread || !ABBands.Banded(map) || ABWormhole.PairCount(map) == 0 || !center.InBounds(map))
            {
                return true;
            }

            // A validator can run another search. Keep its memo separate so
            // the outer search keeps the same pawn, root and route estimates.
            memo ??= new ABBandDistance.IslandMemo();
            ABBandDistance.IslandMemo searchMemo = memo.Active
                ? new ABBandDistance.IslandMemo()
                : memo;
            searchMemo.Begin(map, traverseParams.pawn, center);
            try
            {
                __result = Search(center, map, searchSet, peMode, traverseParams, maxDistance, validator, priorityGetter, canLookInHaulableSources, searchMemo);
            }
            finally
            {
                searchMemo.End();
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] V2: band-aware closest-thing threw; vanilla ran: " + ex.Message, 11218691);
            return true;
        }
    }

    private static Thing Search(IntVec3 center, Map map, IEnumerable<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator, Func<Thing, float> priorityGetter, bool canLookInHaulableSources, ABBandDistance.IslandMemo memo)
    {
        Thing bestThing = null;
        float bestPrio = float.MinValue;
        float maxDistanceSquared = maxDistance * maxDistance;
        float closestDistSquared = 2.1474836E+09f;
        bool careAboutHaulSourceEnabled = canLookInHaulableSources && traverseParams.pawn != null && traverseParams.pawn.IsColonist;
        if (searchSet is IList<Thing> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Process(list[i]);
            }
        }
        else if (searchSet is IList<Pawn> list2)
        {
            for (int j = 0; j < list2.Count; j++)
            {
                Process(list2[j]);
            }
        }
        else if (searchSet is IList<Building> list3)
        {
            for (int k = 0; k < list3.Count; k++)
            {
                Process(list3[k]);
            }
        }
        else
        {
            foreach (Thing item in searchSet)
            {
                Process(item);
            }
        }

        return bestThing;
        void Process(Thing t)
        {
            if (t != null && t.Spawned)
            {
                IntVec3 positionHeld = t.PositionHeld;
                float num = ((!memo.TryDistance(positionHeld, manhattan: false, out var length)) ? ((float)(center - positionHeld).LengthHorizontalSquared) : (length * length));
                if (!(num > maxDistanceSquared) && (priorityGetter != null || !(num >= closestDistSquared)))
                {
                    ValidateThing(t, num);
                    if (canLookInHaulableSources && t is IHaulSource haulSource && (!careAboutHaulSourceEnabled || haulSource.HaulSourceEnabled))
                    {
                        ThingOwner directlyHeldThings = haulSource.GetDirectlyHeldThings();
                        for (int l = 0; l < directlyHeldThings.Count; l++)
                        {
                            ValidateThing(directlyHeldThings[l], num);
                        }
                    }
                }
            }
        }

        void ValidateThing(Thing t, float distSquared)
        {
            if (map.reachability.CanReach(center, t.SpawnedParentOrMe, peMode, traverseParams) && (validator == null || validator(t)))
            {
                float num = 0f;
                if (priorityGetter != null)
                {
                    num = priorityGetter(t);
                    if (num < bestPrio || (Mathf.Approximately(num, bestPrio) && distSquared >= closestDistSquared))
                    {
                        return;
                    }
                }

                bestThing = t;
                closestDistSquared = distSquared;
                bestPrio = num;
            }
        }
    }
}
