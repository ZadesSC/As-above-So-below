using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreLib;
using Unity.Mathematics;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Lets one vehicle at a time use a ramp. A vehicle whose path crosses a ramp claims it as its next step would take
/// it into the ramp's mouth area (<see cref="RampGroup.EntryApron"/>). While another vehicle holds the claim it
/// stops on its current cell, outside that area, and waits in line. The claim is released once the holder has
/// crossed and left both mouth areas, or when it stops, leaves the map, turns away from the ramp or makes no
/// progress for <see cref="StallTicks"/>. The longest-waiting vehicle then takes it (ties by thing ID, so every
/// client agrees).
/// <para>Without this, vehicles ordered across together drive into the same approach cells, then each counts the
/// other as an obstacle and both give up (issue #18). A vehicle opposite the holder waits outside the mouth the holder
/// leaves by. Vehicle Framework's look-ahead lets the holder treat it as parked and drive around it
/// (<see cref="LookAheadPatch.BlockerWaiting"/>); everyone else, including vehicles queued behind it, waits.</para>
/// <para>A vehicle already inside a mouth area when it reaches the ramp cannot stop short: it takes a free claim and
/// otherwise proceeds, and the crossing checks in <see cref="CrossingPatch"/> govern it as before. Claims and the
/// queue are rebuilt from vehicle positions after a load, so nothing is saved. Main thread only.</para>
/// </summary>
public static class RampTraffic
{
    /// <summary>A holder that has not moved or crossed for this long gives the ramp up.</summary>
    internal const int StallTicks = 600;

    private static readonly Dictionary<long, Claim> s_claims = new Dictionary<long, Claim>();
    private static readonly Dictionary<VehiclePawn, Claim> s_byHolder = new Dictionary<VehiclePawn, Claim>();
    private static readonly Dictionary<VehiclePawn, Ticket> s_tickets = new Dictionary<VehiclePawn, Ticket>();
    private static readonly ConditionalWeakTable<VehiclePawn, Approach> s_approaches =
        new ConditionalWeakTable<VehiclePawn, Approach>();
    private static readonly List<VehiclePawn> s_scratch = new List<VehiclePawn>();

    public static int ClaimCount => s_claims.Count;

    public static int TicketCount => s_tickets.Count;

    public static bool IsHolder(VehiclePawn vehicle) => vehicle != null && s_byHolder.ContainsKey(vehicle);

    /// <summary>True while the vehicle is stopped in line for a ramp.</summary>
    public static bool IsQueued(VehiclePawn vehicle) => vehicle != null && s_tickets.ContainsKey(vehicle);

    /// <summary>
    /// Called before Vehicle Framework sets up the step from the vehicle's cell to <paramref name="next"/>. True
    /// stops the vehicle on its cell and puts it in line.
    /// </summary>
    internal static bool ShouldHold(VehiclePathFollower follower, IntVec3 next)
    {
        var vehicle = follower.vehicle;
        var table = RampRegistry.FreshTable(vehicle.Map);
        if (table.IsEmpty || !TryNextCrossing(follower, table, out var edge))
        {
            s_tickets.Remove(vehicle);
            return false;
        }

        var group = edge.Group;
        if (s_byHolder.TryGetValue(vehicle, out var held) && held.Key == group.TrafficKey)
        {
            return false;
        }

        var now = Find.TickManager.TicksGame;
        var apron = group.EntryApron;
        if (Covers(vehicle, apron))
        {
            // Already in the mouth area: it cannot stop short. Take a free claim so others wait, otherwise drive on.
            if (!TryAcquire(vehicle, in edge, now, inside: true) && RampDiagnostics.Enabled)
            {
                RampDiagnostics.Event("inside without claim " + vehicle.def.defName + " " + vehicle.thingIDNumber
                    + " at " + vehicle.Position + " rot " + vehicle.FullRotation.AsInt + " tick " + now);
            }

            return false;
        }

        if (!MayCover(vehicle, next, apron))
        {
            return false;
        }

        if (TryAcquire(vehicle, in edge, now, inside: false))
        {
            return false;
        }

        Enqueue(follower, in edge, now);
        return true;
    }

