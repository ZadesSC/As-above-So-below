using System.Reflection;
using System.Text;
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 SPIKE - cross-band movement, done at the ONE place every pawn movement
    /// funnels through.
    ///
    /// 1.6's pathfinder is jobified (PathFinderJob is an IJob over NativeArray, with a
    /// NativePriorityQueue frontier), so A* neighbour expansion cannot be patched and
    /// the wormhole is invisible to it. Reachability says "yes" but the pathfinder
    /// would return NotFound -> PatherFailed -> job restart loop.
    ///
    /// The fix is to never ask the pathfinder to cross: segment the journey at
    /// Pawn_PathFollower.StartPath into (leg to near anchor) -> (transit) -> (leg to
    /// real destination). Both legs are ordinary intra-band paths.
    ///
    /// Why this single interception is the whole point of V2: EVERY job, every pawn,
    /// every faction and every third-party JobDriver reaches movement through
    /// StartPath. V1 needed ~130 patches because it had to widen each CONSUMER's idea
    /// of "my map"; here the consumers are untouched and only the movement primitive
    /// knows bands exist.
    /// </summary>
    public static class ABWormholePather
    {
        private struct Transit
        {
            public LocalTargetInfo realDest;
            public PathEndMode realPeMode;
            public Building_Door near;
            public Building_Door far;
            public int expiresAtTick;
            public IntVec3 entryCell;
            public IntVec3 approachCell;
            public bool legTwo;
            public int startedTick;
            public Job job;
            public int nearSinceTick;
        }

        /// <summary>
        /// How long a pawn that is already within ArriveRadius may keep walking toward its
        /// entry cell before we carry it anyway.
        ///
        /// ⚠⚠ THIS IS THE ONLY THING STANDING BETWEEN §78c AND A RE-RUN OF THE OLD STALLS.
        /// Waiting for the pawn to reach a specific cell is a NARROWER carry condition, and
        /// narrowing this condition has caused a stairwell jam every single time it has been
        /// tried (see the ArriveRadius banner). The patience makes the narrowing
        /// time-bounded rather than conditional: within five seconds of getting close, the
        /// pawn crosses whether or not it ever reached the cell.
        ///
        /// ⚠ MEASURED FROM Transit.nearSinceTick, NOT startedTick - see the banner there for
        /// what measuring it from the wrong event did.
        ///
        /// ⚠ RAISED FROM 120 BECAUSE THE FOOTPRINT IS THE UNIT, NOT THE CELL. ArriveRadius
        /// is measured from the anchor, so on a 3x3 grand staircase a pawn is already "close"
        /// while it still has to walk most of the way AROUND the building to reach the mouth
        /// - roughly 8 cells, about 1.7s at walk speed, and more if it has to yield. 120
        /// ticks made the backstop fire mid-detour and land the pawn on the side it was
        /// passing. This is a ceiling on a case that stoppedShort already catches instantly
        /// whenever the pawn is genuinely blocked, so spending it is cheap.
        /// </summary>
        private const int ApproachPatienceTicks = 300;

        /// <summary>A transit record lives this long before being abandoned. Long enough to
        /// cross a band on foot, short enough that a stranded record cannot linger.</summary>
        private const int TransitTimeoutTicks = 4000;

        /// <summary>How far from the far anchor a transiting pawn may be set down.</summary>
        internal const int LandingRadius = 2;

        /// <summary>How close the pawn must get to the near anchor to be carried across.
        ///
        /// Deliberately LandingRadius + 1, not an independent number. Pawns that have already
        /// crossed are set down within LandingRadius of the anchor, so they occupy exactly the
        /// cells the next pawn wants to walk through - which makes that pawn come to rest up
        /// to LandingRadius cells short. An arrival test tighter than that rejects a pawn that
        /// is standing as close as it can physically get, the transit never completes, the job
        /// re-issues, and the stairwell jams. Observed as:
        ///   ARRIVE-MISMATCH Chewy at (92, 0, 659) expected (90, 0, 661)
        /// where the pawn was 2.83 cells away - blocked by a pawn that had just landed.</summary>
        private const int ArriveRadius = LandingRadius + 1;

        private static readonly Dictionary<int, Transit> pending = new Dictionary<int, Transit>();

        /// <summary>
        /// Snapshot of every in-flight transit, for the "AB2: transit health" dev action.
        ///
        /// A PROBE, not a fix. "Pawns get stuck at the stairs" has now had three different
        /// causes (stacked landings, mismatched arrival radii, cross-band wander roots), and
        /// each was diagnosed only after a wrong guess. The distinguishing facts are always
        /// the same four: how OLD the record is, what JOB owns it, how far the pawn still is
        /// from its near anchor, and whether that anchor is even in the pawn's band. A record
        /// ageing without the distance shrinking is a stuck pawn; a young record with a large
        /// distance is just a pawn still walking.
        /// </summary>
        public static string HealthReport(Map map)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("pending transits: " + pending.Count);
            // One line that splits "the transit side never fires" from "the draw side eats
            // it" - the two invisible halves of "the stair animations are gone". See the
            // counter banner in ABStairAnim.
            sb.AppendLine(ABStairAnim.CountersLine());
            if (map == null)
            {
                return sb.ToString();
            }
            int now = Find.TickManager.TicksGame;
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            foreach (KeyValuePair<int, Transit> kv in pending)
            {
                Pawn pawn = FindPawn(spawned, kv.Key);
                Transit t = kv.Value;
                int age = TransitTimeoutTicks - (t.expiresAtTick - now);
                if (pawn == null)
                {
                    sb.AppendLine("  [id " + kv.Key + "] pawn not on this map; age=" + age);
                    continue;
                }
                float dist = t.near != null
                    ? pawn.Position.DistanceTo(t.near.Position)
                    : -1f;
                sb.AppendLine("  " + pawn.LabelShortCap
                    + " (" + (pawn.RaceProps != null && pawn.RaceProps.Animal ? "animal" : "humanlike") + ")"
                    + " at " + pawn.Position + " band " + ABBands.BandOf(map, pawn.Position)
                    + " | job=" + (pawn.CurJob?.def?.defName ?? "none")
                    + " | age=" + age + "/" + TransitTimeoutTicks
                    + " | distToNear=" + dist.ToString("0.0") + " (need <=" + ArriveRadius + ")"
                    + " | near=" + (t.near != null ? t.near.Position.ToString() : "null")
                    + " band " + (t.near != null ? ABBands.BandOf(map, t.near.Position) : -1)
                    + " | moving=" + (pawn.pather != null && pawn.pather.Moving));
            }
            return sb.ToString();
        }

        /// <summary>Pawns that have been segmented at least once. Arrival diagnostics are
        /// scoped to these, otherwise every arrival of every pawn on the map logs a line and
        /// the one that matters is buried.</summary>
        private static readonly HashSet<int> everSegmented = new HashSet<int>();

        public static bool HasPending(Pawn p) => p != null && pending.ContainsKey(p.thingIDNumber);

        /// <summary>Is ANY transit in flight. A plain count read, so the renderer can skip
        /// the dictionary probe entirely on the hot path - see ABStairAnim.ProgressFor.</summary>
        public static bool AnyPending => pending.Count > 0;

        /// <summary>
        /// The in-flight transit for a pawn, for the SELECTION OVERLAYS to draw.
        ///
        /// ⚠ THE PATH LINE IS NOT MISSING PAST THE STAIRS, IT GENUINELY DOES NOT EXIST.
        /// TrySegment rewrites the destination to the near anchor BEFORE the pather builds a
        /// path, so `curPath` correctly ends at the stairwell and the rest of the journey
        /// lives only here. Anything that wants to show the whole trip has to read this
        /// record and compute the far side itself - see ABTransitVisuals.
        /// </summary>
        public static bool TryGetPending(Pawn p, out IntVec3 nearCell, out IntVec3 farCell,
            out LocalTargetInfo realDest)
        {
            nearCell = IntVec3.Invalid;
            farCell = IntVec3.Invalid;
            realDest = LocalTargetInfo.Invalid;
            if (p == null || !pending.TryGetValue(p.thingIDNumber, out Transit t))
            {
                return false;
            }
            if (t.near == null || t.far == null || !t.near.Spawned || !t.far.Spawned)
            {
                return false;
            }
            nearCell = t.near.Position;
            farCell = t.far.Position;
            realDest = t.realDest;
            return true;
        }

        public static void Clear(Pawn p)
        {
            if (p != null)
            {
                pending.Remove(p.thingIDNumber);
                lastReal.Remove(p.thingIDNumber);
            }
        }

        /// <summary>
        /// ⚠⚠ EVERY STATIC HERE IS KEYED BY thingIDNumber, AND THOSE IDS ARE STABLE ACROSS A
        /// SAVE LINEAGE - so without this hook a loaded game inherits the PREVIOUS session's
        /// transit records and they bind straight onto the live pawns that share those ids.
        ///
        /// Reported as "sometimes pawns refuse to move when you load a prior save, until they
        /// complete an action on the level they're on". The record survives the load holding
        /// doors from the discarded map; dropping a Game does not despawn its things, so
        /// `near.Spawned` can still read true and the sweep happily acts on it - hijacking a
        /// pawn toward a destination that belonged to a different session, or overwriting the
        /// transit it is legitimately trying to make. Finishing a local job clears it because
        /// the next segmentation overwrites the entry.
        ///
        /// ⚠ AND "PRIOR SAVE" IS THE TELL, NOT A DETAIL. Loading an EARLIER save rewinds
        /// TicksGame, so `now > expiresAtTick` is false for as long as the rewind lasts and
        /// the 4000-tick self-cleanup - the thing that would otherwise have hidden this bug -
        /// cannot fire at all. See the matching guard in TickTransits.
        /// </summary>
        [ABGameReset(0)]
        public static void ResetForNewGame()
        {
            pending.Clear();
            everSegmented.Clear();
            lastReal.Clear();
            tmpDone.Clear();
            tmpStamp.Clear();
            tmpLegTwo.Clear();
            tmpCarry.Clear();
            holding.Clear();
            tmpHoldDone.Clear();
            tmpHoldFire.Clear();
            tmpHoldAdopt.Clear();
            List<Map> list = Current.Game?.Maps;
            for (int num = restoreQueue.Count - 1; num >= 0; num--)
            {
                if (list == null || !list.Contains(restoreQueue[num].Key))
                {
                    restoreQueue.RemoveAt(num);
                }
            }
        }

        // ================================================== §78 the held crossing

        /// <summary>
        /// A transit that has been DECIDED and is now waiting out its entry clip before the
        /// position write. See the banner on BeginCrossing for why this is not the thing
        /// that broke in run #297.
        /// </summary>
        private struct Crossing
        {
            public Pawn pawn;
            public Building_Door near;
            public Building_Door far;
            public LocalTargetInfo realDest;
            public PathEndMode realPeMode;
            public int fireAtTick;
            public IntVec3 holdCell;

            /// <summary>The job that owned the pather when the hold started. If it changes,
            /// something else has taken command and this teleport must not happen.</summary>
            public Job job;
        }

        private static readonly Dictionary<int, Crossing> holding =
            new Dictionary<int, Crossing>();

        private static readonly List<int> tmpHoldDone = new List<int>();

        private static readonly List<Crossing> tmpHoldFire = new List<Crossing>();

        /// <summary>Hard ceiling on a hold. Longer than any clip; if this ever fires,
        /// something stopped ticking and finishing late beats never finishing.</summary>
        private const int MaxHoldTicks = 300;

        /// <summary>Returns true when the destination was rewritten to a near anchor.</summary>
        public static bool TrySegment(Pawn pawn, ref LocalTargetInfo dest, ref PathEndMode peMode)
        {
            if (pawn == null || !pawn.Spawned || !dest.IsValid)
            {
                return false;
            }

            Map map = pawn.Map;
            if (!ABBands.Banded(map))
            {
                return false;
            }

            IntVec3 intVec = dest.Cell;
            if (dest.HasThing && dest.Thing != null && dest.Thing.Spawned)
            {
                Thing thing = dest.Thing;
                if (peMode == PathEndMode.InteractionCell && thing.def.hasInteractionCell)
                {
                    IntVec3 interactionCell = thing.InteractionCell;
                    if (interactionCell.InBounds(map))
                    {
                        intVec = interactionCell;
                    }
                }
                else if ((peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && (thing.def.size.x > 1 || thing.def.size.z > 1) && !ABBands.SameBand(map, pawn.Position, intVec))
                {
                    foreach (IntVec3 item in thing.OccupiedRect())
                    {
                        if (item.InBounds(map) && ABBands.SameBand(map, pawn.Position, item))
                        {
                            intVec = item;
                            break;
                        }
                    }
                }
            }

            if (ABBands.SameBand(map, pawn.Position, intVec) && !ABBandComponents.KnownDifferentComponents(map, pawn.Position, intVec, pawn))
            {
                bool flag = false;
                if (pending.TryGetValue(pawn.thingIDNumber, out var value))
                {
                    flag = intVec == value.entryCell || intVec == value.approachCell || (value.near != null && value.near.Spawned && value.near.OccupiedRect().Contains(intVec));
                    if (!flag)
                    {
                        string[] array = new string[9]
                        {
                            "SUPERSEDED ",
                            pawn.LabelShort,
                            ": new same-band order to ",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null
                        };
                        IntVec3 intVec2 = intVec;
                        array[3] = intVec2.ToString();
                        array[4] = " is not this transit's leg (mouth ";
                        intVec2 = value.entryCell;
                        array[5] = intVec2.ToString();
                        array[6] = ", approach ";
                        intVec2 = value.approachCell;
                        array[7] = intVec2.ToString();
                        array[8] = "); dropping the record";
                        ABV2Debug.Transit(string.Concat(array));
                        Clear(pawn);
                    }
                }

                if (!flag)
                {
                    lastReal.Remove(pawn.thingIDNumber);
                }

                return false;
            }

            bool flag2 = ABWormhole.TryGetTransit(map, pawn.Position, intVec, pawn, out var near, out var far, dest);
            if (ABV2Debug.LogTransit)
            {
                string[] array2 = new string[21]
                {
                    pawn.LabelShort,
                    " ",
                    pawn.Position.ToString(),
                    " (band ",
                    ABBands.BandOf(map, pawn.Position).ToString(),
                    ") comp ",
                    ABBandComponents.ComponentOf(map, pawn.Position, pawn).ToString(),
                    " -> ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                };
                IntVec3 intVec2 = intVec;
                array2[8] = intVec2.ToString();
                array2[9] = " (band ";
                array2[10] = ABBands.BandOf(map, intVec).ToString();
                array2[11] = ") comp ";
                array2[12] = ABBandComponents.ComponentOf(map, intVec, pawn).ToString();
                array2[13] = " | forbidAware=";
                array2[14] = ABBandComponents.RespectsForbiddenDoors(pawn).ToString();
                array2[15] = " | job=";
                array2[16] = pawn.CurJob?.def?.defName ?? "none";
                array2[17] = " | pairs=";
                array2[18] = ABWormhole.PairCount(map).ToString();
                array2[19] = " | transit=";
                array2[20] = (flag2 ? ("YES via " + near.Position.ToString() + " -> " + far.Position.ToString()) : ("NONE - " + ABWormhole.LastRefusalText + "; pawn will try to walk it and fail"));
                ABV2Debug.Transit(string.Concat(array2));
            }

            if (!flag2)
            {
                Clear(pawn);
                lastOutcome = SegmentOutcome.FailedFast;
                failedFastNoLink++;
                MarkNextRequestDoomed(pawn);
                return false;
            }

            IntVec3 intVec3 = EntryCellFor(near, pawn);
            IntVec3 intVec4 = ApproachCellFor(near, pawn, intVec3);
            ABV2Debug.Transit("  entry cell for " + near.def.defName + " " + near.Rotation.ToString() + " at " + near.Position.ToString() + " = " + (intVec3.IsValid ? intVec3.ToString() : "INVALID - walking to the building"));
            everSegmented.Add(pawn.thingIDNumber);
            Job curJob = pawn.CurJob;
            pending[pawn.thingIDNumber] = new Transit
            {
                realDest = dest,
                realPeMode = peMode,
                near = near,
                far = far,
                entryCell = intVec3,
                approachCell = intVec4,
                startedTick = Find.TickManager.TicksGame,
                expiresAtTick = Find.TickManager.TicksGame + 4000,
                job = curJob
            };
            int retries = ((lastReal.TryGetValue(pawn.thingIDNumber, out var value2) && value2.job != null && value2.job == curJob) ? value2.retries : 0);
            lastReal[pawn.thingIDNumber] = new RealLeg
            {
                dest = dest,
                peMode = peMode,
                job = curJob,
                retries = retries
            };
            IntVec3 position = pawn.Position;
            if (intVec4.IsValid && intVec4 == position)
            {
                intVec4 = IntVec3.Invalid;
            }

            if (intVec3.IsValid && intVec3 == position)
            {
                intVec3 = IntVec3.Invalid;
            }

            LocalTargetInfo localTargetInfo;
            if (intVec4.IsValid)
            {
                localTargetInfo = new LocalTargetInfo(intVec4);
            }
            else
            {
                localTargetInfo = (intVec3.IsValid ? new LocalTargetInfo(intVec3) : ((LocalTargetInfo)near));
            }

            if (localTargetInfo.Cell == position)
            {
                IntVec3 intVec2 = position;
                ABV2Debug.Transit("  StartPath rewritten -> own cell " + intVec2.ToString() + " (already on the mouth); arrival fires synchronously and begins the crossing");
                dest = new LocalTargetInfo(position);
                peMode = PathEndMode.OnCell;
                lastOutcome = SegmentOutcome.Rewritten;
                arrivedOnMouth++;
                return true;
            }

            dest = localTargetInfo;
            peMode = PathEndMode.OnCell;
            LocalTargetInfo localTargetInfo2 = dest;
            string text = localTargetInfo2.ToString();
            string text2;
            if (!intVec4.IsValid)
            {
                text2 = "  [single leg]";
            }
            else
            {
                IntVec3 intVec2 = intVec3;
                text2 = "  [leg 1 of 2, mouth is " + intVec2.ToString() + "]";
            }

            ABV2Debug.Transit("  StartPath rewritten -> " + text + " (OnCell)" + text2);
            lastOutcome = SegmentOutcome.Rewritten;
            return true;
        }

        /// <summary>
        /// The cell a pawn should stand in to use this link: the footprint cell on the edge
        /// OPPOSITE the link's facing, centred on the run - i.e. just inside the notch.
        ///
        /// ⚠ ON THE FOOTPRINT, NOT ONE STEP OUTSIDE IT (§78c). It was outside, and the field
        /// report was "the animation still starts before they hit the stair texture": the
        /// clip legitimately began while the pawn was standing next to the staircase, so it
        /// shrank into a descent it had not walked onto yet. Pathing the pawn ONTO the art
        /// means the descent clip starts on the frame the pawn is standing on the treads,
        /// and the approach is done by the pawn's own pather at its own walk speed instead
        /// of being faked inside the clip.
        ///
        /// ⚠⚠ EVERY FAILURE RETURNS Invalid AND THE CALLER FALLS BACK. A link whose entry
        /// cell is walled in, out of bounds or unreachable must stay USABLE - degrading to
        /// a slightly wrong-looking animation is fine, refusing to path is the
        /// ladder-to-nowhere class of bug and is not.
        ///
        /// ⚠ CanReach IS THE EXPENSIVE CLAUSE AND IT IS ORDERED LAST. The cheap tests
        /// (bounds, standable) reject the walled-in case without touching the reachability
        /// cache. This runs inside a StartPath prefix, but only on the cross-band path that
        /// was already about to do a wormhole lookup, and CanReach is a read-only cache
        /// query - it cannot re-enter StartPath.
        ///
        /// ⚠ THE EDGE IS TAKEN FROM OccupiedRect, NOT FROM Position. For an even-sized
        /// footprint (the 2x2 stairs and elevator) Position is not the geometric centre -
        /// GenAdj.AdjustForRotation shifts it - so deriving the edge arithmetically from
        /// Position lands a cell off on half the rotations.
        /// </summary>
        private static IntVec3 EntryCellFor(Building_Door link, Pawn pawn)
        {
            if (link == null || pawn == null)
            {
                return IntVec3.Invalid;
            }
            Map map = link.Map;
            if (map == null)
            {
                return IntVec3.Invalid;
            }
            // §85: the edge math that used to live here moved to ABLinkApproach, so the
            // departure and the arrival read the SAME per-def table and cannot disagree
            // about which side of the art is open. The derived fallback there is this code
            // verbatim, so an untagged def behaves exactly as it did.
            IntVec3 c = ABLinkApproach.TryGet(link, out ABApproach a)
                ? a.mouth
                : link.Position; // elevator: no open side, any footprint cell will do
            // ⚠ WALKABLE, NOT STANDABLE. This cell is INSIDE the footprint, and the link is
            // a Building_Door - `passability` is PassThroughOnly, so GenGrid.Standable is
            // false for it and a Standable test would reject every link there is. Walkable
            // reads the path grid, where a door is passable, which is the question actually
            // being asked: can the pawn stand here on its way through.
            if (!c.InBounds(map) || !c.Walkable(map))
            {
                return IntVec3.Invalid;
            }
            // Must be on the pawn's own side of the wormhole, or we would be asking it to
            // path to a cell it can only get to by crossing the link it has not crossed yet.
            if (!ABBands.SameBand(map, pawn.Position, c))
            {
                return IntVec3.Invalid;
            }
            if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
            {
                return IntVec3.Invalid;
            }
            return c;
        }


        /// <summary>
        /// §85.19 The cell a pawn should walk to BEFORE stepping onto the link: the tile
        /// directly outside the opening, off the footprint. IntVec3.Invalid when there is no
        /// usable one, and then the caller keeps the single-leg behaviour.
        ///
        /// ⚠ STANDABLE, NOT WALKABLE - the opposite of EntryCellFor's test, and deliberately.
        /// That cell is OFF the footprint, so a walkable-but-not-standable cell out here is
        /// somebody else's doorway, and parking the whole approach in a doorway is the jam
        /// §85.4 exists to avoid. Inside the footprint the link is PassThroughOnly and only
        /// Walkable is true, which is why the two tests differ.
        ///
        /// ⚠ TRANSIENT OCCUPANCY IS NOT CHECKED ON PURPOSE. A pawn standing on the approach
        /// tile right now does not make it the wrong tile - it makes a queue, which is the
        /// correct behaviour for a staircase and the reason this fixes the crowd case. Cells
        /// are not reserved; a pawn that cannot get its exact cell comes to rest beside it and
        /// the sweep's stoppedShort backstop carries it.
        ///
        /// ⚠ ORDERED CHEAP-FIRST AND CanReach IS LAST, same as EntryCellFor: this runs inside
        /// a StartPath prefix and the reachability cache is the only expensive clause.
        /// </summary>
        private static IntVec3 ApproachCellFor(Building_Door link, Pawn pawn, IntVec3 entry)
        {
            if (link == null || pawn == null || !entry.IsValid)
            {
                return IntVec3.Invalid;   // no mouth resolved: nothing to stand in front of
            }
            if (pawn.Position == entry)
            {
                // Already standing on the art. Sending it out to the front tile so it can
                // walk back in would be a visible round trip for no gain - and it is the
                // exact shape of an oscillation, so it is refused rather than tolerated.
                // (This is the routine [single leg] of every ping-pong return trip - the
                // pawn is ordered back up the stairs it just came out of.)
                Decline(link, "pawn already on the mouth");
                return IntVec3.Invalid;
            }
            Map map = link.Map;
            if (map == null || !ABLinkApproach.TryGet(link, out ABApproach a))
            {
                Decline(link, "no approach resolved for this def/rotation");
                return IntVec3.Invalid;
            }
            IntVec3 c = a.outside;
            if (!c.InBounds(map) || !c.Standable(map))
            {
                Decline(link, "front tile " + c + " not standable");
                return IntVec3.Invalid;
            }
            // A seam runs between bands; the cell in front of a link built flush against one
            // belongs to another level entirely.
            if (!ABBands.SameBand(map, pawn.Position, c))
            {
                Decline(link, "front tile " + c + " is across a band seam");
                return IntVec3.Invalid;
            }
            if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
            {
                Decline(link, "front tile " + c + " unreachable for this pawn");
                return IntVec3.Invalid;
            }
            return c;
        }

        /// <summary>
        /// Rule 31: make the guard say which clause declined. §85.19's first field outing
        /// produced `[single leg]` for two rotations and NO WAY TO TELL WHY - four clauses,
        /// one silent Invalid. Behind LogTransit, so a Release build never builds the string.
        /// </summary>
        private static void Decline(Building_Door link, string why)
        {
            if (ABV2Debug.LogTransit)
            {
                ABV2Debug.Transit("  no front tile for " + link.def.defName + " "
                    + link.Rotation + " at " + link.Position + ": " + why);
            }
        }

        /// <summary>
        /// Per-tick sweep: carry across any pawn that has reached its near anchor.
        ///
        /// This, not PatherArrived, is now the primary trigger. Hanging the transit off the
        /// pather's arrival callback meant it only fired if the pawn finished a leg exactly
        /// on (or beside) the anchor, and the pather has many ways to end a leg somewhere
        /// else - a re-issued path, an interrupting job, stopping short. Sins ended four
        /// cells away and the transit never ran.
        ///
        /// Position is the honest condition: if the pawn is standing at the stairwell with a
        /// transit pending, take it. Cheap - the dictionary is empty almost always.
        /// </summary>
        [ABGameTick(70)]
        public static void TickTransits()
        {
            ABStairAnim.Sweep();
            TickCrossings();
            ABTransitVisuals.TickRoutes();
            DrainRestoreQueue();
            ReconcileAfterLoad();
            if (pending.Count == 0)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            tmpDone.Clear();
            tmpCarry.Clear();
            foreach (KeyValuePair<int, Transit> item in pending)
            {
                Transit value = item.Value;
                int num = value.expiresAtTick - ticksGame;
                if (num <= 0 || num > 4000 || value.near == null || value.far == null || !value.near.Spawned || !value.far.Spawned)
                {
                    tmpDone.Add(item.Key);
                    continue;
                }

                IReadOnlyList<Pawn> readOnlyList = value.near.Map?.mapPawns?.AllPawnsSpawned;
                Pawn pawn = ((readOnlyList != null) ? FindPawn(readOnlyList, item.Key) : null);
                if (pawn == null || !pawn.Spawned)
                {
                    tmpDone.Add(item.Key);
                }
                else if (value.job != null && pawn.CurJob != value.job)
                {
                    ABV2Debug.Transit("STALE " + pawn.LabelShort + ": record belongs to " + (value.job.def?.defName ?? "job") + ", pawn is now on " + (pawn.CurJob?.def?.defName ?? "none") + "; dropping");
                    tmpDone.Add(item.Key);
                }
                else
                {
                    if (!pawn.Position.InHorDistOf(value.near.Position, 3f))
                    {
                        continue;
                    }

                    bool flag = value.entryCell.IsValid && pawn.Position == value.entryCell;
                    if (!flag && value.entryCell.IsValid && value.approachCell.IsValid && !value.legTwo && pawn.Position == value.approachCell)
                    {
                        tmpLegTwo.Add(new KeyValuePair<Pawn, int>(pawn, item.Key));
                        continue;
                    }

                    bool flag2 = pawn.pather == null || !pawn.pather.Moving;
                    bool flag3 = value.nearSinceTick != 0 && ticksGame - value.nearSinceTick > 300;
                    bool flag4 = value.approachCell.IsValid && pawn.Position == value.approachCell;
                    if (flag | flag2 | flag3)
                    {
                        ABV2Debug.Transit("CARRY " + pawn.LabelShort + " at " + pawn.Position.ToString() + " | entryCell=" + (value.entryCell.IsValid ? value.entryCell.ToString() : "INVALID (never rewritten)") + " | onEntry=" + flag + " stoppedShort=" + flag2 + " outOfPatience=" + flag3 + " | nearSince=" + ((value.nearSinceTick == 0) ? "this tick" : (ticksGame - value.nearSinceTick + "t")) + " | moving=" + (pawn.pather != null && pawn.pather.Moving) + " | legTwo=" + value.legTwo + " onApproach=" + flag4 + " | dest=" + ((pawn.pather != null) ? pawn.pather.Destination.ToString() : "none"));
                        tmpDone.Add(item.Key);
                        tmpCarry.Add(new KeyValuePair<Pawn, Transit>(pawn, value));
                    }
                    else if (value.nearSinceTick == 0)
                    {
                        tmpStamp.Add(item.Key);
                    }
                }
            }

            for (int i = 0; i < tmpLegTwo.Count; i++)
            {
                Pawn key = tmpLegTwo[i].Key;
                if (pending.TryGetValue(tmpLegTwo[i].Value, out var value2) && key?.pather != null)
                {
                    value2.legTwo = true;
                    pending[tmpLegTwo[i].Value] = value2;
                    string[] array = new string[6]
                    {
                        "LEG2 ",
                        key.LabelShort,
                        " at ",
                        key.Position.ToString(),
                        " -> mouth ",
                        null
                    };
                    IntVec3 entryCell = value2.entryCell;
                    array[5] = entryCell.ToString();
                    ABV2Debug.Transit(string.Concat(array));
                    try
                    {
                        key.pather.StartPath(new LocalTargetInfo(value2.entryCell), PathEndMode.OnCell);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorOnce("[As above, So below] V2: leg-2 dispatch threw for " + key.LabelShortCap + ": " + ex, key.thingIDNumber ^ 0x2D6E2FE1);
                    }
                }
            }

            tmpLegTwo.Clear();
            for (int j = 0; j < tmpStamp.Count; j++)
            {
                if (pending.TryGetValue(tmpStamp[j], out var value3))
                {
                    value3.nearSinceTick = ticksGame;
                    pending[tmpStamp[j]] = value3;
                }
            }

            tmpStamp.Clear();
            for (int k = 0; k < tmpDone.Count; k++)
            {
                pending.Remove(tmpDone[k]);
            }

            for (int l = 0; l < tmpCarry.Count; l++)
            {
                try
                {
                    Carry(tmpCarry[l].Key, tmpCarry[l].Value);
                }
                catch (Exception ex2)
                {
                    Log.ErrorOnce("[As above, So below] V2: carry threw for " + tmpCarry[l].Key.LabelShortCap + ": " + ex2, tmpCarry[l].Key.thingIDNumber ^ 0x2D6E2FDF);
                }
            }

            tmpCarry.Clear();
            tmpDone.Clear();
        }

        private static readonly List<int> tmpDone = new List<int>();

        /// <summary>Records to stamp with nearSinceTick, deferred out of the enumeration.</summary>
        private static readonly List<int> tmpStamp = new List<int>();

        /// <summary>Pawns owed the §85.19 step onto the mouth, deferred out of the
        /// enumeration because StartPath re-enters TrySegment.</summary>
        private static readonly List<KeyValuePair<Pawn, int>> tmpLegTwo =
            new List<KeyValuePair<Pawn, int>>();

        /// <summary>Carries deferred out of the enumeration - see TickTransits phase 1.</summary>
        private static readonly List<KeyValuePair<Pawn, Transit>> tmpCarry =
            new List<KeyValuePair<Pawn, Transit>>();

        private static Pawn FindPawn(IReadOnlyList<Pawn> list, int thingId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].thingIDNumber == thingId)
                {
                    return list[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Where a transiting pawn should land on the far side.
        ///
        /// NOT simply the far anchor's own cell. Every transit used to teleport onto that one
        /// cell, so whenever two or more pawns crossed at around the same time they stacked on
        /// top of each other and read as a "clump" at the stairs - and because the anchor is a
        /// Building_Door subclass, a pawn parked on it is standing in a doorway, blocking the
        /// next arrival and any normal traffic through the stairwell. A single drafted pawn
        /// never showed it, which is exactly why it survived this long.
        ///
        /// PREFER THE ANCHOR ITSELF whenever it is free, and only step aside when it is not.
        ///
        /// The first version of this did the opposite - it excluded the anchor outright to
        /// stop pawns stacking - and that made things worse. The anchor is a Building_Door,
        /// which can only be entered from its four CARDINAL neighbours, so landing pawns
        /// beside it drops them straight into the approach lane. Two or three arrivals
        /// standing there wall the stairwell off, and following pawns jam on a corner and
        /// re-path the same failing route indefinitely.
        ///
        /// Landing ON the door is what vanilla door traffic does: a pawn occupies the cell
        /// for a moment and immediately walks on, because Carry re-issues its path. Stacking
        /// is only a risk while the cell is genuinely occupied, which is exactly the case the
        /// fallback handles.
        ///
        /// ⚠⚠ §85 SUPERSEDES THE PARAGRAPHS ABOVE FOR STAIRS, GRAND STAIRS AND LADDERS, and
        /// they are kept because they are still the reasoning for the elevator and for every
        /// fallback below. Both of the earlier answers - "the anchor" and "a cell near it" -
        /// were arguing about WHERE AROUND the building to stand, and the real answer was in
        /// the art all along: come out of the opening. `far.Position` is a cell INSIDE a
        /// multi-cell Building_Door, so an arrival parked there pays a door's path cost and
        /// reads as standing in the middle of the staircase; the random-near fallback put
        /// pawns at the SIDES, across a handrail. Field report, with a picture: land on the
        /// tile in front of whichever way the stairs open.
        ///
        /// The ordered candidates are all IN FRONT OF, or ON, the open edge - never beside
        /// it. Only when every one of them is taken does this fall through to the old
        /// anchor-then-anywhere behaviour, which is the crowd case and wants a body-shaped
        /// answer more than a pretty one.
        /// </summary>
        private static IntVec3 LandingCell(Pawn pawn, Building_Door far)
        {
            IntVec3 anchor = far.Position;
            Map map = far.Map;
            if (map == null)
            {
                return anchor;
            }
            if (ABLinkApproach.TryGet(far, out ABApproach a))
            {
                IntVec3 front = ApproachLanding(map, far, a);
                if (front.IsValid)
                {
                    return front;
                }
            }
            if (anchor.GetFirstPawn(map) == null)
            {
                return anchor;
            }
            // Occupied: step aside, but stay in the same band so a landing can never be
            // placed into the gutter or through a seam.
            if (CellFinder.TryFindRandomCellNear(anchor, map, LandingRadius,
                    c => c.InBounds(map)
                         && c != anchor
                         && c.Standable(map)
                         && c.GetFirstPawn(map) == null
                         && ABBands.SameBand(map, c, anchor),
                    out IntVec3 spot))
            {
                return spot;
            }
            return anchor;
        }

        /// <summary>Scratch for ApproachLanding. Never held across a call.</summary>
        private static readonly List<IntVec3> tmpLanding = new List<IntVec3>();

        /// <summary>
        /// The best free cell at the far link's opening, or Invalid when the whole mouth is
        /// blocked or occupied.
        ///
        /// Order: the tagged tile in front of the opening, then the rest of that outside
        /// edge working outwards from it, then the mouth cell itself, then the rest of the
        /// open edge ON the footprint. A pawn set down on the footprint is no worse than what
        /// shipped for eight windows, and it is still at the opening rather than beside it.
        ///
        /// ⚠ STANDABLE OUTSIDE, WALKABLE ON THE FOOTPRINT. The link is a Building_Door and
        /// its passability is PassThroughOnly, so GenGrid.Standable is FALSE for every cell
        /// of its own footprint - testing Standable there would reject the mouth of every
        /// link in the game (the same trap EntryCellFor documents). Outside the footprint the
        /// test must be Standable, because a Walkable-but-not-Standable cell out there is
        /// somebody ELSE's doorway, and parking an arrival in it is the jam this is trying to
        /// stop.
        /// </summary>
        private static IntVec3 ApproachLanding(Map map, Building_Door far, ABApproach a)
        {
            CellRect r = far.OccupiedRect();
            // 90 degrees from outward: the run of the open edge.
            IntVec3 lateral = new IntVec3(a.outward.z, 0, a.outward.x);
            tmpLanding.Clear();
            tmpLanding.Add(a.outside);
            for (int i = 1; i <= 2; i++)
            {
                IntVec3 p = a.outside + lateral * i;
                if (r.Contains(p - a.outward))
                {
                    tmpLanding.Add(p);
                }
                IntVec3 q = a.outside - lateral * i;
                if (r.Contains(q - a.outward))
                {
                    tmpLanding.Add(q);
                }
            }
            int outsideCount = tmpLanding.Count;
            tmpLanding.Add(a.mouth);
            for (int i = 1; i <= 2; i++)
            {
                IntVec3 p = a.mouth + lateral * i;
                if (r.Contains(p))
                {
                    tmpLanding.Add(p);
                }
                IntVec3 q = a.mouth - lateral * i;
                if (r.Contains(q))
                {
                    tmpLanding.Add(q);
                }
            }
            IntVec3 found = IntVec3.Invalid;
            for (int i = 0; i < tmpLanding.Count; i++)
            {
                IntVec3 c = tmpLanding[i];
                if (!c.InBounds(map)
                    || c.GetFirstPawn(map) != null
                    || !(i < outsideCount ? c.Standable(map) : c.Walkable(map))
                    // A landing may never cross a seam: the cell in front of a link built
                    // flush against a band edge is another level entirely.
                    || !ABBands.SameBand(map, c, far.Position))
                {
                    continue;
                }
                found = c;
                break;
            }
            tmpLanding.Clear();
            return found;
        }

        /// <summary>
        /// Either start the §78 hold - the pawn stays where it is and plays the entry clip -
        /// or carry instantly, which is precisely what this method did before §78 and is
        /// still what happens whenever there is no clip to play.
        /// </summary>
        private static void Carry(Pawn pawn, Transit t)
        {
            // Stop tracking: the pawn's NEXT arrival (at the real destination, just after
            // this) would otherwise trip the ARRIVED-NO-PENDING diagnostic and read as a
            // failure when it is simply the journey finishing normally. Done here rather
            // than in CarryNow so it happens once, whichever route the crossing takes.
            everSegmented.Remove(pawn.thingIDNumber);
            if (BeginCrossing(pawn, t))
            {
                return;
            }
            CarryNow(pawn, t);
        }

        /// <summary>
        /// Hold the pawn at the stairwell for the length of its entry clip, then teleport.
        /// Returns false when no hold was started, in which case the caller must carry now.
        ///
        /// ⚠⚠ WHY THIS IS NOT THE RUN #297 BUG WEARING A NEW HAT. That attempt held the pawn
        /// by REFUSING AN ARRIVAL - it gated TryConsumeArrival/ReadyToCarry, so vanilla's
        /// PatherArrived never ran, the leg never completed, the job re-issued StartPath,
        /// TrySegment re-segmented (the real destination is still on another band) and the
        /// pawn re-arrived at the same anchor forever. The transit DECISION was the thing
        /// being deferred, and it was deferred by a cosmetic condition.
        ///
        /// Here the decision is already final: by the time this runs the record has left
        /// `pending` (tick sweep phase 2, or Clear() on the arrival path) and everSegmented
        /// has been dropped. Nothing can re-segment this leg, because there is no leg left -
        /// StopDead ends it. What remains is a plain timer that owns one thing, the position
        /// write, and that timer cannot decline to fire: every exit from TickCrossings either
        /// teleports or abandons, and MaxHoldTicks backstops both.
        ///
        /// ⚠ STOPDEAD, NOT A SUPPRESSED ARRIVAL. StopDead releases the path and clears
        /// `moving` WITHOUT notifying the JobDriver, so the toil that completes on
        /// ToilCompleteMode.PatherArrival simply waits - no arrival is fired at the
        /// stairwell, so no toil completes in the wrong place, and because `moving` is false
        /// PatherTick never re-tests AtDestinationPosition and there is no per-tick loop.
        /// That IS the difference: #297 deferred the DECISION, this defers the MOVE.
        ///
        /// ⚠ THE STAGGER IS BELT TO STOPDEAD'S BRACES, and it is aspirational per pawn
        /// (StaggerDurationFactor, Anomaly awoken corpses). If a pawn shrugs it off and
        /// steps away, TickCrossings sees the cell change and fires the teleport early - a
        /// shorter show, never a desync.
        /// </summary>
        private static bool BeginCrossing(Pawn pawn, Transit t)
        {
            if (pawn == null || !pawn.Spawned || t.near == null || t.far == null)
            {
                return false;
            }

            ABBandMap aBBandMap = ABBands.CompOf(pawn.Map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return false;
            }

            if (holding.TryGetValue(pawn.thingIDNumber, out var value) && value.near == t.near && value.far == t.far)
            {
                value.job = pawn.CurJob;
                value.realDest = t.realDest;
                value.realPeMode = t.realPeMode;
                holding[pawn.thingIDNumber] = value;
                return true;
            }

            if (!ABStairAnim.Begin(pawn, t.near, t.far, aBBandMap.BandOf(t.near.Position), aBBandMap.BandOf(t.far.Position), out var entryTicks) || entryTicks <= 0)
            {
                return false;
            }

            pawn.pather?.StopDead();
            pawn.stances?.stagger?.StaggerFor(entryTicks, 0f);
            holding[pawn.thingIDNumber] = new Crossing
            {
                pawn = pawn,
                near = t.near,
                far = t.far,
                realDest = t.realDest,
                realPeMode = t.realPeMode,
                fireAtTick = Find.TickManager.TicksGame + entryTicks,
                holdCell = pawn.Position,
                job = pawn.CurJob
            };
            ABV2Debug.Transit("HOLDING " + pawn.LabelShort + " at " + pawn.Position.ToString() + " for " + entryTicks + "t (entry clip) before " + t.far.Position.ToString());
            return true;
        }

        /// <summary>
        /// Age every held crossing. Collect-then-act, for the same reason TickTransits is:
        /// CarryNow calls StartPath, which re-enters TrySegment, which writes `pending`.
        /// </summary>
        private static void TickCrossings()
        {
            if (holding.Count == 0 || Find.TickManager == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            tmpHoldDone.Clear();
            tmpHoldFire.Clear();
            foreach (KeyValuePair<int, Crossing> item in holding)
            {
                Crossing value = item.Value;
                Pawn pawn = value.pawn;
                if (pawn == null || !pawn.Spawned || pawn.Dead || value.near == null || value.far == null || !value.near.Spawned || !value.far.Spawned)
                {
                    tmpHoldDone.Add(item.Key);
                    ABStairAnim.Clear(pawn);
                    if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.CurJob == value.job)
                    {
                        ABV2Debug.Transit("HOLD ABANDONED for " + pawn.LabelShort + " (anchor lost mid-clip); ending job");
                        pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    }

                    continue;
                }

                if (pawn.CurJob != value.job)
                {
                    Map map = pawn.Map;
                    LocalTargetInfo localTargetInfo = ((pawn.pather != null) ? pawn.pather.Destination : LocalTargetInfo.Invalid);
                    if (localTargetInfo.IsValid && map != null && localTargetInfo.Cell.InBounds(map) && ABBands.SameBand(map, pawn.Position, localTargetInfo.Cell) && localTargetInfo.Cell != pawn.Position && !value.near.OccupiedRect().Contains(localTargetInfo.Cell))
                    {
                        tmpHoldDone.Add(item.Key);
                        ABStairAnim.Clear(pawn);
                        ABV2Debug.Transit("HOLD CANCELLED for " + pawn.LabelShort + " (job changed mid-clip to a same-level order " + localTargetInfo.Cell.ToString() + "); no teleport");
                        continue;
                    }

                    value.job = pawn.CurJob;
                    tmpHoldAdopt.Add(new KeyValuePair<int, Crossing>(item.Key, value));
                    ABV2Debug.Transit("HOLD ADOPTED by " + pawn.LabelShort + "'s new job " + (pawn.CurJob?.def?.defName ?? "none") + " (same crossing wanted)");
                }

                int num = value.fireAtTick - ticksGame;
                if (num <= 0 || num > 300 || pawn.Position != value.holdCell)
                {
                    tmpHoldDone.Add(item.Key);
                    tmpHoldFire.Add(value);
                }
            }

            for (int i = 0; i < tmpHoldAdopt.Count; i++)
            {
                if (holding.ContainsKey(tmpHoldAdopt[i].Key))
                {
                    holding[tmpHoldAdopt[i].Key] = tmpHoldAdopt[i].Value;
                }
            }

            tmpHoldAdopt.Clear();
            for (int j = 0; j < tmpHoldDone.Count; j++)
            {
                holding.Remove(tmpHoldDone[j]);
            }

            for (int k = 0; k < tmpHoldFire.Count; k++)
            {
                Crossing crossing = tmpHoldFire[k];
                try
                {
                    CarryNow(crossing.pawn, new Transit { near = crossing.near, far = crossing.far, realDest = crossing.realDest, realPeMode = crossing.realPeMode });
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[As above, So below] V2: held carry threw for " + crossing.pawn.LabelShortCap + ": " + ex, crossing.pawn.thingIDNumber ^ 0x2D6E2FE0);
                    crossing.pawn.jobs?.EndCurrentJob(JobCondition.Errored);
                }
            }

            tmpHoldDone.Clear();
            tmpHoldFire.Clear();
        }

        /// <summary>The position write and everything downstream of it. Reached either
        /// straight from Carry (no clip) or from TickCrossings when the hold expires.</summary>
        private static void CarryNow(Pawn pawn, Transit t)
        {
            IntVec3 intVec = LandingCell(pawn, t.far);
            string[] array = new string[10]
            {
                "TRANSITED ",
                pawn.LabelShort,
                " ",
                t.near.Position.ToString(),
                " -> ",
                null,
                null,
                null,
                null,
                null
            };
            IntVec3 intVec2 = intVec;
            array[5] = intVec2.ToString();
            array[6] = " (anchor ";
            array[7] = t.far.Position.ToString();
            array[8] = "); resuming to ";
            array[9] = t.realDest.Cell.ToString();
            ABV2Debug.Transit(string.Concat(array));
            pawn.Position = intVec;
            pawn.Notify_Teleported(endCurrentJob: false);
            ABFogReveal.RevealArrival(pawn, intVec);
            CarryRopees(pawn, t.near, intVec);
            ABStairAnim.NotifyCarried(pawn, t.far, intVec);
            ABBandView.FollowTransit(pawn);
            ABTransitVisuals.Clear(pawn);
            if (t.realDest.HasThing && (t.realDest.ThingDestroyed || !t.realDest.Thing.Spawned))
            {
                ABV2Debug.Transit("  destination gone mid-transit; stopping at " + t.far.Position.ToString());
                pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
            }
            else if (t.realDest.IsValid)
            {
                pawn.pather?.StartPath(t.realDest, t.realPeMode);
            }
            else
            {
                intVec2 = intVec;
                ABV2Debug.Transit("  destination no longer valid; ending job at " + intVec2.ToString());
                pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
            }
        }

        /// <summary>Called from the PatherArrived prefix. Returns true when it consumed
        /// the arrival (pawn transited and was re-dispatched), meaning the JobDriver
        /// must NOT be told it arrived - the journey is not over.</summary>
        public static bool TryConsumeArrival(Pawn_PathFollower pather, Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!pending.TryGetValue(pawn.thingIDNumber, out var value))
            {
                return TryRecoverArrival(pather, pawn);
            }

            if (value.near == null || value.far == null || !value.near.Spawned || !value.far.Spawned)
            {
                ABV2Debug.Transit("ARRIVED-ANCHOR-GONE " + pawn.LabelShort + " at " + pawn.Position.ToString());
                Clear(pawn);
                return false;
            }

            if (!pawn.Position.InHorDistOf(value.near.Position, 3f))
            {
                Map map = pawn.Map;
                ABV2Debug.Transit("ARRIVE-MISMATCH " + pawn.LabelShort + " at " + pawn.Position.ToString() + " (band " + ABBands.BandOf(map, pawn.Position) + ") expected within " + 3 + " of near " + value.near.Position.ToString() + " (band " + ABBands.BandOf(map, value.near.Position) + "), far " + value.far.Position.ToString() + " (band " + ABBands.BandOf(map, value.far.Position) + ") | job=" + (pawn.CurJob?.def?.defName ?? "none") + " | entryCell=" + (value.entryCell.IsValid ? value.entryCell.ToString() : "INVALID") + " | patherDest=" + ((pawn.pather != null) ? pawn.pather.Destination.ToString() : "none") + " | moving=" + (pawn.pather != null && pawn.pather.Moving) + " | aimedAtEntry=" + (pawn.pather != null && value.entryCell.IsValid && pawn.pather.Destination.Cell == value.entryCell));
                if (!ABBands.SameBand(map, pawn.Position, value.near.Position))
                {
                    ABV2Debug.Transit("  stale record dropped (near anchor is in another band)");
                    Clear(pawn);
                }

                return false;
            }

            if (value.entryCell.IsValid && value.approachCell.IsValid && !value.legTwo && pawn.Position == value.approachCell && pawn.Position != value.entryCell && pawn.pather != null)
            {
                value.legTwo = true;
                pending[pawn.thingIDNumber] = value;
                string[] array = new string[6]
                {
                    "LEG2-ON-ARRIVAL ",
                    pawn.LabelShort,
                    " at ",
                    pawn.Position.ToString(),
                    " -> mouth ",
                    null
                };
                IntVec3 entryCell = value.entryCell;
                array[5] = entryCell.ToString();
                ABV2Debug.Transit(string.Concat(array));
                pawn.pather.StartPath(new LocalTargetInfo(value.entryCell), PathEndMode.OnCell);
                return true;
            }

            Clear(pawn);
            if (BeginCrossing(pawn, value))
            {
                return true;
            }

            CarryNow(pawn, value);
            return true;
        }
        private static bool TryRecoverArrival(Pawn_PathFollower pather, Pawn pawn)
        {
            int thingIDNumber = pawn.thingIDNumber;
            if (!lastReal.TryGetValue(thingIDNumber, out var value))
            {
                return false;
            }

            Job curJob = pawn.CurJob;
            if (value.job == null || curJob != value.job || !value.dest.IsValid || pather == null || (value.dest.HasThing && (value.dest.ThingDestroyed || !value.dest.Thing.Spawned)))
            {
                lastReal.Remove(thingIDNumber);
                return false;
            }

            if (pawn.CanReachImmediate(value.dest, value.peMode))
            {
                lastReal.Remove(thingIDNumber);
                return false;
            }

            if (value.retries >= 2)
            {
                ABV2Debug.Transit("ARRIVED-NO-PENDING " + pawn.LabelShort + " at " + pawn.Position.ToString() + " - recovery budget spent; delivering the arrival");
                lastReal.Remove(thingIDNumber);
                return false;
            }

            value.retries++;
            lastReal[thingIDNumber] = value;
            ABV2Debug.Transit("ARRIVED-NO-PENDING " + pawn.LabelShort + " at " + pawn.Position.ToString() + " - record was dropped before arrival; re-issuing the real leg to " + value.dest.Cell.ToString() + " (" + value.retries + "/" + 2 + ")");
            pather.StartPath(value.dest, value.peMode);
            return true;
        }

        private static void CarryRopees(Pawn pawn, Building_Door near, IntVec3 landing)
        {
            List<Pawn> list = pawn?.roping?.Ropees;
            if (list == null || list.Count == 0 || near == null || !near.Spawned)
            {
                return;
            }

            Map map = pawn.Map;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn2 = list[i];
                if (pawn2 != null && pawn2 != pawn && pawn2.Spawned && pawn2.Map == map && pawn2.Position.InHorDistOf(near.Position, 4f) && CellFinder.TryFindRandomCellNear(landing, map, 2, (IntVec3 c) => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null && ABBands.SameBand(map, c, landing), out var result))
                {
                    string[] array = new string[6]
                    {
                        "  ropee ",
                        pawn2.LabelShort,
                        " carried with ",
                        pawn.LabelShort,
                        " -> ",
                        null
                    };
                    IntVec3 intVec = result;
                    array[5] = intVec.ToString();
                    ABV2Debug.Transit(string.Concat(array));
                    pawn2.Position = result;
                    pawn2.Notify_Teleported(endCurrentJob: false);
                    ABFogReveal.RevealArrival(pawn2, result);
                }
            }
        }

        internal static void NoteIfDoomed(Pawn pawn, LocalTargetInfo dest)
        {
            if (pawn == null || !pawn.Spawned || !dest.IsValid)
            {
                return;
            }

            Map map = pawn.Map;
            if (map == null || !ABBands.Banded(map))
            {
                return;
            }

            if (ABBands.SameBand(map, pawn.Position, dest.Cell))
            {
                if (lastOutcome == SegmentOutcome.Rewritten)
                {
                    segmentedOk++;
                }

                return;
            }

            switch (lastOutcome)
            {
                case SegmentOutcome.NoLinkPair:
                    doomedNoLinkPair++;
                    break;
                case SegmentOutcome.CollapsedOntoSelf:
                    doomedCollapsed++;
                    break;
                case SegmentOutcome.FailedFast:
                    break;
                default:
                    doomedOther++;
                    break;
            }
        }

        internal static void ResetDoomedStats()
        {
            doomedNoLinkPair = 0;
            doomedCollapsed = 0;
            doomedOther = 0;
            segmentedOk = 0;
            failedFastNoLink = 0;
            arrivedOnMouth = 0;
        }

        internal static bool ConsumeDoomedRequest(Pawn requester)
        {
            return doomedPawn != null && requester != null && requester == doomedPawn && doomedTick == (Find.TickManager?.TicksGame ?? (-2));
        }

        internal static void ClearDoomedMark()
        {
            doomedPawn = null;
            doomedTick = -1;
        }

        private static void MarkNextRequestDoomed(Pawn pawn)
        {
            doomedPawn = pawn;
            doomedTick = Find.TickManager?.TicksGame ?? (-1);
        }

        private static void ReconcileAfterLoad()
        {
            if (reconcileQueue.Count == 0)
            {
                return;
            }

            int num = 0;
            try
            {
                for (int i = 0; i < reconcileQueue.Count; i++)
                {
                    Map map = reconcileQueue[i];
                    if (map == null || !ABBands.Banded(map))
                    {
                        continue;
                    }

                    IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < allPawnsSpawned.Count; j++)
                    {
                        try
                        {
                            Pawn pawn = allPawnsSpawned[j];
                            if (pawn != null && pawn.Spawned && pawn.health != null && !pawn.Dead && !pawn.Downed && pawn.pather != null && !pawn.pather.Moving && pawn.jobs != null && pawn.CurJob != null && pawn.jobs.curDriver != null && ABStuckWatchdog.CurToilMode(pawn) == ToilCompleteMode.PatherArrival && (pawn.stances == null || !pawn.stances.FullBodyBusy) && !WantsNextToil(pawn.jobs.curDriver))
                            {
                                ABV2Debug.Transit("LOAD-RECONCILE " + pawn.LabelShort + " at " + pawn.Position.ToString() + ": stopped pather under a PatherArrival toil (" + (pawn.CurJob.def?.defName ?? "job") + "); ending the job so it re-plans");
                                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                                num++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WarningOnce("[As above, So below] load reconcile skipped a pawn: " + ex.Message, 11218507);
                        }
                    }
                }
            }
            finally
            {
                reconcileQueue.Clear();
            }

            if (num > 0)
            {
                Log.Message("[As above, So below] freed " + num + " pawn(s) saved mid-crossing.");
            }
        }

        private static bool WantsNextToil(JobDriver driver)
        {
            if (driver == null || WantBeginNextToilField == null)
            {
                return false;
            }

            try
            {
                object value = WantBeginNextToilField.GetValue(driver);
                bool flag = default;
                int num;
                if (value is bool)
                {
                    flag = (bool)value;
                    num = 1;
                }
                else
                {
                    num = 0;
                }

                return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void DrainRestoreQueue()
        {
            if (restoreQueue.Count == 0)
            {
                return;
            }

            int num = 0;
            for (int i = 0; i < restoreQueue.Count; i++)
            {
                Map key = restoreQueue[i].Key;
                List<TransitRecord> value = restoreQueue[i].Value;
                for (int j = 0; j < value.Count; j++)
                {
                    TransitRecord transitRecord = value[j];
                    if (transitRecord?.pawn == null || !transitRecord.pawn.Spawned || transitRecord.pawn.Map != key || transitRecord.near == null || transitRecord.far == null || !transitRecord.near.Spawned || !transitRecord.far.Spawned || !transitRecord.realDest.IsValid)
                    {
                        continue;
                    }

                    Pawn_PathFollower pather = transitRecord.pawn.pather;
                    Job curJob = transitRecord.pawn.CurJob;
                    if (pather != null && pather.Moving && curJob != null && pather.Destination.IsValid)
                    {
                        IntVec3 cell = pather.Destination.Cell;
                        if (cell == transitRecord.entryCell || cell == transitRecord.approachCell || transitRecord.near.OccupiedRect().Contains(cell))
                        {
                            pending[transitRecord.pawn.thingIDNumber] = new Transit
                            {
                                realDest = transitRecord.realDest,
                                realPeMode = transitRecord.realPeMode,
                                near = transitRecord.near,
                                far = transitRecord.far,
                                entryCell = transitRecord.entryCell,
                                approachCell = transitRecord.approachCell,
                                legTwo = transitRecord.legTwo,
                                startedTick = transitRecord.startedTick,
                                expiresAtTick = transitRecord.expiresAtTick,
                                nearSinceTick = transitRecord.nearSinceTick,
                                job = curJob
                            };
                            lastReal[transitRecord.pawn.thingIDNumber] = new RealLeg
                            {
                                dest = transitRecord.realDest,
                                peMode = transitRecord.realPeMode,
                                job = curJob
                            };
                            everSegmented.Add(transitRecord.pawn.thingIDNumber);
                            num++;
                        }
                    }
                }
            }

            restoreQueue.Clear();
            if (num > 0)
            {
                ABV2Debug.Transit("RESTORED " + num + " transit record(s) from the save");
            }
        }

        public static void QueueRestore(Map map, List<TransitRecord> records)
        {
            if (map == null)
            {
                return;
            }

            for (int num = restoreQueue.Count - 1; num >= 0; num--)
            {
                if (restoreQueue[num].Key == map)
                {
                    restoreQueue.RemoveAt(num);
                }
            }

            if (records != null && records.Count > 0)
            {
                restoreQueue.Add(new KeyValuePair<Map, List<TransitRecord>>(map, records));
            }

            if (!reconcileQueue.Contains(map))
            {
                reconcileQueue.Add(map);
            }
        }

        public static List<TransitRecord> CollectForSave(Map map)
        {
            List<TransitRecord> list = new List<TransitRecord>();
            if (map == null || pending.Count == 0)
            {
                return list;
            }

            IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
            foreach (KeyValuePair<int, Transit> item in pending)
            {
                Transit value = item.Value;
                if (value.near != null && value.far != null && value.near.Map == map)
                {
                    Pawn pawn = FindPawn(allPawnsSpawned, item.Key);
                    if (pawn != null)
                    {
                        list.Add(new TransitRecord { pawn = pawn, near = value.near, far = value.far, realDest = value.realDest, realPeMode = value.realPeMode, entryCell = value.entryCell, approachCell = value.approachCell, legTwo = value.legTwo, startedTick = value.startedTick, expiresAtTick = value.expiresAtTick, nearSinceTick = value.nearSinceTick });
                    }
                }
            }

            return list;
        }

        internal static int arrivedOnMouth;

        internal static int failedFastNoLink;

        internal static int segmentedOk;

        internal static int doomedOther;

        internal static int doomedCollapsed;

        internal static int doomedNoLinkPair;

        internal static SegmentOutcome lastOutcome;

        private static int doomedTick = -1;

        private static Pawn doomedPawn;

        private static readonly List<KeyValuePair<int, Crossing>> tmpHoldAdopt = new List<KeyValuePair<int, Crossing>>();

        private static readonly FieldInfo WantBeginNextToilField = AccessTools.Field(typeof(JobDriver), "wantBeginNextToil");

        private static readonly List<Map> reconcileQueue = new List<Map>();

        private static readonly List<KeyValuePair<Map, List<TransitRecord>>> restoreQueue = new List<KeyValuePair<Map, List<TransitRecord>>>();

        private const int MaxArrivalRecoveries = 2;

        private static readonly Dictionary<int, RealLeg> lastReal = new Dictionary<int, RealLeg>();

        internal enum SegmentOutcome : byte
        {
            NotOurs,
            NoLinkPair,
            CollapsedOntoSelf,
            Rewritten,
            FailedFast
        }

        public sealed class TransitRecord : IExposable
        {
            public Pawn pawn;
            public Building_Door near;
            public Building_Door far;
            public LocalTargetInfo realDest = LocalTargetInfo.Invalid;
            public PathEndMode realPeMode = PathEndMode.OnCell;
            public IntVec3 entryCell = IntVec3.Invalid;
            public IntVec3 approachCell = IntVec3.Invalid;
            public bool legTwo;
            public int startedTick;
            public int expiresAtTick;
            public int nearSinceTick;
            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_References.Look(ref near, "near");
                Scribe_References.Look(ref far, "far");
                Scribe_TargetInfo.Look(ref realDest, "realDest");
                Scribe_Values.Look(ref realPeMode, "realPeMode", PathEndMode.OnCell);
                Scribe_Values.Look(ref entryCell, "entryCell", IntVec3.Invalid);
                Scribe_Values.Look(ref approachCell, "approachCell", IntVec3.Invalid);
                Scribe_Values.Look(ref legTwo, "legTwo", defaultValue: false);
                Scribe_Values.Look(ref startedTick, "startedTick", 0);
                Scribe_Values.Look(ref expiresAtTick, "expiresAtTick", 0);
                Scribe_Values.Look(ref nearSinceTick, "nearSinceTick", 0);
            }
        }

        private struct RealLeg
        {
            public LocalTargetInfo dest;
            public PathEndMode peMode;
            public Job job;
            public int retries;
        }

    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_PathFollower_ABStartPath
    {
        private static void Prefix(Pawn ___pawn, ref LocalTargetInfo dest, ref PathEndMode peMode)
        {
            try
            {
                ABWormholePather.lastOutcome = ABWormholePather.SegmentOutcome.NotOurs;
                ABWormholePather.TrySegment(___pawn, ref dest, ref peMode);
                ABWormholePather.NoteIfDoomed(___pawn, dest);
            }
            catch (Exception ex)
            {
                Log.Error("[As above, So below] V2 spike: StartPath segmentation threw: " + ex);
            }
        }
        private static void Postfix()
        {
            ABWormholePather.ClearDoomedMark();
        }

    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
    public static class Patch_PathFollower_ABPatherArrived
    {
        private static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            try
            {
                return !ABWormholePather.TryConsumeArrival(__instance, ___pawn);
            }
            catch (Exception e)
            {
                Log.Error(ABLog.Tag + " V2 spike: transit arrival threw: " + e);
                ABWormholePather.Clear(___pawn);
                return true;
            }
        }
    }
}
