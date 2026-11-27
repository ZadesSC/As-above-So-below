using System;
using System.Collections.Generic;
using System.Linq;
using AASBVehicleRamps;
using RimWorld;
using Rwtf;
using Vehicles;
using Verse;

// Traffic cases for the bundled vehicle ramps (issue #18). Append this file to the adapter generated from the
// RimWorld-Patches ramp tests (see docs/VEHICLE_RAMPS.md). Coordinates are on the RampFixture arena: lower floor
// z 10..52, upper floor = lower + 64. Ramp A's lower mouth faces north at x 34; its upper mouth faces south.
public abstract class RampTrafficCase : RampCase
{
    private readonly List<VehiclePawn> _fleet = new List<VehiclePawn>();
    private string _firstOverlap;
    private string _sharedMouth;

    protected IReadOnlyList<VehiclePawn> Fleet => _fleet;

    public override string[] RequiredPackages => base.RequiredPackages
        .Where(id => id != "zades.aasb.vehicleramps").ToArray();

    protected override int StepTimeoutTicks => 4000;

    protected VehiclePawn Enlist(VehiclePawn vehicle)
    {
        _fleet.Add(vehicle);
        return vehicle;
    }

    /// <summary>Orders every vehicle on the same tick, then waits until each stands still on its goal.</summary>
    protected Func<Context, bool> DriveTogether(IReadOnlyList<IntVec3> goals, string label)
    {
        var ordered = false;
        return c =>
        {
            WatchOverlaps(c);
            if (!ordered)
            {
                ordered = true;
                for (var i = 0; i < _fleet.Count; i++)
                {
                    c.Check(label + " order " + i + " accepted", Order(_fleet[i], goals[i]),
                        new { state = State(_fleet[i]) });
                }

                return false;
            }

            for (var i = 0; i < _fleet.Count; i++)
            {
                var v = _fleet[i];
                if (v.Position != goals[i] || v.vehiclePather.Moving)
                {
                    // An order that ended away from its goal will not finish; let the step time out with evidence.
                    return false;
                }
            }

            c.Event("all-arrived", new { label, tick = c.Tick, states = _fleet.Select(State).ToArray() });
            return true;
        };
    }

    // Two checks per frame. Any body overlap is recorded as information: Vehicle Framework's own look-ahead lets
    // vehicles converging on one lane touch briefly. Two vehicles inside the same ramp mouth area at once fails the
    // case: that is the state the ramp claim prevents and the one that jammed ramps before it.
    protected void WatchTraffic(Context c) => WatchOverlaps(c);

    private void WatchOverlaps(Context c)
    {
        for (var i = 0; i < _fleet.Count && _firstOverlap == null; i++)
        {
            for (var j = i + 1; j < _fleet.Count; j++)
            {
                var a = _fleet[i];
                var b = _fleet[j];
                if (a.Spawned && b.Spawned && a.OccupiedRect().Overlaps(b.OccupiedRect()))
                {
                    _firstOverlap = $"tick {c.Tick}: {State(a)} overlaps {State(b)}";
                    c.Event("overlap", new { info = _firstOverlap });
                    break;
                }
            }
        }

        if (_sharedMouth != null)
        {
            return;
        }

        // One claim covers every mouth area with the same traffic key, so count distinct vehicles across all of
        // them. A vehicle counts when either RimWorld's thing-grid rectangle or Vehicle Framework's own claim (its
        // eight-way footprint) reaches the area.
        var positions = c.Map.GetDetachedMapComponent<VehiclePositionManager>();
        foreach (var traffic in Table.Groups.GroupBy(g => g.TrafficKey))
        {
            var aprons = traffic.SelectMany(g => new[] { g.EntryApron, g.ExitApron }).Where(a => a.Area > 0)
                .Distinct().ToArray();
            var inside = _fleet.Where(v => v.Spawned && aprons.Any(a => Touches(positions, v, a))).ToArray();
            if (inside.Length > 1)
            {
                _sharedMouth = $"tick {c.Tick}: ramp {traffic.Key} mouths hold " + string.Join("; ", inside.Select(State));
                c.Event("shared-mouth", new { info = _sharedMouth });
                return;
            }
        }
    }

    private static bool Touches(VehiclePositionManager positions, VehiclePawn vehicle, CellRect area) =>
        vehicle.OccupiedRect().Overlaps(area) || area.Cells.Any(cell => positions.ClaimedBy(cell) == vehicle);

    protected void CheckCleanTraffic(Context c, int expectedCrossings)
    {
        c.Check("every vehicle crossed once with no refusals or failed orders",
            RampDiagnostics.Crossings == expectedCrossings && RampDiagnostics.JumpFailures == 0
            && RampDiagnostics.RefusedBlocked == 0, new { events = RampDiagnostics.Events });
        c.Check("no two vehicles were in one ramp mouth area at once", _sharedMouth == null,
            new { shared = _sharedMouth, firstOverlap = _firstOverlap });
        c.Check("no ramp claim is left behind", RampTraffic.ClaimCount == 0 && RampTraffic.TicketCount == 0,
            new { claims = RampTraffic.ClaimCount, tickets = RampTraffic.TicketCount });
    }

