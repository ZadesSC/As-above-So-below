using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_CE_TurretOrderAttack_ABBandLocalRange
{
    private static MethodBase TargetMethod()
    {
        Type turretCE = ABCECompat.TurretCE;
        if (turretCE == null)
        {
            return null;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(turretCE))
        {
            ParameterInfo[] parameters = declaredMethod.GetParameters();
            if (declaredMethod.Name == "OrderAttack" && parameters.Length == 1 && parameters[0].ParameterType == typeof(LocalTargetInfo))
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

    private static bool Prefix(Building_Turret __instance, LocalTargetInfo targ)
    {
        ABCECompat.PushOrderScope(__instance?.Map);
        try
        {
            if (!targ.IsValid || __instance == null || !__instance.Spawned)
            {
                return true;
            }

            Verb attackVerb = __instance.AttackVerb;
            ABBandMap aBBandMap = ABBands.CompOf(__instance.Map);
            bool flag = attackVerb != null && ABCombatV2.OwnsPair(attackVerb, __instance.Position, targ);
            if (ABLog.DevEnabled)
            {
                ABV2Debug.Combat("CE turret order: " + __instance.LabelShortCap + " at " + __instance.Position.ToString() + " (band " + (aBBandMap?.BandOf(__instance.Position) ?? (-1)) + ") -> " + targ.Cell.ToString() + " (band " + (aBBandMap?.BandOf(targ.Cell) ?? (-1)) + ") | verb=" + ((attackVerb == null) ? "NULL" : attackVerb.GetType().Name) + " range=" + ((attackVerb == null) ? "?" : attackVerb.verbProps.range.ToString("0.0")) + " min=" + ((attackVerb == null) ? "?" : attackVerb.verbProps.minRange.ToString("0.0")) + " | owns=" + flag);
            }

            if (!flag)
            {
                return true;
            }

            bool flag2 = ABCombatV2.TrySolve(attackVerb, __instance.Position, targ, out var sol);
            ABV2Debug.Combat("CE turret order: solver returned " + flag2 + " for " + __instance.LabelShortCap + " -> " + targ.Cell.ToString());
            if (flag2)
            {
                string[] array = new string[7]
                {
                    "CE turret ",
                    __instance.LabelShortCap,
                    " forced target ",
                    targ.Cell.ToString(),
                    " accepted, dist ",
                    sol.distance.ToString("0.0"),
                    null
                };
                string text;
                if (!sol.overhead)
                {
                    IntVec3 opening = sol.opening;
                    text = " via opening " + opening.ToString();
                }
                else
                {
                    text = " (overhead)";
                }

                array[6] = text;
                ABV2Debug.Combat(string.Concat(array));
                return true;
            }

            float lengthHorizontal = (aBBandMap.Translate(targ.Cell, aBBandMap.BandOf(__instance.Position)) - __instance.Position).LengthHorizontal;
            bool flag3 = lengthHorizontal < attackVerb.verbProps.EffectiveMinRange(targ, __instance);
            ABV2Debug.Combat("CE turret " + __instance.LabelShortCap + " forced target " + targ.Cell.ToString() + " REFUSED: band-local dist " + lengthHorizontal.ToString("0.0") + (flag3 ? " is inside minimum range" : "") + ", solver found no shot (no opening between the levels, or past range)");
            Messages.Message((flag3 ? "MessageTargetBelowMinimumRange" : "MessageTargetBeyondMaximumRange").Translate(), __instance, MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] CE turret forced-target prefix threw: " + ex.GetType().Name + ": " + ex.Message, 2822353);
            ABGuard.Disable(ABGuard.Combat, ex, "CE turret cross-band forced target");
        }

        return true;
    }

    private static void Finalizer()
    {
        ABCECompat.PopOrderScope();
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return ABCECompat.RewriteCellSubtraction(instructions, "Building_TurretGunCE.OrderAttack", 2);
    }
}
