using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Configuration;

public class ManiaMapAnalyserRulesetConfigManager : RulesetConfigManager<ManiaMapAnalyserSetting>
{
    public ManiaMapAnalyserRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
        : base(settings, ruleset, variant)
    {
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(ManiaMapAnalyserSetting.OverlayEnabled, true);
        SetDefault(ManiaMapAnalyserSetting.OverlayPositionX, 0.35f, 0f, 1f, 0.01f);
        SetDefault(ManiaMapAnalyserSetting.OverlayPositionY, 0f, 0f, 1f, 0.01f);
        SetDefault(ManiaMapAnalyserSetting.OverlayOpacity, 0.7f, 0.2f, 1f, 0.01f);
        SetDefault(ManiaMapAnalyserSetting.TextSize, 20f, 10f, 48f, 1f);
        SetDefault(ManiaMapAnalyserSetting.TextColourHex, "#D2DBEBFF");
        SetDefault(ManiaMapAnalyserSetting.BackgroundColourHex, "#12161FD2");
        SetDefault(ManiaMapAnalyserSetting.ContentPadding, 16f, 4f, 40f, 1f);
    }
}
