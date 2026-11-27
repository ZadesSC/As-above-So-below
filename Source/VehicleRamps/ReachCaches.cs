using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Prevents native cache sharing between main-thread ramp-aware and worker-thread ramp-blind searches.
/// Room-pair answers cannot capture a live ramp lane or the exact start cell of a flood fill.
/// </summary>
public static class ReachCaches
{
    private static readonly MethodInfo s_read = AccessTools.Method(typeof(VehicleReachabilityCache),
        nameof(VehicleReachabilityCache.CachedResultFor));

    private static readonly MethodInfo s_write = AccessTools.Method(typeof(VehicleReachabilityCache),
        nameof(VehicleReachabilityCache.AddCachedResult));

    internal static void RequireWrites(List<CodeInstruction> codes, int expected)
    {
        RequireCalls(codes, s_write, expected);
    }

    internal static MethodInfo ResolveReadTarget(MethodInfo cacheLookup)
    {
        var codes = PatchProcessor.GetOriginalInstructions(cacheLookup);
        var candidates = codes
            .Select(code => code.operand)
            .OfType<MethodInfo>()
            .Where(method => !method.IsStatic && method.DeclaringType == typeof(ChunkSearch)
                && method.ReturnType == typeof(BoolUnknown))
            .Distinct()
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException("ChunkSearch.GetCachedResult no longer has one room-cache reader");
        }

        RequireCalls(PatchProcessor.GetOriginalInstructions(candidates[0]).ToList(), s_read, 1);
        return candidates[0];
    }

    public static IEnumerable<CodeInstruction> ReadTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        RequireCalls(codes, s_read, 1);
        return RewriteCalls(codes, s_read, nameof(Read));
    }

    public static IEnumerable<CodeInstruction> FloodWritesTranspiler(IEnumerable<CodeInstruction> instructions) =>
        RewriteWrites(instructions.ToList(), 2);

    internal static List<CodeInstruction> RewriteWrites(List<CodeInstruction> codes, int expected)
    {
        RequireWrites(codes, expected);
        return RewriteCalls(codes, s_write, nameof(Write));
    }

    public static BoolUnknown Read(VehicleReachabilityCache cache, VehicleRoom from, VehicleRoom to,
        TraverseParms traverseParms, ChunkSearch search) =>
        RampRegistry.HasPendingOrUsableRamps(search.map)
            ? BoolUnknown.Unknown
            : cache.CachedResultFor(from, to, traverseParms);

    public static void Write(VehicleReachabilityCache cache, VehicleRoom from, VehicleRoom to,
        TraverseParms traverseParms, bool reachable, ChunkSearch search)
    {
        if (!RampRegistry.HasPendingOrUsableRamps(search.map))
        {
            cache.AddCachedResult(from, to, traverseParms, reachable);
        }
    }

    /// <summary>Main thread only. A topology change also invalidates answers cached before the first ramp.</summary>
    public static void Clear(Map map)
    {
        var pathing = map?.GetComponent<VehiclePathingSystem>();
        var container = pathing?.PathData;
        if (container == null)
        {
            return;
        }

        var all = container.AllPathData;
        for (var i = 0; i < all.Length; i++)
        {
            // Defs that share a grid owner share its RegionData, so a cache may be cleared more than once.
            all[i]?.RegionData?.reachability?.ClearCache();
        }
    }

    private static void RequireCalls(List<CodeInstruction> codes, MethodInfo method, int expected)
    {
        var matching = codes.Where(code => code.Calls(method)).ToList();
        var count = matching.Count;
        if (count != expected)
        {
            throw new InvalidOperationException("expected " + expected + " ChunkSearch calls to " + method.Name
                + ", found " + count);
        }

        if (matching.Any(code => code.blocks.Count != 0))
        {
            throw new InvalidOperationException("ChunkSearch calls " + method.Name + " at an exception boundary");
        }
    }

    private static List<CodeInstruction> RewriteCalls(List<CodeInstruction> codes, MethodInfo native, string name)
    {
        var replacement = AccessTools.Method(typeof(ReachCaches), name);
        for (var i = codes.Count - 1; i >= 0; i--)
        {
            if (!codes[i].Calls(native))
            {
                continue;
            }

            // The cache, room pair and traverse parameters are already on the stack. Pass the owning search too.
            var loadSearch = new CodeInstruction(OpCodes.Ldarg_0);
            loadSearch.labels.AddRange(codes[i].labels);
            codes[i] = new CodeInstruction(OpCodes.Call, replacement);
            codes.Insert(i, loadSearch);
        }

        return codes;
    }
}
