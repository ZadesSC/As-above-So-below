using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
[StaticConstructorOnStartup]
public class SectionLayer_ABRockRootFill : SectionLayer
{
    private const string ShadowFixId = "Antisodium.shadowfix";
    private static bool broken;
    private static int shadowFixState;
    private static int groundQueue = -1;
    private static Material shadeMat;
    private static readonly Dictionary<Material, Material> groundClones = new Dictionary<Material, Material>();
    private static readonly IntVec3[] Search = new IntVec3[8]
    {
        IntVec3.South,
        IntVec3.East,
        IntVec3.North,
        IntVec3.West,
        IntVec3.South + IntVec3.East,
        IntVec3.North + IntVec3.East,
        IntVec3.South + IntVec3.West,
        IntVec3.North + IntVec3.West
    };
    private LayerSubMesh shadeSub;
    private Color32 lastShade;
    private bool shadeDirty;
    internal static bool Enabled = true;
    private static readonly Dictionary<Material, Material> tileClones = new Dictionary<Material, Material>();
    private static readonly Color32 White = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
    public override bool Visible => !broken && ABGuard.On(ABGuard.Rendering);

    internal static bool ShadowFixActive
    {
        get
        {
            if (shadowFixState == 0)
            {
                shadowFixState = ((!ModsConfig.IsActive("Antisodium.shadowfix") && !ModsConfig.IsActive("Antisodium.shadowfix_steam")) ? 1 : 2);
            }

            return shadowFixState == 2;
        }
    }

    private static int GroundQueue
    {
        get
        {
            if (groundQueue < 0)
            {
                Material material = ABDefOf.AB_MountainTop?.graphic?.MatSingle;
                int num = ((material != null && material.renderQueue >= 500) ? material.renderQueue : 2231);
                groundQueue = Mathf.Min(num + 1, CutoutQueue - 2);
            }

            return groundQueue;
        }
    }

    private static int CutoutQueue => (ShaderDatabase.Cutout != null && ShaderDatabase.Cutout.renderQueue >= 500) ? ShaderDatabase.Cutout.renderQueue : 2450;
    private static int ShadeQueue => Mathf.Max(GroundQueue + 1, CutoutQueue - 1);

    private static Material ShadeMat
    {
        get
        {
            if (shadeMat == null && MatBases.EdgeShadow != null)
            {
                shadeMat = new Material(MatBases.EdgeShadow)
                {
                    name = "AB_RockRootShade",
                    renderQueue = ShadeQueue
                };
            }

            return shadeMat;
        }
    }

    private static Color32 DropTone => SectionLayer_ABMountainCap.EdgeShadowed;

    public SectionLayer_ABRockRootFill(Section section) : base(section)
    {
        relevantChangeTypes = (ulong)MapMeshFlagDefOf.Buildings | (ulong)MapMeshFlagDefOf.Terrain | (ulong)MapMeshFlagDefOf.Roofs | (ulong)MapMeshFlagDefOf.FogOfWar | (ulong)ABDefOf.AB_BelowThings;
    }

    private static bool ShadowsOn(Map map)
    {
        int result;
        if (DebugViewSettings.drawShadows && map != null && map.info != null && !map.info.disableSunShadows)
        {
            BiomeDef biome = map.Biome;
            result = ((biome == null || !biome.disableShadows) ? 1 : 0);
        }
        else
        {
            result = 0;
        }

        return (byte)result != 0;
    }

    private static Material GroundClone(Material source)
    {
        if (source == null)
        {
            return null;
        }

        if (groundClones.TryGetValue(source, out var value))
        {
            return value;
        }

        value = new Material(source)
        {
            renderQueue = GroundQueue
        };
        groundClones[source] = value;
        return value;
    }

    private static Material TileClone(Material source)
    {
        if (source == null)
        {
            return null;
        }

        if (tileClones.TryGetValue(source, out var value))
        {
            return value;
        }

        value = new Material(source)
        {
            renderQueue = GroundQueue
        };
        tileClones[source] = value;
        return value;
    }

    private static Color32 CurrentShade()
    {
        Color32 result = MatBases.SunShadow.color;
        result.a = byte.MaxValue;
        return result;
    }

