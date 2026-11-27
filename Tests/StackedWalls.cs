using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AsAboveSoBelow;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Rwtf;
using UnityEngine;
using Verse;
using Verse.AI.Group;

// Issue #21: a wall stacked directly on a lower-floor wall. Standalone rwtf adapter (needs only Harmony and this
// mod); build it with `rwtf build --adapter Tests/StackedWalls.cs` and run with `--rendered`, because the cases
// read their own screenshots. Host config: fixtureContract mirrors WallFixture.Contract, fixtureMode "scenario",
// progression "native-frames-v1".
//
//   walls.stacked  Several stacked-wall layouts plus snow, fog, a tree, a torch, a campfire, a wind turbine and
//                  smoke/sparks below them. Checks from the screenshot that a stacked wood wall is drawn as wood
//                  and not as the granite wall below it, saves screenshots of every layout for review, and checks
//                  after the runner's save/reload that stacked walls still stand on wall tops and floors laid on
//                  empty wall tops are still there.
//   walls.pointer  Fakes the mouse over a stacked wall and over an empty wall top, then runs the game's own
//                  hover cell and click selection: the stacked wall must be what is pointed at, while the empty wall
//                  top still points through to the floor below.
//
// Arena (same as the ramp tests): floor n = rows 64n..64n+63; cleared x 10..117, z 10..52 in each band. Floor 0 is
// roofed concrete with steel columns at 14+8k; floor 1 is AB_RoofSurface.
public class WallFixture : FixtureProvider
{
    protected virtual int Floors => 2;

    protected virtual string ContractId => "aasb-stacked-walls";

    public override FixtureContract Contract => new FixtureContract
    {
        Id = ContractId,
        Version = "1",
        Intent = Floors + "-floor AASB arena for stacked wall rendering",
        HorizonTicks = 16000,
        Capabilities = new[] { "generated-map", "environment", "save-reload" },
        Interventions = new[]
        {
            "manual-two-band-layout", "normal-aasb-band-finalization-after-layout", "storyteller-disabled",
            "random-diseases-disabled", "random-mental-states-disabled", "fixed-weather-temperature",
            "ambient-wildlife-disabled", "setup-pawn-world-retirement", "unused-generated-map-retirement",
            "roof-sync-suspended-during-layout", "camera-moves-for-screenshots",
        },
        Invariants = new Dictionary<string, double>
        {
            { "mapWidth", 64 * Floors }, { "bands", Floors }, { "outdoorC", 21 },
        },
    };

    public override void Setup(Context c)
    {
        var modType = AccessTools.TypeByName("AsAboveSoBelow.ABMod");
        var settings = AccessTools.Property(modType, "Settings")?.GetValue(null)
            ?? AccessTools.Field(modType, "Settings").GetValue(null);
        var multiLevel = AccessTools.Field(settings.GetType(), "multiLevel");
        var old = multiLevel.GetValue(settings);
        multiLevel.SetValue(settings, false);
        try
        {
            new MapRecipe { Size = 64 * Floors, Biome = "Desert", Weather = "Clear", Temperature = 21 }.Generate(c);
        }
        finally
        {
            multiLevel.SetValue(settings, old);
        }

        foreach (var extra in Find.Maps.Where(m => m != c.Map).ToList())
        {
            foreach (var pawn in extra.mapPawns.AllPawnsSpawned.ToList())
            {
                Retire(pawn);
            }

            Current.Game.DeinitAndRemoveMap(extra, false);
        }

        c.ConfigureEnvironment(new EnvironmentPolicy
        {
            Weather = "Clear", OutdoorC = 21, Storyteller = false, RandomDiseases = false,
            RandomMentalStates = false, SuppressWildlife = true,
        });
        foreach (var pawn in c.Map.mapPawns.AllPawnsSpawned.ToList())
        {
            Retire(pawn);
        }

        var bands = ABBands.CompOf(c.Map);
        AccessTools.Method(typeof(ABBandMap), "Setup").Invoke(bands, new object[] { Floors, 62, 0 });
        AccessTools.Method(AccessTools.TypeByName("AsAboveSoBelow.ABBandedGeneration"), "Carve").Invoke(null,
            new object[] { c.Map, bands });
        AccessTools.Method(typeof(ABBandMap), "FinalizeInit").Invoke(bands, null);
        var sync = AccessTools.Field(typeof(ABSkySync), "Suspended");
        var prior = sync.GetValue(null);
        sync.SetValue(null, true);
        try
        {
            foreach (var cell in c.Map.AllCells.Where(x => x.x >= 10 && x.x <= 117 && x.z % 64 >= 10
                && x.z % 64 <= 52).ToList())
            {
                foreach (var thing in cell.GetThingList(c.Map).ToList())
                {
                    if (thing is Pawn)
                    {
                        continue;
                    }

                    if (thing.def.destroyable)
                    {
                        thing.Destroy();
                    }
                    else
                    {
                        thing.DeSpawn();
                    }
                }

                // Every floor but the top one is roofed: AASB keeps an upper floor's surface only above a roof.
                c.Map.roofGrid.SetRoof(cell, cell.z < 64 * (Floors - 1) ? RoofDefOf.RoofConstructed : null);
                c.Map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed(cell.z < 64
                    ? "Concrete" : "AB_RoofSurface"));
                c.Map.fogGrid.Unfog(cell);
            }
        }
        finally
        {
            sync.SetValue(null, prior);
        }