    /// <summary>
    /// Called each tick Vehicle Framework would try to enter the next cell. Returns true when the vehicle is in line
    /// and must stay put this tick; false lets the native step run (the vehicle was granted the ramp or left the
    /// line).
    /// </summary>
    internal static bool IsHeld(VehiclePathFollower follower)
    {
        var vehicle = follower.vehicle;
        if (s_tickets.Count == 0 || !s_tickets.TryGetValue(vehicle, out var ticket))
        {
            return false;
        }

        // A path request made before the vehicle stopped can complete while it waits. It keeps its place in line as
        // long as it stays on its cell and the new path still leads through the same ramp.
        var table = RampRegistry.FreshTable(vehicle.Map);
        if (!ticket.Matches(follower) || !TryNextCrossing(follower, table, out var edge)
            || edge.Group.TrafficKey != ticket.Key)
        {
            s_tickets.Remove(vehicle);
            if (RampDiagnostics.Enabled)
            {
                RampDiagnostics.Event("left line " + vehicle.def.defName + " " + vehicle.thingIDNumber + " at "
                    + vehicle.Position + ": " + ticket.Mismatch(follower) + " tick " + Find.TickManager.TicksGame);
            }

            return false;
        }

        if (TryAcquire(vehicle, in edge, Find.TickManager.TicksGame, inside: false))
        {
            return false;
        }

        RampDiagnostics.Add(ref RampDiagnostics.ClaimHeldTicks);
        return true;
    }

    /// <summary>
    /// True when <paramref name="follower"/>'s vehicle holds the ramp that <paramref name="blocker"/> is waiting
    /// for. The holder may then drive around the waiting vehicle instead of waiting for it.
    /// </summary>
    internal static bool YieldsTo(VehiclePawn blocker, VehiclePawn follower) =>
        s_tickets.Count > 0 && blocker != null && follower != null && s_tickets.TryGetValue(blocker, out var ticket)
        && s_byHolder.TryGetValue(follower, out var claim) && claim.Key == ticket.Key;

    /// <summary>After each pather tick: releases the vehicle's claim once it no longer needs the ramp.</summary>
    internal static void PatherTicked(VehiclePathFollower follower)
    {
        if (s_byHolder.Count == 0 || !s_byHolder.TryGetValue(follower.vehicle, out var claim))
        {
            return;
        }

        var reason = HolderDone(follower, claim, Find.TickManager.TicksGame);
        if (reason != null)
        {
            Release(claim, reason);
        }
    }

    /// <summary>The vehicle crossed through <paramref name="edge"/>.</summary>
    internal static void NotifyCrossed(VehiclePawn vehicle, in RampEdge edge)
    {
        if (s_byHolder.TryGetValue(vehicle, out var claim) && claim.Key == edge.Group.TrafficKey)
        {
            claim.Crossed = true;
            claim.LastProgressTick = Find.TickManager.TicksGame;
            claim.LastPosition = vehicle.Position;
        }
    }

    internal static void Forget(VehiclePawn vehicle)
    {
        s_tickets.Remove(vehicle);
        s_approaches.Remove(vehicle);
        if (s_byHolder.TryGetValue(vehicle, out var claim))
        {
            Release(claim, "despawned");
        }
    }

    internal static void ClearMap(Map map)
    {
        s_scratch.Clear();
        foreach (var pair in s_tickets)
        {
            if (pair.Value.Map == map)
            {
                s_scratch.Add(pair.Key);
            }
        }

        foreach (var vehicle in s_scratch)
        {
            s_tickets.Remove(vehicle);
        }

        s_scratch.Clear();
        foreach (var pair in s_byHolder)
        {
            if (pair.Value.Map == map)
            {
                s_scratch.Add(pair.Key);
            }
        }

        foreach (var vehicle in s_scratch)
        {
            Release(s_byHolder[vehicle], null);
        }

        s_scratch.Clear();
    }

    internal static void ClearAll()
    {
        s_claims.Clear();
        s_byHolder.Clear();
        s_tickets.Clear();
        s_approaches.Clear();
    }

    /// <summary>The first ramp crossing on the follower's current path, from its next node on.</summary>
    internal static bool TryNextCrossing(VehiclePathFollower follower, RampTable table, out RampEdge edge)
    {
        edge = default;
        var path = follower.curPath;
        if (path == null || table.IsEmpty)
        {
            return false;
        }

        var approach = s_approaches.GetOrCreateValue(follower.vehicle);
        var left = path.NodesLeft;
        if (!ReferenceEquals(approach.Path, path) || approach.TableVersion != table.Version
            || (approach.Found && left < approach.EntryNodesLeft))
        {
            approach.Scan(path, table, follower.vehicle.VehicleDef.size);
        }

        edge = approach.Edge;
        return approach.Found;
    }

