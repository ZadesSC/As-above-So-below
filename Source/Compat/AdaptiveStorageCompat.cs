using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
internal static class AdaptiveStorageCompat
{
    public const string PackageId = "adaptive.storage.framework";
    private const string ThingClassName = "AdaptiveStorage.ThingClass";
    private static bool resolved;
    private static Type thingClass;
    private static bool broken;
    internal static int hiddenPrints;
    internal static bool Present
    {
        get
        {
            Resolve();
            return thingClass != null && !broken;
        }
    }

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        try
        {
            if (ModsConfig.IsActive("adaptive.storage.framework"))
            {
                thingClass = AccessTools.TypeByName("AdaptiveStorage.ThingClass");
                ABLog.Dev("Adaptive Storage compat: ThingClass=" + ((thingClass != null) ? "FOUND" : "absent") + " (stored items are " + ((thingClass != null) ? "left to their container" : "printed as vanilla") + " in the see-below layer).");
            }
        }
        catch (Exception ex)
        {
            thingClass = null;
            Log.WarningOnce("[As above, So below] Adaptive Storage compat resolve threw: " + ex.Message, 2822387);
        }
    }

    internal static bool HidesFromSectionLayer(Thing t, Map map)
    {
        if (!Present || t == null || map == null)
        {
            return false;
        }

        try
        {
            if (t.def == null || t.def.category != ThingCategory.Item || t is ISlotGroupParent)
            {
                return false;
            }

            ISlotGroupParent slotGroupParent = map.haulDestinationManager?.SlotGroupParentAt(t.Position);
            if (slotGroupParent == null || !thingClass.IsInstanceOfType(slotGroupParent))
            {
                return false;
            }

            hiddenPrints++;
            return true;
        }
        catch (Exception ex)
        {
            broken = true;
            Log.WarningOnce("[As above, So below] Adaptive Storage compat threw and has stood down for this session; stored items may print twice when seen from a level above: " + ex.Message, 2822388);
            return false;
        }
    }
}
