# Steam Workshop parity inventory

This PR is a partial port. The checked-out base is `58d6c2ae285c701872af2bc1a39cb500eac33539`. The installed Workshop item 3776015553 declares `astryl.AsAboveSoBelow2`, author astryl, and mod version 1.5.7. Its PublishedFileId agrees with the checkout and the installed MIT license is byte-identical to the repository license. The upstream MIT notice is retained. The Workshop preview and XML edits originate from that item. C# changes are implementations in the existing source tree based on inspection of the Workshop behavior.

## Assembly provenance

| Input | SHA-256 | Embedded source revision |
| --- | --- | --- |
| GitHub 1.6 | `2e1e5279e94cd45329412c8ee936ff470015e2188f6d2762b7c5465497a913e5` | `1bc4f79500740e698e81f32f3a623c5e71f543fe` |
| Steam 1.6 | `5f58409800fbc781ff1d348c61ab49bffabca798490e249ce237adfb2f69e5a5` | `e4dc724489778aabf12e06b68e7eae9246970614` |
| Steam 1.7 | `b8bd33cb09115e623b5c2c385bffc8bf39543bcde0996086d80bbac1b507cb99` | `5b198256a0cc6b2d706a314d5f5954b14f26daa5` |

The GitHub DLL predates the base commit. DLL comparison is independent of the comparison against the current C# and XML. The Workshop assemblies were inspected with ILSpy. They were not checked in. The replacement assemblies are compiled from this branch as net472 (1.6) and net10.0 (1.7).

## Files and data

| Paths | Disposition |
| --- | --- |
| About/About.xml | Added the 1.7 target and Combat Extended load ordering. Giddy-Up load ordering awaits its bridge. Workshop modVersion 1.5.7 remains absent because this is a partial fork port. |
| About/Preview.png | Updated from the installed Workshop item. |
| About/Changelog.txt | Outstanding. It describes upstream releases and features absent from this fork; do not present it as the fork’s changelog. |
| Defs/V2/AB2_Columns.xml and AB2_Research.xml | Power columns and risers now require Electricity, with matching research copy. Other research gates are unchanged. |
| 1.7/Patches/AB_OpenAirPollution.xml | Added with 1.7-only pollution cleanup and cached cell counting. |
| Languages/English/Keyed/AsAboveSoBelow.xml | Outstanding. Two orbital keys and the Progress Renderer labels/tooltips require their corresponding features. |
| LoadFolders.xml and the three input DLL paths | Shared content stays at root. Each version loads one locally built DLL. |

All other shared XML and all common texture paths match byte-for-byte. The following Steam-only DDS files each have a same-stem PNG in Git. DDS packaging remains outstanding pending loader precedence and image equivalence verification.

