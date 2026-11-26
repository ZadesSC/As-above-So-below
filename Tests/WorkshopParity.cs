using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class WorkshopParityFixture : FixtureProvider
{
    private readonly string connector;

    public WorkshopParityFixture(string connector = "AB2_LadderUp") => this.connector = connector;

    public override FixtureContract Contract => new FixtureContract
    {
        Id = "aasb-workshop-parity",
        Version = "1",
        Intent = "Two connected levels with room for power and pollution controls",
        HorizonTicks = 4000,
        Capabilities = new[] { "generated-map", "environment", "save-reload" },
        Interventions = new[] { "manual-two-band-layout", "controlled-floor-and-roof", "storyteller-disabled", "fixed-weather-temperature", "ambient-wildlife-disabled" },
        Invariants = new System.Collections.Generic.Dictionary<string, double> { { "bands", 2 }, { "width", 128 } }
    };

    private static Type BandType => AccessTools.TypeByName("AsAboveSoBelow.ABBandMap");
    private static MapComponent Bands(Map map) => map.components.Single(BandType.IsInstanceOfType);

    public override void Setup(Context c)
    {
        var mod = AccessTools.TypeByName("AsAboveSoBelow.ABMod");
        var settings = AccessTools.Property(mod, "Settings").GetValue(null);
        var multiLevel = AccessTools.Field(settings.GetType(), "multiLevel");
        var previous = multiLevel.GetValue(settings);
        multiLevel.SetValue(settings, false);
        try
        {
            new MapRecipe { Size = 128, Biome = "Desert", Weather = "Clear", Temperature = 21 }.Generate(c);
        }
        finally
        {
            multiLevel.SetValue(settings, previous);
        }
        c.ConfigureEnvironment(new EnvironmentPolicy
        {
            Weather = "Clear", OutdoorC = 21, Storyteller = false,
            RandomDiseases = false, RandomMentalStates = false, SuppressWildlife = true
        });

        AccessTools.Method(BandType, "Setup").Invoke(Bands(c.Map), new object[] { 2, 62, 0 });
        AccessTools.Method(AccessTools.TypeByName("AsAboveSoBelow.ABBandedGeneration"), "Carve")
            .Invoke(null, new object[] { c.Map, Bands(c.Map) });
        Bands(c.Map).FinalizeInit();

        var roofSync = AccessTools.Field(AccessTools.TypeByName("AsAboveSoBelow.ABSkySync"), "Suspended");
        var previousRoofSync = roofSync.GetValue(null);
        roofSync.SetValue(null, true);
        try
        {
            foreach (int floorOffset in new[] { 0, 64 })
            {
                foreach (var cell in new CellRect(52, floorOffset + 22, 24, 20))
                {
                    foreach (var thing in cell.GetThingList(c.Map).ToArray())
                    {
                        if (thing is Pawn) continue;
                        if (thing.def.destroyable) thing.Destroy();
                        else thing.DeSpawn();
                    }
                    c.Map.roofGrid.SetRoof(cell, floorOffset == 0 ? RoofDefOf.RoofConstructed : null);
                    c.Map.terrainGrid.SetTerrain(cell, c.Def<TerrainDef>(floorOffset == 0 ? "Concrete" : "AB_RoofSurface"));
                    c.Map.fogGrid.Unfog(cell);
                }
            }
            foreach (int x in new[] { 54, 62, 70, 74 })
            {
                foreach (int z in new[] { 23, 28, 38 })
                {
                    var supportCell = new IntVec3(x, 0, z);
                    var support = GenSpawn.Spawn(ThingMaker.MakeThing(c.Def<ThingDef>("Column"), ThingDefOf.Steel),
                        supportCell, c.Map);
                    support.SetFaction(Faction.OfPlayer);
                }
            }
        }
        finally
        {
            roofSync.SetValue(null, previousRoofSync);
        }
        var link = GenSpawn.Spawn(ThingMaker.MakeThing(c.Def<ThingDef>(connector), ThingDefOf.Steel),
            new IntVec3(64, 0, 30), c.Map);
        link.SetFaction(Faction.OfPlayer);
        c.Map.roofCollapseBuffer.Clear();
        c.Map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
    }

    public override void Validate(Context c, string stage)
    {
        bool passed = c.Map.Size.x == 128 && (int)AccessTools.Field(BandType, "bandCount").GetValue(Bands(c.Map)) == 2;
        c.Check("two-level fixture " + stage, passed);
        c.Event("fixture-validation", new { stage, supported = true, passed, invariants = Contract.Invariants });
    }
}

