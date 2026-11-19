using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;

// Retain the native search loop. Add portal neighbors and adapt its heuristic
// only while the current request has usable portal edges.
internal static class ABNativePortalKernel
{
    public struct Frame
    {
        internal ABNativePortals.SearchContext Context;
        internal int Slot, MinX, MaxX, MinZ, MaxZ, Cardinal, Diagonal;
        internal float Strength;
        internal bool SinglePair;
        internal int GoalFloor, EntryFloor, EntryX, EntryZ, EntryTail;
    }
    public static Frame Begin(ref PathFinderJob job)
    {
        ABNativePortals.TakeContext(ref job, out var context);
        if (context == null)
            return default;

        var frame = new Frame { Context = context, Slot = context.Slot };
        if (job.destRect.minZ / context.Slot == job.destRect.maxZ / context.Slot)
        {
            frame.MinX = job.destRect.minX;
            frame.MaxX = job.destRect.maxX;
            frame.MinZ = job.destRect.minZ % context.Slot;
            frame.MaxZ = job.destRect.maxZ % context.Slot;
            frame.Cardinal = job.moveTicksCardinal;
            frame.Diagonal = job.moveTicksDiagonal;
            frame.Strength = job.heuristicStrength;
            frame.GoalFloor = job.destRect.minZ / context.Slot;
            PrepareSinglePair(ref job, ref frame);
        }
        return frame;
    }
    static void PrepareSinglePair(ref PathFinderJob job, ref Frame frame)
    {
        var edges = frame.Context.Edges;
        if (edges.Count != 2 || frame.Cardinal <= 0 || frame.Diagonal < frame.Cardinal || frame.Diagonal > frame.Cardinal * 2) return;
        var iterator = edges.GetEnumerator();
        iterator.MoveNext();
        var a = iterator.Current;
        iterator.MoveNext();
        var b = iterator.Current;
        if (a.Value.Length != 1 || b.Value.Length != 1 || a.Value[0].Exit != b.Key || b.Value[0].Exit != a.Key || a.Value[0].Cost < 0 || b.Value[0].Cost < 0) return;
        var ac = job.indices.IndexToCell(a.Key); var bc = job.indices.IndexToCell(b.Key);
        int af = ac.z / frame.Slot, bf = bc.z / frame.Slot;
        if (!((af == 0 && bf == 1) || (af == 1 && bf == 0)) || (frame.GoalFloor != af && frame.GoalFloor != bf)) return;
        int width = job.indices.CellToIndex(new IntVec3(0, 0, 1));
        if (width <= 0 || job.calcGrid.Length != width * frame.Slot * 2) return;
        // The bound assumes this portal is required to cross floors. Verify an
        // entire native cost-grid row is impassable; a physical seam passage
        // must retain the general conservative heuristic instead.
        int row = (frame.Slot - 1) * width;
        for (int x = 0; x < width; x++)
            if (Cost(ref job, row + x) < 10000)
                return;
        bool goalA = frame.GoalFloor == af;
        var entry = goalA ? bc : ac; var exit = goalA ? ac : bc;
        int crossing = goalA ? b.Value[0].Cost : a.Value[0].Cost;
        int localExit = exit.z % frame.Slot;
        frame.EntryFloor = entry.z / frame.Slot; frame.EntryX = entry.x; frame.EntryZ = entry.z % frame.Slot;
        frame.EntryTail = crossing + Octile(exit.x - Mathf.Clamp(exit.x, frame.MinX, frame.MaxX), localExit - Mathf.Clamp(localExit, frame.MinZ, frame.MaxZ), frame.Cardinal, frame.Diagonal);
        frame.SinglePair = true;
    }
    public static int Octile(int x, int z, int cardinal, int diagonal)
    {
        x = Math.Abs(x); z = Math.Abs(z); int d = Math.Min(x, z);
        return d * Math.Min(diagonal, cardinal * 2) + (Math.Max(x, z) - d) * cardinal;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryProjected(IntVec3 cell, ref Frame frame, out float value)
    {
        value = 0;
        if (frame.Context == null)
            return false;
        value = ProjectedHeuristic(cell, ref frame);
        return true;
    }
    static float ProjectedHeuristic(IntVec3 cell, ref Frame frame)
    {
        int localZ = cell.z % frame.Slot;
        if (frame.SinglePair && cell.z / frame.Slot == frame.EntryFloor)
            return (Octile(cell.x - frame.EntryX, localZ - frame.EntryZ, frame.Cardinal, frame.Diagonal) + frame.EntryTail) * frame.Strength;
        if (!frame.SinglePair || cell.z / frame.Slot != frame.GoalFloor)
            return 0;
        int x = Mathf.Clamp(cell.x, frame.MinX, frame.MaxX);
        int z = Mathf.Clamp(localZ, frame.MinZ, frame.MaxZ);
        return Octile(cell.x - x, localZ - z, frame.Cardinal, frame.Diagonal) * frame.Strength;
    }
    delegate int CostReader(ref PathFinderJob job, int index);
    // Bind the native private cost method once; no worker reflection/game reads.
    static readonly CostReader Cost = BindCost();
    // Force binding during patch installation, so a failure cannot first occur
    // inside a scheduled search after routing guards have been bypassed.
    static ABNativePortalKernel() { }
    static CostReader BindCost()
    {
        var method = AccessTools.Method(typeof(PathFinderJob), "IndexCost");
        var bridge = new DynamicMethod("NativePortalIndexCost", typeof(int),
            new[] { typeof(PathFinderJob).MakeByRefType(), typeof(int) }, typeof(ABNativePortalKernel).Module, true);
        var il = bridge.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
        return (CostReader)bridge.CreateDelegate(typeof(CostReader));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Expand(ref PathFinderJob job, int parent, ref Frame frame)
    {
        var context = frame.Context;
        if (context != null && context.Edges.TryGetValue(parent, out var edges))
            ExpandEdges(ref job, parent, ref frame, edges);
    }
    static void ExpandEdges(ref PathFinderJob job, int parent, ref Frame frame, ABNativePortals.Edge[] edges)
    {
        foreach (var edge in edges)
        {
            var node = job.calcGrid[edge.Exit];
            if (node.status == PathFinderJob.CalcNode.Status.Closed) continue;
            int cost = Cost(ref job, edge.Exit);
            if (cost >= 10000) continue;
            int candidate = job.calcGrid[parent].gCost + cost + edge.Cost;
            if (node.status != PathFinderJob.CalcNode.Status.None && node.gCost <= candidate) continue;
            if (node.status == PathFinderJob.CalcNode.Status.None)
                node.hCost = ProjectedHeuristic(job.indices.IndexToCell(edge.Exit), ref frame);
            node.parentIndex = parent; node.gCost = candidate;
            node.fCost = Math.Max(0, candidate + node.hCost);
            node.status = PathFinderJob.CalcNode.Status.Open;
            job.calcGrid[edge.Exit] = node;
            job.frontier.Enqueue(edge.Exit, node.fCost);
        }
    }

    // Validate all anchors before editing anything. Reject unknown engine or
    // competing transpiler shapes instead of enabling only half the routing.
    public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator)
    {
        var code = instructions.Select(c => new CodeInstruction(c)).ToList();
        var immutable = new[] { nameof(PathFinderJob.destRect), nameof(PathFinderJob.moveTicksCardinal), nameof(PathFinderJob.moveTicksDiagonal), nameof(PathFinderJob.heuristicStrength) }.Select(name => AccessTools.Field(typeof(PathFinderJob), name)).ToArray();
        if (code.Any(c => immutable.Any(field => c.StoresField(field)))) throw new InvalidOperationException("Unsupported mutation of captured native path parameters");
        var locals = original.GetMethodBody()?.LocalVariables;
        var h = AccessTools.Field(typeof(PathFinderJob.CalcNode), "hCost");
        var grid = AccessTools.Field(typeof(PathFinderJob), "calcGrid");
        var closes = new List<int>();
        for (int i = 0; i + 5 < code.Count; i++)
            if (code[i].opcode == OpCodes.Ldloc_2 && code[i + 1].opcode == OpCodes.Ldc_I4_1 &&
                code[i + 2].opcode == OpCodes.Add && code[i + 3].opcode == OpCodes.Stloc_2 &&
                code[i + 4].opcode == OpCodes.Ldarg_0 && code[i + 5].LoadsField(grid, true)) closes.Add(i);
        var heuristics = Enumerable.Range(0, code.Count).Where(i => code[i].StoresField(h)).ToArray();
        var dequeues = Enumerable.Range(0, code.Count).Where(i => code[i].operand is MethodInfo m && m.Name == "Dequeue").ToArray();
        var additions = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(AccessTools.Method(typeof(IntVec3), "op_Addition"))).ToArray();
        bool localShape = locals != null && locals.Count > 12 && locals[2].LocalType == typeof(int) && locals[4].LocalType == typeof(int) && locals[12].LocalType == typeof(IntVec3);
        bool dequeueShape = dequeues.Length == 1 && dequeues[0] >= 2 && Local(code[dequeues[0] - 2], OpCodes.Ldloca_S, 4);
        bool neighborShape = additions.Length == 1 && additions[0] + 1 < code.Count && Local(code[additions[0] + 1], OpCodes.Stloc_S, 12);
        if (!localShape || !dequeueShape || !neighborShape || closes.Count != 1 || heuristics.Length != 1 || code.Any(c => c.blocks.Count != 0))
            throw new InvalidOperationException("Unsupported native PathFinderJob.Execute shape; no portal search hooks installed");
        int close = closes[0], heuristic = heuristics[0];
        var destination = AccessTools.Field(typeof(PathFinderJob), "destCell");
        var starts = Enumerable.Range(0, code.Count - 4).Where(i => code[i].opcode == OpCodes.Ldarg_0 && code[i+1].LoadsField(destination) && Local(code[i+2], OpCodes.Ldloc_S, 12) && code[i+3].Calls(AccessTools.Method(typeof(IntVec3), "op_Subtraction")) && Local(code[i+4], OpCodes.Stloc_S, 22)).ToArray();
        if (starts.Length != 1 || locals.Count <= 26 || locals[26].LocalType != typeof(PathFinderJob.CalcNode)) throw new InvalidOperationException("Unsupported native heuristic block");
        int heuristicStart = starts[0];
        var nativeHeuristic = generator.DefineLabel(); var storeHeuristic = generator.DefineLabel();
        code[heuristicStart].labels.Add(nativeHeuristic); code[heuristic].labels.Add(storeHeuristic);
        var projected = generator.DeclareLocal(typeof(float));
        var frame = generator.DeclareLocal(typeof(Frame));
        var result = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            CodeInstruction.Call(typeof(ABNativePortalKernel), nameof(Begin)),
            new CodeInstruction(OpCodes.Stloc, frame)
        };
        for (int i = 0; i < code.Count; i++)
        {
            if (i == close)
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(code[i].labels); code[i].labels.Clear();
                result.Add(first);
                result.Add(new CodeInstruction(OpCodes.Ldloc_S, (byte)4));
                result.Add(new CodeInstruction(OpCodes.Ldloca, frame));
                result.Add(CodeInstruction.Call(typeof(ABNativePortalKernel), nameof(Expand)));
            }
            if (i == heuristicStart)
            {
                result.Add(new CodeInstruction(OpCodes.Ldloc_S, (byte)12));
                result.Add(new CodeInstruction(OpCodes.Ldloca, frame)); result.Add(new CodeInstruction(OpCodes.Ldloca, projected));
                result.Add(CodeInstruction.Call(typeof(ABNativePortalKernel), nameof(TryProjected))); result.Add(new CodeInstruction(OpCodes.Brfalse, nativeHeuristic));
                result.Add(new CodeInstruction(OpCodes.Ldarg_0)); result.Add(new CodeInstruction(OpCodes.Ldflda, grid)); result.Add(new CodeInstruction(OpCodes.Ldloc_S, (byte)13));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(grid.FieldType, "Item"))); result.Add(new CodeInstruction(OpCodes.Stloc_S, (byte)26));
                result.Add(new CodeInstruction(OpCodes.Ldloca_S, (byte)26)); result.Add(new CodeInstruction(OpCodes.Ldloc, projected)); result.Add(new CodeInstruction(OpCodes.Br, storeHeuristic));
            }
            result.Add(code[i]);
        }
        return result;
    }
    static bool Local(CodeInstruction code, OpCode opcode, int index) => code.opcode == opcode && (code.operand switch
    {
        LocalBuilder b => b.LocalIndex == index,
        LocalVariableInfo v => v.LocalIndex == index,
        byte b => b == index,
        int n => n == index,
        _ => false
    });
}
