using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(DesignationManager), "AllDesignations", MethodType.Getter)]
public static class Patch_DesignationManager_ABCaptureArea
{
    private static bool Prepare()
    {
        return ProgressRendererCompat.Present;
    }

    private static void Postfix(DesignationManager __instance, ref List<Designation> __result)
    {
        try
        {
            ProgressRendererCompat.SubstituteCornerMarkers(__instance, ref __result);
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] Progress Renderer capture-area patch threw: " + ex, 762195953);
        }
    }
}
