using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_ProgressRenderer_ABPinCaptureBand
{
    private static bool Prepare()
    {
        return ProgressRendererCompat.Present && ProgressRendererCompat.RenderingSetterTarget != null;
    }

    private static MethodBase TargetMethod()
    {
        return ProgressRendererCompat.RenderingSetterTarget;
    }

    private static void Postfix(MapComponent __instance, bool value)
    {
        try
        {
            ProgressRendererCompat.NotifyRenderingChanged(__instance?.map, value);
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] Progress Renderer pin patch threw: " + ex, 762195952);
        }
    }
}
