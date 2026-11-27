using System.Collections.Generic;
using System.Threading;

namespace AASBVehicleRamps;

/// <summary>
/// Optional counters for tests and investigation. Disabled by default: normal play only pays the
/// <see cref="Enabled"/> check. Worker-thread counters use interlocked updates.
/// </summary>
public static class RampDiagnostics
{
    private const int MaxEvents = 64;
    private static readonly List<string> s_events = new List<string>();

    public static bool Enabled;

    /// <summary>
    /// Test-only control: when set, <see cref="CrossingPatch.HoldPrefix"/> lets Vehicle Framework's per-tick cell
    /// entry run during a hold, reproducing the collision behaviour the hold guard prevents. Never set by the mod.
    /// </summary>
    public static bool HoldGuardDisabled;

    /// <summary>Test-only reachability control; see <see cref="ReachTestMode"/>. Never set by the mod.</summary>
    public static ReachTestMode ReachMode;

    /// <summary>
    /// Test-only: when positive, ramp tables built afterwards treat every ramp as at most this many cells wide, so the
    /// width rule can be exercised with the available (narrower) vehicles. Never set by the mod.
    /// </summary>
    public static int RampWidthLimit;

    public static long JobsWithTable;
    public static long NodesExpanded;
    public static long PortalPushes;
    public static long EntryFitRejected;
    public static long ExitFitRejected;
    public static long Crossings;
    public static long RefusedBlocked;
    public static long RefusedEntryBlocked;
    public static long RefusedStale;
    public static long JumpFailures;
    public static long ReachOverrides;

    /// <summary>Regions dequeued by Vehicle Framework's region search (<c>ChunkSearch.CanReach</c>).</summary>
    public static long ReachRegionsVisited;

    /// <summary>Ramp exit regions offered to the region search as links.</summary>
    public static long ReachPortalLinks;

    /// <summary>Reached ramp entries whose entry lane was blocked.</summary>
    public static long ReachEntryRejected;

    /// <summary>Reached ramp entries whose exit lane was blocked.</summary>
    public static long ReachExitRejected;

    /// <summary>Reached ramp entries whose exit cell has no valid region for the vehicle.</summary>
    public static long ReachExitNoRegion;

    /// <summary>Native reachability calls made by the flood-fill-mode fallback chain.</summary>
    public static long ReachChainCalls;
    /// <summary>Crossings whose static lane clearance was evaluated by the flood-fill fallback.</summary>
    public static long ReachChainLaneChecks;
    public static long OccupancyWaitsStarted;
    public static long ClipsStarted;
    public static long ClipsCancelled;
    public static long HeldTicks;

    /// <summary>Ramp claims taken (<see cref="RampTraffic"/>).</summary>
    public static long ClaimsGranted;

    /// <summary>Vehicles that stopped to wait in line for a ramp.</summary>
    public static long ClaimHolds;

    /// <summary>Ticks vehicles spent waiting in line for a ramp.</summary>
    public static long ClaimHeldTicks;

    /// <summary>Claims taken back from a holder that made no progress.</summary>
    public static long ClaimsRevoked;

    /// <summary>Nodes popped by the most recent path job that ran on a map with ramps.</summary>
    public static long LastJobNodes;

    public static IReadOnlyList<string> Events
    {
        get
        {
            lock (s_events)
            {
                return s_events.ToArray();
            }
        }
    }

    public static void Add(ref long counter)
    {
        if (Enabled)
        {
            Interlocked.Increment(ref counter);
        }
    }

    public static void Event(string text)
    {
        if (!Enabled)
        {
            return;
        }

        lock (s_events)
        {
            if (s_events.Count < MaxEvents)
            {
                s_events.Add(text);
            }
        }
    }

    public static void Reset()
    {
        JobsWithTable = NodesExpanded = PortalPushes = EntryFitRejected = ExitFitRejected = Crossings = 0;
        RefusedEntryBlocked = RefusedBlocked = RefusedStale = JumpFailures = ReachOverrides = LastJobNodes = 0;
        ClipsStarted = ClipsCancelled = HeldTicks = 0;
        ReachRegionsVisited = ReachPortalLinks = ReachEntryRejected = ReachExitRejected = ReachExitNoRegion = 0;
        ReachChainCalls = ReachChainLaneChecks = OccupancyWaitsStarted = 0;
        ClaimsGranted = ClaimHolds = ClaimHeldTicks = ClaimsRevoked = 0;
        lock (s_events)
        {
            s_events.Clear();
        }
    }
}
