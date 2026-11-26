using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class WholeMapPathJobFixture : FixtureProvider
{
    private readonly WorkshopParityFixture layout = new WorkshopParityFixture();

    public override FixtureContract Contract
    {
        get
        {
            var contract = layout.Contract;
            contract.Id = "aasb-whole-map-path-job";
            contract.Intent = "Whole-map vanilla worker inputs on native and mixed routing maps";
            contract.Interventions = contract.Interventions.Concat(new[] {
                "second-paired-ladder", "two-floor-partitions-with-controlled-crossings",
                "spawned-doors-pawns-and-traps", "upper-door-forbidden-then-removed",
                "add-paired-elevator", "read-only-vanilla-job-postfix", "queued-path-requests",
                "transit-animation-disabled", "camera-focus-and-path-overlay"
            }).ToArray();
            return contract;
        }
    }

    public override void Setup(Context c) => layout.Setup(c);
    public override void Validate(Context c, string stage) => layout.Validate(c, stage);
}

public sealed class WholeMapPathJobScenario : Rwtf.Scenario
{
    private static readonly IntVec3 Start = new IntVec3(58, 0, 31);
    private static readonly IntVec3 Goal = new IntVec3(75, 0, 31);
    private static readonly IntVec3 MixedGoal = new IntVec3(59, 0, 33);
    private static readonly IntVec3 Gate = new IntVec3(68, 0, 95);
    private static readonly IntVec3 GroundGap = new IntVec3(68, 0, 24);
    private static readonly MethodInfo Prepare = AccessTools.Method(typeof(PathFinder), "ParameterizeDoorBlockedJob");
    private static readonly MethodInfo UsesNativeRouting = AccessTools.Method(
        AccessTools.TypeByName("AsAboveSoBelow.ABNativePortals"), "UsesNativeRouting");
    private static readonly MethodInfo SetBand = AccessTools.Method(
        AccessTools.TypeByName("AsAboveSoBelow.ABBandView"), "SetBand");

    private enum Stage { Allowed, Forbidden, Removed, Mixed, Finished }

    private static WholeMapPathJobScenario observing;
    private Harmony probe;
    private Pawn walker, upstairs;
    private Building_Door gate, groundDoor, upperDoor;
    private IPathFindCostProvider groundTrap, upperTrap;
    private Thing elevator;
    private object settings;
    private FieldInfo transitAnimation;
    private object previousAnimation;
    private PathRequest request;
    private Stage stage;
    private int stageTick, startedAt = -1, captureFrames;
    private string capture;
    private bool sawInputs, sameLists, hasDoors, hasPawns, hasProviders;

    public override string Id => "aasb.whole-map-path-job";
    public override string Intent => "Inspect vanilla's prepared inputs and route a queued ground-to-ground request around an upper-floor door.";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "direct-api";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override string[] RequiredDefs => new[] { "AB2_LadderUp", "AB2_Elevator", "Door", "TrapSpike" };
    public override FixtureProvider Fixture => new WholeMapPathJobFixture();

    private static void CheckLegacyHookAbsent(Context c)
    {
        var scope = AccessTools.TypeByName("AsAboveSoBelow.ABPathBandScope");
        var mod = scope.Assembly;
        const BindingFlags declared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        c.Check("1.7 assembly omits the legacy patch, filter and bucket state",
            mod.GetType("AsAboveSoBelow.Patch_PathFinder_ABDoorPawnBandScope") == null
            && scope.GetMethod("ScopeDoorJob", declared) == null
            && scope.GetMethod("EnsureBuckets", declared) == null
            && new[] { "bucketMap", "doorsByBand", "pawnsByBand", "providersByBand", "filterCalls" }
                .All(name => scope.GetField(name, declared) == null));
        var patches = Harmony.GetPatchInfo(Prepare);
        c.Check("no AASB patch modifies vanilla door job preparation", patches == null
            || (!patches.Owners.Any(owner => owner.StartsWith("astryl.", StringComparison.OrdinalIgnoreCase))
                && !patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                    .Any(patch => patch.PatchMethod.DeclaringType?.Assembly == mod)));
    }

