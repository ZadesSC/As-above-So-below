using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Rwtf;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using UnityEngine;
using HarmonyLib;

public sealed class RoofFixture : FixtureProvider
{
    public override FixtureContract Contract => new FixtureContract {
        Id = "thin-roof-support", Version = "1", Intent = "generate and remove roof-backed thin rock shelves",
        HorizonTicks = 3000, Capabilities = new[] {"generated-map","environment","save-reload"},
        Interventions = new[] {"generated-test-map","controlled-test-roofs-and-rock-supports","native-roof-removal-jobs","configured-three-bands-if-enabled","isolated-weather-and-wildlife","setup-pawn-retirement","controlled-native-connector-placement-and-travel"},
        Invariants = new Dictionary<string,double>{{"size",192},{"targetCount",6}}
    };
    public static bool Banded(Context c) => c.Parameter("mode") == "aasb";
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    public static object Bands(Context c) => c.Map.components.First(x=>x.GetType().FullName == "AsAboveSoBelow.ABBandMap");
    static void Retire(Pawn p) {
        p.GetLord()?.Notify_PawnLost(p,PawnLostCondition.ExitedMap);
        p.ownership?.UnclaimAll(); p.jobs?.StopAll(); p.DeSpawn(); Find.WorldPawns.PassToWorld(p,PawnDiscardDecideMode.KeepForever);
    }
    public override void Setup(Context c) {
        FieldInfo multi=null; object settings=null, old=null;
        if(Banded(c)) {
            var type=c.OptionalType("AsAboveSoBelow.ABMod");
            settings=type.GetProperty("Settings",Flags)?.GetValue(null)??type.GetField("Settings",Flags).GetValue(null);
            multi=settings.GetType().GetField("multiLevel",Flags);old=multi.GetValue(settings);multi.SetValue(settings,false);
        }
        try { new MapRecipe { Size=192,Seed=62391,Biome="Desert",Weather="Clear",Temperature=21 }.Generate(c); }
        finally { if(multi!=null)multi.SetValue(settings,old); }
        foreach(var extra in Find.Maps.Where(m=>m!=c.Map).ToList()) {
            foreach(var p in extra.mapPawns.AllPawnsSpawned.ToList()) Retire(p);
            Current.Game.DeinitAndRemoveMap(extra,false);
        }
        foreach(var p in c.Map.mapPawns.AllPawnsSpawned.ToList()) Retire(p);
        c.ConfigureEnvironment(new EnvironmentPolicy {Weather="Clear",OutdoorC=21,Storyteller=false,RandomDiseases=false,RandomMentalStates=false,SuppressWildlife=true});
        if(Banded(c)){
            var b=Bands(c);var t=b.GetType();
            t.GetMethod("Setup",Flags).Invoke(b,new object[]{3,62,1});
            foreach (var cell in CellRect.CenteredOn(RoofCase.GeneratedRoof, 1))
            {
                if (cell.GetEdifice(c.Map) is Thing rock) rock.Destroy();
                c.Map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }
            c.OptionalType("AsAboveSoBelow.ABBandedGeneration").GetMethod("Carve",Flags).Invoke(null,new object[]{c.Map,b});
            t.GetMethod("FinalizeInit",Flags).Invoke(b,null);
            t.GetMethod("Open",Flags).Invoke(b,new object[]{0});
            t.GetMethod("Open",Flags).Invoke(b,new object[]{2});
        }
        // Clear test neighborhoods only. Surface and underground cases have distinct x coordinates
        // so the three upstairs readings never touch the other test's cells.
        for(int i=0;i<6;i++){
            var cell=RoofCase.Cell(i);
            foreach(var cc in CellRect.CenteredOn(cell,8)) {
                c.Map.roofGrid.SetRoof(cc,null);
                foreach(var thing in cc.GetThingList(c.Map).ToList()) {
                    if(thing is Pawn)Retire((Pawn)thing);
                    else if(thing.def.destroyable)thing.Destroy();
                    else thing.DeSpawn();
                }
                c.Map.terrainGrid.SetTerrain(cc,c.Def<TerrainDef>("Gravel"));
                c.Map.fogGrid.Unfog(cc);
            }
            if(Banded(c)){
                var up=cell+new IntVec3(0,0,64);
                c.Map.roofGrid.SetRoof(up,null);
                foreach(var thing in up.GetThingList(c.Map).ToList()) {
                    if(thing.def.destroyable)thing.Destroy();
                    else thing.DeSpawn();
                }
                // Surface -> sky starts open for sync; underground -> surface keeps natural surface terrain.
                c.Map.terrainGrid.SetTerrain(up,c.Def<TerrainDef>(i>=3?"AB_OpenAir":"Gravel"));
                foreach (var visible in CellRect.CenteredOn(up, 8)) c.Map.fogGrid.Unfog(visible);
            }
        }
        c.Map.roofCollapseBuffer.Clear();
        for(int i=0;i<6;i++){
            var cell=RoofCase.Cell(i);
            GenSpawn.Spawn(c.Def<ThingDef>("Granite"),cell+IntVec3.East,c.Map);
            c.Map.roofGrid.SetRoof(cell,RoofCase.Roof(i));
        }
        var thin = RoofCase.Cell(4);
        var rooftop = c.Def<TerrainDef>("AB_OpenAir");
        var belowSupport = c.Def<ThingDef>("Granite");
        c.Map.roofGrid.SetRoof(thin, null);
        foreach (var cell in CellRect.CenteredOn(thin, 2))
        {
            var upper = cell + new IntVec3(0, 0, 64);
            c.Map.roofGrid.SetRoof(upper, null);
            foreach (var thing in upper.GetThingList(c.Map).ToList()) thing.Destroy();
            c.Map.terrainGrid.SetTerrain(upper, rooftop);
            if (cell.x == thin.x + 2 && cell.GetEdifice(c.Map) == null)
            {
                GenSpawn.Spawn(belowSupport, cell, c.Map);
            }
            c.Map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
        }
        c.Map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
    }
    public override void Validate(Context c,string stage){
        bool pass=c.Map.Size.x==192 && (!Banded(c)||(int)Bands(c).GetType().GetField("surfaceBand",Flags).GetValue(Bands(c))==1);
        c.Check("fixture invariants "+stage,pass);
        c.Event("fixture-validation",new{stage,supported=true,passed=pass,invariants=Contract.Invariants});
    }
}

