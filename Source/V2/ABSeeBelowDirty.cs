using HarmonyLib;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// Invalidation for the see-below resolve cache (ABBandMap.seeBelowVersion).
    ///
    /// The cached quantity is the GEOMETRIC descent answer of
    /// <c>ABBands.TryResolveVisibleBelow</c>, whose only mutable input is
    /// <c>TerrainGrid.TerrainAt</c> (ShowsBelow tests the terrain def and nothing else;
    /// band geometry is fixed for the life of the map). So the cache is valid exactly as
    /// long as no terrain answer changes.
    ///
    /// ⚠ RULE 16: COUNT THE WRITE PATHS. 1.6's TerrainAt consults THREE grids
    /// (tempGrid, then foundationGrid, then topGrid), written by seven public methods -
    /// SetTerrain, RemoveTopLayer, SetFoundation, RemoveFoundation, SetTempTerrain,
    /// RemoveTempTerrain, SetTerrainColor. Every one of them funnels through the private
    /// <c>DoTerrainChangedEffects</c>, so ONE postfix there covers all seven, present and
    /// future - patching the writers individually would be seven chances to miss the
    /// eighth. The single exception is <c>RemoveGravshipTerrainUnsafe</c>, which writes
    /// grids directly without the funnel; it gets its own postfix below.
    ///
    /// The bump is a per-map version increment (O(1)); nothing is cleared. Cells
    /// re-resolve lazily on next touch. On an unbanded map CompOf returns null and the
    /// postfix is a null test.
    /// </summary>
    [HarmonyPatch(typeof(TerrainGrid), "DoTerrainChangedEffects")]
    public static class Patch_TerrainGrid_ABSeeBelowDirty
    {
        private static void Postfix(Map ___map)
        {
            ABBands.CompOf(___map)?.DirtySeeBelowCache();
        }
    }

    /// <summary>See above: the one TerrainGrid writer that bypasses
    /// DoTerrainChangedEffects. Gravship moves rewrite terrain wholesale.</summary>
    /// <summary>
    /// TryResolveVisibleBelow stops on a level whose see-through cell holds a covering
    /// building (ABBands.CoveredAt, #21), so its per-terrain-version cache must also turn
    /// over when such a building appears or goes. Only covering buildings on see-through
    /// terrain matter, which in play is a wall built on, or removed from, a wall top.
    /// </summary>
    [HarmonyPatch(typeof(EdificeGrid), nameof(EdificeGrid.Register))]
    public static class Patch_EdificeGrid_ABSeeBelowDirty
    {
        internal static void Dirty(Map map, Building ed)
        {
            if (map == null || ed == null || ed.def == null || !ed.def.coversFloor)
            {
                return;
            }
            ABBandMap bands = ABBands.CompOf(map);
            bool bumped = false;
            foreach (IntVec3 c in ed.OccupiedRect())
            {
                if (!c.InBounds(map) || !ABBands.ShowsBelow(map.terrainGrid.TerrainAt(c)))
                {
                    continue;
                }
                if (!bumped)
                {
                    bands?.DirtySeeBelowCache();
                    bumped = true;
                }
                // Views from higher levels now stop (or no longer stop) on this cell, which
                // can move a lower multi-cell thing's print anchor (IsPrintAnchor) into a
                // NEIGHBOURING section. The upward mirror only dirties the cell's own
                // section, so the section that gains the anchor kept its old mesh and the
                // thing vanished until something else rebuilt it. Dirty the adjacent
                // sections on every level above; this only runs when a covering building
                // appears on or leaves a see-through cell.
                if (bands == null || !bands.Banded || map.mapDrawer == null)
                {
                    continue;
                }
                for (int b = bands.BandOf(c) + 1; b < bands.bandCount; b++)
                {
                    IntVec3 above = bands.Translate(c, b);
                    if (above.InBounds(map) && !bands.InGutter(above))
                    {
                        map.mapDrawer.MapMeshDirty(above, (ulong)ABDefOf.AB_BelowThings,
                            regenAdjacentCells: false, regenAdjacentSections: true);
                    }
                }
            }
        }

        private static void Postfix(Map ___map, Building ed)
        {
            Dirty(___map, ed);
        }
    }

    [HarmonyPatch(typeof(EdificeGrid), nameof(EdificeGrid.DeRegister))]
    public static class Patch_EdificeGrid_ABSeeBelowDirtyRemove
    {
        private static void Postfix(Map ___map, Building ed)
        {
            Patch_EdificeGrid_ABSeeBelowDirty.Dirty(___map, ed);
        }
    }

    [HarmonyPatch(typeof(TerrainGrid), nameof(TerrainGrid.RemoveGravshipTerrainUnsafe))]
    public static class Patch_TerrainGrid_ABSeeBelowDirtyGravship
    {
        private static void Postfix(Map ___map)
        {
            ABBands.CompOf(___map)?.DirtySeeBelowCache();
            ABPollutionCount.Invalidate(___map);
        }
    }
}
