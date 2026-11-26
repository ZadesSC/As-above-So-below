using System.Reflection;
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 cross-band target ACQUISITION - what pawns and turrets decide to shoot at.
    ///
    /// ABShaft makes a cross-band shot legal, and ABCombatV2 makes the verb agree. But
    /// nothing ever DECIDED to fire: vanilla's target search pre-filters candidates by cell
    /// distance, and a pawn one band up is a whole Slot away, so every cross-band hostile was
    /// discarded long before a verb was consulted.
    ///
    /// ⚠⚠ IT HOOKS <c>BestAttackTarget</c>, NOT <c>BestShootTargetFromCurrentPosition</c>,
    /// AND THAT ONE-WORD DIFFERENCE WAS AN ENTIRE CLASS OF BUG.
    ///
    /// Reported as: "neutrals / allies aren't attacking hostile factions when they aren't
    /// colonists; they walk up the stairs and then engage, but not across levels." The cause
    /// is that the two methods serve DISJOINT populations, and the first version of this file
    /// patched the smaller one:
    ///
    ///   BestShootTargetFromCurrentPosition - turrets (Building_TurretGun, CompTurretGun),
    ///     CompFleshmassSpitter, and JobDriver_Wait. That last one is why it LOOKED like it
    ///     worked: a drafted colonist standing still auto-attacks through JobDriver_Wait.
    ///   BestAttackTarget - JobGiver_AIFightEnemy (every raider, ally and hostile visitor),
    ///     JobGiver_ConfigurableHostilityResponse (UNDRAFTED colonists reacting to a threat),
    ///     Berserk, Manhunter, mutants, metalhorrors, sightstealers, holding-platform escapes.
    ///
    /// So cross-level combat worked for exactly two cases - a turret, and a drafted colonist
    /// told to hold position - and silently did nothing for every AI-driven combatant in the
    /// game. The user's report names allies and neutrals because those are always AI-driven;
    /// it was never faction-specific. **A BUG REPORT THAT NAMES ONE PAWN TYPE IS NOT
    /// NECESSARILY TYPE-SPECIFIC** (§14), for the second time in this project.
    ///
    /// ⚠ AND ONE PATCH HERE COVERS BOTH, because BestShootTargetFromCurrentPosition is a
    /// four-line wrapper that clamps min/max range and calls BestAttackTarget. Hooking the
    /// inner method means we see the ALREADY-CLAMPED limits plus locus, travel radius and
    /// canTakeTargetsCloserThanEffectiveMinRange, all of which the outer signature hides.
    /// Patching both would double every scan.
    ///
    /// It is also the real answer to the shipped note that "turrets do not target across
    /// levels": they always went through this path; what they lacked was the validation below,
    /// because a turret passes scan FLAGS and a range pair that the first version ignored.
    ///
    /// ⚠ IT RUNS ONLY WHEN VANILLA FOUND NOTHING, and that is a balance decision, not an
    /// optimisation. A cross-band target must never outrank something the shooter can already
    /// engage on its own level, or colonists start ignoring the raider next to them to snipe
    /// through a hole in the floor.
    ///
    /// ⚠⚠ AND THE VALIDATION HERE MUST MIRROR VANILLA'S innerValidator, NOT APPROXIMATE IT.
    /// BestAttackTarget builds a closure with ten checks in it; skipping them does not make
    /// the mod permissive, it makes it WRONG in specific, hard-to-attribute ways - a mortar
    /// that lobs shells at a target under thick roof, a turret that wakes on a dormant
    /// mechanoid cluster, a pawn under a Lord that attacks something its lord forbade. Each
    /// check below names the vanilla one it stands in for. The line-of-sight flags are the
    /// only ones deliberately dropped: our shaft solve IS the line-of-sight test, and a
    /// vanilla CanSee across a Slot of gutter can only ever answer false.
    /// </summary>
    public static class ABCombatAcquisition
    {
        // Observe-only counters for `AB2: combat report`.
        public static int scans;

        public static int found;

        public static int rejectedByFlags;

        public static void ResetCounters()
        {
            scans = 0;
            found = 0;
            rejectedByFlags = 0;
            blindedBySmoke = 0;
            outOfReach = 0;
            smokeOnLine = 0;
        }

        public static string CounterReport()
        {
            return "acquisition: scans=" + scans + " found=" + found + " outOfReach=" + outOfReach + " rejectedByFlags=" + rejectedByFlags + " blindedBySmoke=" + blindedBySmoke + " smokeOnLine=" + smokeOnLine;
        }
        private static bool PassesFlags(IAttackTargetSearcher searcher, Thing searcherThing, Lord lord, Pawn searcherPawn, Thing t, IAttackTarget target, TargetScanFlags flags, bool onlyTargetMachines, Verb verb, bool canBashDoors, bool canBashFences)
        {
            if ((flags & TargetScanFlags.IgnoreNonCombatants) != 0 && t is Pawn pawn && !pawn.IsCombatant())
            {
                return false;
            }

            if (searcherPawn != null && lord != null && !lord.LordJob.ValidateAttackTarget(searcherPawn, t))
            {
                return false;
            }

            if ((flags & TargetScanFlags.NeedNotUnderThickRoof) != 0)
            {
                RoofDef roof = t.Position.GetRoof(t.Map);
                if (roof != null && roof.isThickRoof)
                {
                    return false;
                }
            }

            if ((flags & TargetScanFlags.LOSBlockableByGas) != 0 && (flags & TargetScanFlags.NeedLOSToAll) != 0 && !IgnoresAccuracyMaluses(verb) && t.Position.AnyGas(t.Map, GasType.BlindSmoke))
            {
                return false;
            }

            if (((flags & TargetScanFlags.NeedThreat) != 0 || (flags & TargetScanFlags.NeedAutoTargetable) != 0) && target.ThreatDisabled(searcher))
            {
                return false;
            }

            if ((flags & TargetScanFlags.NeedAutoTargetable) != 0 && !AttackTargetFinder.IsAutoTargetable(target))
            {
                return false;
            }

            if ((flags & TargetScanFlags.NeedActiveThreat) != 0 && !GenHostility.IsActiveThreatTo(target, searcherThing.Faction))
            {
                return false;
            }

            if (onlyTargetMachines && t is Pawn pawn2 && pawn2.RaceProps.IsFlesh)
            {
                return false;
            }

            if ((flags & TargetScanFlags.NeedNonBurning) != 0 && t.IsBurning())
            {
                return false;
            }

            if (searcherThing.def.race != null && (int)searcherThing.def.race.intelligence >= 2)
            {
                CompExplosive compExplosive = t.TryGetComp<CompExplosive>();
                if (compExplosive != null && compExplosive.wickStarted)
                {
                    return false;
                }
            }

            if ((searcherPawn != null && searcherPawn.IsColonist) || searcherThing.Faction == Faction.OfPlayer)
            {
                if (t.def.size.x == 1 && t.def.size.z == 1)
                {
                    if (t.Position.Fogged(t.Map))
                    {
                        return false;
                    }
                }
                else
                {
                    bool flag = false;
                    foreach (IntVec3 item in t.OccupiedRect())
                    {
                        if (!item.Fogged(t.Map))
                        {
                            flag = true;
                            break;
                        }
                    }

                    if (!flag)
                    {
                        return false;
                    }
                }
            }

            if ((flags & TargetScanFlags.NeedReachable) != 0)
            {
                if (searcherPawn != null)
                {
                    if (!searcherPawn.CanReach(t, PathEndMode.Touch, Danger.Some, canBashDoors, canBashFences))
                    {
                        return false;
                    }
                }
                else
                {
                    TraverseMode mode = (canBashDoors ? TraverseMode.PassDoors : TraverseMode.NoPassClosedDoors);
                    if (!searcherThing.Map.reachability.CanReach(searcherThing.Position, t, PathEndMode.Touch, TraverseParms.For(mode)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IgnoresAccuracyMaluses(Verb verb)
        {
            CompUniqueWeapon comp;
            return verb.EquipmentSource != null && verb.EquipmentSource.TryGetComp<CompUniqueWeapon>(out comp) && comp.IgnoreAccuracyMaluses;
        }

        private static float ShootingScore(IAttackTarget target, Thing t, IAttackTargetSearcher searcher, Thing searcherThing, Verb verb, ABBandMap bands, IntVec3 root)
        {
            float lengthHorizontal = (bands.Translate(t.Position, bands.BandOf(root)) - root).LengthHorizontal;
            float num = 60f - Mathf.Min(lengthHorizontal, 40f);
            if (target.TargetCurrentlyAimingAt == searcherThing)
            {
                num += 10f;
            }

            if (searcher.LastAttackedTarget == t && Find.TickManager.TicksGame - searcher.LastAttackTargetTick <= 300)
            {
                num += 40f;
            }

            num -= CoverUtility.CalculateOverallBlockChance(t.Position, root, t.Map) * 10f;
            if (target is Pawn pawn)
            {
                if (!pawn.IsCombatant())
                {
                    num -= 50f;
                }
                else if (pawn.DevelopmentalStage.Juvenile())
                {
                    num -= 25f;
                }

                if (verb.verbProps.ai_TargetHasRangedAttackScoreOffset != 0f && pawn.CurrentEffectiveVerb != null && pawn.CurrentEffectiveVerb.verbProps.Ranged)
                {
                    num += verb.verbProps.ai_TargetHasRangedAttackScoreOffset;
                }

                if (pawn.Downed)
                {
                    num -= 50f;
                }
            }

            if (BlastRadiusOffset != null)
            {
                num += BlastRadiusOffset(target, searcher, verb);
            }

            return num * target.TargetPriorityFactor;
        }

        private static IAttackTarget PickByScore(List<Candidate> shootable, float bestScore)
        {
            if (shootable.Count == 0)
            {
                return null;
            }

            if (bestScore < 1f)
            {
                for (int i = 0; i < shootable.Count; i++)
                {
                    if (shootable[i].score == bestScore)
                    {
                        return shootable[i].target;
                    }
                }

                return shootable[0].target;
            }

            float num = bestScore - 30f;
            float num2 = 0f;
            for (int j = 0; j < shootable.Count; j++)
            {
                if (shootable[j].score >= num)
                {
                    num2 += Mathf.InverseLerp(num, bestScore, shootable[j].score);
                }
            }

            if (num2 <= 0f)
            {
                return shootable[0].target;
            }

            float num3 = Rand.Value * num2;
            IAttackTarget result = null;
            for (int k = 0; k < shootable.Count; k++)
            {
                if (!(shootable[k].score < num))
                {
                    float num4 = Mathf.InverseLerp(num, bestScore, shootable[k].score);
                    result = shootable[k].target;
                    if (num4 > 0f && num3 < num4)
                    {
                        return result;
                    }

                    num3 -= num4;
                }
            }

            return result;
        }

        public static IAttackTarget FindCrossBandTarget(IAttackTargetSearcher searcher, TargetScanFlags flags, Predicate<Thing> validator, float minDistance, float maxDistance, IntVec3 locus, float maxTravelRadiusFromLocus, bool canTakeTargetsCloserThanEffectiveMinRange, bool onlyRanged, bool canBashDoors, bool canBashFences)
        {
            Thing thing = searcher?.Thing;
            if (thing == null || !thing.Spawned || !ABCombatV2.Enabled)
            {
                return null;
            }

            Map map = thing.Map;
            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return null;
            }

            Verb currentEffectiveVerb = searcher.CurrentEffectiveVerb;
            if (currentEffectiveVerb == null || currentEffectiveVerb.verbProps == null || currentEffectiveVerb.verbProps.IsMeleeAttack)
            {
                return null;
            }

            Pawn pawn = searcher as Pawn;
            if (pawn != null && pawn.InAggroMentalState)
            {
                return null;
            }

            IntVec3 position = thing.Position;
            int num = aBBandMap.BandOf(position);
            if ((flags & TargetScanFlags.LOSBlockableByGas) != 0 && (flags & TargetScanFlags.NeedLOSToAll) != 0 && !IgnoresAccuracyMaluses(currentEffectiveVerb) && position.AnyGas(map, GasType.BlindSmoke))
            {
                blindedBySmoke++;
                return null;
            }

            List<IAttackTarget> potentialTargetsFor = map.attackTargetsCache.GetPotentialTargetsFor(searcher);
            if (potentialTargetsFor == null || potentialTargetsFor.Count == 0)
            {
                return null;
            }

            if (tmpPotential == null)
            {
                tmpPotential = new List<IAttackTarget>();
            }

            List<IAttackTarget> list = tmpPotential;
            list.Clear();
            list.AddRange(potentialTargetsFor);
            scans++;
            float num2 = Mathf.Max(minDistance, currentEffectiveVerb.verbProps.minRange);
            float num3 = Mathf.Min(maxDistance, currentEffectiveVerb.EffectiveRange);
            Lord lord = ((pawn != null) ? pawn.GetLord() : null);
            bool onlyTargetMachines = currentEffectiveVerb.IsEMP();
            if (tmpCandidates == null)
            {
                tmpCandidates = new List<Candidate>();
            }

            List<Candidate> list2 = tmpCandidates;
            list2.Clear();
            float num4 = float.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                IAttackTarget attackTarget = list[i];
                Thing thing2 = attackTarget?.Thing;
                if (thing2 == null || !thing2.Spawned || thing2.Map != map || thing2 == thing || attackTarget == searcher || aBBandMap.BandOf(thing2.Position) == num || !thing.HostileTo(thing2))
                {
                    continue;
                }

                float num5 = num3 + (float)Mathf.Max(thing2.def.size.x, thing2.def.size.z);
                if ((float)(aBBandMap.Translate(thing2.Position, num) - position).LengthHorizontalSquared > num5 * num5)
                {
                    outOfReach++;
                    continue;
                }

                if (maxTravelRadiusFromLocus < 9999f)
                {
                    float num6 = maxTravelRadiusFromLocus + currentEffectiveVerb.EffectiveRange;
                    IntVec3 intVec = aBBandMap.Translate(thing2.Position, aBBandMap.BandOf(locus));
                    if ((float)(intVec - locus).LengthHorizontalSquared > num6 * num6)
                    {
                        rejectedByFlags++;
                        continue;
                    }
                }

                if (validator != null && !validator(thing2))
                {
                    continue;
                }

                if (!PassesFlags(searcher, thing, lord, pawn, thing2, attackTarget, flags, onlyTargetMachines, currentEffectiveVerb, canBashDoors, canBashFences))
                {
                    rejectedByFlags++;
                    continue;
                }

                float minRange = (canTakeTargetsCloserThanEffectiveMinRange ? num2 : Mathf.Max(num2, currentEffectiveVerb.verbProps.EffectiveMinRange(thing2, thing)));
                if (!ABCombatV2.TrySolveWithin(currentEffectiveVerb, position, thing2, num3, minRange, out var sol, out var source, out var dest))
                {
                    continue;
                }

                if ((flags & TargetScanFlags.LOSBlockableByGas) != 0 && ((thing2 is Pawn && (flags & TargetScanFlags.NeedLOSToPawns) != 0) || (!(thing2 is Pawn) && (flags & TargetScanFlags.NeedLOSToNonPawns) != 0)) && !IgnoresAccuracyMaluses(currentEffectiveVerb) && Patch_ShotReport_ABBandLocalDistance.SolvedLineHasSmoke(map, aBBandMap, sol, source, dest))
                {
                    smokeOnLine++;
                    continue;
                }

                float num7 = ShootingScore(attackTarget, thing2, searcher, thing, currentEffectiveVerb, aBBandMap, position);
                list2.Add(new Candidate { target = attackTarget, score = num7 });
                if (num7 > num4)
                {
                    num4 = num7;
                }
            }

            IAttackTarget attackTarget2 = PickByScore(list2, num4);
            list2.Clear();
            list.Clear();
            if (attackTarget2 != null)
            {
                found++;
                ABV2Debug.Combat("acquired cross-band " + attackTarget2.Thing.LabelShortCap + " at " + attackTarget2.Thing.Position.ToString() + " for " + thing.LabelShortCap + " (best score " + num4.ToString("0.0") + ")");
            }

            return attackTarget2;
        }

        private static Func<IAttackTarget, IAttackTargetSearcher, Verb, float> MakeBlastRadiusOffset()
        {
            try
            {
                MethodInfo methodInfo = AccessTools.Method(typeof(AttackTargetFinder), "FriendlyFireBlastRadiusTargetScoreOffset", new Type[3] { typeof(IAttackTarget), typeof(IAttackTargetSearcher), typeof(Verb) });
                return (methodInfo == null) ? null : AccessTools.MethodDelegate<Func<IAttackTarget, IAttackTargetSearcher, Verb, float>>(methodInfo);
            }
            catch
            {
                return null;
            }
        }

        [ThreadStatic]
        private static List<IAttackTarget> tmpPotential;

        [ThreadStatic]
        private static List<Candidate> tmpCandidates;

        private static readonly Func<IAttackTarget, IAttackTargetSearcher, Verb, float> BlastRadiusOffset = MakeBlastRadiusOffset();

        public static int smokeOnLine;

        public static int outOfReach;

        public static int blindedBySmoke;

        private struct Candidate
        {
            public IAttackTarget target;
            public float score;
        }

    }

    /// <summary>
    /// Fallback acquisition, at the method EVERY combat think-node reaches. Deliberately a
    /// POSTFIX gated on a null result - see the banner for why cross-band must never outrank
    /// a same-band target, and why this is the inner method rather than the wrapper.
    /// </summary>
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class Patch_AttackTargetFinder_ABCrossBand
    {
        private static void Postfix(IAttackTargetSearcher searcher, TargetScanFlags flags, Predicate<Thing> validator, float minDist, float maxDist, IntVec3 locus, float maxTravelRadiusFromLocus, bool canBashDoors, bool canTakeTargetsCloserThanEffectiveMinRange, bool canBashFences, bool onlyRanged, ref IAttackTarget __result)
        {
            if (__result != null)
            {
                return;
            }

            try
            {
                __result = ABCombatAcquisition.FindCrossBandTarget(searcher, flags, validator, minDist, maxDist, locus, maxTravelRadiusFromLocus, canTakeTargetsCloserThanEffectiveMinRange, onlyRanged, canBashDoors, canBashFences);
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "V2 cross-band acquisition");
            }
        }

    }
}