- `Textures/Terrain/AB_MountainTop.dds`
- `Textures/Terrain/AB_OpenAir.dds`
- `Textures/Things/Building/AB_Elevator.dds`
- `Textures/Things/Building/AB_GrandStairsDown.dds`
- `Textures/Things/Building/AB_GrandStairsDown_east.dds`
- `Textures/Things/Building/AB_GrandStairsDown_north.dds`
- `Textures/Things/Building/AB_GrandStairsDown_south.dds`
- `Textures/Things/Building/AB_GrandStairsUp.dds`
- `Textures/Things/Building/AB_GrandStairsUp_east.dds`
- `Textures/Things/Building/AB_GrandStairsUp_north.dds`
- `Textures/Things/Building/AB_GrandStairsUp_south.dds`
- `Textures/Things/Building/AB_LadderDown.dds`
- `Textures/Things/Building/AB_LadderDown_east.dds`
- `Textures/Things/Building/AB_LadderDown_north.dds`
- `Textures/Things/Building/AB_LadderDown_south.dds`
- `Textures/Things/Building/AB_LadderUp.dds`
- `Textures/Things/Building/AB_LadderUp_east.dds`
- `Textures/Things/Building/AB_LadderUp_north.dds`
- `Textures/Things/Building/AB_LadderUp_south.dds`
- `Textures/Things/Building/AB_StairsDown.dds`
- `Textures/Things/Building/AB_StairsDown_east.dds`
- `Textures/Things/Building/AB_StairsDown_north.dds`
- `Textures/Things/Building/AB_StairsDown_south.dds`
- `Textures/Things/Building/AB_StairsDown_west.dds`
- `Textures/Things/Building/AB_StairsUp.dds`
- `Textures/Things/Building/AB_StairsUp_east.dds`
- `Textures/Things/Building/AB_StairsUp_north.dds`
- `Textures/Things/Building/AB_StairsUp_south.dds`
- `Textures/Things/Building/AB_StairsUp_west.dds`
- `Textures/Things/Building/Columns/AB_ColumnClimate.dds`
- `Textures/Things/Building/Columns/AB_ColumnData.dds`
- `Textures/Things/Building/Columns/AB_ColumnPipe.dds`
- `Textures/Things/Building/Columns/AB_ColumnPower.dds`
- `Textures/Things/Building/Risers/AB_WallRiserClimate.dds`
- `Textures/Things/Building/Risers/AB_WallRiserClimate_east.dds`
- `Textures/Things/Building/Risers/AB_WallRiserClimate_north.dds`
- `Textures/Things/Building/Risers/AB_WallRiserClimate_south.dds`
- `Textures/Things/Building/Risers/AB_WallRiserData.dds`
- `Textures/Things/Building/Risers/AB_WallRiserData_east.dds`
- `Textures/Things/Building/Risers/AB_WallRiserData_north.dds`
- `Textures/Things/Building/Risers/AB_WallRiserData_south.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPipe.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPipe_east.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPipe_north.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPipe_south.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPower.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPower_east.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPower_north.dds`
- `Textures/Things/Building/Risers/AB_WallRiserPower_south.dds`
- `Textures/UI/AB_ViewLevelDown.dds`
- `Textures/UI/AB_ViewLevelUp.dds`
- `Textures/UI/Overlays/AB_ShotThrough.dds`

## Decompiled assembly file differences

Each row records a changed output file, including possible compiler and engine API differences. An open row needs semantic confirmation before it can be counted as a behavior change. Build and runtime evidence appears below.

### GitHub 1.6 to Steam 1.6

