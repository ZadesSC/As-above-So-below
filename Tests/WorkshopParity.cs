using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class WorkshopParityFixture : FixtureProvider
{
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
        var ladder = GenSpawn.Spawn(ThingMaker.MakeThing(c.Def<ThingDef>("AB2_LadderUp"), ThingDefOf.Steel),
            new IntVec3(64, 0, 30), c.Map);
        ladder.SetFaction(Faction.OfPlayer);
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
    private static readonly IntVec3 Goal = new IntVec3(68, 0, 96);
    private static readonly IntVec3 ColumnCell = new IntVec3(57, 0, 30);
    private static readonly IntVec3 AirCell = new IntVec3(55, 0, 90);
    private Pawn walker;
    private ThingDef carrierDef;
    private bool started;

    public override string Id => "aasb.workshop-parity";
    public override string Intent => "Walk between levels, reconcile saved utility carriers and recalculate pollution after terrain changes";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2", "ludeon.rimworld.biotech" };
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
        grid.SetPolluted(AirCell, true, true);
        c.Check("setup polluted the upper cell", grid.IsPolluted(AirCell));
        c.Check("pollution numerator and vanilla denominator agree", PercentMatches(c.Map));
        c.Map.terrainGrid.SetTerrain(AirCell, c.Def<TerrainDef>("AB_OpenAir"));
        c.Check("legacy polluted air reproduced", grid.IsPolluted(AirCell));
        c.Check("open air excluded and cache agrees with game scan", !grid.EverPollutable(AirCell) && PercentMatches(c.Map));
    }

    private static bool PercentMatches(Map map)
    {
        var grid = map.pollutionGrid;
        return Math.Abs(grid.TotalPollutionPercent - (float)grid.TotalPollution / grid.AllPollutableCells.Count) < 0.000001f;
    }

    public override bool AdvanceNative(Context c)
    {
        if (!started)
        {
            started = true;
            bool ready = walker?.Spawned == true && walker.jobs != null;
            c.Check("walker survived fixture warm-up", ready);
            if (!ready)
            {
                return true;
            }
            var order = JobMaker.MakeJob(JobDefOf.Goto, Goal);
            order.playerForced = true;
            walker.jobs.StartJob(order, JobCondition.InterruptForced);
        }
        return walker?.Spawned == true && walker.Position == Goal;
    }

    public override void Assert(Context c)
    {
        c.Check("assigned Goto reached the intended upper level", walker?.Spawned == true && walker.Position == Goal);
    }

    public override void AfterReload(Context c)
    {
        var loaded = c.Subject<Pawn>("workshop-walker");
        c.Check("walker survived reload on upper level", loaded.Position == Goal && !ReferenceEquals(loaded, walker));
        var carriers = c.Map.listerThings.ThingsOfDef(carrierDef);
        c.Check("reload removed duplicate without losing either endpoint",
            carriers.Count == 2 && carriers.Count(t => t.Position == ColumnCell) == 1
                && carriers.Count(t => t.Position == ColumnCell + new IntVec3(0, 0, 64)) == 1);
        c.Check("open air is clear of pollution after reload", !c.Map.pollutionGrid.IsPolluted(AirCell) && PercentMatches(c.Map));
    }
}
