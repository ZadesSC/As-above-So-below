using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// §95 TIER A - SKY-SPACE FX MODS, GATED AND REMAPPED TO THE VIEWED LEVEL.
    ///
    /// Two mods draw "the sky" over a map that is secretly seven maps tall:
    ///
    /// 1. SUN &amp; MOON BEAMS (yuexiatingsong.sunandmoonbeams). Effecter god-rays. THREE
    ///    defects on a banded map, each with its own patch:
    ///    (a) The anchor Thing (`SmallPitLight_Sun`) spawns at MAP CENTER with a Standable
    ///        check and 10 retries. The center of a stack is some band's rock (or the
    ///        gutter), so on most banded maps the mod silently dies at boot.
    ///        → PlaceAnchorOnSurface re-homes it to a standable surface-band cell.
    ///    (b) Beams scatter over 1.5x the WHOLE map (their coverageMode 1), i.e. across
    ///        every band; only thick roof is rejected, so thin-roofed underground carves
    ///        get god-rays. → RemapBeam folds every scatter position into the VIEWED
    ///        band's rect (the exact dz idiom the fleck mirror uses) and re-runs the
    ///        thick-roof verdict on the folded cell, because the fold happens AFTER their
    ///        own FindValidSpawnPosition roof test already ran on the raw position.
    ///    (c) Underground there is no sky at all. → GateSubEffectTick skips the whole
    ///        effecter tick when the view level is below the surface - same verdict, same
    ///        shape, as Patch_WeatherManager_ABBandOverlays' clause (a).
    ///
    /// 2. [LBY]云 (Araneid.cloud, CloudSkyOverlay.dll). One draw seam,
    ///    CloudSkyRenderer.DrawNow → one underground gate. (The user's ICW suppresses
    ///    DrawNow itself when active - explicitly OUT OF SCOPE per user, w17 - so this
    ///    entry only matters for LBY running standalone, and coexists with ICW's prefix.)
    ///
    /// House rules honored: foreign-type-free (TypeByName + reflection only), absent mod =
    /// silent skip, present-but-changed mod = WarningOnce and fail open, every prefix
    /// wrapped so a throw returns control to the original (ABGuard discipline).
    /// Sky semantics per user ruling (w17): beams/clouds show on ALL non-underground
    /// views - surface AND sky bands; only level &lt; 0 suppresses.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SkyFXCompat
    {
        private static FieldInfo fSunLightThing;
        private static FieldInfo fInitialized;
        private static FieldInfo fCreationAttempted;
        private static FieldInfo fCreationAttemptCount;

        static SkyFXCompat()
        {
            InstallSunAndMoonBeams();
            InstallLbyClouds();
        }

        // ------------------------------------------------------------------ Sun & Moon

        private static void InstallSunAndMoonBeams()
        {
            Type type = AccessTools.TypeByName("SunAndMoonBeams.SubEffecter_SunLightBeam");
            if (type == null)
            {
                return;
            }

            try
            {
                MethodInfo methodInfo = AccessTools.Method(type, "SubEffectTick");
                MethodInfo methodInfo2 = AccessTools.Method(type, "CreateSingleBeam");
                if (methodInfo == null || methodInfo2 == null)
                {
                    Log.WarningOnce("[As above, So below] Sun & Moon Beams is present but its effecter changed shape; sunbeams will ignore levels this session.", 2822352);
                    return;
                }

                HarmonyBoot.Harmony.Patch(methodInfo, new HarmonyMethod(typeof(SkyFXCompat), "GateSubEffectTick"));
                HarmonyBoot.Harmony.Patch(methodInfo2, new HarmonyMethod(typeof(SkyFXCompat), "RemapBeam"));
                MethodInfo methodInfo3 = AccessTools.Method(type, "SetMap");
                fCoverageMode = AccessTools.Field(type, "coverageMode");
                fCoverageHeight = AccessTools.Field(type, "coverageHeight");
                fDynamicBurstCount = AccessTools.Field(type, "dynamicBurstCount");
                if (methodInfo3 != null && fCoverageMode != null && fCoverageHeight != null && fDynamicBurstCount != null)
                {
                    HarmonyBoot.Harmony.Patch(methodInfo3, null, new HarmonyMethod(typeof(SkyFXCompat), "RescaleToBand"));
                }
                else
                {
                    Log.WarningOnce("[As above, So below] Sun & Moon Beams' beam coverage changed shape; beams keep whole-stack density and may look too dense on a banded map.", 2822361);
                }

                Type type2 = AccessTools.TypeByName("SunAndMoonBeams.SunLightManager");
                MethodInfo methodInfo4 = ((type2 == null) ? null : AccessTools.Method(type2, "CreateSunLight"));
                fSunLightThing = ((type2 == null) ? null : AccessTools.Field(type2, "sunLightThing"));
                fInitialized = ((type2 == null) ? null : AccessTools.Field(type2, "initialized"));
                fCreationAttempted = ((type2 == null) ? null : AccessTools.Field(type2, "creationAttempted"));
                fCreationAttemptCount = ((type2 == null) ? null : AccessTools.Field(type2, "creationAttemptCount"));
                if (methodInfo4 != null && fSunLightThing != null && fInitialized != null && fCreationAttempted != null && fCreationAttemptCount != null)
                {
                    HarmonyBoot.Harmony.Patch(methodInfo4, new HarmonyMethod(typeof(SkyFXCompat), "PlaceAnchorOnSurface"));
                }
                else
                {
                    Log.WarningOnce("[As above, So below] Sun & Moon Beams' manager changed shape; its sun anchor keeps vanilla placement (may fail to spawn on banded maps, leaving no beams).", 2822353);
                }

                ABLog.Dev("Sun & Moon Beams bridge installed (gate + remap + anchor).");
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[As above, So below] Sun & Moon Beams bridge failed to install: " + ex.Message, 2822354);
            }
        }

        /// <summary>Clause (c): no sky underground. Runs per effecter tick - the guard
        /// chain is bool + memoized comp + int compare before any work.</summary>
        public static bool GateSubEffectTick()
        {
            try
            {
                if (!ABGuard.On(ABGuard.Weather))
                {
                    return true;
                }
                Map map = Find.CurrentMap;
                ABBandMap bands = ABBands.CompOf(map);
                if (map == null || bands == null || !bands.Banded)
                {
                    return true;
                }
                return ABBandView.CurrentLevel(map) >= 0;
            }
            catch (Exception e)
            {
                Log.ErrorOnce(ABLog.Tag + " sunbeam gate threw: " + e, 0x2B10D3);
                return true;
            }
        }

        /// <summary>Clause (b): fold the scatter position into the viewed band. Their
        /// looping beams store the position we hand back, so loops inherit the fold; a
        /// view-band switch mid-loop is re-folded on the next loop spawn. When we move a
        /// position we re-run the thick-roof verdict ourselves (their test already ran on
        /// the RAW position and its answer no longer applies).</summary>
        public static bool RemapBeam(ref Vector3 position, Map map)
        {
            try
            {
                if (!ABGuard.On(ABGuard.Weather))
                {
                    return true;
                }

                ABBandMap aBBandMap = ABBands.CompOf(map);
                if (map == null || aBBandMap == null || !aBBandMap.Banded)
                {
                    return true;
                }

                if (ABBandView.CurrentLevel(map) < 0)
                {
                    return false;
                }

                IntVec3 c = position.ToIntVec3();
                if (!c.InBounds(map) || aBBandMap.InGutter(c))
                {
                    return false;
                }

                int num = ABBandView.CurrentBand(map);
                int num2 = aBBandMap.BandOf(c);
                if (num2 == num)
                {
                    return true;
                }

                int num3 = (num - num2) * aBBandMap.Slot;
                IntVec3 c2 = new IntVec3(c.x, 0, c.z + num3);
                if (!c2.InBounds(map) || aBBandMap.InGutter(c2))
                {
                    return false;
                }

                RoofDef roofDef = map.roofGrid.RoofAt(c2);
                if (roofDef != null && roofDef.isThickRoof)
                {
                    return false;
                }

                position = new Vector3(position.x, position.y, position.z + (float)num3);
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[As above, So below] sunbeam remap threw: " + ex, 2822356);
                return true;
            }
        }

        /// <summary>Clause (a): home the anchor on the surface band. Full reimplementation
        /// of a 20-line foreign private method, accepted because the alternative is the mod
        /// silently dying at its 10-retry limit; every failure path returns true so THEIR
        /// code (and their give-up counter) stays the authority when we cannot help.</summary>
        public static bool PlaceAnchorOnSurface(MapComponent __instance)
        {
            try
            {
                Map map = __instance?.map;
                ABBandMap bands = ABBands.CompOf(map);
                if (map == null || bands == null || !bands.Banded)
                {
                    return true;
                }
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("SmallPitLight_Sun");
                if (def == null)
                {
                    return true; // their error message names the missing def
                }
                CellRect r = bands.RectOfBand(bands.surfaceBand);
                IntVec3 cell = r.CenterCell;
                Predicate<IntVec3> ok = c => c.InBounds(map) && c.Standable(map)
                    && !bands.InGutter(c) && bands.BandOf(c) == bands.surfaceBand;
                if (!ok(cell) && !CellFinder.TryFindRandomCellNear(cell, map, 40, ok, out cell))
                {
                    // Let their center-of-map attempt run (it will likely fail too, but
                    // its give-up counter is the designed off switch).
                    return true;
                }
                Thing old = fSunLightThing.GetValue(__instance) as Thing;
                if (old != null && !old.Destroyed)
                {
                    old.Destroy();
                }
                Thing t = ThingMaker.MakeThing(def);
                GenSpawn.Spawn(t, cell, map);
                fSunLightThing.SetValue(__instance, t);
                fInitialized.SetValue(__instance, true);
                fCreationAttempted.SetValue(__instance, true);
                fCreationAttemptCount.SetValue(__instance, 0);
                ABLog.Dev("sun anchor homed at " + cell + " (surface band).");
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce(ABLog.Tag + " sun anchor placement threw; vanilla placement"
                    + " runs instead. " + e.Message, 0x2B10D5);
                return true;
            }
        }

        // ------------------------------------------------------------------ [LBY]云

        private static void InstallLbyClouds()
        {
            Type rendererT = AccessTools.TypeByName("CloudSkyOverlay.CloudSkyRenderer");
            if (rendererT == null)
            {
                return;
            }
            try
            {
                MethodInfo draw = AccessTools.Method(rendererT, "DrawNow");
                if (draw == null)
                {
                    Log.WarningOnce(ABLog.Tag + " [LBY]云 is present but CloudSkyRenderer"
                        + ".DrawNow was not found; clouds will draw underground.", 0x2B10D6);
                    return;
                }
                HarmonyBoot.Harmony.Patch(draw, prefix: new HarmonyMethod(
                    typeof(SkyFXCompat), nameof(GateCloudDraw)));
                ABLog.Dev("[LBY]云 underground gate installed.");
            }
            catch (Exception e)
            {
                Log.WarningOnce(ABLog.Tag + " [LBY]云 gate failed to install: " + e.Message,
                    0x2B10D7);
            }
        }

        /// <summary>Same verdict as the weather-overlay gate: underground = no sky.</summary>
        public static bool GateCloudDraw()
        {
            try
            {
                if (!ABGuard.On(ABGuard.Weather))
                {
                    return true;
                }
                Map map = Find.CurrentMap;
                ABBandMap bands = ABBands.CompOf(map);
                if (map == null || bands == null || !bands.Banded)
                {
                    return true;
                }
                return ABBandView.CurrentLevel(map) >= 0;
            }
            catch (Exception e)
            {
                Log.ErrorOnce(ABLog.Tag + " cloud gate threw: " + e, 0x2B10D8);
                return true;
            }
        }
        private static float AreaFactor(float area)
        {
            float num = area / 90000f;
            return (num > 1f) ? Mathf.Sqrt(num) : num;
        }

        public static void RescaleToBand(object __instance, Map map)
        {
            try
            {
                if (!ABGuard.On(ABGuard.Weather) || __instance == null || map == null)
                {
                    return;
                }

                ABBandMap aBBandMap = ABBands.CompOf(map);
                if (aBBandMap != null && aBBandMap.Banded && aBBandMap.bandHeight > 0 && fCoverageMode.GetValue(__instance)is int num && num == 1)
                {
                    float num2 = aBBandMap.bandHeight;
                    fCoverageHeight.SetValue(__instance, num2 * 1.5f);
                    float num3 = (float)map.Size.x * (float)map.Size.z;
                    float num4 = (float)map.Size.x * num2;
                    if (num3 > 0f && num4 > 0f && fDynamicBurstCount.GetValue(__instance)is int num5)
                    {
                        float num6 = AreaFactor(num4) / AreaFactor(num3);
                        fDynamicBurstCount.SetValue(__instance, Mathf.Max(1, Mathf.RoundToInt((float)num5 * num6)));
                    }

                    ABLog.Dev("sunbeam coverage rescaled to band " + map.Size.x + "x" + (int)num2 + " (was stack-tall).");
                }
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[As above, So below] sunbeam band rescale threw; beams keep whole-stack density. " + ex.Message, 2822362);
            }
        }

        private static FieldInfo fDynamicBurstCount;

        private static FieldInfo fCoverageHeight;

        private static FieldInfo fCoverageMode;

    }
}
