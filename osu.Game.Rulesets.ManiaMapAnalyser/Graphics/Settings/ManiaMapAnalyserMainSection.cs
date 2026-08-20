using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.ManiaMapAnalyser.Configuration;
using osu.Game.Rulesets.ManiaMapAnalyser.Features.SongSelect;
using osu.Game.Rulesets.ManiaMapAnalyser.Localisation;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Graphics.Settings;

public sealed partial class ManiaMapAnalyserMainSection : RulesetSettingsSubsection
{
    private readonly BindableBool overlayEnabled = new();
    private readonly BindableFloat overlayPositionX = new();
    private readonly BindableFloat overlayPositionY = new();
    private readonly BindableFloat overlayOpacity = new();
    private readonly BindableFloat textSize = new();
    private readonly Bindable<string> textColourHex = new();
    private readonly Bindable<string> backgroundColourHex = new();
    private readonly BindableFloat contentPadding = new();

    public ManiaMapAnalyserMainSection(Ruleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var config = (ManiaMapAnalyserRulesetConfigManager)Config;

        overlayEnabled.BindTo(config.GetBindable<bool>(ManiaMapAnalyserSetting.OverlayEnabled));
        overlayPositionX.BindTo(config.GetBindable<float>(ManiaMapAnalyserSetting.OverlayPositionX));
        overlayPositionY.BindTo(config.GetBindable<float>(ManiaMapAnalyserSetting.OverlayPositionY));
        overlayOpacity.BindTo(config.GetBindable<float>(ManiaMapAnalyserSetting.OverlayOpacity));
        textSize.BindTo(config.GetBindable<float>(ManiaMapAnalyserSetting.TextSize));
        textColourHex.BindTo(config.GetBindable<string>(ManiaMapAnalyserSetting.TextColourHex));
        backgroundColourHex.BindTo(config.GetBindable<string>(ManiaMapAnalyserSetting.BackgroundColourHex));
        contentPadding.BindTo(config.GetBindable<float>(ManiaMapAnalyserSetting.ContentPadding));

        overlayEnabled.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetOverlayEnabled(v.NewValue), true);
        overlayPositionX.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetOverlayPositionX(v.NewValue), true);
        overlayPositionY.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetOverlayPositionY(v.NewValue), true);
        overlayOpacity.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetOverlayOpacity(v.NewValue), true);
        textSize.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetTextSize(v.NewValue), true);
        textColourHex.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetTextColourHex(v.NewValue), true);
        backgroundColourHex.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetBackgroundColourHex(v.NewValue), true);
        contentPadding.BindValueChanged(v => ManiaMapAnalyserOverlayRuntime.SetContentPadding(v.NewValue), true);

        Children = new Drawable[]
        {
            new SettingsItemV2(new FormCheckBox
            {
                Caption = ManiaMapAnalyserSettingsStrings.EnableInSettingsMenu,
                HintText = ManiaMapAnalyserSettingsStrings.EnableInSettingsMenuHint,
                Current = overlayEnabled,
            }),
            new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = ManiaMapAnalyserSettingsStrings.OverlayPositionX,
                HintText = ManiaMapAnalyserSettingsStrings.OverlayPositionXHint,
                Current = overlayPositionX,
                KeyboardStep = 0.01f,
                LabelFormat = v => $"{v:0.00}",
            }),
            new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = ManiaMapAnalyserSettingsStrings.OverlayPositionY,
                HintText = ManiaMapAnalyserSettingsStrings.OverlayPositionYHint,
                Current = overlayPositionY,
                KeyboardStep = 0.01f,
                LabelFormat = v => $"{v:0.00}",
            }),
            new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = ManiaMapAnalyserSettingsStrings.OverlayOpacity,
                HintText = ManiaMapAnalyserSettingsStrings.OverlayOpacityHint,
                Current = overlayOpacity,
                KeyboardStep = 0.01f,
                LabelFormat = v => $"{v * 100:0}%",
            }),
            new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = ManiaMapAnalyserSettingsStrings.TextSize,
                HintText = ManiaMapAnalyserSettingsStrings.TextSizeHint,
                Current = textSize,
                KeyboardStep = 1f,
                LabelFormat = v => $"{v:0}",
            }),
            new SettingsItemV2(new FormTextBox
            {
                Caption = ManiaMapAnalyserSettingsStrings.TextColourHex,
                HintText = ManiaMapAnalyserSettingsStrings.TextColourHexHint,
                PlaceholderText = "#RRGGBBAA",
                Current = textColourHex,
            }),
            new SettingsItemV2(new FormTextBox
            {
                Caption = ManiaMapAnalyserSettingsStrings.BackgroundColourHex,
                HintText = ManiaMapAnalyserSettingsStrings.BackgroundColourHexHint,
                PlaceholderText = "#RRGGBBAA",
                Current = backgroundColourHex,
            }),
            new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = ManiaMapAnalyserSettingsStrings.ContentPadding,
                HintText = ManiaMapAnalyserSettingsStrings.ContentPaddingHint,
                Current = contentPadding,
                KeyboardStep = 1f,
                LabelFormat = v => $"{v:0}",
            }),
        };
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        overlayEnabled.UnbindAll();
        overlayPositionX.UnbindAll();
        overlayPositionY.UnbindAll();
        overlayOpacity.UnbindAll();
        textSize.UnbindAll();
        textColourHex.UnbindAll();
        backgroundColourHex.UnbindAll();
        contentPadding.UnbindAll();
    }
}