    public override void Regenerate()
    {
        ClearSubMeshes(MeshParts.All);
        shadeSub = null;
        shadeDirty = true;
        if (broken || !ABGuard.On(ABGuard.Rendering))
        {
            return;
        }

        Map map = section.map;
        try
        {
            ABBandMap aBBandMap = ABBands.CompOf(map);
            bool flag = aBBandMap?.Banded ?? false;
            int num;
            if (!map.info.disableSunShadows)
            {
                BiomeDef biome = map.Biome;
                num = ((biome == null || !biome.disableShadows) ? 1 : 0);
            }
            else
            {
                num = 0;
            }

            bool flag2 = (byte)num != 0;
            bool shadowFixActive = ShadowFixActive;
            Building[] innerArray = map.edificeGrid.InnerArray;
            CellIndices cellIndices = map.cellIndices;
            RoofGrid roofGrid = map.roofGrid;
            FogGrid fogGrid = map.fogGrid;
            TerrainGrid terrainGrid = map.terrainGrid;
            TerrainDef aB_MountainTop = ABDefOf.AB_MountainTop;
            float y = AltitudeLayer.TerrainScatter.AltitudeFor();
            float y2 = AltitudeLayer.Shadows.AltitudeFor();
            Color32 color = CurrentShade();
            bool flag3 = false;
            CellRect cellRect = new CellRect(section.botLeft.x, section.botLeft.z, 17, 17);
            cellRect.ClipInsideMap(map);
            for (int i = cellRect.minX; i <= cellRect.maxX; i++)
            {
                for (int j = cellRect.minZ; j <= cellRect.maxZ; j++)
                {
                    IntVec3 intVec = new IntVec3(i, 0, j);
                    Building building = innerArray[cellIndices.CellToIndex(intVec)];
                    if (fogGrid.IsFogged(intVec) || (flag && aBBandMap.InGutter(intVec)))
                    {
                        continue;
                    }

                    bool flag4 = building != null && (building.def.mineable || (building.def.building != null && building.def.building.isNaturalRock));
                    if ((!flag4 & flag) && SectionLayer_ABMountainCap.LedgeRockTile && aBBandMap.LevelOf(intVec) > 0 && IsOpenLedge(terrainGrid.TerrainAt(intVec), aB_MountainTop) && TryPickLedgeGround(map, aBBandMap, terrainGrid, aB_MountainTop, intVec, out var def, out var sample, out var fromDrop) && SectionLayer_ABMountainCap.LedgeInteriorTileFor(map, aBBandMap, intVec) != null)
                    {
                        Material material = terrainGrid.GetMaterial(def, sample.IsValid && sample.IsPolluted(map) && def.graphicPolluted != BaseContent.BadGraphic, sample.IsValid ? terrainGrid.ColorAt(sample) : null);
                        Material material2 = GroundClone(material);
                        if (material2 != null)
                        {
                            AddQuad(GetSubMesh(material2), intVec, y, fromDrop ? DropTone : White);
                            flag3 = true;
                        }
                    }

                    if (building == null)
                    {
                        continue;
                    }

                    bool mineable = building.def.mineable;
                    bool flag5 = roofGrid.Roofed(intVec);
                    if ((mineable & flag) && aBBandMap.LevelOf(intVec) > 0 && terrainGrid.TerrainAt(intVec) == aB_MountainTop && IsExposedEdge(map, aBBandMap, flag, innerArray, cellIndices, roofGrid, intVec) && TryPickGround(map, aBBandMap, terrainGrid, building, intVec, out var def2, out var sample2, out var tile, out var fromDrop2))
                    {
                        if (tile != null)
                        {
                            Material material3 = TileClone(tile);
                            if (material3 != null)
                            {
                                SectionLayer_ABMountainCap.AddInteriorTileWithFillers(GetSubMesh(material3), map, intVec, y, White);
                                flag3 = true;
                            }
                        }
                        else
                        {
                            Material material4 = terrainGrid.GetMaterial(def2, sample2.IsValid && sample2.IsPolluted(map) && def2.graphicPolluted != BaseContent.BadGraphic, sample2.IsValid ? terrainGrid.ColorAt(sample2) : null);
                            Material material5 = GroundClone(material4);
                            if (material5 != null)
                            {
                                AddQuad(GetSubMesh(material5), intVec, y, fromDrop2 ? DropTone : White);
                                flag3 = true;
                            }
                        }
                    }

                    if (!flag2 || !(building.def.staticSunShadowHeight > 0f) || building.def.Fillage != FillCategory.Full || building.def.IsDoor)
                    {
                        continue;
                    }

                    bool flag6 = shadowFixActive && !flag5;
                    if (flag6 | flag5)
                    {
                        continue;
                    }

                    Material material6 = ShadeMat;
                    if (!(material6 == null))
                    {
                        if (shadeSub == null)
                        {
                            shadeSub = GetSubMesh(material6);
                        }

                        AddQuad(shadeSub, intVec, y2, color);
                        flag3 = true;
                    }
                }
            }

            if (flag3)
            {
                FinalizeMesh(MeshParts.All);
                lastShade = color;
                shadeDirty = false;
            }
        }
        catch (Exception ex)
        {
            broken = true;
            ClearSubMeshes(MeshParts.All);
            shadeSub = null;
            Log.Error("[As above, So below] Rock root fill failed and is off for this session (rock corners fall back to vanilla). Details: " + ex);
        }
    }

