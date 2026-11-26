using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_VGE_ArtillerySpawnCell_ABBand
{
    private static bool Prepare()
    {
        return VanillaGravshipCompat.ResolveArtillery();
    }

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(VanillaGravshipCompat.artilleryUtility, "FindSpawnCell", new Type[4] { typeof(Map), typeof(PlanetTile), typeof(PlanetTile), typeof(IntVec3) });
    }

    private static void Postfix(Map map, PlanetTile targetTile, PlanetTile startTile, IntVec3 targetCell, ref IntVec3 __result)
    {
        try
        {
            if (!VanillaGravshipCompat.TryBandOf(map, targetCell, out var _, out var band) || (band.Contains(__result) && band.IsOnEdge(__result)))
            {
                return;
            }

            float num = (float)VanillaGravshipCompat.getAngle.Invoke(null, new object[2] { targetTile, startTile });
            Vector3 vector = Vector3Utility.FromAngleFlat(num - 90f);
            Vector3 vect = targetCell.ToVector3();
            IntVec3 intVec = targetCell;
            for (int i = 0; i < 4096; i++)
            {
                vect += vector;
                IntVec3 intVec2 = vect.ToIntVec3();
                if (!band.Contains(intVec2))
                {
                    break;
                }

                intVec = intVec2;
            }

            if (intVec != __result)
            {
                VanillaGravshipCompat.artilleryClamped++;
                __result = intVec;
            }
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] VGE artillery spawn-cell clamp threw: " + ex.Message, 2822353);
        }
    }
}