    protected override void Verify(Context c)
    {
        c.Event("traffic", new
        {
            granted = RampDiagnostics.ClaimsGranted,
            holds = RampDiagnostics.ClaimHolds,
            revoked = RampDiagnostics.ClaimsRevoked,
            heldTicks = RampDiagnostics.ClaimHeldTicks,
            firstOverlap = _firstOverlap,
        });
    }
}

public sealed class RampTrafficPair : RampTrafficCase
{
    public override string Id => "ramps.traffic-pair";

    public override string Intent => "Two vehicles ordered on the same tick climb one ramp without jamming it";

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 48), Rot4.West));
        Step(Wait(90));
        Step(DriveTogether(new[] { new IntVec3(57, 0, 83), new IntVec3(69, 0, 86) }, "pair climb"));
        Step(ctx =>
        {
            CheckCleanTraffic(ctx, 2);
            ctx.Check("each vehicle took the ramp", RampDiagnostics.ClaimsGranted >= 2,
                new { granted = RampDiagnostics.ClaimsGranted });
            // Whether the second vehicle reaches the mouth before the first leaves depends on Vehicle Framework's
            // path timing; ramps.traffic-cleanup and ramps.traffic-waiter-cancel require a vehicle in line.
            ctx.Event("pair-queue", new { holds = RampDiagnostics.ClaimHolds });
            return true;
        });
    }
}

public sealed class RampTrafficTrio : RampTrafficCase
{
    private const string Cv90 = "SV_CVNinety";

    public override string Id => "ramps.traffic-trio";

    public override string Intent => "Three vehicles of two sizes ordered together climb one ramp in turn";

    public override string[] RequiredDefs => base.RequiredDefs.Concat(new[] { Cv90 }).ToArray();

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 48), Rot4.West));
        Enlist(SpawnVehicle(Cv90, new IntVec3(62, 0, 42), Rot4.West));
        Step(Wait(90));
        Step(DriveTogether(new[] { new IntVec3(57, 0, 83), new IntVec3(69, 0, 86), new IntVec3(60, 0, 100) },
            "trio climb"));
        Step(ctx =>
        {
            CheckCleanTraffic(ctx, 3);
            return true;
        });
    }
}

public sealed class RampTrafficOpposed : RampTrafficCase
{
    public override string Id => "ramps.traffic-opposed";

    public override string Intent => "One vehicle climbs while another descends the same ramp; both swap floors";

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 84), Rot4.West));
        Step(Wait(90));
        Step(DriveTogether(new[] { new IntVec3(69, 0, 86), Parking }, "opposed swap"));
        Step(ctx =>
        {
            CheckCleanTraffic(ctx, 2);
            ctx.Check("each vehicle ended on the other floor", FloorOf(Fleet[0].Position) == 1
                && FloorOf(Fleet[1].Position) == 0, new { states = Fleet.Select(State).ToArray() });
            return true;
        });
    }
}

public sealed class RampTrafficOpposedQueued : RampTrafficCase
{
    public override string Id => "ramps.traffic-opposed-queued";

    public override string Intent =>
        "Opposite traffic arriving while the other vehicle holds the ramp waits outside its mouth; the holder crosses "
        + "out past it and the waiting vehicle follows";

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        var climber = Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        // Parked a few cells from the upper mouth, the descender reaches it about when the climber reaches the lower
        // one. Which takes the ramp depends on Vehicle Framework's path timing; either way the other one must arrive
        // while the ramp is held and wait outside the mouth the holder comes out of.
        var descender = Enlist(SpawnVehicle(MainVehicle, new IntVec3(44, 0, 86), Rot4.West));
        var climbGoal = new IntVec3(69, 0, 86);
        var ordered = false;
        var sawQueued = false;
        var otherHeld = false;
        Step(Wait(90));
        Step(ctx =>
        {
            WatchTraffic(ctx);
            if (!ordered)
            {
                ordered = true;
                ctx.Check("both orders accepted", Order(climber, climbGoal) && Order(descender, Parking));
                return false;
            }

            if (!sawQueued && (RampTraffic.IsQueued(descender) || RampTraffic.IsQueued(climber)))
            {
                sawQueued = true;
                otherHeld = RampTraffic.IsQueued(descender) ? RampTraffic.IsHolder(climber)
                    : RampTraffic.IsHolder(descender);
            }

            return climber.Position == climbGoal && descender.Position == Parking
                && !climber.vehiclePather.Moving && !descender.vehiclePather.Moving;
        });
        Step(ctx =>
        {
            CheckCleanTraffic(ctx, 2);
            ctx.Check("one vehicle waited in line while the other held the ramp", sawQueued && otherHeld,
                new { holds = RampDiagnostics.ClaimHolds, events = RampDiagnostics.Events });
            ctx.Check("each vehicle ended on the other floor", FloorOf(climber.Position) == 1
                && FloorOf(descender.Position) == 0, new { states = Fleet.Select(State).ToArray() });
            return true;
        });
    }
}

