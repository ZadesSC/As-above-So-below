using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AsAboveSoBelow;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class VehicleRampIntegrationFixture : RampFixture, IFixturePersistence
{
    public string StateVersion => "1";

    public List<FixtureStateField> CaptureState(Context c) => new();

    public void RestoreState(Context c, List<FixtureStateField> state)
    {
        c.Check("fixture has no additional state to restore", state.Count == 0);
    }
}

public sealed class VehicleRampIntegrationScenario : Rwtf.Scenario, IFixturePersistence
{
    private const string Aasb = "astryl.AsAboveSoBelow2";
    private const string VehicleFramework = "SmashPhil.VehicleFramework";
    private const string Standalone = "zades.aasb.vehicleramps";
    private const string RampUp = "AB2_VehicleRampUp";
    private const string RampDown = "AB2_VehicleRampDown";
    private const string GrandUp = "AB2_GrandStairsUp";
    private const string GrandDown = "AB2_GrandStairsDown";
    private const string WalkerName = "ramp-integration-walker";
    private static readonly IntVec3 Home = new(35, 0, 43);
    private static readonly IntVec3 Upper = new(39, 0, 101);
    private static readonly IntVec3 RampCenter = new(34, 0, 34);
    private static readonly IntVec3 GrandCenter = new(61, 0, 33);

    private Building_ABStairs2 ramp;
    private Building_ABStairs2 grand;
    private Pawn walker;
    private bool previousAnimation;
    private bool animationSet;
    private int leg;
    private int orderedAt;
    private bool sawCrossing;

    public override string Id => "ramps.fork-integration";
    public override string Intent => "Construct an up-ramp into a closed floor, use native pawn routing and reload the pair";
    public override string EvidenceLevel => "assigned-job";
    public override string ProgressionMode => "native-frames-v1";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { Aasb };
    public override string[] RequiredDefs => new[] { RampUp, RampDown, GrandUp, GrandDown };
    public override FixtureProvider Fixture => new VehicleRampIntegrationFixture();
    public string StateVersion => "1";

    public override void Setup(Context c)
    {
        CheckContentOwner(c);
        var bands = ABBands.CompOf(c.Map);
        c.Check("upper floor is closed before the ramp is built", !bands.IsOpen(1));

        var rampDef = c.Def<ThingDef>(RampUp);
        Find.ResearchManager.FinishProject(c.Def<ResearchProjectDef>("AB2_Stairs"));
        var rotation = Rot4.North;
        var placement = GenConstruct.CanPlaceBlueprintAt(rampDef, RampCenter, rotation, c.Map,
            false, null, null, ThingDefOf.Steel);
        c.Check("vehicle ramp blueprint placement accepted", placement.Accepted, new { reason = placement.Reason });
        var blueprint = (Blueprint_Build)GenConstruct.PlaceBlueprintForBuild(rampDef, RampCenter, c.Map, rotation,
            Faction.OfPlayer, ThingDefOf.Steel);
        walker = c.SpawnPawn(WalkerName, "Colonist", Home, Faction.OfPlayer);
        walker.drafter.Drafted = true;
        blueprint.TryReplaceWithSolidThing(walker, out var frame, out _);
        ((Frame)frame).CompleteConstruction(walker);

        ramp = c.Map.listerThings.ThingsOfDef(rampDef).Cast<Building_ABStairs2>().Single();
        c.Check("construction opened the upper floor and created its reciprocal ramp", bands.IsOpen(1)
            && IsPair(ramp, RampDown));
        grand = (Building_ABStairs2)GenSpawn.Spawn(ThingMaker.MakeThing(c.Def<ThingDef>(GrandUp), ThingDefOf.Steel),
            GrandCenter, c.Map, rotation);
        grand.SetFaction(Faction.OfPlayer);
        SetForbidden(grand, true);
        c.Check("mixed ramp and grand stairs keep native routing active", UsesNativeRouting(c.Map));

        EnableAnimation();
    }

