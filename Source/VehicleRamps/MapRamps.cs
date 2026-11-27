using System.Threading;
using AsAboveSoBelow;
using Verse;

namespace AASBVehicleRamps;

/// <summary>
/// Per-map holder of the current <see cref="RampTable"/>. The table reference is published with a volatile write
/// and read by path jobs; rebuilds happen lazily on the main thread after <see cref="MarkDirty"/>.
/// </summary>
public sealed class MapRamps
{
    private volatile RampTable _table = RampTable.Empty;
    private volatile bool _dirty = true;
    private GridBinding[] _bindingsByDefIndex = new GridBinding[0];
    private static int s_version;

    public MapRamps(Map map)
    {
        Map = map;
    }

    public Map Map { get; }

    /// <summary>
    /// Latest published table; safe from any thread, possibly stale until the next main-thread refresh.
    /// </summary>
    public RampTable Published => _table;

    /// <summary>True once any ramp edge was ever published for this map.</summary>
    public bool EverHadRamps { get; private set; }

    internal bool HasPendingOrUsableRamps => _dirty || !_table.IsEmpty;

    public GridBinding[] Bindings => _bindingsByDefIndex;

    public void MarkDirty() => _dirty = true;

    /// <summary>Main thread only: rebuilds the table if ramps changed since the last build.</summary>
    public RampTable Fresh()
    {
        // Opening a level through ordinary AASB stairs adds a floor; the table's floor rows would be stale.
        var bands = ABBands.CompOf(Map);
        if (!_dirty && bands != null && bands.bandCount != _table.BandCount && (!_table.IsEmpty || EverHadRamps))
        {
            _dirty = true;
        }

        if (_dirty)
        {
            var table = RampTable.Build(Map, Interlocked.Increment(ref s_version));
            _table = table;
            // Ramp changes clear the caches when they happen (RampRegistry.Invalidate); this also covers a rebuild
            // forced by a new AASB floor. LifecyclePatches.ReachabilityQueried runs this before VF's cache lookup.
            ReachCaches.Clear(Map);
            _dirty = false;
            if (!table.IsEmpty)
            {
                EverHadRamps = true;
            }

            if (RampLog.Verbose)
            {
                RampLog.Debug("table rebuilt for map " + Map.uniqueID + ": " + table.EdgeCount + " edges, "
                    + table.Groups.Count + " ramp directions, " + table.SkippedForbidden + " forbidden pairs skipped");
            }
        }

        return _table;
    }

    /// <summary>Main thread only.</summary>
    public GridBinding BindingFor(int defIndex)
    {
        var bindings = _bindingsByDefIndex;
        return defIndex >= 0 && defIndex < bindings.Length ? bindings[defIndex] : null;
    }

    /// <summary>Main thread only.</summary>
    public void SetBinding(int defIndex, GridBinding binding)
    {
        if (defIndex >= _bindingsByDefIndex.Length)
        {
            var grown = new GridBinding[defIndex + 1];
            _bindingsByDefIndex.CopyTo(grown, 0);
            _bindingsByDefIndex = grown;
        }

        _bindingsByDefIndex[defIndex] = binding;
    }
}
