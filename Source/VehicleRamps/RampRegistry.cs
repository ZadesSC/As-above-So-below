using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Owns the per-map ramp holders and the path-grid lookup used by path jobs. Entries are removed when a map is
/// deinitialised or all maps are cleared, so the lookup holds at most one binding per live map and vehicle def.
/// </summary>
public static class RampRegistry
{
    private static readonly ConcurrentDictionary<Map, MapRamps> s_maps = new ConcurrentDictionary<Map, MapRamps>();
    private static readonly ConcurrentDictionary<nint, GridBinding> s_grids =
        new ConcurrentDictionary<nint, GridBinding>();

    public static int MapCount => s_maps.Count;

    public static int GridCount => s_grids.Count;

    /// <summary>Main thread only.</summary>
    public static MapRamps For(Map map) => s_maps.GetOrAdd(map, m => new MapRamps(m));

    /// <summary>Main thread only: the current table for the map, rebuilt if ramps changed.</summary>
    public static RampTable FreshTable(Map map) => map == null ? RampTable.Empty : For(map).Fresh();

    /// <summary>
    /// Main thread only: a ramp on the map was built, removed, forbidden or unforbidden. Marks the table dirty and
    /// clears Vehicle Framework's cached reachability answers, which may be stale "unreachable" results. The table
    /// itself is rebuilt lazily.
    /// </summary>
    public static void Invalidate(Map map)
    {
        if (map == null)
        {
            return;
        }

        For(map).MarkDirty();
        ReachCaches.Clear(map);
    }

    /// <summary>
    /// Main thread only: refreshes the table of a map this registry already tracks (one dictionary lookup, then a
    /// rebuild only if ramps or AASB's floor count changed). Maps with no entry never had a table to go stale.
    /// </summary>
    public static void RefreshIfTracked(Map map)
    {
        if (map != null && s_maps.TryGetValue(map, out var ramps))
        {
            ramps.Fresh();
        }
    }

    internal static bool HasPendingOrUsableRamps(Map map) =>
        map != null && s_maps.TryGetValue(map, out var ramps) && ramps.HasPendingOrUsableRamps;

    /// <summary>Any thread: the binding registered for a path-grid snapshot buffer.</summary>
    public static GridBinding LookupGrid(nint key) => s_grids.TryGetValue(key, out var binding) ? binding : null;

    /// <summary>
    /// Main thread only. Called before each vehicle path request; refreshes the table and (re)binds the request's
    /// path-grid buffer. Allocates only when a map/def pair is seen for the first time or its buffer changed.
    /// </summary>
    public static void Register(Map map, VehicleDef def, nint key)
    {
        if (map == null || key == 0)
        {
            return;
        }

        var ramps = For(map);
        ramps.Fresh();
        var defIndex = def.DefIndex;
        var binding = ramps.BindingFor(defIndex);
        if (binding != null && binding.Key == key)
        {
            return;
        }

        if (binding != null)
        {
            s_grids.TryRemove(new KeyValuePair<nint, GridBinding>(binding.Key, binding));
        }

        binding = new GridBinding(ramps, key, def);
        ramps.SetBinding(defIndex, binding);
        s_grids[key] = binding;
    }

    public static void Remove(Map map)
    {
        RegionPortalPatch.Forget();
        if (map == null || !s_maps.TryRemove(map, out var ramps))
        {
            return;
        }

        foreach (var binding in ramps.Bindings)
        {
            if (binding != null)
            {
                // Only removes the entry if it still belongs to this map; a reused buffer address may already
                // be bound to another map.
                s_grids.TryRemove(new KeyValuePair<nint, GridBinding>(binding.Key, binding));
            }
        }
    }

    public static void Clear()
    {
        RegionPortalPatch.Forget();
        s_maps.Clear();
        s_grids.Clear();
    }
}
