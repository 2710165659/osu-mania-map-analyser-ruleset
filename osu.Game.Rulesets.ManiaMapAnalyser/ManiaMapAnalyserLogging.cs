using osu.Framework.Logging;

namespace osu.Game.Rulesets.ManiaMapAnalyser;

public static class ManiaMapAnalyserLogging
{
    private const string prefix = "[ManiaMapAnalyser]";

    public static void Log(string message, LogLevel level = LogLevel.Verbose)
        => Logger.Log($"{prefix} {message}", level: level);

    public static void Error(System.Exception exception, string message)
        => Logger.Error(exception, $"{prefix} {message}");
}
