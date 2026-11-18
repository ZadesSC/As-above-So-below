using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
internal static class CommonSenseCompat
{
    private static readonly MethodInfo StraightLineSquared = AccessTools.Method(typeof(IntVec3Utility), "DistanceToSquared", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
    private static readonly MethodInfo WalkLineSquared = AccessTools.Method(typeof(CommonSenseCompat), "WalkSquared");
    private static bool resolved;
    private static MethodInfo haulingOpportunity;
    private static bool broken;
    private static Map scopeMap;
    private static Pawn scopePawn;
    private static int depth;
    private static readonly Dictionary<long, int> trips = new Dictionary<long, int>(32);
    private static int scopeRetargets;
    internal static int totalRetargets;
    internal static bool Present
    {
        get
        {
            Resolve();
            return haulingOpportunity != null;
        }
    }

    internal static bool CanRewrite => StraightLineSquared != null && WalkLineSquared != null;

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        try
        {
            Type type = AccessTools.TypeByName("CommonSense.OpportunisticTasks");
            if (type == null)
            {
                return;
            }

            foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
            {
                if (!(declaredMethod.Name != "Hauling_Opportunity"))
                {
                    ParameterInfo[] parameters = declaredMethod.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Job) && parameters[1].ParameterType == typeof(Pawn) && declaredMethod.ReturnType == typeof(Job))
                    {
                        haulingOpportunity = declaredMethod;
                        break;
                    }
                }
            }

            if (haulingOpportunity == null)
            {
                Log.WarningOnce("[As above, So below] Common Sense is loaded but OpportunisticTasks.Hauling_Opportunity was not found; colonists may keep choosing hauling over a bill that is on another level.", 2822352);
            }
            else
            {
                ABLog.Dev("Common Sense bridge resolved (hauling-over-bills distance).");
            }
        }
        catch (Exception ex)
        {
            haulingOpportunity = null;
            Log.WarningOnce("[As above, So below] Common Sense bridge failed to resolve: " + ex.Message, 2822353);
        }
    }

    internal static MethodBase HaulingOpportunityMethod()
    {
        Resolve();
        return haulingOpportunity;
    }

    internal static void Push(Pawn pawn)
    {
        if (depth++ > 0)
        {
            return;
        }

        trips.Clear();
        scopeRetargets = 0;
        scopeMap = null;
        scopePawn = null;
        if (!broken && pawn != null)
        {
            Map map = pawn.Map;
            if (map != null && ABBands.Banded(map) && ABWormhole.PairCount(map) != 0)
            {
                scopeMap = map;
                scopePawn = pawn;
            }
        }
    }

    internal static void Pop()
    {
        if (depth != 0 && --depth <= 0)
        {
            if (scopeRetargets > 0)
            {
                ABLog.Dev("Common Sense: " + scopeRetargets + " cross-level measurement" + ((scopeRetargets == 1) ? "" : "s") + " corrected for " + ((scopePawn != null) ? scopePawn.LabelShortCap : "?") + " (hauling vs bill).");
            }

            scopeMap = null;
            scopePawn = null;
            trips.Clear();
        }
    }

    public static int WalkSquared(IntVec3 a, IntVec3 b)
    {
        int num = a.DistanceToSquared(b);
        Map map = scopeMap;
        if (map == null)
        {
            return num;
        }

        long key = Key(a, b);
        if (trips.TryGetValue(key, out var value))
        {
            return value;
        }

        int num2 = num;
        try
        {
            if (ABBandDistance.TryCrossLevel(map, scopePawn, a, b, out var length))
            {
                float num3 = length * length;
                num2 = ((num3 >= 2.1474836E+09f) ? int.MaxValue : Mathf.RoundToInt(num3));
            }
        }
        catch (Exception ex)
        {
            broken = true;
            scopeMap = null;
            scopePawn = null;
            Log.WarningOnce("[As above, So below] Common Sense distance bridge threw and has stood down for this session; hauling-over-bills will measure in straight lines again: " + ex.Message, 2822354);
            return num;
        }

        trips[key] = num2;
        if (num2 != num)
        {
            scopeRetargets++;
            totalRetargets++;
        }

        return num2;
    }

    private static long Key(IntVec3 a, IntVec3 b)
    {
        return (long)(((ulong)(ushort)a.x << 48) | ((ulong)(ushort)a.z << 32) | ((ulong)(ushort)b.x << 16) | (ushort)b.z);
    }

    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, string where, int expected)
    {
        List<CodeInstruction> list = new List<CodeInstruction>(input);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            CodeInstruction codeInstruction = list[i];
            if ((codeInstruction.opcode == OpCodes.Call || codeInstruction.opcode == OpCodes.Callvirt) && codeInstruction.operand as MethodInfo == StraightLineSquared)
            {
                codeInstruction.operand = WalkLineSquared;
                num++;
            }
        }

        if (num == 0)
        {
            Log.WarningOnce("[As above, So below] Common Sense bridge found no distance calls to correct in " + where + "; colonists may keep choosing hauling over a bill that is on another level.", 2822355);
        }
        else
        {
            ABLog.Dev("Common Sense bridge rewrote " + num + " of " + expected + " expected distance calls in " + where + ".");
        }

        return list;
    }
}
