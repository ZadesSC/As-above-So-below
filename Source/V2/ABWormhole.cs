using System.Text;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 SPIKE - the load-bearing primitive.
    ///
    /// Joins two spatially distant areas of ONE map into a single connectivity
    /// graph by handing both ends' regions a SHARED synthetic RegionLink.
    ///
    /// Why this works (verified against 1.6 source):
    ///  - Region.Neighbors / NeighborsOfSameType iterate region.links and return
    ///    link.regions[i] with ZERO spatial validation. RegionLink.span is only used
    ///    for hash-dedup in RegionLinkDatabase and for RegionCostCalculator distance
    ///    estimates. Two regions sharing a link ARE adjacent as far as RimWorld cares.
    ///  - Reachability.CanReach is managed region-BFS, so it starts returning true
    ///    across the wormhole with no further patching - and that transitively fixes
    ///    ClosestThingReachable, RegionTraverser, storage search and every
    ///    WorkGiver_Scanner. That transitive win is the whole thesis of V2.
    ///  - Both ends are RegionType.Portal (the anchor is a Building_Door subclass, and
    ///    GetExpectedRegionType returns Portal for any door cell). Portal fails
    ///    RegionAndRoomUpdater.ShouldBeInTheSameRoom and AllowsMultipleRegionsPerDistrict,
    ///    so the link conducts connectivity WITHOUT merging rooms, temperature or vacuum.
    ///
    /// Three traps this class exists to handle:
    ///  1. RegionDirtyer sets reg.valid=false, deregisters every link and CLEARS
    ///     reg.links on any region rebuild - synthetic links are silently wiped, so
    ///     they must be re-armed after every rebuild (see the postfix below).
    ///  2. Do NOT mint the link via map.regionLinkDatabase.LinkFrom(span): that dedups
    ///     by span.UniqueHashCode() and would collide with a genuine edge link. We
    ///     construct RegionLink directly and never register it in the database.
    ///     (RegionLink.Deregister -> Notify_LinkHasNoRegions -> links.Remove(hash) is a
    ///     safe no-op for a key that was never added.)
    ///  3. Reachability saying "yes" does NOT produce a path - 1.6's pathfinder is
    ///     jobified and cannot traverse the link. ABWormholePather handles that.
    /// </summary>
    public static class ABWormhole
    {
        /// <summary>Ends are typed as Building_Door because that is what makes the cell a
        /// RegionType.Portal region - the property the whole mechanism depends on. Both
        /// the spike anchor and the V2 stairwell derive from it.</summary>
        private sealed class Pair
        {
            public Building_Door a;
            public Building_Door b;

            /// <summary>ONE LINK PER CELL PAIR, not one per building.
            ///
            /// RegionTypeUtility.IsOneCellRegion(Portal) is true: every door cell becomes its
            /// OWN one-cell Portal region. A 2x1 stairwell is therefore two Portal regions at
            /// each end, and linking only the building's Position cell leaves the other cell
            /// conducting nothing - a pawn that happens to walk into that half finds no
            /// connection, while the identical-looking cell beside it works. That is exactly
            /// the "sometimes the stairs work" symptom, and it gets worse the larger the
            /// footprint, so it had to be fixed before any multi-cell variant could exist.</summary>
            public readonly List<RegionLink> links = new List<RegionLink>();
        }

        /// <summary>Pairs per map, keyed by the Map OBJECT, not map.uniqueID.
        ///
        /// This was a Dictionary&lt;int, List&lt;Pair&gt;&gt; keyed by uniqueID, and that broke
        /// every reload: loading a save neither clears statics nor fires MapRemoved for the
        /// torn-down session, and the loaded map carries the SAME uniqueID as its dead
        /// predecessor - so ListFor handed the fresh map a list still full of stale Pairs
        /// whose buildings belong to unspawned objects on a dead Map. uniqueIDs also restart
        /// at 0 for every NEW game, so a different colony could inherit them too. Identical
        /// to the viewBand lesson: per-map state must be keyed by the Map object. A CWT also
        /// lets dead maps' lists be collected instead of pinned forever.</summary>
        private static readonly ConditionalWeakTable<Map, List<Pair>> byMap =
            new ConditionalWeakTable<Map, List<Pair>>();

        /// <summary>Re-entrancy latch: rearming must never trigger a region rebuild
        /// (we only use NoRebuild accessors, but the latch makes that guarantee
        /// explicit and cheap to audit).</summary>
        private static bool rearming;

        public static int PairCount(Map map)
        {
            return map != null && byMap.TryGetValue(map, out List<Pair> l) ? l.Count : 0;
        }

        /// <summary>True when the cell is within <paramref name="radius"/> of either end of any
        /// wormhole on this map. Used to scope the stuck watchdog to stairwell traffic instead
        /// of every pawn on the map.</summary>
        public static bool NearAnyAnchor(Map map, IntVec3 cell, int radius)
        {
            if (map == null || !byMap.TryGetValue(map, out List<Pair> list))
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Pair p = list[i];
                if (p.a != null && p.a.Spawned && cell.InHorDistOf(p.a.Position, radius))
                {
                    return true;
                }
                if (p.b != null && p.b.Spawned && cell.InHorDistOf(p.b.Position, radius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Full state of every wormhole on this map: whether each end resolved to a
        /// Portal region, whether the synthetic link is actually present in BOTH regions'
        /// link lists, and whether vanilla reachability agrees the ends connect.
        ///
        /// The last line is the one that matters: if CanReach is false the link is not
        /// armed, and every cross-band order will be rejected before it starts - a pawn told
        /// to use the stairs simply stands there.</summary>
        public static string DebugDump(Map map)
        {
            if (map == null)
            {
                return "no map";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!byMap.TryGetValue(map, out List<Pair> list) || list.Count == 0)
            {
                sb.AppendLine("NO WORMHOLE PAIRS REGISTERED on this map.");
                return sb.ToString();
            }
            sb.AppendLine("wormhole pairs: " + list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Pair p = list[i];
                sb.AppendLine("  [" + i + "] a=" + (p.a != null ? p.a.Position.ToString() : "null")
                    + " spawned=" + (p.a != null && p.a.Spawned)
                    + "  b=" + (p.b != null ? p.b.Position.ToString() : "null")
                    + " spawned=" + (p.b != null && p.b.Spawned));
                if (p.a == null || p.b == null || !p.a.Spawned || !p.b.Spawned)
                {
                    continue;
                }
                Region ra = map.regionGrid.GetValidRegionAt_NoRebuild(p.a.Position);
                Region rb = map.regionGrid.GetValidRegionAt_NoRebuild(p.b.Position);
                sb.AppendLine("      regionA=" + (ra != null ? ra.type.ToString() : "NULL")
                    + "  regionB=" + (rb != null ? rb.type.ToString() : "NULL")
                    + "   (both MUST be Portal)");
                // Reports links armed vs cells occupied. A multi-cell stairwell must have
                // ONE link per cell pair; "3/4" means a quarter of the footprint conducts
                // nothing and pawns entering that cell will find no connection.
                int cells = p.a.OccupiedRect().Area;
                int live = 0;
                for (int li = 0; li < p.links.Count; li++)
                {
                    RegionLink l = p.links[li];
                    if (l != null && l.RegionA != null && l.RegionB != null
                        && l.RegionA.links.Contains(l) && l.RegionB.links.Contains(l))
                    {
                        live++;
                    }
                }
                sb.AppendLine("      links armed = " + live + "/" + cells + " cells"
                    + (live == cells ? "" : "   <-- PARTIAL, some cells conduct nothing"));
                bool reach = map.reachability.CanReach(p.a.Position, p.b.Position,
                    PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly));
                sb.AppendLine("      CanReach across = " + reach + "   (MUST be true)");
                sb.AppendLine("      isDoorA=" + (p.a.Position.GetDoor(map) != null)
                    + " isDoorB=" + (p.b.Position.GetDoor(map) != null));
            }
            return sb.ToString();
        }

        public static void Link(Building_Door a, Building_Door b)
        {
            if (a == null || b == null || !a.Spawned || !b.Spawned)
            {
                return;
            }

            if (a.Map == null || a.Map != b.Map)
            {
                Log.Warning("[As above, So below] V2: refusing to link wormhole ends on different maps.");
                return;
            }

            List<Pair> list = ListFor(a.Map);
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].a == a && list[i].b == b) || (list[i].a == b && list[i].b == a))
                {
                    return;
                }
            }

            list.Add(new Pair { a = a, b = b });
            RearmAll(a.Map);
        }

        public static void Unlink(Building_Door anchor, Map map)
        {
            if (anchor == null || map == null || !byMap.TryGetValue(map, out List<Pair> list))
            {
                return;
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pair p = list[i];
                if (p.a != anchor && p.b != anchor)
                {
                    continue;
                }
                TearDown(p);
                list.RemoveAt(i);
            }
            map.reachability.ClearCache();
        }

        private static List<Pair> ListFor(Map map)
        {
            if (!byMap.TryGetValue(map, out List<Pair> list))
            {
                list = new List<Pair>();
                byMap.Add(map, list);
            }
            return list;
        }

        private static void TearDown(Pair p)
        {
            for (int i = 0; i < p.links.Count; i++)
            {
                RegionLink l = p.links[i];
                if (l == null)
                {
                    continue;
                }
                l.RegionA?.links.Remove(l);
                l.RegionB?.links.Remove(l);
            }
            p.links.Clear();
        }

        /// <summary>Re-create every synthetic link on this map whose regions have been
        /// rebuilt out from under it. Cheap and idempotent: a still-armed pair costs
        /// two grid lookups and two list scans.</summary>
        public static void RearmAll(Map map)
        {
            if (map == null || rearming || !byMap.TryGetValue(map, out List<Pair> list) || list.Count == 0)
            {
                return;
            }
            rearming = true;
            try
            {
                // Prune pairs whose ends are gone. Normal teardown goes through DeSpawn ->
                // Unlink, but map teardown and load-ordering edge cases can strand a pair,
                // and a stranded pair silently fails every rearm from then on.
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Pair p = list[i];
                    if (p.a == null || p.b == null || !p.a.Spawned || !p.b.Spawned
                        || p.a.Map != map || p.b.Map != map)
                    {
                        TearDown(p);
                        list.RemoveAt(i);
                    }
                }
                bool changed = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (Rearm(map, list[i]))
                    {
                        changed = true;
                    }
                }
                if (changed)
                {
                    // Reachability memoizes region-to-region answers; a new edge in the
                    // graph invalidates them.
                    map.reachability.ClearCache();
                }
            }
            catch (Exception e)
            {
                Log.Error(ABLog.Tag + " V2 spike: wormhole rearm threw: " + e);
            }
            finally
            {
                rearming = false;
            }
        }

        /// <summary>The cell pairs joining two anchors, matched by position WITHIN each
        /// building's own rect so rotation and multi-cell footprints line up. Returns false
        /// when the two ends disagree on shape, which would otherwise silently link the wrong
        /// cells together.</summary>
        private static bool TryCellPairs(Pair p, List<KeyValuePair<IntVec3, IntVec3>> into)
        {
            into.Clear();
            CellRect ra = p.a.OccupiedRect();
            CellRect rb = p.b.OccupiedRect();
            // Compare WIDTH and HEIGHT, not just Area. Index-order pairing below is only a
            // valid positional correspondence when the two rects have the same shape - and a
            // rotated non-square end has identical Area with its dimensions swapped, so an
            // Area-only check would wave it through and silently pair the wrong cells.
            //
            // ⚠ THIS GUARD IS NO LONGER HYPOTHETICAL. It was written when every link def was
            // square and read "stops a FUTURE non-square one failing invisibly"; §85 made the
            // plain staircases 1x2. Pairs stay safe only because both ends share a def SIZE
            // and a ROTATION (Building_ABStairs2 spawns the counterpart with this end's
            // Rotation, and that line is load-bearing for exactly this reason) - so if a
            // future link ever pairs two different defs, or something re-rotates one end,
            // this is the warning that will fire instead of a silent cell mismatch.
            if (ra.Width != rb.Width || ra.Height != rb.Height)
            {
                Log.WarningOnce(ABLog.Tag + " V2: wormhole ends differ in shape ("
                    + ra.Width + "x" + ra.Height + " vs " + rb.Width + "x" + rb.Height
                    + "); linking the first cell only.",
                    762195901);
                into.Add(new KeyValuePair<IntVec3, IntVec3>(p.a.Position, p.b.Position));
                return true;
            }
            // Both rects walked in the same order, so cell i of one end maps to cell i of the
            // other. Counterparts are spawned translated with matching rotation, so this is a
            // straight positional correspondence.
            List<IntVec3> ca = new List<IntVec3>(ra.Area);
            foreach (IntVec3 c in ra) ca.Add(c);
            int i = 0;
            foreach (IntVec3 c in rb)
            {
                into.Add(new KeyValuePair<IntVec3, IntVec3>(ca[i], c));
                i++;
            }
            return into.Count > 0;
        }

        private static readonly List<KeyValuePair<IntVec3, IntVec3>> tmpPairs =
            new List<KeyValuePair<IntVec3, IntVec3>>();

        private static bool Rearm(Map map, Pair p)
        {
            if (p.a == null || p.b == null || !p.a.Spawned || !p.b.Spawned)
            {
                return false;
            }
            if (!TryCellPairs(p, tmpPairs))
            {
                return false;
            }

            // Still armed? Every cell pair must still hold a valid link in BOTH regions.
            // Checked wholesale: a partially armed anchor is the failure mode this exists to
            // prevent, so anything less than fully armed is torn down and rebuilt.
            bool allArmed = p.links.Count == tmpPairs.Count;
            if (allArmed)
            {
                for (int i = 0; i < p.links.Count; i++)
                {
                    RegionLink l = p.links[i];
                    if (l == null || l.RegionA == null || !l.RegionA.valid
                        || l.RegionB == null || !l.RegionB.valid
                        || !l.RegionA.links.Contains(l) || !l.RegionB.links.Contains(l))
                    {
                        allArmed = false;
                        break;
                    }
                }
            }
            if (allArmed)
            {
                return false;
            }

            // NoRebuild: this runs INSIDE the region rebuild postfix, so asking for a
            // rebuild here would recurse.
            Region ra = map.regionGrid.GetValidRegionAt_NoRebuild(p.a.Position);
            Region rb = map.regionGrid.GetValidRegionAt_NoRebuild(p.b.Position);
            if (ra == null || rb == null || ra == rb)
            {
                return false;
            }
            // REFUSE to arm unless both ends are Portal regions.
            //
            // This is nearly always a STALE READ rather than a broken def: Link() is
            // called from SpawnSetup, and the region containing the brand-new door has
            // only been marked dirty at that point - GetValidRegionAt_NoRebuild (which
            // must not trigger a rebuild, since we run inside the rebuild postfix) still
            // returns the pre-door Normal region.
            //
            // Arming anyway would be actively harmful: a link between two NORMAL regions
            // merges their districts into one room, so the basement would share a room -
            // and a temperature - with the surface. Deferring costs nothing; the rebuild
            // postfix re-runs this a moment later with the Portal regions in place.
            if (ra.type != RegionType.Portal || rb.type != RegionType.Portal)
            {
                ABLog.Dev("Wormhole ends not Portal yet (" + ra.type + "/" + rb.type
                    + "); deferring to the next region rebuild.");
                return false;
            }

            TearDown(p);

            for (int i = 0; i < tmpPairs.Count; i++)
            {
                IntVec3 ca = tmpPairs[i].Key;
                IntVec3 cb = tmpPairs[i].Value;
                Region rca = map.regionGrid.GetValidRegionAt_NoRebuild(ca);
                Region rcb = map.regionGrid.GetValidRegionAt_NoRebuild(cb);
                if (rca == null || rcb == null || rca == rcb)
                {
                    continue;
                }
                // Each cell must be its own Portal region; anything else would merge rooms.
                if (rca.type != RegionType.Portal || rcb.type != RegionType.Portal)
                {
                    continue;
                }
                RegionLink link = new RegionLink();
                // Synthetic span. Never handed to RegionLinkDatabase, so the hash cannot
                // collide with a real edge link; it exists only so RegionCostCalculator and
                // debug drawing have something non-degenerate to read.
                link.span = new EdgeSpan(ca, SpanDirection.North, 1);
                link.RegionA = rca;
                link.RegionB = rcb;
                rca.links.Add(link);
                rcb.links.Add(link);
                p.links.Add(link);
            }
            return p.links.Count > 0;
        }

        /// <summary>
        /// The FIRST HOP of a route from one band to another, which may take several.
        ///
        /// It used to require a single pair whose two ends were exactly (fromBand, toBand).
        /// That silently capped the mod at journeys of one flight: on a 4-level column a
        /// pawn on the surface asking for level +2 matched nothing, TrySegment logged
        /// "NONE (pawn will try to walk it and fail)", and the only way up was to order each
        /// flight by hand. Reported as "pawns can't path through the first set of stairs to
        /// proceed to the second one".
        ///
        /// §94: it now runs an EXACT planner - Dijkstra over the anchor graph, seeded at
        /// the destination - and returns the first crossing of the cheapest FULL chain
        /// (walks + flights), not the best-looking single hop. Chaining still comes for
        /// free from machinery that already exists: after each transit,
        /// ABWormholePather.Carry re-issues StartPath toward the true destination, which
        /// re-enters TrySegment and asks for the next hop - and because every suffix of a
        /// cheapest chain is itself a cheapest chain, the per-hop re-plans agree with each
        /// other instead of needing the old strict-progress hop filter.
        ///
        /// ⚠⚠ §94 WHY THE PREDECESSOR (min-hop BFS + greedy two-term proxy) HAD TO GO. Its
        /// proxy measured (far anchor -> FINAL destination) RAW across bands, and upper
        /// bands sit NORTH in map space - so for any multi-hop UP trip, "north of the
        /// destination" and "closer to it" were the same number, up to a full Slot of free
        /// discount. Field report: every ground -> 3rd-floor trip detoured through
        /// battlement ladders ~a hundred cells north while the pawn stood BESIDE the grand
        /// staircase; the reverse trip, where the same term PENALIZES northness, chose
        /// correctly; single-hop trips (same-band term) were always clean. The proxy also
        /// let min-hop dominate (a full-meshed elevator pair spanning N bands is ONE hop
        /// and would beat every staircase chain regardless of walking) and measured
        /// progress against the destination COLUMN rather than the NEXT FLIGHT
        /// (split-flight layouts mis-picked). Dijkstra answers all three with one piece of
        /// arithmetic - see TryPlanFirstHop.
        /// </summary>
        public static bool TryGetTransit(Map map, IntVec3 from, IntVec3 to,
            out Building_Door near, out Building_Door far)
        {
            return TryGetTransit(map, from, to, null, out near, out far);
        }

        /// <summary>
        /// §94: cost of one wormhole crossing, in walk-cell equivalents (a cell is ~13
        /// ticks at baseline speed; the §78 hold plus climb clip is on the order of 90
        /// ticks, so ~7 cells).
        ///
        /// Two duties beyond realism:
        ///  - it keeps chains SHORT on ties: with a zero crossing cost, a two-flight chain
        ///    with marginally less walking would beat one flight next door;
        ///  - it is the anti-regress margin for per-hop re-planning. A crossed pawn lands
        ///    within LandingRadius of the far anchor (diagonal ~2.8 cells), so a re-plan
        ///    starts at most that far from where the chain thought it stood. FlightCost
        ///    exceeds twice that displacement, so "cross straight back" can never price
        ///    below continuing an optimal chain - the weighted successor of the old
        ///    `farHops == hops - 1` strict-progress filter, which Dijkstra obsoletes.
        /// </summary>
        private const float FlightCost = 7f;

        /// <summary>
        /// Register one anchor building as a planner node, resolving its island ONCE.
        ///
        /// ⚠ AN ANCHOR WITH NO COMPONENT IS AN ANCHOR NOBODY CAN STAND ON, and it is not in
        /// the graph at all - not a waypoint, not a crossing end, not a seed. That keeps
        /// the old guarantees: a staircase sealed inside rock conducts nothing, and §59's
        /// widening still holds - anchors are Building_Door subclasses, so a FORBIDDEN
        /// staircase resolves to -1 on the forbid-aware partition and forbidding it
        /// actually closes it to colonists. Recorded as index -1 so each pair membership
        /// does not re-resolve the island of a dropped building.
        /// </summary>
        private static void AddNode(Map map, Building_Door d, bool forbidAware,
            Dictionary<Building_Door, int> index, List<Building_Door> nodes, List<int> comps)
        {
            if (index.ContainsKey(d))
            {
                return;
            }
            int c = ABBandComponents.ComponentOf(map, d.Position, forbidAware);
            if (c < 0)
            {
                index.Add(d, -1);
                return;
            }
            index.Add(d, nodes.Count);
            nodes.Add(d);
            comps.Add(c);
        }

        /// <summary>Rounds of winner-verification before the planner ships its current
        /// best. Each dirty round verifies at least one fresh term, so the loop is bounded
        /// by construction; this cap is a belt. Six rounds ≈ three candidates fully
        /// verified, beyond which the field is lying so uniformly that another probe will
        /// not change the story.</summary>
        private const int MaxVerifyRounds = 6;

        /// <summary>Correction threshold in cells. Re-ranking is free, so this only
        /// suppresses churn from Touch-vs-OnCell off-by-ones, not real divergence.</summary>
        private const float VerifyEpsilon = 0.5f;

        /// <summary>§94.g: real anchor-to-anchor walk lengths, cached until regions change.
        /// Keyed by the two anchors' thingIDs (order-normalized), complemented for the
        /// forbid-aware partition - the partitions can disagree about which doors exist.</summary>
        private sealed class WalkCache
        {
            public int version = -1;

            public readonly Dictionary<long, float> dist = new Dictionary<long, float>();
        }

        private static readonly ConditionalWeakTable<Map, WalkCache> walkCache =
            new ConditionalWeakTable<Map, WalkCache>();

        /// <summary>
        /// Real walk length between two same-island anchors, cached per region-rebuild
        /// version (ABBandComponents.Version - the exact signal that moves walls).
        ///
        /// ⚠ PAWNLESS BY DESIGN: the cache is shared across every pawn, so it paths with
        /// PassDoors - walls, the actual liars, are captured; per-pawn door nuance is not.
        /// Per-pawn exactness lives in the winner-verification legs, which DO thread the
        /// pawn. A NOT-FOUND probe (transient mid-rebuild state) falls back to the straight
        /// line rather than poisoning the cache with an infinity.
        /// </summary>
        private static float WalkEdge(Map map, Building_Door a, Building_Door b,
            bool forbidAware)
        {
            if (!walkCache.TryGetValue(map, out WalkCache c))
            {
                c = new WalkCache();
                try
                {
                    walkCache.Add(map, c);
                }
                catch (ArgumentException)
                {
                    walkCache.TryGetValue(map, out c);
                }
            }
            int v = ABBandComponents.Version;
            if (c.version != v)
            {
                c.version = v;
                c.dist.Clear();
            }
            long key = a.thingIDNumber < b.thingIDNumber
                ? ((long)a.thingIDNumber << 32) | (uint)b.thingIDNumber
                : ((long)b.thingIDNumber << 32) | (uint)a.thingIDNumber;
            if (forbidAware)
            {
                key = ~key;
            }
            if (c.dist.TryGetValue(key, out float d))
            {
                return d;
            }
            float real = RealPathLen(map, null, a.Position, new LocalTargetInfo(b));
            if (real < 0f)
            {
                real = (a.Position - b.Position).LengthHorizontal;
            }
            c.dist[key] = real;
            return real;
        }

        /// <summary>
        /// Length of the real path from <paramref name="start"/> to the target, in
        /// straight-line-comparable cells (octile-summed over the nodes: cardinal 1,
        /// diagonal √2 - PawnPath node COUNTS undercount diagonals against
        /// LengthHorizontal). Returns -1 when no path was found.
        ///
        /// ⚠ TICK-SIDE ONLY. FindPathNow opens with ForceCompleteScheduledJobs(); calling
        /// it from a draw callback is the documented ABTransitVisuals trap. Every caller
        /// here is reached from StartPath segmentation or the tick-built route preview.
        /// ⚠ PawnPath IS POOLED - measured and disposed inside one using, never held.
        /// ⚠ Same-band by construction (island-scoped legs), so
        /// Patch_PathFinder_ABRejectCrossBandSync waves every probe through; if a
        /// cross-band pair ever slipped in it would come back NotFound, which degrades to
        /// "keep the estimate" - safe.
        /// </summary>
        private static float RealPathLen(Map map, Pawn pawn, IntVec3 start,
            LocalTargetInfo target)
        {
            if (map?.pathFinder == null || !start.IsValid || !target.IsValid)
            {
                return -1f;
            }
            try
            {
                using (PawnPath path = pawn != null && pawn.Spawned && pawn.Map == map
                    ? map.pathFinder.FindPathNow(start, target, pawn, null,
                        PathEndMode.Touch)
                    : map.pathFinder.FindPathNow(start, target,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly), null,
                        PathEndMode.Touch))
                {
                    if (path == null || !path.Found)
                    {
                        return -1f;
                    }
                    List<IntVec3> cells = path.NodesReversed;
                    if (cells == null || cells.Count < 2)
                    {
                        return 0f;
                    }
                    float len = 0f;
                    for (int i = 1; i < cells.Count; i++)
                    {
                        IntVec3 d = cells[i] - cells[i - 1];
                        len += d.x != 0 && d.z != 0 ? 1.41421356f : 1f;
                    }
                    return len;
                }
            }
            catch (Exception)
            {
                return -1f;
            }
        }

        /// <summary>Rank one pair orientation as the pawn's first hop: walk-in estimate to
        /// the near end, cross, then the far end's settled cost-to-destination. Returns the
        /// winner as NODE INDICES so the §94.g verification loop can name what it checked.</summary>
        private static void RankHop(Dictionary<Building_Door, int> index, List<int> comps,
            int compFrom, float[] walkIn, float[] dist, Building_Door candNear,
            Building_Door candFar, ref float bestTotal, ref int bestNi, ref int bestFi)
        {
            if (!index.TryGetValue(candNear, out int ni) || ni < 0
                || !index.TryGetValue(candFar, out int fi) || fi < 0)
            {
                return; // an end nobody can stand on never became a node (§59)
            }
            // ⚠ THE NEAR ANCHOR MUST BE IN THE PAWN'S OWN ISLAND, NOT MERELY ITS OWN BAND.
            // Still the soundness line it always was: a pawn dispatched to a staircase it
            // cannot walk to stalls at the edge of its island re-issuing the same order.
            if (comps[ni] != compFrom)
            {
                return;
            }
            if (comps[fi] == comps[ni])
            {
                return; // both ends in one island conduct nothing worth crossing
            }
            if (dist[fi] >= float.MaxValue || walkIn[ni] >= float.MaxValue)
            {
                return; // no chain from the far side reaches the destination
            }
            // The walk-in term is same-band by the compFrom check; the far side's cost is
            // built from same-island walks and constant flights - nothing here measures
            // across a band boundary (rule 75).
            float total = walkIn[ni] + FlightCost + dist[fi];
            if (total < bestTotal)
            {
                bestTotal = total;
                bestNi = ni;
                bestFi = fi;
            }
        }
        private static LocalTargetInfo ProbeTarget(Map map, IntVec3 to, LocalTargetInfo destTarget)
        {
            Thing thing = (destTarget.HasThing ? destTarget.Thing : null);
            if (thing != null && thing.Spawned && thing.Map == map && thing.OccupiedRect().Contains(to))
            {
                return destTarget;
            }

            return new LocalTargetInfo(to);
        }

        // Start from every destination island and walk the anchor graph backward.
        // A walk edge stays within a single island. A crossing uses a flat cost,
        // so the artificial spacing between floors never enters the distance.
        private static bool TryPlanFirstHop(Map map, List<Pair> list, IntVec3 from, IntVec3 to, int compFrom, HashSet<int> destComps, bool forbidAware, Pawn pawn, bool exact, LocalTargetInfo destTarget, out Building_Door near, out Building_Door far)
        {
            near = null;
            far = null;
            Dictionary<Building_Door, int> anchorIndices = new Dictionary<Building_Door, int>();
            List<Building_Door> anchors = new List<Building_Door>();
            List<int> islands = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                Pair pair = list[i];
                if (pair.a != null && pair.b != null && pair.a.Spawned && pair.b.Spawned)
                {
                    AddNode(map, pair.a, forbidAware, anchorIndices, anchors, islands);
                    AddNode(map, pair.b, forbidAware, anchorIndices, anchors, islands);
                }
            }

            int count = anchors.Count;
            if (count == 0)
            {
                return false;
            }

            if (ABV2Debug.LogTransit)
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (int destComp in destComps)
                {
                    if (stringBuilder.Length > 0)
                    {
                        stringBuilder.Append(", ");
                    }

                    stringBuilder.Append("isl ").Append(destComp);
                }

                lastDestCompsText = "{" + stringBuilder?.ToString() + "}";
                StringBuilder stringBuilder2 = new StringBuilder();
                for (int j = 0; j < count; j++)
                {
                    if (stringBuilder2.Length > 0)
                    {
                        stringBuilder2.Append(", ");
                    }

                    stringBuilder2.Append(anchors[j].Position).Append("=isl ").Append(islands[j]);
                }

                lastAnchorCompsText = "{" + stringBuilder2?.ToString() + "}";
            }
            else
            {
                lastDestCompsText = null;
                lastAnchorCompsText = null;
            }

            // Estimate both end legs first. Exact mode verifies the winner's legs
            // with the pawn's real pathfinder, then reranks if the estimates change.
            float[] seedDistance = new float[count];
            bool[] seedVerified = new bool[count];
            float[] walkInDistance = new float[count];
            bool[] walkInVerified = new bool[count];
            for (int k = 0; k < count; k++)
            {
                seedDistance[k] = (destComps.Contains(islands[k]) ? (anchors[k].Position - to).LengthHorizontal : float.MaxValue);
                walkInDistance[k] = ((islands[k] == compFrom) ? (anchors[k].Position - from).LengthHorizontal : float.MaxValue);
            }

            float[] distance = new float[count];
            bool[] settled = new bool[count];
            int[] nextTowardSeed = new int[count];
            int correctionRound = 0;
            while (true)
            {
                for (int l = 0; l < count; l++)
                {
                    distance[l] = seedDistance[l];
                    settled[l] = false;
                    nextTowardSeed[l] = -1;
                }

                for (int m = 0; m < count; m++)
                {
                    int settledAnchor = -1;
                    float bestDistance = float.MaxValue;
                    for (int n = 0; n < count; n++)
                    {
                        if (!settled[n] && distance[n] < bestDistance)
                        {
                            bestDistance = distance[n];
                            settledAnchor = n;
                        }
                    }

                    if (settledAnchor < 0)
                    {
                        break;
                    }

                    settled[settledAnchor] = true;
                    for (int walkNeighbor = 0; walkNeighbor < count; walkNeighbor++)
                    {
                        if (!settled[walkNeighbor] && islands[walkNeighbor] == islands[settledAnchor])
                        {
                            float walkCost = (exact ? WalkEdge(map, anchors[settledAnchor], anchors[walkNeighbor], forbidAware) : (anchors[walkNeighbor].Position - anchors[settledAnchor].Position).LengthHorizontal);
                            float walkDistance = distance[settledAnchor] + walkCost;
                            if (walkDistance < distance[walkNeighbor])
                            {
                                distance[walkNeighbor] = walkDistance;
                                nextTowardSeed[walkNeighbor] = settledAnchor;
                            }
                        }
                    }

                    for (int pairIndex = 0; pairIndex < list.Count; pairIndex++)
                    {
                        Pair pair2 = list[pairIndex];
                        Building_Door crossingNeighbor;
                        if (pair2.a == anchors[settledAnchor])
                        {
                            crossingNeighbor = pair2.b;
                        }
                        else
                        {
                            crossingNeighbor = ((pair2.b == anchors[settledAnchor]) ? pair2.a : null);
                        }

                        if (crossingNeighbor != null && anchorIndices.TryGetValue(crossingNeighbor, out var value) && value >= 0 && !settled[value])
                        {
                            float crossingDistance = distance[settledAnchor] + 7f;
                            if (crossingDistance < distance[value])
                            {
                                distance[value] = crossingDistance;
                                nextTowardSeed[value] = settledAnchor;
                            }
                        }
                    }
                }

                float bestTotal = float.MaxValue;
                int bestNi = -1;
                int bestFi = -1;
                for (int candidatePairIndex = 0; candidatePairIndex < list.Count; candidatePairIndex++)
                {
                    Pair pair3 = list[candidatePairIndex];
                    if (pair3.a != null && pair3.b != null && pair3.a.Spawned && pair3.b.Spawned)
                    {
                        RankHop(anchorIndices, islands, compFrom, walkInDistance, distance, pair3.a, pair3.b, ref bestTotal, ref bestNi, ref bestFi);
                        RankHop(anchorIndices, islands, compFrom, walkInDistance, distance, pair3.b, pair3.a, ref bestTotal, ref bestNi, ref bestFi);
                    }
                }

                if (bestNi < 0)
                {
                    return false;
                }

                near = anchors[bestNi];
                far = anchors[bestFi];
                lastPlanTotal = bestTotal;
                // Follow the winning chain to the destination-side seed to expose
                // both the approach anchor and the distance before its last leg.
                int seedIndex = bestFi;
                for (int hop = 0; hop < count; hop++)
                {
                    if (nextTowardSeed[seedIndex] < 0)
                    {
                        break;
                    }

                    seedIndex = nextTowardSeed[seedIndex];
                }

                lastPlanSeedAnchor = anchors[seedIndex];
                lastPlanSeedLeg = seedDistance[seedIndex];
                if (!exact)
                {
                    return true;
                }

                bool winnerVerified = true;
                if (!walkInVerified[bestNi])
                {
                    walkInVerified[bestNi] = true;
                    float actualWalkIn = RealPathLen(map, pawn, from, new LocalTargetInfo(anchors[bestNi]));
                    if (actualWalkIn < 0f)
                    {
                        if (ABV2Debug.LogTransit)
                        {
                            ABV2Debug.Transit("PLAN: walk-in to " + anchors[bestNi].ThingID + " " + anchors[bestNi].Position.ToString() + " NOT FOUND for " + pawn?.LabelShort + "; candidate excluded");
                        }

                        walkInDistance[bestNi] = float.MaxValue;
                        winnerVerified = false;
                    }
                    else if (Math.Abs(actualWalkIn - walkInDistance[bestNi]) > 0.5f)
                    {
                        if (ABV2Debug.LogTransit)
                        {
                            ABV2Debug.Transit("PLAN: walk-in to " + anchors[bestNi].ThingID + " " + anchors[bestNi].Position.ToString() + " corrected " + walkInDistance[bestNi].ToString("0.0") + " -> " + actualWalkIn.ToString("0.0"));
                        }

                        walkInDistance[bestNi] = actualWalkIn;
                        winnerVerified = false;
                    }
                }

                if (destComps.Contains(islands[bestFi]) && !seedVerified[bestFi])
                {
                    seedVerified[bestFi] = true;
                    float actualSeedDistance = RealPathLen(map, pawn, anchors[bestFi].Position, ProbeTarget(map, to, destTarget));
                    if (actualSeedDistance < 0f)
                    {
                        if (ABV2Debug.LogTransit)
                        {
                            ABV2Debug.Transit("PLAN: seed leg from " + anchors[bestFi].ThingID + " " + anchors[bestFi].Position.ToString() + " NOT FOUND for " + pawn?.LabelShort + "; candidate excluded");
                        }

                        seedDistance[bestFi] = float.MaxValue;
                        winnerVerified = false;
                    }
                    else if (Math.Abs(actualSeedDistance - seedDistance[bestFi]) > 0.5f)
                    {
                        if (ABV2Debug.LogTransit)
                        {
                            ABV2Debug.Transit("PLAN: seed leg from " + anchors[bestFi].ThingID + " " + anchors[bestFi].Position.ToString() + " corrected " + seedDistance[bestFi].ToString("0.0") + " -> " + actualSeedDistance.ToString("0.0"));
                        }

                        seedDistance[bestFi] = actualSeedDistance;
                        winnerVerified = false;
                    }
                }

                if (winnerVerified)
                {
                    return true;
                }

                if (correctionRound >= 6)
                {
                    break;
                }

                correctionRound++;
            }

            if (ABV2Debug.LogTransit)
            {
                ABV2Debug.Transit("PLAN: verify budget exhausted after " + 6 + " rounds; shipping current best");
            }

            return true;
        }

        private static bool TryGetTransitToTouch(Map map, IntVec3 from, IntVec3 to, int compFrom, bool forbidAware, List<Pair> list, Pawn pawn, bool exact, LocalTargetInfo destTarget, out Building_Door near, out Building_Door far)
        {
            near = null;
            far = null;
            HashSet<int> hashSet = null;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 intVec = to + GenAdj.AdjacentCells[i];
                if (!intVec.InBounds(map) || !ABBands.SameBand(map, intVec, to))
                {
                    continue;
                }

                int num = ABBandComponents.ComponentOf(map, intVec, forbidAware);
                if (num >= 0)
                {
                    if (num == compFrom)
                    {
                        lastRefusal = TransitRefusal.TouchableFromPawnIsland;
                        return false;
                    }

                    if (hashSet == null)
                    {
                        hashSet = new HashSet<int>();
                    }

                    hashSet.Add(num);
                }
            }

            if (hashSet == null)
            {
                lastRefusal = TransitRefusal.TouchNoStandableNeighbour;
                return false;
            }

            lastDestCompCount = hashSet.Count;
            if (TryPlanFirstHop(map, list, from, to, compFrom, hashSet, forbidAware, pawn, exact, destTarget, out near, out far))
            {
                return true;
            }

            lastRefusal = TransitRefusal.NoLinkChain;
            return false;
        }

        public static void CollectPairs(Map map, List<KeyValuePair<Building_Door, Building_Door>> into)
        {
            if (map == null || into == null || !byMap.TryGetValue(map, out var value))
            {
                return;
            }

            for (int i = 0; i < value.Count; i++)
            {
                Pair pair = value[i];
                if (pair.a != null && pair.b != null && pair.a.Spawned && pair.b.Spawned)
                {
                    into.Add(new KeyValuePair<Building_Door, Building_Door>(pair.a, pair.b));
                }
            }
        }

        public static bool TryEstimateTrip(Map map, IntVec3 from, IntVec3 to, Pawn pawn, out float length, out IntVec3 lastAnchor, out float chainCost)
        {
            lastAnchor = IntVec3.Invalid;
            chainCost = float.MaxValue;
            if (!TryEstimateTrip(map, from, to, pawn, out length))
            {
                return false;
            }

            if (lastPlanSeedAnchor != null && lastPlanSeedAnchor.Spawned)
            {
                lastAnchor = lastPlanSeedAnchor.Position;
                chainCost = Math.Max(0f, length - lastPlanSeedLeg);
            }

            return true;
        }

        public static bool TryEstimateTrip(Map map, IntVec3 from, IntVec3 to, Pawn pawn, out float length)
        {
            length = float.MaxValue;
            if (map == null || !byMap.TryGetValue(map, out var value) || value.Count == 0)
            {
                return false;
            }

            if (!from.InBounds(map) || !to.InBounds(map))
            {
                return false;
            }

            bool forbidAware = ABBandComponents.RespectsForbiddenDoors(pawn);
            int num = ABBandComponents.ComponentOf(map, from, forbidAware);
            if (num < 0)
            {
                return false;
            }

            int num2 = ABBandComponents.ComponentOf(map, to, forbidAware);
            HashSet<int> hashSet;
            if (num2 >= 0)
            {
                if (num2 == num)
                {
                    return false;
                }

                hashSet = new HashSet<int>
                {
                    num2
                };
            }
            else
            {
                hashSet = null;
                for (int i = 0; i < 8; i++)
                {
                    IntVec3 intVec = to + GenAdj.AdjacentCells[i];
                    if (!intVec.InBounds(map) || !ABBands.SameBand(map, intVec, to))
                    {
                        continue;
                    }

                    int num3 = ABBandComponents.ComponentOf(map, intVec, forbidAware);
                    if (num3 >= 0)
                    {
                        if (num3 == num)
                        {
                            return false;
                        }

                        if (hashSet == null)
                        {
                            hashSet = new HashSet<int>();
                        }

                        hashSet.Add(num3);
                    }
                }

                if (hashSet == null)
                {
                    return false;
                }
            }

            lastPlanTotal = float.MaxValue;
            if (!TryPlanFirstHop(map, value, from, to, num, hashSet, forbidAware, pawn, exact: false, default, out var _, out var _))
            {
                return false;
            }

            length = lastPlanTotal;
            return length < float.MaxValue;
        }

        public static bool TryGetTransit(Map map, IntVec3 from, IntVec3 to, Pawn pawn, out Building_Door near, out Building_Door far, LocalTargetInfo destTarget = default(LocalTargetInfo))
        {
            near = null;
            far = null;
            lastRefusal = TransitRefusal.None;
            lastDestCompCount = 0;
            if (map == null || !byMap.TryGetValue(map, out var value) || value.Count == 0)
            {
                lastRefusal = TransitRefusal.NoPairsOnMap;
                return false;
            }

            bool forbidAware = ABBandComponents.RespectsForbiddenDoors(pawn);
            bool exact = pawn?.CurJob?.def != JobDefOf.GotoWander;
            int num = ABBandComponents.ComponentOf(map, from, forbidAware);
            if (num < 0)
            {
                lastRefusal = TransitRefusal.PawnHasNoIsland;
                return false;
            }

            int num2 = ABBandComponents.ComponentOf(map, to, forbidAware);
            if (num2 < 0)
            {
                return TryGetTransitToTouch(map, from, to, num, forbidAware, value, pawn, exact, destTarget, out near, out far);
            }

            if (num == num2)
            {
                lastRefusal = TransitRefusal.SameIsland;
                return false;
            }

            HashSet<int> destComps = new HashSet<int>
            {
                num2
            };
            lastDestCompCount = 1;
            if (TryPlanFirstHop(map, value, from, to, num, destComps, forbidAware, pawn, exact, destTarget, out near, out far))
            {
                return true;
            }

            lastRefusal = TransitRefusal.NoLinkChain;
            return false;
        }

        internal static string LastRefusalText => lastRefusal switch
        {
            TransitRefusal.NoPairsOnMap => "no link pairs on this map",
            TransitRefusal.PawnHasNoIsland => "pawn is not standing on a walkable island",
            TransitRefusal.SameIsland => "same island, nothing to segment",
            TransitRefusal.TouchableFromPawnIsland => "already touchable from the pawn's own island",
            TransitRefusal.TouchNoStandableNeighbour => "target has no standable neighbour in its own band",
            TransitRefusal.NoLinkChain => "no chain of links reaches the target island (" + lastDestCompCount + " approach island" + ((lastDestCompCount == 1) ? "" : "s") + " tried)" + ((lastDestCompsText != null) ? (" | sought " + lastDestCompsText) : string.Empty) + ((lastAnchorCompsText != null) ? (" | anchors " + lastAnchorCompsText) : string.Empty),
            _ => "no reason recorded",
        };

        private static float lastPlanSeedLeg;

        private static Building_Door lastPlanSeedAnchor;

        private static int lastDestCompCount;

        private static string lastAnchorCompsText;

        private static string lastDestCompsText;

        private static TransitRefusal lastRefusal;

        private static float lastPlanTotal = float.MaxValue;

        private enum TransitRefusal
        {
            None,
            NoPairsOnMap,
            PawnHasNoIsland,
            SameIsland,
            TouchableFromPawnIsland,
            TouchNoStandableNeighbour,
            NoLinkChain
        }

    }

    /// <summary>
    /// Synthetic links are wiped by RegionDirtyer on every region rebuild, so they must be
    /// re-armed immediately after the rebuild that destroyed them.
    ///
    /// NOT A HARMONY PATCH ANY MORE. This was a postfix on
    /// RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms, which is called ~4,500 times per
    /// frame and early-outs on !regionDirtyer.AnyDirty. Re-arming on every no-op call cost
    /// 0.34 ms/frame. Gating it with a prefix that sampled AnythingToRebuild only recovered
    /// 22% (0.339 -> 0.266 ms/frame), because the gate ADDED a second patch to an extremely
    /// hot method and the residue was Harmony dispatch, not work.
    ///
    /// vanilla already publishes exactly the signal we want: MapEvents.RegionsRoomsChanged
    /// is invoked on the LAST line of TryRebuildDirtyRegionsAndRooms, on the only path that
    /// actually rebuilt (after SetAllClean and initialized = true). Subscribing costs nothing
    /// on the millions of no-op calls and fires precisely when links have been wiped.
    /// </summary>
    public static class ABWormholeRearmHook
    {
        /// <summary>Maps we have already subscribed, so a re-entrant or repeated
        /// FinalizeInit cannot stack duplicate handlers on one map's event.</summary>
        private static readonly ConditionalWeakTable<Map, object> hooked =
            new ConditionalWeakTable<Map, object>();

        public static void Register(Map map)
        {
            if (map?.events == null)
            {
                return;
            }
            if (hooked.TryGetValue(map, out _))
            {
                return;
            }
            try
            {
                hooked.Add(map, new object());
            }
            catch (ArgumentException)
            {
                return; // benign race; already registered
            }
            // Captures the map, and MapEvents dies with the map, so there is nothing to
            // unsubscribe - the handler becomes unreachable together with its map.
            map.events.RegionsRoomsChanged += delegate { Rearm(map); };

            // ⚠⚠ INITIAL ARM - THE SUBSCRIPTION ALONE IS NOT ENOUGH ON THE LOAD PATH.
            //
            // The event fires on the LAST line of TryRebuildDirtyRegionsAndRooms, i.e.
            // only on the NEXT rebuild after this registration. On a NEW banded game that
            // is fine: carving and the post-FinalizeInit terrain repairs dirty regions
            // within frames, the event fires, the links arm. On a LOADED game the one big
            // region rebuild has ALREADY happened by the time we get here -
            // Map.FinalizeInit rebuilds all regions BEFORE calling MapComponents'
            // FinalizeInit, which is where Register is called from. So the map sat with
            // regions built, ZERO synthetic links, and no event due until the first
            // genuine region change of the session. On a settled colony that can be
            // hours. Symptom: after save+reload every cross-level bill, haul and
            // construction job is silently dead until the player builds or destroys
            // something. (The old Harmony postfix re-armed ~4,500 times per FRAME and hid
            // this hole by brute force; the event rework removed the accidental initial
            // arm together with the waste - the perf numbers in the banner above were
            // real, and so was the regression they smuggled in.)
            //
            // Deferred to the main thread: FinalizeInit runs on the LongEvent WORKER
            // thread during load - the same reason ABBandMap defers its camera move.
            LongEventHandler.ExecuteWhenFinished(delegate { Rearm(map); });
        }

        /// <summary>One body for both arm paths (the per-rebuild event and the initial
        /// post-load arm), so the guard gate and the error handling cannot drift apart.</summary>
        private static void Rearm(Map map)
        {
            if (!ABGuard.On(ABGuard.Transit))
            {
                return;
            }

            try
            {
                ABWormhole.RearmAll(map);
                rearmFailures = 0;
            }
            catch (Exception ex)
            {
                rearmFailures++;
                if (rearmFailures >= 3)
                {
                    ABGuard.Disable(ABGuard.Transit, ex, "V2 wormhole re-arm");
                    return;
                }

                Log.WarningOnce("[As above, So below] V2 wormhole re-arm threw (attempt " + rearmFailures + "/" + 3 + "; will retry on the next region rebuild): " + ex, 11218506);
            }
        }
        private static int rearmFailures;

        private const int MaxConsecutiveRearmFailures = 3;

    }
}
