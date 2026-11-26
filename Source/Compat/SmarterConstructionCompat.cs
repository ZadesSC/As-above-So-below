using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
internal static class SmarterConstructionCompat
{
    private const int LinkProbeRadius = 2;
    private static readonly MethodInfo Subtraction = AccessTools.Method(typeof(IntVec3), "op_Subtraction", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
    private static readonly MethodInfo BandDeltaMethod = AccessTools.Method(typeof(SmarterConstructionCompat), "BandDelta");
    private static bool resolved;
    private static MethodInfo wouldEncloseThings;
    private static MethodInfo findSafeSpots;
    private static MethodInfo closedRegion;
    private static MethodInfo closestCustom;
    private static MethodInfo isPawnStuck;
    private static readonly List<MethodBase> distanceLambdas = new List<MethodBase>();
    private static bool broken;
    private static Map scopeMap;
    private static Pawn scopePawn;
    private static int depth;
    private static readonly Dictionary<long, int> trips = new Dictionary<long, int>(64);
    internal static int totalPocketsFreed;
    internal static int totalRetargets;
    internal static int totalStuckSuppressed;
    internal static bool CanRewrite => Subtraction != null && BandDeltaMethod != null;

    private static void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        try
        {
            Type type = AccessTools.TypeByName("SmarterConstruction.Core.ClosedRegionDetector");
            Type type2 = AccessTools.TypeByName("SmarterConstruction.Patches.CustomGenClosest");
            Type type3 = AccessTools.TypeByName("SmarterConstruction.Core.PawnPositionCache");
            if (type == null && type2 == null && type3 == null)
            {
                return;
            }

            if (type != null)
            {
                foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
                {
                    ParameterInfo[] parameters = declaredMethod.GetParameters();
                    if (declaredMethod.Name == "WouldEncloseThings" && parameters.Length == 3 && parameters[0].ParameterType == typeof(Thing) && parameters[1].ParameterType == typeof(Pawn))
                    {
                        wouldEncloseThings = declaredMethod;
                    }
                    else if (declaredMethod.Name == "FindSafeConstructionSpots" && parameters.Length == 1 && parameters[0].ParameterType == typeof(Thing))
                    {
                        findSafeSpots = declaredMethod;
                    }
                    else if (declaredMethod.Name == "ClosedRegionCreatedByAddingImpassable" && parameters.Length == 2 && parameters[1].ParameterType == typeof(HashSet<IntVec3>) && declaredMethod.ReturnType == typeof(HashSet<IntVec3>))
                    {
                        closedRegion = declaredMethod;
                    }
                }
            }

            if (type2 != null)
            {
                foreach (MethodInfo declaredMethod2 in AccessTools.GetDeclaredMethods(type2))
                {
                    ParameterInfo[] parameters2 = declaredMethod2.GetParameters();
                    if (declaredMethod2.Name == "ClosestThing_Global_Reachable_Custom" && parameters2.Length == 9 && parameters2[0].ParameterType == typeof(IntVec3) && parameters2[1].ParameterType == typeof(Map) && parameters2[4].ParameterType == typeof(TraverseParms))
                    {
                        closestCustom = declaredMethod2;
                        break;
                    }
                }

                CollectDistanceLambdas(type2, 0);
            }

            if (type3 != null)
            {
                foreach (MethodInfo declaredMethod3 in AccessTools.GetDeclaredMethods(type3))
                {
                    ParameterInfo[] parameters3 = declaredMethod3.GetParameters();
                    if (declaredMethod3.Name == "IsPawnStuck" && parameters3.Length == 1 && parameters3[0].ParameterType == typeof(Pawn) && declaredMethod3.ReturnType == typeof(bool))
                    {
                        isPawnStuck = declaredMethod3;
                        break;
                    }
                }
            }

            if (closedRegion == null || wouldEncloseThings == null)
            {
                Log.WarningOnce("[As above, So below] Smarter Construction is loaded but its enclosure check was not found; colonists may refuse to build walls on any level smaller than 100 open cells.", 2822368);
            }

            ABLog.Dev("Smarter Construction bridge resolved (enclosure=" + ((closedRegion != null && wouldEncloseThings != null) ? "yes" : "NO") + ", ranking=" + ((closestCustom != null && distanceLambdas.Count > 0) ? (distanceLambdas.Count + " lambda(s)") : "NO") + ", stuck=" + ((isPawnStuck != null) ? "yes" : "NO") + ").");
        }
        catch (Exception ex)
        {
            wouldEncloseThings = null;
            closedRegion = null;
            closestCustom = null;
            isPawnStuck = null;
            distanceLambdas.Clear();
            Log.WarningOnce("[As above, So below] Smarter Construction bridge failed to resolve: " + ex.Message, 2822369);
        }
    }

    private static void CollectDistanceLambdas(Type type, int recursion)
    {
        if (type == null || recursion > 3)
        {
            return;
        }

        foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
        {
            ParameterInfo[] parameters = declaredMethod.GetParameters();
            if (parameters.Length == 1 && !(parameters[0].ParameterType != typeof(Thing)))
            {
                Type returnType = declaredMethod.ReturnType;
                if (!(returnType == null) && !(returnType == typeof(void)) && (returnType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || returnType.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)))
                {
                    distanceLambdas.Add(declaredMethod);
                }
            }
        }

