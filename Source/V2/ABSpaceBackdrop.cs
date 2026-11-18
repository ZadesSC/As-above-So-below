using Verse;

namespace AsAboveSoBelow;
internal static class ABSpaceBackdrop
{
    internal static bool HasWorldBackdrop(Map map)
    {
        return map != null && map.generatorDef != null && !map.DrawMapClippers;
    }

    internal static bool IsVoid(Map map, ABBandMap bands, IntVec3 cell)
    {
        TerrainDef terrainDef = map.terrainGrid.TerrainAt(cell);
        if (terrainDef == null)
        {
            return false;
        }

        if (terrainDef.exposesToVacuum)
        {
            return true;
        }

        if (!ABBands.ShowsBelow(terrainDef) || bands == null || !bands.Banded)
        {
            return false;
        }

        if (bands.BandOf(cell) <= 0 || bands.InGutter(cell))
        {
            return true;
        }

        if (!ABBands.TryResolveVisibleBelow(map, bands, cell, out var below, out var _))
        {
            return true;
        }

        return map.terrainGrid.TerrainAt(below)?.exposesToVacuum ?? true;
    }
}
