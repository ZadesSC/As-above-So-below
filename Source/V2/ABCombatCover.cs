using System;
using Verse;

namespace AsAboveSoBelow;
public static class ABCombatCover
{
    public struct Scope
    {
        public bool fromBelow;
        public Map crossMap;
        public IntVec3 crossReal;
        public IntVec3 crossCarried;
    }

    [ThreadStatic]
    private static bool fromBelow;
    [ThreadStatic]
    private static Map crossMap;
    [ThreadStatic]
    private static IntVec3 crossReal;
    [ThreadStatic]
    private static IntVec3 crossCarried;
    public static int localized;
    public static int pointBlankSkipped;
    internal static bool pointBlankRuleArmed;
    internal static Scope Snapshot()
    {
        return new Scope
        {
            fromBelow = fromBelow,
            crossMap = crossMap,
            crossReal = crossReal,
            crossCarried = crossCarried
        };
    }

    internal static bool TryCrossShooter(IntVec3 carried, Map map, out IntVec3 real)
    {
        real = crossReal;
        return crossMap != null && crossMap == map && carried == crossCarried;
    }

    public static void ResetCounters()
    {
        localized = 0;
        pointBlankSkipped = 0;
    }

    public static string CounterReport()
    {
        return "cover: localized=" + localized + " pointBlankSkipped(from below)=" + pointBlankSkipped + (pointBlankRuleArmed ? "" : " [from-below rule NOT ARMED]");
    }

    internal static void Enter(LocalTargetInfo target, ref IntVec3 shooterLoc, Map map)
    {
        if (map == null || !target.IsValid || !ABCombatV2.Enabled)
        {
            return;
        }

        ABBandMap aBBandMap = ABBands.CompOf(map);
        if (aBBandMap == null || !aBBandMap.Banded)
        {
            return;
        }

        IntVec3 cell = target.Cell;
        if (!cell.InBounds(map) || !shooterLoc.InBounds(map))
        {
            return;
        }

        int num = aBBandMap.BandOf(cell);
        int num2 = aBBandMap.BandOf(shooterLoc);
        if (aBBandMap.InGutter(cell) || aBBandMap.InGutter(shooterLoc))
        {
            return;
        }

        if (num == num2)
        {
            if (ABCombatPosition.ScopeEnemyBelow(shooterLoc))
            {
                fromBelow = true;
            }

            return;
        }

        IntVec3 intVec = shooterLoc;
        shooterLoc = aBBandMap.Translate(shooterLoc, num);
        fromBelow = num2 < num;
        crossMap = map;
        crossReal = intVec;
        crossCarried = shooterLoc;
        localized++;
    }

    internal static void Exit(Scope previous)
    {
        fromBelow = previous.fromBelow;
        crossMap = previous.crossMap;
        crossReal = previous.crossReal;
        crossCarried = previous.crossCarried;
    }

    public static float PointBlankDistance(ref IntVec3 delta)
    {
        float lengthHorizontal = delta.LengthHorizontal;
        if (fromBelow && lengthHorizontal < 2.9f)
        {
            pointBlankSkipped++;
            return float.MaxValue;
        }

        return lengthHorizontal;
    }
}