        Type[] nestedTypes = type.GetNestedTypes(AccessTools.all);
        foreach (Type type2 in nestedTypes)
        {
            CollectDistanceLambdas(type2, recursion + 1);
        }
    }

    internal static MethodBase WouldEncloseThingsMethod()
    {
        Resolve();
        return wouldEncloseThings;
    }

    internal static MethodBase FindSafeSpotsMethod()
    {
        Resolve();
        return findSafeSpots;
    }

    internal static MethodBase ClosedRegionMethod()
    {
        Resolve();
        return closedRegion;
    }

    internal static MethodBase ClosestCustomMethod()
    {
        Resolve();
        return closestCustom;
    }

    internal static MethodBase IsPawnStuckMethod()
    {
        Resolve();
        return isPawnStuck;
    }

    internal static IEnumerable<MethodBase> DistanceLambdaMethods()
    {
        Resolve();
        return distanceLambdas;
    }

    internal static void Push(Map map, Pawn pawn)
    {
        if (depth++ <= 0)
        {
            trips.Clear();
            scopeMap = null;
            scopePawn = null;
            if (!broken && map != null && ABBands.Banded(map) && ABWormhole.PairCount(map) != 0)
            {
                scopeMap = map;
                scopePawn = pawn;
            }
        }
    }

    internal static void PushFor(Thing target)
    {
        Push((target != null && target.Spawned) ? target.Map : null, null);
    }

    internal static void Pop()
    {
        if (depth != 0 && --depth <= 0)
        {
            scopeMap = null;
            scopePawn = null;
            trips.Clear();
        }
    }

    internal static void PruneEscapablePockets(HashSet<IntVec3> pockets)
    {
        Map map = scopeMap;
        if (map == null || pockets == null || pockets.Count == 0)
        {
            return;
        }

        try
        {
            HashSet<IntVec3> hashSet = new HashSet<IntVec3>(pockets);
            List<IntVec3> list = new List<IntVec3>();
            Queue<IntVec3> queue = new Queue<IntVec3>();
            IntVec3[] cardinalDirectionsAround = GenAdj.CardinalDirectionsAround;
            while (hashSet.Count > 0)
            {
                IntVec3 item = IntVec3.Invalid;
                using (HashSet<IntVec3>.Enumerator enumerator = hashSet.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        IntVec3 current = enumerator.Current;
                        item = current;
                    }
                }

                hashSet.Remove(item);
                list.Clear();
                queue.Clear();
                queue.Enqueue(item);
                bool flag = false;
                while (queue.Count > 0)
                {
                    IntVec3 intVec = queue.Dequeue();
                    list.Add(intVec);
                    if (!flag && ABWormhole.NearAnyAnchor(map, intVec, 2))
                    {
                        flag = true;
                    }

                    for (int i = 0; i < cardinalDirectionsAround.Length; i++)
                    {
                        IntVec3 item2 = intVec + cardinalDirectionsAround[i];
                        if (hashSet.Remove(item2))
                        {
                            queue.Enqueue(item2);
                        }
                    }
                }

                if (flag)
                {
                    for (int j = 0; j < list.Count; j++)
                    {
                        pockets.Remove(list[j]);
                    }

                    totalPocketsFreed++;
                    ABLog.Dev("Smarter Construction: released a " + list.Count + "-cell pocket with a level link in it; the wall is buildable.");
                }
            }
        }
        catch (Exception ex)
        {
            broken = true;
            scopeMap = null;
            scopePawn = null;
            Log.WarningOnce("[As above, So below] Smarter Construction enclosure bridge threw and has stood down for this session; colonists may refuse to build walls on small levels again: " + ex.Message, 2822370);
        }
    }

    public static IntVec3 BandDelta(IntVec3 a, IntVec3 b)
    {
        IntVec3 intVec = a - b;
        Map map = scopeMap;
        if (map == null)
        {
            return intVec;
        }

        long key = Key(a, b);
        if (trips.TryGetValue(key, out var value))
        {
            return (value < 0) ? intVec : new IntVec3(value, 0, 0);
        }

        int num = -1;
        try
        {
            if (ABBandDistance.TryCrossLevel(map, scopePawn, a, b, out var length))
            {
                num = Mathf.Max(0, Mathf.RoundToInt(length));
            }
        }
        catch (Exception ex)
        {
            broken = true;
            scopeMap = null;
            scopePawn = null;
            Log.WarningOnce("[As above, So below] Smarter Construction ranking bridge threw and has stood down for this session; construction targets will be ranked in straight lines again: " + ex.Message, 2822371);
            return intVec;
        }

        trips[key] = num;
        if (num < 0)
        {
            return intVec;
        }

        totalRetargets++;
        return new IntVec3(num, 0, 0);
    }

    private static long Key(IntVec3 a, IntVec3 b)
    {
        return (long)(((ulong)(ushort)a.x << 48) | ((ulong)(ushort)a.z << 32) | ((ulong)(ushort)b.x << 16) | (ushort)b.z);
    }

    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input, string where)
    {
        List<CodeInstruction> list = new List<CodeInstruction>(input);
        int num = 0;
        for (int i = 0; i < list.Count; i++)
        {
            CodeInstruction codeInstruction = list[i];
            if ((codeInstruction.opcode == OpCodes.Call || codeInstruction.opcode == OpCodes.Callvirt) && codeInstruction.operand as MethodInfo == Subtraction)
            {
                codeInstruction.operand = BandDeltaMethod;
                num++;
            }
        }

        if (num == 0)
        {
            Log.WarningOnce("[As above, So below] Smarter Construction bridge found no distance arithmetic to correct in " + where + "; colonists may finish every frame on their own level before starting one on another.", 2822372);
        }
        else
        {
            ABLog.Dev("Smarter Construction bridge rewrote " + num + " distance call(s) in " + where + ".");
        }

        return list;
    }

    internal static bool HeldByTransit(Pawn pawn)
    {
        try
        {
            return ABWormholePather.HasPending(pawn);
        }
        catch
        {
            return false;
        }
    }
}
