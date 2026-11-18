using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AsAboveSoBelow;
internal static class ABOrbitRules
{
    internal static bool IsSpaceTile(PlanetTile tile)
    {
        if (!ModsConfig.OdysseyActive || !tile.Valid)
        {
            return false;
        }

        try
        {
            return tile.LayerDef?.isSpace ?? false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool InVacuum(Map map)
    {
        if (!ModsConfig.OdysseyActive || map == null)
        {
            return false;
        }

        try
        {
            return map.Biome?.inVacuum ?? false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool ProjectsUpperLevel(Map map, IntVec3 below)
    {
        return !InVacuum(map) || PlatformGround(map, below);
    }

    internal static bool LinkMayRise(Map map, CellRect footprint)
    {
        if (!InVacuum(map))
        {
            return true;
        }

        foreach (IntVec3 item in footprint)
        {
            if (!item.InBounds(map) || !PlatformGround(map, item))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool PlatformGround(Map map, IntVec3 c)
    {
        if (map == null || !c.InBounds(map))
        {
            return false;
        }

        TerrainGrid terrainGrid = map.terrainGrid;
        if (terrainGrid.FoundationAt(c) != null)
        {
            return false;
        }

        return IsPlatformDef(terrainGrid.TerrainAt(c)) || IsPlatformDef(terrainGrid.UnderTerrainAt(c));
    }

    internal static bool IsPlatformDef(TerrainDef t)
    {
        if (t == null || t.exposesToVacuum || t.isFoundation || t.IsSubstructure)
        {
            return false;
        }

        if (t.natural && !t.canEverTerraform && !t.layerable && HasAffordance(t, TerrainAffordanceDefOf.Walkable) && HasAffordance(t, TerrainAffordanceDefOf.Heavy))
        {
            return true;
        }

        return t.defName != null && t.defName.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasAffordance(TerrainDef t, TerrainAffordanceDef a)
    {
        return a != null && t.affordances != null && t.affordances.Contains(a);
    }

    internal static bool OrbitStartExpected()
    {
        try
        {
            GameInitData gameInitData = Find.GameInitData;
            if (gameInitData != null && gameInitData.startingTile.Valid)
            {
                return IsSpaceTile(gameInitData.startingTile);
            }

            Scenario scenario = Find.Scenario;
            if (scenario == null)
            {
                return false;
            }

            foreach (ScenPart allPart in scenario.AllParts)
            {
                if (allPart is ScenPart_ForcedMap { layerDef: not null } scenPart_ForcedMap && scenPart_ForcedMap.layerDef.isSpace)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
