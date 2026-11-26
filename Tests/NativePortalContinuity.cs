using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Rwtf;
using Verse;
using Verse.AI;

public sealed class PortalContinuityFixture : FixtureProvider, IFixturePersistence
{
    private readonly WorkshopParityFixture layout = new WorkshopParityFixture();
    public override FixtureContract Contract
    {
        get
        {
            var contract = layout.Contract;
            contract.Id = "aasb-portal-continuity";
            contract.Intent = "Displace a pawn during a native hold or load a real pre-native first leg";
            contract.Interventions = contract.Interventions.Concat(new[] {
                "assigned-goto", "short-setup-tick-advance", "explicit-touch-end-mode",
                "same-mouth-replacement-control", "forced-crossing-animation",
                "same-job-position-displacement", "camera-focus"
            }).ToArray();
            return contract;
        }
    }

    public string StateVersion => "1";
    public override void Setup(Context c) => layout.Setup(c);
    public override void Validate(Context c, string stage) => layout.Validate(c, stage);
    public List<FixtureStateField> CaptureState(Context c) => new List<FixtureStateField>();
    public void RestoreState(Context c, List<FixtureStateField> state)
    {
        if (state.Count != 0)
            throw new Unsupported("Unexpected portal fixture state");
    }
}

public abstract class PortalContinuityScenario : Rwtf.Scenario
{
    protected static readonly IntVec3 Start = new IntVec3(53, 0, 25);
    protected static readonly IntVec3 Goal = new IntVec3(73, 0, 96);
    protected static readonly Type Transit = AccessTools.TypeByName("AsAboveSoBelow.ABWormholePather");
    public override string ProgressionMode => "native-frames-v1";
    public override string EvidenceLevel => "assigned-job";
    public override string[] SupportedGameVersions => new[] { "1.7" };
    public override string[] RequiredPackages => new[] { "astryl.AsAboveSoBelow2" };
    public override FixtureProvider Fixture => new PortalContinuityFixture();

    private string capture;
    private int frames;

    protected static Job Order(Pawn pawn, IntVec3 goal)
    {
        var order = JobMaker.MakeJob(JobDefOf.Goto, goal);
        order.playerForced = true;
        pawn.jobs.StartJob(order, JobCondition.InterruptForced);
        return order;
    }

    protected static T Field<T>(object record, string name) =>
        (T)AccessTools.Field(record.GetType(), name).GetValue(record);

    protected void Capture(Context c, Pawn pawn, string filename)
    {
        if (Environment.GetEnvironmentVariable("RWTF_RENDERED") != "1")
            return;

        AccessTools.Method(AccessTools.TypeByName("AsAboveSoBelow.ABBandView"), "SetBand")
            .Invoke(null, new object[] { c.Map, pawn.Position.z / 64, false });
        Find.CameraDriver.SetRootPosAndSize(pawn.Position.ToVector3Shifted(), 13f);
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn);
        c.Map.debugDrawer.FlashCell(pawn.Position, 0.35f, "Pawn", 30);
        capture = Path.Combine(c.Output, filename);
        frames = 0;
        c.Event("portal-capture", new { filename, tick = c.Tick, position = pawn.Position.ToString() });
    }

    protected bool FinishCapture()
    {
        if (capture == null)
            return true;
        if (++frames == 2)
            UnityEngine.ScreenCapture.CaptureScreenshot(capture);
        if (!File.Exists(capture) || new FileInfo(capture).Length <= 8)
            return false;
        capture = null;
        return true;
    }
}

public sealed class LegacyFirstLegScenario : PortalContinuityScenario, IFixturePersistence
{
    public override string Id => "aasb.pr7-legacy-first-leg";
    public override string Intent => "Load the pre-native first walking leg with its original Touch order and reject a stale record for a replacement Goto.";
    public string StateVersion => "1";
    private Pawn walker;
    private Pawn replacement;
    private int jobId;
    private int replacementJobId;
    private bool restored;
    private bool photographed;
    private IntVec3 savedPosition;
    private IntVec3 approach;
    private int exerciseStart = -1;

