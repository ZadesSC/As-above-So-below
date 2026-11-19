using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;

[StaticConstructorOnStartup]
internal static class ABNativePortals
{
    private const string HarmonyId = "astryl.asabovesobelow.nativeportals";
    private static readonly ConditionalWeakTable<Map, List<Link>> graphs = new();
    private static readonly ConcurrentDictionary<IntPtr, SearchContext> pending = new();
    private static bool active;

    internal readonly struct Link
    {
        public readonly Building_Door Near, Far;
        public readonly IntVec3 Entry, Exit;
        public readonly int Delay;

        internal Link(Building_Door near, Building_Door far, IntVec3 entry, IntVec3 exit, int delay)
        {
            Near = near;
            Far = far;
            Entry = entry;
            Exit = exit;
            Delay = delay;
        }
    }

    internal readonly struct Edge
    {
        public readonly int Exit, Cost;

        internal Edge(int exit, int cost)
        {
            Exit = exit;
            Cost = cost;
        }
    }

    internal sealed class SearchContext
    {
        public int Slot;
        public Dictionary<int, Edge[]> Edges;
    }

    static ABNativePortals()
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            RuntimeHelpers.RunClassConstructor(typeof(ABNativePortalKernel).TypeHandle);
            harmony.Patch(Require(typeof(PathFinderJob), nameof(PathFinderJob.Execute)),
                transpiler: new HarmonyMethod(typeof(ABNativePortalKernel), nameof(ABNativePortalKernel.Rewrite)));
            harmony.Patch(Require(typeof(PathFinder), "ParameterizePathJob"),
                postfix: Hook(nameof(Prepare)));
            harmony.Patch(Require(typeof(PathFinder), "ScheduleBatchedPathJobs"),
                transpiler: Hook(nameof(RewriteDispatch)));
            harmony.Patch(Require(typeof(PathFinder), nameof(PathFinder.FindPathNow), new[]
            {
                typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathFinderCostTuning?),
                typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer)
            }), transpiler: Hook(nameof(RewriteDispatch)));
            harmony.Patch(Require(typeof(Pawn_PathFollower), "SetupMoveIntoNextCell"),
                prefix: Hook(nameof(BeforeMove)));
            active = true;
            Log.Message(ABLog.Tag + " Native portal pathfinding active for ordinary stairs and ladders.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Warning(ABLog.Tag + " Native portal pathfinding unavailable. " + exception);
        }
    }

    private static MethodInfo Require(Type type, string name, Type[] parameters = null) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);

    private static HarmonyMethod Hook(string name) => new(typeof(ABNativePortals), name);

    internal static void Invalidate(Map map)
    {
        if (map != null)
            graphs.Remove(map);
    }

    private static List<Link> GraphFor(Map map) => graphs.GetValue(map, BuildGraph);

    private static List<Link> BuildGraph(Map map)
    {
        var links = new List<Link>();
        foreach (var pair in ABWormhole.PortalPairs(map))
        {
            // Keep the existing routing for maps with connector types whose
            // movement, approach rules or animation are outside this port.
            if (!ABStairAnim.TryPortalDelay(pair.Key, out int firstDelay)
                || !ABStairAnim.TryPortalDelay(pair.Value, out int secondDelay)
                || !ABLinkApproach.TryGet(pair.Key, out ABApproach first)
                || !ABLinkApproach.TryGet(pair.Value, out ABApproach second))
                return new List<Link>();

            links.Add(new Link(pair.Key, pair.Value, first.mouth, second.mouth, firstDelay));
            links.Add(new Link(pair.Value, pair.Key, second.mouth, first.mouth, secondDelay));
        }
        return links;
    }

    internal static bool ShouldRoute(Map map, Pawn pawn, IntVec3 start, LocalTargetInfo target)
    {
        if (!active || pawn == null || map == null || !start.InBounds(map)
            || !target.IsValid || !target.Cell.InBounds(map) || ABBands.CompOf(map)?.Banded != true
            || ABBands.InGutter(map, start) || ABBands.InGutter(map, target.Cell))
            return false;

        if (GraphFor(map).Count == 0)
            return false;

        if (!ABBands.SameBand(map, start, target.Cell))
            return true;

        int from = ABBandComponents.ComponentOf(map, start, pawn);
        return from < 0 || from != ABBandComponents.ComponentOf(map, target.Cell, pawn);
    }

    internal static bool CanCross(Pawn pawn, Building_Door near, Building_Door far) =>
        pawn.Spawned && near.Spawned && far.Spawned && near.Map == pawn.Map && far.Map == pawn.Map
        && !near.IsForbiddenToPass(pawn) && !far.IsForbiddenToPass(pawn)
        && (near.Open || near.PawnCanOpen(pawn)) && (far.Open || far.PawnCanOpen(pawn));

    private static void Prepare(ref PathFinderJob job, PathRequest request)
    {
        if (!ShouldRoute(request.map, request.TraverseParms.pawn, request.Start, request.Target))
            return;

        Pawn pawn = request.TraverseParms.pawn;
        bool animated = ABMod.Settings?.transitAnim == true;
        var links = new Dictionary<int, List<Edge>>();
        foreach (Link link in GraphFor(request.map))
        {
            if (!CanCross(pawn, link.Near, link.Far))
                continue;
            int from = job.indices.CellToIndex(link.Entry);
            int to = job.indices.CellToIndex(link.Exit);
            if (!links.TryGetValue(from, out List<Edge> edges))
            {
                edges = new List<Edge>();
                links.Add(from, edges);
            }
            edges.Add(new Edge(to, animated ? link.Delay : 0));
        }

        if (links.Count == 0)
            return;

        pending[PathKey(ref job)] = new SearchContext
        {
            Slot = ABBands.CompOf(request.map).Slot,
            Edges = links.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray())
        };
    }

    private static unsafe IntPtr PathKey(ref PathFinderJob job) =>
        (IntPtr)NativeListUnsafeUtility.GetUnsafePtr(job.path);

    internal static void TakeContext(ref PathFinderJob job, out SearchContext context) =>
        pending.TryRemove(PathKey(ref job), out context);

    // Unity 1.6 can dispatch PathFinderJob through Burst and bypass managed IL.
    // Only prepared searches dispatch this non-Burst wrapper.
    private struct PortalJob : IJob
    {
        public PathFinderJob Job;
        public void Execute() => Job.Execute();
    }

    private static JobHandle Schedule(PathFinderJob job, JobHandle dependency)
    {
        IntPtr key = PathKey(ref job);
        if (!pending.ContainsKey(key))
            return job.Schedule(dependency);
        try
        {
            return new PortalJob { Job = job }.Schedule(dependency);
        }
        catch
        {
            pending.TryRemove(key, out _);
            throw;
        }
    }

    private static void Run(PathFinderJob job)
    {
        IntPtr key = PathKey(ref job);
        if (!pending.ContainsKey(key))
        {
            job.Run();
            return;
        }
        try
        {
            new PortalJob { Job = job }.Run();
        }
        finally
        {
            pending.TryRemove(key, out _);
        }
    }

    private static IEnumerable<CodeInstruction> RewriteDispatch(IEnumerable<CodeInstruction> instructions,
        MethodBase original)
    {
        string expected = original.Name == nameof(PathFinder.FindPathNow) ? nameof(Run) : nameof(Schedule);
        var code = instructions.ToList();
        var calls = code.Where(instruction => instruction.opcode == OpCodes.Call
            && instruction.operand is MethodInfo method && method.DeclaringType == typeof(IJobExtensions)
            && method.Name == expected && method.IsGenericMethod
            && method.GetGenericArguments().SequenceEqual(new[] { typeof(PathFinderJob) })).ToList();
        if (calls.Count != 1)
            throw new InvalidOperationException("Unexpected native path dispatch in " + original.Name);
        calls[0].operand = Require(typeof(ABNativePortals), expected);
        return code;
    }

    private static bool BeforeMove(Pawn_PathFollower __instance, Pawn ___pawn, PawnPath ___curPath,
        IntVec3 ___nextCell, PathEndMode ___peMode)
    {
        if (!active || ___pawn?.Map == null || ___curPath == null || ___curPath.NodesLeftCount < 2)
            return true;

        IntVec3 next = ___curPath.Peek(0);
        if (next == ___nextCell)
            next = ___curPath.Peek(1);
        IntVec3 at = ___pawn.Position;
        if ((Math.Abs(next.x - at.x) <= 1 && Math.Abs(next.z - at.z) <= 1)
            || !ABBands.Banded(___pawn.Map) || ABBands.SameBand(___pawn.Map, at, next))
            return true;

        LocalTargetInfo destination = __instance.Destination;
        foreach (Link link in GraphFor(___pawn.Map))
        {
            if (link.Entry == at && link.Exit == next
                && CanCross(___pawn, link.Near, link.Far))
            {
                ABWormholePather.CarryNative(___pawn, link.Near, link.Far, destination, ___peMode);
                return false;
            }
        }

        // The path's permission snapshot may be stale by the time the pawn reaches it.
        __instance.StopDead();
        __instance.StartPath(destination, ___peMode);
        return false;
    }
}
