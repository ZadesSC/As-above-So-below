using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;
internal static class SmarterDeconstructionCompat
{
    private static bool resolved;
    private static MethodInfo checkForRoofs;
    private static Assembly theirs;
    private static bool broken;
    private static Map scopeMap;
    private static int depth;
    private static int scopeFills;
    private static int scopeRejected;
    private static int scopeBand;
    private static readonly FieldInfo FillerMap = AccessTools.Field(typeof(FloodFiller), "map");
    internal static int totalFillsClamped;
    internal static int totalToilsWrapped;
    internal static bool Present
    {
        get
        {
            Resolve();
            return checkForRoofs != null;
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
            Type type = AccessTools.TypeByName("SmartDeconstruct.SmartDeconstructMod");
            if (type == null)
            {
                return;
            }

            foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
            {
                if (!(declaredMethod.Name != "CheckForRoofsBeforeJob"))
                {
                    ParameterInfo[] parameters = declaredMethod.GetParameters();
                    if (parameters.Length == 2 && typeof(IEnumerable<Toil>).IsAssignableFrom(parameters[0].ParameterType) && parameters[1].ParameterType == typeof(JobDriver) && typeof(IEnumerable<Toil>).IsAssignableFrom(declaredMethod.ReturnType))
                    {
                        checkForRoofs = declaredMethod;
                        theirs = type.Assembly;
                        break;
                    }
                }
            }

            if (checkForRoofs == null)
            {
                Log.WarningOnce("[As above, So below] Smarter Deconstruction and Mining is loaded but its roof check was not found; mining near a level seam may queue roof removal on the wrong level.", 2822384);
            }
            else
            {
                ABLog.Dev("Smarter Deconstruction bridge resolved (roof scan band clamp).");
            }
        }
        catch (Exception ex)
        {
            checkForRoofs = null;
            theirs = null;
            Log.WarningOnce("[As above, So below] Smarter Deconstruction bridge failed to resolve: " + ex.Message, 2822385);
        }
    }

    internal static MethodBase CheckForRoofsMethod()
    {
        Resolve();
        return checkForRoofs;
    }

    internal static bool IsTheirs(Delegate d)
    {
        Type type = d?.Method?.DeclaringType;
        return type != null && theirs != null && type.Assembly == theirs;
    }

    internal static void RunScoped(Map map, Action body)
    {
        if (body == null)
        {
            return;
        }

        bool flag = false;
        bool flag2 = false;
        try
        {
            if (!broken && map != null && ABBands.Banded(map))
            {
                if (depth++ == 0)
                {
                    scopeMap = map;
                    flag2 = true;
                    scopeFills = 0;
                    scopeRejected = 0;
                    scopeBand = -1;
                }

                flag = true;
            }
        }
        catch
        {
            flag = false;
        }

        try
        {
            body();
        }
        finally
        {
            if (flag && --depth <= 0)
            {
                depth = 0;
                scopeMap = null;
                if (flag2)
                {
                    Report();
                }
            }
        }
    }

    private static void Report()
    {
        if (scopeFills == 0)
        {
            ABLog.Dev("Smarter Deconstruction: roof scan ran but did no flood fill (the target does not hold roof); nothing to clamp.");
            return;
        }

        ABLog.Dev("Smarter Deconstruction: roof scan clamped to band " + scopeBand + " over " + scopeFills + " fill(s); " + scopeRejected + " cell(s) outside that band refused" + ((scopeRejected == 0) ? " (the scan never reached a seam, so the clamp changed nothing)." : " - without this they would have been read as this level's roof."));
    }

    internal static void ClampToBand(FloodFiller filler, IntVec3 root, ref Predicate<IntVec3> passCheck)
    {
        Map map = scopeMap;
        if (map == null || passCheck == null)
        {
            return;
        }

        try
        {
            if (FillerMap != null && FillerMap.GetValue(filler) != map)
            {
                return;
            }

            int band = ABBands.BandOf(map, root);
            Predicate<IntVec3> inner = passCheck;
            passCheck = (IntVec3 c) =>
            {
                if (ABBands.BandOf(map, c) != band || ABBands.InGutter(map, c))
                {
                    scopeRejected++;
                    return false;
                }

                return inner(c);
            };
            scopeBand = band;
            scopeFills++;
            totalFillsClamped++;
        }
        catch (Exception ex)
        {
            broken = true;
            scopeMap = null;
            depth = 0;
            Log.WarningOnce("[As above, So below] Smarter Deconstruction roof-scan bridge threw and has stood down for this session; mining near a level seam may queue roof removal on the wrong level again: " + ex.Message, 2822386);
        }
    }
}
