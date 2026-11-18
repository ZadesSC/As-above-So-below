using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(TileMutatorWorker_River), "GetMapEdgeNodes")]
public static class Patch_TileMutatorWorker_River_ABBandEdgeNodes
{
    private static readonly AccessTools.FieldRef<TileMutatorWorker_River, IntVec3> RiverCentre = AccessTools.FieldRefAccess<TileMutatorWorker_River, IntVec3>("riverCenter");
    private const float Overhang = 25f;
    internal static int rebased;
    private static void Postfix(TileMutatorWorker_River __instance, Map map, float angle, ref (Vector3, Vector3) __result)
    {
        try
        {
            if (ABGuard.On(ABGuard.LevelGen) && map != null && ABBandLocal.TryBandGeometry(map, out var _, out var _, out var offset) && ABBandedGeneration.TryPendingSurfaceRect(map, out var surface, out offset))
            {
                IntVec3 intVec = RiverCentre(__instance);
                float slope = Mathf.Tan((450f - angle) % 360f * (Mathf.PI / 180f));
                List<Vector2> intersections = new List<Vector2>();
                GenGeo.LineRectIntersection(new Vector2(intVec.x, intVec.z), slope, new Vector2(-25f, (float)surface.minZ - 25f), new Vector2((float)map.Size.x + 25f, (float)(surface.maxZ + 1) + 25f), ref intersections);
                if (intersections.Count >= 2)
                {
                    __result = (new Vector3(intersections[0].x, 0f, intersections[0].y), new Vector3(intersections[1].x, 0f, intersections[1].y));
                    rebased++;
                    string[] array = new string[5]
                    {
                        "V2: river edge nodes rebased onto the surface band: ",
                        null,
                        null,
                        null,
                        null
                    };
                    Vector3 vector;
                    (vector, _) = __result;
                    array[1] = vector.ToString();
                    array[2] = " -> ";
                    vector = __result.Item2;
                    array[3] = vector.ToString();
                    array[4] = ".";
                    ABLog.Dev(string.Concat(array));
                }
            }
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] V2: river edge-node patch threw: " + ex, 762195909);
        }
    }
}
