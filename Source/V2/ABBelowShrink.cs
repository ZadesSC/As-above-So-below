using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// Makes the depth falloff stick on LIVE PAWNS drawn by the see-below pass, and applies
    /// the §73 transit clip pose (tread steps, ladder pulls, freight-lift ride).
    ///
    /// Everything else below the viewing band is printed into the map mesh, so
    /// SectionLayer_ABBelowV2 can shrink it by editing the vertices it just emitted. Pawns
    /// are not in that mesh - they are redrawn every frame - and DynamicDrawPhaseAt takes a
    /// POSITION and nothing else, so there is no per-call size to pass in.
    ///
    /// PawnRenderer does funnel every piece of a pawn (body, head, apparel, carried thing,
    /// held weapon) through one transform: PawnDrawParms.matrix. Right-multiplying a TRS
    /// there moves the whole pawn about its own draw root in one place, which is why this
    /// is two tiny patches rather than a per-piece hunt. Ported from V1, where it was found
    /// the hard way.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Patch_PawnRenderer_ABBelowShrink
    {
        private static void Postfix(ref PawnDrawParms __result)
        {
            try
            {
                if (__result.Portrait || __result.Cache)
                {
                    return;
                }

                float belowDrawScale = ABBelowDynamicDraw.BelowDrawScale;
                if (belowDrawScale <= 0f)
                {
                    return;
                }

                if (!ABStairAnim.TryGetPose(__result.pawn, out var pose))
                {
                    if (!(belowDrawScale > 0.999f))
                    {
                        __result.matrix *= Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(belowDrawScale, 1f, belowDrawScale));
                    }

                    return;
                }

                ABStairAnim.NoteAnimApplied();
                if (pose.facing != -1)
                {
                    __result.facing = new Rot4(pose.facing);
                }

                __result.matrix *= Matrix4x4.TRS(new Vector3(pose.offX, 0f, pose.offZ), Quaternion.Euler(0f, pose.rot, 0f), new Vector3(pose.sx * belowDrawScale, 1f, pose.sz * belowDrawScale));
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[As above, So below] V2: pawn pose/shrink failed: " + ex.Message, 762195891);
            }
        }
    }

    /// <summary>
    /// ⚠ THE PART THAT LOOKED LIKE THE SHRINK "RANDOMLY STOPS WORKING".
    ///
    /// Beyond ZoomRootSize 18 a humanlike pawn renders from a cached atlas blit:
    /// ParallelGetPreRenderResults sets `useCached` and the draw positions a premade mesh
    /// directly at bodyPos, never reading PawnDrawParms.matrix at all. So the transform
    /// above silently evaporated at exactly the zoom levels a player uses to look down a
    /// column - colonists two levels down shrank while zoomed in and snapped back to full
    /// size when zoomed out, which reads as a flicker bug rather than a missing patch.
    ///
    /// ⚠ THE BLANKET FIX WAS REPLACED, DO NOT PUT IT BACK. This prefix used to force
    /// `disableCache` for EVERY below pawn, which is the most expensive render path in the
    /// game applied to the pawns this mod already draws the most awkwardly (three phases,
    /// serially, on the main thread). Patch_GenDraw_ABBlitScale now scales the blit itself,
    /// so the cache and the depth cue finally coexist, and ABBelowRenderCache decides per
    /// pawn - vetoing only the cases the blit genuinely cannot express (gear, transit
    /// clips, unaffordable first-touch bakes) and only when it would actually be visible.
    /// It arms SuppressCache for exactly those.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class Patch_PawnRenderer_ABBelowDisableCache
    {
        private static void Prefix(PawnRenderer __instance, ref bool disableCache)
        {
            if (ABBelowRenderCache.SuppressCache)
            {
                disableCache = true;
                return;
            }
            // ⚠ THE TRANSIT CLIPS FALL INTO THE SAME TRAP AND FOR THE SAME REASON. A pawn
            // animating on the band you are looking at is NOT in the see-below pass, so the
            // check above misses it - and past ZoomRootSize 18 the cached atlas blit ignores
            // PawnDrawParms.matrix outright, so the clip (and the hide-at-landing dot, which
            // is nothing but a matrix scale) would evaporate at exactly the zoom a player
            // uses to watch someone take the stairs.
            try
            {
                if (ABStairAnim.IsAnimating(PawnRef(__instance)))
                {
                    disableCache = true;
                    ABStairAnim.NoteCacheVeto();
                }
            }
            catch
            {
                // Never break rendering over a cosmetic effect.
            }
        }

        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnRef =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
    }
}
