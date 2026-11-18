using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AsAboveSoBelow
{
    public static partial class ABDevTools
    {
#if RIMWORLD_17
        private static int probeSeenCalls;
#endif

        [DebugAction("As above", "AB2: toggle cap corner fillers", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleCapCornerFillers()
        {
            SectionLayer_ABMountainCap.CornerFillersEnabled = !SectionLayer_ABMountainCap.CornerFillersEnabled;
            Messages.Message("AB2: cap corner fillers " + (SectionLayer_ABMountainCap.CornerFillersEnabled ? "ON" : "OFF"), MessageTypeDefOf.TaskCompletion, historical: false);
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: toggle stone border rule", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleStoneBorderRule()
        {
            SectionLayer_ABMountainCap.StoneBorderRule = !SectionLayer_ABMountainCap.StoneBorderRule;
            Messages.Message("AB2: stone border rule " + (SectionLayer_ABMountainCap.StoneBorderRule ? "ON (build 0053)" : "OFF (build 0051 fades)"), MessageTypeDefOf.TaskCompletion, historical: false);
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: toggle south filler stretch", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleSouthFillerStretch()
        {
            SectionLayer_ABMountainCap.SouthFillerStretch = !SectionLayer_ABMountainCap.SouthFillerStretch;
            Messages.Message("AB2: south filler stretch " + (SectionLayer_ABMountainCap.SouthFillerStretch ? "ON (build 0053)" : "OFF (vanilla)"), MessageTypeDefOf.TaskCompletion, historical: false);
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: toggle ledge rock tile", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleLedgeRockTile()
        {
            SectionLayer_ABMountainCap.LedgeRockTile = !SectionLayer_ABMountainCap.LedgeRockTile;
            Messages.Message("AB2: ledge rock tile " + (SectionLayer_ABMountainCap.LedgeRockTile ? "ON (option 3)" : "OFF (field plus rim tiles)"), MessageTypeDefOf.TaskCompletion, historical: false);
            V2ApplyBisect();
        }

        [DebugAction("As above", "AB2: toggle rock root fill", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleRockRootFill()
        {
            SectionLayer_ABRockRootFill.Enabled = !SectionLayer_ABRockRootFill.Enabled;
            string text;
            if (SectionLayer_ABRockRootFill.Enabled)
            {
                text = "ON";
            }
            else
            {
                text = (SectionLayer_ABMountainCap.LedgeRockTile ? "OFF (nothing under rock corners: worse than pre-fix while ledge rock tile is on)" : "OFF (pre-fix corners)");
            }

            Messages.Message("AB2: rock root fill " + text, MessageTypeDefOf.TaskCompletion, historical: false);
            Find.CurrentMap?.mapDrawer?.RegenerateEverythingNow();
            Log.Warning("[As above, So below] AB2 bisect: " + ABV2Debug.StateSummary());
        }

        [DebugAction("As above", "AB2: bisect - toggle space curtain", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleSpaceCurtain()
        {
            ABBandCurtain.SpaceCurtainOff = !ABBandCurtain.SpaceCurtainOff;
            Messages.Message("AB2 bisect: space curtain " + (ABBandCurtain.SpaceCurtainOff ? "OFF" : "ON"), MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction("As above", "AB2: bisect - cycle space shadow mask", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2CycleSpaceShadowMask()
        {
            int num = (Patch_SectionLayer_Terrain_ABUnmaskShadows.SpaceMaskMode = (Patch_SectionLayer_Terrain_ABUnmaskShadows.SpaceMaskMode + 1) % 3);
            Find.CurrentMap?.mapDrawer?.RegenerateEverythingNow();
            string text = num switch
            {
                1 => "VANILLA (untouched)",
                0 => "REBUILT for void cells",
                _ => "OFF",
            };
            Messages.Message("AB2 bisect: space shadow mask " + text, MessageTypeDefOf.TaskCompletion, historical: false);
            Log.Warning("[As above, So below] AB2 bisect: space shadow mask " + text);
        }

        private static void AppendOverlayForensics(Map map, IntVec3 c, StringBuilder sb)
        {
            try
            {
                Material lightOverlay = MatBases.LightOverlay;
                sb.AppendLine("materials: LightOverlay color=" + lightOverlay.color.ToString() + " shader=" + lightOverlay.shader?.name + " queue=" + lightOverlay.renderQueue + " | ShadowMask shader=" + MatBases.ShadowMask.shader?.name + " queue=" + MatBases.ShadowMask.renderQueue + " color=" + MatBases.ShadowMask.color.ToString() + " | FogOfWar color=" + MatBases.FogOfWar.color.ToString() + " queue=" + MatBases.FogOfWar.renderQueue);
                sb.AppendLine("sky: glow=" + map.skyManager.CurSkyGlow + " disableSkyLighting=" + map.Biome?.disableSkyLighting + " backdrop=" + ABSpaceBackdrop.HasWorldBackdrop(map) + " drawMapClippers=" + map.DrawMapClippers + " hour=" + GenLocalDate.HourInteger(map));
                sb.AppendLine("cell: roof=" + (map.roofGrid.RoofAt(c)?.defName ?? "none") + " edifice=" + (c.GetEdifice(map)?.def.defName ?? "none") + " fogged=" + map.fogGrid.IsFogged(c));
                MapDrawLayer_ExteriorLightingOverlay globalLayer = map.mapDrawer.GetGlobalLayer<MapDrawLayer_ExteriorLightingOverlay>();
                if (globalLayer == null)
                {
                    sb.AppendLine("exterior overlay: NOT PRESENT");
                }
                else
                {
                    sb.Append("exterior overlay: visible=" + globalLayer.Visible + " dirty=" + globalLayer.Dirty + " submeshes=" + globalLayer.subMeshes.Count);
                    for (int i = 0; i < globalLayer.subMeshes.Count; i++)
                    {
                        LayerSubMesh layerSubMesh = globalLayer.subMeshes[i];
                        Color32[] array = ((layerSubMesh.mesh != null) ? layerSubMesh.mesh.colors32 : null);
                        sb.Append(" [" + layerSubMesh.material?.name + " verts=" + ((layerSubMesh.mesh != null) ? layerSubMesh.mesh.vertexCount : (-1)) + " layer=" + layerSubMesh.renderLayer + " disabled=" + layerSubMesh.disabled + " c0=" + ((array != null && array.Length != 0) ? array[0].ToString() : "-") + "]");
                    }

                    sb.AppendLine();
                }

                Section sec = map.mapDrawer.SectionAt(c);
                foreach (SectionLayer item in SectionLayersOf(sec))
                {
                    if (item is SectionLayer_ABBelowLighting sectionLayer_ABBelowLighting)
                    {
                        sb.AppendLine(sectionLayer_ABBelowLighting.ReportAt(c));
                    }
                    else if (item is SectionLayer_LightingOverlay sectionLayer_LightingOverlay)
                    {
                        string text;
                        try
                        {
                            text = sectionLayer_LightingOverlay.GlowReportAt(c).Replace("\n", " ");
                        }
                        catch (Exception ex)
                        {
                            text = "(GlowReportAt threw: " + ex.GetType().Name + ")";
                        }

                        sb.AppendLine("  vanilla overlay (invisible, still baked): visible=" + sectionLayer_LightingOverlay.Visible + " " + text);
                    }
                    else
                    {
                        if (!(item is SectionLayer_Terrain sectionLayer_Terrain))
                        {
                            continue;
                        }

                        for (int j = 0; j < sectionLayer_Terrain.subMeshes.Count; j++)
                        {
                            LayerSubMesh layerSubMesh2 = sectionLayer_Terrain.subMeshes[j];
                            if (layerSubMesh2.material == MatBases.ShadowMask)
                            {
                                sb.AppendLine("  terrain ShadowMask submesh: verts=" + ((layerSubMesh2.mesh != null) ? layerSubMesh2.mesh.vertexCount : (-1)) + " disabled=" + layerSubMesh2.disabled + " finalized=" + layerSubMesh2.finalized + " mode=" + Patch_SectionLayer_Terrain_ABUnmaskShadows.SpaceMaskMode);
                            }
                        }
                    }
                }
            }
            catch (Exception ex2)
            {
                sb.AppendLine("forensics threw: " + ex2);
            }
        }

        [DebugAction("As above", "AB2: bisect - vanilla lighting instead of ours", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleVanillaLighting()
        {
            bool flag = (Patch_LightingOverlay_ABSuppressOnBanded.LetVanillaDraw = !Patch_LightingOverlay_ABSuppressOnBanded.LetVanillaDraw);
            ABV2Debug.DrawBelowLighting = !flag;
            Messages.Message("AB2 bisect: " + (flag ? "VANILLA lighting overlay drawing, ours off" : "ours drawing, vanilla suppressed"), MessageTypeDefOf.TaskCompletion, historical: false);
            Log.Warning("[As above, So below] AB2 bisect: " + (flag ? "VANILLA lighting overlay drawing, ours off" : "ours drawing, vanilla suppressed"));
            Find.CurrentMap?.mapDrawer?.RegenerateEverythingNow();
        }

        [DebugAction("As above", "AB2: bisect - cut overlay over void", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleOverlayOverVoid()
        {
            SectionLayer_ABBelowLighting.VoidCut = !SectionLayer_ABBelowLighting.VoidCut;
            string text = (SectionLayer_ABBelowLighting.VoidCut ? "CUT (void undimmed)" : "OFF (shipped rule: the void dims with the sky)");
            Messages.Message("AB2 bisect: overlay over void " + text, MessageTypeDefOf.TaskCompletion, historical: false);
            Log.Warning("[As above, So below] AB2 bisect: overlay over void " + text);
            Find.CurrentMap?.mapDrawer?.RegenerateEverythingNow();
        }

        [DebugAction("As above", "AB2: bisect - zero below lighting colours", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2ToggleZeroBelowLightingColors()
        {
            SectionLayer_ABBelowLighting.ZeroColors = !SectionLayer_ABBelowLighting.ZeroColors;
            Messages.Message("AB2 bisect: below lighting colours " + (SectionLayer_ABBelowLighting.ZeroColors ? "ALL ZERO" : "normal"), MessageTypeDefOf.TaskCompletion, historical: false);
            Log.Warning("[As above, So below] AB2 bisect: below lighting colours " + (SectionLayer_ABBelowLighting.ZeroColors ? "ALL ZERO" : "normal"));
            Find.CurrentMap?.mapDrawer?.RegenerateEverythingNow();
        }

        private static IntVec3 FindProbeCell(Map map, ABBandMap b)
        {
            CellRect cellRect = ((b != null && b.Banded) ? b.RectOfBand(ABBandView.CurrentBand(map)) : new CellRect(0, 0, map.Size.x, map.Size.z));
            IntVec3 centerCell = cellRect.CenterCell;
            IntVec3[] radialPattern = GenRadial.RadialPattern;
            foreach (IntVec3 intVec in radialPattern)
            {
                IntVec3 intVec2 = centerCell + intVec;
                if (cellRect.Contains(intVec2) && intVec2.InBounds(map))
                {
                    TerrainDef terrainDef = map.terrainGrid.TerrainAt(intVec2);
                    if (terrainDef != null && (terrainDef.exposesToVacuum || ABBands.ShowsBelow(terrainDef)) && map.roofGrid.RoofAt(intVec2) == null && intVec2.GetEdifice(map) == null)
                    {
                        return intVec2;
                    }
                }
            }

            return centerCell;
        }

        [DebugAction("As above", "AB2: self-test carrier hashes", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2SelfTestCarrierHashes()
        {
            StringBuilder stringBuilder = new StringBuilder();
            int num = 0;
            int num2 = 0;
            Dictionary<ushort, ThingDef> dictionary = new Dictionary<ushort, ThingDef>();
            List<string> list = new List<string>();
            List<string> list2 = new List<string>();
            foreach (ThingDef allDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (dictionary.TryGetValue(allDef.shortHash, out var value))
                {
                    string item = allDef.defName + "  <->  " + value.defName + "   (both short hash " + allDef.shortHash + ")";
                    if (ABColumnNetworks.IsCarrier(allDef) || ABColumnNetworks.IsCarrier(value))
                    {
                        list.Add(item);
                    }
                    else
                    {
                        list2.Add(item);
                    }
                }
                else
                {
                    dictionary.Add(allDef.shortHash, allDef);
                }
            }

            int carrierDefCount = ABColumnNetworks.CarrierDefCount;
            if (carrierDefCount > 0)
            {
                num++;
                stringBuilder.AppendLine("PASS  " + carrierDefCount + " carrier def(s) generated.");
            }
            else
            {
                num2++;
                stringBuilder.AppendLine("FAIL  no carrier defs were generated, so checks 2 and 3 below would pass vacuously. Either no utility network was detected on this modlist, or Scan() has not run yet - open a map with a column on it.");
            }

            int num3 = 0;
            foreach (ThingDef allCarrierDef in ABColumnNetworks.AllCarrierDefs)
            {
                if (allCarrierDef.shortHash == 0)
                {
                    num3++;
                    stringBuilder.AppendLine("        " + allCarrierDef.defName + " has short hash 0");
                }
            }

            if (num3 == 0)
            {
                num++;
                stringBuilder.AppendLine("PASS  every carrier holds a non-zero short hash.");
            }
            else
            {
                num2++;
                stringBuilder.AppendLine("FAIL  " + num3 + " carrier(s) still hold short hash 0 - GiveCarrierShortHash did not run for them.");
            }

            if (list.Count == 0)
            {
                num++;
                stringBuilder.AppendLine("PASS  no carrier shares a short hash with any other ThingDef. MapFileCompressor cannot report us on this modlist.");
            }
            else
            {
                num2++;
                stringBuilder.AppendLine("FAIL  " + list.Count + " collision(s) involve a carrier:");
                for (int i = 0; i < list.Count; i++)
                {
                    stringBuilder.AppendLine("        " + list[i]);
                }
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("  collisions NOT involving this mod: " + list2.Count + ((list2.Count > 0) ? "  (these are between other mods; not ours to fix, but worth passing on)" : string.Empty));
            for (int j = 0; j < list2.Count && j < 25; j++)
            {
                stringBuilder.AppendLine("        " + list2[j]);
            }

            if (list2.Count > 25)
            {
                stringBuilder.AppendLine("        ... and " + (list2.Count - 25) + " more.");
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("  carrier hashes, for the record:");
            foreach (ThingDef allCarrierDef2 in ABColumnNetworks.AllCarrierDefs)
            {
                stringBuilder.AppendLine("        " + allCarrierDef2.shortHash.ToString().PadLeft(5) + "  " + allCarrierDef2.defName);
            }

            Report("carrier short hashes", stringBuilder, num, num2);
        }

        private static void StuckProbeAISection(StringBuilder sb, Pawn pawn, Map map)
        {
            try
            {
                int ticksGame = Find.TickManager.TicksGame;
                Faction ofPlayer = Faction.OfPlayer;
                bool flag = ofPlayer != null && pawn.HostileTo(ofPlayer);
                sb.AppendLine("  AI:");
                sb.AppendLine("    faction=" + (pawn.Faction?.Name ?? "none") + " hostileToPlayer=" + flag + " level=" + ABBands.LevelOf(map, pawn.Position) + " mutant=" + (pawn.mutant?.Def?.defName ?? "-") + " mentalState=" + (pawn.MentalStateDef?.defName ?? "-") + " downed=" + pawn.Downed + " drafted=" + pawn.Drafted);
                Job curJob = pawn.CurJob;
                if (curJob != null)
                {
                    string[] array = new string[12]
                    {
                        "    current job ",
                        curJob.def.defName,
                        " from giver=",
                        curJob.jobGiver?.GetType().Name ?? "-",
                        " tree=",
                        curJob.jobGiverThinkTree?.defName ?? "-",
                        " age=",
                        (ticksGame - curJob.startTick).ToString(),
                        "t expiry=",
                        curJob.expiryInterval.ToString(),
                        " targetA=",
                        null
                    };
                    LocalTargetInfo targetA = curJob.targetA;
                    array[11] = targetA.ToString();
                    sb.AppendLine(string.Concat(array));
                }

                Lord lord = pawn.GetLord();
                if (lord == null)
                {
                    sb.AppendLine("    lord: none");
                }
                else
                {
                    sb.AppendLine("    lord: " + (lord.LordJob?.GetType().Name ?? "?") + " toil=" + (lord.CurLordToil?.GetType().Name ?? "?") + " pawns=" + lord.ownedPawns.Count + " ticksInToil=" + lord.ticksInToil);
                }

                PawnDuty pawnDuty = pawn.mindState?.duty;
                if (pawnDuty == null)
                {
                    sb.AppendLine("    duty: none");
                }
                else
                {
                    IntVec3 intVec = (pawnDuty.focus.IsValid ? pawnDuty.focus.Cell : IntVec3.Invalid);
                    string[] array2 = new string[6]
                    {
                        "    duty: ",
                        pawnDuty.def.defName,
                        " focus=",
                        null,
                        null,
                        null
                    };
                    string text;
                    if (!intVec.IsValid)
                    {
                        text = "-";
                    }
                    else
                    {
                        IntVec3 intVec2 = intVec;
                        text = intVec2.ToString() + " band " + ABBands.BandOf(map, intVec) + (ABBands.SameBand(map, intVec, pawn.Position) ? " (own band)" : " (OTHER BAND)");
                    }

                    array2[3] = text;
                    array2[4] = " radius=";
                    array2[5] = pawnDuty.radius.ToString("0.#");
                    sb.AppendLine(string.Concat(array2));
                }

                Thing thing = pawn.mindState?.enemyTarget;
                if (thing == null)
                {
                    sb.AppendLine("    enemyTarget: none");
                }
                else
                {
                    bool flag2 = thing.Spawned && ABBands.SameBand(map, thing.Position, pawn.Position);
                    sb.AppendLine("    enemyTarget: " + thing.LabelShort + " at " + thing.Position.ToString() + " band " + ABBands.BandOf(map, thing.Position) + (flag2 ? " (own band)" : " (OTHER BAND)") + " engaged " + (ticksGame - pawn.mindState.lastEngageTargetTick) + "t ago");
                    if (thing.Spawned && thing.Map == map)
                    {
                        sb.AppendLine("      CanReach Touch/Deadly=" + pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly) + "  CanReach PassAllDestroyable=" + pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly, canBashDoors: false, canBashFences: false, TraverseMode.PassAllDestroyablePlayerOwnedThings));
                        if (!flag2)
                        {
                            bool flag3 = ABWormhole.TryGetTransit(map, pawn.Position, thing.Position, pawn, out var near, out var far, thing);
                            sb.AppendLine("      transit to it: " + (flag3 ? ("YES via " + near.Position.ToString() + " -> " + far.Position.ToString()) : ("NONE - " + ABWormhole.LastRefusalText)));
                        }
                    }
                }

                List<IAttackTarget> potentialTargetsFor = map.attackTargetsCache.GetPotentialTargetsFor(pawn);
                int count = potentialTargetsFor.Count;
                int num = 0;
                int num2 = 0;
                int num3 = 0;
                List<KeyValuePair<float, string>> list = new List<KeyValuePair<float, string>>();
                for (int i = 0; i < potentialTargetsFor.Count; i++)
                {
                    Thing thing2 = potentialTargetsFor[i]?.Thing;
                    if (thing2 == null || !thing2.Spawned)
                    {
                        continue;
                    }

                    bool flag4 = ABBands.SameBand(map, thing2.Position, pawn.Position);
                    if (flag4)
                    {
                        num++;
                    }

                    bool flag5 = potentialTargetsFor[i].ThreatDisabled(pawn);
                    bool flag6 = AttackTargetFinder.IsAutoTargetable(potentialTargetsFor[i]);
                    bool flag7 = !(thing2 is Pawn pawn2) || pawn2.IsCombatant() || GenSight.LineOfSightToThing(pawn.Position, pawn2, map);
                    bool flag8 = pawn.Faction == null || !pawn.Faction.IsPlayer || !thing2.Position.Fogged(map);
                    bool flag9 = (!flag5 & flag6 & flag7 & flag8) && pawn.CanReach(thing2, PathEndMode.OnCell, Danger.Deadly);
                    if (flag9)
                    {
                        num2++;
                        if (flag4)
                        {
                            num3++;
                        }
                    }

                    float key = (flag4 ? 0f : 1000000f) + (float)thing2.Position.DistanceToSquared(pawn.Position);
                    list.Add(new KeyValuePair<float, string>(key, thing2.LabelShort + " at " + thing2.Position.ToString() + " band " + ABBands.BandOf(map, thing2.Position) + " threatDisabled=" + flag5 + " autoTargetable=" + flag6 + " combatantOrSeen=" + flag7 + (flag8 ? string.Empty : " FOGGED") + " usable=" + flag9));
                }

                sb.AppendLine("    hostile targets (AIGotoNearestHostile's census): " + count + " total, " + num + " on own band; " + num2 + " usable (" + num3 + " on own band)");
                list.Sort((KeyValuePair<float, string> a, KeyValuePair<float, string> b) => a.Key.CompareTo(b.Key));
                for (int num4 = 0; num4 < list.Count && num4 < 8; num4++)
                {
                    sb.AppendLine("      " + list[num4].Value);
                }

                if (pawn.mutant != null)
                {
                    sb.AppendLine("    (a mutant picks targets through MutantUtility.FindShamblerTarget and keeps them by ShouldLoseTarget; the census above is the assault giver's, not the shambler's acquisition. The enemyTarget line is the one to read.)");
                }
                else if (count > 0 && num2 == 0)
                {
                    sb.AppendLine("    >> NO USABLE HOSTILE TARGET: an assault duty's goto-hostile node returns nothing, and the pawn falls through to trashing, sapping, or wander. If the targets are on another band, the transit line and the link permissions are the question.");
                }

                if (flag)
                {
                    sb.AppendLine("    leap AI (runs only when the duty gave nothing):");
                    sb.Append(ABBandLeapAI.Explain(pawn));
                }

                sb.AppendLine("    " + ABShamblerCrossLevel.CounterReport());
            }
            catch (Exception ex)
            {
                sb.AppendLine("  AI section THREW: " + ex.Message);
            }
        }

        [DebugAction("As above", "AB2: designate a diagonal-only mine target", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2DesignateDiagonalOnlyMine()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null || !ABBands.Banded(currentMap))
            {
                Messages.Message("AB2: not a banded map.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int num = ABBandView.CurrentBand(currentMap);
            CellRect cellRect = ABBands.RectOfBand(currentMap, num);
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("AB2 DIAGONAL-ONLY MINE TARGETS");
            string[] array = new string[6]
            {
                "  viewed band ",
                num.ToString(),
                " (level ",
                ABBands.LevelOf(currentMap, cellRect.CenterCell).ToString(),
                "), rect ",
                null
            };
            CellRect cellRect2 = cellRect;
            array[5] = cellRect2.ToString();
            stringBuilder.AppendLine(string.Concat(array));
            int num2 = 0;
            int num3 = 0;
            int num4 = 0;
            int num5 = 0;
            List<IntVec3> list = new List<IntVec3>();
            foreach (IntVec3 item in cellRect)
            {
                if (item.GetFirstMineable(currentMap) == null)
                {
                    continue;
                }

                num2++;
                if (currentMap.designationManager.DesignationAt(item, DesignationDefOf.Mine) != null)
                {
                    num5++;
                    continue;
                }

                int num6 = 0;
                bool flag = true;
                for (int i = 0; i < 8; i++)
                {
                    IntVec3 intVec = item + GenAdj.AdjacentCells[i];
                    if (intVec.InBounds(currentMap) && ABBands.SameBand(currentMap, intVec, item) && intVec.Walkable(currentMap))
                    {
                        num6++;
                        int num7 = intVec.x - item.x;
                        int num8 = intVec.z - item.z;
                        if (num7 == 0 || num8 == 0)
                        {
                            flag = false;
                            break;
                        }

                        IntVec3 c = new IntVec3(item.x, 0, intVec.z);
                        IntVec3 c2 = new IntVec3(intVec.x, 0, item.z);
                        if ((c.InBounds(currentMap) && c.Walkable(currentMap)) || (c2.InBounds(currentMap) && c2.Walkable(currentMap)))
                        {
                            flag = false;
                            break;
                        }
                    }
                }

                if (num6 == 0)
                {
                    num3++;
                }
                else if (!flag)
                {
                    num4++;
                }
                else
                {
                    list.Add(item);
                }
            }

            List<KeyValuePair<Building_Door, Building_Door>> list2 = new List<KeyValuePair<Building_Door, Building_Door>>();
            ABWormhole.CollectPairs(currentMap, list2);
            List<IntVec3> anchors = new List<IntVec3>();
            for (int j = 0; j < list2.Count; j++)
            {
                if (list2[j].Key != null && ABBands.BandOf(currentMap, list2[j].Key.Position) == num)
                {
                    anchors.Add(list2[j].Key.Position);
                }

                if (list2[j].Value != null && ABBands.BandOf(currentMap, list2[j].Value.Position) == num)
                {
                    anchors.Add(list2[j].Value.Position);
                }
            }

            if (anchors.Count > 0)
            {
                list.SortBy((IntVec3 c3) => NearestAnchorDist(c3, anchors));
            }

            stringBuilder.AppendLine("  scanned: " + num2 + " mineable cell(s) | " + num3 + " walled in (no open neighbour) | " + num4 + " have a cardinal or unblocked-diagonal opening | " + num5 + " already designated | " + list.Count + " MATCH");
            stringBuilder.AppendLine("  link anchors on this band: " + anchors.Count);
            int num9 = 0;
            for (int num10 = 0; num10 < list.Count; num10++)
            {
                if (num9 >= 3)
                {
                    break;
                }

                IntVec3 intVec2 = list[num10];
                currentMap.designationManager.AddDesignation(new Designation(intVec2, DesignationDefOf.Mine));
                num9++;
                string[] array2 = new string[5]
                {
                    "  DESIGNATED ",
                    null,
                    null,
                    null,
                    null
                };
                IntVec3 intVec3 = intVec2;
                array2[1] = intVec3.ToString();
                array2[2] = "  ";
                array2[3] = intVec2.GetFirstMineable(currentMap).def.defName;
                array2[4] = ((anchors.Count > 0) ? ("  (" + NearestAnchorDist(intVec2, anchors).ToString("0.0") + " from the nearest anchor on this band)") : string.Empty);
                stringBuilder.AppendLine(string.Concat(array2));
                for (int num11 = 0; num11 < 8; num11++)
                {
                    IntVec3 intVec4 = intVec2 + GenAdj.AdjacentCells[num11];
                    if (intVec4.InBounds(currentMap) && intVec4.Walkable(currentMap) && ABBands.SameBand(currentMap, intVec4, intVec2))
                    {
                        string[] array3 = new string[5]
                        {
                            "      approach ",
                            null,
                            null,
                            null,
                            null
                        };
                        intVec3 = intVec4;
                        array3[1] = intVec3.ToString();
                        array3[2] = " island ";
                        array3[3] = ABBandComponents.ComponentOf(currentMap, intVec4, forbidAware: true).ToString();
                        array3[4] = " (diagonal, both flanks solid)";
                        stringBuilder.AppendLine(string.Concat(array3));
                    }
                }
            }

            if (num9 == 0)
            {
                stringBuilder.AppendLine("  NOTHING DESIGNATED.");
                if (num2 == 0)
                {
                    stringBuilder.AppendLine("    This band has no mineable rock at all.");
                }
                else if (num3 == num2 - num5)
                {
                    stringBuilder.AppendLine("    Every mineable cell is walled in: nothing is open down here yet. Build a link to this band and let its landing carve a pocket, then run this again - the pocket's outward corners are exactly this shape.");
                }
                else
                {
                    stringBuilder.AppendLine("    Every open approach on this band is cardinal or has a clear flank, so vanilla's corner rule never engages and #787 cannot be reproduced here. Mine a dead-end corridor and try again.");
                }
            }

            Log.Warning("[As above, So below] " + stringBuilder);
            Messages.Message("AB2: designated " + num9 + " diagonal-only mine target(s); details in the log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        private static float NearestAnchorDist(IntVec3 c, List<IntVec3> anchors)
        {
            float num = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                float lengthHorizontal = (anchors[i] - c).LengthHorizontal;
                if (lengthHorizontal < num)
                {
                    num = lengthHorizontal;
                }
            }

            return num;
        }

        [DebugAction("As above", "AB2: rock edge probe", false, false, false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2RockEdgeProbe()
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
            stringBuilder.Append("[As above, So below]").AppendLine(" V2 rock edge probe");
            ABBandMap aBBandMap = ABBands.CompOf(currentMap);
            bool flag = aBBandMap?.Banded ?? false;
            TerrainDef aB_MountainTop = ABDefOf.AB_MountainTop;
            TerrainGrid terrainGrid = currentMap.terrainGrid;
            stringBuilder.Append("CELL ").Append(intVec);
            if (flag)
            {
                stringBuilder.Append("  band=").Append(aBBandMap.BandOf(intVec)).Append(" level=").Append(aBBandMap.LevelOf(intVec)).Append(aBBandMap.InGutter(intVec) ? " IN GUTTER" : string.Empty);
            }

            stringBuilder.AppendLine();
            stringBuilder.Append("  terrain=").Append(terrainGrid.TerrainAt(intVec)?.defName ?? "null").Append(" roof=").Append(currentMap.roofGrid.RoofAt(intVec)?.defName ?? "none").Append(" fogged=").Append(currentMap.fogGrid.IsFogged(intVec)).Append(" showsBelow=").Append(ABBands.ShowsBelow(terrainGrid.TerrainAt(intVec))).AppendLine();
            Building edifice = intVec.GetEdifice(currentMap);
            if (edifice != null)
            {
                stringBuilder.Append("  edifice=").Append(edifice.def.defName).Append(" mineable=").Append(edifice.def.mineable).Append(" graphic=").Append(edifice.Graphic?.GetType().Name ?? "null");
                if (edifice.Graphic is Graphic_Linked graphic_Linked)
                {
                    int num = 0;
                    if (graphic_Linked.ShouldLinkWith(intVec + IntVec3.North, edifice))
                    {
                        num |= 1;
                    }

                    if (graphic_Linked.ShouldLinkWith(intVec + IntVec3.East, edifice))
                    {
                        num |= 2;
                    }

                    if (graphic_Linked.ShouldLinkWith(intVec + IntVec3.South, edifice))
                    {
                        num |= 4;
                    }

                    if (graphic_Linked.ShouldLinkWith(intVec + IntVec3.West, edifice))
                    {
                        num |= 8;
                    }

                    stringBuilder.Append(" spriteLinks=").Append(MaskText(num));
                }

                stringBuilder.AppendLine();
            }
            else
            {
                stringBuilder.AppendLine("  edifice=none");
            }

            bool flag2 = SectionLayer_ABMountainCap.IsMassCell(currentMap, terrainGrid, aB_MountainTop, intVec);
            IntVec3 intVec2;
            if (edifice != null && (edifice.def.mineable || (edifice.def.building != null && edifice.def.building.isNaturalRock)))
            {
                stringBuilder.AppendLine("  cap: wall cell, prints nothing (native sprite)");
            }
            else if (!flag2)
            {
                stringBuilder.AppendLine("  cap: not a mass cell");
            }
            else if (!flag || aBBandMap.BandOf(intVec) <= aBBandMap.surfaceBand || currentMap.fogGrid.IsFogged(intVec))
            {
                stringBuilder.AppendLine("  cap: not drawn here (surface/basement band, or fogged)");
            }
            else
            {
                int num2 = 0;
                if (SectionLayer_ABMountainCap.Linked(currentMap, terrainGrid, aB_MountainTop, intVec + IntVec3.North))
                {
                    num2 |= 1;
                }

                if (SectionLayer_ABMountainCap.Linked(currentMap, terrainGrid, aB_MountainTop, intVec + IntVec3.East))
                {
                    num2 |= 2;
                }

                if (SectionLayer_ABMountainCap.Linked(currentMap, terrainGrid, aB_MountainTop, intVec + IntVec3.South))
                {
                    num2 |= 4;
                }

                if (SectionLayer_ABMountainCap.Linked(currentMap, terrainGrid, aB_MountainTop, intVec + IntVec3.West))
                {
                    num2 |= 8;
                }

                bool flag3 = SectionLayer_ABMountainCap.LedgeRockTile && SectionLayer_ABMountainCap.LedgeInteriorTileFor(currentMap, aBBandMap, intVec) != null;
                if (SectionLayer_ABMountainCap.LedgeRockTile)
                {
                    stringBuilder.Append("  cap: open mass cell, option 3: ").Append(flag3 ? ("atlas tile " + MaskText(num2) + " (no field)") : "rock has no atlas (variant mode): field kept").AppendLine();
                }
                else
                {
                    stringBuilder.Append("  cap: open mass cell, field + ").Append((num2 == 15) ? "NO atlas tile (mask 15, field alone)" : ("atlas tile " + MaskText(num2))).AppendLine();
                }

                if ((flag && aBBandMap.LevelOf(intVec) > 0) & flag3)
                {
                    bool flag4 = SectionLayer_ABRockRootFill.TryPickLedgeGround(currentMap, aBBandMap, terrainGrid, aB_MountainTop, intVec, out var def, out var sample, out var fromDrop);
                    StringBuilder stringBuilder2 = stringBuilder.Append("  rootFill ledge: ground=");
                    string value;
                    if (!flag4)
                    {
                        value = "none (no unlinked neighbour, or nothing drawable)";
                    }
                    else
                    {
                        string defName = def.defName;
                        intVec2 = sample;
                        value = defName + " from " + intVec2.ToString() + (fromDrop ? " (drop tone)" : "");
                    }

                    stringBuilder2.Append(value).AppendLine();
                }
            }

            if ((edifice?.def.mineable ?? false) & flag)
            {
                bool flag5 = SectionLayer_ABRockRootFill.IsExposedEdge(currentMap, aBBandMap, banded: true, currentMap.edificeGrid.InnerArray, currentMap.cellIndices, currentMap.roofGrid, intVec);
                stringBuilder.Append("  rootFill: exposedEdge=").Append(flag5);
                if (flag5 && aBBandMap.LevelOf(intVec) > 0 && terrainGrid.TerrainAt(intVec) == aB_MountainTop)
                {
                    bool flag6 = SectionLayer_ABRockRootFill.TryPickGround(currentMap, aBBandMap, terrainGrid, edifice, intVec, out var def2, out var sample2, out var tile, out var fromDrop2);
                    StringBuilder stringBuilder3 = stringBuilder.Append(" ground=");
                    string value2;
                    if (flag6)
                    {
                        if (!(tile != null))
                        {
                            string defName2 = def2.defName;
                            string text;
                            if (!sample2.IsValid)
                            {
                                text = " (rock's own rough stone)";
                            }
                            else
                            {
                                intVec2 = sample2;
                                text = " from " + intVec2.ToString();
                            }

                            value2 = defName2 + text + (fromDrop2 ? " (drop tone)" : "");
                        }
                        else
                        {
                            string name = tile.name;
                            intVec2 = sample2;
                            value2 = "ledge rock tile " + name + " from " + intVec2.ToString();
                        }
                    }
                    else
                    {
                        value2 = "NONE (no usable neighbour)";
                    }

                    stringBuilder3.Append(value2);
                }
                else
                {
                    stringBuilder.Append(" ground=not applicable");
                }

                stringBuilder.Append(" litAsOpenEdge=").Append(SectionLayer_ABRockRootFill.LitAsOpenEdge(currentMap, aBBandMap, currentMap.cellIndices.CellToIndex(intVec)));
                stringBuilder.AppendLine();
            }

            stringBuilder.AppendLine("  3x3 (N up; W wall, L ledge, A air, G ground; lower=fogged, *=roofed):");
            for (int num3 = 1; num3 >= -1; num3--)
            {
                stringBuilder.Append("    ");
                for (int i = -1; i <= 1; i++)
                {
                    IntVec3 n = intVec + new IntVec3(i, 0, num3);
                    stringBuilder.Append(CellCode(currentMap, terrainGrid, aB_MountainTop, n)).Append(' ');
                }

                stringBuilder.AppendLine();
            }

            if (flag && ABBands.ShowsBelow(terrainGrid.TerrainAt(intVec)))
            {
                stringBuilder.AppendLine("  see-below column:");
                IntVec3 intVec3 = intVec;
                for (int j = 1; j <= 3; j++)
                {
                    intVec3 += new IntVec3(0, 0, -aBBandMap.Slot);
                    if (!intVec3.InBounds(currentMap) || aBBandMap.InGutter(intVec3))
                    {
                        break;
                    }

                    TerrainDef terrainDef = terrainGrid.TerrainAt(intVec3);
                    Building edifice2 = intVec3.GetEdifice(currentMap);
                    stringBuilder.Append("    -").Append(j).Append(' ').Append(intVec3).Append(" level=").Append(aBBandMap.LevelOf(intVec3)).Append(" terrain=").Append(terrainDef?.defName ?? "null").Append(" edifice=").Append(edifice2?.def.defName ?? "none").Append(" fogged=").Append(currentMap.fogGrid.IsFogged(intVec3)).Append(" mass=").Append(SectionLayer_ABMountainCap.IsMassCell(currentMap, terrainGrid, aB_MountainTop, intVec3)).Append(" capMask=").Append(MaskText(CapMask(currentMap, terrainGrid, aB_MountainTop, intVec3))).Append(" massMask=").Append(MaskText(SectionLayer_ABMountainCap.MassMaskForProbe(currentMap, intVec3))).AppendLine();
                    if (!ABBands.ShowsBelow(terrainDef))
                    {
                        break;
                    }
                }
            }

            stringBuilder.Append("  flags: ledgeRockTile=").Append(SectionLayer_ABMountainCap.LedgeRockTile).Append(" cornerFillers=").Append(SectionLayer_ABMountainCap.CornerFillersEnabled).Append(" stoneBorder=").Append(SectionLayer_ABMountainCap.StoneBorderRule).Append(" southStretch=").Append(SectionLayer_ABMountainCap.SouthFillerStretch).Append(" massFieldFade=").Append(SectionLayer_ABMountainCap.MassFieldFadeEnabled).Append(" rockRootFill=").Append(SectionLayer_ABRockRootFill.Enabled).AppendLine();
            Log.Warning(stringBuilder.ToString());
            intVec2 = intVec;
            Messages.Message("AB2: rock edge probe logged " + intVec2.ToString(), MessageTypeDefOf.TaskCompletion, historical: false);
        }

        private static int CapMask(Map map, TerrainGrid grid, TerrainDef cap, IntVec3 c)
        {
            int num = 0;
            if (SectionLayer_ABMountainCap.Linked(map, grid, cap, c + IntVec3.North))
            {
                num |= 1;
            }

            if (SectionLayer_ABMountainCap.Linked(map, grid, cap, c + IntVec3.East))
            {
                num |= 2;
            }

            if (SectionLayer_ABMountainCap.Linked(map, grid, cap, c + IntVec3.South))
            {
                num |= 4;
            }

            if (SectionLayer_ABMountainCap.Linked(map, grid, cap, c + IntVec3.West))
            {
                num |= 8;
            }

            return num;
        }

        private static string MaskText(int mask)
        {
            return mask + " (" + (((mask & 1) != 0) ? "N" : "") + (((mask & 2) != 0) ? "E" : "") + (((mask & 4) != 0) ? "S" : "") + (((mask & 8) != 0) ? "W" : "") + ")";
        }

        private static string CellCode(Map map, TerrainGrid grid, TerrainDef cap, IntVec3 n)
        {
            if (!n.InBounds(map))
            {
                return "## ";
            }

            Building edifice = n.GetEdifice(map);
            TerrainDef terrainDef = grid.TerrainAt(n);
            string text;
            if (edifice != null && edifice.def.mineable)
            {
                text = "W";
            }
            else if (terrainDef == cap)
            {
                text = "L";
            }
            else
            {
                text = (ABBands.ShowsBelow(terrainDef) ? "A" : "G");
            }

            if (map.fogGrid.IsFogged(n))
            {
                text = text.ToLowerInvariant();
            }

            return text + (map.roofGrid.Roofed(n) ? "*" : " ");
        }

#if RIMWORLD_17
        [DebugAction("As above", "AB2: selection draw probe", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2SelectionDrawProbe()
        {
            Map currentMap = Find.CurrentMap;
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("[As above, So below]").Append(" SELECTION DRAW PROBE (view band ").Append(ABBandView.CurrentBand(currentMap)).Append(", view rect ").Append(Find.CameraDriver.CurrentViewRect).Append(", camera ").Append(Find.Camera.transform.position).Append(")");
            stringBuilder.Append("\n  bracket transpiler: ").Append(Patch_SelectionBrackets_ABBelow.rewritten).Append(" call(s) rewritten in SelectionDrawer.DrawSelectionBracketFor, pivot reads lifted: ").Append(Patch_SelectionBrackets_ABBelow.pivotsLifted);
            stringBuilder.Append("\n  bracket lift (draw path): ").Append(Patch_SelectionBrackets_ABBelow.calls).Append(" call(s), ").Append(Patch_SelectionBrackets_ABBelow.localized).Append(" lifted; last ").Append(Patch_SelectionBrackets_ABBelow.lastIn).Append(" -> ").Append(Patch_SelectionBrackets_ABBelow.lastOut);
            Vector3[] array = new Vector3[4];
            Dictionary<object, float> selectTimes = new Dictionary<object, float>();
            int calls = Patch_SelectionBrackets_ABBelow.calls;
            int localized = Patch_SelectionBrackets_ABBelow.localized;
            Vector3 lastIn = Patch_SelectionBrackets_ABBelow.lastIn;
            Vector3 lastOut = Patch_SelectionBrackets_ABBelow.lastOut;
            int num = calls - probeSeenCalls;
            probeSeenCalls = calls;
            try
            {
                foreach (object selectedObject in Find.Selector.SelectedObjects)
                {
                    if (selectedObject is Thing { Spawned: not false } thing)
                    {
                        bool flag = ABBelowSelectionDraw.TryLocalize(currentMap, thing.DrawPos, out var localized2);
                        Patch_SelectionBrackets_ABBelow.Calc(array, thing, thing.DrawPos, thing.RotatedSize.ToVector2(), selectTimes, Vector2.one, 1f, 1f);
                        stringBuilder.Append("\n  selected ").Append(thing.LabelShort).Append(" at ").Append(thing.Position).Append(" band ").Append(ABBands.CompOf(currentMap)?.BandOf(thing.Position)).Append(", DrawPos ").Append(thing.DrawPos).Append(", TryLocalize ").Append(flag ? ("-> " + localized2) : "no").Append(", bracket[0] ").Append(array[0]).Append(Find.CameraDriver.CurrentViewRect.Contains(array[0].ToIntVec3()) ? " (in view)" : " (OUT OF VIEW)");
                    }
                    else
                    {
                        stringBuilder.Append("\n  selected ").Append(selectedObject).Append(": not a spawned thing");
                    }
                }
            }
            finally
            {
                Patch_SelectionBrackets_ABBelow.calls = calls;
                Patch_SelectionBrackets_ABBelow.localized = localized;
                Patch_SelectionBrackets_ABBelow.lastIn = lastIn;
                Patch_SelectionBrackets_ABBelow.lastOut = lastOut;
            }

            stringBuilder.Append("\n  the bracket drawer reached the lift ").Append(num).Append(" time(s) since the previous probe").Append((num == 0) ? " (NEVER: brackets are not being lifted)" : "");
            stringBuilder.Append("\n  ").Append(ABMouseDescend.CounterReport());
            Log.Warning(stringBuilder.ToString());
            Messages.Message("AB2: selection draw probe written to log.", MessageTypeDefOf.TaskCompletion, historical: false);
        }
#endif

#if RIMWORLD_17
        [DebugAction("As above", "AB2: check pollution count cache", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void V2CheckPollutionCount()
        {
            Map currentMap = Find.CurrentMap;
            long num = ABPerfStats.Now();
            int count = currentMap.pollutionGrid.AllPollutableCells.Count;
            long ticks = ABPerfStats.Now() - num;
            ABPollutionCount.Get(currentMap);
            num = ABPerfStats.Now();
            int num2 = ABPollutionCount.Get(currentMap);
            long ticks2 = ABPerfStats.Now() - num;
            Log.Warning("[As above, So below] pollutable cells: vanilla scan " + count + " in " + ABPerfStats.MsOf(ticks).ToString("0.000") + " ms, cached " + num2 + " in " + ABPerfStats.MsOf(ticks2).ToString("0.000") + " ms" + ((count == num2) ? " (MATCH)" : " (MISMATCH)"));
            Messages.Message("AB2: pollution count " + ((count == num2) ? "matches" : "MISMATCH") + ", see log.", (count == num2) ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, historical: false);
        }

#endif
    }
}
