using System;
using HarmonyLib;
using Verse;
using Verse.Noise;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(Translate), MethodType.Constructor, new Type[] { typeof(double), typeof(double), typeof(double), typeof(ModuleBase) })]
public static class Patch_Translate_ABSurfaceCentre
{
    internal static int redirected;
    private const double Tolerance = 0.5;
    private static void Prefix(double x, ref double z)
    {
        try
        {
            Map mapBeingGenerated = MapGenerator.mapBeingGenerated;
            if (mapBeingGenerated == null || !ABGuard.On(ABGuard.LevelGen) || ABBandScope.Active || ABBandedGeneration.CarveInProgress || !ABBandLocal.TryBandGeometry(mapBeingGenerated, out var bandHeight, out var _, out var offset))
            {
                return;
            }

            double num = (double)mapBeingGenerated.Size.z / 2.0;
            if (!(Math.Abs(z + num) > 0.5) && !(Math.Abs(x + (double)mapBeingGenerated.Size.x / 2.0) > 0.5))
            {
                IntVec3 intVec = ((mapBeingGenerated.waterInfo != null) ? mapBeingGenerated.waterInfo.lakeCenter : IntVec3.Invalid);
                if (!intVec.IsValid || !(Math.Abs(x + (double)intVec.x) <= 0.5) || !(Math.Abs(z + (double)intVec.z) <= 0.5))
                {
                    z += num - ((double)offset + (double)bandHeight / 2.0);
                    redirected++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] V2: field-centre translate patch threw: " + ex, 762195907);
        }
    }
}
