using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_CoverWalk_ABTargetLevel
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
            if (declaredMethod.Name == "GetHighestCoverAndSmokeForTarget" && parameters.Length == 4 && parameters[0].ParameterType == typeof(LocalTargetInfo))
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

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = ABCECompat.RewritePositionReads(instructions, "Verb_LaunchProjectileCE.GetHighestCoverAndSmokeForTarget", 2, "CoverEye", 2822354, out var swapped);
        ABCECompat.coverSeamArmed = swapped > 0;
        return result;
    }
}
