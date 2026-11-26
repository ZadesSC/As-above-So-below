using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// Automatic detector for "pawn is stuck", in the two shapes it actually takes.
    ///
    /// Manual probing cannot catch either reliably: any single sample lands on a frame where
    /// everything looks healthy. Only a comparison ACROSS ticks can see them.
    ///
    /// EPISODE A - WALKING, NOT ARRIVING (the original). The pawn reports moving=True with a
    /// valid short path but keeps stepping and re-targeting without making progress.
    ///
    /// EPISODE B - FROZEN UNDER A LIVE JOB (added after the stairwell hang). moving=False,
    /// and nothing will ever change that.
    ///
    /// ⚠⚠ EPISODE B EXISTED FOR MONTHS AND THIS FILE COULD NOT SEE IT. The original report
    /// was gated on `moving`, which is precisely the flag a wedged pawn does not have:
    /// Notify_Teleported -> Pawn_PathFollower.StopDead() clears the path and sets
    /// moving=false WITHOUT notifying the JobDriver, so a toil that completes on
    /// ToilCompleteMode.PatherArrival waits forever. The instrument was structurally unable
    /// to report the bug it was built to find - three of five early returns again.
    ///
    /// ⚠ THE DISCRIMINATOR IS THE TOIL'S COMPLETE MODE, NOT THE STILL TIME. Plenty of healthy
    /// pawns stand motionless under a live job for minutes (crafting, mining, surgery,
    /// charging, sleeping) - a pure "has not moved" test would drown the log. A toil that can
    /// ONLY be completed by a pushed pather arrival, on a pather that is not moving, is
    /// unambiguously wedged: there is no code path left that can finish it. Delay / Never /
    /// FinishedBusy toils are excluded because something else is still driving them.
    ///
    /// Watch set: anything within WatchRadius of a wormhole anchor (episode A happens at the
    /// stairwell) plus every player-faction pawn anywhere (episode B strands a pawn wherever
    /// its last leg ended, typically nowhere near the stairs - a mech parked on the surface
    /// that "forgot" to go back down is the reported case).
    ///
    /// Reading the report:
    ///   FROZEN                       -> live job on a dead pather; see the toil fields
    ///   path NOT FOUND               -> connectivity: regions connected, no walkable route
    ///   path FOUND, destChanges high -> re-targeting loop (a job giver re-picking every tick)
    ///   path FOUND, destChanges low  -> movement blocked (traffic, door, reservation)
    /// </summary>
    public static class ABStuckWatchdog
    {
        private const int StuckTicks = 180;   // ~3 seconds at normal speed
        private const int WatchRadius = 8;    // cells from an anchor

        private struct Watch
        {
            public IntVec3 lastPos;
            public IntVec3 lastDest;
            public int stillSinceTick;
            public int destChanges;
            public bool reported;
            public bool lastMoving;
        }

        private static readonly Dictionary<int, Watch> watching = new Dictionary<int, Watch>();

        private static readonly List<int> tmpDrop = new List<int>();

        // ⚠⚠ VANILLA'S OWN TWO GATES, READ RATHER THAN RE-DERIVED (rule 36). PatherTick
        // opens with `if (WillCollideWithPawnAt(Position, forceOnlyStanding: true,
        // useId: true)) { if (FailedToFindCloseUnoccupiedCellRecently()) return; }` -
        // an early return AHEAD of every movement statement, which is the whole of
        // §89. Reimplementing the collision test was rejected: the `useId` tie-break
        // decides WHICH of two co-located pawns is allowed to yield, and guessing it
        // wrong inverts the answer on exactly the case being diagnosed.
        //
        // Reflected as MethodInfo/FieldInfo rather than delegates so a vanilla rename
        // leaves them null and the report degrades to its old wording, instead of
        // throwing at type-init and taking the whole watchdog down with it.
        private static readonly MethodInfo WillCollideMethod =
            AccessTools.Method(typeof(Pawn_PathFollower), "WillCollideWithPawnAt",
                new[] { typeof(IntVec3), typeof(bool), typeof(bool) });

        private static readonly FieldInfo FailedTicksField = AccessTools.Field(
            typeof(Pawn_PathFollower), "failedToFindCloseUnoccupiedCellTicks");

        /// <summary>Pawn-id keyed, so it must not cross a game load - see the banner on
        /// ABWormholePather.ResetForNewGame. Stale entries here only cost a false episode
        /// (the watchdog would compare a loaded pawn against a previous session's position),
        /// but a diagnostic that cries wolf after every load is worse than none.</summary>
        [ABGameReset]
        public static void ResetForNewGame()
        {
            watching.Clear();
            tmpDrop.Clear();
        }

        [ABGameTick(75)]
        public static void Tick()
        {
            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame % 30 != 0)
            {
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null || !ABBands.Banded(currentMap) || ABWormhole.PairCount(currentMap) == 0)
            {
                return;
            }

            IReadOnlyList<Pawn> allPawnsSpawned = currentMap.mapPawns.AllPawnsSpawned;
            tmpDrop.Clear();
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                Pawn pawn = allPawnsSpawned[i];
                if (pawn == null || !pawn.Spawned || pawn.Dead)
                {
                    continue;
                }

                if (!ABWormhole.NearAnyAnchor(currentMap, pawn.Position, 8) && pawn.Faction != Faction.OfPlayer)
                {
                    watching.Remove(pawn.thingIDNumber);
                    continue;
                }

                IntVec3 intVec = ((pawn.pather != null && pawn.pather.Destination.IsValid) ? pawn.pather.Destination.Cell : IntVec3.Invalid);
                bool flag = pawn.pather != null && pawn.pather.Moving;
                if (!watching.TryGetValue(pawn.thingIDNumber, out var value))
                {
                    watching[pawn.thingIDNumber] = new Watch
                    {
                        lastPos = pawn.Position,
                        lastDest = intVec,
                        stillSinceTick = ticksGame,
                        destChanges = 0,
                        reported = false,
                        lastMoving = flag
                    };
                    continue;
                }

                if (flag && !value.lastMoving)
                {
                    value.stillSinceTick = ticksGame;
                    value.destChanges = 0;
                    value.reported = false;
                }

                value.lastMoving = flag;
                if (intVec != value.lastDest)
                {
                    value.destChanges++;
                    value.lastDest = intVec;
                }

                if (pawn.Position != value.lastPos)
                {
                    value.lastPos = pawn.Position;
                    value.stillSinceTick = ticksGame;
                    value.destChanges = 0;
                    value.reported = false;
                    watching[pawn.thingIDNumber] = value;
                    continue;
                }

                bool flag2 = flag;
                if (!value.reported && ticksGame - value.stillSinceTick >= 180 && (flag2 || WedgedOnArrival(pawn)))
                {
                    value.reported = true;
                    Report(currentMap, pawn, intVec, ticksGame - value.stillSinceTick, value.destChanges, flag2);
                    TryBreakSelfStep(pawn);
                }

                watching[pawn.thingIDNumber] = value;
            }

            for (int j = 0; j < tmpDrop.Count; j++)
            {
                watching.Remove(tmpDrop[j]);
            }

            tmpDrop.Clear();
        }

        /// <summary>
        /// True when the pawn's current toil can only ever be completed by a pather arrival
        /// that can no longer happen.
        ///
        /// `JobDriver.CurToil` is protected, so it is read reflectively - acceptable here and
        /// nowhere else: this whole file is opt-in behind LogTransit and runs for a handful of
        /// pawns every 75 ticks. CurToilIndex and CurJob are checked first because the getter
        /// itself logs an error on an inconsistent driver, and a diagnostic that spams errors
        /// while diagnosing is worse than no diagnostic.
        /// </summary>
        private static readonly MethodInfo CurToilGetter =
            AccessTools.PropertyGetter(typeof(JobDriver), "CurToil");

        private static bool WedgedOnArrival(Pawn p)
        {
            return CurToilMode(p) == ToilCompleteMode.PatherArrival;
        }

        internal static ToilCompleteMode? CurToilMode(Pawn p)
        {
            JobDriver jobDriver = p.jobs?.curDriver;
            if (jobDriver == null || CurToilGetter == null || p.CurJob == null || jobDriver.CurToilIndex < 0)
            {
                return null;
            }

            try
            {
                return (CurToilGetter.Invoke(jobDriver, null) as Toil)?.defaultCompleteMode;
            }
            catch
            {
                return null;
            }
        }

        private static void Report(Map map, Pawn p, IntVec3 dest, int stillFor, int destChanges, bool moving)
        {
            if (!ABV2Debug.TracingAllowed)
            {
                Log.WarningOnce("[As above, So below] a pawn stood still for 3+ seconds under a live pather near a level link (possibly wedged; the watchdog repairs the one wedge it can identify). For the full per-pawn diagnostic, turn on verbose logging in this mod's settings (Diagnostics tab) and reproduce - dev mode alone does not enable it.", 11229121);
                return;
            }

            bool flag = false;
            string text = "no destination";
            if (dest.IsValid)
            {
                flag = p.CanReach(dest, PathEndMode.OnCell, Danger.Deadly);
                PawnPath pawnPath = null;
                try
                {
                    pawnPath = map.pathFinder.FindPathNow(p.Position, dest, TraverseParms.For(p));
                    text = ((pawnPath != null && pawnPath.Found) ? ("FOUND (" + pawnPath.NodesLeftCount + " nodes)") : "NOT FOUND");
                }
                finally
                {
                    pawnPath?.ReleaseToPool();
                }
            }

            bool flag2 = false;
            string text2 = "n/a";
            IntVec3 intVec;
            if (p.pather != null)
            {
                IntVec3 nextCell = p.pather.nextCell;
                flag2 = nextCell.IsValid && nextCell == p.Position;
                if (nextCell.IsValid && nextCell.InBounds(map))
                {
                    Building edifice = nextCell.GetEdifice(map);
                    Building_Door building_Door = edifice as Building_Door;
                    string[] array = new string[9];
                    intVec = nextCell;
                    array[0] = intVec.ToString();
                    array[1] = (flag2 ? " (== PAWN'S OWN CELL)" : string.Empty);
                    array[2] = " walkable=";
                    array[3] = nextCell.Walkable(map).ToString();
                    array[4] = " pawnsHere=";
                    array[5] = PawnCensus(map, nextCell, p);
                    array[6] = " edifice=";
                    array[7] = ((edifice != null) ? edifice.def.defName : "-");
                    array[8] = ((building_Door != null) ? (" door[open=" + building_Door.Open + " freePassage=" + building_Door.FreePassage + " blockedOpenMomentary=" + building_Door.BlockedOpenMomentary + "]") : string.Empty);
                    text2 = string.Concat(array);
                }
            }

            bool flag3 = CollidesHere(p);
            int num = UnstickGaveUpTicksAgo(p);
            if (WillCollideMethod == null || FailedTicksField == null)
            {
                Log.WarningOnce("[As above, So below] V2: stuck watchdog could not bind vanilla's PatherTick gates (WillCollideWithPawnAt=" + (WillCollideMethod != null) + ", failedToFindCloseUnoccupiedCellTicks=" + (FailedTicksField != null) + "); SELF-STACK episodes will be misreported as BLOCKED.", 2822337);
            }

            string text3;
            if (moving)
            {
                if (text == "NOT FOUND")
                {
                    text3 = "CONNECTIVITY - region says reachable, no walkable route";
                }
                else if (destChanges >= 3)
                {
                    text3 = "RE-TARGETING - destination changed " + destChanges + " times while standing still";
                }
                else
                {
                    text3 = ((num >= 0 || (flag3 & flag2)) ? ("SELF-STACK - another STANDING pawn shares this cell. Vanilla's PatherTick returns before ANY movement code while TryFindBestPawnStandCell keeps failing" + ((num >= 0) ? (" (it gave up " + num + " ticks ago; it re-tries every 100)") : " (collision confirmed, timestamp unreadable)") + ". NOT obstructed - co-located. Read pawnsHere: a co-occupant marked `transit` makes this OURS (§78's hold parks a crossing pawn with StopDead for 90t, §85.19 queues onto the approach tile); anything else is vanilla traffic and not our bug") : "BLOCKED - path exists and destination is stable, so movement is obstructed");
                }
            }
            else
            {
                text3 = "FROZEN - live job on a stopped pather; its toil completes only on a pather arrival that can no longer fire (teleport without re-dispatch, or a leg that ended without notifying the driver). Draft/undraft would clear it.";
            }

            string[] array2 = new string[55]
            {
                "[As above, So below] V2 STUCK: ",
                p.LabelShortCap,
                " at ",
                p.Position.ToString(),
                " band ",
                ABBands.BandOf(map, p.Position).ToString(),
                " | still for ",
                stillFor.ToString(),
                " ticks | job=",
                p.CurJob?.def?.defName ?? "none",
                " | toil=",
                ((p.jobs?.curDriver != null) ? p.jobs.curDriver.CurToilIndex : (-1)).ToString(),
                "/",
                CurToilMode(p)?.ToString() ?? "unknown",
                " ticksLeft=",
                ((p.jobs?.curDriver != null) ? p.jobs.curDriver.ticksLeftThisToil : (-1)).ToString(),
                " | moving=",
                moving.ToString(),
                " | carrying=",
                p.carryTracker?.CarriedThing?.LabelCap ?? "nothing",
                " | dest=",
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
            intVec = dest;
            array2[21] = intVec.ToString();
            array2[22] = " band ";
            array2[23] = (dest.IsValid ? ABBands.BandOf(map, dest) : (-1)).ToString();
            array2[24] = " | CanReach=";
            array2[25] = flag.ToString();
            array2[26] = " | path=";
            array2[27] = text;
            array2[28] = " | destChanges=";
            array2[29] = destChanges.ToString();
            array2[30] = " | pendingTransit=";
            array2[31] = ABWormholePather.HasPending(p).ToString();
            array2[32] = " | nextCell=";
            array2[33] = text2;
            array2[34] = " | busy=";
            array2[35] = (p.stances != null && p.stances.FullBodyBusy).ToString();
            array2[36] = " stance=";
            array2[37] = p.stances?.curStance?.GetType().Name ?? "none";
            array2[38] = " stunned=";
            array2[39] = (p.stances?.stunner?.Stunned == true).ToString();
            array2[40] = " staggered=";
            array2[41] = (p.stances?.stagger?.Staggered == true).ToString();
            array2[42] = ((p.stances?.stagger != null && p.stances.stagger.Staggered) ? ("(x" + p.stances.stagger.StaggerMoveSpeedFactor.ToString("0.00") + ")") : string.Empty);
            array2[43] = " | curPath=";
            ref string reference = ref array2[44];
            string text4;
            if (p.pather?.curPath == null)
            {
                text4 = "null";
            }
            else
            {
                text4 = (p.pather.curPath.Found ? (p.pather.curPath.NodesLeftCount + " left") : "NOT FOUND");
            }

            reference = text4;
            array2[45] = " request=";
            array2[46] = ((p.pather?.curPathRequest == null) ? "none" : "pending");
            array2[47] = " cellCost=";
            array2[48] = ((p.pather != null) ? (p.pather.nextCellCostLeft.ToString("0") + "/" + p.pather.nextCellCostTotal.ToString("0")) : "-");
            array2[49] = " | collidesHere=";
            array2[50] = flag3.ToString();
            array2[51] = " | unstickGaveUp=";
            array2[52] = ((num >= 0) ? (num + "t ago") : "no");
            array2[53] = " | verdict=";
            array2[54] = text3;
            string text5 = string.Concat(array2);
            Log.Warning(text5);
        }

        /// <summary>
        /// Every pawn standing on a cell, SELF INCLUDED and labelled as such.
        ///
        /// Self is counted rather than filtered because the count is the finding: one
        /// is normal, two is <see href="#">\u00a789</see>. Each is tagged moving/idle
        /// (vanilla's gate is <c>forceOnlyStanding</c>, so only idle ones wedge a
        /// pather) and, decisively, whether it is mid-transit - which is what tells
        /// a stairs-side stack of ours apart from ordinary vanilla traffic.
        /// </summary>
        private static string PawnCensus(Map map, IntVec3 c, Pawn self)
        {
            List<Thing> things = map.thingGrid.ThingsListAtFast(c);
            int count = 0;
            string names = string.Empty;
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Pawn q))
                {
                    continue;
                }
                count++;
                names += (names.Length > 0 ? "," : string.Empty)
                    + (q == self ? "SELF" : q.LabelShortCap)
                    + "(" + (q.pather != null && q.pather.Moving ? "moving" : "idle")
                    + (ABWormholePather.HasPending(q) ? ",TRANSIT" : string.Empty) + ")";
            }
            return count + (count > 0 ? " [" + names + "]" : string.Empty);
        }

        /// <summary>Vanilla's own "another standing pawn is on my cell" test, invoked
        /// rather than reimplemented - see the banner on WillCollideMethod.</summary>
        private static bool CollidesHere(Pawn p)
        {
            if (WillCollideMethod == null || p?.pather == null)
            {
                return false;
            }
            try
            {
                return WillCollideMethod.Invoke(p.pather,
                    new object[] { p.Position, true, true }) is bool b && b;
            }
            catch
            {
                return false; // a diagnostic must never be the thing that throws
            }
        }

        /// <summary>
        /// How long ago vanilla's unstick search last failed, or -1 if it has not
        /// failed recently.
        ///
        /// ⚠ THE 100-TICK WINDOW IS VANILLA'S, COPIED VERBATIM from
        /// <c>FailedToFindCloseUnoccupiedCellRecently</c>
        /// (<c>failedToFindCloseUnoccupiedCellTicks + 100 &gt; TicksGame</c>). Inside it,
        /// PatherTick returns early every tick. The default is -999999, so an
        /// untouched field falls out of the window and reads as "no".
        ///
        /// Reflection cost is irrelevant here: this runs once per REPORTED episode,
        /// not per tick.
        /// </summary>
        private static int UnstickGaveUpTicksAgo(Pawn p)
        {
            if (FailedTicksField == null || p?.pather == null)
            {
                return -1;
            }
            try
            {
                if (!(FailedTicksField.GetValue(p.pather) is int t))
                {
                    return -1;
                }
                int age = Find.TickManager.TicksGame - t;
                return age >= 0 && age < 100 ? age : -1;
            }
            catch
            {
                return -1;
            }
        }
        private static void TryBreakSelfStep(Pawn p)
        {
            Pawn_PathFollower pawn_PathFollower = p?.pather;
            if (pawn_PathFollower == null || !pawn_PathFollower.Moving)
            {
                return;
            }

            IntVec3 nextCell = pawn_PathFollower.nextCell;
            if (nextCell.IsValid && !(nextCell != p.Position) && pawn_PathFollower.curPathRequest == null && pawn_PathFollower.curPath == null && (p.stances == null || !p.stances.FullBodyBusy) && !CollidesHere(p))
            {
                Job job = p.jobs?.curJob;
                if (job == null)
                {
                    pawn_PathFollower.StopDead();
                    return;
                }

                ABV2Debug.Transit("SELF-STEP REPAIR: " + p.LabelShortCap + " at " + p.Position.ToString() + " had nextCell == its own cell while moving; ending " + (job.def?.defName ?? "job") + " to force a re-dispatch");
                pawn_PathFollower.StopDead();
                p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private const int SweepInterval = 30;

    }
}
