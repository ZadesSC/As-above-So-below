using System.IO;
using System.Reflection;
using Verse.AI;
using Verse.AI.Group;
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>V2 dev tools: inspect the band layout, force bands open, and place
    /// stairwells without waiting for a construction job.</summary>
    public static partial class ABDevTools
    {
        [DebugAction("As above", "AB2: band info", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2BandInfo()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            ABBandMap b = ABBands.CompOf(map);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("map size: " + map.Size);
            if (b == null || !b.Banded)
            {
                sb.AppendLine("NOT BANDED (ordinary vanilla map).");
                sb.AppendLine("V2 enabled: " + ABV2.Enabled + " - banding applies to newly generated player colony maps.");
            }
            else
            {
                sb.AppendLine("bandCount=" + b.bandCount + " bandHeight=" + b.bandHeight
                    + " gutter=" + b.GutterRows + " slot=" + b.Slot
                    + " (aligned to " + ABBandMap.SlotAlignment + ")"
                    + " surfaceBand=" + b.surfaceBand);
                for (int i = 0; i < b.bandCount; i++)
                {
                    sb.AppendLine("  band " + i + " (level " + (i - b.surfaceBand) + ") rect=" + b.RectOfBand(i)
                        + " open=" + b.IsOpen(i));
                }
                sb.AppendLine("current view band: " + ABBandView.CurrentBand(map)
                    + " (level " + ABBandView.CurrentLevel(map) + ")");
                sb.Append(ABWormhole.DebugDump(map));
            }
            Log.Warning(ABLog.Tag + " V2 band info:\n" + sb);
            Messages.Message("AB2: band info written to log.", MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>Per-pawn verdict from ONE below-pawn draw pass: drawn, or skipped and
        /// why. Run it from the band ABOVE the pawn you cannot see - if the pawn is listed
        /// DRAW then the masking is fine and the problem is draw order or altitude; if it is
        /// listed SKIP the reason names the filter that rejected it.</summary>
        [DebugAction("As above", "AB2: below pawn report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2BelowPawnReport()
        {
            ABBelowDynamicDraw.ReportNextPass = true;
            Messages.Message("AB2: below pawn report armed for the next frame - see log.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>Point-in-time view of every in-flight transit. Use when pawns or animals
        /// look stuck at a stairwell: an ageing record whose distToNear is not shrinking is a
        /// stuck pawn, a young record with a large distance is one still walking.</summary>
        [DebugAction("As above", "AB2: transit health", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2TransitHealth()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            Log.Warning(ABLog.Tag + " V2 transit health:\n" + ABWormholePather.HealthReport(map));
            Messages.Message("AB2: transit health written to log.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("As above", "AB2: toggle transit logging", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleTransitLog()
        {
            ABV2Debug.LogTransit = !ABV2Debug.LogTransit;
            Messages.Message("AB2: transit logging " + (ABV2Debug.LogTransit ? "ON" : "OFF") + ".",
                MessageTypeDefOf.TaskCompletion, false);
            Log.Warning(ABLog.Tag + " transit logging " + (ABV2Debug.LogTransit ? "ON" : "OFF"));
        }

        [DebugAction("As above", "AB2: toggle combat logging", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleCombatLog()
        {
            ABV2Debug.LogCombat = !ABV2Debug.LogCombat;
            Messages.Message("AB2: combat logging " + (ABV2Debug.LogCombat ? "ON" : "OFF") + ".",
                MessageTypeDefOf.TaskCompletion, false);
            Log.Warning(ABLog.Tag + " combat logging " + (ABV2Debug.LogCombat ? "ON" : "OFF"));
        }

        /// <summary>A/B for the retracting rock field. OFF = the historical full-cell opaque
        /// quad, which squares off the mass silhouette past its own black outline once per
        /// level; ON = the fill eroded to the tile's own link rule so it hides under the art.
        /// Flips all three emitters at once (cap in-band, cross-level, below-terrain base) -
        /// they are one look and splitting the switch would invite half-fixing it.</summary>
        [DebugAction("As above", "AB2: toggle mass field fade", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleMassFieldFade()
        {
            SectionLayer_ABMountainCap.MassFieldFadeEnabled =
                !SectionLayer_ABMountainCap.MassFieldFadeEnabled;
            Messages.Message(
                "AB2: mass field fade "
                    + (SectionLayer_ABMountainCap.MassFieldFadeEnabled ? "ON (retracts under the outline)" : "OFF (full-cell square)"),
                MessageTypeDefOf.TaskCompletion, false);
            V2ApplyBisect();
        }

        /// <summary>A/B for the multi-level descent. OFF = one level per column (the shared
        /// descent's own answer); ON = keep descending through mass to the first solid floor.
        /// Kept separate from the field fade because they fail differently: the fade is a
        /// look, this one is a COST.</summary>
        [DebugAction("As above", "AB2: toggle mass depth cut", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleMassDepthCut()
        {
            SectionLayer_ABMountainCap.MassDepthCutEnabled =
                !SectionLayer_ABMountainCap.MassDepthCutEnabled;
            Messages.Message(
                "AB2: mass depth cut "
                    + (SectionLayer_ABMountainCap.MassDepthCutEnabled ? "ON (through to the first floor)" : "OFF (one level)"),
                MessageTypeDefOf.TaskCompletion, false);
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: bisect - toggle below terrain", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowTerrain()
        {
            ABV2Debug.DrawBelowTerrain = !ABV2Debug.DrawBelowTerrain;
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: bisect - toggle below things", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowThings()
        {
            ABV2Debug.DrawBelowThings = !ABV2Debug.DrawBelowThings;
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: bisect - toggle below air mask", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowAirMask()
        {
            ABV2Debug.DrawBelowAirMask = !ABV2Debug.DrawBelowAirMask;
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: bisect - toggle below lighting", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowLighting()
        {
            ABV2Debug.DrawBelowLighting = !ABV2Debug.DrawBelowLighting;
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: bisect - toggle below water depth", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowWater()
        {
            ABV2Debug.DrawBelowWater = !ABV2Debug.DrawBelowWater;
            V2ApplyBisect();
        }

        /// <summary>Everything the water systems know, per band. Run it BEFORE theorising
        /// about water: the per-band census separates "the river never generated" from "the
        /// river generated three levels up and was carved away", and the flow/global readout
        /// separates a missing depth pass from a stretched shader UV.</summary>
        [DebugAction("As above", "AB2: water report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2WaterReport()
        {
            Log.Warning(ABLog.Tag + " V2 water report:\n" + ABWaterBand.Report(Find.CurrentMap));
            Messages.Message("AB2: water report written to log.", MessageTypeDefOf.TaskCompletion, false);
        }

        private static void V2ApplyBisect()
        {
            Map map = Find.CurrentMap;
            map?.mapDrawer?.RegenerateEverythingNow();
            Messages.Message("AB2 bisect: " + ABV2Debug.StateSummary(),
                MessageTypeDefOf.TaskCompletion, false);
            Log.Warning(ABLog.Tag + " AB2 bisect: " + ABV2Debug.StateSummary());
        }

        [DebugAction("As above", "AB2: below layer report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2BelowLayerReport()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            IntVec3 intVec = UI.MouseCell();
            if (!intVec.InBounds(currentMap))
            {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();
            string[] array = new string[6]
            {
                "cell ",
                null,
                null,
                null,
                null,
                null
            };
            IntVec3 intVec2 = intVec;
            array[1] = intVec2.ToString();
            array[2] = " band=";
            array[3] = ABBands.BandOf(currentMap, intVec).ToString();
            array[4] = " terrain=";
            array[5] = currentMap.terrainGrid.TerrainAt(intVec).defName;
            stringBuilder.AppendLine(string.Concat(array));
            stringBuilder.AppendLine("DebugViewSettings.drawShadows=" + DebugViewSettings.drawShadows);
            Section section = null;
            foreach (Section item in AllSectionsOf(currentMap))
            {
                if (item.CellRect.Contains(intVec))
                {
                    section = item;
                    break;
                }
            }

            if (section == null)
            {
                stringBuilder.AppendLine("no section found for that cell");
                Log.Warning("[As above, So below] V2 below layer report:\n" + stringBuilder);
                return;
            }

            intVec2 = section.botLeft;
            stringBuilder.AppendLine("section botLeft=" + intVec2.ToString());
            foreach (SectionLayer item2 in SectionLayersOf(section))
            {
                string name = item2.GetType().Name;
                bool flag = name.StartsWith("SectionLayer_AB");
                bool flag2 = name.Contains("Shadow");
                if (flag || flag2)
                {
                    stringBuilder.AppendLine(name + "  visible=" + item2.Visible + ((item2 is SectionLayer_ABRockRootFill) ? ("  enabled=" + SectionLayer_ABRockRootFill.Enabled) : "") + "  subMeshes=" + item2.subMeshes.Count);
                    for (int i = 0; i < item2.subMeshes.Count; i++)
                    {
                        LayerSubMesh layerSubMesh = item2.subMeshes[i];
                        string text = ((layerSubMesh.material != null) ? layerSubMesh.material.name : "null");
                        stringBuilder.AppendLine("    [" + i + "] verts=" + layerSubMesh.verts.Count + " tris=" + layerSubMesh.tris.Count + " finalized=" + layerSubMesh.finalized + " disabled=" + layerSubMesh.disabled + " queue=" + ((layerSubMesh.material != null) ? layerSubMesh.material.renderQueue.ToString() : "?") + " mat=" + text);
                    }
                }
            }

            Log.Warning("[As above, So below] V2 below layer report:\n" + stringBuilder);
            Messages.Message("AB2: below layer report written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction("As above", "AB2: lighting report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2LightingReport()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("banded: " + ABBands.Banded(currentMap));
            ABBandMap aBBandMap = ABBands.CompOf(currentMap);
            IntVec3 intVec = UI.MouseCell();
            IntVec3 intVec2;
            if (!intVec.InBounds(currentMap))
            {
                intVec = FindProbeCell(currentMap, aBBandMap);
                intVec2 = intVec;
                stringBuilder.AppendLine("(mouse off map; probing " + intVec2.ToString() + " on the viewed level)");
                if (!intVec.InBounds(currentMap))
                {
                    Log.Warning("[As above, So below] V2 lighting report:\n" + stringBuilder);
                    return;
                }
            }

            string[] array = new string[10]
            {
                "cell ",
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
            intVec2 = intVec;
            array[1] = intVec2.ToString();
            array[2] = " band=";
            array[3] = ABBands.BandOf(currentMap, intVec).ToString();
            array[4] = " level=";
            array[5] = ABBands.LevelOf(currentMap, intVec).ToString();
            array[6] = " terrain=";
            array[7] = currentMap.terrainGrid.TerrainAt(intVec).defName;
            array[8] = " roof=";
            array[9] = currentMap.roofGrid.RoofAt(intVec)?.defName ?? "none";
            stringBuilder.AppendLine(string.Concat(array));
            stringBuilder.AppendLine("  glow here: " + currentMap.glowGrid.VisualGlowAt(intVec).ToString());
            if (aBBandMap != null && aBBandMap.Banded)
            {
                IntVec3 intVec3 = new IntVec3(intVec.x, 0, intVec.z - aBBandMap.Slot);
                if (intVec3.InBounds(currentMap))
                {
                    string[] array2 = new string[10]
                    {
                        "  below ",
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
                    intVec2 = intVec3;
                    array2[1] = intVec2.ToString();
                    array2[2] = " terrain=";
                    array2[3] = currentMap.terrainGrid.TerrainAt(intVec3).defName;
                    array2[4] = " roof=";
                    array2[5] = currentMap.roofGrid.RoofAt(intVec3)?.defName ?? "none";
                    array2[6] = " edifice=";
                    array2[7] = intVec3.GetEdifice(currentMap)?.def.defName ?? "none";
                    array2[8] = " fogged=";
                    array2[9] = currentMap.fogGrid.IsFogged(intVec3).ToString();
                    stringBuilder.AppendLine(string.Concat(array2));
                    stringBuilder.AppendLine("  glow below: " + currentMap.glowGrid.VisualGlowAt(intVec3).ToString());
                }

                if (ABBands.TryResolveVisibleFrom(currentMap, aBBandMap, intVec, requireUnfogged: false, out var below, out var drop))
                {
                    string[] array3 = new string[10]
                    {
                        "  overlay source (TryResolveVisibleFrom): ",
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
                    intVec2 = below;
                    array3[1] = intVec2.ToString();
                    array3[2] = " drop=";
                    array3[3] = drop.ToString();
                    array3[4] = " terrain=";
                    array3[5] = currentMap.terrainGrid.TerrainAt(below).defName;
                    array3[6] = " roof=";
                    array3[7] = currentMap.roofGrid.RoofAt(below)?.defName ?? "none";
                    array3[8] = " glow=";
                    array3[9] = currentMap.glowGrid.VisualGlowAt(below).ToString();
                    stringBuilder.AppendLine(string.Concat(array3));
                }
                else
                {
                    stringBuilder.AppendLine("  overlay source: the cell itself (nothing visible below)");
                }

                for (int i = 0; i < aBBandMap.bandCount; i++)
                {
                    int num = 0;
                    int num2 = 0;
                    int num3 = 0;
                    foreach (IntVec3 item in aBBandMap.RectOfBand(i))
                    {
                        if (currentMap.roofGrid.Roofed(item))
                        {
                            num++;
                        }

                        TerrainDef terrainDef = currentMap.terrainGrid.TerrainAt(item);
                        if (terrainDef != null && terrainDef.exposesToVacuum)
                        {
                            num2++;
                        }

                        if (ABBands.ShowsBelow(terrainDef))
                        {
                            num3++;
                        }
                    }

                    stringBuilder.AppendLine("  band " + i + ": roofed=" + num + " vacuum=" + num2 + " seeThrough=" + num3 + " of " + aBBandMap.RectOfBand(i).Area);
                }
            }

            int num4 = 0;
            int num5 = 0;
            foreach (Section item2 in AllSectionsOf(currentMap))
            {
                foreach (SectionLayer item3 in SectionLayersOf(item2))
                {
                    if (item3 is SectionLayer_LightingOverlay && item3.Visible)
                    {
                        num4++;
                    }
                    else if (item3 is SectionLayer_ABBelowLighting && item3.Visible)
                    {
                        num5++;
                    }
                }
            }

            stringBuilder.AppendLine("vanilla lighting layers still visible: " + num4 + "  (MUST be 0 on a banded map)");
            stringBuilder.AppendLine("AB below-lighting layers visible: " + num5);
            AppendOverlayForensics(currentMap, intVec, stringBuilder);
            Log.Warning("[As above, So below] V2 lighting report:\n" + stringBuilder);
            Messages.Message("AB2: lighting report written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        private static IEnumerable<Section> AllSectionsOf(Map map)
        {
            Section[,] arr = (Section[,])AccessTools.Field(typeof(MapDrawer), "sections").GetValue(map.mapDrawer);
            if (arr == null)
            {
                yield break;
            }
            foreach (Section s in arr)
            {
                if (s != null)
                {
                    yield return s;
                }
            }
        }

        private static List<SectionLayer> SectionLayersOf(Section sec)
        {
            return (List<SectionLayer>)AccessTools.Field(typeof(Section), "layers").GetValue(sec);
        }

        /// <summary>Opens the camera-bounds calibration readout. The window UNLOCKS the
        /// band clamp while it is open, so the camera can be pushed past the currently
        /// baked limits to find better ones; the numbers it reports get baked into
        /// ABCameraBounds.For(level).</summary>
        /// <summary>Bisect for the drafted group-move bug: three drafted pawns ordered to
        /// one cell, only one moves. Flip these ONE AT A TIME and re-test - whichever
        /// restores group movement owns the bug.</summary>
        [DebugAction("As above", "AB2: bisect - toggle click-through", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleClickThrough()
        {
            ABBelowClickThrough.Enabled = !ABBelowClickThrough.Enabled;
            Messages.Message("AB2: below click-through / select-through "
                + (ABBelowClickThrough.Enabled ? "ON" : "OFF"),
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("As above", "AB2: bisect - toggle below multi-select", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleBelowMultiSelect()
        {
            Patch_ThingSelectionUtility_ABBelow.Enabled = !Patch_ThingSelectionUtility_ABBelow.Enabled;
            Messages.Message("AB2: below multi-select "
                + (Patch_ThingSelectionUtility_ABBelow.Enabled ? "ON" : "OFF"),
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("As above", "AB2: camera calibration", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2CameraCalibration()
        {
            Find.WindowStack.Add(new Dialog_ABCameraCalibration());
        }

        [DebugAction("As above", "AB2: open all bands", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2OpenAllBands()
        {
            Map map = Find.CurrentMap;
            ABBandMap b = ABBands.CompOf(map);
            if (b == null || !b.Banded)
            {
                Messages.Message("AB2: this map is not banded.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            for (int i = 0; i < b.bandCount; i++)
            {
                b.Open(i);
                foreach (IntVec3 c in b.RectOfBand(i))
                {
                    if (c.InBounds(map))
                    {
                        map.fogGrid.Unfog(c);
                    }
                }
            }
            Messages.Message("AB2: all " + b.bandCount + " bands opened and unfogged.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("As above", "AB2: place stairs down here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2PlaceStairsDown()
        {
            V2PlaceStairs("AB2_StairsDown");
        }

        [DebugAction("As above", "AB2: place stairs up here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2PlaceStairsUp()
        {
            V2PlaceStairs("AB2_StairsUp");
        }

        private static void V2PlaceStairs(string defName)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            IntVec3 intVec = UI.MouseCell();
            if (!intVec.InBounds(currentMap))
            {
                return;
            }

            ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (namedSilentFail == null)
            {
                Messages.Message("AB2: " + defName + " def missing.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            try
            {
                ClearCell(currentMap, intVec);
                Thing thing = ThingMaker.MakeThing(namedSilentFail, GenStuff.DefaultStuffFor(namedSilentFail));
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, intVec, currentMap);
                string[] array = new string[5]
                {
                    "AB2: placed ",
                    namedSilentFail.label,
                    " at ",
                    null,
                    null
                };
                IntVec3 intVec2 = intVec;
                array[3] = intVec2.ToString();
                array[4] = ".";
                Messages.Message(string.Concat(array), MessageTypeDefOf.TaskCompletion, historical: false);
            }
            catch (Exception ex)
            {
                Log.Error("[As above, So below] V2: stairs placement failed: " + ex);
            }
        }
    }
}