        for (var x = 14; x < 118; x += 8)
        {
            for (var z = 14; z < 53; z += 8)
            {
                var column = GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Column"),
                    ThingDefOf.Steel), new IntVec3(x, 0, z), c.Map);
                column.SetFaction(Faction.OfPlayer);
            }
        }

        c.Map.roofCollapseBuffer.Clear();
        c.Map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
    }

    public override void Validate(Context c, string stage)
    {
        var pass = c.Map.Size.x == 64 * Floors && ABBands.BandCount(c.Map) == Floors
            && Math.Abs(c.Map.mapTemperature.OutdoorTemp - 21) < .01;
        c.Check("fixture " + stage, pass);
        c.Event("fixture-validation", new { stage, supported = true, passed = pass, invariants = Contract.Invariants });
    }

    private static void Retire(Pawn pawn)
    {
        pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
        pawn.ownership?.UnclaimAll();
        pawn.jobs?.StopAll();
        pawn.DeSpawn();
        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
    }
}

/// <summary>Three floors (192 x 192 map); floors 0 and 1 are roofed. Needs its own host config, because the fixture
/// contract differs.</summary>
public sealed class ThreeFloorWallFixture : WallFixture
{
    protected override int Floors => 3;

    protected override string ContractId => "aasb-stacked-walls-3f";
}

/// <summary>Shared frame loop: each step is an action; a step may request a screenshot, which is captured after a
/// short settle and then waited on until the file exists.</summary>
public abstract class WallCase : Rwtf.Scenario
{
    protected const int Up = 64;

    private readonly List<Action> _steps = new List<Action>();
    private int _step;
    private int _wait;
    private string _pendingShot;
    private string _writing;
    private string _writingName;
    private int _writeFrames;
    private bool _priorPause;

    protected Context Ctx { get; private set; }

    protected List<string> Shots { get; } = new List<string>();

    protected List<string> MissingShots { get; } = new List<string>();

    private readonly Dictionary<string, Vector3> _marks = new Dictionary<string, Vector3>();
    private readonly List<(string label, string shot, string warm, string reference, float minGap)> _colourChecks =
        new List<(string, string, string, string, float)>();
    private readonly List<(string label, string shot, string warm, float minRatio)> _warmChecks =
        new List<(string, string, string, float)>();
    private readonly Dictionary<string, string> _colourResults = new Dictionary<string, string>();

    /// <summary>Remember where a world point lands on screen for the next screenshot.</summary>
    protected void Mark(string name, Vector3 world) => _marks[name] = Find.Camera.WorldToScreenPoint(world);

    /// <summary>After <paramref name="shot"/> is written, require the sample at mark <paramref name="warm"/> to read
    /// as wood: red over blue at least <paramref name="minGap"/> above the sample at <paramref name="reference"/>.
    /// Ratios rather than levels, so the check does not depend on lighting.</summary>
    protected void ExpectWoodAgainst(string label, string shot, string warm, string reference, float minGap = .4f) =>
        _colourChecks.Add((label, shot, warm, reference, minGap));

    /// <summary>Require the sample at mark <paramref name="warm"/> to read as wood on its own (red/blue at least
    /// <paramref name="minRatio"/>; wood is about 2.4 at noon, granite and steel about 1.1).</summary>
    protected void ExpectWood(string label, string shot, string warm, float minRatio = 1.6f) =>
        _warmChecks.Add((label, shot, warm, minRatio));

    private readonly List<(string label, string shot, string mark, float maxRatio)> _coolChecks =
        new List<(string, string, string, float)>();

    /// <summary>Require the sample at <paramref name="mark"/> to read blue-ish (red/blue at most
    /// <paramref name="maxRatio"/>), e.g. a solar panel rather than the grey floor or sand around it.</summary>
    protected void ExpectCool(string label, string shot, string mark, float maxRatio) =>
        _coolChecks.Add((label, shot, mark, maxRatio));

    protected void ReadColours(string shot)
    {
        var path = ShotPath(shot);
        if (!File.Exists(path))
        {
            return;
        }

        var image = new Texture2D(2, 2);
        try
        {
            if (!image.LoadImage(File.ReadAllBytes(path)))
            {
                return;
            }

            float Ratio(string mark)
            {
                var col = Sample(image, _marks[mark], 2);
                return col.r / Mathf.Max(col.b, .01f);
            }

            foreach (var check in _colourChecks.Where(x => x.shot == shot))
            {
                var warm = Ratio(check.warm);
                var reference = Ratio(check.reference);
                _colourResults[check.label] = warm - reference > check.minGap
                    ? "ok" : $"{check.warm} {warm:F2} vs {check.reference} {reference:F2}";
                Ctx.Event("colours", new { check.label, warm, reference });
            }

            foreach (var check in _coolChecks.Where(x => x.shot == shot))
            {
                var ratio = Ratio(check.mark);
                _colourResults[check.label] = ratio <= check.maxRatio ? "ok" : $"{check.mark} {ratio:F2}";
                Ctx.Event("colours", new { check.label, ratio });
            }

            foreach (var check in _warmChecks.Where(x => x.shot == shot))
            {
                var warm = Ratio(check.warm);
                _colourResults[check.label] = warm >= check.minRatio ? "ok" : $"{check.warm} {warm:F2}";
                Ctx.Event("colours", new { check.label, warm });
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(image);
        }
    }

    protected void CheckColours(Context c)
    {
        foreach (var label in _colourChecks.Select(x => x.label).Concat(_warmChecks.Select(x => x.label))
                     .Concat(_coolChecks.Select(x => x.label)))
        {
            c.Check(label, _colourResults.TryGetValue(label, out var r) && r == "ok",
                new { result = _colourResults.TryGetValue(label, out var v) ? v : "screenshot missing" });
        }
    }

    /// <summary>Noon, so colour checks read lit walls whatever the generated start hour was.</summary>
    protected static void SetNoon(Map map) => Find.TickManager.gameStartAbsTick +=
        ((12 - GenLocalDate.HourOfDay(map) + 24) % 24) * GenDate.TicksPerHour;

    public override string EvidenceLevel => "assigned-job";

    public override string ProgressionMode => "native-frames-v1";

    public override string[] SupportedGameVersions => new[] { "1.7" };

    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };

