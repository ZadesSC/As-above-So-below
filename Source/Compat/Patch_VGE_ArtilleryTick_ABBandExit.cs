using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_VGE_ArtilleryTick_ABBandExit
{
    private static bool Prepare()
    {
        return VanillaGravshipCompat.ResolveArtillery();
    }

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(VanillaGravshipCompat.projectileArtillery, "TickInterval", new Type[1] { typeof(int) });
    }

    private static bool Prefix(Projectile __instance)
    {
        try
        {
            Map map = __instance?.Map;
            if (map == null || !__instance.Spawned)
            {
                return true;
            }

            if (!(VanillaGravshipCompat.projectileTargetTile.GetValue(__instance)is PlanetTile { Valid: not false } planetTile) || planetTile == map.Tile)
            {
                return true;
            }

            IntVec3 c = __instance.ExactPosition.ToIntVec3();
            if (!VanillaGravshipCompat.TryBandOf(map, __instance.Position, out var _, out var band))
            {
                return true;
            }

            if (band.Contains(c) && !band.IsOnEdge(c))
            {
                return true;
            }

            VanillaGravshipCompat.spawnWorldProjectile.Invoke(__instance, null);
            __instance.Destroy();
            return false;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] VGE artillery band-exit prefix threw: " + ex.Message, 2822355);
            return true;
        }
    }
}
