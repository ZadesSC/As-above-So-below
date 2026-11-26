using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_GiddyUp_ABAutoMountDistance
{
    private static MethodBase TargetMethod()
    {
        return GiddyUpCompat.TryAutoMountMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && GiddyUpCompat.CanRewrite;
    }

    private static void Prefix(Pawn __0)
    {
        GiddyUpCompat.Push(__0);
    }

    private static void Finalizer()
    {
        GiddyUpCompat.Pop();
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return GiddyUpCompat.Rewrite(instructions, "TryAutoMount", 3);
    }
}
