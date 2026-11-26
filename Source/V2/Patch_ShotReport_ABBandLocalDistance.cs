using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(ShotReport), "HitReportFor")]
public static class Patch_ShotReport_ABBandLocalDistance
{
    private static readonly AccessTools.StructFieldRef<ShotReport, float> GasRef = MakeGasRef();
    [ThreadStatic]
    private static Map scopeMap;
    private static bool armed;
    public static int localizedReports;
    public static int smokeRewalks;
    private static AccessTools.StructFieldRef<ShotReport, float> MakeGasRef()
    {
        FieldInfo fieldInfo = AccessTools.Field(typeof(ShotReport), "factorFromCoveringGas");
        return (fieldInfo == null) ? null : AccessTools.StructFieldRefAccess<ShotReport, float>(fieldInfo);
    }

    public static string CounterReport()
    {
        return "shotReport: localized=" + localizedReports + " smokeRewalks=" + smokeRewalks + (armed ? "" : " [distance swap NOT ARMED]");
    }

    public static void ResetCounters()
    {
        localizedReports = 0;
        smokeRewalks = 0;
    }

    public static IntVec3 ShotDelta(IntVec3 a, IntVec3 b)
    {
        Map map = scopeMap;
        if (map == null)
        {
            return a - b;
        }

        ABBandMap aBBandMap = ABBands.CompOf(map);
        if (aBBandMap == null || !aBBandMap.Banded || !a.InBounds(map) || !b.InBounds(map))
        {
            return a - b;
        }

        int num = aBBandMap.BandOf(b);
        if (aBBandMap.BandOf(a) == num || aBBandMap.InGutter(a) || aBBandMap.InGutter(b))
        {
            return a - b;
        }

        localizedReports++;
        return aBBandMap.Translate(a, num) - b;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo methodInfo = AccessTools.Method(typeof(IntVec3), "op_Subtraction", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
        MethodInfo operand = AccessTools.Method(typeof(Patch_ShotReport_ABBandLocalDistance), "ShotDelta");
        List<CodeInstruction> list = new List<CodeInstruction>(instructions);
        armed = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Call && list[i].operand as MethodInfo == methodInfo)
            {
                list[i].operand = operand;
                armed = true;
                break;
            }
        }

        if (!armed)
        {
            Log.WarningOnce("[As above, So below] shot report: no distance subtraction found in ShotReport.HitReportFor; accuracy across levels will read the whole stack as range, and smoke along a cross-level shot is not re-walked.", 2822392);
        }

        return list;
    }

    private static void Prefix(Thing caster, out Map __state)
    {
        __state = scopeMap;
        scopeMap = null;
        try
        {
            if (armed && caster != null && caster.Spawned && ABCombatV2.Enabled && ABBands.Banded(caster.Map))
            {
                scopeMap = caster.Map;
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "V2 shot report scope");
        }
    }

    private static void Postfix(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport __result)
    {
        try
        {
            Map map = scopeMap;
            if (map != null && verb != null && target.IsValid && GasRef != null && __result.ShootLine.Source.IsValid)
            {
                ABBandMap aBBandMap = ABBands.CompOf(map);
                IntVec3 position = caster.Position;
                if (aBBandMap != null && aBBandMap.BandOf(position) != aBBandMap.BandOf(target.Cell) && (verb.EquipmentSource == null || !verb.EquipmentSource.TryGetComp<CompUniqueWeapon>(out var comp) || !comp.IgnoreAccuracyMaluses) && ABCombatV2.TrySolve(verb, position, target, out var sol, out var source, out var dest))
                {
                    bool flag = SolvedLineHasSmoke(map, aBBandMap, sol, source, dest);
                    smokeRewalks++;
                    GasRef(ref __result) = (flag ? 0.7f : 1f);
                }
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "V2 shot report smoke walk");
        }
    }

    private static void Finalizer(Map __state)
    {
        scopeMap = __state;
    }

    internal static bool SolvedLineHasSmoke(Map map, ABBandMap bands, ABShotSolution sol, IntVec3 source, IntVec3 dest)
    {
        int toBand = bands.BandOf(source);
        int toBand2 = bands.BandOf(dest);
        if (sol.overhead || !sol.opening.IsValid)
        {
            return LineHasSmoke(map, bands.Translate(source, toBand2), dest);
        }

        return LineHasSmoke(map, source, bands.Translate(sol.opening, toBand)) || LineHasSmoke(map, bands.Translate(sol.opening, toBand2), dest);
    }

    private static bool LineHasSmoke(Map map, IntVec3 from, IntVec3 to)
    {
        foreach (IntVec3 item in new ShootLine(from, to).Points())
        {
            if (item.InBounds(map) && item.AnyGas(map, GasType.BlindSmoke))
            {
                return true;
            }
        }

        return false;
    }
}