    public override bool AdvanceNative(Context c)
    {
        if (leg == 2)
            return true;

        var goal = leg == 0 ? Upper : Home;
        var expectedLink = leg == 0 ? RampUp : GrandDown;
        if (orderedAt == 0)
        {
            c.Check("ordered pawn trip through " + expectedLink,
                walker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, goal)));
            orderedAt = c.Tick;
        }

        var holds = (IDictionary)AccessTools.Field(typeof(ABWormholePather), "holding").GetValue(null);
        if (!sawCrossing && holds[walker.thingIDNumber] is object hold)
        {
            var near = (Building_Door)AccessTools.Field(hold.GetType(), "near").GetValue(hold);
            var native = (bool)AccessTools.Field(hold.GetType(), "nativePortal").GetValue(hold);
            c.Check("crossing through " + expectedLink + " used the native pawn path",
                near.def.defName == expectedLink && native);
            sawCrossing = true;
        }

        if (walker.Position != goal || walker.pather.Moving)
        {
            if (c.Tick - orderedAt <= 1800)
                return false;
            c.Check("pawn trip met its tick limit", false, new
            {
                link = expectedLink, position = walker.Position.ToString(), sawCrossing, walker.pather.Moving
            });
            return true;
        }

        c.Check("reached original goal through " + expectedLink, sawCrossing);
        if (leg == 0)
        {
            SetForbidden(ramp, true);
            SetForbidden(grand, false);
        }
        leg++;
        orderedAt = 0;
        sawCrossing = false;
        return leg == 2;
    }

    public override void Assert(Context c) => c.Check("both native pawn trips completed", leg == 2);

    public List<FixtureStateField> CaptureState(Context c) => new()
    {
        new() { Name = "ramp", Value = ramp.GetUniqueLoadID() },
        new() { Name = "counterpart", Value = ramp.Counterparts.Single().GetUniqueLoadID() },
        new() { Name = "grand", Value = grand.GetUniqueLoadID() }
    };

    public void RestoreState(Context c, List<FixtureStateField> state)
    {
        CheckContentOwner(c);
        ramp = FindLink(c, RampUp, state.Single(field => field.Name == "ramp").Value);
        grand = FindLink(c, GrandUp, state.Single(field => field.Name == "grand").Value);
        walker = c.Subject<Pawn>(WalkerName);
        c.Check("the original saved ramp pair is still linked",
            IsPair(ramp, RampDown) && ramp.Counterparts[0].GetUniqueLoadID() ==
            state.Single(field => field.Name == "counterpart").Value);
        c.Check("native routing is active on the restored map", UsesNativeRouting(c.Map));
        EnableAnimation();
    }

    public override void AfterReload(Context c)
    {
        var loadedRamp = c.Map.listerThings.ThingsOfDef(c.Def<ThingDef>(RampUp)).OfType<Building_ABStairs2>().Single();
        c.Check("saved ramp pair and native routing survived reload",
            !ReferenceEquals(loadedRamp, ramp) && IsPair(loadedRamp, RampDown) && UsesNativeRouting(c.Map));
        c.Check("walker kept final destination", c.Subject<Pawn>(WalkerName).Position == Home);
    }

    public override void Teardown(Context c)
    {
        if (animationSet)
            ABMod.Settings.transitAnim = previousAnimation;
    }

    private void EnableAnimation()
    {
        previousAnimation = ABMod.Settings.transitAnim;
        animationSet = true;
        ABMod.Settings.transitAnim = true;
    }

    private static Building_ABStairs2 FindLink(Context c, string defName, string stableId) =>
        c.Map.listerThings.ThingsOfDef(c.Def<ThingDef>(defName)).OfType<Building_ABStairs2>()
            .Single(link => link.GetUniqueLoadID() == stableId);

    private static void CheckContentOwner(Context c)
    {
        bool hasStandalone = ModsConfig.IsActive(Standalone);
        bool hasVehicleFramework = ModsConfig.IsActive(VehicleFramework);
        string expectedDefOwner = hasStandalone ? Standalone : Aasb;
        c.Check("exactly one owner supplied both ramp defs",
            new[] { RampUp, RampDown }.All(name => DefDatabase<ThingDef>.AllDefsListForReading.Count(
                def => def.defName == name) == 1
                && string.Equals(c.Def<ThingDef>(name).modContentPack.PackageId, expectedDefOwner,
                    StringComparison.OrdinalIgnoreCase)));

        var backends = LoadedModManager.RunningModsListForReading
            .SelectMany(mod => mod.assemblies.loadedAssemblies
                .Where(assembly => assembly.GetName().Name == "AASBVehicleRamps")
                .Select(assembly => (mod.PackageId, Assembly: assembly)))
            .ToArray();
        var active = backends.Length == 1 && backends[0].Assembly
            .GetType("AASBVehicleRamps.VehicleRampsMod")?.GetProperty("Active")?.GetValue(null) is true;
        c.Check("one active vehicle backend with VF, zero without VF", hasVehicleFramework
            ? active && string.Equals(backends[0].PackageId, expectedDefOwner, StringComparison.OrdinalIgnoreCase)
            : backends.Length == 0, new { active, owners = backends.Select(backend => backend.PackageId).ToArray() });
    }

    private static bool UsesNativeRouting(Map map) => AccessTools.Method(
        AccessTools.TypeByName("AsAboveSoBelow.ABNativePortals"), "UsesNativeRouting")
        .Invoke(null, new object[] { map }) is true;

    private static bool IsPair(Building_ABStairs2 near, string expectedFarDef) =>
        near.Counterparts.Count == 1 && near.Counterparts[0] is { Spawned: true } far
        && far.def.defName == expectedFarDef && far.Counterparts.Contains(near);

    private static void SetForbidden(Building_ABStairs2 near, bool forbidden)
    {
        near.SetForbidden(forbidden);
        foreach (var far in near.Counterparts)
            far.SetForbidden(forbidden);
    }
}
