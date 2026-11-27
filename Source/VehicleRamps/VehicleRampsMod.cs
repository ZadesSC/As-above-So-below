using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace AASBVehicleRamps;

/// <summary>
/// Startup entry point. Logs exactly one Active or NOT ACTIVE line. When not active no patch remains, so the ramp
/// defs behave as plain AASB staircases for pawns and stay impassable to vehicles.
/// </summary>
[StaticConstructorOnStartup]
public static class VehicleRampsMod
{
    public const string HarmonyId = "zades.aasb.vehicleramps";

    static VehicleRampsMod()
    {
        Initialize();
    }

    public static bool Active { get; private set; }

    public static string FailureReason { get; private set; }

    private static void Initialize()
    {
        var harmony = new Harmony(HarmonyId);
        string failure;
        try
        {
            failure = ValidateIsolated();
        }
        catch (Exception e)
        {
            // A type that no longer loads fails when the validation method is compiled.
            failure = "binding failed: " + e.GetType().Name + ": " + e.Message;
        }

        if (failure == null)
        {
            try
            {
                InstallIsolated(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(HarmonyId);
                failure = "patching failed and was rolled back: " + e.GetType().Name + ": " + e.Message;
            }
        }

        Active = failure == null;
        FailureReason = failure;
        if (Active)
        {
            RampLog.Message("Active: " + RampDefs.Count + " ramp defs, vehicle path search extended.");
        }
        else
        {
            RampLog.Error("NOT ACTIVE; vehicle ramps work only as AASB staircases: " + failure);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ValidateIsolated() => StartupBinding.Validate();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstallIsolated(Harmony harmony) => StartupBinding.Install(harmony);
}
