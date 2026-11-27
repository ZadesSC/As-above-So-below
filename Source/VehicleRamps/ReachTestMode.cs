namespace AASBVehicleRamps;

/// <summary>
/// Test-only reachability controls (<see cref="RampDiagnostics.ReachMode"/>). Never changed by the mod.
/// </summary>
public enum ReachTestMode
{
    /// <summary>Ramps are region links inside Vehicle Framework's region search (the shipped behaviour).</summary>
    Normal,

    /// <summary>No ramp links: Vehicle Framework's own answer, to show that a layout is split without ramps.</summary>
    NativeOnly,

    /// <summary>
    /// Comparison with 0.2.0: no region links, and the recursive chain can answer any cross-floor traverse mode.
    /// The comparison uses the current crossing geometry and clearance rules.
    /// </summary>
    LegacyChain,
}
