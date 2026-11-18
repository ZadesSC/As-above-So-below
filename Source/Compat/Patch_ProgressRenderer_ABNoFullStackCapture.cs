using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_ProgressRenderer_ABNoFullStackCapture
{
    private static bool Prepare()
    {
        return ProgressRendererCompat.Present && ProgressRendererCompat.ManualRenderTarget != null;
    }

    private static MethodBase TargetMethod()
    {
        return ProgressRendererCompat.ManualRenderTarget;
    }

    private static void Prefix(MapComponent __instance, ref bool forceRenderFullMap)
    {
        try
        {
            ProgressRendererCompat.ClearFullMapRequest(__instance?.map, ref forceRenderFullMap);
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] Progress Renderer full-map guard threw: " + ex, 762195954);
        }
    }
}