    public override FixtureProvider Fixture => new WallFixture();

    public override void Setup(Context c)
    {
        Ctx = c;
        _priorPause = Prefs.PauseOnLoad;
        Prefs.PauseOnLoad = false;
        Arrange(c);
    }

    protected abstract void Arrange(Context c);

    protected void Step(Action action) => _steps.Add(action);

    protected void View(IntVec3 at, float size = 9f)
    {
        ABBandView.SetBand(Ctx.Map, ABBands.BandOf(Ctx.Map, at), false);
        Find.CameraDriver.SetRootPosAndSize(new Vector3(at.x + .5f, 0f, at.z + .5f), size);
        _wait = 20;
    }

    protected void Shot(string name)
    {
        _pendingShot = name;
        _wait = 15;
    }

    protected string ShotPath(string name) => Path.Combine(Ctx.Output, name + ".png");

    protected static IntVec3 Above(IntVec3 c) => new IntVec3(c.x, 0, c.z + Up);

    protected static void Build(Map map, ThingDef def, IntVec3 cell, ThingDef stuff, Rot4? rot = null)
    {
        foreach (var t in cell.GetThingList(map).Where(t => t.def.category == ThingCategory.Building).ToList())
        {
            t.Destroy();
        }

        var thing = ThingMaker.MakeThing(def, stuff);
        thing.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(thing, cell, map, rot ?? Rot4.North);
    }

    protected static void Wall(Map map, IntVec3 cell, ThingDef stuff) => Build(map, ThingDefOf.Wall, cell, stuff);

    /// <summary>Unroof and clear every building in a lower-floor rect (columns included).</summary>
    protected static void OpenGround(Map map, CellRect rect)
    {
        foreach (var cell in rect.Cells)
        {
            foreach (var t in cell.GetThingList(map).Where(t => t.def.category == ThingCategory.Building).ToList())
            {
                t.Destroy();
            }

            map.roofGrid.SetRoof(cell, null);
        }
    }

    protected virtual void EachFrame(Context c)
    {
    }

    public override bool AdvanceNative(Context c)
    {
        EachFrame(c);
        if (_writing != null)
        {
            // ScreenCapture writes at the end of a later frame; wait for the file before moving on.
            if (File.Exists(_writing))
            {
                Shots.Add(_writingName);
                _writing = null;
                _wait = 5;
            }
            else if (++_writeFrames > 120)
            {
                MissingShots.Add(_writingName);
                _writing = null;
            }

            return false;
        }

        if (_pendingShot != null)
        {
            if (--_wait <= 0)
            {
                var path = ShotPath(_pendingShot);
                ScreenCapture.CaptureScreenshot(path);
                c.Event("screenshot", new { path, tick = c.Tick });
                _writingName = _pendingShot;
                _writing = path;
                _writeFrames = 0;
                _pendingShot = null;
            }

            return false;
        }

        if (_wait > 0)
        {
            _wait--;
            return false;
        }

        if (_step >= _steps.Count)
        {
            return true;
        }

        _steps[_step++]();
        return false;
    }

    public override void Assert(Context c)
    {
        c.Check("all steps ran", _step >= _steps.Count, new { step = _step, of = _steps.Count });
        c.Check("every screenshot was written", MissingShots.Count == 0, new { missing = MissingShots });
        CheckColours(c);
        Verify(c);
    }

    protected virtual void Verify(Context c)
    {
    }

    public override void Teardown(Context c)
    {
        Prefs.PauseOnLoad = _priorPause;
    }

    /// <summary>Average colour of a small square of screenshot pixels around a cell's centre.</summary>
    protected static Color Sample(Texture2D image, Vector3 screen, int half = 4)
    {
        var sum = Color.clear;
        var n = 0;
        for (var dx = -half; dx <= half; dx++)
        {
            for (var dy = -half; dy <= half; dy++)
            {
                sum += image.GetPixel((int)screen.x + dx, (int)screen.y + dy);
                n++;
            }
        }

        return sum / n;
    }
}

public sealed class StackedWalls : WallCase
{
    private bool _effects;
    private readonly List<IntVec3> _stacked = new List<IntVec3>();
    private readonly List<IntVec3> _floored = new List<IntVec3>();
    private static readonly IntVec3 Legacy = new IntVec3(108, 0, 25 + Up);
    private string _legacyBeforeSave;
    private static readonly IntVec3 StairCell = new IntVec3(42, 0, 42 + Up);
    private Building_ABStairs2 _stairs;

