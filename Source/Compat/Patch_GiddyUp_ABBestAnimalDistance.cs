using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AsAboveSoBelow;
[HarmonyPatch]
public static class Patch_GiddyUp_ABBestAnimalDistance
{
    private static MethodBase TargetMethod()
    {
        return GiddyUpCompat.GetBestAnimalMethod();
    }

    private static bool Prepare()
    {
        return TargetMethod() != null && GiddyUpCompat.CanRewrite;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return GiddyUpCompat.Rewrite(instructions, "GetBestAnimal", 5);
    }
}
