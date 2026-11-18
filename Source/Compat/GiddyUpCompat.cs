using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
internal static class GiddyUpCompat
{
    private static readonly MethodInfo StraightLine = AccessTools.Method(typeof(IntVec3Utility), "DistanceTo", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
    private static readonly MethodInfo WalkLine = AccessTools.Method(typeof(GiddyUpCompat), "Walk");
    private static bool resolved;
    private static Type mountUtility;
    private static MethodInfo tryAutoMount;
    private static MethodInfo getBestAnimal;
    private static bool broken;
    private static Map scopeMap;
    private static Pawn scopePawn;
    private static int depth;
    private static readonly Dictionary<long, float> trips = new Dictionary<long, float>(64);
    private static int scopeRetargets;
    internal static int totalRetargets;
    internal static bool Present
    {
        get
        {
            Resolve();
            return mountUtility != null;
        }
    }

    internal static bool CanRewrite => StraightLine != null && WalkLine != null;

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        try
        {
            mountUtility = AccessTools.TypeByName("GiddyUp.MountUtility");
            if (mountUtility == null)
            {
                return;
            }

            foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(mountUtility))
            {
                ParameterInfo[] parameters = declaredMethod.GetParameters();
                if (declaredMethod.Name == "TryAutoMount" && parameters.Length == 3 && parameters[0].ParameterType == typeof(Pawn))
                {
                    tryAutoMount = declaredMethod;
                }
                else if (declaredMethod.Name == "GetBestAnimal" && parameters.Length == 7 && parameters[0].ParameterType == typeof(Pawn) && parameters[2].ParameterType == typeof(IntVec3) && parameters[3].ParameterType == typeof(IntVec3) && parameters[4].ParameterType == typeof(float) && parameters[5].ParameterType == typeof(float))
                {
                    getBestAnimal = declaredMethod;
                }
            }

            if (tryAutoMount == null)
            {
                Log.WarningOnce("[As above, So below] Giddy-Up is loaded but MountUtility.TryAutoMount was not found; colonists may walk across the map to fetch a mount for a job that is only one level away.", 2822336);
                mountUtility = null;
            }
            else
            {
                ABLog.Dev("Giddy-Up bridge resolved (auto-mount distance" + ((getBestAnimal != null) ? " + animal ranking" : ", ranking seam MISSING") + ").");
            }
        }
        catch (Exception ex)
        {
            mountUtility = null;
            Log.WarningOnce("[As above, So below] Giddy-Up bridge failed to resolve: " + ex.Message, 2822337);
        }
    }

    internal static MethodBase TryAutoMountMethod()
    {
        Resolve();
        return tryAutoMount;
    }

    internal static MethodBase GetBestAnimalMethod()
    {
        Resolve();
        return getBestAnimal;
    }

    internal static void Push(Pawn pawn)
    {
        if (depth++ > 0)
        {
            return;
        }

        trips.Clear();
        scopeRetargets = 0;
        scopeMap = null;
        scopePawn = null;
        if (!broken && pawn != null)
        {
            Map map = pawn.Map;
            if (map != null && ABBands.Banded(map) && ABWormhole.PairCount(map) != 0)
            {
                scopeMap = map;
                scopePawn = pawn;
            }
        }
    }

    internal static void Pop()
    {
        if (depth != 0 && --depth <= 0)
        {
            if (scopeRetargets > 0)
            {
                ABLog.Dev("Giddy-Up: " + scopeRetargets + " cross-level measurement" + ((scopeRetargets == 1) ? "" : "s") + " corrected for " + ((scopePawn != null) ? scopePawn.LabelShortCap : "?") + ".");
            }

            scopeMap = null;
            scopePawn = null;
            trips.Clear();
        }
    }

    public static float Walk(IntVec3 a, IntVec3 b)
    {
        float num = a.DistanceTo(b);
        Map map = scopeMap;
        if (map == null)
        {
            return num;
        }

        long key = Key(a, b);
        if (trips.TryGetValue(key, out var value))
        {
            return value;
        }

        float num2 = num;
        try
        {
            if (ABBandDistance.TryCrossLevel(map, scopePawn, a, b, out var length))
            {
                num2 = length;
            }
        }
        catch (Exception ex)
        {
            broken = true;
            scopeMap = null;
            scopePawn = null;
            Log.WarningOnce("[As above, So below] Giddy-Up distance bridge threw and has stood down for this session; auto-mount will measure in straight lines again: " + ex.Message, 2822338);
            return num;
        }

        trips[key] = num2;
        if (num2 != num)
        {
            scopeRetargets++;
            totalRetargets++;
        }

        return num2;
    }

    private static long Key(IntVec3 a, IntVec3 b)
    {
        return (long)(((ulong)(ushort)a.x << 48) | ((ulong)(ushort)a.z << 32) | ((ulong)(ushort)b.x << 16) | (ushort)b.z);
    }

    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, string where, int expected)
    {
        List<CodeInstruction> list = new List<CodeInstruction>(input);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            CodeInstruction codeInstruction = list[i];
            if ((codeInstruction.opcode == OpCodes.Call || codeInstruction.opcode == OpCodes.Callvirt) && codeInstruction.operand as MethodInfo == StraightLine)
            {
                codeInstruction.operand = WalkLine;
                num++;
            }
        }

        if (num == 0)
        {
            Log.WarningOnce("[As above, So below] Giddy-Up bridge found no distance calls to correct in " + where + "; colonists may fetch a mount for a job one level away.", 2822339);
        }
        else
        {
            ABLog.Dev("Giddy-Up bridge rewrote " + num + " of " + expected + " expected distance calls in " + where + ".");
        }

        return list;
    }
}
