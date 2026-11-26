using System;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(WorldCameraDriver), "ApplyMapPositionToGameObject")]
public static class Patch_WorldCameraDriver_ABBandParallax
{
    private static bool Prefix()
    {
        Map currentMap = Find.CurrentMap;
        if (currentMap == null || !ABGuard.On(ABGuard.Camera))
        {
            return true;
        }

        ABBandMap aBBandMap = ABBands.CompOf(currentMap);
        if (aBBandMap == null || !aBBandMap.Banded)
        {
            return true;
        }

        try
        {
            Vector3 vector = ((currentMap.ParentHolder is MapParent mapParent) ? mapParent.WorldCameraPosition : Find.WorldGrid.GetTileCenter(currentMap.Tile));
            if (vector == Vector3.zero)
            {
                return true;
            }

            PlanetLayer layer = currentMap.Tile.Layer;
            Camera worldCamera = Find.WorldCamera;
            if (layer == null || worldCamera == null)
            {
                return true;
            }

            Vector3 vector2 = -vector.normalized;
            vector += -vector2 * layer.BackgroundWorldCameraOffset;
            Transform transform = worldCamera.transform;
            transform.rotation = Quaternion.LookRotation(vector2, Vector3.up);
            float num = layer.BackgroundWorldCameraParallaxDistancePer100Cells;
            if (num == 0f)
            {
                transform.position = vector;
                return false;
            }

            CellRect cellRect = aBBandMap.RectOfBand(ABBandView.CurrentBand(currentMap));
            Vector3 position = Find.Camera.transform.position;
            float num2 = Mathf.InverseLerp(2f, (float)currentMap.Size.x - 2f, position.x);
            float num3 = Mathf.InverseLerp((float)cellRect.minZ + 2f, (float)cellRect.maxZ + 1f - 2f, position.z);
            int x = currentMap.Size.x;
            int height = cellRect.Height;
            float num4 = 1f;
            float num5 = 1f;
            if (x > height)
            {
                num5 = (float)height / (float)x;
                num = num * (float)x / 100f;
            }
            else if (height > x)
            {
                num4 = (float)x / (float)height;
                num = num * (float)height / 100f;
            }

            Vector3 up = transform.up;
            Vector3 right = transform.right;
            Vector3 vector3 = up * (num3 * num * num5) - up * num / 2f * num5;
            Vector3 vector4 = right * (num2 * num * num4) - right * num / 2f * num4;
            transform.position = vector + vector3 + vector4 + layer.Origin;
            return false;
        }
        catch (Exception e)
        {
            ABGuard.Disable(ABGuard.Camera, e, "V2 planet backdrop parallax");
            return true;
        }
    }
}
