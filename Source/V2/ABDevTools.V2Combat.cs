using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse.AI;
using Verse.AI.Group;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// Dev tooling for cross-level combat.
    ///
    /// ⚠ THE ORDER THESE ARE MEANT TO BE USED IN, because §14's rule is "run the diagnostic
    /// before the first theory" and these two are not interchangeable:
    ///   1. `AB2: band info` FIRST, always. It prints NO WORMHOLE PAIRS REGISTERED, which is
    ///      the fastest way to learn that a test map has no stairs yet - and while stairs are
    ///      irrelevant to shooting, their absence usually means the map has no OPENINGS
    ///      either, and an opening is the one thing cross-level fire cannot do without.
    ///   2. `AB2: why can't this pawn shoot that` for a specific failure. It runs the real
    ///      solver with tracing on and prints the intermediate values - the drift, the blocked
    ///      column cell, the band-local distance - not a verdict.
    ///   3. `AB2: combat report` for counters after a firefight.
    /// </summary>
    public static partial class ABDevTools
    {
        [DebugAction("As above", "AB2: combat report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2CombatReport()
        {
            Map currentMap = Find.CurrentMap;
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("V2 combat report:");
            stringBuilder.AppendLine("  guard: combat=" + ABGuard.On(ABGuard.Combat) + " rendering=" + ABGuard.On(ABGuard.Rendering) + " clickThrough=" + ABBelowClickThrough.Enabled);
            stringBuilder.AppendLine("  geometry rule: driftPerLevel=" + 12f + " verticalCostPerLevel=" + 3f);
            stringBuilder.AppendLine("  shaft: " + ABShaft.CounterReport() + " leanSolves=" + ABCombatV2.leanSolves);
            stringBuilder.AppendLine("  " + ABCombatCover.CounterReport() + " largePawnsCrossSight=" + LargePawnsCompat.crossAnswered);
            stringBuilder.AppendLine("  " + Patch_ShotReport_ABBandLocalDistance.CounterReport());
            stringBuilder.AppendLine("  " + ABCombatAcquisition.CounterReport());
            stringBuilder.AppendLine("  " + ABCombatPosition.CounterReport());
            stringBuilder.AppendLine("  " + ABCombatTargeting.CounterReport());
            stringBuilder.AppendLine("  " + ABRangeOverlay.CounterReport());
            stringBuilder.AppendLine("  " + ABCombatRelay.CounterReport());
            stringBuilder.AppendLine("  " + ABBandLeap.CounterReport());
            stringBuilder.AppendLine("  " + ABBandLeapAI.CounterReport());
            stringBuilder.AppendLine("  " + ABCombatAbilities.CounterReport());
            stringBuilder.AppendLine("  " + ABBandSenses.CounterReport());
            stringBuilder.AppendLine("  " + ABBandArrivals.CounterReport());
            stringBuilder.AppendLine("  defend-base anchors moved onto the pawns' level: " + Patch_LordMaker_ABDefendBaseAnchor.anchorsMoved);
            stringBuilder.AppendLine(VanillaGravshipCompat.CounterReport());
            stringBuilder.AppendLine("  " + ABCECompat.CounterReport());
            stringBuilder.AppendLine("  " + ABCEHuntCompat.CounterReport());
            stringBuilder.AppendLine("  " + ABShamblerCrossLevel.CounterReport());
            if (currentMap != null)
            {
                ABBandMap aBBandMap = ABBands.CompOf(currentMap);
                stringBuilder.AppendLine("  map: banded=" + (aBBandMap?.Banded ?? false) + " viewBand=" + ((currentMap != null) ? ABBandView.CurrentBand(currentMap) : (-1)) + " openAirCells=" + CountOpenAir(currentMap));
            }

            Log.Warning("[As above, So below] " + stringBuilder.ToString().TrimEndNewlines());
            Messages.Message("AB2: combat report written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction("As above", "AB2: reset combat counters", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ResetCombatCounters()
        {
            ABShaft.ResetCounters();
            ABCombatV2.leanSolves = 0;
            ABCombatCover.ResetCounters();
            LargePawnsCompat.crossAnswered = 0;
            Patch_ShotReport_ABBandLocalDistance.ResetCounters();
            ABCombatAcquisition.ResetCounters();
            ABCombatPosition.ResetCounters();
            ABCombatTargeting.ResetCounters();
            ABRangeOverlay.ResetCounters();
            ABCombatRelay.ResetCounters();
            ABBandLeap.ResetCounters();
            ABBandLeapAI.ResetCounters();
            ABCombatAbilities.ResetCounters();
            ABBandSenses.ResetCounters();
            ABBandArrivals.ResetCounters();
            ABCECompat.ResetCounters();
            ABCEHuntCompat.huntDistancesLocalized = 0;
            ABShamblerCrossLevel.ResetCounters();
            Messages.Message("AB2: combat counters reset.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>
        /// For the selected pawn (or turret), trace the solver against every attackable thing
        /// on another band. This is the one-stop probe for "why did nothing happen".
        ///
        /// ⚠ IT TRACES THE *REAL* SOLVE AND BYPASSES THE MEMO. A cached answer would print no
        /// trace at all, which would read as "the solver was never asked" - the exact
        /// misreading that costs a test cycle.
        /// </summary>
        [DebugAction("As above", "AB2: why can't this pawn shoot that", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2WhyNoShot()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(currentMap);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                Log.Warning("[As above, So below] V2 shot probe: this map is not banded.");
                return;
            }

            Thing thing = null;
            foreach (object selectedObject in Find.Selector.SelectedObjects)
            {
                if (selectedObject is Thing { Spawned: not false } thing2)
                {
                    thing = thing2;
                    break;
                }
            }

            if (thing == null)
            {
                Messages.Message("AB2: select a pawn or turret first.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Verb verb = VerbOf(thing);
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("V2 shot probe for " + thing.LabelShortCap + " at " + thing.Position.ToString() + " (band " + aBBandMap.BandOf(thing.Position) + ", view band " + ABBandView.CurrentBand(currentMap) + ")");
            if (verb == null)
            {
                stringBuilder.AppendLine("  NO EFFECTIVE RANGED VERB. Nothing below can apply - an unarmed pawn, an unmanned turret, or a melee-only weapon.");
                Log.Warning("[As above, So below] " + stringBuilder.ToString().TrimEndNewlines());
                return;
            }

            bool flag = ABShaft.IsOverheadFire(verb);
            stringBuilder.AppendLine("  verb " + verb.GetType().Name + " range " + verb.EffectiveRange + " minRange " + verb.verbProps.minRange + " requireLOS=" + verb.verbProps.requireLineOfSight + " fliesOverhead=" + verb.ProjectileFliesOverhead() + " => rule: " + (flag ? "MAP COORDINATES (levels ignored)" : "BALCONY"));
            int num = aBBandMap.BandOf(thing.Position);
            int num2 = 0;
            List<Thing> list = currentMap.listerThings.ThingsInGroup(ThingRequestGroup.AttackTarget);
            for (int i = 0; i < list.Count; i++)
            {
                Thing thing3 = list[i];
                if (thing3 == null || !thing3.Spawned || thing3 == thing || aBBandMap.BandOf(thing3.Position) == num)
                {
                    continue;
                }

                num2++;
                stringBuilder.AppendLine("  --- " + thing3.LabelShortCap + " (band " + aBBandMap.BandOf(thing3.Position) + ", hostile=" + thing.HostileTo(thing3) + ")");
                stringBuilder.Append(ABShaft.Explain(currentMap, thing.Position, thing3.Position, verb.EffectiveRange, verb.verbProps.EffectiveMinRange(thing3, thing), flag));
                bool flag2 = ABCombatV2.TrySolve(verb, thing.Position, thing3, out var sol, out var source, out var dest);
                string text = "";
                if (flag2)
                {
                    int localized = ABCombatCover.localized;
                    int pointBlankSkipped = ABCombatCover.pointBlankSkipped;
                    int crossAnswered = LargePawnsCompat.crossAnswered;
                    text = CoverUtility.CalculateOverallBlockChance(thing3, thing.Position, currentMap).ToStringPercent();
                    ABCombatCover.localized = localized;
                    ABCombatCover.pointBlankSkipped = pointBlankSkipped;
                    LargePawnsCompat.crossAnswered = crossAnswered;
                }

                string text2;
                if (!flag2)
                {
                    text2 = "no shot";
                }
                else
                {
                    string[] array = new string[7]
                    {
                        "SHOT from ",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null
                    };
                    IntVec3 intVec = source;
                    array[1] = intVec.ToString();
                    array[2] = " at ";
                    intVec = dest;
                    array[3] = intVec.ToString();
                    string text3;
                    if (!sol.overhead)
                    {
                        intVec = sol.opening;
                        text3 = " via opening " + intVec.ToString();
                    }
                    else
                    {
                        text3 = " (overhead)";
                    }

                    array[4] = text3;
                    array[5] = ", cover ";
                    array[6] = text;
                    text2 = string.Concat(array);
                }

                stringBuilder.AppendLine("    verb verdict with lean-out cells: " + text2);
            }

            if (num2 == 0)
            {
                stringBuilder.AppendLine("  NOTHING ATTACKABLE ON ANOTHER BAND AT ALL. That is the answer: there is no cross-level target to fail against. Spawn something on another level first (`AB2: open all bands` if it is still fogged).");
            }

            Log.Warning("[As above, So below] " + stringBuilder.ToString().TrimEndNewlines());
            Messages.Message("AB2: shot probe written to log (" + num2 + " cross-band candidates).", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        /// <summary>The verb the thing would actually shoot with, for pawns and turrets alike.
        /// A turret's searcher is its mannable pawn when manned, but the VERB is always the
        /// gun's - which is the distinction that matters here.</summary>
        private static Verb VerbOf(Thing t)
        {
            if (t is Building_Turret turret)
            {
                return turret.AttackVerb;
            }
            if (t is Pawn p)
            {
                return p.TryGetAttackVerb(null, allowManualCastWeapons: true);
            }
            return null;
        }

        private static int CountOpenAir(Map map)
        {
            TerrainDef air = ABDefOf.AB_OpenAir;
            TerrainGrid grid = map.terrainGrid;
            int n = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (grid.TerrainAt(c) == air)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