public sealed class WorkshopParityScenario : Rwtf.Scenario
{
    private static readonly IntVec3 Home = new IntVec3(61, 0, 32);
    private static readonly IntVec3 ReplacementGoal = new IntVec3(59, 0, 35);
    private static readonly IntVec3 Goal = new IntVec3(68, 0, 96);
    private static readonly IntVec3 ColumnCell = new IntVec3(57, 0, 30);
    private static readonly IntVec3 AirCell = new IntVec3(55, 0, 90);
    private static readonly IntVec3 SpaceCell = new IntVec3(69, 0, 34);
    private static readonly IntVec3 PollutableControl = new IntVec3(69, 0, 36);

    private enum TravelPhase
    {
        OrderFirstTrip,
        WaitForFirstHold,
        ConfirmNoCrossing,
        WaitForSecondHold,
        ConfirmReplacement,
        FinishThirdTrip
    }

    private Pawn walker;
    private ThingDef carrierDef;
    private TravelPhase phase;
    private int cancelledAt;
    private bool replacementReached;
    private bool replacementCrossed;
    private IDictionary heldCrossings;

    public override string Id => "aasb.workshop-parity";
    public override string Intent => "Walk between levels, reconcile saved utility carriers and recalculate pollution after terrain changes";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2", "ludeon.rimworld.biotech", "ludeon.rimworld.odyssey" };
    public override string[] RequiredDefs => new[] { "AB_ColumnPower", "AB2_LadderUp" };
    public override FixtureProvider Fixture => new WorkshopParityFixture();

    public override void Setup(Context c)
    {
        walker = c.SpawnPawn("workshop-walker", "Colonist", Home, Faction.OfPlayer);
        walker.drafter.Drafted = true;
        Find.ResearchManager.FinishProject(c.Def<ResearchProjectDef>("Electricity"), false);
        var column = GenSpawn.Spawn(ThingMaker.MakeThing(c.Def<ThingDef>("AB_ColumnPower")), ColumnCell, c.Map);
        column.SetFaction(Faction.OfPlayer);
        carrierDef = ColumnCell.GetThingList(c.Map).Select(thing => thing.def)
            .Single(def => def.defName.StartsWith("AB_Carrier_", StringComparison.Ordinal));
        var upperCell = ColumnCell + new IntVec3(0, 0, 64);
        c.Check("power column generated exactly two carriers", c.Map.listerThings.ThingsOfDef(carrierDef).Count == 2);
        c.Check("upper carrier exists", upperCell.GetThingList(c.Map).Any(t => t.def == carrierDef));
        GenSpawn.Spawn(carrierDef, ColumnCell, c.Map);
        c.Check("legacy duplicate reproduced", ColumnCell.GetThingList(c.Map).Count(t => t.def == carrierDef) == 2);

        var grid = c.Map.pollutionGrid;
        grid.SetPolluted(PollutableControl, true, true);
        grid.SetPolluted(SpaceCell, true, true);
        c.Map.terrainGrid.SetTerrain(SpaceCell, c.Def<TerrainDef>("Space"));
        c.Check("legacy polluted non-air and pollutable control reproduced",
            !grid.EverPollutable(SpaceCell) && grid.IsPolluted(SpaceCell)
            && grid.EverPollutable(PollutableControl) && grid.IsPolluted(PollutableControl));
        grid.SetPolluted(AirCell, true, true);
        c.Check("setup polluted the upper cell", grid.IsPolluted(AirCell));
        c.Check("pollution numerator and vanilla denominator agree", PercentMatches(c.Map));
        c.Map.terrainGrid.SetTerrain(AirCell, c.Def<TerrainDef>("AB_OpenAir"));
        c.Check("legacy polluted air reproduced", grid.IsPolluted(AirCell));
        c.Check("open air excluded and cache agrees with game scan", !grid.EverPollutable(AirCell) && PercentMatches(c.Map));
        var patherType = AccessTools.TypeByName("AsAboveSoBelow.ABWormholePather");
        heldCrossings = (IDictionary)AccessTools.Field(patherType, "holding").GetValue(null);
    }

