using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AsAboveSoBelow;
using CoreLib.Collections;
using CoreLib.PathFinding;
using HarmonyLib;
using RimWorld;
using SmashTools;
using SmashTools.PathFinder;
using Unity.Collections;
using Unity.Mathematics;
using Verse;
using Verse.AI;
using Verse.Profile;
using Vehicles;
using Vehicles.Config;
using Vehicles.Rendering;
using PathData = SmashTools.PathFinder.PathData;
using SmashPathFinder = SmashTools.PathFinder.PathFinder;

namespace AASBVehicleRamps;

/// <summary>
/// Resolves and type-checks every external member before any patch is applied, then installs the patches.
/// Validation failures install nothing; an exception while patching removes every patch this mod made.
/// </summary>
public static class StartupBinding
{
    private sealed class Targets
    {
        public readonly List<MethodInfo> JobExecutes = new List<MethodInfo>();
        public readonly List<MethodInfo> JobProcesses = new List<MethodInfo>();
        public MethodInfo FindPath;
        public MethodInfo RequestPath;
        public MethodInfo CanReachVehicle;
        public MethodInfo StartPath;
        public MethodInfo BestExitSpot;
        public MethodInfo RandomExitSpotValidator;
        public MethodInfo PreviewReach;
        public MethodInfo RegionSearch;
        public MethodInfo FloodSearch;
        public MethodInfo FloodArrival;
        public MethodInfo ReachCacheRead;
        public MethodInfo SetupMoveIntoNextCell;
        public MethodInfo NeedNewPath;
        public MethodInfo RampSpawnSetup;
        public MethodInfo RampDeSpawn;
        public MethodInfo ForbiddenSetter;
        public MethodInfo MapDeinit;
        public MethodInfo ClearAllMaps;
        public MethodInfo VehicleDraw;
        public MethodInfo PatherTick;
        public MethodInfo TryEnterNextPathCell;
        public MethodInfo VehicleDeSpawn;
        public readonly List<MethodInfo> MeshDrawers = new List<MethodInfo>();
    }

    private static Targets s_targets;

    /// <summary>Returns null when every member matches, otherwise the first mismatch.</summary>
    public static string Validate()
    {
        try
        {
            var reason = RampDefs.Resolve();
            if (reason != null)
            {
                return reason;
            }

            if (!FeatureFlags.IsFeatureEnabled(FeatureFlags.PathFinderV2))
            {
                return "Vehicle Framework's PathFinderV2 feature is disabled; only that path search is supported";
            }

            s_targets = ResolveTargets();
            return null;
        }
        catch (Exception e) when (e is InvalidOperationException || e is TypeLoadException
            || e is MissingMemberException || e is BadImageFormatException)
        {
            return e.Message;
        }
    }