    public override void Setup(Context c)
    {
        var mod = AccessTools.TypeByName("AsAboveSoBelow.ABMod");
        settings = AccessTools.Property(mod, "Settings").GetValue(null);
        transitAnimation = AccessTools.Field(settings.GetType(), "transitAnim");
        previousAnimation = transitAnimation.GetValue(settings);
        transitAnimation.SetValue(settings, false);

        // Each floor has one opening through the partition. A known spike trap
        // makes the ground detour expensive, while keeping it traversable.
        foreach (int floor in new[] { 0, 64 })
            for (int z = floor; z < floor + 62; z++)
            {
                var cell = new IntVec3(Gate.x, 0, z);
                if (cell != GroundGap && cell != Gate)
                    Spawn(c, ThingDefOf.Wall, cell);
            }

        // A wall below the gate updates the projected upper terrain. Back the gate
        // with a constructed roof so its cell remains walkable after wall placement.
        c.Map.roofGrid.SetRoof(Gate - new IntVec3(0, 0, 64), RoofDefOf.RoofConstructed);
        c.Map.terrainGrid.SetTerrain(Gate, c.Def<TerrainDef>("AB_RoofSurface"));
        Spawn(c, c.Def<ThingDef>("AB2_LadderUp"), new IntVec3(72, 0, 30));
        gate = (Building_Door)Spawn(c, c.Def<ThingDef>("Door"), Gate);
        groundDoor = (Building_Door)Spawn(c, c.Def<ThingDef>("Door"), new IntVec3(58, 0, 37));
        upperDoor = (Building_Door)Spawn(c, c.Def<ThingDef>("Door"), new IntVec3(58, 0, 101));
        groundTrap = (IPathFindCostProvider)Spawn(c, c.Def<ThingDef>("TrapSpike"), GroundGap);
        upperTrap = (IPathFindCostProvider)Spawn(c, c.Def<ThingDef>("TrapSpike"), new IntVec3(60, 0, 100));
        walker = c.SpawnPawn("ground-requester", "Colonist", Start, Faction.OfPlayer);
        upstairs = c.SpawnPawn("upper-input-pawn", "Colonist", new IntVec3(58, 0, 98), Faction.OfPlayer);
        walker.drafter.Drafted = upstairs.drafter.Drafted = true;
        c.Map.roofCollapseBuffer.Clear();
        c.Map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
        var groundGapWalkable = GroundGap.Walkable(c.Map);
        var gateWalkable = Gate.Walkable(c.Map);
        var native = Native(c.Map);
        var ladders = c.Map.listerThings.ThingsOfDef(c.Def<ThingDef>("AB2_LadderUp")).Count;
        var trapCost = groundTrap.PathFindCostFor(walker);
        c.Check("both floors have the expected crossing and ordinary ladders use native routing",
            groundGapWalkable && gateWalkable && native && ladders == 2 && trapCost > 0,
            new { groundGapWalkable, gateWalkable, native, ladders, trapCost,
                groundTerrain = GroundGap.GetTerrain(c.Map).defName,
                groundThings = GroundGap.GetThingList(c.Map).Select(thing => thing.def.defName).ToArray() });

        observing = this;
        probe = new Harmony("aasb.tests.whole-map-path-job");
        probe.Patch(Prepare, postfix: new HarmonyMethod(typeof(WholeMapPathJobScenario), nameof(ObservePreparedJob))
        {
            priority = Priority.Last
        });
    }

    private static Thing Spawn(Context c, ThingDef def, IntVec3 cell)
    {
        var thing = GenSpawn.Spawn(ThingMaker.MakeThing(def, ThingDefOf.Steel), cell, c.Map);
        thing.SetFaction(Faction.OfPlayer);
        return thing;
    }

    private static bool Native(Map map) => UsesNativeRouting.Invoke(null, new object[] { map }) is true;

    // ParameterizeDoorBlockedJob runs on the main thread, immediately before the work is scheduled.
    private static void ObservePreparedJob(PathGridDoorsBlockedJob job, PathRequest request,
        object ___cachedDoors, object ___cachedPawns, object ___cachedProviders)
    {
        var scenario = observing;
        if (scenario == null || request != scenario.request)
            return;
        scenario.sawInputs = true;
        scenario.sameLists = ReferenceEquals(job.doors, ___cachedDoors)
            && ReferenceEquals(job.pawns, ___cachedPawns) && ReferenceEquals(job.providers, ___cachedProviders);
        scenario.hasDoors = job.doors.Contains(scenario.groundDoor) && job.doors.Contains(scenario.upperDoor)
            && (scenario.gate.Destroyed || job.doors.Contains(scenario.gate));
        scenario.hasPawns = job.pawns.Contains(scenario.walker) && job.pawns.Contains(scenario.upstairs);
        scenario.hasProviders = job.providers.Contains(scenario.groundTrap) && job.providers.Contains(scenario.upperTrap);
    }

