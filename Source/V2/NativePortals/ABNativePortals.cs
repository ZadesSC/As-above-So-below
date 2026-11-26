using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Unity.Collections.LowLevel.Unsafe;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;

[StaticConstructorOnStartup]
internal static class ABNativePortals
{
    private const string HarmonyId = "astryl.asabovesobelow.nativeportals";
    private static readonly ConditionalWeakTable<Map, Geometry> graphs = new();
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

    private sealed class Geometry
    {
        public readonly List<Link> Links = new();
        public bool HasSpecialConnector;
    }

    internal sealed class SearchContext
    {
        public int Slot;
        public bool CanProject;
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

    private static Geometry GraphFor(Map map) => graphs.GetValue(map, BuildGraph);

    private static Geometry BuildGraph(Map map)
    {
        var geometry = new Geometry();
        foreach (var pair in ABWormhole.PortalPairs(map))
        {
            if (!ABStairAnim.TryPortalDelay(pair.Key, out int firstDelay)
                || !ABStairAnim.TryPortalDelay(pair.Value, out int secondDelay)
                || !ABLinkApproach.TryGet(pair.Key, out ABApproach first)
                || !ABLinkApproach.TryGet(pair.Value, out ABApproach second)
                || !first.mouth.InBounds(map) || !second.mouth.InBounds(map)
                || ABBands.InGutter(map, first.mouth) || ABBands.InGutter(map, second.mouth)
                || ABBands.SameBand(map, first.mouth, second.mouth))
            {
                geometry.HasSpecialConnector = true;
                continue;
            }

            geometry.Links.Add(new Link(pair.Key, pair.Value, first.mouth, second.mouth, firstDelay));
            geometry.Links.Add(new Link(pair.Value, pair.Key, second.mouth, first.mouth, secondDelay));
        }
        return geometry;
    }

    // Keep the segmented planner on maps with connectors whose choreography
    // still requires it. Ordinary maps use the whole native grid even with no links.
    internal static bool UsesNativeRouting(Map map) =>
        active && map != null && ABBands.Banded(map) && !GraphFor(map).HasSpecialConnector;

    internal static bool CanCross(Pawn pawn, Building_Door near, Building_Door far) =>
        pawn.Spawned && near.Spawned && far.Spawned && near.Map == pawn.Map && far.Map == pawn.Map
        && !near.IsForbiddenToPass(pawn) && !far.IsForbiddenToPass(pawn)
        && near.CanPhysicallyPass(pawn) && far.CanPhysicallyPass(pawn);

    internal static void Replan(Pawn pawn, LocalTargetInfo destination, PathEndMode endMode)
    {
        pawn.pather.StopDead();
        if (destination.IsValid && (!destination.HasThing
            || (!destination.ThingDestroyed && destination.Thing.Spawned && destination.Thing.Map == pawn.Map)))
            pawn.pather.StartPath(destination, endMode);
        else if (pawn.CurJob != null)
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, false);
    }

    private static void Prepare(ref PathFinderJob job, PathRequest request)
    {
        if (!UsesNativeRouting(request.map))
            return;

        Pawn pawn = request.TraverseParms.pawn;
        bool animated = ABMod.Settings?.transitAnim == true;
        var links = new Dictionary<int, List<Edge>>();
        int slot = ABBands.CompOf(request.map).Slot;
        bool canProject = slot > 0 && ABNativePortalKernel.ValidMoveCosts(job.moveTicksCardinal, job.moveTicksDiagonal);
        foreach (Link link in GraphFor(request.map).Links)
        {
            if (pawn != null && !CanCross(pawn, link.Near, link.Far))
                continue;
            int from = job.indices.CellToIndex(link.Entry);
            int to = job.indices.CellToIndex(link.Exit);
            if (!links.TryGetValue(from, out List<Edge> edges))
            {
                edges = new List<Edge>();
                links.Add(from, edges);
            }
            int delay = animated ? link.Delay : 0;
            edges.Add(new Edge(to, delay));
            if (canProject && ABNativePortalKernel.Octile(link.Entry.x - link.Exit.x,
                link.Entry.z % slot - link.Exit.z % slot, job.moveTicksCardinal, job.moveTicksDiagonal) > delay)
                canProject = false;
        }

        if (links.Count == 0)
            return;

        pending[PathKey(ref job)] = new SearchContext
        {
            Slot = slot,
            CanProject = canProject,
            Edges = links.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray())
        };
    }

    private static unsafe IntPtr PathKey(ref PathFinderJob job) =>
        (IntPtr)NativeListUnsafeUtility.GetUnsafePtr(job.path);

    internal static void TakeContext(ref PathFinderJob job, out SearchContext context) =>
        pending.TryRemove(PathKey(ref job), out context);

    private static bool BeforeMove(Pawn_PathFollower __instance, Pawn ___pawn, PawnPath ___curPath,
        IntVec3 ___nextCell, PathEndMode ___peMode)
    {
        if (___curPath == null || ___curPath.NodesLeftCount < 2 || !UsesNativeRouting(___pawn?.Map))
            return true;

        IntVec3 next = ___curPath.Peek(0);
        if (next == ___nextCell)
            next = ___curPath.Peek(1);
        IntVec3 at = ___pawn.Position;
        if (Math.Abs(next.x - at.x) <= 1 && Math.Abs(next.z - at.z) <= 1
            || ABBands.SameBand(___pawn.Map, at, next))
            return true;

        // StartPath can leave the previous path moving while its replacement is queued.
        // Stop consuming the old path here, and let the normal pather claim the new one.
        if (__instance.curPathRequest != null)
        {
            __instance.DisposeAndClearCurPath();
            return false;
        }

        LocalTargetInfo destination = __instance.Destination;
        foreach (Link link in GraphFor(___pawn.Map).Links)
        {
            if (link.Entry == at && link.Exit == next
                && CanCross(___pawn, link.Near, link.Far))
            {
                ABWormholePather.CarryNative(___pawn, link.Near, link.Far, destination, ___peMode);
                return false;
            }
        }

        // The path's permission snapshot may be stale by the time the pawn reaches it.
        Replan(___pawn, destination, ___peMode);
        return false;
    }
}