| File | Difference | Disposition |
| --- | --- | --- |
| `ABAirSpawnGuard.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `ABBandCurtain.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBandDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ABBandDressing.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABBandEnv.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABBandLocal.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `ABBandMap.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `ABBandScope.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABBandView.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBandWeather.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBandedGeneration.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABBelowClickThrough.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBelowDynamicDraw.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBelowForeignLayers.cs` | Added | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABCECompat.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `ABCEHuntCompat.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `ABColumnNetworks.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `ABColumnNetworksBoot.cs` | Added | Ported in ABColumnNetworks.cs, ABBandSafety.cs or ABBandLocal.cs respectively. |
| `ABCombatAcquisition.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABCombatCover.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABCombatPosition.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABCombatV2.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABDevTools.cs` | Changed | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `ABGLBiomeGridSync.cs` | Added | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABMapSizeLimit.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABNetwork.cs` | Changed | Open. Needs tests of the version-specific host API, carrier linking and save/load. |
| `ABOrbitRules.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `ABOrbitalDebrisBands.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `ABPathBandScope.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABSettings.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `ABShaft.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ABShamblerCrossLevel.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABSkyBandGen.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABSkySync.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `ABSpaceBackdrop.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `ABStairAnim.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABStructureFit.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABStuckWatchdog.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ABTransitVisuals.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABV2Debug.cs` | Changed | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `ABWormhole.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ABWormholePather.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ABWormholeRearmHook.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `AdaptiveStorageCompat.cs` | Added | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Building_ABColumn.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `Building_ABStairs2.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `CommonSenseCompat.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `FloatMenuOptionProvider_ABStairsTravel.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `GeologicalLandformsCompat.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `GiddyUpCompat.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `LargePawnsCompat.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_AdvancedGameConfig_ABMapSizeLock.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_AttackTargetFinder_ABCrossBand.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CE_AdjustShotHeight_ABTargetLevel.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_CoverWalk_ABTargetLevel.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_FireArc_ABBandLocalBearing.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_HuntRange_ABBandLocal.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_ProjectileLaunch_ABCrossBand.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_ProjectileRayCast_ABCrossBand.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_ShiftVecReport_ABCrossBand.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_ShootLine_ABCrossBand.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_TurretOrderAttack_ABBandLocalRange.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CameraDriver_ABClipViewToBand.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Patch_CastPositionFinder_ABCrossBand.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CastPositionFinder_ABEvaluateCellBandLocal.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CastPositionFinder_ABPreferenceBandLocal.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CellFinder_ABRandomNotEdgeCell.cs` | Added | Ported in ABColumnNetworks.cs, ABBandSafety.cs or ABBandLocal.cs respectively. |
| `Patch_CommonSense_ABHaulOverBillDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_CoverUtility_ABCrossBandBlockChance.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CoverUtility_ABCrossBandCoverGivers.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_CoverUtility_ABNoPointBlankFromBelow.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_DesignationManager_ABCaptureArea.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `Patch_EdificeGrid_ABDeRegister.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `Patch_EdificeGrid_ABRegister.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `Patch_FloodFiller_ABGLAnchorClamp.cs` | Added | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_FloodFiller_ABSmartDeconBand.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_FoodUtility_ABFoodOptimalityDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_FoodUtility_ABInnerScanContext.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_GL_ABBiomeTransitionScope.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_GenClosest_ABGlobalReachableBandDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_GenDraw_ABLiftTargetHighlightThing.cs` | Added | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Patch_GenSpawn_ABNoAirSpawn.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `Patch_GenStep_ABSurfaceBandScope.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_GenStep_Scatterer_ABBandSeam.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_GiddyUp_ABAutoMountDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_GiddyUp_ABBestAnimalDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_IntVec3Utility_ABBandDistanceToEdge.cs` | Added | Ported in ABColumnNetworks.cs, ABBandSafety.cs or ABBandLocal.cs respectively. |
| `Patch_LargePawns_ABCrossLevelBlockChance.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_LargePawns_ABCrossLevelCoverGivers.cs` | Added | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_LightingOverlay_ABSuppressOnBanded.cs` | Changed | Open. Needs semantic analysis of the type and a matching runtime regression case. |
| `Patch_LordMaker_ABDefendBaseAnchor.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_MapGenUtility_ABBandBiomeAt.cs` | Added | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_PathFinder_ABHeuristicStrength.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_PathFinder_ABRejectCrossBandAsync.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_PathFinder_ABRejectCrossBandSync.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_PathFollower_ABStartPath.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_PawnRenderer_ABBelowShrink.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Patch_ProgressRenderer_ABNoFullStackCapture.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `Patch_ProgressRenderer_ABPinCaptureBand.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `Patch_SectionLayer_Terrain_ABUnmaskShadows.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Patch_ShamblerFight_ABCrossLevelMelee.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_ShotReport_ABBandLocalDistance.cs` | Added | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_ShotReport_ABCrossBandDistance.cs` | Removed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_SmartDecon_ABScopeRoofScan.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABEncloseScope.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABPocketHasExit.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABRankingDistance.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABRankingScope.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABSafeSpotScope.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_SmarterConstruction_ABStuckDuringTransit.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_TileMutatorWorker_Plateau_ABBandSize.cs` | Added | Ported in ABColumnNetworks.cs, ABBandSafety.cs or ABBandLocal.cs respectively. |
| `Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs` | Added | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_ToilsIngest_ABCrossLevelChair.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_Translate_ABSurfaceCentre.cs` | Added | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Patch_VGE_ArtilleryEdgeCell_ABBand.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `Patch_VGE_ArtillerySpawnCell_ABBand.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `Patch_VGE_ArtilleryTick_ABBandExit.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `Patch_Verb_ABCrossBandShootLine.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_WanderUtility_ABKeepRootInBand.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `Patch_WorldCameraDriver_ABBandParallax.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `PlaceWorker_ABLinkApproach.cs` | Changed | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `ProgressRendererCompat.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `SectionLayer_ABBelowLighting.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `SectionLayer_ABBelowV2.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `SectionLayer_ABMountainCap.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `SectionLayer_ABRockRootFill.cs` | Added | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `SkyFXCompat.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `SmarterConstructionCompat.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `SmarterDeconstructionCompat.cs` | Added | Open. Needs connected/disconnected-band route and job regressions, with the named host active where applicable. |
| `VanillaGravshipCompat.cs` | Added | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `AssemblyInfo.cs` | Changed | Build metadata or decompiler project. Both outputs are built from the fork’s shared project. |
### Steam 1.6 to Steam 1.7