public sealed class RoofCase : Rwtf.Scenario
{
    public override string Id => "aasb.thin-roof-support";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override FixtureProvider Fixture => new RoofFixture();
    public override string EvidenceLevel => "direct-api";
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public static IntVec3 Cell(int i) => new IntVec3(22 + i * 27, 0, i < 3 ? 30 : 94);
    public static IntVec3 GeneratedRoof => new IntVec3(160, 0, 82);
    public static RoofDef Roof(int i) => i % 3 == 0 ? RoofDefOf.RoofRockThick :
        i % 3 == 1 ? RoofDefOf.RoofRockThin : RoofDefOf.RoofConstructed;

    private static IntVec3 Thin => Cell(4);
    private static IntVec3 Upper => Thin + new IntVec3(0, 0, 64);

    public override void Setup(Context c)
    {
        c.Spawn("marker", "Steel", Cell(0) + new IntVec3(4, 0, 4), count: 7);
    }

    public override void Assert(Context c)
    {
        var map = c.Map;
        var grid = map.terrainGrid;
        var rooftop = c.Def<TerrainDef>("AB_RoofSurface");
        var openAir = c.Def<TerrainDef>("AB_OpenAir");
        var mountain = c.Def<TerrainDef>("AB_MountainTop");
        c.Check("natural thin roof stays in roof grid", map.roofGrid.RoofAt(Thin) == RoofDefOf.RoofRockThin);
        c.Check("thin roof creates standalone walkable upper terrain",
            grid.TerrainAt(Upper) == rooftop && Upper.Standable(map));
        c.Check("generation assigns roof-backed surface to an isolated thin rock cap",
            map.roofGrid.RoofAt(GeneratedRoof) == RoofDefOf.RoofRockThin &&
            grid.TerrainAt(GeneratedRoof + new IntVec3(0, 0, 64)) == rooftop);
        c.Check("overhead mountain projects mountain terrain",
            grid.TerrainAt(Cell(3) + new IntVec3(0, 0, 64)) == mountain);

        var finish = c.Def<TerrainDef>("WoodPlankFloor");
        grid.SetTerrain(Upper, finish);
        c.Check("wood finish sits on rooftop", grid.UnderTerrainAt(Upper) == rooftop);
        grid.RemoveTopLayer(Upper, doLeavings: false);
        c.Check("removing finish exposes rooftop and leaves roof", grid.TerrainAt(Upper) == rooftop &&
            map.roofGrid.RoofAt(Thin) == RoofDefOf.RoofRockThin);
        grid.SetTerrain(Upper, finish);

        var builder = c.SpawnPawn("builder", "Colonist", Thin + IntVec3.South, Faction.OfPlayer);
        var work = new WorkGiver_RemoveRoof();
        map.areaManager.NoRoof[Thin] = true;
        map.roofGrid.SetRoof(Upper, RoofDefOf.RoofConstructed);
        c.Check("native work giver defers removal while the upper tile has its own roof",
            map.roofGrid.RoofAt(Upper) == RoofDefOf.RoofConstructed && !work.HasJobOnCell(builder, Thin));
        c.Check("both roofs remain while removal is blocked",
            map.roofGrid.RoofAt(Thin) == RoofDefOf.RoofRockThin &&
            map.roofGrid.RoofAt(Upper) == RoofDefOf.RoofConstructed);
        map.roofGrid.SetRoof(Upper, null);
        c.Check("roof job becomes available after removing the separate upper roof", work.HasJobOnCell(builder, Thin));

        var blocker = SpawnStool(c);
        c.Check("native work giver defers removal with a building above",
            blocker.Position == Upper && !work.HasJobOnCell(builder, Thin),
            new { position = blocker.Position.ToString(), category = blocker.def.category.ToString(),
                things = Upper.GetThingList(map).Select(t => t.def.defName).ToArray() });
        blocker.Destroy();
        var occupant = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, canGeneratePawnRelations: false));
        GenSpawn.Spawn(occupant, Upper, map);
        c.Check("native work giver defers removal with a pawn above", !work.HasJobOnCell(builder, Thin));
        occupant.DeSpawn();
        Find.WorldPawns.PassToWorld(occupant, PawnDiscardDecideMode.KeepForever);
        c.Check("roof job becomes available when the upper tile clears", work.HasJobOnCell(builder, Thin));

        if (c.Parameter("capture") != "before")
        {
            builder.jobs.StartJob(work.JobOnCell(builder, Thin), JobCondition.InterruptForced);
            using (var blockedJob = new JobObservation(builder, c))
            {
                blocker = SpawnStool(c);
                c.Check("roof job rechecks support at completion",
                    blockedJob.Wait(500, 30, () => false) == JobOutcome.Failed &&
                    blockedJob.Condition == JobCondition.Incompletable &&
                    map.roofGrid.RoofAt(Thin) == RoofDefOf.RoofRockThin);
                blocker.Destroy();
            }
            builder.jobs.StartJob(work.JobOnCell(builder, Thin), JobCondition.InterruptForced);
            using var job = new JobObservation(builder, c);
            c.Check("native remove-roof job completes", job.Wait(500, 30, () => map.roofGrid.RoofAt(Thin) == null) ==
                JobOutcome.Succeeded);
            c.Check("removal opens upper tile and strips its finish", grid.TerrainAt(Upper) == openAir &&
                grid.UnderTerrainAt(Upper) == null);
            c.Check("open upper tile is impassable", !Upper.Standable(map));
            c.Check("opening the rooftop recovers floor materials",
                GenRadial.RadialCellsAround(Upper, 3f, true).Any(cell =>
                    cell.GetThingList(map).Any(thing => thing.def == ThingDefOf.WoodLog)));
        }

        var legacy = Cell(1) + new IntVec3(0, 0, 64);
        grid.SetTerrain(legacy, mountain);
        map.roofGrid.SetRoof(Cell(1), null);
        c.Check("ambiguous older mountain terrain survives synchronization", grid.TerrainAt(legacy) == mountain);
        c.Check("overhead mountain remains present", map.roofGrid.RoofAt(Cell(3)) == RoofDefOf.RoofRockThick);
        c.Check("constructed rooftop remains present", grid.TerrainAt(Cell(5) + new IntVec3(0, 0, 64)) == rooftop);
        CheckOtherSupports(c, builder);
        if (CaptureLanding(c))
        {
            CheckLandings(c, builder);
            c.Map.areaManager.NoRoof[Thin] = false;
        }
    }

    private static void CheckOtherSupports(Context c, Pawn builder)
    {
        var map = c.Map;
        var grid = map.terrainGrid;
        var rooftop = c.Def<TerrainDef>("AB_RoofSurface");
        var wallTop = c.Def<TerrainDef>("AB_WallTop");
        var repairRoof = Thin + IntVec3.North;
        var repairUpper = repairRoof + LevelOffset;
        grid.SetTerrain(repairUpper, wallTop);
        c.OptionalType("AsAboveSoBelow.ABSkySync").GetMethod("RepairAfterLoad")
            .Invoke(null, new[] { (object)map, RoofFixture.Bands(c) });
        c.Check("legacy wall-top repair uses a walkable rooftop over thin rock",
            map.roofGrid.RoofAt(repairRoof) == RoofDefOf.RoofRockThin && grid.TerrainAt(repairUpper) == rooftop);

        var support = Thin + IntVec3.East;
        var supportedUpper = support + LevelOffset;
        var finish = c.Def<TerrainDef>("WoodPlankFloor");
        c.Check("fixture has a lower edifice and a thin-roof surface",
            support.GetEdifice(map) != null && grid.TerrainAt(supportedUpper) == rooftop);
        grid.SetTerrain(supportedUpper, finish);
        map.roofGrid.SetRoof(support, null);
        c.Check("wall top retains the finish after roof removal",
            grid.TerrainAt(supportedUpper) == finish && grid.UnderTerrainAt(supportedUpper) == wallTop);

        var constructed = Cell(5);
        var upper = constructed + LevelOffset;
        var work = new WorkGiver_RemoveRoof();
        map.areaManager.NoRoof[constructed] = true;
        map.roofGrid.SetRoof(upper, RoofDefOf.RoofConstructed);
        c.Check("separate upper roof blocks constructed-roof removal too", !work.HasJobOnCell(builder, constructed));
        map.roofGrid.SetRoof(upper, null);
        c.Check("constructed-roof job resumes after clearing the upper roof", work.HasJobOnCell(builder, constructed));
        map.areaManager.NoRoof[constructed] = false;
    }

    private static bool CaptureLanding(Context c) =>
        c.Parameter("capture") == "landing-ladder" || c.Parameter("capture") == "landing-stairs";

    private static IntVec3 ConnectorCell(int index) => new IntVec3(21 + index * 21, 0, 112);
    private static IntVec3 LevelOffset => new IntVec3(0, 0, 64);
    private static CellRect ConnectorFootprint(Context c, int index, IntVec3 position) =>
        GenAdj.OccupiedRect(position, new Rot4(index % 4),
            c.Def<ThingDef>(index < 4 ? "AB2_LadderUp" : "AB2_StairsUp").size);

    private static void CheckLandings(Context c, Pawn traveler)
    {
        var map = c.Map;
        var roofSurface = c.Def<TerrainDef>("AB_RoofSurface");
        var openAir = c.Def<TerrainDef>("AB_OpenAir");
        var gravel = c.Def<TerrainDef>("Gravel");
        var granite = c.Def<ThingDef>("Granite");
        for (int index = 0; index < 8; index++)
        {
            var anchor = ConnectorCell(index);
            var upperAnchor = anchor + LevelOffset;
            var rotation = new Rot4(index % 4);
            var def = c.Def<ThingDef>(index < 4 ? "AB2_LadderUp" : "AB2_StairsUp");
            var footprint = ConnectorFootprint(c, index, upperAnchor);
            var landing = footprint.ExpandedBy(1);
            var probe = footprint.ExpandedBy(2);
            PrepareLandingSite(map, probe, gravel, openAir);
            PlaceConnector(def, anchor, map, rotation);
            var far = upperAnchor.GetFirstThing<Building_Door>(map);
            c.Check($"native paired connector {def.defName} rotation {rotation.AsInt}",
                far != null && far.Rotation == rotation && far.OccupiedRect().Equals(footprint));
            c.Check($"exact one-tile platform and support roofs {def.defName} rotation {rotation.AsInt}",
                probe.All(cell => landing.Contains(cell)
                    ? map.roofGrid.RoofAt(cell - LevelOffset) == RoofDefOf.RoofConstructed &&
                      map.terrainGrid.TerrainAt(cell) == roofSurface
                    : map.roofGrid.RoofAt(cell - LevelOffset) == null &&
                      map.terrainGrid.TerrainAt(cell) == openAir),
                new { surfaces = probe.Where(cell => map.terrainGrid.TerrainAt(cell) == roofSurface)
                    .Select(cell => cell.ToString()).ToArray(), roofs = probe
                    .Where(cell => map.roofGrid.RoofAt(cell - LevelOffset) != null)
                    .Select(cell => cell.ToString()).ToArray() });

            // A rocky landing starts on natural ground, separate from the open-air platform.
            var rockAnchor = anchor - new IntVec3(0, 0, 42);
            var rockFootprint = ConnectorFootprint(c, index, rockAnchor + LevelOffset);
            var rockLanding = rockFootprint.ExpandedBy(1);
            var rockProbe = rockFootprint.ExpandedBy(2);
            PrepareLandingSite(map, rockProbe, gravel, gravel);
            foreach (var upper in rockProbe)
            {
                GenSpawn.Spawn(granite, upper, map);
            }
            c.Check($"fixture rock is present throughout second ring {index}",
                rockProbe.All(cell => cell.GetEdifice(map)?.def == granite));
            PlaceConnector(def, rockAnchor, map, rotation);
            c.Check($"exact one-tile rock excavation {def.defName} rotation {rotation.AsInt}",
                rockProbe.All(cell => rockLanding.Contains(cell) == (cell.GetEdifice(map)?.def != granite)),
                new { remainingRock = rockProbe.Where(cell => cell.GetEdifice(map)?.def == granite)
                    .Select(cell => cell.ToString()).ToArray(), landing = rockLanding.ToString() });
        }
        map.roofCollapseBuffer.Clear();
        map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

        // Exercise both connector types through the native Goto path, transit and continuation.
        // The geometry assertions above cover all rotations independently of the crossing path.
        foreach (int index in new[] { 0, 5 })
        {
            var anchor = ConnectorCell(index);
            var source = ConnectorFootprint(c, index, anchor);
            var upper = ConnectorFootprint(c, index, anchor + LevelOffset);
            var origin = source.ExpandedBy(1).First(cell => !source.Contains(cell) && cell.Standable(map));
            var destination = upper.ExpandedBy(1).First(cell => !upper.Contains(cell) && cell.Standable(map));
            traveler.Position = origin;
            traveler.Notify_Teleported(false, true);
            MoveThroughLink(c, traveler, destination, $"connector {index} departure and upper arrival");
            MoveThroughLink(c, traveler, origin, $"connector {index} upper departure and return");
        }
        CheckOccupiedExit(c, traveler);

        // A trimmed apron stays open. A legacy rooftop on the second ring keeps its terrain.
        var trimmed = TrimmedApronProbe(c);
        map.roofGrid.SetRoof(trimmed - LevelOffset, null);
        c.Check("first-ring apron can be trimmed", map.terrainGrid.TerrainAt(trimmed) == openAir);
        var outer = LandingBackfillProbe(c);
        map.terrainGrid.SetTerrain(outer, roofSurface);
        c.Check("excluded second ring remains unroofed before checkpoint",
            map.roofGrid.RoofAt(outer - LevelOffset) == null);
    }

    private static void CheckOccupiedExit(Context c, Pawn traveler)
    {
        var map = c.Map;
        var anchor = ConnectorCell(0);
        var far = (anchor + LevelOffset).GetFirstThing<Building_Door>(map);
        var getApproach = c.OptionalType("AsAboveSoBelow.ABLinkApproach").GetMethod("TryGet");
        object[] args = { far, null };
        if (!(bool)getApproach.Invoke(null, args))
        {
            c.Check("ladder has an upper exit for the occupancy check", false);
            return;
        }

        var outside = (IntVec3)args[1].GetType().GetField("outside").GetValue(args[1]);
        var footprint = far.OccupiedRect();
        var destination = footprint.ExpandedBy(1).First(cell =>
            cell != outside && !footprint.Contains(cell) && cell.Standable(map));
        var occupant = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, canGeneratePawnRelations: false));
        GenSpawn.Spawn(occupant, outside, map);
        occupant.drafter.Drafted = true;
        traveler.Position = anchor + IntVec3.South;
        traveler.Notify_Teleported(false, true);
        MoveThroughLink(c, traveler, destination, "ladder crossing can continue around an occupied upper exit");
        c.Check("upper exit stayed occupied during transit", occupant.Position == outside);
        occupant.DeSpawn();
        Find.WorldPawns.PassToWorld(occupant, PawnDiscardDecideMode.KeepForever);
    }

    private static void PrepareLandingSite(Map map, CellRect upperCells, TerrainDef lowerTerrain,
        TerrainDef upperTerrain)
    {
        foreach (var upper in upperCells)
        {
            var lower = upper - LevelOffset;
            map.roofGrid.SetRoof(upper, null);
            map.roofGrid.SetRoof(lower, null);
            foreach (var thing in upper.GetThingList(map).Concat(lower.GetThingList(map)).Distinct().ToList())
            {
                if (thing.def.destroyable)
                {
                    thing.Destroy();
                }
                else
                {
                    thing.DeSpawn();
                }
            }
            map.terrainGrid.SetTerrain(lower, lowerTerrain);
            map.terrainGrid.SetTerrain(upper, upperTerrain);
            map.fogGrid.Unfog(lower);
            map.fogGrid.Unfog(upper);
        }
    }

    private static void PlaceConnector(ThingDef def, IntVec3 cell, Map map, Rot4 rotation)
    {
        var connector = ThingMaker.MakeThing(def, ThingDefOf.Steel);
        connector.SetFactionDirect(Faction.OfPlayer);
        GenSpawn.Spawn(connector, cell, map, rotation);
    }

    private static void MoveThroughLink(Context c, Pawn traveler, IntVec3 destination, string phase)
    {
        traveler.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, destination), JobCondition.InterruptForced);
        using var job = new JobObservation(traveler, c);
        c.Check(phase, job.Wait(600, 30, () => traveler.Position == destination) == JobOutcome.Succeeded);
    }

    private static IntVec3 LandingBackfillProbe(Context c)
    {
        var footprint = ConnectorFootprint(c, 0, ConnectorCell(0) + LevelOffset);
        return new IntVec3(footprint.minX - 2, 0, footprint.minZ);
    }

    private static IntVec3 TrimmedApronProbe(Context c)
    {
        var footprint = ConnectorFootprint(c, 0, ConnectorCell(0) + LevelOffset);
        return new IntVec3(footprint.minX - 1, 0, footprint.minZ);
    }

    private static Thing SpawnStool(Context c)
    {
        var stool = ThingMaker.MakeThing(c.Def<ThingDef>("Stool"), ThingDefOf.WoodLog);
        return GenSpawn.Spawn(stool, Upper, c.Map);
    }

    private const string CameraPatchId = "zades.aasb.fork.thinroof.tests.camera";
    private static int _cameraFrames;
    private static IntVec3 _cameraCell;

    public override void AfterReload(Context c)
    {
        bool before = c.Parameter("capture") == "before";
        c.Check("roof state persists after reload", c.Map.roofGrid.RoofAt(Thin) ==
            (before ? RoofDefOf.RoofRockThin : null));
        c.Check("upper terrain and floor finish persist after reload",
            c.Map.terrainGrid.TerrainAt(Upper) == c.Def<TerrainDef>(before ? "WoodPlankFloor" : "AB_OpenAir"));
        c.Check("ambiguous legacy mountain terrain survives reload",
            c.Map.terrainGrid.TerrainAt(Cell(1) + LevelOffset) == c.Def<TerrainDef>("AB_MountainTop"));
        var view = c.OptionalType("AsAboveSoBelow.ABBandView");
        view.GetMethod("SetBand", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(null, new object[] { c.Map, 2, false });
        // The runner centers its camera after AfterReload. Reframe on subsequent rendered updates.
        _cameraFrames = 8;
        _cameraCell = Upper;
        if (CaptureLanding(c))
        {
            var map = c.Map;
            var rooftop = c.Def<TerrainDef>("AB_RoofSurface");
            for (int i = 0; i < 8; i++)
            {
                var upper = ConnectorCell(i) + LevelOffset;
                var footprint = ConnectorFootprint(c, i, upper);
                var trimmed = TrimmedApronProbe(c);
                c.Check($"connector and one-tile landing persist after reload {i}",
                    upper.GetFirstThing<Building_Door>(map) != null && footprint.ExpandedBy(1).All(cell =>
                        cell == trimmed
                            ? map.terrainGrid.TerrainAt(cell) == c.Def<TerrainDef>("AB_OpenAir") &&
                              map.roofGrid.RoofAt(cell - LevelOffset) == null
                            : map.terrainGrid.TerrainAt(cell) == rooftop &&
                              map.roofGrid.RoofAt(cell - LevelOffset) == RoofDefOf.RoofConstructed));
            }
            var outside = LandingBackfillProbe(c);
            c.Check("reload backfill does not restore a roof on the second ring",
                map.terrainGrid.TerrainAt(outside) == rooftop &&
                map.roofGrid.RoofAt(outside - LevelOffset) == null);
            _cameraCell = ConnectorCell(c.Parameter("capture") == "landing-ladder" ? 1 : 5) + LevelOffset;
        }
        new Harmony(CameraPatchId).Patch(AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.Update)),
            postfix: new HarmonyMethod(typeof(RoofCase), nameof(FrameRooftop)));
    }

    private static void FrameRooftop(CameraDriver __instance)
    {
        if (_cameraFrames > 0)
        {
            _cameraFrames--;
            __instance.SetRootPosAndSize(new Vector3(_cameraCell.x + 0.5f, 0f, _cameraCell.z + 0.5f), 5f);
        }
    }

    public override void Teardown(Context c) => new Harmony(CameraPatchId).UnpatchAll(CameraPatchId);
}
