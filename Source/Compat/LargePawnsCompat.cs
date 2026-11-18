using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
public static class LargePawnsCompat
{
    internal const string OverallType = "LargePawns.Patches.CoverUtility_CalculateOverallBlockChance_LargePatch";
    internal const string GiversType = "LargePawns.Patches.CoverUtility_CalculateCoverGiverSet_LargePatch";
    internal static readonly MethodInfo VanillaLos = AccessTools.Method(typeof(GenSight), "LineOfSight", new Type[7] { typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(bool), typeof(Func<IntVec3, bool>), typeof(int), typeof(int) });
    internal const string PackageId = "neku.largepawns";
    public static int crossAnswered;
    [ThreadStatic]
    private static List<IntVec3> tmpLean;
    internal static bool Active => ModsConfig.IsActive("neku.largepawns") || ModsConfig.IsActive("neku.largepawns_steam");

    internal static MethodBase PostfixOf(string typeName)
    {
        Type type = AccessTools.TypeByName(typeName);
        return (type == null) ? null : AccessTools.Method(type, "Postfix");
    }

    public static bool LineOfSight(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell, Func<IntVec3, bool> validator, int halfXOffset, int halfZOffset)
    {
        try
        {
            if (ABCombatCover.TryCrossShooter(start, map, out var real))
            {
                crossAnswered++;
                return CrossLevelSight(map, real, end);
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "Large Pawns cross-level cover sight");
        }

        return GenSight.LineOfSight(start, end, map, skipFirstCell, validator, halfXOffset, halfZOffset);
    }

    private static bool CrossLevelSight(Map map, IntVec3 real, IntVec3 cell)
    {
        if (ABShaft.TrySolve(map, real, cell, float.MaxValue, 0f, overheadFire: false, out var sol))
        {
            return true;
        }

        ABBandMap aBBandMap = ABBands.CompOf(map);
        if (aBBandMap == null || !aBBandMap.Banded)
        {
            return false;
        }

        int num = aBBandMap.BandOf(real);
        if (tmpLean == null)
        {
            tmpLean = new List<IntVec3>();
        }

        ABCombatV2.LeanCells(real, aBBandMap.Translate(cell, num), map, tmpLean);
        for (int i = 0; i < tmpLean.Count; i++)
        {
            IntVec3 intVec = tmpLean[i];
            if (!(intVec == real) && intVec.InBounds(map) && aBBandMap.BandOf(intVec) == num && !aBBandMap.InGutter(intVec) && ABShaft.TrySolve(map, intVec, cell, float.MaxValue, 0f, overheadFire: false, out sol))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, string where)
    {
        MethodInfo operand = AccessTools.Method(typeof(LargePawnsCompat), "LineOfSight");
        List<CodeInstruction> list = new List<CodeInstruction>(instructions);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && list[i].operand as MethodInfo == VanillaLos)
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = operand;
                num++;
            }
        }

        if (num == 0)
        {
            Log.WarningOnce("[As above, So below] Large Pawns bridge found no line-of-sight test to correct in " + where + "; large pawns shot from another level may read as fully covered.", 0x2B10E1 ^ where.GetHashCode());
        }
        else
        {
            ABLog.Dev("Large Pawns bridge rewrote " + num + " line-of-sight test(s) in " + where + ".");
        }

        return list;
    }
}
