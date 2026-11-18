using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
internal static class ABOrbitalDebrisBands
{
    private enum Mode
    {
        None,
        SpreadAcrossBands,
        ViewedBand
    }

    [HarmonyPatch(typeof(MapDrawLayer_OrbitalDebris), "Regenerate")]
    public static class Patch_OrbitalDebris_ABRegenerateScope
    {
        private static void Prefix(MapDrawLayer_OrbitalDebris __instance)
        {
            try
            {
                Enter(__instance, Mode.SpreadAcrossBands);
            }
            catch
            {
                Leave();
            }
        }

        private static void Finalizer()
        {
            Leave();
        }
    }

    [HarmonyPatch(typeof(MapDrawLayer_OrbitalDebris), "DrawLayer")]
    public static class Patch_OrbitalDebris_ABDrawScope
    {
        private static void Prefix(MapDrawLayer_OrbitalDebris __instance)
        {
            try
            {
                Enter(__instance, Mode.ViewedBand);
            }
            catch
            {
                Leave();
            }
        }

        private static void Finalizer()
        {
            Leave();
        }
    }

    private static Mode mode;
    private static Map scopedMap;
    private static readonly AccessTools.FieldRef<MapDrawLayer, Map> LayerMap = AccessTools.FieldRefAccess<MapDrawLayer, Map>("map");
    internal static bool TryOverride(Map map, out IntVec3 centre)
    {
        centre = default;
        if (mode == Mode.None || map != scopedMap)
        {
            return false;
        }

        ABBandMap aBBandMap = ABBands.CompOf(map);
        if (aBBandMap == null || !aBBandMap.Banded)
        {
            return false;
        }

        int band = ((mode == Mode.SpreadAcrossBands) ? Rand.Range(0, aBBandMap.bandCount) : ABBandView.CurrentBand(map));
        centre = aBBandMap.RectOfBand(band).CenterCell;
        return true;
    }

    private static void Enter(MapDrawLayer layer, Mode m)
    {
        Map map = ((layer != null) ? LayerMap(layer) : null);
        if (map != null && ABBands.Banded(map))
        {
            scopedMap = map;
            mode = m;
        }
    }

    private static void Leave()
    {
        mode = Mode.None;
        scopedMap = null;
    }
}
