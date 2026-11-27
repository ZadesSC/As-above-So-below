using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>Cross-band request guards for segmented routing, pathing diagnostics and heuristic tuning.</summary>
    public static class ABPathBandScope
    {
        // ---- diagnostics ----------------------------------------------------
        //
        // ⚠ A GUARD CLAUSE THAT SILENTLY EARLY RETURNS IS INDISTINGUISHABLE FROM AN
        // UNIMPLEMENTED FEATURE (§14, learned the hard way on the Dubwise merge). Every
        // early return below is counted, and the last rejection carries its own reason
        // string, so "did this ever fire" is one dev action away instead of a theory.

        public static int rejectedSync;

        public static int rejectedAsync;

        public static int guardCalls;


        public static string lastReject = "none yet";

        public static void ResetStats()
        {
            rejectedSync = 0;
            rejectedAsync = 0;
            guardCalls = 0;
            lastReject = "none yet";
            ABPerfStats.ResetPath();
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AB2 PATHING REPORT");
            sb.AppendLine("  cross band guard: " + guardCalls + " requests inspected, "
                + rejectedSync + " rejected sync (FindPathNow), "
                + rejectedAsync + " rejected async (PushRequest)");
            sb.AppendLine("  last rejection: " + lastReject);
            sb.AppendLine("  heuristic multiplier: " + HeuristicBoost().ToString("0.00")
                + (Mathf.Approximately(HeuristicBoost(), 1f) ? " (off, vanilla accuracy)" : "")
                + "; transit-leg floor " + TransitLegHeuristic.ToString("0.00"));
            sb.AppendLine("  same-island-first: "
                + Patch_GenClosest_ABSameIslandFirst.localHits + " local picks, "
                + Patch_GenClosest_ABSameIslandFirst.fallbacks + " cross-island fallbacks");
            sb.AppendLine("  mech command range: "
                + Patch_MechanitorTracker_ABBandLocalRange.rescued
                + " cross-band orders permitted (0 with no mechanitor, or if no order was "
                + "ever aimed at another level)");
            sb.Append(ABPerfStats.PathReport());
            return sb.ToString();
        }


        internal static float HeuristicBoost()
        {
            ABSettings s = ABMod.Settings;
            return s == null ? 1f : Mathf.Clamp(s.pathHeuristic, MinHeuristic, MaxHeuristic);
        }

        public const float MinHeuristic = 1f;

        public const float MaxHeuristic = 2.5f;

        /// <summary>
        /// Heuristic floor for the LEGS OF A SEGMENTED CROSS-ISLAND TRIP, applied even when
        /// the player's slider is at 1.00 (off).
        ///
        /// ⚠ WHY A FLOOR AND NOT A CHANGED DEFAULT. Turning the slider's shipped default up
        /// would silently make ALL pathing greedier for every user; this instead prices only
        /// the trips §34 newly enabled - long down-across-up hauls where a slightly longer
        /// route is invisible but the A* expansion count is not. Vanilla precedent for the
        /// magnitude: 1.5 for colonists in darkness, 1.75 for animals; 1.35 is milder than
        /// both.
        ///
        /// ⚠ COMPOSED BY MAX(), NEVER MULTIPLIED, with the slider. A player at 2.5 already
        /// gets more than the floor asks for; 2.5 x 1.35 = 3.4 would be a different (and
        /// unrequested) trade.
        /// </summary>
        public const float TransitLegHeuristic = 1.35f;


        // ---- the cross band test --------------------------------------------

        /// <summary>
        /// True when this request can never produce a path because start and destination sit
        /// in different bands.
        ///
        /// ⚠ EVERY EARLY RETURN HERE IS "LET VANILLA HANDLE IT", NEVER "REJECT". A guard that
        /// is wrong in the permissive direction costs a slow path; wrong in the other
        /// direction it silently strands a pawn, and that failure mode looks exactly like the
        /// three unrelated stairwell bugs already recorded on ABWormholePather. So:
        ///
        ///  - Not banded, which includes EVERY MAP DURING GENERATION (⚠ ABBands.Banded is
        ///    FALSE while gensteps run): permit. This is load bearing for GenStep_Roads,
        ///    which calls FindPathNow across the whole stack and must not be interfered with.
        ///  - Either endpoint out of bounds or invalid: permit.
        ///  - Either endpoint IN THE GUTTER: permit. BandOf has to return some band for a
        ///    gutter cell, and which side it picks is an implementation detail we must not
        ///    build a rejection on. A destination on a band edge reached with
        ///    PathEndMode.Touch is the realistic case.
        /// </summary>
        internal static bool CrossBand(Map map, IntVec3 start, LocalTargetInfo target, PathEndMode peMode, IntVec3? exactDestination, out string why)
        {
            why = null;
            if (map == null || !ABBands.Banded(map))
            {
                return false;
            }

            guardCalls++;
            if (!target.IsValid)
            {
                return false;
            }

            IntVec3 cell = target.Cell;
            if (!start.IsValid || !start.InBounds(map) || !cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }

            if (ABBands.InGutter(map, start) || ABBands.InGutter(map, cell))
            {
                return false;
            }

            if (ABBands.SameBand(map, start, cell))
            {
                return false;
            }

            if (exactDestination.HasValue && exactDestination.Value.IsValid && exactDestination.Value != cell && exactDestination.Value.InBounds(map) && (ABBands.InGutter(map, exactDestination.Value) || ABBands.SameBand(map, start, exactDestination.Value)))
            {
                return false;
            }

            if (target.HasThing && target.Thing != null && target.Thing.Spawned)
            {
                Thing thing = target.Thing;
                if (peMode == PathEndMode.InteractionCell && thing.def.hasInteractionCell)
                {
                    IntVec3 interactionCell = thing.InteractionCell;
                    if (interactionCell.InBounds(map) && (ABBands.InGutter(map, interactionCell) || ABBands.SameBand(map, start, interactionCell)))
                    {
                        return false;
                    }
                }

                if ((peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && (thing.def.size.x > 1 || thing.def.size.z > 1))
                {
                    foreach (IntVec3 item in thing.OccupiedRect())
                    {
                        if (item.InBounds(map) && (ABBands.InGutter(map, item) || ABBands.SameBand(map, start, item)))
                        {
                            return false;
                        }
                    }
                }
            }

            string[] array = new string[7];
            IntVec3 intVec = start;
            array[0] = intVec.ToString();
            array[1] = " band ";
            array[2] = ABBands.BandOf(map, start).ToString();
            array[3] = " -> ";
            intVec = cell;
            array[4] = intVec.ToString();
            array[5] = " band ";
            array[6] = ABBands.BandOf(map, cell).ToString();
            why = string.Concat(array);
            return true;
        }

    }

    // =====================================================================================
    // 1. CROSS BAND REQUESTS
    // =====================================================================================

    /// <summary>
    /// The synchronous entry, and the expensive one. FindPathNow calls job.Run(), so the
    /// exhaustive failure search happens ON THE MAIN THREAD, inside whatever JobGiver asked.
    ///
    /// ⚠ ONE PATCH COVERS BOTH OVERLOADS. FindPathNow(start, target, Pawn, ...) does nothing
    /// but resolve bashing flags and delegate to the TraverseParms overload, so patching the
    /// TraverseParms one catches every caller. The vanilla call sites that reach here with a
    /// target on another band are JobGiver_AISapper, JobGiver_Manhunter, JobGiver_ShamblerFight,
    /// JobGiver_AIWaitAmbush, JobGiver_RevenantEscape, JobGiver_Duel, LordToil_EntitySwarm,
    /// PrisonBreakUtility, RCellFinder and RoyalTitleUtility.
    ///
    /// ⚠ PawnPath.NotFound IS THE CORRECT RETURN AND IT DOES NOT NEED DISPOSING. Vanilla
    /// FindPathNow already returns exactly this singleton on three of its own paths, and its
    /// callers wrap the result in `using`, so the contract is unchanged.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PathFinder_ABRejectCrossBandSync
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PathFinder), nameof(PathFinder.FindPathNow), new[]
            {
                typeof(IntVec3),
                typeof(LocalTargetInfo),
                typeof(TraverseParms),
                typeof(PathFinderCostTuning?),
                typeof(PathEndMode),
                typeof(PathRequest.IPathGridCustomizer)
            });
        }
        private static bool Prefix(Map ___map, IntVec3 start, LocalTargetInfo target,
            PathEndMode peMode, ref PawnPath __result)
        {
            ABPerfStats.NoteRequest(___map, sync: true);
            if (ABNativePortals.UsesNativeRouting(___map))
                return true;
            if (!ABPathBandScope.CrossBand(___map, start, target, peMode, null, out string why))
                return true;

            ABPathBandScope.rejectedSync++;
            ABPathBandScope.lastReject = "sync " + why;
            __result = PawnPath.NotFound;
            return false;
        }

    }

    /// <summary>
    /// The asynchronous entry. Resolving the request in place is exactly what
    /// FinalizeRecyclePathJobData does for a search that came back empty, so the requester
    /// sees the same result one tick earlier and the work queue never grows an entry that
    /// can only fail.
    ///
    /// ⚠ THIS DOES NOT TIGHTEN A RETRY LOOP. A failed path ends the job and the think tree
    /// re-runs next tick either way; the retry cadence is the tick, not the search cost.
    /// In practice ABWormholePather has already segmented ordinary pawn movement at
    /// StartPath, so anything reaching here is a third party or a vanilla helper pushing a
    /// request directly.
    /// </summary>
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PushRequest))]
    public static class Patch_PathFinder_ABRejectCrossBandAsync
    {
        private static bool Prefix(PathRequest request)
        {
            if (request == null || request.ResultIsReady || request.Cancelled)
            {
                return true;
            }

            ABPerfStats.NoteRequest(request.map, sync: false);
            if (ABNativePortals.UsesNativeRouting(request.map))
                return true;
            if (ABWormholePather.ConsumeDoomedRequest(request.pawn))
            {
                ABPathBandScope.rejectedAsync++;
                ABPathBandScope.lastReject = "async: no link route (marked by TrySegment)";
                request.Resolve(null);
                return false;
            }

            if (!ABPathBandScope.CrossBand(request.map, request.Start, request.Target, request.EndMode, request.ExactDestination, out var why))
            {
                return true;
            }

            ABPathBandScope.rejectedAsync++;
            ABPathBandScope.lastReject = "async " + why;
            request.Resolve(null);
            return false;
        }
    }


    // =====================================================================================
    // HEURISTIC STRENGTH
    // =====================================================================================

    /// <summary>
    /// A* expansion count is governed by how greedy the heuristic is. PathFinderJob computes
    /// hCost as `octileDistance * 13 * heuristicStrength`, and 13 is exactly
    /// DefaultMoveTicksCardinal, so strength 1.0 is admissible for an ordinary pawn on
    /// cost-free terrain: optimal paths, maximum expansions. Above 1.0 the search is greedier,
    /// visits far fewer cells and may return a slightly longer route.
    ///
    /// ⚠ THIS IS A VANILLA TRADE, NOT A NEW ONE. DetermineHeuristicStrength already returns
    /// 1.75 for every animal and a distance scaled curve for AI pawns, and 1.5 for colonists
    /// under unnatural darkness. The slider extends the same dial to colonists.
    ///
    /// ⚠ GATED ON A BANDED MAP ON PURPOSE. The mod has no business changing pathfinding on a
    /// player's ordinary maps, and pawn == null (map generation, GenStep_Roads, world work)
    /// keeps vanilla's flat 1.0 because there is no map to test.
    /// </summary>
    [HarmonyPatch(typeof(PathFinder), "DetermineHeuristicStrength")]
    public static class Patch_PathFinder_ABHeuristicStrength
    {
        private static void Postfix(Pawn pawn, ref float __result)
        {
            if (pawn == null)
            {
                return;
            }

            Map map = pawn.Map;
            if (map != null && ABBands.Banded(map))
            {
                float num = ABPathBandScope.HeuristicBoost();
                if (ABWormholePather.AnyPending && ABWormholePather.HasPending(pawn))
                {
                    num = Mathf.Max(num, 1.35f);
                }

                if (!(num <= 1f))
                {
                    __result = Mathf.Max(__result, num);
                }
            }
        }
    }
}
