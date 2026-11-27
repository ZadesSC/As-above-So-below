using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
public static class ABShamblerCrossLevel
{
    private struct DropVerdict
    {
        public int targetId;
        public int untilTick;
    }

    public static bool broken;
    public static int sentAtTarget;
    public static int sentAtBlocker;
    public static int targetsDropped;
    private static readonly FieldInfo TuningField = AccessTools.Field(typeof(JobGiver_ShamblerFight), "tuning");
    private static readonly IntRange MeleeExpiry = new IntRange(240, 540);
    private const int DropCacheTicks = 600;
    private static readonly Dictionary<int, DropVerdict> dropCache = new Dictionary<int, DropVerdict>();
    public static int dropCacheHits;
    [ABGameReset(0)]
    public static void ResetForNewGame()
    {
        dropCache.Clear();
    }

    public static void ResetCounters()
    {
        sentAtTarget = 0;
        sentAtBlocker = 0;
        targetsDropped = 0;
        dropCacheHits = 0;
    }

    public static string CounterReport()
    {
        return "shamblers across levels: sentAtTarget=" + sentAtTarget + " sentAtBlocker=" + sentAtBlocker + " noCrossing=" + targetsDropped + " (cached " + dropCacheHits + ")" + (broken ? " [BROKEN, standing down]" : string.Empty);
    }

    private static bool IsLinkEnd(Thing t)
    {
        return t is Building_ABStairs2 || t is Building_ABAnchor;
    }

    private static bool LetsThrough(Building_Door near, Building_Door far, Pawn pawn)
    {
        return near != null && near.CanPhysicallyPass(pawn) && (far?.CanPhysicallyPass(pawn) ?? true);
    }

    private static bool Drop(Pawn pawn, Thing target, int now, out bool drop)
    {
        drop = true;
        targetsDropped++;
        dropCache[pawn.thingIDNumber] = new DropVerdict
        {
            targetId = target.thingIDNumber,
            untilTick = now + 600
        };
        if (dropCache.Count > 256)
        {
            dropCache.Clear();
        }

        if (ABLog.DevEnabled)
        {
            ABLog.Dev("shambler " + pawn.LabelShort + " at " + pawn.Position.ToString() + " has no crossing to " + target.LabelShort + " at " + target.Position.ToString() + "; wandering instead of waiting (target kept)");
        }

        return true;
    }

    private static Job MeleeJob(Thing target)
    {
        Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
        job.expiryInterval = MeleeExpiry.RandomInRange;
        job.checkOverrideOnExpire = true;
        job.expireRequiresEnemiesNearby = true;
        return job;
    }

    public static bool TryAnswer(Pawn pawn, Thing enemyTarget, out Job job, out bool drop)
    {
        job = null;
        drop = false;
        if (broken || pawn == null || enemyTarget == null || !pawn.Spawned || !enemyTarget.Spawned || pawn.Faction == Faction.OfPlayer)
        {
            return false;
        }

        Map map = pawn.Map;
        if (map == null || enemyTarget.Map != map || !ABBands.Banded(map))
        {
            return false;
        }

        if (ABBands.SameBand(map, pawn.Position, enemyTarget.Position) && !ABBandComponents.KnownDifferentComponents(map, pawn.Position, enemyTarget.Position, pawn))
        {
            return false;
        }

        int ticksGame = Find.TickManager.TicksGame;
        if (dropCache.TryGetValue(pawn.thingIDNumber, out var value) && value.targetId == enemyTarget.thingIDNumber && ticksGame < value.untilTick)
        {
            dropCacheHits++;
            drop = true;
            return true;
        }

        bool flag = ABWormhole.TryGetTransit(map, pawn.Position, enemyTarget.Position, pawn, out var near, out var far, enemyTarget);
        if (flag && !LetsThrough(near, far, pawn))
        {
            return Drop(pawn, enemyTarget, ticksGame, out drop);
        }

        if (!flag)
        {
            ABWormhole.TryGetTransit(map, pawn.Position, enemyTarget.Position, null, out near, out far, enemyTarget);
            if (near != null && !LetsThrough(near, far, pawn))
            {
                return Drop(pawn, enemyTarget, ticksGame, out drop);
            }
        }

        if (near == null || !near.Spawned || !ABBands.SameBand(map, pawn.Position, near.Position))
        {
            return Drop(pawn, enemyTarget, ticksGame, out drop);
        }

        PathFinderCostTuning? tuning = null;
        if (TuningField != null && TuningField.GetValue(null)is PathFinderCostTuning value2)
        {
            tuning = value2;
        }

        using (PawnPath pawnPath = map.pathFinder.FindPathNow(pawn.Position, near, TraverseParms.For(TraverseMode.PassAllDestroyablePlayerOwnedThings), tuning, PathEndMode.Touch))
        {
            if (!pawnPath.Found)
            {
                return Drop(pawn, enemyTarget, ticksGame, out drop);
            }

            Thing thing = pawnPath.FirstBlockingBuilding(out var _, pawn);
            if (thing != null && !IsLinkEnd(thing))
            {
                job = MeleeJob(thing);
                sentAtBlocker++;
                return true;
            }
        }

        if (!flag)
        {
            return Drop(pawn, enemyTarget, ticksGame, out drop);
        }

        job = MeleeJob(enemyTarget);
        sentAtTarget++;
        return true;
    }
}