    public override void Setup(Context c)
    {
        walker = c.SpawnPawn("legacy-walker", "Colonist", Start, Faction.OfPlayer);
        replacement = c.SpawnPawn("legacy-replacement", "Colonist", new IntVec3(56, 0, 36), Faction.OfPlayer);
        walker.drafter.Drafted = replacement.drafter.Drafted = true;
        jobId = Order(walker, Goal).loadID;
        walker.pather.StartPath(Goal, PathEndMode.Touch);
        Order(replacement, new IntVec3(71, 0, 98));
        approach = replacement.pather.Destination.Cell;

        for (int i = 0; i < 60 && walker.Position == Start; i++)
            c.Step();
        savedPosition = walker.Position;

        // Save before the next legacy tick can discard the previous job's record.
        replacementJobId = Order(replacement, approach).loadID;
        object[] records = ((IEnumerable)AccessTools.Method(Transit, "CollectForSave")
            .Invoke(null, new object[] { c.Map })).Cast<object>().ToArray();
        object walk = records.Single(record => Field<Pawn>(record, "pawn") == walker);
        c.Check("pre-native checkpoint is on the first walking leg", walker.pather.Moving && savedPosition != Start
            && savedPosition.z < 64 && walker.pather.Destination.Cell == approach && savedPosition != approach
            && !Field<bool>(walk, "legTwo") && Field<IntVec3>(walk, "approachCell") == approach
            && Field<IntVec3>(walk, "entryCell") != approach
            && Field<LocalTargetInfo>(walk, "realDest") == (LocalTargetInfo)Goal
            && Field<PathEndMode>(walk, "realPeMode") == PathEndMode.Touch);
        c.Check("pre-native save contains a stale record beside a local replacement order",
            replacement.pather.Moving && replacement.pather.Destination.Cell == approach
            && records.Any(record => Field<Pawn>(record, "pawn") == replacement));
        c.Event("first-leg-save", new { jobId, replacementJobId, position = savedPosition.ToString(), approach = approach.ToString() });
    }

    public List<FixtureStateField> CaptureState(Context c) => new List<FixtureStateField>
    {
        new FixtureStateField { Name = "jobId", Value = jobId.ToString(CultureInfo.InvariantCulture) },
        new FixtureStateField { Name = "replacementJobId", Value = replacementJobId.ToString(CultureInfo.InvariantCulture) },
        new FixtureStateField { Name = "approach", Value = approach.ToString() },
        new FixtureStateField { Name = "position", Value = savedPosition.ToString() }
    };

    public void RestoreState(Context c, List<FixtureStateField> state)
    {
        jobId = int.Parse(state.Single(f => f.Name == "jobId").Value, CultureInfo.InvariantCulture);
        replacementJobId = int.Parse(state.Single(f => f.Name == "replacementJobId").Value, CultureInfo.InvariantCulture);
        walker = c.Subject<Pawn>("legacy-walker");
        replacement = c.Subject<Pawn>("legacy-replacement");
        var savedApproach = state.Single(f => f.Name == "approach").Value;
        approach = replacement.CurJob.targetA.Cell;
        IntVec3 destination = walker.pather.Destination.Cell;
        var endMode = Field<PathEndMode>(walker.pather, "peMode");
        // RimWorld can tick once during loading, before the Runner rebinds the subjects.
        bool onSavedLeg = destination == approach && endMode == PathEndMode.OnCell;
        bool alreadyResumed = destination == Goal && endMode == PathEndMode.Touch;
        restored = true;
        c.Check("the original in-flight jobs loaded with the saved leg or its restored Touch route",
            walker.CurJob?.loadID == jobId && walker.CurJob.targetA.Cell == Goal && walker.pather.Moving
            && walker.Position.ToString() == state.Single(f => f.Name == "position").Value
            && approach.ToString() == savedApproach && (onSavedLeg || alreadyResumed)
            && replacement.CurJob?.loadID == replacementJobId && replacement.CurJob.targetA.Cell == approach
            && replacement.pather.Moving && replacement.pather.Destination.Cell == approach,
            new { onSavedLeg, alreadyResumed, destination = destination.ToString(), endMode = endMode.ToString(),
                position = walker.Position.ToString(), replacementDestination = replacement.pather.Destination.Cell.ToString() });
    }

    private static bool AtTouchGoal(Pawn pawn) =>
        !pawn.pather.Moving && pawn.Position != Goal && pawn.Position.z >= 64
        && pawn.CanReachImmediate(Goal, PathEndMode.Touch);

    public override bool AdvanceNative(Context c)
    {
        if (exerciseStart < 0)
            exerciseStart = c.Tick;
        if (restored && !photographed && AtTouchGoal(walker))
        {
            photographed = true;
            Capture(c, walker, "legacy-restored-touch-goal.png");
        }
        return ((AtTouchGoal(walker) && replacement.Position == approach) || c.Tick - exerciseStart > 1200)
            && FinishCapture();
    }

    public override void Assert(Context c)
    {
        c.Check("the original Touch order completed next to its upper-floor goal", AtTouchGoal(walker),
            new { position = walker.Position.ToString(), moving = walker.pather.Moving, restored });
        c.Check("replacement finished on the departure floor at its own mouth approach",
            replacement.Position == approach, new { position = replacement.Position.ToString(), approach = approach.ToString() });
    }

