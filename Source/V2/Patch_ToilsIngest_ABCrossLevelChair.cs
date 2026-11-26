using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(Toils_Ingest), "TryFindChairOrSpot")]
public static class Patch_ToilsIngest_ABCrossLevelChair
{
    private const float FlightCost = 7f;
    private static readonly List<KeyValuePair<Building_Door, Building_Door>> tmpPairs = new List<KeyValuePair<Building_Door, Building_Door>>();
    private static void Postfix(Pawn pawn, Thing ingestible, ref IntVec3 cell, ref bool __result)
    {
        try
        {
            if (pawn == null || !pawn.Spawned || ingestible == null || !UnityData.IsInMainThread)
            {
                return;
            }

            Map map = pawn.Map;
            if (map != null && ABBands.Banded(map) && ABWormhole.PairCount(map) != 0 && (!__result || !cell.IsValid || !IsSittable(cell.GetEdifice(map))))
            {
                float num = 0f;
                IngestibleProperties ingestibleProperties = ingestible.def?.ingestible;
                if (ingestibleProperties != null)
                {
                    num = ingestibleProperties.chairSearchRadius;
                }
                else if (ingestible is Building_NutrientPasteDispenser { DispensableDef: var dispensableDef } building_NutrientPasteDispenser && dispensableDef?.ingestible != null)
                {
                    num = building_NutrientPasteDispenser.DispensableDef.ingestible.chairSearchRadius;
                }

                if (!(num <= 0f) && TryFindCrossLevelChair(pawn, map, num, out var seat))
                {
                    cell = seat;
                    __result = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] V2: cross-level chair search threw: " + ex.Message, 11218690);
        }
    }

    private static bool IsSittable(Building b)
    {
        return b != null && b.def.building != null && b.def.building.isSittable;
    }

    private static bool TryFindCrossLevelChair(Pawn pawn, Map map, float radius, out IntVec3 seat)
    {
        seat = IntVec3.Invalid;
        tmpPairs.Clear();
        ABWormhole.CollectPairs(map, tmpPairs);
        IntVec3 position = pawn.Position;
        float best = float.MaxValue;
        Thing bestChair = null;
        for (int i = 0; i < tmpPairs.Count; i++)
        {
            if (!Consider(pawn, map, position, tmpPairs[i].Key, tmpPairs[i].Value, radius, ref best, ref bestChair))
            {
                Consider(pawn, map, position, tmpPairs[i].Value, tmpPairs[i].Key, radius, ref best, ref bestChair);
            }
        }

        tmpPairs.Clear();
        if (bestChair == null)
        {
            return false;
        }

        return Toils_Ingest.TryFindFreeSittingSpotOnThing(bestChair, pawn, out seat);
    }

    private static bool Consider(Pawn pawn, Map map, IntVec3 from, Building_Door near, Building_Door far, float radius, ref float best, ref Thing bestChair)
    {
        if (near == null || far == null || !near.Spawned || !far.Spawned || !ABBands.SameBand(map, near.Position, from))
        {
            return false;
        }

        if (ABBands.SameBand(map, far.Position, from))
        {
            return true;
        }

        float costToExit = (near.Position - from).LengthHorizontal + FlightCost;
        float remaining = radius - costToExit;
        if (remaining < 0f || costToExit >= best)
        {
            return true;
        }

        if (!pawn.CanReach(near, PathEndMode.OnCell, Danger.Deadly))
        {
            return true;
        }

        IntVec3 farRoot = far.Position;
        var request = ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        var traverse = TraverseParms.For(pawn);
        bool ValidChair(Thing chair) => ABBands.SameBand(map, chair.Position, farRoot)
            && ChairValidator(pawn, chair) && chair.Position.GetDangerFor(pawn, map) == Danger.None;

        Thing thing = GenClosest.ClosestThingReachable(farRoot, map, request, PathEndMode.OnCell,
            traverse, remaining, ValidChair);
        // Region search excludes the radius itself, including the root when the radius is zero.
        thing ??= GenClosest.ClosestThing_Global(farRoot, map.listerThings.ThingsMatching(request),
            remaining, chair => ValidChair(chair) && map.reachability.CanReach(farRoot, chair, PathEndMode.OnCell, traverse));
        if (thing == null)
        {
            return true;
        }

        float total = costToExit + (thing.Position - farRoot).LengthHorizontal;
        if (total <= radius && total < best)
        {
            best = total;
            bestChair = thing;
        }

        return true;
    }

    private static bool ChairValidator(Pawn pawn, Thing t)
    {
        if (t.def.building == null || !t.def.building.isSittable)
        {
            return false;
        }

        if (t.Faction != pawn.Faction && pawn.Faction != null && pawn.Faction.IsPlayer)
        {
            return false;
        }

        if (!Toils_Ingest.TryFindFreeSittingSpotOnThing(t, pawn, out var cell))
        {
            return false;
        }

        if (t.IsForbidden(pawn))
        {
            return false;
        }

        if (pawn.IsColonist && t.Position.Fogged(t.Map))
        {
            return false;
        }

        if (!pawn.CanReserve(t))
        {
            return false;
        }

        if (!t.IsSociallyProper(pawn))
        {
            return false;
        }

        if (t.IsBurning())
        {
            return false;
        }

        if (t.HostileTo(pawn))
        {
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            Building edifice = (cell + GenAdj.CardinalDirections[i]).GetEdifice(t.Map);
            if (edifice != null && edifice.def.surfaceType == SurfaceType.Eat)
            {
                return true;
            }
        }

        return false;
    }
}
