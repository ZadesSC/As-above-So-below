#if RIMWORLD_17
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow
{
    internal static class ABPollutionCount
    {
        private sealed class CountCache
        {
            private int count;
            private int countedAt = -2500;
            private bool dirty = true;

            public CountCache(Map map)
            {
                map.events.TerrainChanged += _ => dirty = true;
                map.events.BuildingSpawned += InvalidateForBuilding;
                map.events.BuildingDespawned += InvalidateForBuilding;
            }

            private void InvalidateForBuilding(Building building)
            {
                if (building.def.IsEdifice() && !building.def.EverPollutable)
                {
                    dirty = true;
                }
            }

            public int Get(Map map)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (dirty || tick < countedAt || tick - countedAt >= 2500)
                {
                    count = 0;
                    foreach (IntVec3 cell in CellRect.WholeMap(map))
                    {
                        if (map.pollutionGrid.EverPollutable(cell))
                        {
                            count++;
                        }
                    }
                    countedAt = tick;
                    dirty = false;
                }
                return count;
            }
        }

        private static readonly ConditionalWeakTable<Map, CountCache> Counts =
            new ConditionalWeakTable<Map, CountCache>();

        internal static int Get(Map map) => Counts.GetValue(map, key => new CountCache(key)).Get(map);

        internal static void ClearPollutedAir(Map map)
        {
            PollutionGrid grid = map.pollutionGrid;
            if (grid == null || grid.TotalPollution == 0)
            {
                return;
            }

            foreach (IntVec3 cell in CellRect.WholeMap(map))
            {
                if (grid.IsPolluted(cell) && map.terrainGrid.TerrainAt(cell) == ABDefOf.AB_OpenAir)
                {
                    grid.SetPolluted(cell, false, true);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PollutionGrid), nameof(PollutionGrid.TotalPollutionPercent), MethodType.Getter)]
    internal static class Patch_PollutionGrid_ABCachedPollutableCount
    {
        private static bool Prefix(PollutionGrid __instance, Map ___map, ref float __result)
        {
            if (!ABBands.Banded(___map))
            {
                return true;
            }

            int count = ABPollutionCount.Get(___map);
            __result = count == 0 ? 0f : (float)__instance.TotalPollution / count;
            return false;
        }
    }
}
#endif