    public override string Id => "walls.stacked";

    public override string Intent =>
        "Issue #21: a wall stacked on a lower-floor wall is drawn on top; snow, fog and effects below stay below";

    // A x 23..29 z 25 (+T up to z 28): unroofed lower granite run, granite stacked on all of it.
    // D x 71..77 z 23..29: upper steel ring; only its west half (x <= 74) stands on lower walls, the east half
    //   on roofed concrete. The ring must read as one connected ring.
    // E x 87..93 z 25: lower granite run; upper WOOD walls only on x 87..90. The stacked cells must look like wood.
    // K x 96..97 z 24..25: lower 2x2 granite block; one wood wall stacked on its corner (97,25). The block's corner
    //   filler (drawn one AltInc above the wall face) must stay under the wood wall.
    // J x 103..109 z 25: lower steel run; wood floor on the empty wall tops x 103..106, steel stacked on x 107..109.
    //   (108,89) is then forced to open air with sync suspended, as a save from before the load fix would hold.
    // G x 20..34 z 33..48 (open ground): lower granite run z 40 x 22..32, wood stacked on x 22..28; deep snow below,
    //   an oak at (27,39) and a torch at (31,41).
    // H x 50..64 z 33..48 (open ground): lower steel run z 40 x 52..62, wood stacked on all; lower cells north of
    //   the wall fogged, and the lower wall cells themselves fogged under x 52..56.
    // L (42,106): a staircase down on the upper floor with its lower counterpart unroofed, its upper footprint
    //   forced to open air as an old broken save might hold it. Load must give it walkable rooftop back, not an
    //   impassable wall top.
    // I x 70..84 z 33..48 (open ground): lower granite run z 40 x 72..82, wood stacked on all; a lit campfire at
    //   (74,39), a wind turbine at (77,37), and smoke, sparks and fire glow thrown every frame at (78,41).
    protected override void Arrange(Context c)
    {
        var map = c.Map;
        SetNoon(map);
        var granite = DefDatabase<ThingDef>.GetNamed("BlocksGranite");
        var steel = ThingDefOf.Steel;
        var wood = ThingDefOf.WoodLog;

        var runA = Enumerable.Range(23, 7).Select(x => new IntVec3(x, 0, 25))
            .Concat(Enumerable.Range(26, 3).Select(z => new IntVec3(26, 0, z))).ToList();
        foreach (var cell in runA)
        {
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, granite);
            Wall(map, Above(cell), granite);
        }

        _stacked.AddRange(runA.Select(Above));

