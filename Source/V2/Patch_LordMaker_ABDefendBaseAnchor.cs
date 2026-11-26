using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace AsAboveSoBelow;
[HarmonyPatch(typeof(LordMaker), "MakeNewLord")]
public static class Patch_LordMaker_ABDefendBaseAnchor
{
    private static AccessTools.FieldRef<LordJob_DefendBase, IntVec3> baseCenter;
    private static bool baseCenterResolved;
    internal static int anchorsMoved;
    private static AccessTools.FieldRef<LordJob_DefendBase, IntVec3> BaseCenter
    {
        get
        {
            if (!baseCenterResolved)
            {
                baseCenterResolved = true;
                try
                {
                    baseCenter = AccessTools.FieldRefAccess<LordJob_DefendBase, IntVec3>("baseCenter");
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[As above, So below] V2: LordJob_DefendBase.baseCenter not found (" + ex.Message + "); defend-base anchors are not band-corrected.", 2822321);
                    baseCenter = null;
                }
            }

            return baseCenter;
        }
    }

    private static void Prefix(Faction faction, LordJob lordJob, Map map, IEnumerable<Pawn> startingPawns)
    {
        try
        {
            if (!(lordJob is LordJob_DefendBase instance) || map == null || startingPawns == null)
            {
                return;
            }

            ABBandMap aBBandMap = ABBands.CompOf(map);
            if (aBBandMap == null || !aBBandMap.Banded)
            {
                return;
            }

            AccessTools.FieldRef<LordJob_DefendBase, IntVec3> fieldRef = BaseCenter;
            if (fieldRef == null)
            {
                return;
            }

            int num = -1;
            foreach (Pawn startingPawn in startingPawns)
            {
                if (startingPawn != null && startingPawn.Spawned && startingPawn.Map == map)
                {
                    num = aBBandMap.BandOf(startingPawn.Position);
                    break;
                }
            }

            if (num < 0 || !aBBandMap.BandExists(num))
            {
                return;
            }

            IntVec3 intVec = fieldRef(instance);
            if (intVec.IsValid && intVec.InBounds(map) && (aBBandMap.BandOf(intVec) != num || aBBandMap.InGutter(intVec)))
            {
                CellRect cellRect = aBBandMap.RectOfBand(num);
                IntVec3 intVec2 = aBBandMap.Translate(intVec, num);
                intVec2.z = Math.Min(Math.Max(intVec2.z, cellRect.minZ), cellRect.maxZ);
                if (intVec2.InBounds(map))
                {
                    fieldRef(instance) = intVec2;
                    anchorsMoved++;
                    string[] array = new string[9]
                    {
                        "V2: defend-base anchor moved ",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null
                    };
                    IntVec3 intVec3 = intVec;
                    array[1] = intVec3.ToString();
                    array[2] = " -> ";
                    intVec3 = intVec2;
                    array[3] = intVec3.ToString();
                    array[4] = " onto band ";
                    array[5] = num.ToString();
                    array[6] = " (lord of ";
                    array[7] = faction?.Name ?? "no faction";
                    array[8] = ").";
                    ABLog.Dev(string.Concat(array));
                }
            }
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] V2: defend-base anchor prefix threw: " + ex, 762195991);
        }
    }
}
