using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SmashTools.PathFinder;
using Unity.Mathematics;
using Verse;
using PathData = SmashTools.PathFinder.PathData;

namespace AASBVehicleRamps;

/// <summary>
/// Adds ramp crossings as extra neighbours inside Vehicle Framework's A* job
/// (<c>SmashTools.PathFinder.FindPathJob&lt;TProvider, THeuristic&gt;.Execute</c>), which runs on job worker
/// threads. The transpiler inserts three calls into each closed instantiation:
/// <list type="bullet">
/// <item><see cref="Begin{TP,TH}"/> at method entry resolves the map's published table once per job.</item>
/// <item><see cref="Expand{TP,TH}"/> after the second native <c>Process</c> call (diagonal neighbours) of every
/// popped node offers the paired exit cell.</item>
/// <item><see cref="End"/> before each return clears the job state and records optional diagnostics.</item>
/// </list>
/// Worker jobs only read immutable <see cref="RampTable"/> data and their native arrays. Synchronous jobs on the
/// main thread can also check live lane costs. <see cref="FloorHeuristic"/> replaces the heuristic call in
/// <c>Process</c> for cross-floor requests.
/// </summary>
public static class PathSearchPatch
{
    // Extra cost of a crossing, in the search's x10 cost units (roughly five cardinal steps).
    private const int CrossingCost = 50;

    // Per approach cell, in the same units. Crossing one cell further out saves a step on each floor (20), so a
    // larger penalty makes the search cross from the nearest cell where the vehicle's hitbox clears the ramp.
    private const int ApproachDistanceCost = 40;
    private const int CardinalStepCost = 10;

    [ThreadStatic] private static RampCrossings s_crossings;
    [ThreadStatic] private static GridBinding s_mainThreadBinding;
    [ThreadStatic] private static long s_nodes;

    /// <summary>The closed job types Vehicle Framework schedules (see SmashTools PathFinder.RequestPath).</summary>
    public static IEnumerable<Type> ClosedJobTypes()
    {
        yield return typeof(FindPathJob<NormalCostProvider, NormalHeuristic>);
        yield return typeof(FindPathJob<NormalCostProvider, WeightedHeuristic>);
        yield return typeof(FindPathJob<SumTableCostProvider, NormalHeuristic>);
        yield return typeof(FindPathJob<SumTableCostProvider, WeightedHeuristic>);
    }