        var ringD = CellRect.FromLimits(71, 23, 77, 29).EdgeCells.ToList();
        foreach (var cell in ringD.Where(x => x.x <= 74))
        {
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, steel);
        }

        foreach (var cell in ringD)
        {
            Wall(map, Above(cell), steel);
        }

        for (var x = 87; x <= 93; x++)
        {
            var cell = new IntVec3(x, 0, 25);
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, granite);
            if (x <= 90)
            {
                Wall(map, Above(cell), wood);
                _stacked.Add(Above(cell));
            }
        }

        foreach (var cell in CellRect.FromLimits(96, 24, 97, 25).Cells)
        {
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, granite);
        }

        Wall(map, new IntVec3(97, 0, 25 + Up), wood);
        _stacked.Add(new IntVec3(97, 0, 25 + Up));

        var plank = DefDatabase<TerrainDef>.GetNamed("WoodPlankFloor");
        for (var x = 103; x <= 109; x++)
        {
            var cell = new IntVec3(x, 0, 25);
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, steel);
            if (x <= 106)
            {
                map.terrainGrid.SetTerrain(Above(cell), plank);
                _floored.Add(Above(cell));
            }
            else
            {
                Wall(map, Above(cell), steel);
                _stacked.Add(Above(cell));
            }
        }

        c.Check("floors laid on empty wall tops keep the wall top underneath", _floored.All(cell =>
            map.terrainGrid.TerrainAt(cell) == plank && map.terrainGrid.UnderTerrainAt(cell) == ABDefOf.AB_WallTop));

        OpenGround(map, CellRect.FromLimits(20, 33, 34, 48));
        for (var x = 22; x <= 32; x++)
        {
            Wall(map, new IntVec3(x, 0, 40), granite);
            if (x <= 28)
            {
                Wall(map, new IntVec3(x, 0, 40 + Up), wood);
            }
        }

        foreach (var cell in CellRect.FromLimits(20, 33, 34, 48).Cells)
        {
            map.snowGrid.SetDepth(cell, 1f);
        }

        var tree = (Plant)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Plant_TreeOak"));
        tree.Growth = 1f;
        GenSpawn.Spawn(tree, new IntVec3(27, 0, 39), map);
        Build(map, DefDatabase<ThingDef>.GetNamed("TorchLamp"), new IntVec3(31, 0, 41), null);

        OpenGround(map, CellRect.FromLimits(50, 33, 64, 48));
        for (var x = 52; x <= 62; x++)
        {
            Wall(map, new IntVec3(x, 0, 40), steel);
            Wall(map, new IntVec3(x, 0, 40 + Up), wood);
        }

        map.fogGrid.Refog(CellRect.FromLimits(50, 41, 64, 48));
        // And the lower wall cells themselves under x 52..56: fog directly beneath a stacked wall.
        map.fogGrid.Refog(CellRect.FromLimits(52, 40, 56, 40));

        OpenGround(map, CellRect.FromLimits(70, 33, 84, 48));
        for (var x = 72; x <= 82; x++)
        {
            Wall(map, new IntVec3(x, 0, 40), granite);
            Wall(map, new IntVec3(x, 0, 40 + Up), wood);
        }

        Build(map, DefDatabase<ThingDef>.GetNamed("Campfire"), new IntVec3(74, 0, 39), null);
        new IntVec3(74, 0, 39).GetFirstThing(map, DefDatabase<ThingDef>.GetNamed("Campfire"))
            ?.TryGetComp<CompRefuelable>()?.Refuel(100f);
        Build(map, DefDatabase<ThingDef>.GetNamed("WindTurbine"), new IntVec3(77, 0, 37), null);
        _effects = true;

        c.Check("stacked cells hold their own wall on a wall top", _stacked.All(cell =>
            cell.GetEdifice(map)?.def == ThingDefOf.Wall && map.terrainGrid.TerrainAt(cell) == ABDefOf.AB_WallTop
            && ABBands.CoveredAt(map, cell)), new { cells = _stacked.Count });

        // M: a 4x4 solar generator on open ground (x 102..105, z 40..43) whose first cell row-major is fogged.
        OpenGround(map, CellRect.FromLimits(98, 36, 107, 45));
        Build(map, DefDatabase<ThingDef>.GetNamed("SolarGenerator"), new IntVec3(103, 0, 41), null);
        var solarM = new IntVec3(103, 0, 41).GetFirstBuilding(map);
        c.Check("solar generator M covers x 102..105, z 40..43",
            solarM != null && solarM.OccupiedRect() == CellRect.FromLimits(102, 40, 105, 43),
            new { rect = solarM?.OccupiedRect().ToString() });
        map.fogGrid.Refog(CellRect.SingleCell(new IntVec3(102, 0, 40)));

        var down = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("AB2_StairsDown"), ThingDefOf.WoodLog);
        down.SetFaction(Faction.OfPlayer);
        _stairs = (Building_ABStairs2)GenSpawn.Spawn(down, StairCell, map, Rot4.North);
        var lowerLink = _stairs.Counterparts.FirstOrDefault(x => x != null && x.Spawned);
        c.Check("staircase pair spawned", lowerLink != null, new { stairs = _stairs.Position.ToString() });
        if (lowerLink != null)
        {
            foreach (var cell in lowerLink.OccupiedRect())
            {
                map.roofGrid.SetRoof(cell, null);
            }
        }

        // Last: a stacked wall on open air, as saved by builds before the load fix. Written with sync suspended so
        // nothing repairs it before the runner's save; AfterReload expects the wall top back.
        var sync = AccessTools.Field(typeof(ABSkySync), "Suspended");
        sync.SetValue(null, true);
        try
        {
            map.terrainGrid.SetTerrain(Legacy, ABDefOf.AB_OpenAir);
            foreach (var cell in _stairs.OccupiedRect())
            {
                map.terrainGrid.SetTerrain(cell, ABDefOf.AB_OpenAir);
            }
        }
        finally
        {
            sync.SetValue(null, false);
        }

        _stacked.Remove(Legacy);
        c.Check("legacy stacked wall set up on open air", map.terrainGrid.TerrainAt(Legacy) == ABDefOf.AB_OpenAir
            && Legacy.GetEdifice(map)?.def == ThingDefOf.Wall);

        Step(() => View(new IntVec3(92, 0, 25 + Up), 6f));
        Step(() =>
        {
            Mark("e-wood", new IntVec3(88, 0, 25 + Up).ToVector3Shifted());
            Mark("e-granite", new IntVec3(92, 0, 25 + Up).ToVector3Shifted());
            Mark("k-corner", new Vector3(97.18f, 0f, 25.18f + Up));
            Mark("k-granite", new IntVec3(96, 0, 24 + Up).ToVector3Shifted());
            Shot("e-k-upper");
        });
        Step(() => ReadColours("e-k-upper"));
        Step(() => View(new IntVec3(92, 0, 25), 6f));
        Step(() => Shot("e-k-lower"));
        Step(() => View(new IntVec3(26, 0, 26 + Up)));
        Step(() => Shot("a-upper"));
        Step(() => View(new IntVec3(74, 0, 26 + Up)));
        Step(() => Shot("d-upper"));
        Step(() => View(new IntVec3(27, 0, 40 + Up), 6f));
        Step(() =>
        {
            Mark("g-wood", new IntVec3(24, 0, 40 + Up).ToVector3Shifted());
            Mark("g-granite", new IntVec3(31, 0, 40 + Up).ToVector3Shifted());
            Shot("g-upper-snow-tree-torch");
        });
        Step(() => ReadColours("g-upper-snow-tree-torch"));
        Step(() => View(new IntVec3(57, 0, 40 + Up), 6f));
        Step(() =>
        {
            Mark("h-wood", new Vector3(60.5f, 0f, 40.3f + Up));
            Mark("h-wood-fogged-below", new IntVec3(54, 0, 40 + Up).ToVector3Shifted());
            Shot("h-upper-fog");
        });
        Step(() => ReadColours("h-upper-fog"));
        Step(() => View(new IntVec3(102, 0, 40 + Up), 6f));
        Step(() =>
        {
            Mark("m-solar", new Vector3(104.5f, 0f, 42.5f + Up));
            Shot("m-upper-solar-fogged-corner");
        });
        Step(() => ReadColours("m-upper-solar-fogged-corner"));
        Step(() => View(new IntVec3(77, 0, 40 + Up), 6f));
        Step(() => Shot("i-upper-fire-smoke-turbine"));
        Step(() => View(new IntVec3(77, 0, 40), 6f));
        Step(() => Shot("i-lower"));

        ExpectWoodAgainst("stacked wood wall is drawn as wood, not as the granite wall below it", "e-k-upper",
            "e-wood", "e-granite");
        ExpectWoodAgainst("a lower block's corner filler stays under the wall stacked on that corner", "e-k-upper",
            "k-corner", "k-granite");
        ExpectWoodAgainst("snow below does not cover a stacked wall", "g-upper-snow-tree-torch", "g-wood",
            "g-granite");
        ExpectWood("fog below does not cover the body of a stacked wall", "h-upper-fog", "h-wood");
        ExpectWood("fog directly under a stacked wall is not drawn over it", "h-upper-fog", "h-wood-fogged-below");
        ExpectCool("a big building below is drawn when its first cell is fogged", "m-upper-solar-fogged-corner",
            "m-solar", 0.9f);
    }

    protected override void EachFrame(Context c)
    {
        if (!_effects)
        {
            return;
        }

        var at = new Vector3(78.5f, 0f, 41.5f);
        FleckMaker.ThrowSmoke(at, c.Map, 1.5f);
        FleckMaker.ThrowMicroSparks(at, c.Map);
        if (c.Tick % 30 == 0)
        {
            FleckMaker.ThrowFireGlow(at, c.Map, 1f);
        }
    }

    protected override void Verify(Context c)
    {
        c.Check("screenshots taken", Shots.Count == 9, new { shots = Shots });
        _legacyBeforeSave = c.Map.terrainGrid.TerrainAt(Legacy)?.defName;
        c.Event("legacy-before-save", new { terrain = _legacyBeforeSave });
    }

    public override void AfterReload(Context c)
    {
        var map = c.Map;
        var broken = _stacked.Where(cell => !(cell.GetEdifice(map)?.def == ThingDefOf.Wall
                && map.terrainGrid.TerrainAt(cell) == ABDefOf.AB_WallTop && ABBands.CoveredAt(map, cell)))
            .Select(cell => cell + " " + cell.GetEdifice(map)?.def.defName + " " + map.terrainGrid.TerrainAt(cell)?.defName)
            .ToList();
        c.Check("stacked walls survive reload", broken.Count == 0, new { broken });
        var lostFloors = _floored.Where(cell => map.terrainGrid.TerrainAt(cell)?.defName != "WoodPlankFloor"
                || map.terrainGrid.UnderTerrainAt(cell) != ABDefOf.AB_WallTop)
            .Select(cell => cell + " " + map.terrainGrid.TerrainAt(cell)?.defName).ToList();
        c.Check("floors on empty wall tops survive reload", lostFloors.Count == 0, new { lostFloors });
        var stairs = map.listerThings.AllThings.OfType<Building_ABStairs2>()
            .FirstOrDefault(x => x.Position == StairCell);
        var footprint = stairs == null ? new List<string>() : stairs.OccupiedRect().Cells
            .Select(cell => map.terrainGrid.TerrainAt(cell)?.defName).ToList();
        c.Check("a staircase saved on open air gets walkable rooftop back, not a wall top",
            stairs != null && footprint.All(t => t == "AB_RoofSurface"), new { footprint });
        c.Check("a stacked wall saved on open air gets its wall top back on load",
            map.terrainGrid.TerrainAt(Legacy) == ABDefOf.AB_WallTop && Legacy.GetEdifice(map)?.def == ThingDefOf.Wall,
            new { before = _legacyBeforeSave, after = map.terrainGrid.TerrainAt(Legacy)?.defName });
    }
}

