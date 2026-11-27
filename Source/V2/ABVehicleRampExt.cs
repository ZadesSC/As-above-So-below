using Verse;

namespace AsAboveSoBelow;

/// <summary>
/// Marks an AASB link def (a <c>Building_ABStairs2</c> thing class) as a vehicle ramp. Only defs carrying this
/// extension contribute vehicle portal edges; every other stair, ladder and door is left alone.
/// </summary>
public class ABVehicleRampExt : DefModExtension
{
}