    /// <summary>
    /// Locates the transpiler anchors: exactly two native <c>Process(int, int, int, bool)</c> calls, the second
    /// preceded by <c>ldarg.0; ldloc current; ldloc x; ldloc y; ldc.i4.0</c>, and at least one <c>ret</c>.
    /// Returns null on success or a description of the mismatch.
    /// </summary>
    public static string FindAnchors(List<CodeInstruction> codes, out int secondProcess,
        out CodeInstruction loadCurrent, out List<int> returns)
    {
        secondProcess = -1;
        loadCurrent = null;
        returns = new List<int>();
        var processCalls = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            if (code.opcode == OpCodes.Ret)
            {
                returns.Add(i);
            }

            if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method) || method.Name != "Process")
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (method.DeclaringType == null || !method.DeclaringType.IsGenericType
                || method.DeclaringType.GetGenericTypeDefinition() != typeof(FindPathJob<,>)
                || parameters.Length != 4 || parameters[0].ParameterType != typeof(int)
                || parameters[1].ParameterType != typeof(int) || parameters[2].ParameterType != typeof(int)
                || parameters[3].ParameterType != typeof(bool))
            {
                return "unexpected Process signature " + method;
            }

            processCalls++;
            if (processCalls == 2)
            {
                secondProcess = i;
            }
        }

        if (processCalls != 2)
        {
            return "expected 2 Process calls in FindPathJob.Execute, found " + processCalls;
        }

        if (returns.Count == 0)
        {
            return "no return in FindPathJob.Execute";
        }

        if (secondProcess < 5 || codes[secondProcess - 5].opcode != OpCodes.Ldarg_0
            || !IsLoadLocal(codes[secondProcess - 4].opcode) || codes[secondProcess - 1].opcode != OpCodes.Ldc_I4_0)
        {
            return "second Process call arguments do not match ldarg.0, ldloc current, x, y, false";
        }

        loadCurrent = new CodeInstruction(codes[secondProcess - 4].opcode, codes[secondProcess - 4].operand);
        return null;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase original)
    {
        var codes = new List<CodeInstruction>(instructions);
        var failure = FindAnchors(codes, out var secondProcess, out var loadCurrent, out var returns);
        if (failure != null)
        {
            throw new InvalidOperationException(failure);
        }

        var arguments = original.DeclaringType.GetGenericArguments();
        var begin = AccessTools.Method(typeof(PathSearchPatch), nameof(Begin)).MakeGenericMethod(arguments);
        var expand = AccessTools.Method(typeof(PathSearchPatch), nameof(Expand)).MakeGenericMethod(arguments);
        var end = AccessTools.Method(typeof(PathSearchPatch), nameof(End));

        // Insert from the back so earlier indices stay valid. Branch targets move to the inserted call.
        for (var r = returns.Count - 1; r >= 0; r--)
        {
            var returnIndex = returns[r];
            var endCall = new CodeInstruction(OpCodes.Call, end);
            endCall.labels.AddRange(codes[returnIndex].labels);
            codes[returnIndex].labels.Clear();
            codes.Insert(returnIndex, endCall);
            if (returnIndex <= secondProcess)
            {
                secondProcess++;
            }
        }

        codes.InsertRange(secondProcess + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            loadCurrent,
            new CodeInstruction(OpCodes.Call, expand),
        });

        var beginLoad = new CodeInstruction(OpCodes.Ldarg_0);
        beginLoad.labels.AddRange(codes[0].labels);
        codes[0].labels.Clear();
        codes.InsertRange(0, new[] { beginLoad, new CodeInstruction(OpCodes.Call, begin) });
        return codes;
    }

    /// <summary>Job entry: resolves the published table for the job's path grid (one dictionary lookup).</summary>
    public static unsafe void Begin<TP, TH>(ref FindPathJob<TP, TH> job)
        where TP : unmanaged, IPathCostProvider
        where TH : unmanaged, IHeuristicProvider
    {
        s_nodes = 0;
        s_mainThreadBinding = null;
        var binding = RampRegistry.LookupGrid((nint)job.pathData.pathGrid.GetUnsafeReadOnlyPtrInternal());
        var table = binding?.Ramps.Published;
        var crossings = table?.ForSize(binding.VehicleSize);
        if (crossings == null || crossings.Edges.IsEmpty)
        {
            s_crossings = null;
            FloorHeuristic.Finish();
            return;
        }

        s_crossings = crossings;
        if (UnityData.IsInMainThread)
        {
            s_mainThreadBinding = binding;
        }

        FloorHeuristic.Prepare(table, crossings.Edges, job.start, job.width, job.xEnd, job.yEnd,
            CrossingCost, CardinalStepCost, ApproachDistanceCost);
        RampDiagnostics.Add(ref RampDiagnostics.JobsWithTable);
    }

    /// <summary>
    /// After the native neighbours of <paramref name="current"/>: if it is a ramp entry, offer the far exit
    /// with the same cost update as the native Process step.
    /// </summary>
    public static void Expand<TP, TH>(ref FindPathJob<TP, TH> job, int current)
        where TP : unmanaged, IPathCostProvider
        where TH : unmanaged, IHeuristicProvider
    {
        var crossings = s_crossings;
        if (crossings == null)
        {
            return;
        }

        s_nodes++;
        foreach (ref readonly var edge in crossings.At(current))
        {
            TryExpand(ref job, current, in edge);
        }
    }

    private static void TryExpand<TP, TH>(ref FindPathJob<TP, TH> job, int current, in RampEdge edge)
        where TP : unmanaged, IPathCostProvider
        where TH : unmanaged, IHeuristicProvider
    {
        ref PathData data = ref job.pathData;

        var exit = edge.ExitIndex;
        var status = data.NodeStatus(exit);
        if (status == PathData.Status.Closed)
        {
            return;
        }

        // The arrival heading can be sideways or diagonal. Check the whole straight-through lane independently
        // of the native hitbox selected for the node's arrival.
        if (!LanePassable(ref data, job.width, edge.EntryLane))
        {
            RampDiagnostics.Add(ref RampDiagnostics.EntryFitRejected);
            return;
        }

        var exitX = exit % job.width;
        var exitY = exit / job.width;
        // The native provider checks the vehicle hitbox at the exit for the exit heading.
        var cost = job.provider.GetPathCost(exitX, exitY, edge.ExitHeading, in data);
        if (cost >= RampClearance.ImpassableCost || !LanePassable(ref data, job.width, edge.ExitLane))
        {
            RampDiagnostics.Add(ref RampDiagnostics.ExitFitRejected);
            return;
        }

        var g = data.gCosts[current] + CardinalStepCost + cost * 10 + CrossingCost
            + edge.ApproachDistance * ApproachDistanceCost;
        if (status == PathData.Status.Stale || g < data.gCosts[exit])
        {
            // Same estimate as every other node of this search (floor-aware for cross-floor requests).
            var h = FloorHeuristic.Estimate(ref job.heuristic, new int4(exitX), new int4(exitY), job.xEnd, job.yEnd).x;
            data.gCosts[exit] = g;
            data.heading[exit] = edge.ExitHeading;
            // The crossing sets the exit heading outright, so no turn cooldown carries over (Process resets it
            // the same way on a heading change it does not penalise).
            data.turnCooldown[exit] = 0;
            data.parents[exit] = current;
            data.frontier.Push(exit, g + h);
            data.SetNodeStatus(exit, PathData.Status.Open);
            RampDiagnostics.Add(ref RampDiagnostics.PortalPushes);
        }
    }

    private static bool LanePassable(ref PathData data, int width, CellRect lane)
    {
        for (var z = lane.minZ; z <= lane.maxZ; z++)
        {
            var row = z * width;
            for (var x = lane.minX; x <= lane.maxX; x++)
            {
                if (data.pathGrid[row + x] >= RampClearance.ImpassableCost)
                {
                    return false;
                }
            }
        }

        // VF can reuse a completed snapshot for a synchronous request after the terrain or buildings change.
        // Synchronous jobs executed on the main thread can rule out false-positive crossings using current costs.
        var binding = s_mainThreadBinding;
        return binding == null || RampClearance.IsClear(binding.Ramps.Map, binding.VehicleDef, lane);
    }

    public static void End()
    {
        if (RampDiagnostics.Enabled && s_crossings != null)
        {
            System.Threading.Interlocked.Add(ref RampDiagnostics.NodesExpanded, s_nodes);
            System.Threading.Interlocked.Exchange(ref RampDiagnostics.LastJobNodes, s_nodes);
        }

        s_crossings = null;
        s_mainThreadBinding = null;
        FloorHeuristic.Finish();
    }

    private static bool IsLoadLocal(OpCode opcode) =>
        opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S || opcode == OpCodes.Ldloc_0
        || opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
}
