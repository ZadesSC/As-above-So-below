using System.Collections.Generic;
using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 lighting for banded maps.
    ///
    /// THE PROBLEM: below content is drawn into the sky band's cells, so vanilla's lighting
    /// overlay shades it with the SKY cell's glow. Through open air you are looking at the
    /// surface, so it must be shaded with the SURFACE cell's glow instead. At night that is
    /// the difference between a lit camp glowing from above and a black rectangle.
    ///
    /// WHY NOT TWO OVERLAYS (the first attempt, which produced VIGNETTING):
    /// vanilla's overlay does not give each cell its own colour - every vertex is the
    /// AVERAGE GLOW OF THE FOUR CELLS TOUCHING IT, and neighbouring cells share vertices.
    /// Baking a "sky" mesh and a "below" mesh with complementary filters therefore cannot
    /// work: each mesh still emits a quad for EVERY cell, so the quad covering a
    /// see-through cell picks up transparent-black corners from its opaque neighbours (and
    /// vice versa). The result is a dark halo around every opening - exactly the vignette.
    ///
    /// THE FIX: ONE overlay, with the glow SOURCE substituted per cell. Vanilla's geometry
    /// is reused via the public Bake(...), then the vertex colours are recomputed with its
    /// own algorithm, except that any cell you can see through resolves its glow, roof and
    /// edifice from the cell one band DOWN. Vertices then average across a coherent set of
    /// values and light falls off between lit and unlit cells exactly as it does anywhere
    /// else on a vanilla map.
    ///
    /// Sections can straddle a band seam (Slot is not a multiple of the 17-cell section
    /// size), which is why the substitution is per CELL rather than per section.
    /// </summary>
    [StaticConstructorOnStartup]
    public class SectionLayer_ABBelowLighting : SectionLayer
    {
        private LayerSubMesh mesh;

        private Vector3 offset;

        private static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        public SectionLayer_ABBelowLighting(Section section) : base(section)
        {
            // AB_BelowThings: the mirrored below-change signal (§36c-B1). The glow, roof
            // and edifice this layer samples live on the RESOLVED (below) band; their
            // dirties arrive only through the mirror now.
            relevantChangeTypes = (ulong)MapMeshFlagDefOf.Roofs
                | (ulong)MapMeshFlagDefOf.GroundGlow
                | (ulong)MapMeshFlagDefOf.Terrain
                | (ulong)ABDefOf.AB_BelowThings;
        }

        public override bool Visible => ABGuard.On(ABGuard.Rendering)
            && ABV2Debug.DrawBelowLighting
            && DebugViewSettings.drawLightingOverlay;

        public override void Regenerate()
        {
            if (!ABGuard.On(ABGuard.Rendering))
            {
                Release();
                return;
            }

            Map map = section.map;
            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                Release();
                return;
            }

            try
            {
                CellRect cellRect = new CellRect(section.botLeft.x, section.botLeft.z, 17, 17);
                cellRect.ClipInsideMap(map);
                if (cellRect.Width <= 0 || cellRect.Height <= 0)
                {
                    Release();
                    return;
                }

                if (mesh == null || mesh.mesh == null)
                {
                    Release();
                    mesh = SectionLayer_LightingOverlay.Bake(map, cellRect, MatBases.LightOverlay, null);
                    offset = new Vector3((float)cellRect.minX + (float)cellRect.Width / 2f, 0f, (float)cellRect.minZ + (float)cellRect.Height / 2f);
                    bakedRect = cellRect;
                    fullTris = mesh.tris.ToArray();
                    voidCut = false;
                }

                if (mesh?.mesh != null)
                {
                    Color32[] array = (ZeroColors ? new Color32[(cellRect.Width + 1) * (cellRect.Height + 1) + cellRect.Width * cellRect.Height] : BuildColors(map, aBBandMap, cellRect));
                    CelestialLightingCompat.ApplyOverlayPasses(map, cellRect, array);
                    mesh.mesh.colors32 = array;
                    ApplyVoidCut(map, aBBandMap, cellRect);
                }
            }
            catch (Exception e)
            {
                Release();
                ABGuard.Disable(ABGuard.Rendering, e, "V2 below lighting");
            }
        }

        /// <summary>
        /// Reimplements SectionLayer_LightingOverlay's colour pass with one change: every
        /// cell lookup goes through SourceIndex, so a see-through cell reports the glow,
        /// roof and edifice of the cell one band below it.
        ///
        /// Vertex layout is vanilla's (MakeBaseGeometry): (W+1)x(H+1) corner vertices in
        /// row-major order, followed by WxH centre vertices.
        /// </summary>
        internal static Color32[] BuildColors(Map map, ABBandMap bands, CellRect rect)
        {
            int width = rect.Width;
            int height = rect.Height;
            int num = (width + 1) * (height + 1);
            Color32[] array = new Color32[num + width * height];
            CellIndices cellIndices = map.cellIndices;
            RoofGrid roofGrid = map.roofGrid;
            GlowGrid glowGrid = map.glowGrid;
            Thing[] innerArray = map.edificeGrid.InnerArray;
            Thing[] array2 = innerArray;
            int x = map.Size.x;
            int z = map.Size.z;
            for (int i = 0; i <= height; i++)
            {
                for (int j = 0; j <= width; j++)
                {
                    int num2 = rect.minX + j;
                    int num3 = rect.minZ + i;
                    ColorInt colorInt = new ColorInt(0, 0, 0, 0);
                    int num4 = 0;
                    bool flag = false;
                    for (int k = 0; k < 4; k++)
                    {
                        int num5 = num2 - ((k % 2 == 0) ? 1 : 0);
                        int num6 = num3 - ((k < 2) ? 1 : 0);
                        if (num5 < 0 || num6 < 0 || num5 >= x || num6 >= z)
                        {
                            continue;
                        }

                        int num7 = SourceIndex(map, bands, cellIndices, cellIndices.CellToIndex(num5, num6));
                        if (num7 >= 0)
                        {
                            Thing thing = array2[num7];
                            RoofDef roofDef = roofGrid.RoofAt(num7);
                            if (!flag && roofDef != null && (roofDef.isThickRoof || thing == null || !thing.def.holdsRoof || thing.def.altitudeLayer == AltitudeLayer.DoorMoveable) && (!roofDef.isThickRoof || thing == null || !thing.def.mineable || !SectionLayer_ABRockRootFill.LitAsOpenEdge(map, bands, num7)))
                            {
                                flag = true;
                            }

                            if (thing == null || !thing.def.blockLight)
                            {
                                colorInt += glowGrid.VisualGlowAt(num7);
                                num4++;
                            }
                        }
                    }

                    Color32 color = ((num4 > 0) ? (colorInt / num4).ProjectToColor32() : Transparent);
                    if (flag && color.a < 100)
                    {
                        color.a = 100;
                    }

                    array[i * (width + 1) + j] = color;
                }
            }

            for (int l = 0; l < height; l++)
            {
                for (int m = 0; m < width; m++)
                {
                    int num8 = l * (width + 1) + m;
                    ColorInt colorInt2 = default;
                    colorInt2 += array[num8];
                    colorInt2 += array[num8 + 1];
                    colorInt2 += array[num8 + width + 1];
                    colorInt2 += array[num8 + width + 2];
                    Color32 color2 = new Color32((byte)(colorInt2.r / 4), (byte)(colorInt2.g / 4), (byte)(colorInt2.b / 4), (byte)(colorInt2.a / 4));
                    int x2 = rect.minX + m;
                    int z2 = rect.minZ + l;
                    int num9 = SourceIndex(map, bands, cellIndices, cellIndices.CellToIndex(x2, z2));
                    if (color2.a < 100 && num9 >= 0 && roofGrid.Roofed(num9))
                    {
                        Thing thing2 = array2[num9];
                        if (thing2 == null || !thing2.def.holdsRoof)
                        {
                            color2.a = 100;
                        }
                    }

                    array[num + l * width + m] = color2;
                }
            }

            return array;
        }

        /// <summary>
        /// The whole trick: a cell you can see through reports the cell the column ACTUALLY
        /// SHOWS.
        ///
        /// ⚠ THIS WAS THE ONE-DESCENT BUG, FOR THE EIGHTH TIME. It used to end with a single
        /// <c>idx - slot * sizeX</c> step - one band down, unconditionally. That is correct
        /// from level +1, where the band below is the opaque surface, and wrong from every
        /// level above it: from +2 or +3 the cell one band down is usually open air too, so
        /// the overlay shaded the below view with the glow, roof and edifice of an EMPTY AIR
        /// CELL while SectionLayer_ABBelowV2 was drawing the ground two or three levels
        /// further down. Unroofed air reads as full daylight, so the symptom is a deep view
        /// that stays bright at night and ignores every lamp actually lighting it - which
        /// looks like a lighting bug, not like a missing descent.
        ///
        /// It survived the standing audit because that audit greps for `- Slot`, and this was
        /// written in index space. See ABBands.TryResolveVisibleFrom.
        ///
        /// Deliberately requireUnfogged: FALSE. The overlay must shade whatever the terrain
        /// layer drew, and that layer draws fogged ground too (behind its own fog fan);
        /// vanilla shades fogged cells as well, so demanding legibility here would put a
        /// bright square under every fog skirt.
        /// </summary>
        private static int SourceIndex(Map map, ABBandMap bands, CellIndices indices, int idx)
        {
            if (idx < 0 || idx >= indices.NumGridCells)
            {
                return -1;
            }
            IntVec3 c = indices.IndexToCell(idx);
            if (!ABBands.TryResolveVisibleFrom(map, bands, c, requireUnfogged: false,
                    out IntVec3 below, out _))
            {
                return idx; // opaque, off-band or in a seam: this cell shades itself
            }
            return indices.CellToIndex(below);
        }

        public override void DrawLayer()
        {
            if (!Visible || mesh == null || mesh.disabled || mesh.mesh == null)
            {
                return;
            }
            Graphics.DrawMesh(mesh.mesh, Matrix4x4.Translate(offset), mesh.material, mesh.renderLayer);
        }

        /// <summary>Baked submeshes are free-standing (not owned by this layer's subMeshes
        /// list), so their Unity mesh must be destroyed by hand or every section
        /// regeneration leaks one.</summary>
        private void Release()
        {
            fullTris = null;
            voidCut = false;
            if (mesh != null)
            {
                if (mesh.mesh != null)
                {
                    UnityEngine.Object.Destroy(mesh.mesh);
                }

                mesh = null;
            }
        }
        internal string ReportAt(IntVec3 c)
        {
            if (mesh == null || mesh.mesh == null)
            {
                return "  AB overlay: no mesh";
            }

            CellRect cellRect = bakedRect;
            CellRect cellRect2;
            if (!cellRect.Contains(c))
            {
                cellRect2 = cellRect;
                return "  AB overlay: cell outside baked rect " + cellRect2.ToString();
            }

            int width = cellRect.Width;
            int num = (width + 1) * (cellRect.Height + 1);
            int num2 = c.x - cellRect.minX;
            int num3 = c.z - cellRect.minZ;
            int num4 = num3 * (width + 1) + num2;
            int num5 = (num3 + 1) * (width + 1) + num2;
            int i = num5 + 1;
            int i2 = num4 + 1;
            int i3 = num + num3 * width + num2;
            Color32[] cols = mesh.mesh.colors32;
            string[] array = new string[28]
            {
                "  AB overlay: visible=",
                Visible.ToString(),
                " verts=",
                mesh.mesh.vertexCount.ToString(),
                " colors=",
                cols.Length.ToString(),
                " material=",
                mesh.material?.name,
                " shader=",
                mesh.material?.shader?.name,
                " queue=",
                (mesh.material?.renderQueue).ToString(),
                " layer=",
                mesh.renderLayer.ToString(),
                " rect=",
                null,
                null,
                null,
                null,
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
            cellRect2 = cellRect;
            array[15] = cellRect2.ToString();
            array[16] = " zeroColors=";
            array[17] = ZeroColors.ToString();
            array[18] = "\n    BL=";
            array[19] = At(num4);
            array[20] = " TL=";
            array[21] = At(num5);
            array[22] = " TR=";
            array[23] = At(i);
            array[24] = " BR=";
            array[25] = At(i2);
            array[26] = " C=";
            array[27] = At(i3);
            return string.Concat(array);
            string At(int num6)
            {
                return (num6 >= 0 && num6 < cols.Length) ? cols[num6].ToString() : "(none)";
            }
        }

        private void ApplyVoidCut(Map map, ABBandMap bands, CellRect rect)
        {
            if (fullTris == null)
            {
                return;
            }

            if (!ABSpaceBackdrop.HasWorldBackdrop(map) || !VoidCut)
            {
                if (voidCut)
                {
                    mesh.mesh.SetTriangles(fullTris, 0);
                    voidCut = false;
                }

                mesh.disabled = false;
                return;
            }

            List<int> list = cutScratch;
            list.Clear();
            int width = rect.Width;
            int num = 0;
            for (int i = 0; i < rect.Height; i++)
            {
                int num2 = 0;
                while (num2 < width)
                {
                    IntVec3 cell = new IntVec3(rect.minX + num2, 0, rect.minZ + i);
                    if (!ABSpaceBackdrop.IsVoid(map, bands, cell))
                    {
                        int num3 = num * 12;
                        if (num3 + 12 > fullTris.Length)
                        {
                            break;
                        }

                        for (int j = 0; j < 12; j++)
                        {
                            list.Add(fullTris[num3 + j]);
                        }
                    }

                    num2++;
                    num++;
                }
            }

            voidCut = true;
            if (list.Count == 0)
            {
                mesh.disabled = true;
                return;
            }

            mesh.disabled = false;
            mesh.mesh.SetTriangles(list, 0);
        }

        internal static bool VoidCut = false;

        private static readonly List<int> cutScratch = new List<int>(3468);

        private bool voidCut;

        private int[] fullTris;

        internal static bool ZeroColors = false;

        private CellRect bakedRect;

    }

    /// <summary>
    /// On a banded map SectionLayer_ABBelowLighting replaces vanilla's overlay entirely, so
    /// vanilla's must go quiet or every cell would be darkened twice.
    /// </summary>
    [HarmonyPatch(typeof(SectionLayer_LightingOverlay), "Visible", MethodType.Getter)]
    public static class Patch_LightingOverlay_ABSuppressOnBanded
    {
        /// <summary>Resolved from SectionLayer.section, NOT MapDrawLayer.map.
        ///
        /// MapDrawLayer.map is PRIVATE, and the first version of this patch resolved it
        /// inside a try/catch that swallowed the failure and left __result true. Vanilla's
        /// overlay therefore kept drawing while this file's layer added a second one, and
        /// the two darkening masks multiplied. Silent fallback on a render path is very
        /// hard to see; this fails LOUDLY once instead.</summary>
        private static readonly AccessTools.FieldRef<SectionLayer, Section> SectionRef =
            AccessTools.FieldRefAccess<SectionLayer, Section>("section");

        private static bool warned;

        private static void Postfix(SectionLayer_LightingOverlay __instance, ref bool __result)
        {
            if (!__result || !ABGuard.On(ABGuard.Rendering) || LetVanillaDraw)
            {
                return;
            }

            try
            {
                Map map = SectionRef(__instance)?.map;
                if (map != null && ABBands.Banded(map))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Error("[As above, So below] V2: could not suppress vanilla lighting overlay on banded maps; below content will be double-darkened. " + ex);
                }
            }
        }
        internal static bool LetVanillaDraw = false;

    }

    // ⚠⚠ DO NOT RE-ADD THE "SKIP VANILLA'S BAKE TOO" PREFIX. It existed here until
    // 2026-08, prefixing SectionLayer_LightingOverlay.Regenerate to return false whenever
    // Banded(map) && ABGuard.On(Rendering). It was DELETED, and the reasoning is worth the
    // twenty lines because the optimisation looks free and is not.
    //
    // What it bought: 3.92 ms per 2000 frames across 46 regenerations, i.e. ~0.002 ms per
    // frame. Vanilla's overlay is suppressed from DRAWING by the Visible patch above, and
    // Section.TryUpdate does not consult Visible, so it kept baking a mesh nobody drew.
    //
    // What it cost, all three found by a player reading the patch:
    //
    // 1. IT LEFT THE LAYER CLEAN AND EMPTY. Section.TryUpdate sets Dirty = false AFTER
    //    calling Regenerate - it does not care that a prefix skipped the body. So vanilla's
    //    layer sat marked-clean holding zero verts, with sectRect and firstCenterInd never
    //    initialised.
    //
    // 2. ⚠ THE SKIP CONDITION WAS A MUTABLE GLOBAL, AND FLIPPING IT DIRTIED NOTHING.
    //    ABGuard.Rendering is not config: ~10 rendering call sites trip it via
    //    ABGuard.Disable when they throw, the settings panel re-arms it, and Reset() clears
    //    it on load. The instant it flipped, BOTH patches stopped applying together -
    //    vanilla's overlay became Visible again holding the empty mesh from (1), and since
    //    its relevantChangeTypes are only Roofs | GroundGlow, a settled colony can go a very
    //    long time before a section re-dirties. Symptom: guard trips, and the map then draws
    //    with NO lighting overlay at all until the player happens to build something.
    //
    // 3. IT HAD A SECOND CONSUMER WE NEVER COUNTED. LudeonTK's EditWindow_DebugInspector
    //    calls SectionLayer_LightingOverlay.GlowReportAt, which reads the baked mesh's
    //    colors32 DIRECTLY and never consults Visible. Against an unbaked mesh that is an
    //    IndexOutOfRange per frame with the glow report open.
    //
    // Plus an empty `catch { }` that swallowed every exception silently - in the same file
    // whose Visible patch above carries a doc comment about why that is wrong. Guards are
    // per-consumer; fixing one does not fix its neighbour.
    //
    // THE RULE: suppressing a vanilla layer's DRAW is safe because Visible is re-read every
    // frame. Suppressing its BAKE is not, because the mesh persists and the dirty system is
    // not listening to our global. If a future window really needs those 0.002 ms, the price
    // is a shared ownership predicate plus a WholeMapChanged(Roofs | GroundGlow) fired
    // (deferred out of the draw call) on every ABGuard.Rendering transition, AND a guard on
    // GlowReportAt. That is ~40 lines of hazard for a rounding error. Leave it deleted.
}