    public override bool AdvanceNative(Context c)
    {
        if (startedAt < 0)
            startedAt = c.Tick;
        if (c.Tick - startedAt > 240)
        {
            c.Check("all queued requests and captures complete within 240 ticks", false, new { stage });
            return true;
        }
        if (!FinishCapture() || c.Tick <= stageTick)
            return false;
        if (stage == Stage.Finished)
            return true;

        if (request == null)
        {
            if (stage == Stage.Removed && gate.Spawned)
            {
                gate.Destroy();
                stageTick = c.Tick;
                return false;
            }
            if (stage == Stage.Mixed && elevator == null)
            {
                elevator = Spawn(c, c.Def<ThingDef>("AB2_Elevator"), new IntVec3(55, 0, 34));
                stageTick = c.Tick;
                return false;
            }
            IntVec3 goal = stage == Stage.Mixed ? MixedGoal : Goal;
            bool native = Native(c.Map);
            c.Check(stage + " uses the intended routing mode", native == (stage != Stage.Mixed));
            request = c.Map.pathFinder.CreateRequest(Start, goal, goal, walker, null);
            if (stage == Stage.Allowed || stage == Stage.Forbidden)
            {
                ushort gateCost = PathUtility.GetDoorCost(gate, request.TraverseParms, walker, request.Tuning);
                c.Check(stage + " vanilla traversal cost matches the door state",
                    (gateCost == ushort.MaxValue) == (stage == Stage.Forbidden), new { gateCost });
            }
            sawInputs = sameLists = hasDoors = hasPawns = hasProviders = false;
            c.Map.pathFinder.PushRequest(request);
            c.Check(stage + " request entered the queue unresolved", !request.ResultIsReady);
            return false;
        }
        if (!request.TryGetPath(out PawnPath path))
            return false;

        c.Check(stage + " job kept the vanilla lists and both floors' doors, pawns and cost providers",
            sawInputs && sameLists && hasDoors && hasPawns && hasProviders,
            new { sawInputs, sameLists, hasDoors, hasPawns, hasProviders });
        var nodes = path?.NodesReversed.ToArray() ?? Array.Empty<IntVec3>();
        int crossings = nodes.Zip(nodes.Skip(1), (a, b) => Math.Abs(a.z - b.z) > 1).Count(jump => jump);
        bool viaGate = nodes.Contains(Gate);
        bool viaGroundGap = nodes.Contains(GroundGap);
        bool portalRoute = stage == Stage.Allowed || stage == Stage.Removed;
        bool expectedCrossing = stage == Stage.Mixed || (portalRoute ? viaGate && !viaGroundGap : viaGroundGap && !viaGate);
        c.Check(stage + " queued route reaches the ground goal by the expected crossing",
            path?.Found == true && path.LastNode == (stage == Stage.Mixed ? MixedGoal : Goal)
            && crossings == (portalRoute ? 2 : 0) && expectedCrossing,
            new { crossings, viaGate, viaGroundGap, cost = path?.TotalCost });
        c.Event("whole-map-queued-route", new { stage = stage.ToString(), crossings, viaGate, viaGroundGap,
            cost = path?.TotalCost, nodes = nodes.Select(node => node.ToString()).ToArray() });
        request.Dispose();
        request = null;

        switch (stage)
        {
            case Stage.Allowed:
                gate.SetForbidden(true);
                break;
            case Stage.Forbidden:
                Capture(c, 0, GroundGap, nodes, "forbidden-upper-door-ground-detour.png");
                break;
            case Stage.Removed:
                Capture(c, 1, Gate, nodes, "removed-upper-door-portal-shortcut.png");
                break;
        }
        stageTick = c.Tick;
        stage++;
        return false;
    }

    private void Capture(Context c, int floor, IntVec3 focus, IntVec3[] nodes, string filename)
    {
        if (Environment.GetEnvironmentVariable("RWTF_RENDERED") != "1")
            return;
        SetBand.Invoke(null, new object[] { c.Map, floor, false });
        Find.CameraDriver.SetRootPosAndSize(focus.ToVector3Shifted(), 15f);
        foreach (var node in nodes.Where(node => node.z / 64 == floor))
            c.Map.debugDrawer.FlashCell(node, 0.55f, "", 240);
        c.Map.debugDrawer.FlashCell(focus, 0.8f, floor == 0 ? "Ground detour" : "Upper crossing", 240);
        capture = Path.Combine(c.Output, filename);
        captureFrames = 0;
        c.Event("path-job-capture", new { filename, floor, tick = c.Tick });
    }

    private bool FinishCapture()
    {
        if (capture == null)
            return true;
        if (++captureFrames == 2)
            UnityEngine.ScreenCapture.CaptureScreenshot(capture);
        if (!File.Exists(capture) || new FileInfo(capture).Length <= 8)
            return false;
        capture = null;
        return true;
    }

    public override void Assert(Context c)
    {
        CheckLegacyHookAbsent(c);
        c.Check("allowed, forbidden, removed and mixed requests were all exercised", stage == Stage.Finished);
    }

    public override void AfterReload(Context c) =>
        c.Check("named pawns persist on their starting floors after the final reload",
            c.Subject<Pawn>("ground-requester").Position == Start
            && c.Subject<Pawn>("upper-input-pawn").Position.z >= 64);

    public override void Teardown(Context c)
    {
        request?.Dispose();
        probe?.UnpatchAll(probe.Id);
        if (observing == this)
            observing = null;
        if (transitAnimation != null)
            transitAnimation.SetValue(settings, previousAnimation);
    }
}
