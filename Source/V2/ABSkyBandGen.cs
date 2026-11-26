using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 sky-band terrain: a real mountain, not a flat projection.
    ///
    /// Ports V1's GenStep_ABSkyTerrain classification onto a band rect. The mass is
    /// projected from the band directly below (same Map, so one subtraction instead of a
    /// cross-map lookup plus a sync mirror), then classified:
    ///
    ///   solid      = rockish cells (mineable edifice below, or thick natural roof
    ///                below) that are 8-way CONNECTED to a thick-natural-roof seed -
    ///                see pass 1 for why raw rockish alone is not the mass
    ///   edgeDist   = 8-way BFS distance out of the mass
    ///   meadow field (one low-frequency Perlin) decides rock vs open per cell:
    ///     open  -> KindPlateau (walkable mountain top), except the outermost ring which
    ///              stays KindLedge so the rock face below still renders
    ///     rock  -> KindLedge within a wandering terrace width, KindWall behind it
    ///
    /// THE LEDGE BAND IS >= 1 CELL BY CONSTRUCTION (option A's requirement, and it holds
    /// without a special case): a plateau cell requires edgeDist >= 2, and TerraceWidth()
    /// never returns less than 1, so no plateau or wall cell can ever touch a non-mass
    /// cell - there is always at least one ledge cell between the mass interior and the
    /// drop. The renderer leans on this: the rim is what carries the silhouette lip and
    /// the cliff face.
    ///
    /// Terrain then follows V1 exactly: ledge = mountain-top lip, wall = mineable rock
    /// under thick roof (deep interior fogged like an unexplored vanilla mountain),
    /// plateau = stone/gravel/soil by noise. Outside the mass: constructed roof below ->
    /// rooftop, natural roof below -> mountain cap, nothing below -> open air (which is
    /// what SectionLayer_ABBelowV2 renders the level below through).
    ///
    /// NOT PORTED from V1: outcrop mounts and hidden-valley breaching. Both are additive
    /// polish on top of this classification and can be layered in later.
    /// </summary>
    internal static class ABSkyBandGen
    {
        private const byte KindOutside = 0;
        private const byte KindLedge = 1;
        private const byte KindWall = 2;
        private const byte KindPlateau = 3;

        private const float MeadowCutoffDefault = 0.62f;

        internal static void Generate(Map map, ABBandMap bands, int band, List<ThingDef> rocks, List<Perlin> noises)
        {
            CellRect cellRect = bands.RectOfBand(band);
            int width = cellRect.Width;
            int height = cellRect.Height;
            int num = width * height;
            int slot = bands.Slot;
            TerrainGrid terrainGrid = map.terrainGrid;
            RoofGrid roofGrid = map.roofGrid;
            TerrainDef aB_OpenAir = ABDefOf.AB_OpenAir;
            bool[] array = new bool[num];
            bool[] array2 = new bool[num];
            bool[] array3 = new bool[num];
            bool flag = false;
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    IntVec3 intVec = new IntVec3(cellRect.minX + j, 0, cellRect.minZ + i - slot);
                    if (intVec.InBounds(map))
                    {
                        Building building = map.edificeGrid[intVec];
                        RoofDef roofDef = roofGrid.RoofAt(intVec);
                        bool flag2 = roofDef != null && roofDef.isNatural && roofDef.isThickRoof;
                        int num2 = i * width + j;
                        array[num2] = flag2 || (building?.def.mineable ?? false);
                        if (BuiltBelow(map, intVec, building, roofDef))
                        {
                            array3[num2] = true;
                            flag = true;
                        }

                        if (flag2)
                        {
                            array2[num2] = true;
                            queue.Enqueue(num2);
                        }
                    }
                }
            }

            while (queue.Count > 0)
            {
                int num3 = queue.Dequeue();
                int num4 = num3 % width;
                int num5 = num3 / width;
                for (int k = -1; k <= 1; k++)
                {
                    for (int l = -1; l <= 1; l++)
                    {
                        if (l == 0 && k == 0)
                        {
                            continue;
                        }

                        int num6 = num4 + l;
                        int num7 = num5 + k;
                        if (num6 >= 0 && num6 < width && num7 >= 0 && num7 < height)
                        {
                            int num8 = num7 * width + num6;
                            if (array[num8] && !array2[num8])
                            {
                                array2[num8] = true;
                                queue.Enqueue(num8);
                            }
                        }
                    }
                }
            }

            bool[] array4 = null;
            if (flag && !ABOrbitRules.InVacuum(map))
            {
                bool[] array5 = Dilate(array3, width, height, 1);
                for (int m = 0; m < height; m++)
                {
                    for (int n = 0; n < width; n++)
                    {
                        int num9 = m * width + n;
                        if (!array5[num9])
                        {
                            continue;
                        }

                        IntVec3 intVec2 = new IntVec3(cellRect.minX + n, 0, cellRect.minZ + m - slot);
                        if (intVec2.InBounds(map) && roofGrid.RoofAt(intVec2) != null && PregenRoofOver(map, intVec2))
                        {
                            if (array4 == null)
                            {
                                array4 = new bool[num];
                            }

                            array4[num9] = true;
                            array2[num9] = false;
                        }
                    }
                }
            }

            bool[] noMeadow = (flag ? Dilate(array3, width, height, 2) : null);
            byte[] array6 = Classify(array2, noMeadow, width, height, out var edgeDistOut);
            bool[] array7 = ComputeFogMask(array6, width, height);
            ABSettings settings = ABMod.Settings;
            float num10 = Mathf.Clamp((settings != null) ? settings.peakSoilFraction : 0.15f, 0f, 0.5f);
            Perlin noise = new Perlin(0.05, 2.0, 0.5, 5, Rand.Range(0, int.MaxValue), QualityMode.Medium);
            Perlin noise2 = new Perlin(0.06, 2.0, 0.5, 4, Rand.Range(0, int.MaxValue), QualityMode.Medium);
            BiomeDef biomeDef = ABBandRocks.BiomeOfBand(map, bands, band);
            TerrainDef terrainDef = ArableTerrainFor(biomeDef ?? map.Biome);
            TerrainDef terrainDef2 = ABBandRocks.GravelFor(map, biomeDef);
            List<IntVec3> list = new List<IntVec3>();
            List<IntVec3> list2 = new List<IntVec3>();
            List<IntVec3> list3 = new List<IntVec3>();
            for (int num11 = 0; num11 < height; num11++)
            {
                for (int num12 = 0; num12 < width; num12++)
                {
                    int num13 = num11 * width + num12;
                    IntVec3 intVec3 = new IntVec3(cellRect.minX + num12, 0, cellRect.minZ + num11);
                    if (!intVec3.InBounds(map))
                    {
                        continue;
                    }

                    roofGrid.SetRoof(intVec3, null);
                    switch (array6[num13])
                    {
                        case 1:
                            terrainGrid.SetTerrain(intVec3, ABDefOf.AB_MountainTop);
                            continue;
                        case 2:
                        {
                            ThingDef def = rocks[ABRockGen.PickIndex(noises, intVec3)];
                            terrainGrid.SetTerrain(intVec3, ABDefOf.AB_MountainTop);
                            GenSpawn.Spawn(def, intVec3, map);
                            roofGrid.SetRoof(intVec3, RoofDefOf.RoofRockThick);
                            list.Add(intVec3);
                            if (array7[num13])
                            {
                                list2.Add(intVec3);
                            }

                            continue;
                        }

                        case 3:
                        {
                            float num14 = Noise01(noise, intVec3);
                            float num15 = Noise01(noise2, intVec3);
                            TerrainDef terrainDef3;
                            if (!(num14 < 0.22f))
                            {
                                if (num14 < 0.4f)
                                {
                                    terrainDef3 = terrainDef2;
                                }
                                else if (num14 > 1f - num10)
                                {
                                    terrainDef3 = ((num15 > 0.8f) ? (BiomeGroundAt(biomeDef, 0.95f) ?? TerrainDefOf.SoilRich ?? terrainDef) : terrainDef);
                                }
                                else
                                {
                                    terrainDef3 = ((num15 > 0.9f) ? (TerrainDefOf.Mud ?? TerrainDefOf.Soil) : (BiomeGroundAt(biomeDef, 0.6f) ?? TerrainDefOf.Soil));
                                }
                            }
                            else
                            {
                                ThingDef thingDef = rocks[ABRockGen.PickIndex(noises, intVec3)];
                                terrainDef3 = thingDef.building?.naturalTerrain ?? terrainDef2;
                            }

                            terrainGrid.SetTerrain(intVec3, terrainDef3 ?? terrainDef2);
                            list3.Add(intVec3);
                            continue;
                        }
                    }

                    IntVec3 intVec4 = new IntVec3(intVec3.x, 0, intVec3.z - slot);
                    RoofDef roofDef2 = (intVec4.InBounds(map) ? roofGrid.RoofAt(intVec4) : null);
                    if (array4 != null && array4[num13])
                    {
                        terrainGrid.SetTerrain(intVec3, ABDefOf.AB_RoofSurface);
                    }
                    else if (roofDef2 != null && !roofDef2.isNatural)
                    {
                        terrainGrid.SetTerrain(intVec3, ABOrbitRules.ProjectsUpperLevel(map, intVec4) ? ABDefOf.AB_RoofSurface : aB_OpenAir);
                    }
                    else if (roofDef2 != null && roofDef2.isNatural)
                    {
                        terrainGrid.SetTerrain(intVec3, ABDefOf.AB_MountainTop);
                    }
                    else
                    {
                        terrainGrid.SetTerrain(intVec3, aB_OpenAir);
                    }
                }
            }

            for (int num16 = 0; num16 < list2.Count; num16++)
            {
                IntVec3 intVec5 = list2[num16];
                map.fogGrid.Refog(new CellRect(intVec5.x, intVec5.z, 1, 1));
            }

            if (list.Count > 0)
            {
                ABOreGen.ScatterOres(map, list, Mathf.Clamp(settings?.basementOreDensity ?? 6f, 0f, 12f) * 0.5f);
            }

            CarveTarns(map, cellRect, width, height, array6, edgeDistOut, settings);
            ScatterRimScree(map, cellRect, width, height, array6, edgeDistOut, rocks, noises);
            if (ABBandDressing.WillDressFlora(map))
            {
                List<IntVec3> fallbackCells = list3;
                int[] fallbackEdge = edgeDistOut;
                CellRect fallbackRect = cellRect;
                int fallbackWidth = width;
                ABBandDressing.RegisterFloraFallback(band, () =>
                {
                    SeedFlora(map, fallbackCells, settings, fallbackEdge, fallbackRect, fallbackWidth);
                });
            }
            else
            {
                SeedFlora(map, list3, settings, edgeDistOut, cellRect, width);
            }
        }

        /// <summary>No tarn water within this many cells of a drop. The user's rule -
        /// "lakes should never spawn on upper levels near the edge" - and a sound one: a
        /// lake lipping over a cliff would need the whole waterfall system (option E) to
        /// make any sense, and looks broken without it.</summary>
        private const int MinTarnEdgeDist = 6;

        /// <summary>Roughly one tarn per this many eligible interior cells.</summary>
        private const int TarnCellsPerSeed = 1100;

        /// <summary>
        /// Tarns: small alpine lakes in the plateau interior, shallow water with a mud
        /// fringe (the moisture field already puts rich soil and mud nearby, so a tarn
        /// reads as the reason that ground is damp).
        ///
        /// SHALLOW ONLY, deliberately: deep water is impassable, and an impassable pocket
        /// on a plateau reachable solely through our wormhole stairs is exactly the kind
        /// of thing that turns into a stuck-pawn report. Shallow water is wadeable, so the
        /// plateau stays fully traversable.
        ///
        /// The edge rule is enforced PER CELL, not on the seed: every water cell must
        /// itself be MinTarnEdgeDist from a drop, so the blob clips itself against the rim
        /// instead of relying on radius arithmetic being right.
        /// </summary>
        private static void CarveTarns(Map map, CellRect rect, int w, int h, byte[] kind,
            int[] edgeDist, ABSettings settings)
        {
            if (settings != null && !settings.naturalPeaks)
            {
                return;
            }
            TerrainDef water = TerrainDefOf.WaterShallow;
            if (water == null)
            {
                return;
            }
            TerrainDef fringe = TerrainDefOf.Mud;
            List<int> seeds = new List<int>();
            for (int i = 0; i < kind.Length; i++)
            {
                if (kind[i] == KindPlateau && edgeDist[i] >= MinTarnEdgeDist + 2)
                {
                    seeds.Add(i);
                }
            }
            if (seeds.Count == 0)
            {
                return;
            }
            int tarns = Mathf.Clamp(seeds.Count / TarnCellsPerSeed, seeds.Count >= 400 ? 1 : 0, 4);
            if (tarns <= 0)
            {
                return;
            }
            TerrainGrid grid = map.terrainGrid;
            int carved = 0;
            for (int t = 0; t < tarns; t++)
            {
                int seed = seeds[Rand.Range(0, seeds.Count)];
                int sx = seed % w;
                int sz = seed / w;
                float radius = Rand.Range(2.2f, 4.2f);
                int reach = Mathf.CeilToInt(radius) + 2;
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int lx = sx + dx;
                        int lz = sz + dz;
                        if (lx < 0 || lz < 0 || lx >= w || lz >= h)
                        {
                            continue;
                        }
                        int idx = lz * w + lx;
                        if (kind[idx] != KindPlateau || edgeDist[idx] < MinTarnEdgeDist)
                        {
                            continue;
                        }
                        IntVec3 c = new IntVec3(rect.minX + lx, 0, rect.minZ + lz);
                        if (!c.InBounds(map))
                        {
                            continue;
                        }
                        // Deterministic per-cell jitter: an irregular shore instead of a
                        // disc, stable across regeneration.
                        float d = Mathf.Sqrt(dx * dx + dz * dz)
                            + (Rand.ValueSeeded(idx * 977 + 13) - 0.5f) * 0.9f;
                        if (d <= radius)
                        {
                            grid.SetTerrain(c, water);
                            carved++;
                        }
                        else if (fringe != null && d <= radius + 1.1f
                            && grid.TerrainAt(c) != water)
                        {
                            grid.SetTerrain(c, fringe);
                        }
                    }
                }
            }
            if (carved > 0)
            {
                ABLog.Dev("Sky tarns: " + tarns + " attempted, " + carved
                    + " shallow-water cells (all >= " + MinTarnEdgeDist + " cells from any drop).");
            }
        }

        private const float RimChunkChance = 0.014f;

        private const int MaxRimChunks = 60;

        /// <summary>
        /// Rim scree: loose stone chunks along the plateau shoulder, in the local rock
        /// type, so the edge reads as weathered rather than cut.
        ///
        /// These are real haulable chunks, which is a deliberate trade the user chose:
        /// they look right and give free stone, but every chunk is a Thing that a hauler
        /// will eventually want to carry down the stairs. Hence the hard cap and the low
        /// per-cell chance - this is scenery with a side of resource, not a quarry.
        /// Restricted to the rim shoulder (edgeDist 1-4) so the plateau interior stays
        /// clean and the chunks read as having tumbled from the edge.
        /// </summary>
        private static void ScatterRimScree(Map map, CellRect rect, int w, int h, byte[] kind,
            int[] edgeDist, List<ThingDef> rocks, List<Perlin> noises)
        {
            if (rocks == null || rocks.Count == 0)
            {
                return;
            }
            int placed = 0;
            for (int lz = 0; lz < h && placed < MaxRimChunks; lz++)
            {
                for (int lx = 0; lx < w && placed < MaxRimChunks; lx++)
                {
                    int idx = lz * w + lx;
                    byte k = kind[idx];
                    if (k != KindLedge && k != KindPlateau)
                    {
                        continue;
                    }
                    int ed = edgeDist[idx];
                    if (ed < 1 || ed > 4 || !Rand.Chance(RimChunkChance))
                    {
                        continue;
                    }
                    IntVec3 c = new IntVec3(rect.minX + lx, 0, rect.minZ + lz);
                    if (!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null)
                    {
                        continue;
                    }
                    TerrainDef t = c.GetTerrain(map);
                    if (t == null || t.IsWater)
                    {
                        continue;
                    }
                    ThingDef chunk = ChunkFor(rocks[ABRockGen.PickIndex(noises, c)]);
                    if (chunk == null)
                    {
                        continue;
                    }
                    if (GenSpawn.Spawn(chunk, c, map, WipeMode.Vanish) != null)
                    {
                        placed++;
                    }
                }
            }
            if (placed > 0)
            {
                ABLog.Dev("Sky rim scree: " + placed + " stone chunks along the plateau shoulder.");
            }
        }

        /// <summary>The chunk def for a rock type, by the "Chunk" + defName convention
        /// every stone in the game (and every stone-adding mod) follows. Deliberately NOT
        /// falling back to `building.mineableThing` - for some rocks that is blocks or an
        /// ore product, and spawning steel bars on a mountain shoulder would be worse than
        /// spawning nothing.</summary>
        private static ThingDef ChunkFor(ThingDef rock)
        {
            return rock != null
                ? DefDatabase<ThingDef>.GetNamedSilentFail("Chunk" + rock.defName)
                : null;
        }

        /// <summary>
        /// Starting vegetation for the plateau.
        ///
        /// Without this the sky band generated completely barren: nothing here spawned a
        /// single plant, so the only vegetation was whatever WildPlantSpawner trickled in
        /// over subsequent in-game months. A player climbing to a brand-new summit found
        /// bare soil and gravel.
        ///
        /// The species list comes from ABBandEnv.BiomeOf, which for the sky band resolves
        /// to the SURFACE biome - a plateau above a boreal forest should read boreal. The
        /// filtering that keeps that honest is per-cell and already vanilla's own:
        /// ABFloraPicker runs every candidate through CanEverPlantAt, so lowland and water
        /// species simply have nowhere to land on soil and gravel, and open air and roof
        /// surfaces are never candidates because they are not plateau cells at all.
        ///
        /// ⚠ THAT USED TO BE A FERTILITY TEST ONLY, WHICH WAS NOT VANILLA'S GATE. It let
        /// through anything whose fertilityMin cleared the terrain and ignored
        /// wildTerrainTags entirely, so a tagged species could be planted somewhere it
        /// immediately starts dying. The cavern band is where that was reported, but this
        /// seeder had the identical hole and is fixed the same way.
        /// </summary>
        private static void SeedFlora(Map map, List<IntVec3> plateauCells, ABSettings settings,
            int[] edgeDist, CellRect rect, int w)
        {
            if (plateauCells == null || plateauCells.Count == 0)
            {
                return;
            }
            float density = Mathf.Clamp(settings?.skyVegetationDensity ?? 1f, 0f, 2f);
            if (density <= 0f)
            {
                return;
            }
            BiomeDef biome = ABBandEnv.BiomeOf(map, plateauCells[0]);
            ABFloraPicker picker = ABFloraPicker.For(map, biome);
            if (picker == null || picker.PoolCount == 0)
            {
                return;
            }

            // Scaled by the biome's own plantDensity so a desert summit stays sparse and a
            // temperate one comes in thick, then by the player's setting.
            float chanceBase = 0.16f * Mathf.Max(0.15f, biome.plantDensity) * density;
            int placed = 0;
            for (int i = 0; i < plateauCells.Count; i++)
            {
                IntVec3 c = plateauCells[i];
                if (!c.Standable(map) || c.GetPlant(map) != null || c.GetEdifice(map) != null)
                {
                    continue;
                }
                TerrainDef t = c.GetTerrain(map);
                // Vanilla's terrain weighting: fertility, floored to 0.1 on water when the
                // biome has anything that ignores fertility, so a summit tarn can hold reeds
                // instead of being discarded for having fertility 0.
                float fertWeight = picker.FertilityWeightOn(t);
                if (fertWeight <= 0f)
                {
                    continue; // bare rock and mud flats fall out here
                }
                // ALPINE CHARACTER (option D): exposure falls off inland, so growth
                // thins toward the rim and trees effectively refuse to stand there.
                // rim01 = 0 at the shoulder, 1 well inside the plateau.
                int idx = (c.z - rect.minZ) * w + (c.x - rect.minX);
                int ed = idx >= 0 && idx < edgeDist.Length ? edgeDist[idx] : 99;
                float rim01 = Mathf.Clamp01((ed - 1) / 8f);
                float exposure = Mathf.Lerp(0.30f, 1f, rim01);
                if (!Rand.Chance(chanceBase * fertWeight * exposure))
                {
                    continue;
                }
                float treeWeight = Mathf.Lerp(0.05f, 1f, rim01);
                // The rim bias rides on the picker's weighting instead of replacing it, so
                // a thinned tree can still lose to a legal shrub rather than wasting the cell.
                ThingDef plantDef = picker.Pick(c, t, p => p.plant.IsTree ? treeWeight : 1f);
                if (plantDef == null)
                {
                    continue;
                }
                Plant plant = GenSpawn.Spawn(plantDef, c, map, WipeMode.Vanish) as Plant;
                if (plant != null)
                {
                    // Staggered growth so the summit does not look planted all at once.
                    plant.Growth = Rand.Range(0.15f, 0.95f);
                    placed++;
                }
            }
            ABLog.Dev("Sky flora: " + placed + " plants seeded across " + plateauCells.Count
                + " plateau cells (biome " + (biome.defName ?? "?") + ", density "
                + density.ToString("0.0")
                + (picker.TemperatureGateRelaxed ? ", temperature gate relaxed" : "") + ").");
        }

        /// <summary>
        /// THE REAL CAUSE OF "TEXTURE OUTLINE ERRORS", fixed at its source.
        ///
        /// Two independent noise fields (meadow and terrace) crossed at cell resolution
        /// speckle the mass into dozens of one- and two-cell wall stubs and pinhole floor
        /// pockets. Every one of those is a real wall/floor boundary, so vanilla draws it
        /// a lip, an outline and an edge shadow - correctly - and the summit ends up laced
        /// with nested rings. Hiding the wall sprites "fixed" the symptom and destroyed
        /// the wall/floor read (minable rock looked like floor, ore veins turned into
        /// glyphs); the honest fix is to stop generating the speckle.
        ///
        /// A majority filter: a wall with 6+ open neighbours dissolves into ledge, a floor
        /// cell with 6+ wall neighbours fills in. Conversions require all EIGHT neighbours
        /// to be inside the mass, which means edgeDist >= 2 - so the rim is never touched
        /// and the "ledge band >= 1 cell" invariant survives untouched. Two passes clears
        /// stubs and pinholes while leaving any 3x3-or-larger formation intact.
        /// </summary>
        private static void Despeckle(byte[] kind, bool[] solid, int w, int h, int passes)
        {
            int count = w * h;
            byte[] next = new byte[count];
            for (int p = 0; p < passes; p++)
            {
                Array.Copy(kind, next, count);
                for (int z = 0; z < h; z++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = z * w + x;
                        if (!solid[i])
                        {
                            continue;
                        }
                        int wall = 0;
                        int open = 0;
                        int inMass = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dz == 0)
                                {
                                    continue;
                                }
                                int nx = x + dx;
                                int nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= w || nz >= h)
                                {
                                    continue;
                                }
                                int n = nz * w + nx;
                                if (!solid[n])
                                {
                                    continue;
                                }
                                inMass++;
                                if (kind[n] == KindWall)
                                {
                                    wall++;
                                }
                                else
                                {
                                    open++;
                                }
                            }
                        }
                        if (inMass < 8)
                        {
                            continue; // rim cell - leave the ledge band alone
                        }
                        if (kind[i] == KindWall)
                        {
                            // 7, not 6, and the difference is visible from orbit.
                            //
                            // At 6 a wall standing on any CONVEX bend of a plateau boundary
                            // has enough open neighbours to dissolve, so two passes chewed
                            // the outermost wall row off every meadow edge and left walkable
                            // ledge floor in its place. Floor against floor gets a fade, not
                            // a lip - which is why the plateau edge stopped reading as a
                            // mountain edge even though the surface's did. At 7 only genuinely
                            // isolated stubs and one-cell spurs dissolve (the speckle this
                            // pass exists for), boundaries survive intact, and the rock walls
                            // sit right at the meadow where VANILLA draws their pale top lip
                            // and dark outline for us.
                            if (open >= 7)
                            {
                                next[i] = KindLedge;
                            }
                        }
                        else if (wall >= 6)
                        {
                            // Filling notches stays at 6: it makes boundaries cleaner rather
                            // than eroding them.
                            next[i] = KindWall;
                        }
                    }
                }
                Array.Copy(next, kind, count);
            }
        }

        /// <summary>Width of the walkable rim before solid rock begins.
        ///
        /// Ported verbatim from V1 - the shape matters enormously. HALF of all cells get
        /// width exactly 1 (n &lt; 0.5), and above that it grows on a 1.6 power curve, so
        /// wide terraces are rare and the mass is mostly ROCK.
        ///
        /// The naive `1 + floor(noise * max)` (uniform 1..5, mean ~3) made almost every
        /// cell a ledge, so no rock ever spawned and the sky read as a flat plate - the
        /// run #8 "ledges don't spawn with rock, it's all flat" report.</summary>
        private static int TerraceWidth(Perlin noise, IntVec3 c, int max)
        {
            float n = Noise01(noise, c);
            if (n < 0.5f || max <= 1)
            {
                return 1;
            }
            int w = 1 + Mathf.FloorToInt(Mathf.Pow(Mathf.InverseLerp(0.5f, 1f, n), 1.6f) * max);
            return Mathf.Clamp(w, 1, max);
        }

        /// <summary>Wall depth at which the interior fogs.</summary>
        private const int FogDepth = 3;

        /// <summary>Fog regions smaller than this are dropped outright: a handful of
        /// fogged cells buys nothing and costs another soft border.</summary>
        private const int MinFogComponent = 24;

        /// <summary>
        /// The fog mask: wall cells at least FogDepth into the rock, keeping only
        /// components big enough to be worth a border - "one centre hole" rather than a
        /// scatter of small ones, which is what multiplied the border lines before.
        ///
        /// Distance is measured out of everything that is NOT wall (open mass cells and
        /// off-mass cells alike), so a tunnel or a plateau bay pushes the fog line back
        /// exactly as a player would expect.
        /// </summary>
        private static bool[] ComputeFogMask(byte[] kind, int w, int h)
        {
            int count = w * h;
            int[] depth = new int[count];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (kind[i] == KindWall)
                {
                    depth[i] = int.MaxValue;
                }
                else
                {
                    depth[i] = 0;
                    queue.Enqueue(i);
                }
            }
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % w;
                int cz = cur / w;
                int d = depth[cur] + 1;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h)
                        {
                            continue;
                        }
                        int n = nz * w + nx;
                        if (depth[n] > d)
                        {
                            depth[n] = d;
                            queue.Enqueue(n);
                        }
                    }
                }
            }
            bool[] mask = new bool[count];
            for (int i = 0; i < count; i++)
            {
                mask[i] = kind[i] == KindWall && depth[i] >= FogDepth;
            }
            // Prune fog islands below the size threshold.
            bool[] seen = new bool[count];
            List<int> comp = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (!mask[i] || seen[i])
                {
                    continue;
                }
                comp.Clear();
                queue.Clear();
                queue.Enqueue(i);
                seen[i] = true;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    comp.Add(cur);
                    int cx = cur % w;
                    int cz = cur / w;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0)
                            {
                                continue;
                            }
                            int nx = cx + dx;
                            int nz = cz + dz;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= h)
                            {
                                continue;
                            }
                            int n = nz * w + nx;
                            if (mask[n] && !seen[n])
                            {
                                seen[n] = true;
                                queue.Enqueue(n);
                            }
                        }
                    }
                }
                if (comp.Count < MinFogComponent)
                {
                    for (int k = 0; k < comp.Count; k++)
                    {
                        mask[comp[k]] = false;
                    }
                }
            }
            return mask;
        }

        private static float Noise01(Perlin noise, IntVec3 c)
        {
            return (float)(noise.GetValue(c.x, 0.0, c.z) + 1.0) * 0.5f;
        }

        /// <summary>
        /// §99.o - THE BIOME'S OWN GROUND AT A GIVEN FERTILITY.
        ///
        /// Uses vanilla's OWN selector over the biome's <c>terrainsByFertility</c> table
        /// (rule 36: run vanilla's predicate), which is the same table <c>GenStep_Terrain</c>
        /// reads for the surface - so a plateau is floored with whatever that biome floors
        /// its ground with, at the equivalent fertility.
        ///
        /// ⚠ THIS IS WHAT LETS FOREIGN CONTENT FIND THE UPPER LEVELS AT ALL, and the
        /// Gallatross bones are the case that proved it. Alpha Biomes places them from its
        /// own <c>MapComponentExtender.doMapSpawns</c> at <c>Map.FinalizeInit</c> - AFTER our
        /// carve, so nothing of ours deletes them - walking <c>map.AllCells</c> across every
        /// band. The only thing that decided they never appeared upstairs was
        /// <c>SpecialSpawnsDef.terrainValidationAllowed</c>: the bones require
        /// <c>AB_CrackedMud</c> or <c>AB_RichCrackedMud</c>, and our plateau was hardcoded to
        /// vanilla <c>Soil</c> / <c>SoilRich</c>, which are not on that list. Give the band
        /// the biome's real ground and their own unmodified code places them for us.
        ///
        /// The general lesson is worth more than the one mod: a foreign spawner usually gates
        /// on TERRAIN, so a band floored in generic vanilla terrain is invisible to all of
        /// them at once (rule 54 - search the capability).
        /// </summary>
        private static TerrainDef BiomeGroundAt(BiomeDef biome, float fertility)
        {
            if (biome?.terrainsByFertility == null || biome.terrainsByFertility.Count == 0)
            {
                return null;
            }
            try
            {
                return TerrainThreshold.TerrainAtValue(biome.terrainsByFertility, fertility);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Arable patches use the band biome's own fertile terrain, so modded
        /// biomes get their native soil instead of a hardcoded vanilla Soil.</summary>
        private static TerrainDef ArableTerrainFor(BiomeDef biome)
        {
            if (biome?.terrainsByFertility != null)
            {
                for (int i = 0; i < biome.terrainsByFertility.Count; i++)
                {
                    TerrainDef t = biome.terrainsByFertility[i].terrain;
                    if (t != null && t.fertility >= 0.7f)
                    {
                        return t;
                    }
                }
            }
            return TerrainDefOf.Soil;
        }
        private static byte[] Classify(bool[] solid, bool[] noMeadow, int w, int h, out int[] edgeDistOut)
        {
            int num = w * h;
            byte[] array = new byte[num];
            int[] array2 = new int[num];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < num; i++)
            {
                if (solid[i])
                {
                    array2[i] = int.MaxValue;
                    continue;
                }

                array2[i] = 0;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int num2 = queue.Dequeue();
                int num3 = num2 % w;
                int num4 = num2 / w;
                int num5 = array2[num2] + 1;
                for (int j = -1; j <= 1; j++)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        if (k == 0 && j == 0)
                        {
                            continue;
                        }

                        int num6 = num3 + k;
                        int num7 = num4 + j;
                        if (num6 >= 0 && num7 >= 0 && num6 < w && num7 < h)
                        {
                            int num8 = num7 * w + num6;
                            if (array2[num8] > num5)
                            {
                                array2[num8] = num5;
                                queue.Enqueue(num8);
                            }
                        }
                    }
                }
            }

            edgeDistOut = array2;
            ABSettings settings = ABMod.Settings;
            if (settings != null && !settings.naturalPeaks)
            {
                for (int l = 0; l < num; l++)
                {
                    if (solid[l])
                    {
                        array[l] = (byte)((array2[l] <= 1) ? 1 : 2);
                    }
                }

                return array;
            }

            float num9 = Mathf.Clamp(settings?.peakMeadowCutoff ?? 0.62f, 0.45f, 0.75f);
            float num10 = Mathf.Clamp(settings?.peakMeadowScale ?? 0.024f, 0.012f, 0.048f);
            int max = Mathf.Clamp(settings?.peakTerraceMax ?? 4, 1, 6);
            Perlin noise = new Perlin(num10, 2.0, 0.5, 5, Rand.Range(0, int.MaxValue), QualityMode.Medium);
            Perlin noise2 = new Perlin(0.035, 2.0, 0.5, 4, Rand.Range(0, int.MaxValue), QualityMode.Medium);
            for (int m = 0; m < num; m++)
            {
                if (solid[m])
                {
                    int newX = m % w;
                    int newZ = m / w;
                    IntVec3 c = new IntVec3(newX, 0, newZ);
                    int num11 = array2[m];
                    if ((noMeadow == null || !noMeadow[m]) && Noise01(noise, c) > num9)
                    {
                        array[m] = (byte)((num11 <= 1) ? 1 : 3);
                    }
                    else
                    {
                        array[m] = (byte)((num11 <= TerraceWidth(noise2, c, max)) ? 1 : 2);
                    }
                }
            }

            Despeckle(array, solid, w, h, 2);
            return array;
        }

        private static bool[] Dilate(bool[] src, int w, int h, int radius)
        {
            bool[] array = new bool[src.Length];
            for (int i = 0; i < h; i++)
            {
                for (int j = 0; j < w; j++)
                {
                    if (!src[i * w + j])
                    {
                        continue;
                    }

                    int num = Math.Max(0, j - radius);
                    int num2 = Math.Min(w - 1, j + radius);
                    int num3 = Math.Max(0, i - radius);
                    int num4 = Math.Min(h - 1, i + radius);
                    for (int k = num3; k <= num4; k++)
                    {
                        for (int l = num; l <= num2; l++)
                        {
                            array[k * w + l] = true;
                        }
                    }
                }
            }

            return array;
        }

        internal static bool PregenRoofOver(Map map, IntVec3 below)
        {
            if (!below.InBounds(map))
            {
                return false;
            }

            if (BuiltBelow(map, below))
            {
                return true;
            }

            RoofGrid roofGrid = map.roofGrid;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 intVec = below + GenAdj.AdjacentCells[i];
                if (intVec.InBounds(map) && roofGrid.RoofAt(intVec) != null && BuiltBelow(map, intVec))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool BuiltBelow(Map map, IntVec3 below)
        {
            return below.InBounds(map) && BuiltBelow(map, below, map.edificeGrid[below], map.roofGrid.RoofAt(below));
        }

        private static bool BuiltBelow(Map map, IntVec3 below, Building edifice, RoofDef roof)
        {
            if (roof != null && !roof.isNatural)
            {
                return true;
            }

            if (edifice != null && edifice.def != null && !edifice.def.mineable && (edifice.def.building == null || !edifice.def.building.isNaturalRock) && (edifice.def.building == null || edifice.def.building.artificialForMeditationPurposes))
            {
                return true;
            }

            TerrainDef terrainDef = map.terrainGrid.TerrainAt(below);
            return terrainDef != null && !terrainDef.natural && (ABMod.ModContent == null || terrainDef.modContentPack != ABMod.ModContent);
        }

        private const int NoMeadowRadius = 2;

    }
}
