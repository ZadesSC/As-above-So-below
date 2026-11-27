# Vehicle ramps

Research Staircases, then build a ramp up or down in Structure. Completing the
5×5 ramp creates a matching ramp one floor away. Colonists can walk through both
ends. With Vehicle Framework active, draft a ground vehicle and order it to a
destination on another floor. Leave room for its full footprint and length at
both mouths. A forbidden ramp closes the crossing. The AASB transit animation
setting also controls vehicle ramp animation.

Vehicles use a ramp one at a time. A vehicle about to drive into a ramp's mouth
area waits one step short while another vehicle is using the ramp, in either
direction. It drives on once that vehicle has crossed and left both mouth
areas. Waiting vehicles go in arrival order. A vehicle that is already inside a
mouth area when it reaches the ramp cannot stop short; it waits at the crossing
and may give up as before.

The fork bundles the 0.4.0 Ramp code and textures from RimWorld-Patches at
`2aca85189069828ce5a54b48fae21f3f99f5f425`. The MIT notice and texture credit
are in [the bundled license](../1.7/VehicleRamps/LICENSE). Vehicle support
requires Vehicle Framework's PathFinderV2. The supported behavior is drafted
ground vehicle movement. If a startup binding fails after a VF update, the
ramp backend reports the failure and the buildings continue to serve pawns.

## Existing colonies

The standalone add-on `zades.aasb.vehicleramps` can stay enabled during the
upgrade. While it is active, it supplies both ramp defs and vehicle patches.
To switch to the bundled implementation, back up the save, disable the
standalone add-on and accept RimWorld's changed-mod-list prompt. The bundled
defs retain the same names, class, geometry and counterpart references.
Colonists can still use existing ramps on a load order without Vehicle Framework.

## Release build

Build [the core project](../Source/ModSource.csproj) first. Then run the command
below with a path to a supported Vehicle Framework installation. Both projects
target RimWorld 1.7. The optional build uses compile-only VF references and
ships `AASBVehicleRamps.dll` under `1.7/VehicleFramework/Assemblies/`.

```sh
dotnet build Source/VehicleRamps/AASBVehicleRamps.csproj -c Release \
  -p:VehicleFrameworkDir=/absolute/path/to/VehicleFramework
```

## In-game regression checks

Use the RimWorld Test Framework and the standalone RimWorld 1.7 installation.
The existing tests in RimWorld-Patches under `Mods/AASBVehicleRamps/Tests`
cover normal drafted move orders, clearance and waits, rotation and facing,
multi-floor routing, reachability, animation, and save/reload.

- Generate the existing adapter with `prepare_adapter.py`. For the bundled run,
  remove the standalone package from the chosen test configuration and from
  the adapter's `RequiredPackages`. Reference the fork's core and optional DLLs.
- Append [VehicleRampIntegration.cs](../Tests/VehicleRampIntegration.cs) to the
  generated adapter, merging its `using` directives at the top. Run
  `ramps.fork-integration` on the two-floor fixture to check native pawn travel,
  construction into a closed floor, the single content owner and reload.
- For a pawn-only build of the integration scenario, combine it with the
  upstream `RampFixture.cs`. Run once with Harmony and the fork only, and once
  with Vehicle Framework and the standalone add-on enabled. The scenario
  checks which package supplied the defs and which supplied the vehicle DLL.
- To exercise migration, take the standalone run's `candidate.rws` and its
  `fixture-state.xml` sidecar. Run the same case with the bundled configuration
  and pass `--fixture /absolute/path/to/candidate.rws`. The test finds the
  original ramp buildings by saved ID and repeats the pawn orders.
- Append [VehicleRampTraffic.cs](../Tests/VehicleRampTraffic.cs) the same way
  for the ramp traffic cases on the two-floor fixture. Its cases drop the
  standalone package from `RequiredPackages`.

| Case | Checks |
| --- | --- |
| `ramps.traffic-pair` | Two MRAPs ordered on the same tick both climb. No refusals, never two vehicles in the mouth areas of one ramp, no claim left. Whether one has to wait depends on path timing |
| `ramps.traffic-trio` | Two MRAPs and a CV90 ordered together climb in turn |
| `ramps.traffic-opposed` | One vehicle climbs while another descends, ordered on the same tick. Both swap floors |
| `ramps.traffic-opposed-queued` | The descent is ordered once the climber holds the ramp. The descender waits outside the upper mouth and the climber drives around it |
| `ramps.traffic-waiter-cancel` | Undrafting the vehicle waiting in line drops it from the line within a few ticks. The holder crosses and nothing is left behind |
| `ramps.traffic-cleanup` | Undrafting the vehicle holding the ramp hands it to the waiting vehicle, which crosses |