    public override void AfterReload(Context c)
    {
        c.Check("both named pawns remained at their respective goals after the final reload",
            AtTouchGoal(c.Subject<Pawn>("legacy-walker")) && c.Subject<Pawn>("legacy-replacement").Position == approach);
    }
}

public sealed class NativeHoldDisplacementScenario : PortalContinuityScenario
{
    public override string Id => "aasb.pr7-hold-displacement";
    public override string Intent => "Displace a held pawn on the same job with unchanged permissions, then observe a fresh approach and crossing.";
    private static readonly IntVec3 Displaced = new IntVec3(55, 0, 33);
    private Pawn walker;
    private Job job;
    private int startTick;
    private int displacementTick;
    private int stage;
    private IntVec3 mouth;
    private object settings;
    private object previousAnimation;
    private System.Reflection.FieldInfo animation;
    private Building_Door near;
    private Building_Door far;
    private bool freshApproach;
    private bool observedReplan;
    private IDictionary Holds => (IDictionary)AccessTools.Field(Transit, "holding").GetValue(null);
    private bool Allowed => (bool)AccessTools.Method(
        AccessTools.TypeByName("AsAboveSoBelow.ABNativePortals"), "CanCarry")
        .Invoke(null, new object[] { walker, near, far });

    public override void Setup(Context c)
    {
        walker = c.SpawnPawn("displaced-walker", "Colonist", Start, Faction.OfPlayer);
        walker.drafter.Drafted = true;
        settings = AccessTools.Property(AccessTools.TypeByName("AsAboveSoBelow.ABMod"), "Settings").GetValue(null);
        animation = AccessTools.Field(settings.GetType(), "transitAnim");
        previousAnimation = animation.GetValue(settings);
        animation.SetValue(settings, true);
    }

    public override bool AdvanceNative(Context c)
    {
        if (!FinishCapture())
            return false;

        object hold = Holds[walker.thingIDNumber];
        switch (stage)
        {
            case 0:
                job = Order(walker, Goal);
                startTick = c.Tick;
                stage = 1;
                break;
            case 1 when hold != null:
                mouth = walker.Position;
                near = Field<Building_Door>(hold, "near");
                far = Field<Building_Door>(hold, "far");
                c.Check("owned native hold at an allowed ground-floor mouth", Field<bool>(hold, "nativePortal")
                    && Field<IntVec3>(hold, "holdCell") == mouth && mouth.z < 64
                    && walker.CurJob == job && !walker.pather.Moving && Allowed
                    && Displaced.Standable(c.Map) && Displaced.DistanceTo(mouth) > 4);
                walker.Position = Displaced;
                displacementTick = c.Tick;
                c.Check("the displacement changed only the pawn position, with the hold and order still active",
                    walker.Position == Displaced && walker.CurJob == job && job.targetA.Cell == Goal
                    && Equals(Holds[walker.thingIDNumber], hold) && Allowed);
                stage = 2;
                break;
            case 2 when c.Tick > displacementTick:
                observedReplan = walker.Position == Displaced && walker.CurJob == job && Allowed && hold == null
                    && walker.pather.Moving && walker.pather.Destination.Cell == Goal;
                c.Check("the old hold cancelled at the displaced cell and replanned the same order without carrying",
                    observedReplan, new { position = walker.Position.ToString(), jobSame = walker.CurJob == job,
                        allowed = Allowed, hold = hold != null, moving = walker.pather.Moving,
                        destination = walker.pather.Destination.Cell.ToString() });
                if (observedReplan)
                    Capture(c, walker, "native-after-displacement.png");
                stage = 3;
                break;
            case 3 when walker.CurJob == job && hold != null && walker.Position == mouth:
                freshApproach = true;
                stage = 4;
                break;
            case 4 when walker.Position == Goal:
                stage = 5;
                Capture(c, walker, "native-after-fresh-crossing.png");
                break;
            case 5:
                return true;
        }
        return c.Tick - startTick > 1600;
    }

    public override void Assert(Context c) => c.Check(
        "same job returned to the mouth before crossing and reaching its original goal",
        observedReplan && freshApproach && walker.Position == Goal,
        new { observedReplan, freshApproach, position = walker.Position.ToString() });

    public override void AfterReload(Context c) => c.Check(
        "displaced pawn retained its achieved upper-floor position on final reload",
        c.Subject<Pawn>("displaced-walker").Position == Goal);

    public override void Teardown(Context c)
    {
        if (settings != null)
            animation.SetValue(settings, previousAnimation);
    }
}
