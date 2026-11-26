using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
public static class ProgressRendererCompat
{
    public const int LevelTopmost = 0;
    public const int LevelSurface = 1;
    public const int LevelViewed = 2;
    private const string RenderManagerTypeName = "ProgressRenderer.MapComponent_RenderManager";
    private const string CornerMarkerDefName = "CornerMarker";
    private const int PinWatchdogMs = 10000;
    private static bool resolved;
    private static Type renderManagerType;
    private static MethodInfo renderingSetter;
    private static MethodInfo manualRenderMethod;
    private static bool broken;
    private static DesignationDef cornerMarker;
    private static bool cornerMarkerResolved;
    private static Map pinnedMap;
    private static int pinnedBand;
    private static int pinnedAtMs;
    private static List<Designation> pinnedMarkers;
    public static bool Present
    {
        get
        {
            Resolve();
            return renderManagerType != null && !broken;
        }
    }

    internal static MethodBase RenderingSetterTarget
    {
        get
        {
            Resolve();
            return renderingSetter;
        }
    }

    internal static MethodBase ManualRenderTarget
    {
        get
        {
            Resolve();
            return manualRenderMethod;
        }
    }

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        renderManagerType = AccessTools.TypeByName("ProgressRenderer.MapComponent_RenderManager");
        if (renderManagerType == null)
        {
            ABLog.Dev("Progress Renderer compat: not present.");
            return;
        }

        try
        {
            renderingSetter = AccessTools.PropertySetter(renderManagerType, "Rendering");
        }
        catch (Exception ex)
        {
            ABLog.Dev("Progress Renderer compat: Rendering setter not found (" + ex.Message + ") - captures will not be pinned to a level.");
        }

        try
        {
            manualRenderMethod = AccessTools.Method(renderManagerType, "DoManualRendering");
        }
        catch (Exception ex2)
        {
            ABLog.Dev("Progress Renderer compat: DoManualRendering not found (" + ex2.Message + ") - the force-full-map keybinding stays unguarded.");
        }