    private static bool PercentMatches(Map map)
    {
        var grid = map.pollutionGrid;
        return Math.Abs(grid.TotalPollutionPercent - (float)grid.TotalPollution / grid.AllPollutableCells.Count) < 0.000001f;
    }

    public override bool AdvanceNative(Context c)
    {
        switch (phase)
        {
            case TravelPhase.OrderFirstTrip:
                bool ready = walker?.Spawned == true && walker.jobs != null;
                c.Check("walker survived fixture warm-up", ready);
                if (!ready) return true;
                OrderGoto(Goal);
                phase = TravelPhase.WaitForFirstHold;
                break;
            case TravelPhase.WaitForFirstHold:
                if (heldCrossings.Contains(walker.thingIDNumber))
                {
                    c.Check("first trip reached animated hold on surface", walker.Position.z < 64);
                    walker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    cancelledAt = c.Tick;
                    phase = TravelPhase.ConfirmNoCrossing;
                }
                break;
            case TravelPhase.ConfirmNoCrossing:
                if (c.Tick - cancelledAt >= 130)
                {
                    c.Check("cancelled hold expired without crossing",
                        walker.Spawned && walker.Position.z < 64 && !heldCrossings.Contains(walker.thingIDNumber));
                    OrderGoto(Goal);
                    phase = TravelPhase.WaitForSecondHold;
                }
                break;
            case TravelPhase.WaitForSecondHold:
                if (heldCrossings.Contains(walker.thingIDNumber))
                {
                    c.Check("second trip reached animated hold on surface", walker.Position.z < 64);
                    OrderGoto(ReplacementGoal);
                    cancelledAt = c.Tick;
                    phase = TravelPhase.ConfirmReplacement;
                }
                break;
            case TravelPhase.ConfirmReplacement:
                replacementReached |= walker.Spawned && walker.Position == ReplacementGoal;
                replacementCrossed |= walker.Spawned && walker.Position.z >= 64;
                int replacementTicks = c.Tick - cancelledAt;
                if ((replacementTicks >= 130 && replacementReached) || replacementCrossed || replacementTicks >= 350)
                {
                    c.Check("replacement order reached its surface destination without crossing",
                        replacementReached && !replacementCrossed && !heldCrossings.Contains(walker.thingIDNumber),
                        new { replacementTicks, replacementReached, replacementCrossed, cell = walker.Position.ToString(),
                            job = walker.CurJob?.def?.defName, moving = walker.pather?.Moving });
                    OrderGoto(Goal);
                    phase = TravelPhase.FinishThirdTrip;
                }
                break;
            case TravelPhase.FinishThirdTrip:
                return walker?.Spawned == true && walker.Position == Goal;
        }

        return false;
    }

    private void OrderGoto(IntVec3 destination)
    {
        var order = JobMaker.MakeJob(JobDefOf.Goto, destination);
        order.playerForced = true;
        walker.jobs.StartJob(order, JobCondition.InterruptForced);
    }

    public override void Assert(Context c)
    {
        c.Check("assigned Goto reached the intended upper level", phase == TravelPhase.FinishThirdTrip && walker?.Spawned == true && walker.Position == Goal);
    }