| File | Difference | Disposition |
| --- | --- | --- |
| `ABBandCurtain.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABBandMap.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `ABBelowForeignLayers.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABDevTools.cs` | Changed | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `ABLineTrap.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `ABPollutionCount.cs` | Added | Ported in ABPollutionCount.cs. Gameplay verification pending. |
| `ABShamblerCrossLevel.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `ABWaterBand.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `ABWaterLab.cs` | Changed | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `Building_ABColumn.cs` | Changed | Partial. Column startup, save reconciliation, pollution cleanup and plateau sizing ported. Other changes in these types require method-by-method review and a save/map-generation test. |
| `DubsMintMinimapCompat.cs` | Changed | Open. Needs tests of the version-specific host API, carrier linking and save/load. |
| `Patch_CE_ProjectileLaunch_ABCrossBand.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CE_ProjectileRayCast_ABCrossBand.cs` | Changed | Open. Needs version-matched Combat Extended or Large Pawns hooks and cross-level combat tests with each host active. |
| `Patch_CoverUtility_ABNoPointBlankFromBelow.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_DubsPipes_ABRiserLink.cs` | Changed | Open. Needs tests of the version-specific host API, carrier linking and save/load. |
| `Patch_PawnFlyer_ABCrossBandArc.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_PollutionGrid_ABCachedPollutableCount.cs` | Added | Ported in ABPollutionCount.cs. Gameplay verification pending. |
| `Patch_Projectile_ABCrossBandOrigin.cs` | Changed | Open. Needs cross-level firing/melee regression cases for angles, cover, range and both directions. |
| `Patch_SelectionBrackets_ABBelow.cs` | Changed | Open. Needs a visual comparison across levels and Waylith draw API validation where applicable. |
| `Patch_TerrainGrid_ABSeeBelowDirtyGravship.cs` | Changed | Open. Needs an orbital map and gravship arrival/departure fixtures with Odyssey and the applicable host. |
| `Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs` | Changed | Open. Needs fixed-seed map generation coverage for terrain, biomes, landmarks and band seams. |
| `Showcase.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `ShowcaseUnloadWatcher.cs` | Added | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `Window_ABTextureTuner.cs` | Changed | Open. Needs to distinguish developer tooling from gameplay and verify any user-facing capture and rendering hooks. |
| `AsAboveSoBelow.csproj` | Changed | Build metadata or decompiler project. Both outputs are built from the fork’s shared project. |
| `AssemblyInfo.cs` | Changed | Build metadata or decompiler project. Both outputs are built from the fork’s shared project. |

## Verification

- Release builds for both targets completed with zero warnings and errors using the local game and Workshop reference assemblies.
- The standalone 1.7 launcher ran with a disposable save-data folder and the existing zero app ID. The game attempted Steam initialization, which failed for lack of an app ID. The run also encountered a duplicate package ID and the installed Harmony 3.0.4 runtime reported a missing `WaylithClr.MethodBodyTransformer` type. The launch therefore provides no gameplay validation. No additional runtime launches were made.
- A 1.6 runtime launch would use the separate Game-1.6 installation. The requested runtime path only permits Game, so verification of 1.6 is build-only.
