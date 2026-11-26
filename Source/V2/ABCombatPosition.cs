using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// WHERE AN AI PAWN STANDS TO SHOOT ACROSS A LEVEL.
    ///
    /// Reported as: "neutrals / allies aren't attacking hostile factions when they aren't
    /// colonists. They will walk up the stairs and then begin to engage, but not across
    /// levels." Two distinct defects behind one symptom, and BOTH had to be fixed for any of
    /// it to work - see ABCombatAcquisition for the first (which target gets chosen).
    ///
    /// THIS FILE IS THE SECOND: having chosen a cross-band target, the pawn could not find
    /// anywhere to shoot it from.
    ///
    /// <c>JobGiver_AIFightEnemy.TryGiveJob</c> stands and fires only when it already has a
    /// firing solution AND either cover or a target within five cells; otherwise it asks
    /// <c>TryFindShootingPosition</c> -> <c>CastPositionFinder.TryFindCastPosition</c> for
    /// somewhere better. That search is anchored on the TARGET:
    ///
    ///     int num2 = Mathf.CeilToInt(req.maxRangeFromTarget);
    ///     CellRect otherRect2 = new CellRect(targetLoc.x - num2, targetLoc.z - num2, ...);
    ///     cellRect.ClipInsideRect(otherRect2);
    ///
    /// so the candidate rect is a square around the target's REAL cell, one Slot away in the
    /// target's own band. Every cell the caster could actually stand on is outside it, and the
    /// per-cell filter repeats the same mistake
    /// (<c>(c - req.target.Position).LengthHorizontalSquared > maxRangeFromTargetSquared</c>).
    /// The search therefore returns false ALWAYS for a cross-band pair - not sometimes, not
    /// for awkward geometry, but by construction.
    ///
    /// ⚠⚠ AND THAT IS WHY THEY WALKED UP THE STAIRS. A failed TryFindShootingPosition makes
    /// TryGiveJob return null, the fight think-node declines, and a later node sends the pawn
    /// toward the enemy instead. The pawn was not "choosing" to close the distance; it was
    /// falling through to the only behaviour left. **The symptom named movement and the defect
    /// was in a range check** - the same shape as §41e, for the fifth time.
    ///
    /// ⚠ ONE PATCH, SIX CONSUMERS. Every TryFindShootingPosition override in the game
    /// (AIFightEnemies, AIDefendPawn, AIDefendPoint, AIAbilityFight, MetalhorrorFight,
    /// ShamblerFight) bottoms out here, so this is the choke point - the same reasoning that
    /// made GenUI.ThingsUnderMouse the single interception for player targeting.
    /// </summary>
    public static class ABCombatPosition
    {
        /// <summary>How far from its current cell a pawn will look for a firing position on a
        /// cross-band target. Deliberately small: standing still and firing is the correct
        /// answer almost always, because the balcony rule already lets it shoot from anywhere
        /// with sight of an opening. This is for the case where it is standing just off the
        /// sight line and a step or two fixes it.</summary>
        private const float SearchRadius = 12f;

        public static int solvedInPlace;

        public static int solvedByStep;

        public static int noPosition;

        public static void ResetCounters()
        {
            standStillLoopsBroken = 0;
            vanillaSearches = 0;
            solvedInPlace = 0;
            solvedByStep = 0;
            noPosition = 0;
        }

        public static string CounterReport()
        {
            return "castPos: vanillaSearches=" + vanillaSearches + (vanillaSearchArmed ? "" : (" [vanilla search NOT ARMED, " + ArmedCount + "/3 methods]")) + " | fallback inPlace=" + solvedInPlace + " stepped=" + solvedByStep + " none=" + noPosition + " | standStillLoopsBroken=" + standStillLoopsBroken;
        }

        /// <summary>
        /// Band-local cast position search. Runs ONLY for a cross-band pair, where vanilla's
        /// answer is a guaranteed false.
        ///
        /// ⚠ NO COVER SCORING, AND THAT IS A DELIBERATE OMISSION RATHER THAN AN OVERSIGHT.
        /// Vanilla's evaluator weighs cover from the target, distance preference, avoid grids
        /// and reservations, all computed against the target's real cell. Reproducing that in
        /// band-local terms would be a second copy of a long private method - and §14 is
        /// explicit that reproducing a subsystem means ALL of it. What this needs is far
        /// smaller: the nearest cell this pawn can legally stand in that HAS a firing
        /// solution. Cover across a hole in the floor is close to meaningless anyway; the
        /// opening is the cover.
        /// </summary>
        public static bool TryFindBandLocalCastPosition(CastPositionRequest req, out IntVec3 dest)
        {
            dest = IntVec3.Invalid;
            Pawn caster = req.caster;
            Thing target = req.target;
            Verb verb = req.verb;
            if (caster == null || target == null || verb == null || !caster.Spawned || !target.Spawned)
            {
                return false;
            }

            Map map = caster.Map;
            LocalTargetInfo targ = target;
            int num = ABBands.BandOf(map, caster.Position);
            ABBandMap aBBandMap = ABBands.CompOf(map);
            float num2 = req.maxRangeFromTarget * req.maxRangeFromTarget;
            bool flag = num2 > 0.01f && num2 < 250000f && aBBandMap != null;
            IntVec3 intVec = aBBandMap?.Translate(target.Position, num) ?? target.Position;
            if ((!flag || (float)(caster.Position - intVec).LengthHorizontalSquared <= num2) && verb.CanHitTargetFrom(caster.Position, targ))
            {
                dest = caster.Position;
                solvedInPlace++;
                return true;
            }

            int num3 = Mathf.Min(GenRadial.NumCellsInRadius(12f), GenRadial.RadialPattern.Length);
            for (int i = 0; i < num3; i++)
            {
                IntVec3 intVec2 = caster.Position + GenRadial.RadialPattern[i];
                if (intVec2.InBounds(map) && aBBandMap != null && aBBandMap.BandOf(intVec2) == num && !aBBandMap.InGutter(intVec2) && (!flag || !((float)(intVec2 - intVec).LengthHorizontalSquared > num2)) && intVec2.Standable(map) && intVec2.WalkableBy(map, caster) && (req.validator == null || req.validator(intVec2)) && map.pawnDestinationReservationManager.CanReserve(intVec2, caster) && verb.CanHitTargetFrom(intVec2, targ))
                {
                    dest = intVec2;
                    solvedByStep++;
                    return true;
                }
            }

            noPosition++;
            return false;
        }
        internal static bool StandStillLoopTrips(Pawn caster, Thing target, IntVec3 dest)
        {
            if (caster == null || dest != caster.Position)
            {
                loopPawn = null;
                loopTarget = null;
                return false;
            }

            int frameCount = Time.frameCount;
            int num = ((Find.TickManager != null) ? Find.TickManager.TicksGame : 0);
            if (caster == loopPawn && target == loopTarget && frameCount == loopFrame && num == loopTick)
            {
                loopCount++;
            }
            else
            {
                loopPawn = caster;
                loopTarget = target;
                loopFrame = frameCount;
                loopTick = num;
                loopCount = 1;
            }

            if (loopCount <= 4)
            {
                return false;
            }

            standStillLoopsBroken++;
            Log.WarningOnce("[As above, So below] " + caster.LabelShortCap + " was told to stand still and shoot " + ((target != null) ? target.LabelShortCap : "its target") + " on another level " + loopCount + " times in one tick by " + ((caster.jobs?.curDriver != null) ? caster.jobs.curDriver.GetType().FullName : "an unknown job driver") + ", which re-checks the position with its own rule and asks again. The search now answers no position so the job ends instead of overflowing the stack; a '10 jobs in one tick' error for this pawn may follow and is harmless. Please report the driver named here.", 2822389);
            return true;
        }

        internal static IEnumerable<CodeInstruction> RewritePositionReads(IEnumerable<CodeInstruction> instructions, int slot, string where)
        {
            MethodInfo methodInfo = AccessTools.PropertyGetter(typeof(Thing), "Position");
            MethodInfo operand = AccessTools.Method(typeof(ABCombatPosition), "PositionOf");
            FieldInfo fieldInfo = AccessTools.Field(typeof(CastPositionRequest), "target");
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            int num = 0;
            int num2 = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && list[i].operand as MethodInfo == methodInfo)
                {
                    if (i > 0 && list[i - 1].opcode == OpCodes.Ldfld && list[i - 1].operand as FieldInfo == fieldInfo)
                    {
                        num2++;
                    }

                    list[i].opcode = OpCodes.Call;
                    list[i].operand = operand;
                    num++;
                }
            }

            armedMethods[slot] = num2 > 0;
            if (num2 == 0)
            {
                Log.WarningOnce("[As above, So below] cast position: no req.target.Position read found in CastPositionFinder." + where + " (" + num + " other Position reads); cross-level firing positions fall back to the nearest-cell search, which ignores cover.", 2822400 + slot);
            }

            return list;
        }

        internal static bool ScopeEnemyBelow(IntVec3 shooterLoc)
        {
            return scopeTarget != null && scopeEnemyBelow && shooterLoc == scopeLocal && UnityData.IsInMainThread;
        }

        internal static void Exit(ScopeState previous)
        {
            if (previous.entered)
            {
                scopeTarget = previous.target;
                scopeLocal = previous.local;
                scopeEnemyBelow = previous.enemyBelow;
            }
        }

        internal static ScopeState Enter(Thing target, IntVec3 local, bool enemyBelow)
        {
            ScopeState result = new ScopeState
            {
                target = scopeTarget,
                local = scopeLocal,
                enemyBelow = scopeEnemyBelow,
                entered = true
            };
            scopeTarget = target;
            scopeLocal = local;
            scopeEnemyBelow = enemyBelow;
            vanillaSearches++;
            return result;
        }

        public static IntVec3 PositionOf(Thing t)
        {
            if (t != null && t == scopeTarget)
            {
                return scopeLocal;
            }

            return t.Position;
        }

        private static int ArmedCount => (armedMethods[0] ? 1 : 0) + (armedMethods[1] ? 1 : 0) + (armedMethods[2] ? 1 : 0);

        internal static bool vanillaSearchArmed => armedMethods[0] && armedMethods[1] && armedMethods[2];

        public static int standStillLoopsBroken;

        private static int loopCount;

        private static int loopTick = -1;

        private static int loopFrame = -1;

        private static Thing loopTarget;

        private static Pawn loopPawn;

        private const int StandStillLoopLimit = 4;

        public static int vanillaSearches;

        private static readonly bool[] armedMethods = new bool[3];

        private static bool scopeEnemyBelow;

        private static IntVec3 scopeLocal;

        private static Thing scopeTarget;

        public struct ScopeState
        {
            public Thing target;
            public IntVec3 local;
            public bool enemyBelow;
            public bool entered;
            public bool fallback;
        }

    }

    /// <summary>
    /// The single interception, prefixed rather than postfixed because vanilla's failure is
    /// structural: by the time it returns, it has clipped its candidate rect into the target's
    /// band and there is nothing left to repair.
    ///
    /// Same-band pairs never enter this code and pay one band compare.
    /// </summary>
    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    public static class Patch_CastPositionFinder_ABCrossBand
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ABCombatPosition.RewritePositionReads(instructions, 0, "TryFindCastPosition");
        }

        private static void Finalizer(ABCombatPosition.ScopeState __state)
        {
            ABCombatPosition.Exit(__state);
        }

        private static void Postfix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result, ABCombatPosition.ScopeState __state)
        {
            if (__state.fallback && !__result)
            {
                try
                {
                    __result = ABCombatPosition.TryFindBandLocalCastPosition(newReq, out dest);
                    if (ABV2Debug.LogCombat)
                    {
                        ABV2Debug.Combat("cast position for " + newReq.caster?.LabelShortCap + " vs " + newReq.target?.LabelShortCap + " across bands (fallback search): " + (__result ? dest.ToString() : "NONE"));
                    }
                }
                catch (Exception e)
                {
                    ABGuard.Disable(ABGuard.Combat, e, "V2 cross-band cast position fallback");
                }
            }
            else if (__state.entered && ABV2Debug.LogCombat)
            {
                ABV2Debug.Combat("cast position for " + newReq.caster?.LabelShortCap + " across bands: " + (__result ? dest.ToString() : "NONE"));
            }

            if (__result && (__state.entered || __state.fallback) && ABCombatPosition.StandStillLoopTrips(newReq.caster, newReq.target, dest))
            {
                __result = false;
            }
        }

        private static void Prefix(ref CastPositionRequest newReq, out ABCombatPosition.ScopeState __state)
        {
            __state = default;
            try
            {
                Pawn caster = newReq.caster;
                Thing target = newReq.target;
                if (caster == null || target == null || !caster.Spawned || !target.Spawned || !ABCombatV2.Enabled)
                {
                    return;
                }

                Map map = caster.Map;
                if (target.Map != map)
                {
                    return;
                }

                ABBandMap bands = ABBands.CompOf(map);
                if (bands == null || !bands.Banded)
                {
                    return;
                }

                int band = bands.BandOf(caster.Position);
                if (band == bands.BandOf(target.Position))
                {
                    return;
                }

                if (ABCombatPosition.vanillaSearchArmed)
                {
                    Func<IntVec3, bool> inner = newReq.validator;
                    newReq.validator = (IntVec3 c) => bands.BandOf(c) == band && !bands.InGutter(c) && (inner == null || inner(c));
                    __state = ABCombatPosition.Enter(target, bands.Translate(target.Position, band), bands.BandOf(target.Position) < band);
                    if (ABV2Debug.LogCombat)
                    {
                        ABV2Debug.Combat("cast position for " + caster.LabelShortCap + " vs " + target.LabelShortCap + " across bands: vanilla search, target carried to " + bands.Translate(target.Position, band).ToString());
                    }
                }
                else
                {
                    Func<IntVec3, bool> innerFallback = newReq.validator;
                    newReq.validator = (IntVec3 c) => bands.BandOf(c) == band && !bands.InGutter(c) && (innerFallback == null || innerFallback(c));
                    __state = new ABCombatPosition.ScopeState
                    {
                        fallback = true
                    };
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "V2 cross-band cast position");
            }
        }

    }
}
