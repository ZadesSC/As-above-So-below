using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(JobGiver_ShamblerFight), "MeleeAttackJob")]
public static class Patch_ShamblerFight_ABCrossLevelMelee
{
    private static bool Prefix(Pawn pawn, Thing enemyTarget, ref Job __result)
    {
        try
        {
            if (!ABShamblerCrossLevel.TryAnswer(pawn, enemyTarget, out var job, out var _))
            {
                return true;
            }

            __result = job;
            return false;
        }
        catch (Exception ex)
        {
            ABShamblerCrossLevel.broken = true;
            Log.Warning("[As above, So below] shambler cross-level melee disabled for this session: " + ex);
            return true;
        }
    }
}
