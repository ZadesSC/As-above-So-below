using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Postfix on <c>VehicleReachability.CanReachVehicle(IntVec3, LocalTargetInfo, PathEndMode, TraverseParms)</c>, kept
/// only for the two traverse modes Vehicle Framework answers with a cell flood fill instead of its region search
/// (<c>ChunkSearch.CanReachByCell</c>: <see cref="TraverseMode.NoPassClosedDoorsOrWater"/> and
/// <see cref="TraverseMode.PassAllDestroyableThingsNotWater"/>). That flood fill is RimWorld's shared
/// <c>FloodFiller</c>, which has no hook for extra cell links, so these modes keep the 0.2.0 chain. Every other mode
/// is answered by Vehicle Framework itself: the region search carries ramp links (<see cref="RegionPortalPatch"/>)
/// and the two breach modes run a real path search that already crosses ramps.
/// <para>
/// When Vehicle Framework answers false, the chain runs a depth-first search over
/// ramp exit cells: from a cell, every usable crossing whose entry Vehicle Framework says the vehicle reaches leads
/// to that crossing's exit cell, and the search continues from every reached exit cell (including ones on the
/// destination's floor that cannot reach the destination directly); the destination is reachable if some reached
/// exit on its floor reaches it. Each exit cell is explored at most once per query, with a backstop of 4,096 native
/// calls. The chain uses the vehicle's size-specific crossings and checks both lanes as in the region search.
/// </para>
/// </summary>
public static class ReachabilityPatch
{
    // Backstop on native reachability calls per query. The exit-cell memo normally keeps far below this; running
    // out answers "unreachable" and logs once at verbose level.
    private const int MaxReachCalls = 4096;

    [ThreadStatic] private static bool s_searching;
    [ThreadStatic] private static int s_reachCalls;
    [ThreadStatic] private static HashSet<int> s_explored;
    [ThreadStatic] private static LaneStatus[] s_clearance;
    private static bool s_budgetLogged;

    public static void Postfix(VehicleReachability __instance, IntVec3 start, LocalTargetInfo dest,
        PathEndMode peMode, TraverseParms traverseParms, ref bool __result)
    {
        if (__result || s_searching || !dest.IsValid || !UsesChain(traverseParms.mode) || !UnityData.IsInMainThread)
        {
            return;
        }

        var map = __instance.map;
        var def = (traverseParms.pawn as VehiclePawn)?.VehicleDef ?? __instance.CreatedFor;
        if (map == null || def == null)
        {
            return;
        }

        var table = RampRegistry.FreshTable(map);
        var crossings = table.ForSize(def.size);
        if (crossings == null || crossings.Edges.IsEmpty)
        {
            return;
        }

        var startFloor = table.FloorOf(start);
        var destFloor = table.FloorOf(dest.Cell);
        if (startFloor < 0 || destFloor < 0 || crossings.FromFloor(startFloor).IsEmpty
            || !table.FloorsConnected(startFloor, destFloor)
            || (startFloor == destFloor && RampDiagnostics.ReachMode == ReachTestMode.LegacyChain))
        {
            return;
        }

        s_searching = true;
        s_reachCalls = 0;
        var explored = s_explored ??= new HashSet<int>();
        explored.Clear();
        if (s_clearance == null || s_clearance.Length < crossings.Edges.Length)
        {
            s_clearance = new LaneStatus[crossings.Edges.Length];
        }
        else
        {
            Array.Clear(s_clearance, 0, crossings.Edges.Length);
        }

        try
        {
            var query = new Query(__instance, map, def, crossings, dest, destFloor, peMode, traverseParms, explored);
            if (query.FromCell(start, startFloor))
            {
                __result = true;
                RampDiagnostics.Add(ref RampDiagnostics.ReachOverrides);
            }
            else if (s_reachCalls > MaxReachCalls && !s_budgetLogged && RampLog.Verbose)
            {
                s_budgetLogged = true;
                RampLog.Debug("reachability search budget (" + MaxReachCalls + " calls) ran out; answered unreachable");
            }
        }
        finally
        {
            s_searching = false;
        }
    }

    /// <summary>The flood-fill modes, or every mode under the test-only 0.2.0 comparison.</summary>
    private static bool UsesChain(TraverseMode mode) => RampDiagnostics.ReachMode switch
    {
        ReachTestMode.NativeOnly => false,
        ReachTestMode.LegacyChain => true,
        _ => AvoidsWater(mode),
    };

    private static bool AvoidsWater(TraverseMode mode) =>
        mode == TraverseMode.NoPassClosedDoorsOrWater || mode == TraverseMode.PassAllDestroyableThingsNotWater;

    private enum LaneStatus : byte
    {
        Unknown,
        Clear,
        Blocked,
    }

    private readonly struct Query
    {
        private readonly VehicleReachability _reachability;
        private readonly Map _map;
        private readonly VehicleDef _def;
        private readonly RampCrossings _crossings;
        private readonly LocalTargetInfo _dest;
        private readonly int _destFloor;
        private readonly PathEndMode _peMode;
        private readonly TraverseParms _traverseParms;
        private readonly HashSet<int> _explored;
        private readonly bool _noWater;

        public Query(VehicleReachability reachability, Map map, VehicleDef def, RampCrossings crossings, LocalTargetInfo dest,
            int destFloor, PathEndMode peMode, TraverseParms traverseParms, HashSet<int> explored)
        {
            _reachability = reachability;
            _map = map;
            _def = def;
            _crossings = crossings;
            _dest = dest;
            _destFloor = destFloor;
            _peMode = peMode;
            _traverseParms = traverseParms;
            _explored = explored;
            _noWater = AvoidsWater(traverseParms.mode);
        }

        public bool FromCell(IntVec3 from, int floor)
        {
            foreach (var index in _crossings.FromFloor(floor))
            {
                if (s_reachCalls > MaxReachCalls)
                {
                    return false;
                }

                ref readonly var edge = ref _crossings.Edges[index];
                // Only memoise exits actually reached. A different mouth can lead to the same exit.
                if (_explored.Contains(edge.ExitIndex) || !LanesClear(index, in edge)
                    || !Reach(from, edge.Entry, PathEndMode.OnCell))
                {
                    continue;
                }

                _explored.Add(edge.ExitIndex);
                if ((edge.ExitFloor == _destFloor && Reach(edge.Exit, _dest, _peMode))
                    || FromCell(edge.Exit, edge.ExitFloor))
                {
                    return true;
                }

                if (RampDiagnostics.Enabled)
                {
                    RampDiagnostics.Event("reach: exit " + edge.Exit + " led nowhere (" + s_reachCalls + " calls)");
                }
            }

            return false;
        }

        private bool LanesClear(int index, in RampEdge edge)
        {
            if (s_clearance[index] == LaneStatus.Unknown)
            {
                RampDiagnostics.Add(ref RampDiagnostics.ReachChainLaneChecks);
                s_clearance[index] = RampClearance.IsClear(_map, _def, edge.EntryLane, _noWater)
                    && RampClearance.IsClear(_map, _def, edge.ExitLane, _noWater)
                    ? LaneStatus.Clear
                    : LaneStatus.Blocked;
            }

            return s_clearance[index] == LaneStatus.Clear;
        }

        private bool Reach(IntVec3 from, LocalTargetInfo to, PathEndMode peMode)
        {
            if (++s_reachCalls > MaxReachCalls)
            {
                return false;
            }

            RampDiagnostics.Add(ref RampDiagnostics.ReachChainCalls);
            return _reachability.CanReachVehicle(from, to, peMode, _traverseParms);
        }
    }
}
