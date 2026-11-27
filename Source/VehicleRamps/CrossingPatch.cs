using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AsAboveSoBelow;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Prefix on <c>VehiclePathFollower.SetupMoveIntoNextCell</c>. When the next path node is a ramp crossing (a
/// non-adjacent node), the crossing is revalidated against the current table and the lanes at both mouths before the
/// vehicle moves. A valid crossing moves the vehicle, with everyone aboard, to the exit cell facing out of the
/// far mouth and lets the native step continue along the same path. Otherwise the vehicle stops and requests a
/// new path to its original destination; repeated refusals end the order instead of retrying forever.
/// With AASB's transit animation enabled the crossing is animated (<see cref="RampClip"/>): the vehicle is held on
/// its approach cell for the entry clip, the crossing commits (with the same rechecks) when the clip ends, and the
/// vehicle is held on the exit cell for the emerge clip before the native step continues. Holding returns false
/// without consuming path nodes, so the vehicle keeps its cell and claims. A path that no longer leads through the
/// crossing (a new order, or a re-plan elsewhere) cancels the entry clip and is followed normally.
/// <para>While held, <see cref="HoldPrefix"/> also skips Vehicle Framework's per-tick cell entry
/// (<c>TryEnterNextPathCell</c>), which otherwise runs every tick because no cell cost is left: its path re-check and
/// its run-over collision check would fire every tick instead of once per cell. The cell cost total is kept across the
/// crossing so the next native collision check uses a real speed.</para>
/// <para>Before a vehicle drives into a ramp's mouth area it takes that ramp's claim (<see cref="RampTraffic"/>);
/// while another vehicle holds it, the same hold keeps the vehicle on its cell outside the area.</para>
/// </summary>
public static class CrossingPatch
{
    private const int RetryWindowTicks = 600;
    private const int MaxRefusalsPerWindow = 3;
    private const int OccupancyCheckIntervalTicks = 10;
    private const int OccupancyWaitTicks = 90;

    private static readonly ConditionalWeakTable<VehiclePawn, RefusalWindow> s_refusals =
        new ConditionalWeakTable<VehiclePawn, RefusalWindow>();
    private static readonly Dictionary<VehiclePawn, OccupancyWait> s_waits =
        new Dictionary<VehiclePawn, OccupancyWait>();
    private static readonly List<VehiclePawn> s_expired = new List<VehiclePawn>();

    public static bool IsWaitingForOccupancy(VehiclePawn vehicle) => s_waits.ContainsKey(vehicle);

    /// <summary>
    /// Prefix on <c>VehiclePathFollower.TryEnterNextPathCell</c>: skips the native body while a clip is holding the
    /// vehicle and is not yet due. With no path, or a path that no longer uses this crossing, native runs so new
    /// orders, arrival and cancellation behave normally.
    /// </summary>
    public static bool HoldPrefix(VehiclePathFollower __instance)
    {
        var vehicle = __instance.vehicle;
        var now = Find.TickManager.TicksGame;
        if (RampTraffic.IsHeld(__instance))
        {
            return false;
        }

        if (s_waits.Count > 0 && s_waits.TryGetValue(vehicle, out var wait))
        {
            if (wait.Matches(__instance))
            {
                return PollWait(__instance, vehicle, wait, now);
            }

            s_waits.Remove(vehicle);
        }

        RampClips.TryGet(vehicle, out var clip);
        if (clip != null && IsHolding(__instance, clip, now))
        {
            if (RampDiagnostics.HoldGuardDisabled)
            {
                return true;
            }

            RampDiagnostics.Add(ref RampDiagnostics.HeldTicks);
            return false;
        }

        // VF may handle a vehicle claim in its native look-ahead before dispatch. If the follower
        // is already at the entry, handle occupancy before those native checks and collision effects.
        var path = __instance.curPath;
        var entry = vehicle.Position;
        if (path == null || path.NodesLeft < 2 || __instance.nextCell != entry || vehicle.beached
            || __instance.Waiting || __instance.RequestStatus == VehiclePathFollower.PathRequestStatus.Calculating)
        {
            return true;
        }

        var exit = path.Peek(1);
        if (exit.AdjacentTo8WayOrInside(entry)
            || !RampRegistry.FreshTable(vehicle.Map).TryGetEdge(vehicle.VehicleDef.size, entry, exit, out var edge)
            || OccupiedSide(vehicle.Map, vehicle, in edge) == null)
        {
            return true;
        }

        RampClips.Clear(vehicle);
        LanesClear(__instance, vehicle, vehicle.Map, in edge);
        return false;
    }

    internal static void UpdateWait(VehiclePathFollower follower)
    {
        if (s_waits.Count == 0 || !s_waits.TryGetValue(follower.vehicle, out var wait))
        {
            return;
        }

        if (!wait.Matches(follower))
        {
            s_waits.Remove(follower.vehicle);
        }
        else if (follower.vehicle.stances.stunner.Stunned)
        {
            wait.Pause();
        }
    }

    internal static void ClearWait(VehiclePawn vehicle) => s_waits.Remove(vehicle);

    internal static void ClearWaits(Map map)
    {
        // Collect first: removing while enumerating a Dictionary throws on RimWorld's runtime.
        s_expired.Clear();
        foreach (var (vehicle, wait) in s_waits)
        {
            if (wait.Map == map)
            {
                s_expired.Add(vehicle);
            }
        }

        foreach (var vehicle in s_expired)
        {
            s_waits.Remove(vehicle);
        }

        s_expired.Clear();
    }

    internal static void ClearAllWaits() => s_waits.Clear();

    /// <summary>True while the clip keeps the vehicle on its cell and the crossing step is not yet due.</summary>
    public static bool IsHolding(VehiclePathFollower follower, RampClip clip, int now)
    {
        var path = follower.curPath;
        var vehicle = follower.vehicle;
        var edge = clip.Edge;
        if (path == null)
        {
            return false;
        }

        if (clip.Phase == RampClipPhase.Emerge)
        {
            return vehicle.Position == edge.Exit && !clip.EmergeDone(now);
        }

        return vehicle.Position == edge.Entry && path.NodesLeft >= 2 && path.Peek(1) == edge.Exit
            && !clip.EntryDone(now);
    }

    public static bool Prefix(VehiclePathFollower __instance)
    {
        var path = __instance.curPath;
        if (RampClips.AnyActive && RampClips.TryGet(__instance.vehicle, out var clip)
            && ContinueClip(__instance, __instance.vehicle, path, clip, out var held))
        {
            return held;
        }

        if (path == null || path.NodesLeft < 2)
        {
            return true;
        }

        var vehicle = __instance.vehicle;
        var map = vehicle?.Map;
        var position = vehicle?.Position ?? IntVec3.Invalid;
        if (path.NodesLeft >= 3 && path.Peek(1) == position)
        {
            // A path that arrived after the vehicle moved on can start with its current cell. Vehicle Framework would
            // skip that node and move straight to the one after it, which may be a ramp jump that then bypasses the
            // crossing checks below. Skip it here so the real next node is classified.
            path.ConsumeNextNode();
        }

        var next = path.Peek(1);
        if (map == null)
        {
            return true;
        }

        if (next == position || next.AdjacentTo8Way(position))
        {
            // An ordinary step. Stop here if it would enter the mouth area of a ramp another vehicle is using.
            return !RampTraffic.ShouldHold(__instance, next);
        }

        var ramps = RampRegistry.For(map);
        var table = ramps.Fresh();
        if (table.TryGetEdge(vehicle.VehicleDef.size, position, next, out var edge))
        {
            if (!LanesClear(__instance, vehicle, map, in edge))
            {
                return false;
            }

            if (RampClips.AnimationsEnabled)
            {
                StartEntry(vehicle, edge);
                return false;
            }

            return Cross(__instance, vehicle, map, path, edge);
        }

        if (!ramps.EverHadRamps)
        {
            // Not a ramp crossing this mod planned; leave it to Vehicle Framework.
            return true;
        }

        // The planned ramp was removed, forbidden or changed after the path was found.
        Refuse(__instance, vehicle, position, next, Refusal.Stale);
        return false;
    }

    /// <summary>
    /// Advances an active clip. Returns true when the clip decided this step (<paramref name="result"/> is the
    /// prefix result), false to continue with normal crossing handling.
    /// </summary>
    private static bool ContinueClip(VehiclePathFollower follower, VehiclePawn vehicle, VehiclePath path, RampClip clip,
        out bool result)
    {
        result = false;
        var now = Find.TickManager.TicksGame;
        var edge = clip.Edge;
        if (clip.Phase == RampClipPhase.Emerge)
        {
            if (vehicle.Position == edge.Exit && !clip.EmergeDone(now))
            {
                return true;
            }

            RampClips.Clear(vehicle);
            return false;
        }

        if (path == null || path.NodesLeft < 2 || vehicle.Position != edge.Entry || path.Peek(1) != edge.Exit)
        {
            // The vehicle was given a path that no longer uses this crossing: cancel and follow it normally.
            RampClips.Clear(vehicle);
            RampDiagnostics.Add(ref RampDiagnostics.ClipsCancelled);
            return false;
        }

        if (!clip.EntryDone(now))
        {
            return true;
        }

        // Commit: recheck the current ramp pair and both lanes. Terrain and things can change during the clip.
        RampClips.Clear(vehicle);
        var map = vehicle.Map;
        var table = RampRegistry.FreshTable(map);
        if (!table.TryGetEdge(vehicle.VehicleDef.size, edge.Entry, edge.Exit, out var current))
        {
            Refuse(follower, vehicle, edge.Entry, edge.Exit, Refusal.Stale);
            return true;
        }

        if (!LanesClear(follower, vehicle, map, in current))
        {
            return true;
        }

        Cross(follower, vehicle, map, path, current);
        clip.BeginEmerge(now);
        if (RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("emerge " + vehicle.def.defName + " at " + current.Exit + " cost total "
                + follower.nextCellCostTotal.ToString("0.##") + " tick " + now);
        }

        RampClips.Start(vehicle, clip);
        return true;
    }

    private static void StartEntry(VehiclePawn vehicle, RampEdge edge)
    {
        // Vehicle Framework's own draw centres (they differ from the cell centre for even-length vehicles).
        var def = vehicle.VehicleDef;
        var from = Ext_Vehicles.TrueCenter(vehicle.Position, vehicle.Rotation, def);
        var to = Ext_Vehicles.TrueCenter(edge.Exit, edge.ExitRotation, def);
        var now = Find.TickManager.TicksGame;
        var clip = new RampClip(vehicle.Map, edge, from, to, edge.EntryRotation,
            Mathf.Max(1f, vehicle.TicksPerMoveCardinal), now);
        RampClips.Start(vehicle, clip);
        RampDiagnostics.Add(ref RampDiagnostics.ClipsStarted);
        if (RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("entry " + vehicle.def.defName + " at " + edge.Entry + " for " + clip.EntryTicks
                + " ticks, emerge " + clip.EmergeTicks + " ticks, tick " + now);
        }
    }

    private static bool PollWait(VehiclePathFollower follower, VehiclePawn vehicle, OccupancyWait wait, int now)
    {
        if (now < wait.ExpiryTick && now < wait.NextCheckTick)
        {
            return false;
        }

        var table = RampRegistry.FreshTable(wait.Map);
        if (!table.TryGetEdge(vehicle.VehicleDef.size, wait.Entry, wait.Exit, out var edge))
        {
            Refuse(follower, vehicle, wait.Entry, wait.Exit, Refusal.Stale);
            return false;
        }

        wait.NextCheckTick = now + OccupancyCheckIntervalTicks;
        return LanesClear(follower, vehicle, wait.Map, in edge, wait);
    }

    private static bool LanesClear(VehiclePathFollower follower, VehiclePawn vehicle, Map map, in RampEdge edge,
        OccupancyWait wait = null)
    {
        var def = vehicle.VehicleDef;
        if (!RampClearance.IsClear(map, def, edge.EntryLane))
        {
            Refuse(follower, vehicle, edge.Entry, edge.Exit, Refusal.EntryBlocked);
            return false;
        }

        if (!RampClearance.IsClear(map, def, edge.ExitLane))
        {
            Refuse(follower, vehicle, edge.Entry, edge.Exit, Refusal.ExitBlocked);
            return false;
        }

        var occupied = OccupiedSide(map, vehicle, in edge);
        if (occupied.HasValue)
        {
            var now = Find.TickManager.TicksGame;
            if (wait == null)
            {
                s_waits.Add(vehicle, new OccupancyWait(follower, map, in edge, now));
                RampDiagnostics.Add(ref RampDiagnostics.OccupancyWaitsStarted);
                if (RampDiagnostics.Enabled)
                {
                    RampDiagnostics.Event("waiting " + vehicle.def.defName + " at " + edge.Entry
                        + " for occupied ramp lanes until tick " + (now + OccupancyWaitTicks));
                }
            }
            else if (now >= wait.ExpiryTick)
            {
                Refuse(follower, vehicle, edge.Entry, edge.Exit, occupied.Value);
            }

            return false;
        }

        if (wait != null)
        {
            s_waits.Remove(vehicle);
        }

        return true;
    }

    private static Refusal? OccupiedSide(Map map, VehiclePawn vehicle, in RampEdge edge)
    {
        // The vehicle holding the ramp leaves its entry lane by crossing, so a vehicle waiting in line for the same
        // ramp there (one that was already inside the mouth area) does not block it. The exit lane stays strict.
        var group = edge.Group;
        Func<VehiclePawn, bool> waiting = RampTraffic.IsHolder(vehicle)
            ? other => RampTraffic.IsQueuedFor(other, group)
            : null;
        if (RampClearance.IsOccupied(map, edge.EntryLane, vehicle, waiting))
        {
            return Refusal.EntryBlocked;
        }

        return RampClearance.IsOccupied(map, edge.ExitLane, vehicle) ? Refusal.ExitBlocked : null;
    }

    private static bool Cross(VehiclePathFollower follower, VehiclePawn vehicle, Map map, VehiclePath path,
        RampEdge edge)
    {
        var from = vehicle.Position;
        var oldRect = vehicle.OccupiedRect();
        path.ConsumeNextNode();
        vehicle.FullRotation = edge.ExitRotation;
        vehicle.Position = edge.Exit;
        // ResetToCurrentPosition sets the cell cost total to 1, which Vehicle Framework's next collision check would
        // read as a very high speed if it ran before the next cell is set up (after an emerge hold, or when the exit
        // is the destination). Keep the approach cell's real cost instead.
        var costTotal = follower.nextCellCostTotal;
        follower.ResetToCurrentPosition();
        follower.nextCellCostTotal = costTotal;
        // Vehicle Framework springs the drawn position toward the new cell; snap it so the vehicle is not drawn
        // sliding across the gap between floors.
        vehicle.DrawTracker.tweener.ResetTweenedPosToRoot();
        foreach (var cell in oldRect)
        {
            map.pathing.RecalculatePerceivedPathCostAt(cell);
        }

        foreach (var cell in vehicle.OccupiedRect())
        {
            map.pathing.RecalculatePerceivedPathCostAt(cell);
        }

        RampTraffic.NotifyCrossed(vehicle, in edge);
        // Same arrival fog reveal AASB performs for pawns crossing a link.
        ABFogReveal.RevealArrival(vehicle, edge.Exit);
        ABBandView.FollowTransit(vehicle);
        RampDiagnostics.Add(ref RampDiagnostics.Crossings);
        if (RampDiagnostics.Enabled)
        {
            RampDiagnostics.Event("crossed " + vehicle.def.defName + " " + from + " -> " + edge.Exit + " facing "
                + edge.ExitRotation.AsInt + " aboard " + vehicle.AllPawnsAboard.Count + " tick "
                + Find.TickManager.TicksGame);
        }

        if (RampLog.Verbose)
        {
            RampLog.Debug(vehicle + " crossed " + from + " -> " + edge.Exit);
        }

        // The exit can be the destination itself; the native step needs a following node.
        return path.NodesLeft > 1;
    }

    private static void Refuse(VehiclePathFollower follower, VehiclePawn vehicle, IntVec3 entry, IntVec3 exit,
        Refusal refusal)
    {
        s_waits.Remove(vehicle);
        if (refusal == Refusal.Stale)
        {
            RampDiagnostics.Add(ref RampDiagnostics.RefusedStale);
        }
        else
        {
            RampDiagnostics.Add(ref RampDiagnostics.RefusedBlocked);
            if (refusal == Refusal.EntryBlocked)
            {
                RampDiagnostics.Add(ref RampDiagnostics.RefusedEntryBlocked);
            }
        }

        var destination = follower.destination;
        var now = Find.TickManager.TicksGame;
        var window = s_refusals.GetOrCreateValue(vehicle);
        if (now - window.StartTick > RetryWindowTicks)
        {
            window.StartTick = now;
            window.Count = 0;
        }

        window.Count++;
        if (RampDiagnostics.Enabled || RampLog.Verbose)
        {
            var reason = refusal switch
            {
                Refusal.EntryBlocked => "entry side blocked at " + entry,
                Refusal.ExitBlocked => "far side blocked at " + exit,
                _ => "ramp crossing " + entry + " -> " + exit + " is no longer available",
            };
            RampLog.Debug(vehicle + " did not cross: " + reason);
            RampDiagnostics.Event("refused " + vehicle.def.defName + " at " + vehicle.Position + ": " + reason
                + " (attempt " + window.Count + ") tick " + now);
        }

        follower.StopDead();
        if (window.Count >= MaxRefusalsPerWindow || !destination.IsValid)
        {
            RampDiagnostics.Add(ref RampDiagnostics.JumpFailures);
            follower.PatherFailed();
            if (refusal != Refusal.Stale && vehicle.Faction == Faction.OfPlayer)
            {
                var message = refusal == Refusal.EntryBlocked
                    ? "AASBVehicleRamps_EntryBlocked"
                    : "AASBVehicleRamps_ExitBlocked";
                Messages.Message(message.Translate(vehicle.LabelShort), vehicle, MessageTypeDefOf.RejectInput, false);
            }

            return;
        }

        follower.StartPath(destination, PathEndMode.OnCell);
    }

    private enum Refusal
    {
        Stale,
        EntryBlocked,
        ExitBlocked,
    }

    private sealed class RefusalWindow
    {
        public int StartTick = int.MinValue / 2;

        public int Count;
    }

    private sealed class OccupancyWait
    {
        private readonly VehiclePath _path;
        private readonly Job _job;
        private readonly LocalTargetInfo _destination;

        public OccupancyWait(VehiclePathFollower follower, Map map, in RampEdge edge, int now)
        {
            _path = follower.curPath;
            _job = follower.vehicle.CurJob;
            _destination = follower.destination;
            Map = map;
            Entry = edge.Entry;
            Exit = edge.Exit;
            ExpiryTick = now + OccupancyWaitTicks;
            NextCheckTick = now + OccupancyCheckIntervalTicks;
        }

        public Map Map { get; }

        public IntVec3 Entry { get; }

        public IntVec3 Exit { get; }

        public int ExpiryTick { get; private set; }

        public int NextCheckTick { get; set; }

        public bool Matches(VehiclePathFollower follower)
        {
            var vehicle = follower.vehicle;
            var path = follower.curPath;
            return vehicle.Spawned && !vehicle.Dead && !vehicle.beached && vehicle.Drafted && follower.Moving
                && vehicle.Map == Map && vehicle.Position == Entry && follower.nextCell == Entry
                && follower.destination == _destination && vehicle.CurJob == _job && ReferenceEquals(path, _path)
                && path != null && path.NodesLeft >= 2 && path.Peek(1) == Exit;
        }

        public void Pause()
        {
            ExpiryTick++;
            NextCheckTick++;
        }
    }
}