public sealed class RampTrafficWaiterCancel : RampTrafficCase
{
    public override string Id => "ramps.traffic-waiter-cancel";

    public override string Intent => "Undrafting a vehicle waiting in line drops it from the line at once";

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        var first = Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        var second = Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 48), Rot4.West));
        var goals = new[] { new IntVec3(57, 0, 83), new IntVec3(69, 0, 86) };
        VehiclePawn holder = null;
        VehiclePawn waiter = null;
        var cancelTick = -1;
        var ordered = false;
        Step(Wait(90));
        Step(ctx =>
        {
            WatchTraffic(ctx);
            if (!ordered)
            {
                ordered = true;
                ctx.Check("both orders accepted", Order(first, goals[0]) && Order(second, goals[1]));
                return false;
            }

            if (waiter == null)
            {
                var claimed = Fleet.FirstOrDefault(RampTraffic.IsHolder);
                var queued = Fleet.FirstOrDefault(RampTraffic.IsQueued);
                if (claimed == null || queued == null)
                {
                    return false;
                }

                holder = claimed;
                waiter = queued;
                waiter.ignition.Drafted = false;
                cancelTick = ctx.Tick;
                ctx.Event("waiter-undrafted", new { holder = State(holder), waiter = State(waiter), tick = ctx.Tick });
                return false;
            }

            if (ctx.Tick - cancelTick == 5)
            {
                ctx.Check("the undrafted vehicle left the line within a few ticks", !RampTraffic.IsQueued(waiter)
                    && RampTraffic.TicketCount == 0 && RampTraffic.IsHolder(holder),
                    new { tickets = RampTraffic.TicketCount, waiter = State(waiter) });
            }

            return ctx.Tick - cancelTick > 5 && holder.Position == goals[holder == first ? 0 : 1]
                && !holder.vehiclePather.Moving;
        });
        Step(ctx =>
        {
            ctx.Check("the holder crossed and nothing is left in the ramp tables", RampDiagnostics.Crossings == 1
                && RampDiagnostics.JumpFailures == 0 && FloorOf(waiter.Position) == 0 && RampTraffic.ClaimCount == 0
                && RampTraffic.TicketCount == 0, new { events = RampDiagnostics.Events });
            return true;
        });
    }
}

public sealed class RampTrafficCleanup : RampTrafficCase
{
    public override string Id => "ramps.traffic-cleanup";

    public override string Intent => "Undrafting the vehicle that holds the ramp hands the ramp to the waiting one";

    protected override void Arrange(Context c)
    {
        SpawnRamp("AB2_VehicleRampDown", UpperA, Rot4.North);
        var first = Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 40), Rot4.West));
        var second = Enlist(SpawnVehicle(MainVehicle, new IntVec3(50, 0, 48), Rot4.West));
        var goals = new[] { new IntVec3(57, 0, 83), new IntVec3(69, 0, 86) };
        VehiclePawn holder = null;
        VehiclePawn waiter = null;
        var ordered = false;
        Step(Wait(90));
        Step(ctx =>
        {
            if (!ordered)
            {
                ordered = true;
                ctx.Check("both orders accepted", Order(first, goals[0]) && Order(second, goals[1]));
                return false;
            }

            if (holder == null)
            {
                var claimed = Fleet.FirstOrDefault(RampTraffic.IsHolder);
                var queued = Fleet.FirstOrDefault(RampTraffic.IsQueued);
                if (claimed == null || queued == null)
                {
                    return false;
                }

                holder = claimed;
                waiter = queued;
                holder.ignition.Drafted = false;
                ctx.Event("holder-undrafted", new { holder = State(holder), waiter = State(waiter), tick = ctx.Tick });
                return false;
            }

            return !waiter.vehiclePather.Moving && waiter.Position == goals[waiter == first ? 0 : 1];
        });
        Step(ctx =>
        {
            ctx.Check("the waiting vehicle took over the ramp and crossed", RampDiagnostics.Crossings == 1
                && RampDiagnostics.JumpFailures == 0 && FloorOf(waiter.Position) == 1
                && FloorOf(holder.Position) == 0, new { events = RampDiagnostics.Events });
            ctx.Check("the undrafted holder no longer holds the ramp", !RampTraffic.IsHolder(holder)
                && RampTraffic.ClaimCount == 0 && RampTraffic.TicketCount == 0);
            return true;
        });
    }
}
