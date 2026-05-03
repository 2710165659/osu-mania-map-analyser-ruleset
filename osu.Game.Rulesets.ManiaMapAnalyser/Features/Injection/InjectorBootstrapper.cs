using System;
using osu.Framework.Threading;
using osu.Game.Rulesets.ManiaMapAnalyser.Features.SongSelect;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Features.Injection;

public static class InjectorBootstrapper
{
    private static int currentSessionHash = int.MinValue;

    public static bool BeginInject(OsuGame game, Scheduler scheduler)
    {
        int sessionHash = game.GetHashCode();

        if (sessionHash == currentSessionHash)
            return true;

        currentSessionHash = sessionHash;

        scheduler.AddDelayed(() =>
        {
            try
            {
                game.Add(new ManiaSongSelectOverlayInterceptor());
            }
            catch (Exception ex)
            {
                currentSessionHash = int.MinValue;
                ManiaMapAnalyserLogging.Error(ex, "Failed to inject the song select overlay interceptor.");
            }
        }, 1);

        return true;
    }
}
