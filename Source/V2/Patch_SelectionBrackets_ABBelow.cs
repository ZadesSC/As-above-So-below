using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;

[HarmonyPatch(typeof(SelectionDrawer), "DrawSelectionBracketFor")]
public static class Patch_SelectionBrackets_ABBelow
{
    internal static int rewritten;

    internal static int calls;

    internal static int localized;

    internal static Vector3 lastIn;

    internal static Vector3 lastOut;

    internal static int pivotsLifted;

    private static Vector3 liftDelta;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo calculateBrackets = AccessTools.Method(typeof(Patch_SelectionBrackets_ABBelow), "Calc");
        MethodInfo liftPivot = AccessTools.Method(typeof(Patch_SelectionBrackets_ABBelow), "LiftPivot");
        MethodInfo method = AccessTools.Method(typeof(Vector3), "op_Subtraction", new Type[2]
        {
            typeof(Vector3),
            typeof(Vector3)
        });
        List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
        rewritten = 0;
        pivotsLifted = 0;
        int calculateIndex = -1;
        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction codeInstruction = codes[i];
            if ((codeInstruction.opcode == OpCodes.Call || codeInstruction.opcode == OpCodes.Callvirt) && codeInstruction.operand is MethodInfo { Name: "CalculateSelectionBracketPositionsWorld" } methodInfo && methodInfo.DeclaringType == typeof(SelectionDrawerUtility) && methodInfo.IsGenericMethod && methodInfo.GetGenericArguments()[0] == typeof(object))
            {
                codeInstruction.opcode = OpCodes.Call;
                codeInstruction.operand = calculateBrackets;
                rewritten++;
                calculateIndex = i;
            }
        }
        if (rewritten == 0)
        {
            Log.Warning("[As above, So below] selection brackets: no CalculateSelectionBracketPositionsWorld<object> call found in SelectionDrawer.DrawSelectionBracketFor; things seen through the floor will show no brackets.");
            return codes;
        }
        int pivotLocal = -1;
        for (int j = calculateIndex + 1; j < codes.Count; j++)
        {
            if (pivotLocal >= 0)
            {
                break;
            }
            if (codes[j].Calls(method) && codes[j - 1].IsLdloc())
            {
                pivotLocal = LocalIndex(codes[j - 1]);
            }
        }
        List<int> pivotReads = new List<int>();
        int index = calculateIndex + 1;
        while (pivotLocal >= 0 && index < codes.Count)
        {
            if (codes[index].IsLdloc() && codes[index].opcode != OpCodes.Ldloca && codes[index].opcode != OpCodes.Ldloca_S && LocalIndex(codes[index]) == pivotLocal)
            {
                pivotReads.Add(index);
            }
            index++;
        }
        if (pivotReads.Count == 2)
        {
            codes.Insert(pivotReads[1] + 1, new CodeInstruction(OpCodes.Call, liftPivot));
            codes.Insert(pivotReads[0] + 1, new CodeInstruction(OpCodes.Call, liftPivot));
            pivotsLifted = 2;
        }
        else
        {
            Log.Warning("[As above, So below] selection brackets: found " + pivotReads.Count + " read(s) of the draw pivot, expected 2; left it alone, so brackets of stacked items or zoomed-out pawns seen through the floor may sit off their thing.");
        }
        return codes;
    }

    private static int LocalIndex(CodeInstruction ins)
    {
        if (ins.opcode == OpCodes.Ldloc_0)
        {
            return 0;
        }
        if (ins.opcode == OpCodes.Ldloc_1)
        {
            return 1;
        }
        if (ins.opcode == OpCodes.Ldloc_2)
        {
            return 2;
        }
        if (ins.opcode == OpCodes.Ldloc_3)
        {
            return 3;
        }
        if (!(ins.operand is LocalVariableInfo { LocalIndex: var localIndex }))
        {
            object localOperand = ins.operand;
            if ((localOperand is byte || localOperand is ushort || localOperand is int) ? true : false)
            {
                return Convert.ToInt32(ins.operand);
            }
            return -1;
        }
        return localIndex;
    }

    public static Vector3 LiftPivot(Vector3 pivot)
    {
        return pivot + liftDelta;
    }

    public static void Calc(Vector3[] bracketLocs, object obj, Vector3 worldPos, Vector2 worldSize, Dictionary<object, float> selectTimes, Vector2 textureSize, float jumpDistanceFactor, float deselectedJumpFactor)
    {
        calls++;
        lastIn = worldPos;
        liftDelta = Vector3.zero;
        try
        {
            if (ABBelowSelectionDraw.TryLocalize(Find.CurrentMap, worldPos, out var localizedPosition))
            {
                liftDelta = localizedPosition - worldPos;
                worldPos = localizedPosition;
                localized++;
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Rendering, e, "V2 below selection brackets");
        }
        lastOut = worldPos;
        SelectionDrawerUtility.CalculateSelectionBracketPositionsWorld(bracketLocs, obj, worldPos, worldSize, selectTimes, textureSize, jumpDistanceFactor, deselectedJumpFactor);
    }
}