    public override void AfterReload(Context c)
    {
        var loaded = c.Subject<Pawn>("workshop-walker");
        c.Check("walker survived reload on upper level", loaded.Position == Goal && !ReferenceEquals(loaded, walker));
        var carriers = c.Map.listerThings.ThingsOfDef(carrierDef);
        c.Check("reload removed duplicate without losing either endpoint",
            carriers.Count == 2 && carriers.Count(t => t.Position == ColumnCell) == 1
                && carriers.Count(t => t.Position == ColumnCell + new IntVec3(0, 0, 64)) == 1);
        var grid = c.Map.pollutionGrid;
        c.Check("air and other invalid terrain are clear of pollution after reload",
            !grid.IsPolluted(AirCell) && !grid.IsPolluted(SpaceCell));
        c.Check("pollutable control persists and cache still agrees with game scan",
            grid.IsPolluted(PollutableControl) && PercentMatches(c.Map));
    }
}

public sealed class WorkshopCombatScenario : Rwtf.Scenario
{
    private static readonly IntVec3 UpperCell = new IntVec3(71, 0, 95);
    private static readonly IntVec3 LowerCell = new IntVec3(66, 0, 31);
    private static readonly IntVec3 Opening = new IntVec3(66, 0, 95);
    private Pawn upper;
    private Pawn lower;
    private int openedAt = -1;

    public override string Id => "aasb.workshop-combat";
    public override string Intent => "Exercise the vanilla ranged-verb visibility gate in both directions across a real opening";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "direct-api";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override string[] RequiredDefs => new[] { "AB2_LadderUp", "Gun_BoltActionRifle" };
    public override FixtureProvider Fixture => new WorkshopParityFixture();

    public override void Setup(Context c)
    {
        upper = c.SpawnPawn("upper-shooter", "Colonist", UpperCell, Faction.OfPlayer);
        lower = c.SpawnPawn("lower-shooter", "Colonist", LowerCell, Faction.OfPlayer);
        foreach (Pawn pawn in new[] { upper, lower })
        {
            pawn.drafter.Drafted = true;
            var rifle = (ThingWithComps)ThingMaker.MakeThing(c.Def<ThingDef>("Gun_BoltActionRifle"));
            pawn.equipment.AddEquipment(rifle);
        }
    }

    private static bool CanHit(Pawn shooter, Pawn target)
    {
        Verb verb = shooter.equipment.PrimaryEq.PrimaryVerb;
        return verb.TryFindShootLineFromTo(shooter.Position, target, out _);
    }

    public override bool AdvanceNative(Context c)
    {
        if (openedAt < 0)
        {
            c.Check("both subjects survived on their assigned floors", upper.Spawned && lower.Spawned
                && upper.Position == UpperCell && lower.Position == LowerCell);
            c.Check("sealed floor blocks shots in both directions", !CanHit(upper, lower) && !CanHit(lower, upper));
            c.Map.terrainGrid.SetTerrain(Opening, c.Def<TerrainDef>("AB_OpenAir"));
            c.Check("real opening inserted", c.Map.terrainGrid.TerrainAt(Opening).defName == "AB_OpenAir");
            openedAt = c.Tick;
        }
        return c.Tick > openedAt;
    }

    public override void Assert(Context c)
    {
        c.Check("ranged verb finds a line down through the opening", CanHit(upper, lower));
        c.Check("ranged verb finds a line up through the opening", CanHit(lower, upper));
    }

    public override void AfterReload(Context c)
    {
        c.Check("both shooters persisted on their floors", c.Subject<Pawn>("upper-shooter").Position == UpperCell
            && c.Subject<Pawn>("lower-shooter").Position == LowerCell);
    }
}

public sealed class WorkshopRopeScenario : Rwtf.Scenario
{
    private static readonly IntVec3 NearConnector = new IntVec3(64, 0, 30);
    private static readonly IntVec3 HitchingCell = new IntVec3(71, 0, 96);
    private Pawn handler;
    private Pawn sheep;
    private Building hitchingSpot;
    private System.Collections.IDictionary heldCrossings;
    private bool started;
    private bool confirmedHold;
    private bool confirmedCarry;

