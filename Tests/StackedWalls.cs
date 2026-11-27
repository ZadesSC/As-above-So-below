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
    public override FixtureContract Contract => new FixtureContract
    {
        Id = "aasb-stacked-walls",
        Version = "1",
        Intent = "2-floor AASB arena for stacked wall rendering",
        HorizonTicks = 16000,
        Capabilities = new[] { "generated-map", "environment", "save-reload" },
        Interventions = new[]
        {
            "manual-two-band-layout", "normal-aasb-band-finalization-after-layout", "storyteller-disabled",
            "random-diseases-disabled", "random-mental-states-disabled", "fixed-weather-temperature",
            "ambient-wildlife-disabled", "setup-pawn-world-retirement", "unused-generated-map-retirement",
            "roof-sync-suspended-during-layout", "camera-moves-for-screenshots",
        },
        Invariants = new Dictionary<string, double> { { "mapWidth", 128 }, { "bands", 2 }, { "outdoorC", 21 } },
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
            new MapRecipe { Size = 128, Biome = "Desert", Weather = "Clear", Temperature = 21 }.Generate(c);
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
        AccessTools.Method(typeof(ABBandMap), "Setup").Invoke(bands, new object[] { 2, 62, 0 });
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

                c.Map.roofGrid.SetRoof(cell, cell.z < 64 ? RoofDefOf.RoofConstructed : null);
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
        var pass = c.Map.Size.x == 128 && ABBands.BandCount(c.Map) == 2
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
    private int _writeFrames;
    private bool _priorPause;

    protected Context Ctx { get; private set; }

    protected List<string> Shots { get; } = new List<string>();

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
            if (File.Exists(_writing) || ++_writeFrames > 120)
            {
                _writing = null;
                _wait = 5;
            }

            return false;
        }

        if (_pendingShot != null)
        {
            if (--_wait <= 0)
            {
                var path = ShotPath(_pendingShot);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(_pendingShot);
                c.Event("screenshot", new { path, tick = c.Tick });
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
    private Vector3 _woodScreen;
    private Vector3 _graniteScreen;
    private string _colourResult;

    public override string Id => "walls.stacked";

    public override string Intent =>
        "Issue #21: a wall stacked on a lower-floor wall is drawn on top; snow, fog and effects below stay below";

    // A x 23..29 z 25 (+T up to z 28): unroofed lower granite run, granite stacked on all of it.
    // D x 71..77 z 23..29: upper steel ring; only its west half (x <= 74) stands on lower walls, the east half
    //   on roofed concrete. The ring must read as one connected ring.
    // E x 87..93 z 25: lower granite run; upper WOOD walls only on x 87..90. The stacked cells must look like wood.
    // G x 20..34 z 33..48 (open ground): lower granite run z 40 x 22..32, wood stacked on x 22..28; deep snow below,
    //   an oak at (27,39) and a torch at (31,41).
    // H x 50..64 z 33..48 (open ground): lower steel run z 40 x 52..62, wood stacked on all; lower cells north of
    //   the wall fogged.
    // I x 70..84 z 33..48 (open ground): lower granite run z 40 x 72..82, wood stacked on all; a lit campfire at
    //   (74,39), a wind turbine at (77,37), and smoke, sparks and fire glow thrown every frame at (78,41).
    protected override void Arrange(Context c)
    {
        var map = c.Map;
        // Noon, so the colour check below reads lit walls whatever the generated start hour was.
        Find.TickManager.gameStartAbsTick +=
            ((12 - GenLocalDate.HourOfDay(map) + 24) % 24) * GenDate.TicksPerHour;
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

        // J x 103..109 z 25: lower steel run; wood floor laid on the empty wall tops x 103..106, steel stacked on
        //   x 107..109. Both must survive a reload unchanged (load used to strip them to open air).
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

        Step(() => View(new IntVec3(90, 0, 25 + Up), 6f));
        Step(() =>
        {
            _woodScreen = Find.Camera.WorldToScreenPoint(new IntVec3(88, 0, 25 + Up).ToVector3Shifted());
            _graniteScreen = Find.Camera.WorldToScreenPoint(new IntVec3(92, 0, 25 + Up).ToVector3Shifted());
            Shot("e-upper");
        });
        Step(() => _colourResult = CompareColours(ShotPath("e-upper")));
        Step(() => View(new IntVec3(90, 0, 25), 6f));
        Step(() => Shot("e-lower"));
        Step(() => View(new IntVec3(26, 0, 26 + Up)));
        Step(() => Shot("a-upper"));
        Step(() => View(new IntVec3(74, 0, 26 + Up)));
        Step(() => Shot("d-upper"));
        Step(() => View(new IntVec3(27, 0, 40 + Up), 6f));
        Step(() => Shot("g-upper-snow-tree-torch"));
        Step(() => View(new IntVec3(57, 0, 40 + Up), 6f));
        Step(() => Shot("h-upper-fog"));
        Step(() => View(new IntVec3(77, 0, 40 + Up), 6f));
        Step(() => Shot("i-upper-fire-smoke-turbine"));
        Step(() => View(new IntVec3(77, 0, 40), 6f));
        Step(() => Shot("i-lower"));
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

    /// <summary>Wood reads warm (red well above blue, about 1.9x); granite reads grey (about 0.9x). A stacked wood wall drawn under the
    /// granite wall below it (the #21 bug) samples as grey.</summary>
    private string CompareColours(string path)
    {
        var image = new Texture2D(2, 2);
        if (!image.LoadImage(File.ReadAllBytes(path)))
        {
            return "unreadable";
        }

        var wood = Sample(image, _woodScreen);
        var granite = Sample(image, _graniteScreen);
        // Red over blue, so the test does not depend on how brightly the scene is lit.
        var woodWarmth = wood.r / Mathf.Max(wood.b, .01f);
        var graniteWarmth = granite.r / Mathf.Max(granite.b, .01f);
        UnityEngine.Object.Destroy(image);
        Ctx.Event("colours", new { wood = wood.ToString(), granite = granite.ToString(), woodWarmth, graniteWarmth });
        return woodWarmth - graniteWarmth > .4f ? "ok" : $"wood {woodWarmth:F3} vs granite {graniteWarmth:F3}";
    }

    protected override void Verify(Context c)
    {
        c.Check("stacked wood wall is drawn as wood, not as the granite wall below it", _colourResult == "ok",
            new { result = _colourResult });
        c.Check("screenshots taken", Shots.Count == 8, new { shots = Shots });
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
