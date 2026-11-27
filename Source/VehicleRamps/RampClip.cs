using System;
using UnityEngine;
using SmashTools;
using Verse;

namespace AASBVehicleRamps;

public enum RampClipPhase
{
    Entry,
    Emerge,
}

/// <summary>Visual-only pose of a vehicle during a ramp crossing animation.</summary>
public readonly struct RampClipPose
{
    public RampClipPose(Vector3 offset, float scale, Rot8 facing)
    {
        Offset = offset;
        Scale = scale;
        Facing = facing;
    }

    /// <summary>World offset added to the vehicle's draw position (x and z only).</summary>
    public Vector3 Offset { get; }

    /// <summary>Uniform horizontal scale about the drawn vehicle centre.</summary>
    public float Scale { get; }

    /// <summary>Drawn facing, or <see cref="Rot8.Invalid"/> to keep the vehicle's own rotation.</summary>
    public Rot8 Facing { get; }
}

/// <summary>
/// One vehicle's crossing animation, modelled on AASB's <c>ABStairAnim</c> clip. During
/// <see cref="RampClipPhase.Entry"/> the vehicle is held on its approach cell while it is drawn driving onto the ramp
/// toward the ramp centre, rising (climbing) or sinking into the recess (descending) and shrinking to 0.72. After the
/// crossing commits, during <see cref="RampClipPhase.Emerge"/> it is held on the exit cell while drawn driving out
/// from the far ramp centre, growing from 0.82 back to 1 with an ease-out. Clip state is never saved. Fields are
/// written on the main thread; the draw thread only reads them, and a torn read can at worst misplace one frame.
/// </summary>
public sealed class RampClip
{
    private const float EntryScale = 0.28f;
    private const float EmergeScale = 0.18f;
    private const float Rise = 0.3f;
    private const float Sink = -0.35f;
    private const float Bob = 0.025f;

    // A clip whose holder stopped ticking (for example a pather that is no longer driven) stops drawing after this.
    private const int StaleTicks = 180;

    /// <param name="entryDrawCenter">Vehicle Framework's draw centre of the vehicle on its approach cell.</param>
    /// <param name="exitDrawCenter">
    /// Vehicle Framework's draw centre of the vehicle on the exit cell, facing out.
    /// </param>
    public RampClip(Map map, RampEdge edge, Vector3 entryDrawCenter, Vector3 exitDrawCenter, Rot4 entryFacing,
        float ticksPerCell, int startTick)
    {
        Map = map;
        Edge = edge;
        EntryFacing = entryFacing;
        var travel = edge.EntryRampCenter - entryDrawCenter;
        Travel = new Vector3(travel.x, 0f, travel.z);
        var emerge = edge.ExitRampCenter - exitDrawCenter;
        EmergeOffset = new Vector3(emerge.x, 0f, emerge.z);
        EntryTicks = Mathf.Clamp(Mathf.RoundToInt(Travel.magnitude * ticksPerCell), 60, 120);
        EmergeTicks = Mathf.Clamp(Mathf.RoundToInt(EmergeOffset.magnitude * ticksPerCell * 0.6f), 30, 75);
        Phase = RampClipPhase.Entry;
        PhaseStart = startTick;
    }

    /// <summary>The map the clip was started on; map removal clears only that map's clips.</summary>
    public Map Map { get; }

    public RampEdge Edge { get; }

    public Rot4 EntryFacing { get; }

    public Vector3 Travel { get; }

    public Vector3 EmergeOffset { get; }

    public int EntryTicks { get; }

    public int EmergeTicks { get; }

    public RampClipPhase Phase { get; private set; }

    public int PhaseStart { get; private set; }

    public int Age(int now) => now - PhaseStart;

    public bool EntryDone(int now) => Phase == RampClipPhase.Entry && Age(now) >= EntryTicks;

    public bool EmergeDone(int now) => Phase == RampClipPhase.Emerge && Age(now) >= EmergeTicks;

    /// <summary>Pauses the clip for one tick (the vehicle is stunned and its pather does not advance).</summary>
    public void Pause() => PhaseStart++;

    public void BeginEmerge(int now)
    {
        PhaseStart = now;
        Phase = RampClipPhase.Emerge;
    }

    public bool TryGetPose(int now, out RampClipPose pose)
    {
        pose = default;
        var age = Age(now);
        if (age < 0)
        {
            return false;
        }

        if (Phase == RampClipPhase.Entry)
        {
            if (age > EntryTicks + StaleTicks)
            {
                return false;
            }

            var s = Smooth(Mathf.Clamp01((float)age / EntryTicks));
            var bob = Mathf.Abs(Mathf.Sin(age * (float)Math.PI / 12f)) * Bob * (1f - s);
            var vertical = (Edge.Climbs ? Rise : Sink) * s + bob;
            pose = new RampClipPose(Travel * s + new Vector3(0f, 0f, vertical), 1f - EntryScale * s, EntryFacing);
            return true;
        }

        if (age >= EmergeTicks)
        {
            return false;
        }

        var r = 1f - Mathf.Clamp01((float)age / EmergeTicks);
        r *= r;
        var start = Edge.Climbs ? Sink : Rise;
        pose = new RampClipPose(EmergeOffset * r + new Vector3(0f, 0f, start * r), 1f - EmergeScale * r, Rot8.Invalid);
        return true;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
