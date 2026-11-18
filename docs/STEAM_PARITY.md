# Steam Workshop parity inventory

The GitHub baseline is `58d6c2ae285c701872af2bc1a39cb500eac33539`. The installed Workshop item 3776015553 identifies itself as `astryl.AsAboveSoBelow2`, by astryl, version 1.5.7. Its PublishedFileId and MIT license match the baseline. All regular files in the installed item matched the supplied fallback snapshot when compared. The original MIT notice remains in the repository.

The two Workshop source revision IDs below were unavailable from the upstream GitHub repository. The port uses ILSpy output as a reference for the astryl-authored MIT code, with game and dependency references resolved for each target. The existing source layout and unchanged methods were retained. The Workshop DLLs, native game assemblies and companion-mod binaries are excluded from the branch.

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
| About/About.xml | Ported the 1.7 version, Combat Extended and Giddy-Up load ordering, and 1.5.7 Workshop version metadata. |
| About/Preview.png and About/Changelog.txt | Imported the upstream preview and historical Workshop changelog. |
| Defs/V2/AB2_Columns.xml and AB2_Research.xml | The power column and riser require Electricity. Transfer Columns still gates non-power networks where applicable. |
| 1.7/Patches/AB_OpenAirPollution.xml | Open air and gutters are excluded from pollutable terrain for 1.7, together with the save cleanup. |
| Languages/English/Keyed/AsAboveSoBelow.xml | Orbital placement and map size messages and Progress Renderer strings. |
| LoadFolders.xml and Assemblies | Shared root data plus one version-specific assembly. Each DLL is compiled from this repository. |

The remaining common XML and PNG texture files match the Workshop bytes. The 52 Workshop DDS files were imported byte-for-byte and have the same dimensions as their existing PNG counterparts.

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

## Gameplay and compatibility

| Area | Workshop behavior carried into source |
| --- | --- |
| Combat | Opening and leaning geometry, band-local range, target acquisition and cast position scoring, target-level cover, blindness/weather/light checks, burst and instant-projectile relays, suppression integration, hunting distance, and shambler melee routing. |
| Routing and AI | In-flight transit save/restore, band-local Touch endpoints, guard and stuck-job recovery, route-based nearest-target/meal/seat scoring, and safe movement for followers. |
| World and generation | Orbital starts without basements, platform-ground rules, orbital debris and gravship landing/artillery, band-local plateau and river sizing, biome transition and grid repair, and corrected edge placement. |
| Rendering and controls | Foreign section layers, lower-floor Adaptive Storage graphics, rock roots and caps, orbital backdrop and depth curtain, below-floor click and selection, and Progress Renderer capture on a chosen level. |
| Companion mods | Conditional compatibility for Combat Extended, Large Pawns, Adaptive Storage, Giddy-Up, Common Sense, Smarter Construction/Deconstruction, Geological Landforms, Dubs Mint Minimap, SkyFX and Vanilla Gravship Expanded. |
| Network and pollution lifecycle | Startup carrier registration, duplicate cleanup, column support on load and during generation, and cached 1.7 pollutable-cell count with legacy air cleanup. |
| Developer tools | Workshop rendering, combat and pathing diagnostics and the 1.7 Showcase and unload hook. |

## Assembly member inventory

The following tables list source counterparts for resolved ILSpy differences. Each member name is listed with its change relative to the previous input. Nested types represent changes inside that type. For whole types added or removed, the row covers all members. Assembly metadata and generated project differences are represented by the framework targets, versioned output paths, and provenance above. Method IL and individual compiler artifacts can differ while producing the same behavior.

### GitHub 1.6 → Steam 1.6