    private static bool TryAcquire(VehiclePawn vehicle, in RampEdge edge, int now, bool inside)
    {
        var key = edge.Group.TrafficKey;
        if (s_claims.TryGetValue(key, out var current))
        {
            if (current.Holder == vehicle)
            {
                return true;
            }

            var reason = HolderDone(current.Holder.vehiclePather, current, now);
            if (reason == null)
            {
                return false;
            }

            Release(current, reason);
        }

        if (!inside && !FirstInLine(vehicle, key, now))
        {
            return false;
        }

        if (s_byHolder.TryGetValue(vehicle, out var other))
        {
            // A vehicle holds one ramp at a time; a path through two nearby ramps takes them in turn.
            Release(other, "next ramp");
        }

        s_tickets.Remove(vehicle);
        var claim = new Claim(vehicle, key, in edge, now);
        s_claims.Add(key, claim);
        s_byHolder.Add(vehicle, claim);
        RampDiagnostics.Add(ref RampDiagnostics.ClaimsGranted);
        if (RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("claimed " + vehicle.def.defName + " " + vehicle.thingIDNumber + " ramp "
                + edge.Entry + " -> " + edge.Exit + " at " + vehicle.Position + (inside ? " (inside)" : "")
                + " tick " + now);
        }

        return true;
    }

    /// <summary>
    /// True when no live vehicle has waited longer for the same ramp. A vehicle not yet in line counts as joining
    /// now.
    /// </summary>
    private static bool FirstInLine(VehiclePawn vehicle, long key, int now)
    {
        var myTick = s_tickets.TryGetValue(vehicle, out var mine) && mine.Key == key ? mine.RequestTick : now;
        var first = true;
        s_scratch.Clear();
        foreach (var pair in s_tickets)
        {
            var other = pair.Key;
            var ticket = pair.Value;
            if (other == vehicle || ticket.Key != key)
            {
                continue;
            }

            if (!ticket.Matches(other.vehiclePather))
            {
                s_scratch.Add(other);
                continue;
            }

            if (ticket.RequestTick < myTick
                || (ticket.RequestTick == myTick && other.thingIDNumber < vehicle.thingIDNumber))
            {
                first = false;
            }
        }

        foreach (var stale in s_scratch)
        {
            s_tickets.Remove(stale);
        }

        s_scratch.Clear();
        return first;
    }

    private static void Enqueue(VehiclePathFollower follower, in RampEdge edge, int now)
    {
        var vehicle = follower.vehicle;
        var key = edge.Group.TrafficKey;
        var requestTick = s_tickets.TryGetValue(vehicle, out var previous) && previous.Key == key
            ? previous.RequestTick
            : now;
        var isNew = previous == null || previous.Key != key;
        s_tickets[vehicle] = new Ticket(follower, key, requestTick);
        if (!isNew)
        {
            return;
        }

        RampDiagnostics.Add(ref RampDiagnostics.ClaimHolds);
        if (RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("queued " + vehicle.def.defName + " " + vehicle.thingIDNumber + " at "
                + vehicle.Position + " for ramp " + edge.Entry + " -> " + edge.Exit + " tick " + now);
        }
    }

    /// <summary>Null while the holder still needs the ramp, otherwise why it is done.</summary>
    private static string HolderDone(VehiclePathFollower follower, Claim claim, int now)
    {
        var vehicle = follower.vehicle;
        if (!vehicle.Spawned || vehicle.Dead || vehicle.Map != claim.Map)
        {
            return "gone";
        }

        if (!follower.Moving)
        {
            return "stopped";
        }

        if (vehicle.Position != claim.LastPosition)
        {
            claim.LastPosition = vehicle.Position;
            claim.LastProgressTick = now;
        }

        if (claim.Crossed)
        {
            if (!RampClips.TryGet(vehicle, out _) && !Covers(vehicle, claim.EntryApron)
                && !Covers(vehicle, claim.ExitApron))
            {
                return "cleared";
            }
        }
        else if (follower.curPath != null
            && follower.RequestStatus != VehiclePathFollower.PathRequestStatus.Calculating
            && (!TryNextCrossing(follower, RampRegistry.FreshTable(claim.Map), out var edge)
                || edge.Group.TrafficKey != claim.Key))
        {
            return "rerouted";
        }

        return now - claim.LastProgressTick > StallTicks ? "stalled" : null;
    }

    private static void Release(Claim claim, string reason)
    {
        s_claims.Remove(claim.Key);
        s_byHolder.Remove(claim.Holder);
        if (reason == "stalled")
        {
            RampDiagnostics.Add(ref RampDiagnostics.ClaimsRevoked);
        }

        if (reason != null && RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("released " + claim.Holder.def.defName + " " + claim.Holder.thingIDNumber + ": "
                + reason + " at " + claim.Holder.Position + " tick " + Find.TickManager.TicksGame);
        }
    }

