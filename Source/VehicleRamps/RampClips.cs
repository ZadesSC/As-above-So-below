using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using AsAboveSoBelow;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Active crossing animations. Keyed weakly by vehicle; written on the main thread, read by the draw path (which
/// may run its pre-draw phase on other threads). <see cref="AnyActive"/> keeps every per-frame and per-tick hook to a
/// single field read when no vehicle is animating. Clips are removed when they finish, on refusal or cancellation,
/// when the vehicle despawns, and when maps are removed or a game is loaded.
/// </summary>
public static class RampClips
{
    private static readonly ConditionalWeakTable<VehiclePawn, RampClip> s_clips =
        new ConditionalWeakTable<VehiclePawn, RampClip>();

    // Main thread only: the vehicles that own a clip, so map removal can clear just that map's clips.
    private static readonly List<VehiclePawn> s_owners = new List<VehiclePawn>();

    private static int s_active;

    public static bool AnyActive => Volatile.Read(ref s_active) > 0;

    /// <summary>AASB's "transit animation" setting; when off, crossings are immediate as before.</summary>
    public static bool AnimationsEnabled => ABMod.Settings == null || ABMod.Settings.transitAnim;

    public static bool TryGet(VehiclePawn vehicle, out RampClip clip)
    {
        clip = null;
        return vehicle != null && AnyActive && s_clips.TryGetValue(vehicle, out clip);
    }

    /// <summary>Current visual pose, or false when the vehicle is drawn normally.</summary>
    public static bool TryGetPose(VehiclePawn vehicle, out RampClipPose pose)
    {
        pose = default;
        var tickManager = Find.TickManager;
        return tickManager != null && TryGet(vehicle, out var clip) && clip.TryGetPose(tickManager.TicksGame, out pose);
    }

    /// <summary>Main thread only.</summary>
    public static void Start(VehiclePawn vehicle, RampClip clip)
    {
        if (!s_clips.TryGetValue(vehicle, out _))
        {
            s_owners.Add(vehicle);
            Interlocked.Increment(ref s_active);
        }

        s_clips.AddOrUpdate(vehicle, clip);
    }

    /// <summary>Main thread only.</summary>
    public static void Clear(VehiclePawn vehicle)
    {
        if (vehicle != null && s_clips.Remove(vehicle))
        {
            s_owners.Remove(vehicle);
            Interlocked.Decrement(ref s_active);
        }
    }

    /// <summary>Main thread only: map removal clears the clips started on that map.</summary>
    public static void ClearMap(Map map)
    {
        for (var i = s_owners.Count - 1; i >= 0; i--)
        {
            var vehicle = s_owners[i];
            if (!s_clips.TryGetValue(vehicle, out var clip) || clip.Map == map)
            {
                Clear(vehicle);
                s_owners.Remove(vehicle);
            }
        }
    }

    /// <summary>Main thread only: game load and full world clears.</summary>
    public static void ClearAll()
    {
        s_clips.Clear();
        s_owners.Clear();
        Volatile.Write(ref s_active, 0);
    }
}