    public override void DrawLayer()
    {
        if (!Enabled || !Visible)
        {
            return;
        }

        if (shadeSub != null && shadeSub.finalized)
        {
            shadeSub.disabled = !ShadowsOn(section.map);
            if (!shadeSub.disabled)
            {
                UpdateShade();
            }
        }

        base.DrawLayer();
    }

    private void UpdateShade()
    {
        List<Color32> colors = shadeSub.colors;
        if (colors == null || colors.Count == 0)
        {
            return;
        }

        Color32 value = CurrentShade();
        if (shadeDirty || value.r != lastShade.r || value.g != lastShade.g || value.b != lastShade.b)
        {
            shadeDirty = false;
            lastShade = value;
            for (int i = 0; i < colors.Count; i++)
            {
                colors[i] = value;
            }

            shadeSub.mesh.SetColors(colors);
        }
    }

    internal static bool IsExposedEdge(Map map, ABBandMap bands, bool banded, Building[] edifices, CellIndices indices, RoofGrid roofs, IntVec3 c)
    {
        int num = (banded ? bands.BandOf(c) : 0);
        for (int i = 0; i < Search.Length; i++)
        {
            IntVec3 c2 = c + Search[i];
            if (c2.InBounds(map) && (!banded || (!bands.InGutter(c2) && bands.BandOf(c2) == num)))
            {
                Building building = edifices[indices.CellToIndex(c2)];
                if ((building == null || !building.def.mineable) && !roofs.Roofed(c2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOpenLedge(TerrainDef t, TerrainDef mountainTop)
    {
        ThingDef rockDef;
        return t != null && (t == mountainTop || ABMinedRockLookup.TryGetMinedRockDef(t, out rockDef));
    }

    internal static bool TryPickLedgeGround(Map map, ABBandMap bands, TerrainGrid terrain, TerrainDef mountainTop, IntVec3 c, out TerrainDef def, out IntVec3 sample, out bool fromDrop)
    {
        def = null;
        sample = IntVec3.Invalid;
        fromDrop = false;
        int num = bands.BandOf(c);
        for (int i = 0; i < Search.Length; i++)
        {
            IntVec3 intVec = c + Search[i];
            if (!intVec.InBounds(map) || bands.InGutter(intVec) || bands.BandOf(intVec) != num || SectionLayer_ABMountainCap.Linked(map, terrain, mountainTop, intVec))
            {
                continue;
            }

            TerrainDef terrainDef = terrain.TerrainAt(intVec);
            if (terrainDef == null)
            {
                continue;
            }

            if (ABBands.ShowsBelow(terrainDef))
            {
                if (TryGroundSeenThrough(map, bands, terrain, mountainTop, c, intVec, null, out def, out sample))
                {
                    fromDrop = true;
                    return true;
                }
            }
            else if (!terrainDef.dontRender && !terrainDef.IsWater)
            {
                def = terrainDef;
                sample = intVec;
                return true;
            }
        }

        return false;
    }

    internal static bool TryPickGround(Map map, ABBandMap bands, TerrainGrid terrain, Building b, IntVec3 c, out TerrainDef def, out IntVec3 sample, out Material tile, out bool fromDrop)
    {
        def = null;
        sample = IntVec3.Invalid;
        tile = null;
        fromDrop = false;
        TerrainDef aB_MountainTop = ABDefOf.AB_MountainTop;
        TerrainDef terrainDef = b.def.building?.naturalTerrain;
        int num = bands.BandOf(c);
        for (int i = 0; i < Search.Length; i++)
        {
            IntVec3 intVec = c + Search[i];
            if (!intVec.InBounds(map) || bands.InGutter(intVec) || bands.BandOf(intVec) != num)
            {
                continue;
            }

            Building edifice = intVec.GetEdifice(map);
            if (edifice != null && edifice.def.mineable)
            {
                continue;
            }

            TerrainDef terrainDef2 = terrain.TerrainAt(intVec);
            if (terrainDef2 == null)
            {
                continue;
            }

            if (ABBands.ShowsBelow(terrainDef2))
            {
                if (TryGroundSeenThrough(map, bands, terrain, aB_MountainTop, c, intVec, terrainDef, out def, out sample))
                {
                    fromDrop = true;
                    return true;
                }

                continue;
            }

            if (IsOpenLedge(terrainDef2, aB_MountainTop))
            {
                Material material = SectionLayer_ABMountainCap.LedgeInteriorTileFor(map, bands, intVec);
                if (material != null)
                {
                    tile = material;
                    sample = intVec;
                    return true;
                }
            }

            if (terrainDef2 == aB_MountainTop)
            {
                if (terrainDef != null)
                {
                    def = terrainDef;
                    sample = IntVec3.Invalid;
                    return true;
                }
            }
            else if (!terrainDef2.dontRender && !terrainDef2.IsWater)
            {
                def = terrainDef2;
                sample = intVec;
                return true;
            }
        }

        return false;
    }

    private static bool TryGroundSeenThrough(Map map, ABBandMap bands, TerrainGrid terrain, TerrainDef mountainTop, IntVec3 c, IntVec3 n, TerrainDef rough, out TerrainDef def, out IntVec3 sample)
    {
        def = null;
        sample = IntVec3.Invalid;
        if (!ABBands.TryResolveVisibleFrom(map, bands, n, requireUnfogged: true, out var below, out var drop))
        {
            return false;
        }

        TerrainDef terrainDef = terrain.TerrainAt(below);
        if (terrainDef != null && !terrainDef.dontRender && !terrainDef.IsWater && terrainDef != mountainTop)
        {
            def = terrainDef;
            sample = below;
            return true;
        }

        IntVec3 intVec = new IntVec3(c.x, 0, c.z - drop);
        if (!intVec.InBounds(map) || bands.InGutter(intVec) || map.fogGrid.IsFogged(intVec))
        {
            return false;
        }

        TerrainDef terrainDef2 = terrain.TerrainAt(intVec);
        if (terrainDef2 == mountainTop && rough != null)
        {
            def = rough;
            sample = IntVec3.Invalid;
            return true;
        }

        if (terrainDef2 != null && !terrainDef2.dontRender && !terrainDef2.IsWater && terrainDef2 != mountainTop)
        {
            def = terrainDef2;
            sample = intVec;
            return true;
        }

        return false;
    }

    internal static bool LitAsOpenEdge(Map map, ABBandMap bands, int idx)
    {
        if (!Enabled || bands == null || !bands.Banded)
        {
            return false;
        }

        Building[] innerArray = map.edificeGrid.InnerArray;
        Building building = innerArray[idx];
        if (building == null || !building.def.mineable)
        {
            return false;
        }

        CellIndices cellIndices = map.cellIndices;
        IntVec3 c = cellIndices.IndexToCell(idx);
        if (bands.InGutter(c) || bands.LevelOf(c) <= 0)
        {
            return false;
        }

        return IsExposedEdge(map, bands, banded: true, innerArray, cellIndices, map.roofGrid, c);
    }

    private static void AddQuad(LayerSubMesh sub, IntVec3 c, float y, Color32 color)
    {
        if (sub != null)
        {
            int count = sub.verts.Count;
            sub.verts.Add(new Vector3(c.x, y, c.z));
            sub.verts.Add(new Vector3(c.x, y, c.z + 1));
            sub.verts.Add(new Vector3(c.x + 1, y, c.z + 1));
            sub.verts.Add(new Vector3(c.x + 1, y, c.z));
            sub.colors.Add(color);
            sub.colors.Add(color);
            sub.colors.Add(color);
            sub.colors.Add(color);
            sub.tris.Add(count);
            sub.tris.Add(count + 1);
            sub.tris.Add(count + 2);
            sub.tris.Add(count);
            sub.tris.Add(count + 2);
            sub.tris.Add(count + 3);
        }
    }
}
