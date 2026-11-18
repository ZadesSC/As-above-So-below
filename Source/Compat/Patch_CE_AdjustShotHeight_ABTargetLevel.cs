using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_AdjustShotHeight_ABTargetLevel
{
    private static MethodBase TargetMethod()
    {
        Type verbCE = ABCECompat.VerbCE;
        if (verbCE == null)
        {
            return null;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(verbCE))
        {
            ParameterInfo[] parameters = declaredMethod.GetParameters();
            if (declaredMethod.Name == "AdjustShotHeight" && parameters.Length == 3 && parameters[0].ParameterType == typeof(Thing) && parameters[1].ParameterType == typeof(LocalTargetInfo))
            {
                return declaredMethod;
            }
        }

        return null;
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    private static void Prefix(Verb __instance, LocalTargetInfo __1, out ABCECompat.EyeScope __state)
    {
        __state = ABCECompat.OpenEye(__instance, __1);
    }

    private static void Finalizer(ABCECompat.EyeScope __state)
    {
        ABCECompat.CloseEye(__state);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = ABCECompat.RewritePositionReads(instructions, "Verb_LaunchProjectileCE.AdjustShotHeight", 2, "HeightEye", 2822355, out var swapped);
        ABCECompat.heightSeamArmed = swapped > 0;
        return result;
    }
}