    public static void Install(Harmony harmony)
    {
        var t = s_targets;
        var transpiler = new HarmonyMethod(typeof(PathSearchPatch), nameof(PathSearchPatch.Transpiler));
        foreach (var execute in t.JobExecutes)
        {
            harmony.Patch(execute, transpiler: transpiler);
        }

        var heuristic = new HarmonyMethod(typeof(FloorHeuristic), nameof(FloorHeuristic.Transpiler));
        foreach (var process in t.JobProcesses)
        {
            harmony.Patch(process, transpiler: heuristic);
        }

        var pathRequested = new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.PathRequested));
        harmony.Patch(t.FindPath, prefix: pathRequested);
        harmony.Patch(t.RequestPath, prefix: pathRequested);
        harmony.Patch(t.RegionSearch,
            transpiler: new HarmonyMethod(typeof(RegionPortalPatch), nameof(RegionPortalPatch.Transpiler)));
        harmony.Patch(t.FloodSearch,
            transpiler: new HarmonyMethod(typeof(ReachCaches), nameof(ReachCaches.FloodWritesTranspiler)));
        harmony.Patch(t.ReachCacheRead,
            transpiler: new HarmonyMethod(typeof(ReachCaches), nameof(ReachCaches.ReadTranspiler)));
        harmony.Patch(t.FloodArrival,
            transpiler: new HarmonyMethod(typeof(FloodReachPatch), nameof(FloodReachPatch.Transpiler)));
        harmony.Patch(t.StartPath,
            transpiler: new HarmonyMethod(typeof(PathStartPatch), nameof(PathStartPatch.Transpiler)));
        harmony.Patch(t.BestExitSpot,
            transpiler: new HarmonyMethod(typeof(ExitSpotReachPatch), nameof(ExitSpotReachPatch.BestTranspiler)));
        harmony.Patch(t.RandomExitSpotValidator, transpiler: new HarmonyMethod(typeof(ExitSpotReachPatch),
            nameof(ExitSpotReachPatch.RandomValidatorTranspiler)));
        harmony.Patch(t.PreviewReach,
            transpiler: new HarmonyMethod(typeof(PathPreviewReachPatch), nameof(PathPreviewReachPatch.Transpiler)));
        harmony.Patch(t.CanReachVehicle,
            prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.ReachabilityQueried)),
            postfix: new HarmonyMethod(typeof(ReachabilityPatch), nameof(ReachabilityPatch.Postfix)));
        harmony.Patch(t.SetupMoveIntoNextCell,
            prefix: new HarmonyMethod(typeof(CrossingPatch), nameof(CrossingPatch.Prefix)));
        harmony.Patch(t.NeedNewPath,
            transpiler: new HarmonyMethod(typeof(LookAheadPatch), nameof(LookAheadPatch.Transpiler)));
        harmony.Patch(t.RampSpawnSetup,
            postfix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.RampSpawned)));
        harmony.Patch(t.RampDeSpawn,
            prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.RampDespawning)),
            postfix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.RampDespawned)));
        harmony.Patch(t.ForbiddenSetter,
            postfix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.ForbiddenChanged)));
        harmony.Patch(t.MapDeinit,
            prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.MapDeinitialising)));
        harmony.Patch(t.ClearAllMaps,
            prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.AllMapsClearing)));
        harmony.Patch(t.TryEnterNextPathCell,
            prefix: new HarmonyMethod(typeof(CrossingPatch), nameof(CrossingPatch.HoldPrefix)));
        harmony.Patch(t.PatherTick,
            postfix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.PatherTicked)));
        harmony.Patch(t.VehicleDeSpawn,
            prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(LifecyclePatches.VehicleDespawning)));
        harmony.Patch(t.VehicleDraw,
            prefix: new HarmonyMethod(typeof(ClipRenderPatch), nameof(ClipRenderPatch.DrawPrefix)),
            finalizer: new HarmonyMethod(typeof(ClipRenderPatch), nameof(ClipRenderPatch.DrawFinalizer)),
            transpiler: new HarmonyMethod(typeof(ClipRenderPatch), nameof(ClipRenderPatch.RotationTranspiler)));
        var meshes = new HarmonyMethod(typeof(ClipRenderPatch), nameof(ClipRenderPatch.DrawMeshTranspiler));
        foreach (var drawer in t.MeshDrawers)
        {
            harmony.Patch(drawer, transpiler: meshes);
        }
    }

    private static unsafe Targets ResolveTargets()
    {
        var t = new Targets();
        ResolvePathJob(t);

        var node = typeof(Path.Node);
        t.FindPath = MemberBinding.RequireMethod(typeof(PathFinderManager), "FindPath",
            new[] { node, node, typeof(PathSettings) }, typeof(Path));
        t.RequestPath = MemberBinding.RequireMethod(typeof(PathFinderManager), "RequestPath",
            new[] { node, node, typeof(PathSettings) }, typeof(IPathPromise));
        MemberBinding.RequireField(typeof(PathFinderManager), "map", typeof(Map));
        var impl = MemberBinding.RequireType(typeof(PathFinderManager).GetNestedType("PathFinderImpl",
            BindingFlags.NonPublic | BindingFlags.Public), "PathFinderManager.PathFinderImpl");
        MemberBinding.RequireField(typeof(PathFinderManager), "pathFinders", impl.MakeArrayType());
        MemberBinding.RequireField(impl, "pathFinder", typeof(SmashPathFinder));
        MemberBinding.RequireField(typeof(SmashPathFinder), "snapshot", typeof(NativeArray<int>));
        MemberBinding.RequireMethod(typeof(NativeArray<int>), "GetUnsafeReadOnlyPtrInternal", Type.EmptyTypes,
            typeof(void*));
        MemberBinding.RequireMethod(typeof(NativeArray<int>.ReadOnly), "GetUnsafeReadOnlyPtrInternal",
            Type.EmptyTypes, typeof(void*));
        MemberBinding.RequireField(typeof(PathSettings), "vehicleDef", typeof(VehicleDef));

        t.CanReachVehicle = MemberBinding.RequireMethod(typeof(VehicleReachability), "CanReachVehicle",
            new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) },
            typeof(bool));
        MemberBinding.RequireField(typeof(VehicleReachability), "map", typeof(Map));
        MemberBinding.RequireProperty(typeof(VehicleReachability), "CreatedFor", typeof(VehicleDef), false);
        ResolveRegionSearch(t);

        var follower = typeof(VehiclePathFollower);
        t.SetupMoveIntoNextCell = MemberBinding.RequireMethod(follower, "SetupMoveIntoNextCell", Type.EmptyTypes,
            typeof(void));
        var pathRequest = MemberBinding.RequireType(follower.GetNestedType("PathRequest",
            BindingFlags.NonPublic | BindingFlags.Public), "VehiclePathFollower.PathRequest");
        t.NeedNewPath = MemberBinding.RequireMethod(follower, "NeedNewPath", Type.EmptyTypes, pathRequest);
        MemberBinding.RequireMethod(typeof(Ext_Map), "DirectionToCell", new[] { typeof(IntVec3), typeof(IntVec3) },
            typeof(Rot8));
        var needNewPath = PatchProcessor.GetOriginalInstructions(t.NeedNewPath).ToList();
        var lookAhead = LookAheadPatch.FindAnchor(needNewPath, out _);
        if (lookAhead != null)
        {
            throw new InvalidOperationException(lookAhead);
        }

        MemberBinding.RequireField(follower, "vehicle", typeof(VehiclePawn));
        MemberBinding.RequireField(follower, "curPath", typeof(VehiclePath));
        MemberBinding.RequireField(follower, "destination", typeof(LocalTargetInfo));
        MemberBinding.RequireMethod(follower, "StopDead", Type.EmptyTypes, typeof(void));
        MemberBinding.RequireMethod(follower, "PatherFailed", Type.EmptyTypes, typeof(void));
        MemberBinding.RequireMethod(follower, "ResetToCurrentPosition", Type.EmptyTypes, typeof(void));
        t.StartPath = MemberBinding.RequireMethod(follower, "StartPath",
            new[] { typeof(LocalTargetInfo), typeof(PathEndMode), typeof(bool) }, typeof(void));
        PathStartPatch.FindAnchor(PatchProcessor.GetOriginalInstructions(t.StartPath).ToList(), t.CanReachVehicle);
        (t.BestExitSpot, t.RandomExitSpotValidator) = ExitSpotReachPatch.ResolveTargets();
        t.PreviewReach = PathPreviewReachPatch.ResolveTarget(t.CanReachVehicle);
        MemberBinding.RequireField(follower, "nextCell", typeof(IntVec3));
        MemberBinding.RequireField(typeof(VehiclePawn), "beached", typeof(bool));
        MemberBinding.RequireProperty(follower, "Waiting", typeof(bool), false);
        MemberBinding.RequireProperty(follower, "Moving", typeof(bool), false);
        // RampTraffic builds Vehicle Framework's own footprint for a cell and rotation.
        MemberBinding.RequireConstructor(typeof(CoreLib.EntityRect), typeof(int), typeof(int), typeof(int),
            typeof(int), typeof(CoreLib.Orientation));
        MemberBinding.RequireConstructor(typeof(CoreLib.Orientation), typeof(int));
        MemberBinding.RequireMethod(typeof(CoreLib.EntityRect), "GetEnumerator", Type.EmptyTypes,
            typeof(CoreLib.EntityRect.Enumerator));
        MemberBinding.RequireProperty(follower, "RequestStatus", typeof(VehiclePathFollower.PathRequestStatus), false);
        MemberBinding.RequireMethod(typeof(VehiclePath), "Peek", new[] { typeof(int) }, typeof(IntVec3));
        MemberBinding.RequireProperty(typeof(VehiclePath), "LastNode", typeof(IntVec3), false);
        MemberBinding.RequireMethod(typeof(VehiclePath), "ConsumeNextNode", Type.EmptyTypes, typeof(IntVec3));
        MemberBinding.RequireProperty(typeof(Path), "NodesLeft", typeof(int), false);

        MemberBinding.RequireProperty(typeof(VehiclePawn), "FullRotation", typeof(Rot8), true);
        MemberBinding.RequireProperty(typeof(VehiclePawn), "DrawTracker", typeof(VehicleDrawTracker), false);
        MemberBinding.RequireProperty(typeof(VehiclePawn), "AllPawnsAboard", typeof(List<Pawn>), false);
        MemberBinding.RequireProperty(typeof(VehiclePawn), "VehicleDef", typeof(VehicleDef), false);
        MemberBinding.RequireField(typeof(VehicleDrawTracker), "tweener", typeof(VehicleTweener));
        MemberBinding.RequireMethod(typeof(VehicleTweener), "ResetTweenedPosToRoot", Type.EmptyTypes, typeof(void));
        MemberBinding.RequireMethod(typeof(Ext_Vehicles), "VehicleRect",
            new[] { typeof(VehicleDef), typeof(IntVec3), typeof(Rot4), typeof(bool) }, typeof(CellRect));
        MemberBinding.RequireMethod(typeof(PathingHelper), "AnyVehicleBlockingPathAt",
            new[] { typeof(IntVec3), typeof(VehiclePawn) }, typeof(VehiclePawn));
        MemberBinding.RequireMethod(typeof(IPathGridCalculator), "PathCostAt",
            new[] { typeof(Map), typeof(IntVec3), typeof(VehicleDef) }, typeof(ushort));
        var costCoords = MemberBinding.RequireType(typeof(SumTableCostProvider).GetNestedType("Coords",
            BindingFlags.NonPublic | BindingFlags.Public), "SumTableCostProvider.Coords");
        MemberBinding.RequireMethod(typeof(SumTableCostProvider), "GetCoords",
            new[] { typeof(int2), typeof(int) }, costCoords);
        var intReference = typeof(int).MakeByRefType();
        MemberBinding.RequireMethod(costCoords, "GetRect",
            new[] { typeof(int), typeof(int), intReference, intReference, intReference, intReference }, typeof(void));
        MemberBinding.RequireProperty(typeof(VehicleDef), "DefIndex", typeof(int), false);

        var stairs = typeof(Building_ABStairs2);
        t.RampSpawnSetup = MemberBinding.RequireMethod(stairs, "SpawnSetup", new[] { typeof(Map), typeof(bool) },
            typeof(void));
        t.RampDeSpawn = MemberBinding.RequireMethod(stairs, "DeSpawn", new[] { typeof(DestroyMode) }, typeof(void));
        if (t.RampSpawnSetup.DeclaringType != stairs || t.RampDeSpawn.DeclaringType != stairs)
        {
            throw new InvalidOperationException("Building_ABStairs2 no longer overrides SpawnSetup/DeSpawn");
        }

        MemberBinding.RequireProperty(stairs, "Counterparts", typeof(IReadOnlyList<Building_ABStairs2>), false);
        MemberBinding.RequireProperty(stairs, "LevelDelta", typeof(int), false);
        MemberBinding.RequireMethod(typeof(ABLinkApproach), "TryGet",
            new[] { typeof(Thing), typeof(ABApproach).MakeByRefType() }, typeof(bool));
        MemberBinding.RequireField(typeof(ABApproach), "outside", typeof(IntVec3));
        MemberBinding.RequireField(typeof(ABApproach), "outward", typeof(IntVec3));
        MemberBinding.RequireMethod(typeof(ABBands), "CompOf", new[] { typeof(Map) }, typeof(ABBandMap));
        MemberBinding.RequireMethod(typeof(ABBandMap), "BandOf", new[] { typeof(IntVec3) }, typeof(int));
        MemberBinding.RequireProperty(typeof(ABBandMap), "Banded", typeof(bool), false);
        MemberBinding.RequireField(typeof(ABBandMap), "bandCount", typeof(int));
        MemberBinding.RequireMethod(typeof(ABBandView), "FollowTransit", new[] { typeof(Pawn) }, typeof(void));
        MemberBinding.RequireMethod(typeof(ABFogReveal), "RevealArrival", new[] { typeof(Pawn), typeof(IntVec3) },
            typeof(int));

        ResolveAnimation(t);

        t.ForbiddenSetter = MemberBinding.RequireProperty(typeof(CompForbiddable), "Forbidden", typeof(bool), true)
            .SetMethod;
        t.MapDeinit = MemberBinding.RequireMethod(typeof(MapDeiniter), "Deinit", new[] { typeof(Map), typeof(bool) },
            typeof(void));
        t.ClearAllMaps = MemberBinding.RequireMethod(typeof(MemoryUtility), "ClearAllMapsAndWorld", Type.EmptyTypes,
            typeof(void));
        return t;
    }

    /// <summary>
    /// Members used by the ramp links in Vehicle Framework's region search (<see cref="RegionPortalPatch"/>) and by
    /// the reachability cache clearing (<see cref="ReachCaches"/>), with the region search's instruction anchor.
    /// </summary>
    private static void ResolveRegionSearch(Targets t)
    {
        var search = typeof(ChunkSearch);
        var data = typeof(ChunkSearch.Data);
        t.RegionSearch = MemberBinding.RequireMethod(search, "CanReach", new[] { data.MakeByRefType() }, typeof(bool));
        var codes = PatchProcessor.GetOriginalInstructions(t.RegionSearch).ToList();
        var anchor = RegionPortalPatch.FindAnchor(codes, out _);
        if (anchor != null)
        {
            throw new InvalidOperationException(anchor);
        }

        ReachCaches.RequireWrites(codes, 1);
        t.FloodSearch = MemberBinding.RequireMethod(search, "CanReachByCell", new[] { data }, typeof(bool));
        var floodCodes = PatchProcessor.GetOriginalInstructions(t.FloodSearch).ToList();
        ReachCaches.RequireWrites(floodCodes, 2);
        t.FloodArrival = FloodReachPatch.ResolveTarget(floodCodes);
        var cacheLookup = MemberBinding.RequireMethod(search, "GetCachedResult",
            new[] { data.MakeByRefType(), typeof(bool).MakeByRefType() }, typeof(bool));
        t.ReachCacheRead = ReachCaches.ResolveReadTarget(cacheLookup);

        MemberBinding.RequireMethod(search, "RegionReachable",
            new[] { typeof(VehicleRegion), typeof(TraverseParms).MakeByRefType() }, typeof(bool));
        MemberBinding.RequireField(search, "map", typeof(Map));
        MemberBinding.RequireField(search, "createdFor", typeof(VehicleDef));
        MemberBinding.RequireField(search, "regionGridManager", typeof(VehicleRegionGridManager));
        MemberBinding.RequireField(search, "reachedIndex", typeof(uint));
        MemberBinding.RequireField(data, "traverseParms", typeof(TraverseParms));
        MemberBinding.RequireMethod(typeof(VehicleRegionGridManager), "GetGridType", new[] { typeof(TraverseParms) },
            typeof(RegionGridType));
        MemberBinding.RequireMethod(typeof(VehicleRegionGridManager), "get_Item", new[] { typeof(RegionGridType) },
            typeof(VehicleRegionGrid));
        MemberBinding.RequireProperty(typeof(VehicleRegionGrid), "DirectGrid", typeof(VehicleRegion[]), false);
        MemberBinding.RequireField(typeof(VehicleRegion), "valid", typeof(bool));
        MemberBinding.RequireProperty(typeof(VehiclePawn), "VehicleDef", typeof(VehicleDef), false);

        MemberBinding.RequireProperty(typeof(VehiclePathingSystem), "PathData", typeof(PathDataContainer), false);
        MemberBinding.RequireProperty(typeof(PathDataContainer), "AllPathData",
            typeof(ReadOnlyArray<Vehicles.PathData>), false);
        MemberBinding.RequireProperty(typeof(Vehicles.PathData), "RegionData", typeof(RegionData), false);
        MemberBinding.RequireField(typeof(RegionData), "reachability", typeof(VehicleReachability));
        MemberBinding.RequireMethod(typeof(VehicleReachability), "ClearCache", Type.EmptyTypes, typeof(void));
    }

    /// <summary>Members used by the crossing animation, with the instruction counts the transpilers rely on.</summary>
    private static void ResolveAnimation(Targets t)
    {
        MemberBinding.RequireProperty(typeof(ABMod), "Settings", typeof(ABSettings), false);
        MemberBinding.RequireField(typeof(ABSettings), "transitAnim", typeof(bool));
        MemberBinding.RequireProperty(typeof(Pawn), "TicksPerMoveCardinal", typeof(float), false);
        t.PatherTick = MemberBinding.RequireMethod(typeof(VehiclePathFollower), "PatherTick", Type.EmptyTypes,
            typeof(void));
        MemberBinding.RequireProperty(typeof(VehiclePathFollower), "Moving", typeof(bool), false);
        ResolveHold(t);
        MemberBinding.RequireField(typeof(Pawn), "stances", typeof(Pawn_StanceTracker));
        MemberBinding.RequireField(typeof(Pawn_StanceTracker), "stunner", typeof(StunHandler));
        MemberBinding.RequireProperty(typeof(StunHandler), "Stunned", typeof(bool), false);
        MemberBinding.RequireField(typeof(GraphicData), "drawRotated", typeof(bool));
        MemberBinding.RequireMethod(typeof(Ext_Vehicles), "TrueCenter",
            new[] { typeof(IntVec3), typeof(Rot8), typeof(VehicleDef) }, typeof(UnityEngine.Vector3));
        t.VehicleDeSpawn = MemberBinding.RequireMethod(typeof(VehiclePawn), "DeSpawn", new[] { typeof(DestroyMode) },
            typeof(void));
        t.VehicleDraw = MemberBinding.RequireMethod(typeof(VehiclePawn), "DynamicDrawPhaseAt",
            new[] { typeof(DrawPhase), typeof(UnityEngine.Vector3), typeof(bool) }, typeof(void));
        if (t.VehicleDeSpawn.DeclaringType != typeof(VehiclePawn) || t.VehicleDraw.DeclaringType != typeof(VehiclePawn))
        {
            throw new InvalidOperationException("VehiclePawn no longer overrides DeSpawn/DynamicDrawPhaseAt");
        }

        if (ClipRenderPatch.NativeDrawMesh == null
            || MemberBinding.RequireMethod(typeof(UnityEngine.Graphics), "DrawMesh",
                new[]
                {
                    typeof(UnityEngine.Mesh), typeof(UnityEngine.Matrix4x4), typeof(UnityEngine.Material), typeof(int),
                },
                typeof(void)) == null)
        {
            throw new InvalidOperationException("missing Graphics.DrawMesh overloads");
        }

        var draw = PatchProcessor.GetOriginalInstructions(t.VehicleDraw).ToList();
        var rotations = ClipRenderPatch.CountFullRotation(draw);
        if (rotations != 1)
        {
            throw new InvalidOperationException("expected 1 FullRotation read in VehiclePawn.DynamicDrawPhaseAt, found "
                + rotations);
        }

        AddMeshDrawer(t, typeof(Vehicles.Rendering.VehicleRenderer), "Draw", Type.EmptyTypes, 1);
        AddMeshDrawer(t, typeof(VehicleTurret), "Draw", Type.EmptyTypes, 2);
        AddMeshDrawer(t, typeof(GraphicOverlay), "Draw",
            new[] { typeof(SmashTools.Rendering.TransformData).MakeByRefType() }, 1);
    }

    /// <summary>
    /// The hold guard skips <c>TryEnterNextPathCell</c> while a clip holds the vehicle. Require that method to be the
    /// one that issues the per-cell collision check, path re-check and next-cell setup, so skipping it covers them.
    /// </summary>
    private static void ResolveHold(Targets t)
    {
        var follower = typeof(VehiclePathFollower);
        t.TryEnterNextPathCell = MemberBinding.RequireMethod(follower, "TryEnterNextPathCell", Type.EmptyTypes,
            typeof(void));
        MemberBinding.RequireField(follower, "nextCellCostTotal", typeof(float));
        var collisions = MemberBinding.RequireMethod(typeof(VehiclePawn), "CheckForCollisions", new[] { typeof(float) },
            typeof(void));
        var codes = PatchProcessor.GetOriginalInstructions(t.TryEnterNextPathCell).ToList();
        foreach (var (callee, name) in new[] { (collisions, "CheckForCollisions"), (t.NeedNewPath, "NeedNewPath"),
            (t.SetupMoveIntoNextCell, "SetupMoveIntoNextCell") })
        {
            var calls = codes.Count(code => code.Calls(callee));
            if (calls != 1)
            {
                throw new InvalidOperationException("expected 1 " + name + " call in TryEnterNextPathCell, found "
                    + calls);
            }
        }
    }

    private static void AddMeshDrawer(Targets t, Type owner, string name, Type[] parameters, int expectedCalls)
    {
        var method = MemberBinding.RequireMethod(owner, name, parameters, typeof(void));
        var calls = ClipRenderPatch.CountDrawMesh(PatchProcessor.GetOriginalInstructions(method).ToList());
        if (calls != expectedCalls)
        {
            throw new InvalidOperationException("expected " + expectedCalls + " Graphics.DrawMesh calls in "
                + owner.Name + "." + name + ", found " + calls);
        }

        t.MeshDrawers.Add(method);
    }

    private static void ResolvePathJob(Targets t)
    {
        var data = typeof(PathData);
        MemberBinding.RequireField(data, "pathGrid", typeof(NativeArray<int>.ReadOnly));
        MemberBinding.RequireField(data, "gCosts", typeof(NativeArray<int>));
        MemberBinding.RequireField(data, "parents", typeof(NativeArray<int>));
        MemberBinding.RequireField(data, "heading", typeof(NativeArray<int>));
        MemberBinding.RequireField(data, "turnCooldown", typeof(NativeArray<int>));
        MemberBinding.RequireField(data, "frontier", typeof(NativeMinHeap));
        MemberBinding.RequireMethod(data, "NodeStatus", new[] { typeof(int) }, typeof(PathData.Status));
        MemberBinding.RequireMethod(data, "SetNodeStatus", new[] { typeof(int), typeof(PathData.Status) },
            typeof(void));
        MemberBinding.RequireMethod(typeof(NativeMinHeap), "Push", new[] { typeof(int), typeof(int) }, typeof(void));
        MemberBinding.RequireMethod(typeof(IPathCostProvider), "GetPathCost",
            new[] { typeof(int), typeof(int), typeof(int), data.MakeByRefType() }, typeof(int));
        MemberBinding.RequireMethod(typeof(IHeuristicProvider), "GetHeuristic",
            new[] { typeof(int4), typeof(int4), typeof(int), typeof(int) }, typeof(int4));

        foreach (var job in PathSearchPatch.ClosedJobTypes())
        {
            var args = job.GetGenericArguments();
            MemberBinding.RequireField(job, "pathData", data);
            MemberBinding.RequireField(job, "provider", args[0]);
            MemberBinding.RequireField(job, "heuristic", args[1]);
            foreach (var name in new[] { "width", "start", "xEnd", "yEnd" })
            {
                MemberBinding.RequireField(job, name, typeof(int));
            }

            var execute = MemberBinding.RequireMethod(job, "Execute", Type.EmptyTypes, typeof(void));
            var codes = PatchProcessor.GetOriginalInstructions(execute).ToList();
            var anchor = PathSearchPatch.FindAnchors(codes, out _, out _, out _);
            if (anchor != null)
            {
                throw new InvalidOperationException(job.Name + ": " + anchor);
            }

            t.JobExecutes.Add(execute);
            var process = MemberBinding.RequireMethod(job, "Process",
                new[] { typeof(int), typeof(int), typeof(int), typeof(bool) }, typeof(void));
            var heuristic = FloorHeuristic.FindAnchor(PatchProcessor.GetOriginalInstructions(process).ToList(), out _);
            if (heuristic != null)
            {
                throw new InvalidOperationException(job.Name + ": " + heuristic);
            }

            t.JobProcesses.Add(process);
        }
    }
}
