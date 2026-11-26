using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse.AI.Group;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// "Why is THIS pawn stuck here?" - a probe for the selected pawn.
    ///
    /// The failure this exists to catch is the one the architecture is most exposed to:
    /// Reachability is REGION based (a region is a cell set), while path production is CELL
    /// based and additionally refuses to cut a diagonal corner when either flanking cell is
    /// unwalkable (PathUtility.BlocksDiagonalMovement, applied in Pawn_PathFollower). A sky
    /// band is full of impassable AB_OpenAir holes, so a stairwell can easily sit where the
    /// only geometric link is diagonal: the region graph says CONNECTED, the pathfinder says
    /// NotFound, PathRequest.ValidateInt keeps letting the job through because it gates on
    /// CanReach, and the pawn re-issues forever while standing still on a corner.
    ///
    /// CanReach=True together with path=NOT FOUND is the signature. It cannot be inferred
    /// from watching the pawn, because a pawn blocked by traffic looks identical.
    /// </summary>
    public static partial class ABDevTools
    {
        /// <summary>`Pawn_PathFollower.peMode` is private. Read it rather than assuming,
        /// because the mode IS the question: OnCell and Touch disagree on every impassable
        /// target. Cached once; null means we fall back to Touch, which is what every
        /// construction and haul WorkGiver uses.</summary>
        private static readonly System.Reflection.FieldInfo StuckProbePeModeField =
            typeof(Pawn_PathFollower).GetField("peMode",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);

        /// <summary>The inputs `Region.DangerFor` actually reads, for one cell. Every access
        /// is guarded: an unwalkable cell has no region and a region can outlive its room,
        /// and a probe that throws tells you nothing.</summary>
        private static string StuckProbeDangerLine(Map map, IntVec3 c, Pawn pawn)
        {
            if (!c.IsValid || !c.InBounds(map))
            {
                return "out of bounds";
            }
            Region reg = c.GetRegion(map, RegionType.Set_All);
            if (reg == null)
            {
                return "NO REGION (cell is unwalkable or outside the region grid)";
            }
            Room room = reg.Room;
            if (room == null)
            {
                return "region " + reg.id + " but NO ROOM";
            }
            return "room temp " + room.Temperature.ToString("F1") + "C"
                + ", vacuum " + room.Vacuum.ToString("F2")
                + ", regionDanger=" + reg.DangerFor(pawn);
        }

        [DebugAction("As above", "AB2: why is this pawn stuck", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2WhyStuck()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            StringBuilder stringBuilder = new StringBuilder();
            if (pawn == null)
            {
                Map currentMap = Find.CurrentMap;
                if (currentMap == null)
                {
                    Log.Warning("[As above, So below] V2 stuck probe: no current map.");
                    return;
                }

                stringBuilder.AppendLine("NO SINGLE PAWN SELECTED. Colony census first, then an auto-picked target.");
                List<Pawn> list = currentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                Pawn pawn2 = null;
                for (int i = 0; i < list.Count; i++)
                {
                    Pawn pawn3 = list[i];
                    bool flag = pawn3.pather != null && pawn3.pather.Moving;
                    IntVec3 intVec = ((pawn3.pather != null && pawn3.pather.Destination.IsValid) ? pawn3.pather.Destination.Cell : IntVec3.Invalid);
                    string[] array = new string[12]
                    {
                        "  ",
                        pawn3.LabelShortCap,
                        " at ",
                        pawn3.Position.ToString(),
                        " band ",
                        ABBands.BandOf(currentMap, pawn3.Position).ToString(),
                        " | job=",
                        pawn3.CurJob?.def?.defName ?? "none",
                        " moving=",
                        flag.ToString(),
                        " dest=",
                        null
                    };
                    string text;
                    if (!intVec.IsValid)
                    {
                        text = "-";
                    }
                    else
                    {
                        IntVec3 intVec2 = intVec;
                        text = intVec2.ToString() + " band " + ABBands.BandOf(currentMap, intVec);
                    }

                    array[11] = text;
                    stringBuilder.AppendLine(string.Concat(array));
                    if (pawn2 == null && !flag && intVec.IsValid && intVec != pawn3.Position)
                    {
                        pawn2 = pawn3;
                    }
                }

                if (pawn2 == null && list.Count > 0)
                {
                    pawn2 = list[0];
                    stringBuilder.AppendLine("  >> nobody matched the stuck signature (stopped while holding a destination); falling back to the first pawn.");
                }

                if (pawn2 == null)
                {
                    Log.Warning("[As above, So below] V2 stuck probe:\n" + stringBuilder?.ToString() + "  no player pawns spawned on this map.");
                    return;
                }

                stringBuilder.AppendLine("  >> auto-picked " + pawn2.LabelShortCap);
                stringBuilder.AppendLine();
                pawn = pawn2;
            }

            Map map = pawn.Map;
            if (map == null)
            {
                Log.Warning("[As above, So below] V2 stuck probe: " + pawn.LabelShortCap + " is not spawned on a map.");
                return;
            }

            stringBuilder.AppendLine(pawn.LabelShortCap + " at " + pawn.Position.ToString() + " band " + ABBands.BandOf(map, pawn.Position));
            stringBuilder.AppendLine("  job=" + (pawn.CurJob?.def?.defName ?? "none") + " moving=" + (pawn.pather != null && pawn.pather.Moving) + " carrying=" + (pawn.carryTracker?.CarriedThing?.LabelCap ?? "nothing"));
            stringBuilder.AppendLine("  pendingTransit=" + ABWormholePather.HasPending(pawn));
            LocalTargetInfo localTargetInfo = pawn.CurJob?.targetA ?? LocalTargetInfo.Invalid;
            if (pawn.pather != null && pawn.pather.Destination.IsValid)
            {
                localTargetInfo = pawn.pather.Destination;
            }

            if (localTargetInfo.IsValid)
            {
                IntVec3 cell = localTargetInfo.Cell;
                IntVec3 intVec2 = cell;
                stringBuilder.AppendLine("  destination " + intVec2.ToString() + " band " + ABBands.BandOf(map, cell));
                PathEndMode peMode = PathEndMode.Touch;
                string text2 = "assumed Touch (reflection unavailable)";
                if (StuckProbePeModeField != null && pawn.pather != null)
                {
                    try
                    {
                        peMode = (PathEndMode)StuckProbePeModeField.GetValue(pawn.pather);
                        text2 = "live from pather";
                    }
                    catch (Exception)
                    {
                    }
                }

                stringBuilder.AppendLine("  pather peMode = " + peMode.ToString() + " (" + text2 + ")");
                bool flag2 = pawn.CanReach(localTargetInfo, PathEndMode.Touch, Danger.Some);
                bool flag3 = pawn.CanReach(localTargetInfo, PathEndMode.Touch, Danger.Deadly);
                Danger maxDanger = pawn.NormalMaxDanger();
                bool flag4 = pawn.CanReach(localTargetInfo, peMode, maxDanger);
                bool flag5 = pawn.CanReach(localTargetInfo, PathEndMode.OnCell, Danger.Deadly);
                stringBuilder.AppendLine("  reachability matrix:");
                stringBuilder.AppendLine("    Touch  + Danger.Some   (NORMAL WORK) = " + flag2);
                stringBuilder.AppendLine("    Touch  + Danger.Deadly (FORCED)      = " + flag3);
                stringBuilder.AppendLine("    " + peMode.ToString() + " + NormalMaxDanger (this pawn now) = " + flag4 + "   [NormalMaxDanger=" + maxDanger.ToString() + "]");
                stringBuilder.AppendLine("    OnCell + Danger.Deadly (what this probe used to ask) = " + flag5);
                if (!flag2 & flag3)
                {
                    stringBuilder.AppendLine("    >> DANGER IS THE DIFFERENTIATOR. Normal work refuses this route and forced accepts the SAME route, which is exactly the reported symptom. This is NOT a connectivity bug. Region.DangerFor returns Deadly when room temperature is outside SafeTemperatureRange expanded by 80C, OR when room.Vacuum > 0.5 and the pawn is concerned by vacuum. The next block says which.");
                }
                else if (!flag2 && !flag3)
                {
                    stringBuilder.AppendLine("    >> UNREACHABLE AT ANY DANGER LEVEL. Danger is NOT the cause; this is a real connectivity failure.");
                }

                FloatRange floatRange = pawn.SafeTemperatureRange();
                stringBuilder.AppendLine("  danger inputs:");
                stringBuilder.AppendLine("    pawn cell " + pawn.Position.ToString() + ": " + StuckProbeDangerLine(map, pawn.Position, pawn));
                intVec2 = cell;
                stringBuilder.AppendLine("    dest cell " + intVec2.ToString() + ": " + StuckProbeDangerLine(map, cell, pawn));
                string[] array2 = new string[6]
                {
                    "    SafeTemperatureRange = ",
                    null,
                    null,
                    null,
                    null,
                    null
                };
                FloatRange floatRange2 = floatRange;
                array2[1] = floatRange2.ToString();
                array2[2] = ", expandedBy(80) = ";
                array2[3] = floatRange.ExpandedBy(80f).ToString();
                array2[4] = ", ConcernedByVacuum = ";
                array2[5] = pawn.ConcernedByVacuum.ToString();
                stringBuilder.AppendLine(string.Concat(array2));
                bool flag6 = flag3;
                bool flag7 = ABBandComponents.RespectsForbiddenDoors(pawn);
                int num = ABBandComponents.ComponentOf(map, pawn.Position, flag7);
                int num2 = ABBandComponents.ComponentOf(map, cell, flag7);
                bool flag8 = ABBands.SameBand(map, pawn.Position, cell);
                bool flag9 = ABBandComponents.KnownDifferentComponents(map, pawn.Position, cell, flag7);
                stringBuilder.AppendLine("  component: pawn=" + num + " dest=" + num2 + "  sameBand=" + flag8 + "  knownDifferentIslands=" + flag9 + "  respectsForbiddenDoors=" + flag7);
                if (flag7)
                {
                    stringBuilder.AppendLine("    optimistic (forbid-blind) islands: pawn=" + ABBandComponents.ComponentOf(map, pawn.Position) + " dest=" + ABBandComponents.ComponentOf(map, cell) + "  - if these AGREE while the pair above DISAGREE, a forbidden door is what splits them.");
                }

                if (num < 0 || num2 < 0)
                {
                    stringBuilder.AppendLine("    >> AN ENDPOINT IS UNWALKABLE OR OFF-BAND (component -1). ComponentOf returns -1 for BOTH cases, and this is NORMAL for a wall frame, a door under construction, or any impassable build target: those are reached with Touch, not OnCell, so -1 here is NOT by itself evidence of a fault. Read the matrix above instead. Segmentation is deliberately DECLINED - unknown must mean leave it alone.");
                }
                else if (flag8 && !flag9)
                {
                    stringBuilder.AppendLine("    >> SAME ISLAND: no segmentation wanted. If the pawn cannot walk it, the component map disagrees with the pathfinder and THAT is the bug.");
                }
                else if (flag8)
                {
                    stringBuilder.AppendLine("    >> SAME BAND, DIFFERENT ISLAND: phase 3 SHOULD segment this. See the transit line below.");
                }

                bool flag10 = ABWormhole.TryGetTransit(map, pawn.Position, cell, pawn, out var near, out var far, localTargetInfo);
                stringBuilder.AppendLine("  TryGetTransit = " + (flag10 ? ("YES via " + near.Position.ToString() + " (comp " + ABBandComponents.ComponentOf(map, near.Position, flag7) + ") -> " + far.Position.ToString() + " (comp " + ABBandComponents.ComponentOf(map, far.Position, flag7) + ")") : ("NONE - " + ABWormhole.LastRefusalText)));
                stringBuilder.AppendLine("  wormhole pairs on map = " + ABWormhole.PairCount(map));
                if (num2 < 0)
                {
                    stringBuilder.AppendLine("  destination neighbours (the cells a Touch job stands on):");
                    int num3 = 0;
                    for (int j = 0; j < 8; j++)
                    {
                        IntVec3 intVec3 = cell + GenAdj.AdjacentCells[j];
                        if (!intVec3.InBounds(map))
                        {
                            intVec2 = intVec3;
                            stringBuilder.AppendLine("    " + intVec2.ToString() + "  OUT OF BOUNDS");
                            continue;
                        }

                        bool flag11 = intVec3.Walkable(map);
                        int num4 = ABBandComponents.ComponentOf(map, intVec3, flag7);
                        bool flag12 = ABBands.SameBand(map, intVec3, cell);
                        if ((flag11 && num4 >= 0) & flag12)
                        {
                            num3++;
                        }

                        Building edifice = intVec3.GetEdifice(map);
                        string[] array3 = new string[12]
                        {
                            "    ",
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
                        intVec2 = intVec3;
                        array3[1] = intVec2.ToString();
                        array3[2] = "  walkable=";
                        array3[3] = flag11.ToString();
                        array3[4] = "  island=";
                        array3[5] = num4.ToString();
                        array3[6] = ((num4 >= 0 && num4 == num) ? " (PAWN'S OWN ISLAND)" : string.Empty);
                        array3[7] = "  band=";
                        array3[8] = ABBands.BandOf(map, intVec3).ToString();
                        array3[9] = (flag12 ? string.Empty : "  <- OTHER BAND, cannot be touched from");
                        array3[10] = "  edifice=";
                        array3[11] = ((edifice != null) ? edifice.def.defName : "-");
                        stringBuilder.AppendLine(string.Concat(array3));
                    }

                    stringBuilder.AppendLine("    " + num3 + " of 8 are standable, in the target's own band, and have an island. THOSE are the approach islands the planner seeds from - if one of them shares an island with a link anchor and the trip still fails, the island flood and the pathfinder disagree.");
                }

                PawnPath pawnPath = null;
                bool flag13 = false;
                try
                {
                    pawnPath = map.pathFinder.FindPathNow(pawn.Position, localTargetInfo, TraverseParms.For(pawn), null, peMode);
                    flag13 = pawnPath?.Found ?? false;
                    stringBuilder.AppendLine("  FindPathNow = " + (flag13 ? ("FOUND (" + pawnPath.NodesLeftCount + " nodes)") : "NOT FOUND"));
                }
                finally
                {
                    pawnPath?.ReleaseToPool();
                }

                if (flag6 && !flag13 && !flag8)
                {
                    stringBuilder.AppendLine("  >> CROSS-BAND: NOT FOUND HERE IS BY DESIGN AND MEANS NOTHING. The pathfinder cannot cross a band, and Patch_PathFinder_ABRejectCrossBandSync answers this probe with the not-found singleton on purpose. The crossing is made by the transit system, so the TryGetTransit line above is the verdict that matters: NONE there, with its reason, is the real failure.");
                }
                else if (flag6 && !flag13)
                {
                    stringBuilder.AppendLine("  >> SAME BAND, CanReach(Touch,Deadly)=True with NOT FOUND at the same mode: the region graph is connected but no walkable route exists (usually a diagonal-only link past an impassable cell). That is the re-issue loop.");
                }
                else if (flag13)
                {
                    stringBuilder.AppendLine("  >> A path EXISTS, so this is not a connectivity failure. If the pawn is not advancing along it, the problem is movement or repeated re-targeting, not reachability.");
                }
            }
            else
            {
                stringBuilder.AppendLine("  no valid destination");
            }

            StuckProbeAISection(stringBuilder, pawn, map);
            stringBuilder.AppendLine("  pawn's own neighbours (walkable / terrain / edifice):");
            for (int k = 0; k < 8; k++)
            {
                IntVec3 intVec4 = pawn.Position + GenAdj.AdjacentCells[k];
                IntVec3 intVec2;
                if (!intVec4.InBounds(map))
                {
                    intVec2 = intVec4;
                    stringBuilder.AppendLine("    " + intVec2.ToString() + "  OUT OF BOUNDS");
                    continue;
                }

                Building edifice2 = intVec4.GetEdifice(map);
                string[] array4 = new string[10]
                {
                    "    ",
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
                intVec2 = intVec4;
                array4[1] = intVec2.ToString();
                array4[2] = "  walkable=";
                array4[3] = intVec4.Walkable(map).ToString();
                array4[4] = "  terrain=";
                array4[5] = map.terrainGrid.TerrainAt(intVec4).defName;
                array4[6] = "  edifice=";
                array4[7] = ((edifice2 != null) ? edifice2.def.defName : "-");
                array4[8] = "  band=";
                array4[9] = ABBands.BandOf(map, intVec4).ToString();
                stringBuilder.AppendLine(string.Concat(array4));
            }

            Log.Warning("[As above, So below] V2 stuck probe:\n" + stringBuilder);
            Messages.Message("AB2: stuck probe written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>Pathing counters and diagnostics. Use warning level so the bridge records the report.</summary>
        [DebugAction("As above", "AB2: pathing report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2PathingReport()
        {
            string text = "\n  §60 animal ecosystem cap: applied " + Patch_WildAnimalSpawner_ABSliceEcosystem.capsApplied + "x, last scale " + Patch_WildAnimalSpawner_ABSliceEcosystem.lastScale.ToString("0.000") + " (1.000 means NOT capped: unbanded map, or the getter never ran)";
            int num = ABWormholePather.doomedNoLinkPair + ABWormholePather.doomedCollapsed + ABWormholePather.doomedOther;
            string text2 = "\n  §183 doomed cross-band StartPath dispatches: " + num + " (vs " + ABWormholePather.segmentedOk + " segmented cleanly)\n      no link pair joins those bands : " + ABWormholePather.doomedNoLinkPair + "\n      entry collapsed onto pawn cell : " + ABWormholePather.doomedCollapsed + "\n      other / declined earlier       : " + ABWormholePather.doomedOther + "\n    handled without a dispatch (post-fix branches):\n      no link pair -> failed fast     : " + ABWormholePather.failedFastNoLink + "\n      already on mouth -> arrived     : " + ABWormholePather.arrivedOnMouth + ((num == 0) ? "\n      (zero is the pass condition - nothing is being handed to vanilla undoable)" : "\n      NON-ZERO: each one costs a wasted path job + a yellow vanilla warning.");
            Log.Warning("[As above, So below] " + ABPathBandScope.Report() + text + text2);
            Messages.Message("AB2: pathing report written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>Why the drafted destination ghost did or did not draw. Four causes look
        /// identical in game; this separates them. Hold the goto drag, then run this.</summary>
        [DebugAction("As above", "AB2: goto ghost report",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2GotoGhostReport()
        {
            Log.Warning(ABLog.Tag + " AB2 GOTO GHOST REPORT\n  ghosts drawn this session: "
                + Patch_MultiPawnGotoController_ABDrawInViewBand.ghostsDrawn
                + "\n  last outcome: "
                + Patch_MultiPawnGotoController_ABDrawInViewBand.lastGhostSkip);
            Messages.Message("AB2: goto ghost report written to log.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>
        /// Per-band component census, plus the selected pawn's component against its
        /// destination.
        ///
        /// ⚠ THIS DOC USED TO SAY "nothing in the mod reads this data yet". THAT IS LONG
        /// DEAD: §34 routes every cross-island trip on it and §59 added a second, forbid-aware
        /// partition on top. Movement depends on this data completely.
        ///
        /// What to look for: a sky or basement band reporting `islands &gt; 1` is a fragmented
        /// band, and a selected pawn showing `sameBand=True knownDifferentIslands=True` is the
        /// exact stall this design exists to fix. §59 adds a `forbid-aware:` line per band
        /// (printed only where a forbidden door actually exists) and prints the selected
        /// pawn's island on BOTH partitions - when those two disagree, a forbidden door is
        /// what splits them.
        /// </summary>
        [DebugAction("As above", "AB2: component report",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ComponentReport()
        {
            Map map = Find.CurrentMap;
            Pawn sel = Find.Selector?.SingleSelectedThing as Pawn;
            Log.Warning(ABLog.Tag + " " + ABBandComponents.Report(map, sel));
            Messages.Message("AB2: component report written to log.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>Node-by-node dump of the cross-level route preview, for the "line goes
        /// half way then straight down" report. Select the transiting pawn first.</summary>
        [DebugAction("As above", "AB2: route line dump",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2RouteLineDump()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Messages.Message("AB2: select exactly one pawn first.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Log.Warning(ABLog.Tag + " AB2 ROUTE LINE DUMP for " + pawn.LabelShortCap + "\n"
                + ABTransitVisuals.DescribeRoute(pawn));
            Messages.Message("AB2: route dump written to log.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("As above", "AB2: reset pathing counters",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2PathingReset()
        {
            ABPathBandScope.ResetStats();
            Messages.Message("AB2: pathing counters reset.",
                MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
