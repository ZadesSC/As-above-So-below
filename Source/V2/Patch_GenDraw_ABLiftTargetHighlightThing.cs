using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(GenDraw), "DrawTargetingHighlight_Thing")]
public static class Patch_GenDraw_ABLiftTargetHighlightThing
{
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(GenDraw), "DrawTargetingHighlight_Thing") != null;
    }

    private static bool Prefix(Thing t)
    {
        try
        {
            if (t == null || !t.Spawned || !ABGuard.On(ABGuard.Rendering))
            {
                return true;
            }

            Map map = t.Map;
            if (map == null || map != Find.CurrentMap)
            {
                return true;
            }

            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return true;
            }

            if (!ABRangeOverlay.TryLiftCell(map, aBBandMap, ABBandView.CurrentBand(map), t.Position, out var lifted))
            {
                return true;
            }

            Vector3 vector = t.TrueCenter();
            Graphics.DrawMesh(position: new Vector3(vector.x, AltitudeLayer.MapDataOverlay.AltitudeFor(), vector.z + (float)(lifted.z - t.Position.z)), mesh: MeshPool.plane10, rotation: t.Rotation.AsQuat, material: GenDraw.CurTargetingMat, layer: 0);
            if (t is Pawn || t is Corpse)
            {
                TargetHighlighter.Highlight(t, arrow: false);
            }

            return false;
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Rendering, e, "V2 target highlight lift (thing)");
        }

        return true;
    }
}
