using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Transpiler on Vehicle Framework's region search, <c>ChunkSearch.CanReach(in Data)</c>. Each region the search
/// dequeues is passed to <see cref="LinkRamps"/>, which treats every usable ramp crossing whose entry cell lies in
/// that region as a region link to the region of the crossing's exit cell. The exit region goes through VF's own
/// <c>RegionReachable</c>, so the visited marker, passability, <c>Allows(traverseParms)</c> and the destination check
/// are VF's. One search therefore covers every floor joined by ramps, and each region is visited at most once.
/// The call is injected right after the dequeue (outside the method's try blocks, so it may return true directly)
/// rather than after the link loop; the visiting order changes but not the answer.
/// <para>
/// No region index outlives a query. VF rebuilds its regions whenever walkability changes (also on its dedicated
/// thread), so a retained region-to-edge index would need its own invalidation. Instead, at the first dequeue of each
/// query (after VF's <c>Prepare</c> has brought the region grid up to date), the entry cells of the usable edges are
/// looked up in the live region grid once and grouped by region in pooled buffers; every dequeue then costs one
/// dictionary lookup. The buffers grow only when a table has more edges than before, so the steady state allocates
/// nothing. Both mouths must have clear lanes. That check reads live map terrain and things, so links are offered
/// only on the main thread.
/// </para>
/// </summary>
public static class RegionPortalPatch
{
    private static readonly MethodInfo DequeueMethod =
        AccessTools.Method(typeof(Queue<VehicleRegion>), nameof(Queue<VehicleRegion>.Dequeue));

    // Per-query state for the main thread. A query is one CanReach call, identified by its ChunkSearch and the
    // search's reachedIndex, which VF increments in Prepare before every search.
    private static ChunkSearch s_search;
    private static uint s_index;
    private static bool s_active;
    private static RampCrossings s_crossings;
    private static VehicleRegion[] s_grid;
    private static Map s_map;
    private static VehicleDef s_def;

    // Entry region -> first edge index; s_slotNext links the other edges in the same region.
    private static readonly Dictionary<VehicleRegion, int> s_firstSlot =
        new Dictionary<VehicleRegion, int>(ReferenceEqualityComparer.Instance);
    private static int[] s_slotNext = new int[0];

    public static MethodInfo Dequeue => DequeueMethod;

