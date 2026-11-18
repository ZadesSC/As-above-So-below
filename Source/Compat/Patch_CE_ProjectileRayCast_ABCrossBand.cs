using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_ProjectileRayCast_ABCrossBand
{
    private static MethodBase TargetMethod()
    {
        Type projectileCE = ABCECompat.ProjectileCE;
        if (projectileCE == null)
        {
            return null;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(projectileCE))
        {
            ParameterInfo[] parameters = declaredMethod.GetParameters();
            if (declaredMethod.Name == "RayCast" && parameters.Length >= 6 && parameters[0].ParameterType == typeof(Thing) && parameters[2].ParameterType == typeof(Vector2) && parameters[3].ParameterType == typeof(float) && parameters[4].ParameterType == typeof(float) && parameters[5].ParameterType == typeof(float))
            {
                return declaredMethod;
            }
        }

        return null;
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && ABCECompat.fIntendedTarget != null;
    }

    private static void Prefix(Thing __instance, Thing __0, ref Vector2 __2, ref float __3, ref float __4, ref float __5)
    {
        try
        {
            if (__instance == null || __0 == null || !__0.Spawned || !ABCombatV2.Enabled)
            {
                return;
            }

            Map map = __0.Map;
            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return;
            }

            Vector2 vector = __2;
            if (!ABCECompat.TryRemap(__instance, map, aBBandMap, ref __2, ref __3, ref __4, ref __5, 0f, instant: true, out var r))
            {
                return;
            }

            if (r.viaOpening)
            {
                ABCECompat.fIgnoreRoof?.SetValue(__instance, true);
                ABCECompat.ceOpeningEmergences++;
            }

            if (!r.solved)
            {
                ABCECompat.ceLaunchUnsolved++;
            }

            ABCECompat.ceRayCastsRemapped++;
            if (ABV2Debug.LogCombat)
            {
                string[] array = new string[12]
                {
                    "CE ray ",
                    __instance.def.defName,
                    " origin ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                };
                Vector2 vector2 = vector;
                array[3] = vector2.ToString();
                array[4] = " (band ";
                array[5] = r.bandFrom.ToString();
                array[6] = ") -> ";
                vector2 = __2;
                array[7] = vector2.ToString();
                array[8] = " (band ";
                array[9] = r.bandTo.ToString();
                array[10] = ") ";
                string text;
                if (!r.viaOpening)
                {
                    text = "shooter's column";
                }
                else
                {
                    IntVec3 opening = r.sol.opening;
                    text = "out of opening " + opening.ToString();
                }

                array[11] = text;
                ABV2Debug.Combat(string.Concat(array));
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "CE cross-band instant shot");
        }
    }
}