    public override string Id => "aasb.workshop-ropee";
    public override string Intent => "Move a roped animal through a ladder with a native hitching job";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override string[] RequiredDefs => new[] { "AB2_LadderUp", "CaravanPackingSpot" };
    public override FixtureProvider Fixture => new WorkshopParityFixture();

    public override void Setup(Context c)
    {
        handler = c.SpawnPawn("animal-handler", "Colonist", NearConnector, Faction.OfPlayer);
        sheep = c.SpawnPawn("roped-sheep", "Sheep", NearConnector + IntVec3.East, Faction.OfPlayer);
        hitchingSpot = (Building)GenSpawn.Spawn(c.Def<ThingDef>("CaravanPackingSpot"), HitchingCell, c.Map);
        hitchingSpot.SetFaction(Faction.OfPlayer);
        var patherType = AccessTools.TypeByName("AsAboveSoBelow.ABWormholePather");
        heldCrossings = (System.Collections.IDictionary)AccessTools.Field(patherType, "holding").GetValue(null);
    }

    public override bool AdvanceNative(Context c)
    {
        if (!started)
        {
            started = true;
            c.Check("handler and sheep survived warm-up on the surface",
                handler.Spawned && sheep.Spawned && handler.Position.z < 64 && sheep.Position.z < 64);
            handler.jobs.StopAll();
            handler.Position = NearConnector;
            handler.Notify_Teleported();
            sheep.Position = NearConnector + IntVec3.East;
            sheep.Notify_Teleported();
            handler.roping.RopePawn(sheep);
            var job = JobMaker.MakeJob(c.Def<JobDef>("RopeRoamerToHitchingPost"), sheep, hitchingSpot);
            job.playerForced = true;
            handler.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        if (!confirmedHold && heldCrossings.Contains(handler.thingIDNumber))
        {
            confirmedHold = true;
            c.Check("native roping job entered the held crossing with a nearby ropee",
                handler.jobs.curDriver is JobDriver_RopeToDestination
                && sheep.roping.RopedByPawn == handler && sheep.Position.InHorDistOf(NearConnector, 4f));
        }

        if (!confirmedCarry && handler.Position.z >= 64)
        {
            confirmedCarry = true;
            c.Check("animal moved with the handler on the landing frame",
                sheep.Position.z >= 64 && sheep.roping.RopedByPawn == handler,
                new { handler = handler.Position.ToString(), sheep = sheep.Position.ToString(),
                    roped = sheep.roping.RopedByPawn == handler });
        }
        return sheep.roping.RopedToHitchingSpot == hitchingSpot;
    }

    public override void Assert(Context c)
    {
        c.Check("native job hitched the transported animal on the upper level",
            confirmedHold && confirmedCarry && sheep.Position.z >= 64 && sheep.roping.RopedToHitchingSpot == hitchingSpot);
    }

    public override void AfterReload(Context c)
    {
        var loaded = c.Subject<Pawn>("roped-sheep");
        c.Check("transported animal and hitch survived reload", loaded.Position.z >= 64
            && loaded.roping.RopedToHitchingSpot?.Position == HitchingCell);
    }
}

public sealed class WorkshopChairRadiusScenario : Rwtf.Scenario
{
    private static readonly IntVec3 Start = new IntVec3(60, 0, 30);
    private static readonly IntVec3 Exit = new IntVec3(64, 0, 94);
    private static readonly IntVec3 Seat = new IntVec3(67, 0, 94);
    private const int ApproachAndFlight = 11;
    private const int CombinedCost = 14;
    private Pawn diner;
    private Thing meal;
    private Building chair;
    private Building table;
    private Building_Door exit;
    private float originalRadius;
    private int jobRadius;
    private bool started;
    private bool observedEating;
    private bool ateUpstairs;
    private int captureFrames;
    private string capturePath;

    public override string Id => "aasb.workshop-chair-radius";
    public override string Intent => "Verify the combined chair radius and observe a native ingestion job";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override string[] RequiredDefs => new[] { "AB2_StairsUp", "DiningChair", "Table1x2c", "MealSimple" };
    public override FixtureProvider Fixture => new WorkshopParityFixture("AB2_StairsUp");