    /// <summary>
    /// Exactly one <c>Queue&lt;VehicleRegion&gt;.Dequeue</c> call, immediately stored to a local outside every
    /// exception block, with no branch target right after the store; null on success. <paramref name="index"/> is
    /// the store instruction. The method must report its exception blocks (it has try/finally blocks today), so a
    /// missing block report cannot pass as "outside every block".
    /// </summary>
    public static string FindAnchor(List<CodeInstruction> codes, out int index)
    {
        index = -1;
        var found = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(DequeueMethod))
            {
                found++;
                index = i + 1;
            }
        }

        if (found != 1)
        {
            return "expected 1 Queue<VehicleRegion>.Dequeue call in ChunkSearch.CanReach, found " + found;
        }

        if (index + 1 >= codes.Count || !codes[index].IsStloc() || codes[index + 1].labels.Count != 0)
        {
            return "ChunkSearch.CanReach no longer stores the dequeued region in a local before using it";
        }

        var depth = 0;
        var anyBlock = false;
        for (var i = 0; i < codes.Count; i++)
        {
            foreach (var block in codes[i].blocks)
            {
                anyBlock = true;
                if (i > index)
                {
                    continue;
                }

                if (block.blockType == ExceptionBlockType.BeginExceptionBlock)
                {
                    depth++;
                }
                else if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                {
                    depth--;
                }
            }
        }

        if (!anyBlock)
        {
            return "ChunkSearch.CanReach reports no exception blocks; the anchor's block depth cannot be checked";
        }

        return depth == 0 ? null : "the dequeued region is stored inside an exception block in ChunkSearch.CanReach";
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);
        var failure = FindAnchor(codes, out var store);
        if (failure != null)
        {
            throw new InvalidOperationException(failure);
        }

        // After "region = openQueue.Dequeue()": if (LinkRamps(this, region, in data)) return true;
        // FindAnchor proved the anchor is outside every exception block, so a plain ret is valid here.
        var next = generator.DefineLabel();
        codes[store + 1].labels.Add(next);
        codes.InsertRange(store + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            CodeInstruction.LoadLocal(codes[store].LocalIndex()),
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RegionPortalPatch), nameof(LinkRamps))),
            new CodeInstruction(OpCodes.Brfalse, next),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ret),
        });
        return ReachCaches.RewriteWrites(codes, 1);
    }

    /// <summary>
    /// Called for each region the search dequeues. Returns true when a crossing leads straight into a destination
    /// region; otherwise queues reachable exit regions through VF's <c>RegionReachable</c>.
    /// </summary>
    public static bool LinkRamps(ChunkSearch search, VehicleRegion region, in ChunkSearch.Data data)
    {
        RampDiagnostics.Add(ref RampDiagnostics.ReachRegionsVisited);
        if (!UnityData.IsInMainThread)
        {
            return false;
        }

        if (!ReferenceEquals(search, s_search) || search.reachedIndex != s_index)
        {
            Begin(search, in data);
        }

        if (!s_active || !s_firstSlot.TryGetValue(region, out var slot))
        {
            return false;
        }

        var edges = s_crossings.Edges;
        for (; slot >= 0; slot = s_slotNext[slot])
        {
            if (TryLink(search, in edges[slot], in data))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the per-query references so a removed map is not retained. Main thread only.</summary>
    public static void Forget()
    {
        s_search = null;
        s_active = false;
        s_crossings = null;
        s_grid = null;
        s_map = null;
        s_def = null;
        s_firstSlot.Clear();
    }

    private static bool TryLink(ChunkSearch search, in RampEdge edge, in ChunkSearch.Data data)
    {
        var exitRegion = s_grid[edge.ExitIndex];
        if (exitRegion == null || !exitRegion.valid)
        {
            RampDiagnostics.Add(ref RampDiagnostics.ReachExitNoRegion);
            return false;
        }

        if (!RampClearance.IsClear(s_map, s_def, edge.EntryLane))
        {
            RampDiagnostics.Add(ref RampDiagnostics.ReachEntryRejected);
            return false;
        }

        if (!RampClearance.IsClear(s_map, s_def, edge.ExitLane))
        {
            RampDiagnostics.Add(ref RampDiagnostics.ReachExitRejected);
            return false;
        }

        RampDiagnostics.Add(ref RampDiagnostics.ReachPortalLinks);
        return search.RegionReachable(exitRegion, in data.traverseParms);
    }

    private static void Begin(ChunkSearch search, in ChunkSearch.Data data)
    {
        s_search = search;
        s_index = search.reachedIndex;
        s_active = false;
        s_firstSlot.Clear();
        if (RampDiagnostics.ReachMode != ReachTestMode.Normal)
        {
            return;
        }

        var def = (data.traverseParms.pawn as VehiclePawn)?.VehicleDef
            ?? PathPreviewReachPatch.SelectedDef(search) ?? search.createdFor;
        var crossings = RampRegistry.FreshTable(search.map).ForSize(def.size);
        if (crossings == null || crossings.Edges.IsEmpty)
        {
            return;
        }

        var grid = search.regionGridManager[VehicleRegionGridManager.GetGridType(data.traverseParms)].DirectGrid;
        if (grid == null)
        {
            return;
        }

        var edges = crossings.Edges;
        if (s_slotNext.Length < edges.Length)
        {
            s_slotNext = new int[edges.Length];
        }

        for (var i = 0; i < edges.Length; i++)
        {
            var entryRegion = grid[edges[i].EntryIndex];
            if (entryRegion == null || !entryRegion.valid)
            {
                continue;
            }

            s_slotNext[i] = s_firstSlot.TryGetValue(entryRegion, out var first) ? first : -1;
            s_firstSlot[entryRegion] = i;
        }

        s_def = def;
        s_map = search.map;
        s_grid = grid;
        s_crossings = crossings;
        s_active = s_firstSlot.Count > 0;
    }
}
