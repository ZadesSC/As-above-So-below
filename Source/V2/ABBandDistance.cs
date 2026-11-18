using System.Collections.Generic;
using Verse;

namespace AsAboveSoBelow;

internal static class ABBandDistance
{
    // One search can score many targets on the same remote island. Reuse the
    // trip to its entrance until either the search ends or connectivity changes.
    internal sealed class IslandMemo
    {
        private struct Route
        {
            public bool found;
            public IntVec3 anchor;
            public float distanceToEntrance;
        }

        private readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();
        private Map map;
        private Pawn pawn;
        private IntVec3 root;
        private bool forbidAware;
        private int version = -1;
        public bool Active => map != null;

        public void Begin(Map map, Pawn pawn, IntVec3 root)
        {
            this.map = map;
            this.pawn = pawn;
            this.root = root;
            forbidAware = ABBandComponents.RespectsForbiddenDoors(pawn);
            version = ABBandComponents.Version;
            routes.Clear();
        }

        public void End()
        {
            map = null;
            pawn = null;
            routes.Clear();
        }

        public bool TryDistance(IntVec3 cell, bool manhattan, out float length)
        {
            length = 0f;
            if (map == null || !cell.InBounds(map) || ABBands.SameBand(map, root, cell))
            {
                return false;
            }

            if (version != ABBandComponents.Version)
            {
                routes.Clear();
                version = ABBandComponents.Version;
            }

            int island = ABBandComponents.ComponentOf(map, cell, forbidAware);
            if (island < 0)
            {
                foreach (IntVec3 offset in GenAdj.AdjacentCells)
                {
                    IntVec3 neighbor = cell + offset;
                    if (neighbor.InBounds(map) && ABBands.SameBand(map, neighbor, cell))
                    {
                        island = ABBandComponents.ComponentOf(map, neighbor, forbidAware);
                        if (island >= 0)
                        {
                            break;
                        }
                    }
                }

                if (island < 0)
                {
                    return false;
                }
            }

            if (!routes.TryGetValue(island, out Route route))
            {
                route.found = ABWormhole.TryEstimateTrip(map, root, cell, pawn,
                    out _, out route.anchor, out route.distanceToEntrance) && route.anchor.IsValid;
                routes[island] = route;
            }

            if (!route.found)
            {
                return false;
            }

            IntVec3 lastLeg = route.anchor - cell;
            length = route.distanceToEntrance + (manhattan ? lastLeg.LengthManhattan : lastLeg.LengthHorizontal);
            return true;
        }
    }

    public static bool TryCrossLevel(Map map, Pawn pawn, IntVec3 from, IntVec3 to, out float length)
    {
        length = 0f;
        if (map == null || !ABBands.Banded(map) || ABWormhole.PairCount(map) == 0 || !from.InBounds(map) || !to.InBounds(map) || ABBands.SameBand(map, from, to))
        {
            return false;
        }

        return ABWormhole.TryEstimateTrip(map, from, to, pawn, out length);
    }
}
