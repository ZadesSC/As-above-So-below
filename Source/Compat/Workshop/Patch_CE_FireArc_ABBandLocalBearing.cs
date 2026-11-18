using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_FireArc_ABBandLocalBearing
{
    private static MethodBase TargetMethod()
    {
        Type fireArcCE = ABCECompat.FireArcCE;
        if (fireArcCE == null)
        {
            return null;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(fireArcCE))
        {
            ParameterInfo[] parameters = declaredMethod.GetParameters();
            if (declaredMethod.Name == "WithinFireArc" && parameters.Length == 1 && parameters[0].ParameterType == typeof(LocalTargetInfo))
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

    private static void Prefix(ThingComp __instance, ref LocalTargetInfo __0)
    {
        try
        {
            if (!__0.IsValid || !ABCombatV2.Enabled)
            {
                return;
            }

            Thing thing = __instance?.parent;
            if (thing == null || !thing.Spawned)
            {
                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(thing.Map);
            if (aBBandMap != null && aBBandMap.Banded)
            {
                int num = aBBandMap.BandOf(thing.Position);
                if (aBBandMap.BandOf(__0.Cell) != num)
                {
                    __0 = new LocalTargetInfo(aBBandMap.Translate(__0.Cell, num));
                    ABCECompat.ceFireArcsLocalized++;
                }
            }
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Combat, e, "CE cross-band fire arc");
        }
    }
}