public sealed class StackedWallPointer : WallCase
{
    private static Vector2? s_fakeMouse;
    private Harmony _harmony;
    private readonly Dictionary<string, string> _results = new Dictionary<string, string>();

    public override string Id => "walls.pointer";

    public override string Intent => "Issue #21: hovering and clicking a stacked wall points at that wall";

    private static readonly IntVec3 Stacked = new IntVec3(88, 0, 25 + Up);
    private static readonly IntVec3 OpenTop = new IntVec3(92, 0, 25 + Up);

    private static bool MousePrefix(ref Vector2 __result)
    {
        if (s_fakeMouse == null)
        {
            return true;
        }

        __result = s_fakeMouse.Value;
        return false;
    }

    // Lower granite run z 25 x 87..93 (unroofed); wood stacked on x 87..90 only. (92,89) is an empty wall top.
    protected override void Arrange(Context c)
    {
        var map = c.Map;
        _harmony = new Harmony("aasb.test.walls.pointer");
        _harmony.Patch(AccessTools.PropertyGetter(typeof(UI), nameof(UI.MousePositionOnUI)),
            prefix: new HarmonyMethod(typeof(StackedWallPointer), nameof(MousePrefix)));
        for (var x = 87; x <= 93; x++)
        {
            var cell = new IntVec3(x, 0, 25);
            map.roofGrid.SetRoof(cell, null);
            Wall(map, cell, DefDatabase<ThingDef>.GetNamed("BlocksGranite"));
            if (x <= 90)
            {
                Wall(map, Above(cell), ThingDefOf.WoodLog);
            }
        }

        c.Check("click translation skips the stacked wall",
            !ABBelowClickThrough.TryTranslate(map, Stacked.ToVector3Shifted(), out _));
        c.Check("click translation still looks through an empty wall top",
            ABBelowClickThrough.TryTranslate(map, OpenTop.ToVector3Shifted(), out var through)
            && IntVec3.FromVector3(through) == new IntVec3(92, 0, 25));

        Step(() => View(new IntVec3(90, 0, 26 + Up)));
        foreach (var (name, cell) in new[] { ("stacked", Stacked), ("open", OpenTop) })
        {
            Step(() =>
            {
                Point(cell);
                var hovered = UI.MouseCell();
                _results[name + "-hover"] = hovered + " " + hovered.GetEdifice(c.Map)?.LabelCap;
                Shot(name + "-hover");
            });
            Step(() =>
            {
                Point(cell);
                Find.Selector.ClearSelection();
                AccessTools.Method(typeof(Selector), "SelectUnderMouse").Invoke(Find.Selector, null);
                var picked = Find.Selector.SingleSelectedThing;
                _results[name + "-select"] = picked == null ? "nothing" : picked.Position + " " + picked.LabelCap;
                Shot(name + "-select");
            });
            Step(() => Find.Selector.ClearSelection());
        }
    }

