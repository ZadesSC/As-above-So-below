using System.Collections.Generic;
using AsAboveSoBelow;
using Verse;

namespace AASBVehicleRamps;

/// <summary>Vehicle ramp defs, resolved once after defs load.</summary>
public static class RampDefs
{
    private static readonly HashSet<ThingDef> s_ramps = new HashSet<ThingDef>();
    private static readonly List<ThingDef> s_upRamps = new List<ThingDef>();

    public static IReadOnlyList<ThingDef> UpRamps => s_upRamps;

    public static int Count => s_ramps.Count;

    public static bool IsRamp(ThingDef def) => def != null && s_ramps.Contains(def);

    /// <summary>Collects ramp defs; returns a reason string when a marked def is not a usable AASB link.</summary>
    public static string Resolve()
    {
        s_ramps.Clear();
        s_upRamps.Clear();
        foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (def.GetModExtension<ABVehicleRampExt>() == null)
            {
                continue;
            }

            var link = def.GetModExtension<ABBandStairsExt>();
            if (!typeof(Building_ABStairs2).IsAssignableFrom(def.thingClass) || link == null || link.linksAllLevels
                || link.levelDelta == 0)
            {
                return "def " + def.defName + " is marked as a vehicle ramp but is not a single-level AASB link";
            }

            s_ramps.Add(def);
            if (link.levelDelta > 0)
            {
                s_upRamps.Add(def);
            }
        }

        return s_upRamps.Count == 0 ? "no vehicle ramp up def found" : null;
    }
}
