using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SmashTools.PathFinder;
using Unity.Mathematics;

namespace AASBVehicleRamps;

/// <summary>
/// Floor-aware estimate for Vehicle Framework's A*. Its native heuristic measures straight-line distance on the
/// flat banded map, which hugely overestimates the cost to a goal on another floor reached through a ramp; the
/// search then returns the first crossing it stumbles on rather than the cheapest. For a cross-floor request,
/// nodes off the goal floor instead get <c>min over ramp entries (octile(node, entry) + bound)</c>, where the
/// bound is the crossing's own cost plus the octile distance from its exit to the goal (zero for exits to an
/// intermediate floor). This never exceeds the true remaining cost, so the search stays admissible. Same-floor
/// requests and nodes on the goal floor keep the native value.
/// The single <c>constrained. callvirt IHeuristicProvider.GetHeuristic</c> in <c>FindPathJob.Process</c> is
/// replaced with a call to <see cref="Estimate{TH}"/>; per-job state is prepared in <see cref="Prepare"/> and
/// lives in reusable thread-static buffers (no allocation per request once a thread's buffers are large enough).
/// </summary>
public static class FloorHeuristic
{
    [ThreadStatic] private static bool s_active;
    [ThreadStatic] private static RampTable s_table;
    [ThreadStatic] private static int s_goalFloor;
    [ThreadStatic] private static int s_count;
    [ThreadStatic] private static int[] s_entryX;
    [ThreadStatic] private static int[] s_entryZ;
    [ThreadStatic] private static int[] s_entryFloor;
    [ThreadStatic] private static int[] s_bound;

    /// <summary>
    /// Finds the single heuristic call in <c>Process</c>. Returns null on success or the mismatch description.
    /// </summary>
    public static string FindAnchor(List<CodeInstruction> codes, out int constrainedIndex)
    {
        constrainedIndex = -1;
        var found = 0;
        for (var i = 0; i + 1 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Constrained || codes[i + 1].opcode != OpCodes.Callvirt
                || !(codes[i + 1].operand is MethodInfo method) || method.Name != "GetHeuristic"
                || method.DeclaringType != typeof(IHeuristicProvider))
            {
                continue;
            }

            found++;
            constrainedIndex = i;
        }

        return found == 1 ? null : "expected 1 GetHeuristic call in FindPathJob.Process, found " + found;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase original)
    {
        var codes = new List<CodeInstruction>(instructions);
        var failure = FindAnchor(codes, out var index);
        if (failure != null)
        {
            throw new InvalidOperationException(failure);
        }

        var heuristicType = original.DeclaringType.GetGenericArguments()[1];
        var estimate = AccessTools.Method(typeof(FloorHeuristic), nameof(Estimate)).MakeGenericMethod(heuristicType);
        // Stack at the call: ref THeuristic, int4 x, int4 y, int xEnd, int yEnd - the same as Estimate's.
        var call = new CodeInstruction(OpCodes.Call, estimate);
        call.labels.AddRange(codes[index].labels);
        call.labels.AddRange(codes[index + 1].labels);
        codes.RemoveRange(index, 2);
        codes.Insert(index, call);
        return codes;
    }

    /// <summary>Called from <see cref="PathSearchPatch"/> at job start on the job's thread.</summary>
    internal static void Prepare(RampTable table, ReadOnlySpan<RampEdge> edges, int startIndex, int width,
        int xEnd, int yEnd, int crossingCost, int stepCost, int approachDistanceCost)
    {
        s_active = false;
        s_table = table;
        if (table == null || width <= 0)
        {
            return;
        }

        s_goalFloor = table.FloorOfRow(yEnd);
        var startFloor = table.FloorOfRow(startIndex / width);
        if (s_goalFloor < 0 || startFloor < 0 || startFloor == s_goalFloor)
        {
            return;
        }

        if (s_entryX == null || s_entryX.Length < edges.Length)
        {
            var size = Math.Max(edges.Length, 32);
            s_entryX = new int[size];
            s_entryZ = new int[size];
            s_entryFloor = new int[size];
            s_bound = new int[size];
        }

        for (var i = 0; i < edges.Length; i++)
        {
            var edge = edges[i];
            s_entryX[i] = edge.Entry.x;
            s_entryZ[i] = edge.Entry.z;
            s_entryFloor[i] = edge.EntryFloor;
            var toGoal = edge.ExitFloor == s_goalFloor ? Octile(edge.Exit.x - xEnd, edge.Exit.z - yEnd) : 0;
            s_bound[i] = stepCost + crossingCost + edge.ApproachDistance * approachDistanceCost + toGoal;
        }

        s_count = edges.Length;
        s_active = true;
    }

    public static void Finish()
    {
        s_active = false;
        s_table = null;
    }

    public static int4 Estimate<TH>(ref TH heuristic, int4 x, int4 y, int xEnd, int yEnd)
        where TH : unmanaged, IHeuristicProvider
    {
        var native = heuristic.GetHeuristic(x, y, xEnd, yEnd);
        if (!s_active)
        {
            return native;
        }

        var table = s_table;
        for (var lane = 0; lane < 4; lane++)
        {
            var floor = table.FloorOfRow(y[lane]);
            if (floor < 0 || floor == s_goalFloor)
            {
                continue;
            }

            var best = int.MaxValue;
            for (var i = 0; i < s_count; i++)
            {
                if (s_entryFloor[i] != floor)
                {
                    continue;
                }

                var estimate = Octile(x[lane] - s_entryX[i], y[lane] - s_entryZ[i]) + s_bound[i];
                if (estimate < best)
                {
                    best = estimate;
                }
            }

            if (best != int.MaxValue)
            {
                native[lane] = best;
            }
        }

        return native;
    }

    // Same units as SmashTools NormalHeuristic: 10 per cardinal and 14 per diagonal step.
    private static int Octile(int dx, int dz)
    {
        dx = Math.Abs(dx);
        dz = Math.Abs(dz);
        return 10 * Math.Max(dx, dz) + 4 * Math.Min(dx, dz);
    }
}
