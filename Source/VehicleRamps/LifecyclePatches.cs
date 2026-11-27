using AsAboveSoBelow;
using RimWorld;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Keeps the per-map tables current and the registries bounded. Ramp spawn, despawn and forbid changes mark the
/// map dirty and clear Vehicle Framework's reachability caches; the table is rebuilt lazily on the main thread before
/// the next vehicle path request, reachability query or crossing. Map deinitialisation and full world clears drop the
/// map's entries.
/// </summary>
public static class LifecyclePatches
{
    public static void RampSpawned(Building_ABStairs2 __instance, Map map)
    {
        if (RampDefs.IsRamp(__instance.def))
        {
            RampRegistry.Invalidate(map);
        }
    }

    public static void RampDespawning(Building_ABStairs2 __instance, out Map __state)
    {
        __state = __instance.Map;
    }

    public static void RampDespawned(Building_ABStairs2 __instance, Map __state)
    {
        if (RampDefs.IsRamp(__instance.def))
        {
            RampRegistry.Invalidate(__state);
        }
    }

    public static void ForbiddenChanged(CompForbiddable __instance)
    {
        var parent = __instance.parent;
        if (parent != null && parent.Spawned && RampDefs.IsRamp(parent.def))
        {
            RampRegistry.Invalidate(parent.Map);
        }
    }

    public static void MapDeinitialising(Map map)
    {
        RampRegistry.Remove(map);
        RampClips.ClearMap(map);
        CrossingPatch.ClearWaits(map);
    }

    public static void AllMapsClearing()
    {
        RampRegistry.Clear();
        RampClips.ClearAll();
        CrossingPatch.ClearAllWaits();
    }

    /// <summary>
    /// After each vehicle pather tick: ends a clip whose vehicle despawned, died, was undrafted or stopped during
    /// entry, and expires a finished emerge clip that no longer reaches the crossing hold (for example on arrival).
    /// While the vehicle is stunned its pather does not advance, so the clip is paused rather than left to run out.
    /// </summary>
    public static void PatherTicked(VehiclePathFollower __instance)
    {
        CrossingPatch.UpdateWait(__instance);
        if (!RampClips.AnyActive || !RampClips.TryGet(__instance.vehicle, out var clip))
        {
            return;
        }

        var vehicle = __instance.vehicle;
        if (!vehicle.Spawned || vehicle.Dead)
        {
            RampClips.Clear(vehicle);
        }
        else if (clip.Phase == RampClipPhase.Entry && (!vehicle.Drafted || !__instance.Moving))
        {
            RampClips.Clear(vehicle);
            RampDiagnostics.Add(ref RampDiagnostics.ClipsCancelled);
        }
        else if (vehicle.stances?.stunner != null && vehicle.stances.stunner.Stunned)
        {
            clip.Pause();
        }
        else if (clip.EmergeDone(Find.TickManager.TicksGame))
        {
            RampClips.Clear(vehicle);
        }
    }

    public static void VehicleDespawning(VehiclePawn __instance)
    {
        RampClips.Clear(__instance);
        CrossingPatch.ClearWait(__instance);
    }

    /// <summary>
    /// Before each <c>VehicleReachability.CanReachVehicle</c> (main thread): rebuild a stale table now, so a rebuild's
    /// reachability-cache clear (for example after AASB adds a floor) happens before VF consults its cache.
    /// </summary>
    public static void ReachabilityQueried(VehicleReachability __instance)
    {
        if (UnityData.IsInMainThread)
        {
            RampRegistry.RefreshIfTracked(__instance.map);
        }
    }

    /// <summary>
    /// Before each Vehicle Framework path request (main thread): refresh the map's table and bind the request's
    /// path-grid snapshot buffer so the worker-side search finds the table.
    /// </summary>
    public static unsafe void PathRequested(PathFinderManager __instance, PathSettings settings)
    {
        var def = settings.vehicleDef;
        var finders = __instance.pathFinders;
        if (def == null || finders == null || !UnityData.IsInMainThread)
        {
            return;
        }

        var index = def.DefIndex;
        if (index < 0 || index >= finders.Length || finders[index] == null)
        {
            return;
        }

        var key = (nint)finders[index].pathFinder.snapshot.GetUnsafeReadOnlyPtrInternal();
        RampRegistry.Register(__instance.map, def, key);
    }
}
