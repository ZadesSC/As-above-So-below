using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
public static class VanillaGravshipCompat
{
    private static Type landingType;
    private static bool resolved;
    internal static int landingsRedirected;
    internal static string lastRedirect = "none yet";
    internal static Type artilleryUtility;
    internal static Type compWorldArtillery;
    internal static Type projectileArtillery;
    internal static MethodInfo getAngle;
    internal static FieldInfo projectileTargetTile;
    internal static MethodInfo spawnWorldProjectile;
    private static bool artilleryResolved;
    internal static int artilleryClamped;
    private static Type LandingType
    {
        get
        {
            if (!resolved)
            {
                resolved = true;
                landingType = AccessTools.TypeByName("VanillaGravshipExpanded.LandingStructureBase");
            }

            return landingType;
        }
    }

    public static string CounterReport()
    {
        if (LandingType == null)
        {
            return "    VanillaGravship: (not installed)";
        }

        return "    VanillaGravship: landingsRedirected=" + landingsRedirected + " artilleryEdgesClamped=" + artilleryClamped + " | last: " + lastRedirect;
    }

    internal static bool IsLandingStructure(Thing t)
    {
        Type type = LandingType;
        return type != null && t != null && type.IsInstanceOfType(t);
    }

    internal static bool ResolveArtillery()
    {
        if (artilleryResolved)
        {
            return getAngle != null;
        }

        artilleryResolved = true;
        artilleryUtility = AccessTools.TypeByName("VanillaGravshipExpanded.ArtilleryUtility");
        compWorldArtillery = AccessTools.TypeByName("VanillaGravshipExpanded.CompWorldArtillery");
        projectileArtillery = AccessTools.TypeByName("VanillaGravshipExpanded.Projectile_Artillery");
        if (artilleryUtility == null || compWorldArtillery == null || projectileArtillery == null)
        {
            return false;
        }

        getAngle = AccessTools.Method(artilleryUtility, "GetAngle", new Type[2] { typeof(PlanetTile), typeof(PlanetTile) });
        projectileTargetTile = AccessTools.Field(projectileArtillery, "targetTile");
        spawnWorldProjectile = AccessTools.Method(projectileArtillery, "SpawnWorldProjectile");
        if (getAngle == null || projectileTargetTile == null || spawnWorldProjectile == null)
        {
            Log.Warning("[As above, So below] Vanilla Gravship Expanded is present but its artillery internals did not resolve; cross-tile shells keep vanilla's stack-wide edges.");
            getAngle = null;
            return false;
        }

        return true;
    }

    internal static bool TryBandOf(Map map, IntVec3 c, out ABBandMap bands, out CellRect band)
    {
        band = default;
        bands = ABBands.CompOf(map);
        if (bands == null || !bands.Banded || !c.InBounds(map))
        {
            return false;
        }

        band = bands.RectOfBand(bands.BandOf(c));
        return true;
    }

    internal static bool TryRedirectLanding(Thing newThing, ref IntVec3 loc, Map map, ABBandMap bands)
    {
        if (map == null || bands == null || !bands.Banded || !loc.InBounds(map))
        {
            return false;
        }

        int slot = bands.Slot;
        int bandHeight = bands.bandHeight;
        int num = Mathf.Max(ABBandSafety.SeamMarginFor(bandHeight), bandHeight / 5);
        if (2 * num >= bandHeight)
        {
            num = Mathf.Max(0, (bandHeight - 1) / 2);
        }

        int num2 = Mathf.Clamp(loc.z % slot, num, bandHeight - 1 - num);
        IntVec3 intVec = new IntVec3(loc.x, loc.y, bands.surfaceBand * slot + num2);
        if (intVec == loc || !intVec.InBounds(map))
        {
            return false;
        }

        landingsRedirected++;
        string[] array = new string[5]
        {
            newThing.def.defName,
            " ",
            null,
            null,
            null
        };
        IntVec3 intVec2 = loc;
        array[2] = intVec2.ToString();
        array[3] = " -> ";
        intVec2 = intVec;
        array[4] = intVec2.ToString();
        lastRedirect = string.Concat(array);
        ABLog.Dev("VGE landing structure redirected to the surface band: " + lastRedirect + " (band " + bands.BandOf(loc) + " -> " + bands.surfaceBand + ").");
        loc = intVec;
        return true;
    }
}