    public override void Setup(Context c)
    {
        diner = c.SpawnPawn("diner", "Colonist", Start, Faction.OfPlayer);
        diner.Name = new NameSingle("Diner");
        meal = ThingMaker.MakeThing(c.Def<ThingDef>("MealSimple"));
        originalRadius = meal.def.ingestible.chairSearchRadius;
        chair = SpawnFurniture(c, "DiningChair", Seat, Rot4.East);
        table = SpawnFurniture(c, "Table1x2c", Seat + IntVec3.East, Rot4.North);
        var pairs = new List<KeyValuePair<Building_Door, Building_Door>>();
        AccessTools.Method(c.OptionalType("AsAboveSoBelow.ABWormhole"), "CollectPairs")
            .Invoke(null, new object[] { c.Map, pairs });
        var pair = pairs.Single();
        var near = pair.Key.Position.z < 64 ? pair.Key : pair.Value;
        exit = near == pair.Key ? pair.Value : pair.Key;
        c.Check("fixture has one stair pair and a valid upstairs dining set",
            near.Position == new IntVec3(64, 0, 30) && exit.Position == Exit
            && chair.Position == Seat && table.def.surfaceType == SurfaceType.Eat
            && c.Map.reachability.CanReach(Exit, chair, PathEndMode.OnCell, TraverseParms.For(diner)),
            new { start = Start.ToString(), entrance = near.Position.ToString(), exit = exit.Position.ToString(),
                seat = Seat.ToString(), approach = (near.Position - Start).LengthHorizontal,
                exitLeg = (Seat - Exit).LengthHorizontal });
        jobRadius = int.Parse(c.Parameter("ChairJobRadius", CombinedCost.ToString()));
        if (jobRadius != CombinedCost && jobRadius != CombinedCost - 1)
            throw new Unsupported("Choose the boundary or one cell below it for the photographed job");
    }

    private static Building SpawnFurniture(Context c, string def, IntVec3 cell, Rot4 rotation)
    {
        var building = (Building)ThingMaker.MakeThing(c.Def<ThingDef>(def), ThingDefOf.Steel);
        building.SetFaction(Faction.OfPlayer);
        return (Building)GenSpawn.Spawn(building, cell, c.Map, rotation);
    }

