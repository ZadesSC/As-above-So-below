using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class GrandStairRoutingFixture : FixtureProvider
{
    private readonly WorkshopParityFixture layout = new WorkshopParityFixture();

    public override FixtureContract Contract
    {
        get
        {
            var contract = layout.Contract;
            contract.Id = "aasb-grand-stair-routing";
            contract.Intent = "Native trips on a mixed stair map and compatibility with built elevators";
            contract.Interventions = contract.Interventions.Concat(new[] {
                "paired-grand-staircase", "paired-elevator-spawned-as-existing-building",
                "assigned-goto", "controlled-forbidden-links", "forced-crossing-animation"
            }).ToArray();
            return contract;
        }
    }

    public override void Setup(Context c) => layout.Setup(c);
    public override void Validate(Context c, string stage) => layout.Validate(c, stage);
}

public sealed class GrandStairRoutingScenario : Rwtf.Scenario
{
    private static readonly IntVec3 Home = new IntVec3(59, 0, 32);
    private static readonly IntVec3 Upper = new IntVec3(69, 0, 99);
    private static readonly IntVec3 GrandCell = new IntVec3(67, 0, 33);
    private static readonly IntVec3 ElevatorCell = new IntVec3(57, 0, 36);
    private static readonly MethodInfo UsesNativeRouting = AccessTools.Method(
        AccessTools.TypeByName("AsAboveSoBelow.ABNativePortals"), "UsesNativeRouting");
    private static readonly FieldInfo Holding = AccessTools.Field(
        AccessTools.TypeByName("AsAboveSoBelow.ABWormholePather"), "holding");

    private static readonly (IntVec3 goal, string link, bool native)[] Trips = {
        (Upper, "AB2_GrandStairsUp", true),
        (Home, "AB2_GrandStairsDown", true),
        (Upper, "AB2_LadderUp", true),
        (Home, "AB2_Elevator", false)
    };

    private Pawn walker;
    private Building_Door grand;
    private Building_Door ladder;
    private ThingDef elevator;
    private object settings;
    private FieldInfo animation;
    private object previousAnimation;
    private int trip;
    private int orderedAt;
    private bool sawCrossing;

    public override string Id => "aasb.grand-stair-routing";
    public override string Intent => "Use both grand stair directions and a ladder natively, then cross through an existing elevator";
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override string[] RequiredDefs => new[] {
        "AB2_LadderUp", "AB2_GrandStairsUp", "AB2_GrandStairsDown", "AB2_Elevator"
    };
    public override FixtureProvider Fixture => new GrandStairRoutingFixture();

    public override void Setup(Context c)
    {
        grand = Spawn(c, c.Def<ThingDef>("AB2_GrandStairsUp"), GrandCell);
        ladder = c.Map.listerThings.ThingsOfDef(c.Def<ThingDef>("AB2_LadderUp")).Cast<Building_Door>().Single();
        elevator = c.Def<ThingDef>("AB2_Elevator");
        walker = c.SpawnPawn("grand-stair-walker", "Colonist", Home, Faction.OfPlayer);
        walker.drafter.Drafted = true;
        settings = AccessTools.Property(AccessTools.TypeByName("AsAboveSoBelow.ABMod"), "Settings").GetValue(null);
        animation = AccessTools.Field(settings.GetType(), "transitAnim");
        previousAnimation = animation.GetValue(settings);
        animation.SetValue(settings, true);

        c.Check("mixed ladder and grand stairs keep native routing active", Native(c.Map));
        c.Check("new elevators have no vanilla build designator and saved plans retain their definitions",
            BuildCopyCommandUtility.FindAllowedDesignator(elevator, mustBeVisible: false) == null
            && elevator.blueprintDef != null && elevator.frameDef != null);
    }

    public override bool AdvanceNative(Context c)
    {
        if (trip == Trips.Length)
            return true;

        var (goal, link, native) = Trips[trip];
        if (orderedAt == 0)
        {
            if (trip == 0)
                ladder.SetForbidden(true);
            if (trip == 2)
            {
                ladder.SetForbidden(false);
                grand.SetForbidden(true);
            }
            if (trip == 3)
            {
                ladder.SetForbidden(true);
                Spawn(c, elevator, ElevatorCell);
            }

            c.Check(link + " trip selected the expected routing mode", Native(c.Map) == native);
            Job job = JobMaker.MakeJob(JobDefOf.Goto, goal);
            c.Check(link + " trip was ordered", walker.jobs.TryTakeOrderedJob(job));
            orderedAt = c.Tick;
        }

        object hold = ((IDictionary)Holding.GetValue(null))[walker.thingIDNumber];
        if (hold != null && !sawCrossing)
        {
            var near = (Building_Door)AccessTools.Field(hold.GetType(), "near").GetValue(hold);
            bool usedNative = (bool)AccessTools.Field(hold.GetType(), "nativePortal").GetValue(hold);
            c.Check(link + " was used for the crossing", near.def.defName == link && usedNative == native);
            sawCrossing = true;
        }

        if (walker.Position == goal && !walker.pather.Moving)
        {
            c.Check(link + " trip reached the original goal after crossing", sawCrossing);
            sawCrossing = false;
            orderedAt = 0;
            trip++;
        }

        return orderedAt != 0 && c.Tick - orderedAt > 600;
    }

    public override void Assert(Context c) => c.Check("all four assigned trips completed", trip == Trips.Length);

    public override void AfterReload(Context c)
    {
        c.Check("existing shaft still connects both floors after reload",
            c.Map.listerThings.ThingsOfDef(elevator).Count == 2 && !Native(c.Map)
            && c.Map.reachability.CanReach(ElevatorCell, ElevatorCell + new IntVec3(0, 0, 64),
                PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly)));
        c.Check("walker kept the final destination position", c.Subject<Pawn>("grand-stair-walker").Position == Home);
    }

    public override void Teardown(Context c)
    {
        if (animation != null)
            animation.SetValue(settings, previousAnimation);
    }

    private static bool Native(Map map) => UsesNativeRouting.Invoke(null, new object[] { map }) is true;

    private static Building_Door Spawn(Context c, ThingDef def, IntVec3 cell)
    {
        var door = (Building_Door)GenSpawn.Spawn(ThingMaker.MakeThing(def, ThingDefOf.Steel), cell, c.Map);
        door.SetFaction(Faction.OfPlayer);
        return door;
    }
}
