using System.Text;
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 see-below, dynamic half: pawns.
    ///
    /// Items, buildings, plants and terrain all live in the map mesh, so
    /// SectionLayer_ABBelowV2 already carries them. PAWNS do not - they are
    /// realtime-drawn every frame by DynamicDrawManager, which culls to the camera's
    /// view rect. Since the camera is clamped to the current band, a pawn one band down
    /// is simply off-screen and never drawn at all.
    ///
    /// This is the single place where V2's "same map" property pays off most bluntly.
    /// V1 could not do this without lying about positions - hence DrawPosOffsetPatcher,
    /// hundreds of DrawPos getters patched on ParallelPreDraw worker threads. Here the
    /// pawn is already on this map; it just needs drawing somewhere else. Thing exposes
    /// exactly that: DrawNowAt(loc). No patching of any getter, no position mutation, no
    /// worker-thread hazard - one call with an offset vector.
    ///
    /// Masking matches the mesh layer: a pawn shows only through a cell that is open air
    /// on this band and unfogged below, so roofs and mountain caps stay opaque.
    /// </summary>
    public static class ABBelowDynamicDraw
    {
        /// <summary>Drawn per frame, so keep the scan tight: only pawns whose cell is
        /// inside the translated view rect are considered.</summary>
        /// <summary>Set by the "AB2: below pawn report" dev action for ONE pass, so the
        /// per-pawn verdict is captured without spamming every frame.</summary>
        public static bool ReportNextPass;

        /// <summary>Band offset armed ONLY around a single below-pawn draw call, read by
        /// Patch_PawnRenderer_ABBelowBodyPos. Non-zero exclusively inside that call.</summary>
        public static float BelowDrawOffsetZ;

        /// <summary>Depth-falloff scale armed around a single below-pawn draw call, read by
        /// Patch_PawnRenderer_ABBelowShrink. Exactly the same arm/disarm discipline as
        /// BelowDrawOffsetZ: 1 outside the pass, so nothing else in the game can observe it.
        /// Kept separate from the offset because a pawn is always translated but is only
        /// shrunk when the setting is on, and the two must disarm independently.</summary>
        public static float BelowDrawScale = 1f;

        /// <summary>Armed ONLY around the three draw phases of ONE below realtime THING
        /// (never pawns - the atlas-bake trap, see ABBelowMeshTranslate's banner). Read by
        /// ABBelowMeshTranslate's Graphics.Internal_DrawMesh prefix, which translates every
        /// position-blind map-space draw - campfire flames, windmill blades, hoses, domes,
        /// bars, lines - into the view band. Zero outside the window, same arm/disarm
        /// discipline as BelowDrawOffsetZ next door (§95 Tiers E/E2).</summary>
        public static float RealtimeDropZ;

        /// <summary>The raw source-band z of the thing currently being drawn, for the
        /// discrimination test in the mesh-translate seam: a matrix still NEAR this z is a
        /// legacy draw reading parent.DrawPos; one already a Slot away is the thing's own
        /// graphic built from our translated loc. Bands are >= a Slot apart, so the 8-cell
        /// tolerance can never confuse the two.</summary>
        public static float RealtimeRawZ;

        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();

        public static void DrawBelowPawns(Map map)
        {
            if (map == null || !ABGuard.On(ABGuard.Rendering))
            {
                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return;
            }

            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null || !aBBandMap.BandExists(ABBandView.RenderBand(map) - 1))
            {
                return;
            }

            int slot = aBBandMap.Slot;
            int num = ABBandView.RenderBand(map);
            if (!aBBandMap.AnyUnfoggedBelow(num))
            {
                return;
            }

            long num2 = ABPerfStats.Now();
            int num3 = 0;
            int num4 = 0;
            ABBelowRenderCache.BeginFrame();
            CellRect currentViewRect = cameraDriver.CurrentViewRect;
            TerrainGrid terrainGrid = map.terrainGrid;
            FogGrid fogGrid = map.fogGrid;
            IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                Pawn pawn = allPawnsSpawned[i];
                if (pawn == null || !pawn.Spawned)
                {
                    continue;
                }

                IntVec3 position = pawn.Position;
                bool reportNextPass = ReportNextPass;
                if (ABStairAnim.IsGhosting(pawn))
                {
                    if (reportNextPass)
                    {
                        StringBuilder stringBuilder = report;
                        string[] array = new string[5]
                        {
                            "  SKIP ",
                            pawn.LabelShortCap,
                            " ",
                            null,
                            null
                        };
                        IntVec3 intVec = position;
                        array[3] = intVec.ToString();
                        array[4] = " - transit ghost (drawn by the clip pass)";
                        stringBuilder.AppendLine(string.Concat(array));
                    }

                    continue;
                }

                int num5 = aBBandMap.BandOf(position);
                if (num5 < 0 || num5 >= num)
                {
                    continue;
                }

                num3++;
                IntVec3 intVec2 = aBBandMap.Translate(position, num);
                if (!currentViewRect.Contains(intVec2))
                {
                    if (reportNextPass)
                    {
                        StringBuilder stringBuilder2 = report;
                        string[] array2 = new string[6]
                        {
                            "  SKIP ",
                            pawn.LabelShortCap,
                            " ",
                            null,
                            null,
                            null
                        };
                        IntVec3 intVec = position;
                        array2[3] = intVec.ToString();
                        array2[4] = " - column outside the view rect ";
                        CellRect cellRect = currentViewRect;
                        array2[5] = cellRect.ToString();
                        stringBuilder2.AppendLine(string.Concat(array2));
                    }

                    continue;
                }

                if (fogGrid.IsFogged(position))
                {
                    if (reportNextPass)
                    {
                        StringBuilder stringBuilder3 = report;
                        string[] array3 = new string[5]
                        {
                            "  SKIP ",
                            pawn.LabelShortCap,
                            " ",
                            null,
                            null
                        };
                        IntVec3 intVec = position;
                        array3[3] = intVec.ToString();
                        array3[4] = " - fogged";
                        stringBuilder3.AppendLine(string.Concat(array3));
                    }

                    continue;
                }

                if (!intVec2.InBounds(map) || aBBandMap.InGutter(intVec2))
                {
                    if (reportNextPass)
                    {
                        StringBuilder stringBuilder4 = report;
                        string[] array4 = new string[5]
                        {
                            "  SKIP ",
                            pawn.LabelShortCap,
                            " ",
                            null,
                            null
                        };
                        IntVec3 intVec = position;
                        array4[3] = intVec.ToString();
                        array4[4] = " - cell above out of bounds / in gutter";
                        stringBuilder4.AppendLine(string.Concat(array4));
                    }

                    continue;
                }

                if (!ABBands.TryResolveVisibleBelow(map, aBBandMap, intVec2, out var below, out var drop) || below.x != position.x || below.z != position.z)
                {
                    if (reportNextPass)
                    {
                        StringBuilder stringBuilder5 = report;
                        string[] array5 = new string[7]
                        {
                            "  SKIP ",
                            pawn.LabelShortCap,
                            " ",
                            null,
                            null,
                            null,
                            null
                        };
                        IntVec3 intVec = position;
                        array5[3] = intVec.ToString();
                        array5[4] = " - not what this column shows (covered by ";
                        array5[5] = terrainGrid.TerrainAt(intVec2).defName;
                        array5[6] = ")";
                        stringBuilder5.AppendLine(string.Concat(array5));
                    }

                    continue;
                }

                if (reportNextPass)
                {
                    StringBuilder stringBuilder6 = report;
                    string[] array6 = new string[12]
                    {
                        "  DRAW ",
                        pawn.LabelShortCap,
                        " ",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null
                    };
                    IntVec3 intVec = position;
                    array6[3] = intVec.ToString();
                    array6[4] = " posture=";
                    array6[5] = pawn.GetPosture().ToString();
                    array6[6] = " inBed=";
                    array6[7] = (pawn.CurrentBed() != null).ToString();
                    array6[8] = " drawPos.y=";
                    array6[9] = pawn.DrawPos.y.ToString("0.000");
                    array6[10] = " job=";
                    array6[11] = pawn.CurJob?.def?.defName ?? "none";
                    stringBuilder6.AppendLine(string.Concat(array6));
                }

                try
                {
                    Vector3 drawPos = pawn.DrawPos;
                    drawPos.z += drop;
                    float num6 = ABDepthView.ScaleForLevels((slot <= 0) ? 1 : (drop / slot));
                    BelowDrawOffsetZ = drop;
                    BelowDrawScale = num6;
                    ABBelowRenderCache.BeginPawn(pawn, num6);
                    try
                    {
                        pawn.DynamicDrawPhaseAt(DrawPhase.EnsureInitialized, drawPos);
                        pawn.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw, drawPos);
                        pawn.DynamicDrawPhaseAt(DrawPhase.Draw, drawPos);
                    }
                    finally
                    {
                        BelowDrawOffsetZ = 0f;
                        BelowDrawScale = 1f;
                        ABBelowRenderCache.EndPawn();
                    }

                    num4++;
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[As above, So below] V2 below pawn draw failed for " + pawn.LabelShortCap + ": " + ex.Message, pawn.thingIDNumber ^ 0x2D6E2FA0);
                }
            }

            int realtime = DrawBelowRealtimeThings(map, aBBandMap, currentViewRect, num, fogGrid);
            ABPerfStats.NoteBelowPass(num3, num4, realtime, ABPerfStats.Now() - num2);
            if (ReportNextPass)
            {
                ReportNextPass = false;
                Log.Warning("[As above, So below] V2 below pawn report (view band " + ABBandView.CurrentBand(map) + ", slot " + slot + ", " + allPawnsSpawned.Count + " pawns on map):\n" + ((report.Length == 0) ? "  (no pawns considered)" : report.ToString()));
                report.Length = 0;
            }
        }

        /// <summary>
        /// Non-pawn REALTIME things: construction frames above all.
        ///
        /// SectionLayer_ABBelowV2 carries the level below by printing things into the map
        /// mesh, and it deliberately skips anything realtime:
        ///     if (drawer != MapMeshOnly &amp;&amp; drawer != MapMeshAndRealTime) continue;
        /// because realtime things simply are not in that mesh. Until now the only realtime
        /// things drawn from above were pawns, so everything else in that category was
        /// invisible from the level above:
        ///   * FRAMES are DrawerType.RealtimeOnly (ThingDefGenerator_Buildings.BaseFrameDef),
        ///     so anything under construction vanished when viewed from the sky.
        ///   * DOOR blueprints are forced RealtimeOnly too, so those disappeared while every
        ///     other blueprint (which inherits its target's drawer type) showed fine - which
        ///     is what made the bug look arbitrary.
        ///   * Projectiles are in this category as well, so cross-band shots were never
        ///     drawn either. Same root cause, closed by the same loop.
        ///
        /// Source is DynamicDrawManager.DrawThings - vanilla's own registry of realtime
        /// drawables. That is exactly the complement of what the mesh layer carries, so the
        /// two passes tile the whole map with no overlap and no gaps. Sweeping cells with
        /// ThingsListAtFast instead would repeat the OverlayDrawer mistake that cost
        /// 1.4 ms/frame.
        ///
        /// ⚠ MULTI-BAND SINCE WINDOW 4d. This pass was the documented last single-band
        /// corner of the below view (a strip exactly one Slot down), so fire, motes and
        /// construction frames two or more levels down were invisible while the pawns and
        /// projectiles beside them drew. It now resolves each thing's column with the SAME
        /// shared descent rule as the pawn loop above - the two loops finally agree, and the
        /// "one `- Slot` step" class of bug loses its last host in this file.
        /// </summary>
        private static int DrawBelowRealtimeThings(Map map, ABBandMap bands, CellRect camView,
            int viewBand, FogGrid fog)
        {
            IReadOnlyList<Thing> things = map.dynamicDrawManager?.DrawThings;
            if (things == null)
            {
                return 0;
            }
            int drawn = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || !t.Spawned || t is Pawn)
                {
                    continue; // pawns already handled above, with their bed-pose special case
                }
                // ⚠ CROSS-BAND PROJECTILES HAVE EXACTLY ONE OWNER, AND IT IS NOT THIS PASS.
                // ABCombatRelay draws every registered cross-band round depth-correctly; this
                // pass would draw a second copy on top. No isinst gate any more: Combat
                // Extended's rounds are ThingWithComps, not Verse.Projectile, so a type test
                // here would re-open the double-draw exactly for CE. Handles() fast-exits on
                // an empty registry, which is the common case.
                if (ABCombatRelay.Handles(t))
                {
                    continue;
                }
                IntVec3 pos = t.Position;
                int band = bands.BandOf(pos);
                if (band >= viewBand)
                {
                    continue; // same band or above: vanilla's job (or nothing's)
                }
                IntVec3 above = bands.Translate(pos, viewBand);
                if (!camView.Contains(above))
                {
                    continue; // the cheap screen gate, before any terrain is touched
                }
                if (fog.IsFogged(pos) || bands.InGutter(above))
                {
                    continue;
                }
                // The SAME shared descent rule as pawns: is this thing's cell what the
                // column actually shows from the view band? Multi-level for free, and roofs
                // and caps refuse exactly like the mesh layer.
                if (!ABBands.TryResolveVisibleBelow(map, bands, above, out IntVec3 seen,
                        out int dropRt)
                    || seen.x != pos.x || seen.z != pos.z)
                {
                    continue;
                }
                try
                {
                    Vector3 loc = t.DrawPos;
                    loc.z += dropRt;
                    // ⚠ NO SHRINK ON THIS PATH. A realtime thing is drawn by its own Graphic
                    // through DynamicDrawPhaseAt, which takes a position and nothing else -
                    // unlike a pawn, whose renderer funnels every draw through
                    // PawnDrawParms.matrix (that single funnel is the whole reason the pawn
                    // shrink is two small patches instead of a per-piece hunt). Construction
                    // frames and projectiles one level down therefore keep full size while
                    // the pawns beside them shrink. Accepted for now: this pass is already
                    // documented as the last single-band corner of the below view (§5), and
                    // both gaps want the same fix.
                    // All three phases, unconditionally. A phase-skipping optimisation was
                    // tried here and REVERTED: it caused a visible regression and was never
                    // worth it - it removed two virtual calls that immediately return and
                    // replaced them with a dictionary lookup, so the expected saving was
                    // approximately nothing. Below things are culled from the camera's view
                    // rect, so vanilla never gives them EnsureInitialized or ParallelPreDraw
                    // while their cached render results stay flagged valid; skipping either
                    // phase is how a below thing ends up drawn stale. Do not re-attempt
                    // without a measured reason.
                    // §95 Tiers E/E2: legacy comps and buildings draw overlays at
                    // parent.DrawPos, ignoring our loc - flames, blades, hoses, bars all
                    // landed on the source band, off-camera. Arm the translation window
                    // for the mesh-translate seam; cleared in a finally so a throw cannot
                    // leave every later DrawMesh on the map translated a band down.
                    RealtimeDropZ = dropRt;
                    RealtimeRawZ = t.DrawPos.z;
                    try
                    {
                        t.DynamicDrawPhaseAt(DrawPhase.EnsureInitialized, loc);
                        t.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw, loc);
                        t.DynamicDrawPhaseAt(DrawPhase.Draw, loc);
                    }
                    finally
                    {
                        RealtimeDropZ = 0f;
                        RealtimeRawZ = 0f;
                    }
                    drawn++;
                }
                catch (Exception e)
                {
                    Log.WarningOnce(ABLog.Tag + " V2 below realtime draw failed for "
                        + t.LabelCap + ": " + e.Message, t.thingIDNumber ^ 762195874);
                }
            }
            return drawn;
        }
    }

    /// <summary>
    /// Runs straight after vanilla's own dynamic pass, so below pawns compose on top of
    /// the below mesh but under anything this band draws afterwards.
    /// </summary>
    [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
    public static class Patch_DynamicDrawManager_ABBelowPawns
    {
        private static readonly AccessTools.FieldRef<DynamicDrawManager, Map> MapRef =
            AccessTools.FieldRefAccess<DynamicDrawManager, Map>("map");

        private static void Postfix(DynamicDrawManager __instance)
        {
            try
            {
                Map map = MapRef(__instance);
                ABBelowDynamicDraw.DrawBelowPawns(map);
                // §73 transit ghosts: entry clips drawn at the origin mouth, after the
                // below pass so they compose above it like any same-band pawn would.
                ABStairAnim.DrawGhosts(map);
            }
            catch (Exception e)
            {
                Log.ErrorOnce(ABLog.Tag + " V2: below pawn pass threw: " + e, 762195873);
            }
        }
    }
}
