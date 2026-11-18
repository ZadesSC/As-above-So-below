using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_SmarterConstruction_ABRankingScope
{
    private static MethodBase TargetMethod()
    {
        return SmarterConstructionCompat.ClosestCustomMethod();
    }

    private static bool Prepare()
    {
        if (TargetMethod() == null || !SmarterConstructionCompat.CanRewrite)
        {
            return false;
        }

        using (IEnumerator<MethodBase> enumerator = SmarterConstructionCompat.DistanceLambdaMethods().GetEnumerator())
        {
            if (enumerator.MoveNext())
            {
                MethodBase current = enumerator.Current;
                return true;
            }
        }

        return false;
    }

    private static void Prefix(Map __1, TraverseParms __4)
    {
        SmarterConstructionCompat.Push(__1, __4.pawn);
    }

    private static void Finalizer()
    {
        SmarterConstructionCompat.Pop();
    }
}