        ABLog.Dev("Progress Renderer compat: ACTIVE (renderingSetter=" + ((renderingSetter != null) ? "FOUND" : "absent") + " manualRender=" + ((manualRenderMethod != null) ? "FOUND" : "absent") + ").");
    }

    internal static void NotifyRenderingChanged(Map map, bool rendering)
    {
        if (broken)
        {
            return;
        }

        try
        {
            if (!rendering)
            {
                if (pinnedMap == map)
                {
                    Release();
                }

                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap != null && aBBandMap.Banded)
            {
                int band = TargetBand(map, aBBandMap);
                if (aBBandMap.BandExists(band))
                {
                    pinnedBand = band;
                    pinnedMarkers = null;
                    pinnedAtMs = Environment.TickCount;
                    pinnedMap = map;
                    Patch_CameraDriver_ABClipViewToBand.Invalidate();
                    ABLog.Dev("Progress Renderer: capture pinned to band " + band + " of " + aBBandMap.bandCount + " on " + map?.ToString() + ".");
                }
            }
        }
        catch (Exception e)
        {
            Fail("arming a capture pin", e);
        }
    }

    private static void Release()
    {
        pinnedMap = null;
        pinnedMarkers = null;
        Patch_CameraDriver_ABClipViewToBand.Invalidate();
    }

    private static void Fail(string context, Exception e)
    {
        broken = true;
        Release();
        Log.ErrorOnce("[As above, So below] Progress Renderer compat failed while " + context + "; the bridge is off for this session and captures will show the whole stack: " + e, 762195951);
    }

    private static int TargetBand(Map map, ABBandMap bands)
    {
        return (ABMod.Settings?.progressRendererLevel ?? LevelTopmost) switch
        {
            LevelSurface => bands.surfaceBand,
            LevelViewed => ABBandView.CurrentBand(map),
            _ => bands.bandCount - 1,
        };
    }

    internal static bool TryPinnedBand(Map map, out int band)
    {
        band = 0;
        if (pinnedMap == null || pinnedMap != map)
        {
            return false;
        }

        if (Environment.TickCount - pinnedAtMs > PinWatchdogMs)
        {
            if (UnityData.IsInMainThread)
            {
                ABLog.Dev("Progress Renderer: capture pin expired without a release; the view is going back to the player's own level.");
            }

            Release();
            return false;
        }

        band = pinnedBand;
        return true;
    }

    internal static void SubstituteCornerMarkers(DesignationManager manager, ref List<Designation> result)
    {
        if (broken || pinnedMap == null || manager == null || result == null)
        {
            return;
        }

        try
        {
            if (manager.map != pinnedMap || !TryPinnedBand(manager.map, out var band))
            {
                return;
            }

            DesignationDef designationDef = CornerMarkerDef();
            if (designationDef == null)
            {
                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(manager.map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return;
            }

            CellRect rect = aBBandMap.RectOfBand(band);
            List<Designation> list = new List<Designation>(result.Count + 2);
            int num = 0;
            for (int i = 0; i < result.Count; i++)
            {
                Designation designation = result[i];
                if (designation != null)
                {
                    if (designation.def != designationDef)
                    {
                        list.Add(designation);
                    }
                    else if (rect.Contains(designation.target.Cell))
                    {
                        list.Add(designation);
                        num++;
                    }
                }
            }

            if (num < 2)
            {
                foreach (Designation item in BandCorners(manager, rect, designationDef))
                {
                    list.Add(item);
                }
            }

            result = list;
        }
        catch (Exception e)
        {
            Fail("choosing the captured area", e);
        }
    }

    private static List<Designation> BandCorners(DesignationManager manager, CellRect rect, DesignationDef marker)
    {
        if (pinnedMarkers != null)
        {
            return pinnedMarkers;
        }

        pinnedMarkers = new List<Designation>(2)
        {
            Corner(manager, marker, new IntVec3(rect.minX, 0, rect.minZ)),
            Corner(manager, marker, new IntVec3(rect.maxX, 0, rect.maxZ))
        };
        return pinnedMarkers;
    }

    private static Designation Corner(DesignationManager manager, DesignationDef marker, IntVec3 cell)
    {
        return new Designation(cell, marker)
        {
            designationManager = manager
        };
    }

    private static DesignationDef CornerMarkerDef()
    {
        if (!cornerMarkerResolved)
        {
            cornerMarkerResolved = true;
            cornerMarker = DefDatabase<DesignationDef>.GetNamedSilentFail("CornerMarker");
            if (cornerMarker == null)
            {
                ABLog.Dev("Progress Renderer compat: the CornerMarker designation is missing, so the captured area cannot be narrowed.");
            }
        }

        return cornerMarker;
    }

    internal static void ClearFullMapRequest(Map map, ref bool forceRenderFullMap)
    {
        if (broken || !forceRenderFullMap || map == null)
        {
            return;
        }

        try
        {
            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap != null && aBBandMap.Banded)
            {
                forceRenderFullMap = false;
                ABLog.Dev("Progress Renderer: full-map capture downgraded to a single level (the whole stack is past Unity's texture limit).");
            }
        }
        catch (Exception e)
        {
            Fail("guarding a full-map capture", e);
        }
    }

    internal static string DescribeTarget()
    {
        Map map = ((Current.ProgramState == ProgramState.Playing) ? Find.CurrentMap : null);
        ABBandMap aBBandMap = ((map != null) ? ABBands.CompOf(map) : null);
        if (aBBandMap == null || !aBBandMap.Banded)
        {
            return null;
        }

        int num = TargetBand(map, aBBandMap) - aBBandMap.surfaceBand;
        return (num == 0) ? "AB_PRLevelNowSurface".Translate().ToString() : "AB_PRLevelNow".Translate((num > 0) ? ("+" + num) : num.ToString()).ToString();
    }
}