    /// <summary>
    /// True when the vehicle where it stands touches the area: either Vehicle Framework's claim (its footprint at
    /// the full eight-way rotation) or RimWorld's thing grid (its rectangle at the four-way rotation). The crossing
    /// checks read both, and on a diagonal they differ.
    /// </summary>
    private static bool Covers(VehiclePawn vehicle, CellRect area)
    {
        if (area.Area == 0)
        {
            return false;
        }

        if (vehicle.OccupiedRect().Overlaps(area))
        {
            return true;
        }

        var cell = vehicle.Position;
        var size = vehicle.VehicleDef.Size;
        var orientation = new Orientation(vehicle.FullRotation.AsInt);
        foreach (int2 c in new EntityRect(cell.x, cell.z, size.x, size.z, orientation))
        {
            if (c.x >= area.minX && c.x <= area.maxX && c.y >= area.minZ && c.y <= area.maxZ)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the vehicle on <paramref name="cell"/> could touch the area in any rotation. Vehicle Framework may
    /// turn a stopped vehicle in place, so a vehicle stopped one step short must stay clear whichever way it faces:
    /// the test uses the square of its longest side, which holds every rotated footprint.
    /// </summary>
    private static bool MayCover(VehiclePawn vehicle, IntVec3 cell, CellRect area) =>
        area.Area > 0 && vehicle.VehicleDef.VehicleRect(cell, Rot4.North, maxSizePossible: true).Overlaps(area);

    private sealed class Claim
    {
        public Claim(VehiclePawn holder, long key, in RampEdge edge, int now)
        {
            Holder = holder;
            Key = key;
            Map = holder.Map;
            EntryApron = edge.Group.EntryApron;
            ExitApron = edge.Group.ExitApron;
            LastProgressTick = now;
            LastPosition = holder.Position;
        }

        public VehiclePawn Holder { get; }

        public long Key { get; }

        public Map Map { get; }

        public CellRect EntryApron { get; }

        public CellRect ExitApron { get; }

        public bool Crossed { get; set; }

        public int LastProgressTick { get; set; }

        public IntVec3 LastPosition { get; set; }
    }

    /// <summary>
    /// A vehicle stopped in line on its cell. Valid while it stays there with a path to follow; callers check the
    /// path still leads through the ramp.
    /// </summary>
    private sealed class Ticket
    {
        private readonly IntVec3 _cell;

        public Ticket(VehiclePathFollower follower, long key, int requestTick)
        {
            _cell = follower.vehicle.Position;
            Map = follower.vehicle.Map;
            Key = key;
            RequestTick = requestTick;
        }

        public long Key { get; }

        public Map Map { get; }

        public int RequestTick { get; }

        public string Mismatch(VehiclePathFollower follower)
        {
            var vehicle = follower.vehicle;
            return Matches(follower)
                ? "ramp no longer next"
                : $"spawned={vehicle.Spawned} drafted={vehicle.Drafted} moving={follower.Moving} "
                + $"pos={vehicle.Position} next={follower.nextCell} path={follower.curPath != null}";
        }

        public bool Matches(VehiclePathFollower follower)
        {
            var vehicle = follower.vehicle;
            return vehicle.Spawned && !vehicle.Dead && !vehicle.beached && vehicle.Drafted && follower.Moving
                && vehicle.Map == Map && vehicle.Position == _cell && follower.nextCell == _cell
                && follower.curPath != null;
        }
    }

    /// <summary>Cached first crossing on one path, rescanned when the path or table changes or it is passed.</summary>
    private sealed class Approach
    {
        public VehiclePath Path;
        public int TableVersion;
        public bool Found;
        public RampEdge Edge;

        /// <summary>The path's nodes left when the vehicle stands on the crossing's entry cell.</summary>
        public int EntryNodesLeft;

        public void Scan(VehiclePath path, RampTable table, IntVec2 size)
        {
            Path = path;
            TableVersion = table.Version;
            Found = false;
            Edge = default;
            var left = path.NodesLeft;
            for (var i = 0; i + 1 < left; i++)
            {
                var from = path.Peek(i);
                var to = path.Peek(i + 1);
                if (to.AdjacentTo8WayOrInside(from))
                {
                    continue;
                }

                // Stop at the first jump; one that is no longer a ramp is refused at the crossing (CrossingPatch).
                Found = table.TryGetEdge(size, from, to, out Edge);
                EntryNodesLeft = left - i;
                return;
            }
        }
    }
}
