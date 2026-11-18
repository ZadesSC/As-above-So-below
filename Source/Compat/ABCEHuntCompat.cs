using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
public static class ABCEHuntCompat
{
    private static bool resolved;
    private static MethodInfo rangeLambda;
    private static bool broken;
    internal static bool armed;
    public static int huntDistancesLocalized;
    private static readonly MethodInfo StraightLine = AccessTools.Method(typeof(IntVec3Utility), "DistanceTo", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
    private static readonly MethodInfo BandLocalLine = AccessTools.Method(typeof(ABCEHuntCompat), "HuntDistance");
    private static readonly Dictionary<Type, FieldInfo> driverFields = new Dictionary<Type, FieldInfo>();
    internal static MethodInfo RangeLambda()
    {
        Resolve();
        return rangeLambda;
    }

    public static string CounterReport()
    {
        Resolve();
        if (rangeLambda == null)
        {
            return ABCECompat.Active ? "CE hunt: ⚠ range check NOT FOUND (cross-level hunts rely on the loop guard)" : "CE hunt: not loaded";
        }

        return "CE hunt: rangeChecksLocalized=" + huntDistancesLocalized + (armed ? "" : " | ⚠ range check NOT ARMED") + (broken ? " | ⚠ stood down after a throw" : "");
    }

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        try
        {
            Type type = AccessTools.TypeByName("CombatExtended.JobDriver_Hunt");
            if (type == null)
            {
                return;
            }

            MethodInfo methodInfo = AccessTools.Method(type, "GetOptimalHuntRange", new Type[2] { typeof(Pawn), typeof(Pawn) });
            if (methodInfo == null || StraightLine == null)
            {
                Log.WarningOnce("[As above, So below] Combat Extended is loaded but its hunt driver has no GetOptimalHuntRange(Pawn, Pawn); cross-level hunts are left to CE's own range check.", 2822390);
                return;
            }

            rangeLambda = FindRangeLambda(type, methodInfo, 0);
            if (rangeLambda == null)
            {
                Log.WarningOnce("[As above, So below] Combat Extended's hunt driver has no range check of the expected shape; cross-level hunts are left to CE's own range check and the cast position loop guard.", 2822390);
            }
        }
        catch (Exception ex)
        {
            rangeLambda = null;
            Log.WarningOnce("[As above, So below] Combat Extended hunt bridge failed to resolve: " + ex.Message, 2822391);
        }
    }

    private static MethodInfo FindRangeLambda(Type type, MethodInfo optimal, int recursion)
    {
        if (type == null || recursion > 3)
        {
            return null;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
        {
            if (declaredMethod.IsStatic || declaredMethod.IsAbstract || declaredMethod.ReturnType != typeof(bool) || declaredMethod.GetParameters().Length != 0 || declaredMethod.GetMethodBody() == null)
            {
                continue;
            }

            bool flag = false;
            bool flag2 = false;
            foreach (KeyValuePair<OpCode, object> item in PatchProcessor.ReadMethodBody(declaredMethod))
            {
                if (!(item.Key != OpCodes.Call) || !(item.Key != OpCodes.Callvirt))
                {
                    MethodInfo methodInfo = item.Value as MethodInfo;
                    if (methodInfo == optimal)
                    {
                        flag = true;
                    }
                    else if (methodInfo == StraightLine)
                    {
                        flag2 = true;
                    }
                }
            }

            if (!(flag & flag2))
            {
                continue;
            }

            return declaredMethod;
        }

        Type[] nestedTypes = type.GetNestedTypes(AccessTools.all);
        foreach (Type type2 in nestedTypes)
        {
            MethodInfo methodInfo2 = FindRangeLambda(type2, optimal, recursion + 1);
            if (methodInfo2 != null)
            {
                return methodInfo2;
            }
        }

        return null;
    }

    public static float HuntDistance(IntVec3 a, IntVec3 b, object closure)
    {
        float lengthHorizontal = (a - b).LengthHorizontal;
        if (broken || !ABCombatV2.Enabled)
        {
            return lengthHorizontal;
        }

        try
        {
            Pawn pawn = DriverOf(closure)?.pawn;
            if (pawn == null || !pawn.Spawned)
            {
                return lengthHorizontal;
            }

            ABBandMap aBBandMap = ABBands.CompOf(pawn.Map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return lengthHorizontal;
            }

            int num = aBBandMap.BandOf(a);
            if (aBBandMap.BandOf(b) == num)
            {
                return lengthHorizontal;
            }

            huntDistancesLocalized++;
            return (aBBandMap.Translate(b, num) - a).LengthHorizontal;
        }
        catch (Exception ex)
        {
            broken = true;
            Log.WarningOnce("[As above, So below] Combat Extended hunt bridge threw and has stood down for this session; cross-level hunts fall back to the cast position loop guard: " + ex.GetType().Name + ": " + ex.Message, 2822393);
            return lengthHorizontal;
        }
    }

    private static JobDriver DriverOf(object closure)
    {
        if (closure == null)
        {
            return null;
        }

        if (closure is JobDriver result)
        {
            return result;
        }

        Type type = closure.GetType();
        if (!driverFields.TryGetValue(type, out var value))
        {
            value = null;
            FieldInfo[] fields = type.GetFields(AccessTools.all);
            foreach (FieldInfo fieldInfo in fields)
            {
                if (typeof(JobDriver).IsAssignableFrom(fieldInfo.FieldType))
                {
                    value = fieldInfo;
                    break;
                }
            }

            driverFields[type] = value;
        }

        return (value != null) ? (value.GetValue(closure) as JobDriver) : null;
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input)
    {
        List<CodeInstruction> list = new List<CodeInstruction>(input);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            CodeInstruction codeInstruction = list[i];
            if ((codeInstruction.opcode == OpCodes.Call || codeInstruction.opcode == OpCodes.Callvirt) && codeInstruction.operand as MethodInfo == StraightLine)
            {
                CodeInstruction codeInstruction2 = new CodeInstruction(OpCodes.Ldarg_0);
                codeInstruction2.MoveLabelsFrom(codeInstruction);
                list.Insert(i, codeInstruction2);
                i++;
                codeInstruction.opcode = OpCodes.Call;
                codeInstruction.operand = BandLocalLine;
                num++;
            }
        }

        armed = num > 0;
        if (num == 0)
        {
            Log.WarningOnce("[As above, So below] Combat Extended hunt bridge found no distance call to correct; cross-level hunts rely on the cast position loop guard.", 2822394);
        }
        else
        {
            ABLog.Dev("Combat Extended hunt bridge rewrote " + num + " distance call(s) in the hunt range check.");
        }

        return list;
    }
}