    public override bool AdvanceNative(Context c)
    {
        if (!started)
        {
            started = true;
            diner.jobs.StopAll();
            diner.Position = Start;
            diner.Notify_Teleported();
            diner.needs.food.CurLevelPercentage = 0.35f;
            try
            {
                CheckSeat(c, "in range", CombinedCost + 1, Seat);
                CheckSeat(c, "exact combined budget", CombinedCost, Seat);
                CheckSeat(c, "one cell beyond combined budget", CombinedCost - 1, null);
                CheckSeat(c, "flight consumes more than the budget", ApproachAndFlight - 1, null);
                CheckSeat(c, "zero remainder excludes positive exit leg", ApproachAndFlight, null);
                CheckSeatAtExit(c);

                var localChair = SpawnFurniture(c, "DiningChair", Start + IntVec3.West, Rot4.West);
                var localTable = SpawnFurniture(c, "Table1x2c", localChair.Position + IntVec3.West, Rot4.North);
                try { CheckSeat(c, "vanilla nearby chair takes precedence", CombinedCost, localChair.Position); }
                finally { localChair.Destroy(); localTable.Destroy(); }
            }
            finally
            {
                meal.def.ingestible.chairSearchRadius = originalRadius;
            }

            meal.def.ingestible.chairSearchRadius = jobRadius;
            GenSpawn.Spawn(meal, Start, c.Map);
            var job = JobMaker.MakeJob(JobDefOf.Ingest, meal);
            job.count = 1;
            job.playerForced = true;
            diner.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        if (!observedEating)
        {
            if (diner.CurJobDef != JobDefOf.Ingest || diner.jobs.curDriver.CurToilString != "ChewIngestible"
                || diner.carryTracker.CarriedThing != meal)
                return false;

            observedEating = true;
            ateUpstairs = diner.Position == Seat;
            bool expected = jobRadius == CombinedCost ? ateUpstairs : diner.Position.z < 64;
            c.Check("native ingestion destination obeys the combined chair budget", expected,
                new { radius = jobRadius, cost = CombinedCost, pos = diner.Position.ToString(),
                    table = table.Position.ToString(), carrying = diner.carryTracker.CarriedThing.Label });
            if (!string.IsNullOrEmpty(c.Parameter("CaptureChairJob")))
            {
                AccessTools.Method(c.OptionalType("AsAboveSoBelow.ABBandView"), "SetBand")
                    .Invoke(null, new object[] { c.Map, diner.Position.z < 64 ? 0 : 1, false });
                Find.CameraDriver.SetRootPosAndSize(diner.Position.ToVector3Shifted(), 12f);
                Find.Selector.Select(diner);
                string outcome = ateUpstairs ? "Diner reached the upstairs table" : "Diner stayed downstairs";
                Messages.Message($"Chair radius {jobRadius}, upstairs chair costs {CombinedCost}. {outcome}.",
                    MessageTypeDefOf.NeutralEvent, false);
                capturePath = Path.Combine(c.Output, "chair-ingestion.png");
            }
        }
        if (capturePath != null)
        {
            captureFrames++;
            if (captureFrames == 3)
            {
                UnityEngine.ScreenCapture.CaptureScreenshot(capturePath);
                c.Event("chair-capture-request", new { file = Path.GetFileName(capturePath), tick = c.Tick,
                    pawn = diner.Position.ToString(), radius = jobRadius, expectedCost = CombinedCost });
            }
            if (captureFrames < 4 || !File.Exists(capturePath))
                return false;
        }
        return meal.Destroyed;
    }

    private void CheckSeatAtExit(Context c)
    {
        // A connector normally occupies the exit cell. Make that building a valid seat temporarily
        // to exercise zero remaining distance without introducing an overlapping edifice.
        var exitTable = SpawnFurniture(c, "Table1x2c", Exit + IntVec3.West, Rot4.North);
        var props = exit.def.building;
        var previous = props.isSittable;
        var previousFaction = exit.Faction;
        try
        {
            props.isSittable = true;
            exit.SetFactionDirect(Faction.OfPlayer);
            bool free = Toils_Ingest.TryFindFreeSittingSpotOnThing(exit, diner, out var seat);
            bool reachable = c.Map.reachability.CanReach(Exit, exit, PathEndMode.OnCell, TraverseParms.For(diner));
            c.Check("the synthetic seat at the exit is valid", free && seat == Exit && reachable
                && (seat + IntVec3.West).GetEdifice(c.Map) == exitTable,
                new { free, seat = seat.ToString(), reachable, table = exitTable.Position.ToString() });
            CheckSeat(c, "zero remainder includes a seat exactly at the exit", ApproachAndFlight, Exit);
        }
        finally
        {
            exit.SetFactionDirect(previousFaction);
            props.isSittable = previous;
            exitTable.Destroy();
        }
    }

    private void CheckSeat(Context c, string name, float radius, IntVec3? expected)
    {
        meal.def.ingestible.chairSearchRadius = radius;
        bool found = Toils_Ingest.TryFindChairOrSpot(diner, meal, out var cell);
        c.Check(name, found && (expected.HasValue ? cell == expected.Value : cell.z < 64
            && cell.GetEdifice(c.Map)?.def.building.isSittable != true), new { radius,
                approachAndFlight = ApproachAndFlight, combined = CombinedCost, cell = cell.ToString(), found });
    }

    public override void Assert(Context c)
    {
        c.Check("native ingestion finished and consumed its meal", observedEating && meal.Destroyed);
        meal.def.ingestible.chairSearchRadius = originalRadius;
    }

    public override void Teardown(Context c)
    {
        if (meal != null)
            meal.def.ingestible.chairSearchRadius = originalRadius;
    }
}
