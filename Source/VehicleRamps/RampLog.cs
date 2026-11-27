using Verse;

namespace AASBVehicleRamps;

/// <summary>
/// Logging. Startup status is always written; everything else is a debug line that only appears with
/// Development mode and verbose logging enabled.
/// </summary>
public static class RampLog
{
    private const string Prefix = "[AASB Vehicle Ramps] ";

    public static bool Verbose => Prefs.DevMode && Prefs.LogVerbose;

    public static void Message(string text) => Log.Message(Prefix + text);

    public static void Error(string text) => Log.Error(Prefix + text);

    public static void Debug(string text)
    {
        if (Verbose)
        {
            Log.Message(Prefix + text);
        }
    }
}
