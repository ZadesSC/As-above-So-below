using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_VGE_ArtilleryEdgeCell_ABBand
{
    private static bool Prepare()
    {
        return VanillaGravshipCompat.ResolveArtillery();
    }

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(VanillaGravshipCompat.compWorldArtillery, "FindEdgeCell", new Type[2] { typeof(Map), typeof(GlobalTargetInfo) });
    }

    private static void Postfix(ThingComp __instance, Map map, GlobalTargetInfo worldTarget, ref LocalTargetInfo __result)
    {
        try
        {
            Thing thing = __instance?.parent;
            if (thing == null || map == null || !VanillaGravshipCompat.TryBandOf(map, thing.Position, out var _, out var band))
            {
                return;
            }

            float num = (float)VanillaGravshipCompat.getAngle.Invoke(null, new object[2] { map.Tile, worldTarget.Tile });
            Vector3 vector = thing.TrueCenter();
            IntVec3 intVec = IntVec3.Invalid;
            float num2 = float.MaxValue;
            foreach (IntVec3 edgeCell in band.EdgeCells)
            {
                float num3 = Mathf.Abs(num - (edgeCell.ToVector3() - vector).AngleFlat());
                if (num3 < num2)
                {
                    num2 = num3;
                    intVec = edgeCell;
                }
            }

            if (intVec.IsValid)
            {
                Vector3 normalized = (intVec.ToVector3() - vector).normalized;
                IntVec3 cell = new IntVec3(intVec.x + (int)Math.Round(normalized.x), intVec.y, intVec.z + (int)Math.Round(normalized.z));
                VanillaGravshipCompat.artilleryClamped++;
                __result = new LocalTargetInfo(cell);
            }
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] VGE artillery edge-cell clamp threw: " + ex.Message, 2822354);
        }
    }
}