| Type | Source | Members and disposition |
| --- | --- | --- |
| `ABAirSpawnGuard` | [ABAirSpawnGuard.cs](../Source/V2/ABAirSpawnGuard.cs) | `IsDerivedLevelSpawn(Thing)` (added), `IsLooseDecoration(Thing)` (changed). |
| `ABBandCurtain` | [ABBandCurtain.cs](../Source/V2/ABBandCurtain.cs) | `depthCurtain` (added), `DepthCurtain` (added), `depthCurtainResolved` (added), `Draw(Map)` (changed), `DrawExteriorStamp(float,float,float,float)` (added), `ExteriorRenderLayer` (added), `SpaceCurtainOff` (added), `StampAltitude` (added), `zeroColourQuad` (added), `ZeroColourQuad` (added). |
| `ABBandDistance` | [ABBandDistance.cs](../Source/V2/Workshop/ABBandDistance.cs) | Whole type added. |
| `ABBandDressing` | [ABBandDressing.cs](../Source/V2/ABBandDressing.cs) | `Dress(Map,ABBandMap)` (changed). |
| `ABBandedGeneration` | [ABBandedGeneration.cs](../Source/V2/ABBandedGeneration.cs) | `Patch_GenerateContents_ABCarveInWindow` (changed), `Patch_MapGenerator_GenerateMap` (changed), `TryInferredSurfaceRect(Map,CellRect,int)` (changed). |
| `ABBandEnv` | [ABBandEnv.cs](../Source/V2/ABBandEnv.cs) | `BiomeOf(Map,ABBandMap,IntVec3)` (changed), `SurfaceBiomeUnder(Map,ABBandMap,IntVec3)` (added). |
| `ABBandLocal` | [ABBandLocal.cs](../Source/V2/ABBandLocal.cs) | `TryBandGeometry(Map,int,int,int)` (changed). |
| `ABBandMap` | [ABBandMap.cs](../Source/V2/ABBandMap.cs) | `ExposeData()` (changed), `FinalizeInit()` (changed), `MapRemoved()` (changed), `transitSave` (added). Ports persisted transit and world cleanup, GL synchronization and 1.7 pollution cleanup. Keeps the fork’s all-map carrier reconciliation. |
| `ABBandScope` | [ABBandScope.cs](../Source/V2/ABBandScope.cs) | `RefusesSpawnAt(Map,IntVec3)` (added), `roofRejections` (added). |
| `ABBandView` | [ABBandView.cs](../Source/V2/ABBandView.cs) | `RenderBand(Map)` (added), `TryBandBounds(Map,float,float)` (changed), `TryBoundsOf(Map,int,float,float)` (added), `TryRenderBandBounds(Map,float,float)` (added). |
| `ABBandWeather` | [ABBandWeather.cs](../Source/V2/ABBandWeather.cs) | `SeedAltitudeSnow(Map,ABBandMap)` (changed). |
| `ABBelowClickThrough` | [ABBelowClickThrough.cs](../Source/V2/ABBelowClickThrough.cs) | `AnyCanReach(Map,List<Pawn>,IntVec3)` (changed). |
| `ABBelowDynamicDraw` | [ABBelowDynamicDraw.cs](../Source/V2/ABBelowDynamicDraw.cs) | `DrawBelowPawns(Map)` (changed). |
| `ABBelowForeignLayers` | [ABBelowForeignLayers.cs](../Source/V2/Workshop/ABBelowForeignLayers.cs) | Whole type added. Selects the appropriate uninitialized allocation API per runtime. |
| `ABCECompat` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `AdjustFuse(Thing,Remap,float,float,float)` (added), `BandLocalDelta(IntVec3,IntVec3)` (added), `boundsThrew` (added), `ceCeilingEntries` (added), `ceCeilingPullbacks` (added), `ceCoverEyeReads` (added), `ceFireArcsLocalized` (added), `ceFusesAdjusted` (added), `CeilingHeight` (added), `ceLaunchUnsolved` (added), `ceLeanSolves` (added), `ceOpeningEmergences` (added), `ceRayCastsRemapped` (added), `ceTurretOrdersLocalized` (added), `CloseEye(EyeScope)` (added), `CounterReport()` (changed), `CoverEye(Thing)` (added), `coverSeamArmed` (added), `eyeCaster` (added), `eyeCell` (added), `eyeColumn` (added), `EyeScope` (added), `fArmingDelay` (added), `fIgnoreRoof` (added), `fIntendedTarget` (added), `fIntendedTargetHeight` (added), `FireArcCE` (added), `FlightDir(float)` (added), `fMinCollision` (added), `fRoundGravity` (added), `fShotAngle` (added), `fShotDist` (added), `fShotHeight` (added), `fShotRotation` (added), `fShotSpeed` (added), `fuseFields` (added), `HeightAt(bool,float,float,float,float,float)` (added), `HeightEye(Thing)` (added), `heightSeamArmed` (added), `launchSeamFound` (added), `MaxCeilingMiss` (added), `mBoundsFor` (added), `MinCeilingRun` (added), `MinCollisionFor(float)` (added), `OpenEye(Verb,LocalTargetInfo)` (added), `orderDepth` (added), `orderScopeMap` (added), `pGravityPerWidth` (added), `PopOrderScope()` (added), `PushOrderScope(Map)` (added), `Remap` (added), `ResetCounters()` (changed), `Resolve()` (changed), `RewriteCellSubtraction(IEnumerable<CodeInstruction>,string,int)` (added), `RewritePositionReads(IEnumerable<CodeInstruction>,string,int,string,int,int)` (added), `RotationOf(Vector2)` (added), `tFireArcCE` (added), `tProjectilePropsCE` (added), `TryBallisticAngle(bool,float,float,float,float,float)` (added), `TryCeilingEntry(LocalTargetInfo,Map,bool,float,float,float,Vector2,float,Remap,Vector2,Vector2,float)` (added), `TryRemap(Thing,Map,ABBandMap,Vector2,float,float,float,float,bool,Remap)` (added), `TryTargetBounds(LocalTargetInfo,Map,Bounds)` (added), `tTurretCE` (added), `TurretCE` (added), `Typed(FieldInfo,Type)` (added). |
| `ABCEHuntCompat` | [ABCEHuntCompat.cs](../Source/Compat/Workshop/ABCEHuntCompat.cs) | Whole type added. |
| `ABColumnNetworks` | [ABColumnNetworks.cs](../Source/V2/ABColumnNetworks.cs) | `AllCarrierDefs` (added), `BuildCarrier(ABNetwork)` (changed), `carrierCompVeto` (added), `CloneComps(List<CompProperties>,ABNetwork)` (added), `CloneComps(List<CompProperties>)` (removed), `GiveCarrierShortHash(ThingDef)` (added), `Report()` (changed), `stripped` (added), `takenShortHashes` (added), `VetoedComp(CompProperties)` (added). |
| `ABColumnNetworksBoot` | [ABColumnNetworks.cs](../Source/V2/ABColumnNetworks.cs) | Whole type added. The startup scan supplements the existing implied-def hook. Both call the same idempotent scanner. |
| `ABCombatAcquisition` | [ABCombatAcquisition.cs](../Source/V2/ABCombatAcquisition.cs) | `BlastRadiusOffset` (added), `blindedBySmoke` (added), `Candidate` (added), `CounterReport()` (changed), `FindCrossBandTarget(IAttackTargetSearcher,TargetScanFlags,Predicate<Thing>,float,float,IntVec3,float,bool,bool,bool,bool)` (added), `FindCrossBandTarget(IAttackTargetSearcher,TargetScanFlags,Predicate<Thing>,float,float,IntVec3,float,bool,bool)` (removed), `IgnoresAccuracyMaluses(Verb)` (added), `MakeBlastRadiusOffset()` (added), `outOfReach` (added), `PassesFlags(IAttackTargetSearcher,Thing,Lord,Pawn,Thing,IAttackTarget,TargetScanFlags,bool,Verb,bool,bool)` (added), `PassesFlags(IAttackTargetSearcher,Thing,Lord,Pawn,Thing,IAttackTarget,TargetScanFlags)` (removed), `PickByScore(List<Candidate>,float)` (added), `ResetCounters()` (changed), `ShootingScore(IAttackTarget,Thing,IAttackTargetSearcher,Thing,Verb,ABBandMap,IntVec3)` (added), `smokeOnLine` (added), `tmpCandidates` (added), `tmpPotential` (added). |
| `ABCombatCover` | [ABCombatCover.cs](../Source/V2/Workshop/ABCombatCover.cs) | Whole type added. |
| `ABCombatPosition` | [ABCombatPosition.cs](../Source/V2/ABCombatPosition.cs) | `ArmedCount` (added), `armedMethods` (added), `CounterReport()` (changed), `Enter(Thing,IntVec3,bool)` (added), `Exit(ScopeState)` (added), `loopCount` (added), `loopFrame` (added), `loopPawn` (added), `loopTarget` (added), `loopTick` (added), `PositionOf(Thing)` (added), `ResetCounters()` (changed), `RewritePositionReads(IEnumerable<CodeInstruction>,int,string)` (added), `scopeEnemyBelow` (added), `ScopeEnemyBelow(IntVec3)` (added), `scopeLocal` (added), `ScopeState` (added), `scopeTarget` (added), `StandStillLoopLimit` (added), `standStillLoopsBroken` (added), `StandStillLoopTrips(Pawn,Thing,IntVec3)` (added), `TryFindBandLocalCastPosition(CastPositionRequest,IntVec3)` (changed), `vanillaSearchArmed` (added), `vanillaSearches` (added). |
| `ABCombatV2` | [ABCombatV2.cs](../Source/V2/ABCombatV2.cs) | `FillShootableCells(Map,ABBandMap,LocalTargetInfo,IntVec3,int,List<IntVec3>)` (added), `LeanCells(IntVec3,IntVec3,Map,List<IntVec3>)` (added), `leanSolves` (added), `OnBand(Map,ABBandMap,IntVec3,int)` (added), `Quadrants` (added), `SolveWithLeaning(Verb,IntVec3,LocalTargetInfo,float,float,ABShotSolution,IntVec3,IntVec3)` (added), `tmpDests` (added), `tmpLean` (added), `tmpLeanQuadrant` (added), `tmpSources` (added), `TrySolve(Verb,IntVec3,LocalTargetInfo,ABShotSolution,bool)` (changed), `TrySolve(Verb,IntVec3,LocalTargetInfo,ABShotSolution,IntVec3,IntVec3,bool)` (added), `TrySolveWithin(Verb,IntVec3,LocalTargetInfo,float,float,ABShotSolution,IntVec3,IntVec3)` (added), `TrySolveWithin(Verb,IntVec3,LocalTargetInfo,float,float,ABShotSolution)` (added). |
| `ABDevTools` | [ABDevTools.Workshop.cs](../Source/V2/ABDevTools.Workshop.cs), [ABDevTools.V2.cs](../Source/V2/ABDevTools.V2.cs) | `AppendOverlayForensics(Map,IntVec3,StringBuilder)` (added), `CapMask(Map,TerrainGrid,TerrainDef,IntVec3)` (added), `CellCode(Map,TerrainGrid,TerrainDef,IntVec3)` (added), `FindProbeCell(Map,ABBandMap)` (added), `MaskText(int)` (added), `NearestAnchorDist(IntVec3,List<IntVec3>)` (added), `SampleBelowCamera(Map,ABBandMap,int,int,int,bool)` (changed), `SampleStandable(Map,ABBandMap,int,int,int)` (changed), `StuckProbeAISection(StringBuilder,Pawn,Map)` (added), `V2BelowLayerReport()` (changed), `V2CombatReport()` (changed), `V2CycleSpaceShadowMask()` (added), `V2DesignateDiagonalOnlyMine()` (added), `V2LightingReport()` (changed), `V2PathingReport()` (changed), `V2PlaceStairs(string)` (changed), `V2ResetCombatCounters()` (changed), `V2RockEdgeProbe()` (added), `V2SelfTestCarrierHashes()` (added), `V2ToggleCapCornerFillers()` (added), `V2ToggleLedgeRockTile()` (added), `V2ToggleOverlayOverVoid()` (added), `V2ToggleRockRootFill()` (added), `V2ToggleSouthFillerStretch()` (added), `V2ToggleSpaceCurtain()` (added), `V2ToggleStoneBorderRule()` (added), `V2ToggleVanillaLighting()` (added), `V2ToggleZeroBelowLightingColors()` (added), `V2WhyNoShot()` (changed), `V2WhyStuck()` (changed). Existing partial diagnostic files and ABDevTools.Workshop.cs cover the changed actions. The 1.7 selection and pollution probes use the fork’s runtime counters and pollutable-count API. |
| `ABGLBiomeGridSync` | [ABGLBiomeGridSync.cs](../Source/V2/Workshop/ABGLBiomeGridSync.cs) | Whole type added. |
| `ABMapSizeLimit` | [ABMapSizeLimit.cs](../Source/V2/ABMapSizeLimit.cs) | `BandCountFor(PlanetTile)` (added), `Clamp(int,int)` (added), `Clamp(int)` (changed), `LowerLevelsFor(PlanetTile)` (added), `SurfaceBandFor(PlanetTile)` (added). |
| `ABNetwork` | [ABColumnNetworks.cs](../Source/V2/ABColumnNetworks.cs) | `strippedComps` (added). |
| `ABOrbitalDebrisBands` | [ABOrbitalDebrisBands.cs](../Source/V2/Workshop/ABOrbitalDebrisBands.cs) | Whole type added. |
| `ABOrbitRules` | [ABOrbitRules.cs](../Source/V2/Workshop/ABOrbitRules.cs) | Whole type added. |
| `ABPathBandScope` | [ABPathBandScope.cs](../Source/V2/ABPathBandScope.cs) | `CrossBand(Map,IntVec3,LocalTargetInfo,PathEndMode,IntVec3?,string)` (added), `CrossBand(Map,IntVec3,LocalTargetInfo,string)` (changed), `EnsureBuckets(Map,IReadOnlyList<Thing>,IReadOnlyList<Pawn>,IReadOnlyList<IPathFindCostProvider>)` (changed), `SpansSeam(Map,Thing)` (added). |
| `ABSettings` | [ABSettings.cs](../Source/Core/ABSettings.cs) | `DoProgressRenderer(Listing_Standard)` (added), `DoVisuals(Listing_Standard)` (changed), `ExposeData()` (changed), `progressRendererLevel` (added). |
| `ABShaft` | [ABShaft.cs](../Source/V2/ABShaft.cs) | `Accepts(Map,ABBandMap,IntVec3,IntVec3,IntVec3,int,int,float)` (changed), `ColumnOpen(Map,ABBandMap,IntVec3,int,int)` (changed), `CrossBandDistance(ABBandMap,IntVec3,IntVec3)` (added), `LevelCharged(float,int)` (added), `Solve(Map,ABBandMap,IntVec3,IntVec3,int,int,float,float,bool,ABShotSolution)` (changed). |
| `ABShamblerCrossLevel` | [ABShamblerCrossLevel.cs](../Source/V2/Workshop/ABShamblerCrossLevel.cs) | Whole type added. |
| `ABSkyBandGen` | [ABSkyBandGen.cs](../Source/V2/ABSkyBandGen.cs) | `BuiltBelow(Map,IntVec3,Building,RoofDef)` (added), `BuiltBelow(Map,IntVec3)` (added), `Classify(bool[],bool[],int,int,int[])` (added), `Classify(bool[],int,int,int[])` (removed), `Dilate(bool[],int,int,int)` (added), `Generate(Map,ABBandMap,int,List<ThingDef>,List<Perlin>)` (changed), `NoMeadowRadius` (added), `PregenRoofOver(Map,IntVec3)` (added). |
| `ABSkySync` | [ABSkySync.cs](../Source/V2/ABSkySync.cs) | `Resolve(Map,IntVec3)` (changed), `SyncRectFor(Building)` (added). |
| `ABSpaceBackdrop` | [ABSpaceBackdrop.cs](../Source/V2/Workshop/ABSpaceBackdrop.cs) | Whole type added. |
| `ABStairAnim` | [ABStairAnim.cs](../Source/V2/ABStairAnim.cs) | `EmergePose(Clip,float)` (changed), `EntryPose(Clip,float,int)` (changed). |
| `ABStructureFit` | [ABStructureFit.cs](../Source/V2/ABStructureFit.cs) | `Patch_Map_ABSurfaceCenter` (changed). |
| `ABStuckWatchdog` | [ABStuckWatchdog.cs](../Source/V2/ABStuckWatchdog.cs) | `CurToilMode(Pawn)` (changed), `Report(Map,Pawn,IntVec3,int,int,bool)` (changed), `SweepInterval` (added), `Tick()` (changed), `TryBreakSelfStep(Pawn)` (added), `Watch` (changed). |
| `ABV2Debug` | [ABV2Debug.cs](../Source/V2/ABV2Debug.cs) | `Combat(string)` (changed), `StateSummary()` (changed), `TracingAllowed` (added), `Transit(string)` (changed). |
| `ABWormhole` | [ABWormhole.cs](../Source/V2/ABWormhole.cs) | `CollectPairs(Map,List<KeyValuePair<Building_Door, Building_Door>>)` (added), `lastAnchorCompsText` (added), `lastDestCompCount` (added), `lastDestCompsText` (added), `lastPlanSeedAnchor` (added), `lastPlanSeedLeg` (added), `lastPlanTotal` (added), `lastRefusal` (added), `LastRefusalText` (added), `Link(Building_Door,Building_Door)` (changed), `ProbeTarget(Map,IntVec3,LocalTargetInfo)` (added), `TransitRefusal` (added), `TryEstimateTrip(Map,IntVec3,IntVec3,Pawn,float,IntVec3,float)` (added), `TryEstimateTrip(Map,IntVec3,IntVec3,Pawn,float)` (added), `TryGetTransit(Map,IntVec3,IntVec3,Pawn,Building_Door,Building_Door,LocalTargetInfo)` (added), `TryGetTransit(Map,IntVec3,IntVec3,Pawn,Building_Door,Building_Door)` (removed), `TryGetTransitToTouch(Map,IntVec3,IntVec3,int,bool,List<Pair>,Pawn,bool,Building_Door,Building_Door)` (removed), `TryGetTransitToTouch(Map,IntVec3,IntVec3,int,bool,List<Pair>,Pawn,bool,LocalTargetInfo,Building_Door,Building_Door)` (added), `TryPlanFirstHop(Map,List<Pair>,IntVec3,IntVec3,int,HashSet<int>,bool,Pawn,bool,Building_Door,Building_Door)` (removed), `TryPlanFirstHop(Map,List<Pair>,IntVec3,IntVec3,int,HashSet<int>,bool,Pawn,bool,LocalTargetInfo,Building_Door,Building_Door)` (added). |
| `ABWormholePather` | [ABWormholePather.cs](../Source/V2/ABWormholePather.cs) | `arrivedOnMouth` (added), `BeginCrossing(Pawn,Transit)` (changed), `CarryNow(Pawn,Transit)` (changed), `CarryRopees(Pawn,Building_Door,IntVec3)` (added), `Clear(Pawn)` (changed), `ClearDoomedMark()` (added), `CollectForSave(Map)` (added), `ConsumeDoomedRequest(Pawn)` (added), `doomedCollapsed` (added), `doomedNoLinkPair` (added), `doomedOther` (added), `doomedPawn` (added), `doomedTick` (added), `DrainRestoreQueue()` (added), `failedFastNoLink` (added), `lastOutcome` (added), `lastReal` (added), `MarkNextRequestDoomed(Pawn)` (added), `MaxArrivalRecoveries` (added), `NoteIfDoomed(Pawn,LocalTargetInfo)` (added), `QueueRestore(Map,List<TransitRecord>)` (added), `RealLeg` (added), `ReconcileAfterLoad()` (added), `reconcileQueue` (added), `ResetDoomedStats()` (added), `ResetForNewGame()` (changed), `restoreQueue` (added), `segmentedOk` (added), `SegmentOutcome` (added), `TickCrossings()` (changed), `TickTransits()` (changed), `tmpHoldAdopt` (added), `Transit` (changed), `TransitRecord` (added), `TryConsumeArrival(Pawn_PathFollower,Pawn)` (changed), `TryRecoverArrival(Pawn_PathFollower,Pawn)` (added), `TrySegment(Pawn,LocalTargetInfo,PathEndMode)` (changed), `WantBeginNextToilField` (added), `WantsNextToil(JobDriver)` (added). |
| `ABWormholeRearmHook` | [ABWormhole.cs](../Source/V2/ABWormhole.cs) | `MaxConsecutiveRearmFailures` (added), `Rearm(Map)` (changed), `rearmFailures` (added). |
| `AdaptiveStorageCompat` | [AdaptiveStorageCompat.cs](../Source/Compat/Workshop/AdaptiveStorageCompat.cs) | Whole type added. |
| `Building_ABColumn` | [Building_ABColumn.cs](../Source/V2/Building_ABColumn.cs) | `ColumnDefs()` (added), `columnDefsCache` (added), `ReconcileAfterLoad(Map)` (added), `SpawnSetup(Map,bool)` (changed). The shared column implementation reconciles all spawned columns after load and identifies carrier defs through the network registry. It uses the map lister directly, so the Workshop-specific ColumnDefs helper and cache are unnecessary. |
| `Building_ABStairs2` | [Building_ABStairs2.cs](../Source/V2/Building_ABStairs2.cs) | `EstablishEnd(Map,ABBandMap,int)` (changed), `PawnCanOpen(Pawn)` (changed), `TryEstablish(Map)` (changed). |
| `CommonSenseCompat` | [CommonSenseCompat.cs](../Source/Compat/Workshop/CommonSenseCompat.cs) | Whole type added. |
| `FloatMenuOptionProvider_ABStairsTravel` | [ABStairsOrders.cs](../Source/V2/ABStairsOrders.cs) | `GetOptionsFor(Thing,FloatMenuContext)` (changed), `TravelOptionsFor(List<Pawn>,Building_ABStairs2)` (added), `TravelOptionsFor(Pawn,Building_ABStairs2)` (changed). |
| `GeologicalLandformsCompat` | [GeologicalLandformsCompat.cs](../Source/Compat/GeologicalLandformsCompat.cs) | `biomeGridEnabled` (added), `BiomeGridEnabled(Map)` (added), `biomeGridEntryAt` (added), `biomeGridSetEntry` (added), `Resolve()` (changed), `TryEntryAccessors(Map,Func<IntVec3, object>,Action<IntVec3, object>)` (added). |
| `GiddyUpCompat` | [GiddyUpCompat.cs](../Source/Compat/Workshop/GiddyUpCompat.cs) | Whole type added. |
| `LargePawnsCompat` | [LargePawnsCompat.cs](../Source/Compat/Workshop/LargePawnsCompat.cs) | Whole type added. |
| `Patch_AdvancedGameConfig_ABMapSizeLock` | [ABMapSizeLimit.cs](../Source/V2/ABMapSizeLimit.cs) | `DrawLevelChooser(Rect,Window)` (changed). |
| `Patch_AttackTargetFinder_ABCrossBand` | [ABCombatAcquisition.cs](../Source/V2/ABCombatAcquisition.cs) | `Postfix(IAttackTargetSearcher,TargetScanFlags,Predicate<Thing>,float,float,IntVec3,float,bool,bool,bool,bool,IAttackTarget)` (added), `Postfix(IAttackTargetSearcher,TargetScanFlags,Predicate<Thing>,float,float,IntVec3,float,bool,bool,IAttackTarget)` (removed). |
| `Patch_CameraDriver_ABClipViewToBand` | [ABBandView.cs](../Source/V2/ABBandView.cs) | `Postfix(CellRect)` (changed). |
| `Patch_CastPositionFinder_ABCrossBand` | [ABCombatPosition.cs](../Source/V2/ABCombatPosition.cs) | `Finalizer(ABCombatPosition.ScopeState)` (added), `Postfix(CastPositionRequest,IntVec3,bool,ABCombatPosition.ScopeState)` (added), `Prefix(CastPositionRequest,ABCombatPosition.ScopeState)` (added), `Prefix(CastPositionRequest,IntVec3,bool)` (removed), `Transpiler(IEnumerable<CodeInstruction>)` (added). |
| `Patch_CastPositionFinder_ABEvaluateCellBandLocal` | [Patch_CastPositionFinder_ABEvaluateCellBandLocal.cs](../Source/V2/Workshop/Patch_CastPositionFinder_ABEvaluateCellBandLocal.cs) | Whole type added. |
| `Patch_CastPositionFinder_ABPreferenceBandLocal` | [Patch_CastPositionFinder_ABPreferenceBandLocal.cs](../Source/V2/Workshop/Patch_CastPositionFinder_ABPreferenceBandLocal.cs) | Whole type added. |
| `Patch_CE_AdjustShotHeight_ABTargetLevel` | [Patch_CE_AdjustShotHeight_ABTargetLevel.cs](../Source/Compat/Workshop/Patch_CE_AdjustShotHeight_ABTargetLevel.cs) | Whole type added. |
| `Patch_CE_CoverWalk_ABTargetLevel` | [Patch_CE_CoverWalk_ABTargetLevel.cs](../Source/Compat/Workshop/Patch_CE_CoverWalk_ABTargetLevel.cs) | Whole type added. |
| `Patch_CE_FireArc_ABBandLocalBearing` | [Patch_CE_FireArc_ABBandLocalBearing.cs](../Source/Compat/Workshop/Patch_CE_FireArc_ABBandLocalBearing.cs) | Whole type added. |
| `Patch_CE_HuntRange_ABBandLocal` | [Patch_CE_HuntRange_ABBandLocal.cs](../Source/Compat/Workshop/Patch_CE_HuntRange_ABBandLocal.cs) | Whole type added. |
| `Patch_CE_ProjectileLaunch_ABCrossBand` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `Prefix(Thing,Thing,Vector2)` (changed), `Prepare()` (changed), `TargetMethod()` (changed). |
| `Patch_CE_ProjectileRayCast_ABCrossBand` | [Patch_CE_ProjectileRayCast_ABCrossBand.cs](../Source/Compat/Workshop/Patch_CE_ProjectileRayCast_ABCrossBand.cs) | Whole type added. |
| `Patch_CE_ShiftVecReport_ABCrossBand` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `Finalizer(ABCECompat.EyeScope)` (added), `Postfix(object,int)` (removed), `Postfix(Verb,IntVec3,object,ABCECompat.EyeScope)` (added), `Prefix(Verb,LocalTargetInfo,ABCECompat.EyeScope)` (added), `Prefix(Verb,LocalTargetInfo,IntVec3,int)` (removed), `Prepare()` (changed). |
| `Patch_CE_ShootLine_ABCrossBand` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `Prefix(Verb,IntVec3,LocalTargetInfo,ShootLine,Vector3,bool)` (changed). |
| `Patch_CE_TurretOrderAttack_ABBandLocalRange` | [Patch_CE_TurretOrderAttack_ABBandLocalRange.cs](../Source/Compat/Workshop/Patch_CE_TurretOrderAttack_ABBandLocalRange.cs) | Whole type added. |
| `Patch_CellFinder_ABRandomNotEdgeCell` | [ABBandSafety.cs](../Source/V2/ABBandSafety.cs) | Whole type added. Uses the shared pending/live surface geometry and returns Invalid if the requested margin leaves no cell. |
| `Patch_CommonSense_ABHaulOverBillDistance` | [Patch_CommonSense_ABHaulOverBillDistance.cs](../Source/V2/Workshop/Patch_CommonSense_ABHaulOverBillDistance.cs) | Whole type added. |
| `Patch_CoverUtility_ABCrossBandBlockChance` | [Patch_CoverUtility_ABCrossBandBlockChance.cs](../Source/V2/Workshop/Patch_CoverUtility_ABCrossBandBlockChance.cs) | Whole type added. |
| `Patch_CoverUtility_ABCrossBandCoverGivers` | [Patch_CoverUtility_ABCrossBandCoverGivers.cs](../Source/V2/Workshop/Patch_CoverUtility_ABCrossBandCoverGivers.cs) | Whole type added. |
| `Patch_CoverUtility_ABNoPointBlankFromBelow` | [Patch_CoverUtility_ABNoPointBlankFromBelow.cs](../Source/V2/Workshop/Patch_CoverUtility_ABNoPointBlankFromBelow.cs) | Whole type added. 1.7 arms the below-shooter cover rule when the expected IL pattern occurs at least once. |
| `Patch_DesignationManager_ABCaptureArea` | [Patch_DesignationManager_ABCaptureArea.cs](../Source/V2/Workshop/Patch_DesignationManager_ABCaptureArea.cs) | Whole type added. |
| `Patch_EdificeGrid_ABDeRegister` | [ABSkySync.cs](../Source/V2/ABSkySync.cs) | `Postfix(EdificeGrid,Building)` (changed). |
| `Patch_EdificeGrid_ABRegister` | [ABSkySync.cs](../Source/V2/ABSkySync.cs) | `Postfix(EdificeGrid,Building)` (changed). |
| `Patch_FloodFiller_ABGLAnchorClamp` | [Patch_FloodFiller_ABGLAnchorClamp.cs](../Source/V2/Workshop/Patch_FloodFiller_ABGLAnchorClamp.cs) | Whole type added. |
| `Patch_FloodFiller_ABSmartDeconBand` | [Patch_FloodFiller_ABSmartDeconBand.cs](../Source/V2/Workshop/Patch_FloodFiller_ABSmartDeconBand.cs) | Whole type added. |
| `Patch_FoodUtility_ABFoodOptimalityDistance` | [Patch_FoodUtility_ABFoodOptimalityDistance.cs](../Source/V2/Workshop/Patch_FoodUtility_ABFoodOptimalityDistance.cs) | Whole type added. |
| `Patch_FoodUtility_ABInnerScanContext` | [Patch_FoodUtility_ABInnerScanContext.cs](../Source/V2/Workshop/Patch_FoodUtility_ABInnerScanContext.cs) | Whole type added. Per-scan Harmony state also preserves nested validator calls, including nested scans on ordinary maps. |
| `Patch_GenClosest_ABGlobalReachableBandDistance` | [Patch_GenClosest_ABGlobalReachableBandDistance.cs](../Source/V2/Workshop/Patch_GenClosest_ABGlobalReachableBandDistance.cs) | Whole type added. Uses an independent route memo during nested searches so validators cannot change the outer search context. |
| `Patch_GenDraw_ABLiftTargetHighlightThing` | [Patch_GenDraw_ABLiftTargetHighlightThing.cs](../Source/V2/Workshop/Patch_GenDraw_ABLiftTargetHighlightThing.cs) | Whole type added. |
| `Patch_GenSpawn_ABNoAirSpawn` | [ABAirSpawnGuard.cs](../Source/V2/ABAirSpawnGuard.cs) | `Prefix(Thing,IntVec3,Map,Rot4,bool)` (changed). |
| `Patch_GenStep_ABSurfaceBandScope` | [ABBandDressing.cs](../Source/V2/ABBandDressing.cs) | `Finalizer(Exception,bool)` (added), `Postfix(bool)` (removed). |
| `Patch_GenStep_Scatterer_ABBandSeam` | [ABBandSafety.cs](../Source/V2/ABBandSafety.cs) | `Postfix(GenStep_Scatterer,IntVec3,Map,bool)` (changed). |
| `Patch_GiddyUp_ABAutoMountDistance` | [Patch_GiddyUp_ABAutoMountDistance.cs](../Source/V2/Workshop/Patch_GiddyUp_ABAutoMountDistance.cs) | Whole type added. |
| `Patch_GiddyUp_ABBestAnimalDistance` | [Patch_GiddyUp_ABBestAnimalDistance.cs](../Source/V2/Workshop/Patch_GiddyUp_ABBestAnimalDistance.cs) | Whole type added. |
| `Patch_GL_ABBiomeTransitionScope` | [GeologicalLandformsCompat.cs](../Source/Compat/GeologicalLandformsCompat.cs) | `AnchorSize(Map)` (added), `ClampRowsFor(Map)` (added), `fillsClamped` (added), `Finalizer(bool)` (added), `Prefix(Map,bool)` (added), `Prefix(Map)` (removed), `reported` (added), `scopeDepth` (added), `scopeMap` (added), `scopeRows` (added), `sizeSwaps` (added), `Transpiler(IEnumerable<CodeInstruction>)` (added). |
| `Patch_IntVec3Utility_ABBandDistanceToEdge` | [ABBandSafety.cs](../Source/V2/ABBandSafety.cs) | Whole type added. Uses the shared band rectangle resolver, including pending generation. |
| `Patch_LargePawns_ABCrossLevelBlockChance` | [Patch_LargePawns_ABCrossLevelBlockChance.cs](../Source/V2/Workshop/Patch_LargePawns_ABCrossLevelBlockChance.cs) | Whole type added. |
| `Patch_LargePawns_ABCrossLevelCoverGivers` | [Patch_LargePawns_ABCrossLevelCoverGivers.cs](../Source/V2/Workshop/Patch_LargePawns_ABCrossLevelCoverGivers.cs) | Whole type added. |
| `Patch_LightingOverlay_ABSuppressOnBanded` | [SectionLayer_ABBelowLighting.cs](../Source/V2/SectionLayer_ABBelowLighting.cs) | `LetVanillaDraw` (added), `Postfix(SectionLayer_LightingOverlay,bool)` (changed). |
| `Patch_LordMaker_ABDefendBaseAnchor` | [Patch_LordMaker_ABDefendBaseAnchor.cs](../Source/V2/Workshop/Patch_LordMaker_ABDefendBaseAnchor.cs) | Whole type added. |
| `Patch_MapGenUtility_ABBandBiomeAt` | [Patch_MapGenUtility_ABBandBiomeAt.cs](../Source/V2/Workshop/Patch_MapGenUtility_ABBandBiomeAt.cs) | Whole type added. |
| `Patch_PathFinder_ABHeuristicStrength` | [ABPathBandScope.cs](../Source/V2/ABPathBandScope.cs) | `Postfix(Pawn,float)` (changed). |
| `Patch_PathFinder_ABRejectCrossBandAsync` | [ABPathBandScope.cs](../Source/V2/ABPathBandScope.cs) | `Prefix(PathRequest)` (changed). |
| `Patch_PathFinder_ABRejectCrossBandSync` | [ABPathBandScope.cs](../Source/V2/ABPathBandScope.cs) | `Prefix(Map,IntVec3,LocalTargetInfo,PathEndMode,PawnPath)` (added), `Prefix(Map,IntVec3,LocalTargetInfo,PawnPath)` (removed). |
| `Patch_PathFollower_ABStartPath` | [ABWormholePather.cs](../Source/V2/ABWormholePather.cs) | `Postfix()` (added), `Prefix(Pawn,LocalTargetInfo,PathEndMode)` (changed). |
| `Patch_PawnRenderer_ABBelowShrink` | [ABBelowShrink.cs](../Source/V2/ABBelowShrink.cs) | `Postfix(PawnDrawParms)` (changed). |
| `Patch_ProgressRenderer_ABNoFullStackCapture` | [Patch_ProgressRenderer_ABNoFullStackCapture.cs](../Source/V2/Workshop/Patch_ProgressRenderer_ABNoFullStackCapture.cs) | Whole type added. |
| `Patch_ProgressRenderer_ABPinCaptureBand` | [Patch_ProgressRenderer_ABPinCaptureBand.cs](../Source/V2/Workshop/Patch_ProgressRenderer_ABPinCaptureBand.cs) | Whole type added. |
| `Patch_SectionLayer_Terrain_ABUnmaskShadows` | [SectionLayer_ABBelowShadows.cs](../Source/V2/SectionLayer_ABBelowShadows.cs) | `Postfix(SectionLayer_Terrain)` (changed), `RebuildVoidMask(Map,ABBandMap,Section,LayerSubMesh)` (added), `SpaceMaskMode` (added). |
| `Patch_ShamblerFight_ABCrossLevelMelee` | [Patch_ShamblerFight_ABCrossLevelMelee.cs](../Source/V2/Workshop/Patch_ShamblerFight_ABCrossLevelMelee.cs) | Whole type added. |
| `Patch_ShotReport_ABBandLocalDistance` | [Patch_ShotReport_ABBandLocalDistance.cs](../Source/V2/Workshop/Patch_ShotReport_ABBandLocalDistance.cs) | Whole type added. |
| `Patch_ShotReport_ABCrossBandDistance` | [Patch_ShotReport_ABBandLocalDistance.cs](../Source/V2/Workshop/Patch_ShotReport_ABBandLocalDistance.cs) | Whole type removed. Removed. Patch_ShotReport_ABBandLocalDistance now applies to the expanded shooting and cover model. |
| `Patch_SmartDecon_ABScopeRoofScan` | [Patch_SmartDecon_ABScopeRoofScan.cs](../Source/V2/Workshop/Patch_SmartDecon_ABScopeRoofScan.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABEncloseScope` | [Patch_SmarterConstruction_ABEncloseScope.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABEncloseScope.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABPocketHasExit` | [Patch_SmarterConstruction_ABPocketHasExit.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABPocketHasExit.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABRankingDistance` | [Patch_SmarterConstruction_ABRankingDistance.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABRankingDistance.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABRankingScope` | [Patch_SmarterConstruction_ABRankingScope.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABRankingScope.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABSafeSpotScope` | [Patch_SmarterConstruction_ABSafeSpotScope.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABSafeSpotScope.cs) | Whole type added. |
| `Patch_SmarterConstruction_ABStuckDuringTransit` | [Patch_SmarterConstruction_ABStuckDuringTransit.cs](../Source/V2/Workshop/Patch_SmarterConstruction_ABStuckDuringTransit.cs) | Whole type added. |
| `Patch_TileMutatorWorker_Plateau_ABBandSize` | [ABBandLocal.cs](../Source/V2/ABBandLocal.cs) | Whole type added. Uses the fork’s shared pending geometry and preserves Harmony labels and exception blocks on replacement. |
| `Patch_TileMutatorWorker_River_ABBandEdgeNodes` | [Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs](../Source/V2/Workshop/Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs) | Whole type added. |
| `Patch_ToilsIngest_ABCrossLevelChair` | [Patch_ToilsIngest_ABCrossLevelChair.cs](../Source/V2/Workshop/Patch_ToilsIngest_ABCrossLevelChair.cs) | Whole type added. |
| `Patch_Translate_ABSurfaceCentre` | [Patch_Translate_ABSurfaceCentre.cs](../Source/V2/Workshop/Patch_Translate_ABSurfaceCentre.cs) | Whole type added. |
| `Patch_Verb_ABCrossBandShootLine` | [ABCombatV2.cs](../Source/V2/ABCombatV2.cs) | `Prefix(Verb,IntVec3,LocalTargetInfo,ShootLine,bool,bool)` (changed). |
| `Patch_VGE_ArtilleryEdgeCell_ABBand` | [Patch_VGE_ArtilleryEdgeCell_ABBand.cs](../Source/V2/Workshop/Patch_VGE_ArtilleryEdgeCell_ABBand.cs) | Whole type added. |
| `Patch_VGE_ArtillerySpawnCell_ABBand` | [Patch_VGE_ArtillerySpawnCell_ABBand.cs](../Source/V2/Workshop/Patch_VGE_ArtillerySpawnCell_ABBand.cs) | Whole type added. |
| `Patch_VGE_ArtilleryTick_ABBandExit` | [Patch_VGE_ArtilleryTick_ABBandExit.cs](../Source/V2/Workshop/Patch_VGE_ArtilleryTick_ABBandExit.cs) | Whole type added. |
| `Patch_WanderUtility_ABKeepRootInBand` | [ABBandWander.cs](../Source/V2/ABBandWander.cs) | `BandHasColonyPresence(Map,Pawn)` (added), `Postfix(Pawn,IntVec3)` (changed), `TryColonistRoot(Map,Pawn,IntVec3)` (added). |
| `Patch_WorldCameraDriver_ABBandParallax` | [Patch_WorldCameraDriver_ABBandParallax.cs](../Source/V2/Workshop/Patch_WorldCameraDriver_ABBandParallax.cs) | Whole type added. |
| `PlaceWorker_ABLinkApproach` | [PlaceWorker_ABLinkApproach.cs](../Source/V2/PlaceWorker_ABLinkApproach.cs) | `AllowsPlacing(BuildableDef,IntVec3,Rot4,Map,Thing,Thing)` (changed). |
| `ProgressRendererCompat` | [ProgressRendererCompat.cs](../Source/Compat/Workshop/ProgressRendererCompat.cs) | Whole type added. |
| `SectionLayer_ABBelowLighting` | [SectionLayer_ABBelowLighting.cs](../Source/V2/SectionLayer_ABBelowLighting.cs) | `ApplyVoidCut(Map,ABBandMap,CellRect)` (added), `bakedRect` (added), `BuildColors(Map,ABBandMap,CellRect)` (changed), `cutScratch` (added), `fullTris` (added), `Regenerate()` (changed), `Release()` (changed), `ReportAt(IntVec3)` (added), `voidCut` (added), `VoidCut` (added), `ZeroColors` (added). |
| `SectionLayer_ABBelowV2` | [SectionLayer_ABBelowV2.cs](../Source/V2/SectionLayer_ABBelowV2.cs) | `EmitBelowFogFan(Map,ABBandMap,FogGrid,IntVec3,IntVec3,float)` (changed), `FoggedForView(Map,ABBandMap,FogGrid,IntVec3,bool)` (added), `FoggedForView(Map,ABBandMap,FogGrid,IntVec3)` (removed), `foreignDrops` (added), `PrintBelowTerrain(Map,TerrainGrid,IntVec3,IntVec3,float,int,int,int,bool,bool)` (changed), `Regenerate()` (changed). |
| `SectionLayer_ABMountainCap` | [SectionLayer_ABMountainCap.cs](../Source/V2/SectionLayer_ABMountainCap.cs) | `AddCornerFiller(LayerSubMesh,Map,IntVec3,int,int,float,Color32)` (changed), `AddInteriorTileWithFillers(LayerSubMesh,Map,IntVec3,float,Color32)` (added), `EdgeShadowed` (changed), `EmitMassSilhouetteAt(MapDrawLayer,Map,IntVec3,int,float,bool[],TerrainDef[])` (changed), `EmitMeadowFadeAt(MapDrawLayer,Map,TerrainGrid,IntVec3,int,float,bool[],TerrainDef[])` (changed), `LedgeInteriorTileFor(Map,ABBandMap,IntVec3)` (added), `LedgeRockTile` (added), `MassMaskForProbe(Map,IntVec3)` (added), `Regenerate()` (changed), `SouthFillerStretch` (added), `StoneBorderRule` (added). |
| `SectionLayer_ABRockRootFill` | [SectionLayer_ABRockRootFill.cs](../Source/V2/Workshop/SectionLayer_ABRockRootFill.cs) | Whole type added. |
| `SkyFXCompat` | [SkyFXCompat.cs](../Source/Compat/SkyFXCompat.cs) | `AreaFactor(float)` (added), `fCoverageHeight` (added), `fCoverageMode` (added), `fDynamicBurstCount` (added), `Fold(float,float)` (removed), `InstallSunAndMoonBeams()` (changed), `RemapBeam(Vector3,Map)` (changed), `RescaleToBand(object,Map)` (added). |
| `SmarterConstructionCompat` | [SmarterConstructionCompat.cs](../Source/Compat/Workshop/SmarterConstructionCompat.cs) | Whole type added. |
| `SmarterDeconstructionCompat` | [SmarterDeconstructionCompat.cs](../Source/Compat/Workshop/SmarterDeconstructionCompat.cs) | Whole type added. |
| `VanillaGravshipCompat` | [VanillaGravshipCompat.cs](../Source/Compat/Workshop/VanillaGravshipCompat.cs) | Whole type added. |

### Steam 1.6 → Steam 1.7

| Type | Source | Members and disposition |
| --- | --- | --- |
| `ABBandCurtain` | [ABBandCurtain.cs](../Source/V2/ABBandCurtain.cs) | `DepthCurtain` (changed). Uses Unity’s depth shader on 1.6 and Waylith’s solid color shader with version-specific material properties on 1.7. |
| `ABBandInput` | [ABBandInput.cs](../Source/V2/ABBandInput.cs) | `HandleScroll(Map)` (changed). The shared enum constant encodes the same ScrollWheel value on both engines. |
| `ABBandMap` | [ABBandMap.cs](../Source/V2/ABBandMap.cs) | `FinalizeInit()` (changed), `SeeBelowVersion` (changed). Ports persisted transit and world cleanup, GL synchronization and 1.7 pollution cleanup. Keeps the fork’s all-map carrier reconciliation. |
| `ABBands` | [ABBands.cs](../Source/V2/ABBands.cs) | `TryResolveVisibleBelow(Map,ABBandMap,IntVec3,IntVec3,int)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABBelowForeignLayers` | [ABBelowForeignLayers.cs](../Source/V2/Workshop/ABBelowForeignLayers.cs) | `Make(Map,Section,int,Type[])` (changed). Selects the appropriate uninitialized allocation API per runtime. |
| `ABCECompat` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `FlightDir(float)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABCombatRelay` | [ABCombatRelay.cs](../Source/V2/ABCombatRelay.cs) | `TryTakeSolution(IntVec3,int,int,ABShotSolution)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABDevTools` | [ABDevTools.Workshop.cs](../Source/V2/ABDevTools.Workshop.cs), [ABDevTools.V2.cs](../Source/V2/ABDevTools.V2.cs) | `probeSeenCalls` (added), `V2CheckPollutionCount()` (added), `V2SelectionDrawProbe()` (added). Existing partial diagnostic files and ABDevTools.Workshop.cs cover the changed actions. The 1.7 selection and pollution probes use the fork’s runtime counters and pollutable-count API. |
| `ABLineTrap` | [ABLineTrap.cs](../Source/V2/ABLineTrap.cs) | `Inspect(Vector3,Vector3)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABPollutionCount` | [ABPollutionCount.cs](../Source/V2/ABPollutionCount.cs) | Whole type added. 1.7 weak per-map cache, terrain/building and gravship invalidation, open-air cleanup and a defined zero-cell result. |
| `ABShamblerCrossLevel` | [ABShamblerCrossLevel.cs](../Source/V2/Workshop/ABShamblerCrossLevel.cs) | `ResetForNewGame()` (added), `TryAnswer(Pawn,Thing,Job,bool)` (changed). Shared cache reset. The 1.7 fallback route also checks passage at both connectors. |
| `ABStairAnim` | [ABStairAnim.cs](../Source/V2/ABStairAnim.cs) | `EntryPose(Clip,float,int)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABStuckWatchdog` | [ABStuckWatchdog.cs](../Source/V2/ABStuckWatchdog.cs) | `Report(Map,Pawn,IntVec3,int,int,bool)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `ABWaterBand` | [ABWaterV2.cs](../Source/V2/ABWaterV2.cs) | `Report(Map)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Building_ABColumn` | [Building_ABColumn.cs](../Source/V2/Building_ABColumn.cs) | `ColumnReport(Map)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `DubsMintMinimapCompat` | [DubsMintMinimapCompat.cs](../Source/Compat/DubsMintMinimapCompat.cs) | `DrawBandMinimap(object,Rect)` (changed). The shared enum constant encodes the same Repaint value on both engines. |
| `Patch_CE_ProjectileLaunch_ABCrossBand` | [CombatExtendedCompat.cs](../Source/Compat/CombatExtendedCompat.cs) | `Prefix(Thing,Thing,Vector2)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Patch_CE_ProjectileRayCast_ABCrossBand` | [Patch_CE_ProjectileRayCast_ABCrossBand.cs](../Source/Compat/Workshop/Patch_CE_ProjectileRayCast_ABCrossBand.cs) | `Prefix(Thing,Thing,Vector2,float,float,float)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Patch_CoverUtility_ABNoPointBlankFromBelow` | [Patch_CoverUtility_ABNoPointBlankFromBelow.cs](../Source/V2/Workshop/Patch_CoverUtility_ABNoPointBlankFromBelow.cs) | `Transpiler(IEnumerable<CodeInstruction>)` (changed). 1.7 arms the below-shooter cover rule when the expected IL pattern occurs at least once. |
| `Patch_DubsPipes_ABRiserLink` | [DubsPipeCompat.cs](../Source/Compat/DubsPipeCompat.cs) | `CounterReport()` (changed), `TargetsInternal()` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Patch_PawnFlyer_ABCrossBandArc` | [ABBandLeap.cs](../Source/V2/ABBandLeap.cs) | `Postfix(PawnFlyer,Origin)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Patch_PollutionGrid_ABCachedPollutableCount` | [ABPollutionCount.cs](../Source/V2/ABPollutionCount.cs) | Whole type added. 1.7 Harmony getter prefix in ABPollutionCount.cs. |
| `Patch_Projectile_ABCrossBandOrigin` | [ABCombatV2.cs](../Source/V2/ABCombatV2.cs) | `Postfix(Projectile,string)` (changed), `Prefix(Projectile,Thing,Vector3,LocalTargetInfo,string)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Patch_SelectionBrackets_ABBelow` | [ABBelowSelectionDraw.cs](../Source/V2/ABBelowSelectionDraw.cs), [Patch_SelectionBrackets_ABBelow.cs](../Source/V2/Workshop/Patch_SelectionBrackets_ABBelow.cs) | Type attributes and declaration, `Calc(Vector3[],object,Vector3,Vector2,Dictionary<object, float>,Vector2,float,float)` (added), `calls` (added), `lastIn` (added), `lastOut` (added), `liftDelta` (added), `LiftPivot(Vector3)` (added), `LocalIndex(CodeInstruction)` (added), `localized` (added), `pivotsLifted` (added), `Prefix(Vector3)` (removed), `rewritten` (added), `TargetMethod()` (removed), `Transpiler(IEnumerable<CodeInstruction>)` (added). 1.6 keeps the generic method prefix. 1.7 redirects the concrete drawer’s call and applies the same displacement to both pivot reads. |
| `Patch_TerrainGrid_ABSeeBelowDirtyGravship` | [ABSeeBelowDirty.cs](../Source/V2/ABSeeBelowDirty.cs) | `Postfix(Map)` (changed). Invalidates the 1.7 pollution cache in addition to the below-view cache. |
| `Patch_ThingOverlays_ABBelow` | [ABBelowOverlays.cs](../Source/V2/ABBelowOverlays.cs) | `Postfix()` (changed). The shared enum constant encodes the same Repaint value on both engines. |
| `Patch_TileMutatorWorker_River_ABBandEdgeNodes` | [Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs](../Source/V2/Workshop/Patch_TileMutatorWorker_River_ABBandEdgeNodes.cs) | `Postfix(TileMutatorWorker_River,Map,float,(Vector3, Vector3))` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |
| `Showcase` | [Showcase.cs](../Source/V2/Workshop/Showcase.cs) | Whole type added. 1.7 debug-only in-game feature controlled by the existing developer menu, including staged scenes and teardown. |
| `ShowcaseUnloadWatcher` | [ShowcaseUnloadWatcher.cs](../Source/V2/Workshop/ShowcaseUnloadWatcher.cs) | Whole type added. 1.7 engine lifecycle hook to stop the Showcase on unload. |
| `Window_ABTextureTuner` | [ABTextureTuner.cs](../Source/V2/ABTextureTuner.cs) | `DoWindowContents(Rect)` (changed), `XmlFor(ThingDef)` (changed). Compiler or runtime-library differences only. Shared source retains the same behavior. |

## Verification

- Both Release targets build with zero warnings and errors. The XML trees parse and each selected load-folder set contains exactly one compiled mod DLL. All 52 compressed texture headers and dimensions match their PNG counterparts.
- RimWorld 1.7 runtime testing uses the standalone launcher, disposable profiles, Biotech, the local Harmony v3.0.1 snapshot and the framework adapter in [Tests/WorkshopParity.cs](../Tests/WorkshopParity.cs). The framework validated `port-workshop-supported` on 1.7.4985 rev1269 with a verified target DLL hash `5f3cb1e4d617f18b01946e7a44c442576b4e1a4cdc2aa0204da5e541f7815672`, zero unexpected game errors and successful cleanup. Steam initialization failed as expected in the zero-app-ID standalone copy. Nothing in the installed game or its app IDs was changed.
- The focused scenario checks an assigned cross-floor Goto, power carriers on both ends, removal of a deliberately duplicated legacy carrier after save/reload, the cached denominator against the engine’s pollutable-cell scan, and legacy polluted-air cleanup. The other fixture inputs and limits are declared in the adapter.
- The original quicktest failed at Harmony v3.0.4 and duplicate mod discovery. An initial framework calibration used a fixture that required unrelated extensions, and early fixture iterations supplied an invalid material or lacked stable support for the upper test floor. Those are retained as failed attempts. No 1.6 runtime test was run because the authorized runtime installation is the 1.7 `Game` directory.
- Optional-mod combinations, visuals, orbital generation, scripted Showcase scenes, and all combat variants remain outside the focused runtime coverage. The member inventory documents the implementation targets and is not evidence that those combinations were exercised.