    private static void Point(IntVec3 cell)
    {
        s_fakeMouse = null;
        var screen = Find.Camera.WorldToScreenPoint(cell.ToVector3Shifted());
        s_fakeMouse = new Vector2(screen.x, screen.y) / Prefs.UIScale;
    }

    protected override void Verify(Context c)
    {
        c.Event("pointer", _results);
        string Get(string key) => _results.TryGetValue(key, out var v) ? v : "missing";
        c.Check("hovering a stacked wall points at it", Get("stacked-hover").StartsWith(Stacked.ToString()),
            new { got = Get("stacked-hover") });
        c.Check("clicking a stacked wall selects it", Get("stacked-select").StartsWith(Stacked.ToString()),
            new { got = Get("stacked-select") });
        var below = new IntVec3(92, 0, 25).ToString();
        c.Check("hovering an empty wall top still points at the floor below",
            Get("open-hover").StartsWith(below), new { got = Get("open-hover") });
        c.Check("clicking an empty wall top still selects the wall below", Get("open-select").StartsWith(below),
            new { got = Get("open-select") });
        c.Check("screenshots taken", Shots.Count == 4, new { shots = Shots });
    }

    public override void Teardown(Context c)
    {
        s_fakeMouse = null;
        _harmony?.UnpatchAll("aasb.test.walls.pointer");
        base.Teardown(c);
    }
}

/// <summary>
/// Three floors: lower granite run at level 0, wood stacked at level +1 on x 23..26 only, nothing at +2. Seen from
/// +2, the empty wall top over a stacked wood wall must show (and point at) the wood wall, not look through it to
/// the granite; over x 27..29 it still looks through to the granite two levels down. Needs a host config whose
/// fixtureContract mirrors ThreeFloorWallFixture.
/// </summary>
public sealed class StackedWallsThreeFloors : WallCase
{
    private const int Up2 = 128;

    public override string Id => "walls.three-floors";

    public override string Intent => "Issue #21: a stacked wall hides the level under it from two floors up";

    public override FixtureProvider Fixture => new ThreeFloorWallFixture();

