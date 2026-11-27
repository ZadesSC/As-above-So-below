using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;

internal static class ABRoofRemoval
{
    private static string Blocker(Map map, IntVec3 cell)
    {
        if (map == null || !cell.InBounds(map))
            return null;

        var bands = ABBands.CompOf(map);
        if (bands == null || !bands.Banded || bands.InGutter(cell))
            return null;

        var roof = map.roofGrid.RoofAt(cell);
        if (roof != RoofDefOf.RoofRockThin && roof != RoofDefOf.RoofConstructed)
            return null;

        int upperBand = bands.BandOf(cell) + 1;
        if (!bands.BandExists(upperBand))
            return null;

        IntVec3 upper = bands.Translate(cell, upperBand);
        if (!upper.InBounds(map) || bands.InGutter(upper)
            || ABSkySync.DerivedLayerAt(map.terrainGrid, upper) != ABDefOf.AB_RoofSurface)
            return null;

        Thing occupant = ABSkySync.RooftopOccupant(map, upper);
        if (occupant is Pawn)
            return "AB_RoofRemovalOccupied".Translate(occupant.LabelShort);
        if (occupant != null)
            return "AB_RoofRemovalBuilding".Translate(occupant.LabelShort);

        return map.roofGrid.Roofed(upper) ? "AB_RoofRemovalUpperRoof".Translate().ToString() : null;
    }

    [HarmonyPatch(typeof(WorkGiver_RemoveRoof), nameof(WorkGiver_RemoveRoof.HasJobOnCell))]
    private static class WorkGiverPatch
    {
        private static void Postfix(Pawn pawn, IntVec3 c, ref bool __result)
        {
            if (__result && Blocker(pawn.Map, c) is string reason)
            {
                JobFailReason.Is(reason);
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_RemoveRoof), "DoEffect")]
    private static class CompletionPatch
    {
        private static bool Prefix(JobDriver_RemoveRoof __instance)
        {
            if (Blocker(__instance.pawn.Map, __instance.job.targetA.Cell) is not string reason)
                return true;

            if (__instance.job.playerForced)
                Messages.Message(reason, __instance.pawn, MessageTypeDefOf.RejectInput, historical: false);

            __instance.EndJobWith(JobCondition.Incompletable);
            return false;
        }
    }
}
