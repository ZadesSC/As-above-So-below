using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmartDecon_ABScopeRoofScan
{
    private static MethodBase TargetMethod()
    {
        return SmarterDeconstructionCompat.CheckForRoofsMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    private static void Postfix(ref IEnumerable<Toil> __result, JobDriver __1)
    {
        if (__result == null)
        {
            return;
        }

        Map map = (__1?.pawn)?.Map;
        if (map == null || !ABBands.Banded(map))
        {
            return;
        }

        List<Toil> list = new List<Toil>(__result);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Toil toil = list[i];
            Action action = toil?.initAction;
            if (action != null && SmarterDeconstructionCompat.IsTheirs(action))
            {
                Action body = action;
                Map scoped = map;
                toil.initAction = () =>
                {
                    SmarterDeconstructionCompat.RunScoped(scoped, body);
                };
                num++;
            }
        }

        if (num > 0)
        {
            SmarterDeconstructionCompat.totalToilsWrapped += num;
        }

        __result = list;
    }
}