    protected override void Arrange(Context c)
    {
        var map = c.Map;
        SetNoon(map);
        var bands = ABBands.CompOf(map);
        for (var x = 23; x <= 29; x++)
        {
            map.roofGrid.SetRoof(new IntVec3(x, 0, 25), null);
            map.roofGrid.SetRoof(new IntVec3(x, 0, 25 + Up), null);
            Wall(map, new IntVec3(x, 0, 25), DefDatabase<ThingDef>.GetNamed("BlocksGranite"));
            if (x <= 26)
            {
                Wall(map, new IntVec3(x, 0, 25 + Up), ThingDefOf.WoodLog);
            }
        }

        var wood = new IntVec3(24, 0, 25 + Up2);
        var open = new IntVec3(28, 0, 25 + Up2);
        c.Check("level +2 above a stacked wall is a wall top; above an empty wall top it is open air",
            map.terrainGrid.TerrainAt(wood) == ABDefOf.AB_WallTop
            && map.terrainGrid.TerrainAt(open) == ABDefOf.AB_OpenAir,
            new { wood = map.terrainGrid.TerrainAt(wood)?.defName, open = map.terrainGrid.TerrainAt(open)?.defName });
        var stopsAtWood = ABBands.TryResolveVisibleFrom(map, bands, wood, true, out var seenWood, out _);
        c.Check("looking down from +2 stops at the stacked wall on +1",
            stopsAtWood && seenWood == new IntVec3(24, 0, 25 + Up), new { seen = seenWood.ToString() });
        var throughOpen = ABBands.TryResolveVisibleFrom(map, bands, open, true, out var seenOpen, out _);
        c.Check("looking down from +2 over an empty wall top still reaches level 0",
            throughOpen && seenOpen == new IntVec3(28, 0, 25), new { seen = seenOpen.ToString() });
        c.Check("a click from +2 over a stacked wall lands on that wall",
            ABBelowClickThrough.TryTranslate(map, wood.ToVector3Shifted(), out var clicked)
            && IntVec3.FromVector3(clicked) == new IntVec3(24, 0, 25 + Up));

        ABBandView.SetBand(map, 2, false);
        var hiddenFromTop = ABBelowOverlays.TryLocalizeToView(map, new IntVec3(24, 0, 25).ToVector3Shifted(), out _);
        var visibleFromTop = ABBelowOverlays.TryLocalizeToView(map, new IntVec3(28, 0, 25).ToVector3Shifted(), out _);
        ABBandView.SetBand(map, 1, false);
        var hiddenFromMiddle =
            ABBelowOverlays.TryLocalizeToView(map, new IntVec3(24, 0, 25).ToVector3Shifted(), out _);
        var visibleFromMiddle =
            ABBelowOverlays.TryLocalizeToView(map, new IntVec3(28, 0, 25).ToVector3Shifted(), out _);
        c.Check("selection brackets of a lower wall hidden under a stacked wall are not drawn on it",
            !hiddenFromTop && !hiddenFromMiddle, new { hiddenFromTop, hiddenFromMiddle });
        c.Check("selection brackets of a visible lower wall are still drawn",
            visibleFromTop && visibleFromMiddle, new { visibleFromTop, visibleFromMiddle });

        // A 4x4 solar generator on level 0 (x 39..42, z 23..26), unroofed except its first cell row-major, (39,23).
        // Level +1 is open above it except (39,87), rooftop over that roofed cell, with a wall on it. Seen from +2,
        // that one column stops at the wall; the generator must still be drawn across the rest of its footprint.
        var solarRect = CellRect.FromLimits(39, 23, 42, 26);
        foreach (var cell in solarRect.Cells)
        {
            map.roofGrid.SetRoof(new IntVec3(cell.x, 0, cell.z + Up), null);
            map.roofGrid.SetRoof(cell, cell == new IntVec3(39, 0, 23) ? RoofDefOf.RoofConstructed : null);
        }

        Build(map, DefDatabase<ThingDef>.GetNamed("SolarGenerator"), new IntVec3(40, 0, 24), null);
        Wall(map, new IntVec3(39, 0, 23 + Up), ThingDefOf.Steel);
        var solar = new IntVec3(40, 0, 24).GetFirstBuilding(map);
        c.Check("solar generator covers x 39..42, z 23..26", solar != null && solar.OccupiedRect() == solarRect,
            new { rect = solar?.OccupiedRect().ToString() });

        // Only roof-holding buildings give a wall top: a 3x4 paste dispenser (impassable, but it holds no roof) on
        // unroofed ground leaves open air above it, so no wall can be built on top of it.
        var dispenserCell = new IntVec3(34, 0, 32);
        foreach (var cell in CellRect.CenteredOn(dispenserCell, 3).Cells)
        {
            map.roofGrid.SetRoof(cell, null);
            map.roofGrid.SetRoof(new IntVec3(cell.x, 0, cell.z + Up), null);
        }

        Build(map, DefDatabase<ThingDef>.GetNamed("NutrientPasteDispenser"), dispenserCell, null);
        var dispenser = dispenserCell.GetFirstBuilding(map);
        var overDispenser = dispenser == null ? new List<string>() : dispenser.OccupiedRect().Cells
            .Select(cell => map.terrainGrid.TerrainAt(new IntVec3(cell.x, 0, cell.z + Up))?.defName).Distinct().ToList();
        c.Check("the top of a machine is open air, not a wall top", dispenser != null
            && overDispenser.Count == 1 && overDispenser[0] == "AB_OpenAir", new { overDispenser });
        c.Check("the top of a wall is still a wall top",
            map.terrainGrid.TerrainAt(new IntVec3(27, 0, 25 + Up)) == ABDefOf.AB_WallTop);

        Step(() => View(new IntVec3(30, 0, 25 + Up2), 6f));
        Step(() =>
        {
            Mark("wood", new IntVec3(24, 0, 25 + Up2).ToVector3Shifted());
            Mark("granite", new IntVec3(28, 0, 25 + Up2).ToVector3Shifted());
            Mark("solar", new Vector3(41.5f, 0f, 25.5f + Up2));
            Shot("top-floor");
        });
        Step(() => ReadColours("top-floor"));
        Step(() => View(new IntVec3(26, 0, 25 + Up), 6f));
        Step(() => Shot("middle-floor"));
        ExpectWoodAgainst("seen from two floors up, the stacked wood wall shows, not the granite under it",
            "top-floor", "wood", "granite");
        ExpectCool("a big building two floors down is still drawn when a wall above hides one of its cells",
            "top-floor", "solar", 0.9f);
    }

    protected override void Verify(Context c)
    {
        c.Check("screenshots taken", Shots.Count == 2, new { shots = Shots });
    }
}
