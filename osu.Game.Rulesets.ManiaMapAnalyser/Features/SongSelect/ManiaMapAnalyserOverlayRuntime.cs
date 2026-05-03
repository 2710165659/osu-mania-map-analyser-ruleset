using osu.Framework.Bindables;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Features.SongSelect;

public static class ManiaMapAnalyserOverlayRuntime
{
    public static BindableBool OverlayEnabled { get; } = new BindableBool(true);
    public static BindableFloat OverlayPositionX { get; } = new BindableFloat(0.72f);
    public static BindableFloat OverlayPositionY { get; } = new BindableFloat(0.17f);
    public static BindableFloat OverlayOpacity { get; } = new BindableFloat(1f);
    public static BindableFloat TextSize { get; } = new BindableFloat(18f);
    public static Bindable<string> TextColourHex { get; } = new Bindable<string>("#D2DBEBFF");
    public static Bindable<string> BackgroundColourHex { get; } = new Bindable<string>("#12161FD2");
    public static BindableFloat ContentPadding { get; } = new BindableFloat(16f);

    public static void SetOverlayEnabled(bool enabled)
    {
        if (OverlayEnabled.Value != enabled)
            OverlayEnabled.Value = enabled;
    }

    public static void SetOverlayPositionX(float positionX)
    {
        if (OverlayPositionX.Value != positionX)
            OverlayPositionX.Value = positionX;
    }

    public static void SetOverlayPositionY(float positionY)
    {
        if (OverlayPositionY.Value != positionY)
            OverlayPositionY.Value = positionY;
    }

    public static void SetOverlayOpacity(float opacity)
    {
        if (OverlayOpacity.Value != opacity)
            OverlayOpacity.Value = opacity;
    }

    public static void SetTextSize(float textSize)
    {
        if (TextSize.Value != textSize)
            TextSize.Value = textSize;
    }

    public static void SetTextColourHex(string textColourHex)
    {
        if (TextColourHex.Value != textColourHex)
            TextColourHex.Value = textColourHex;
    }

    public static void SetBackgroundColourHex(string backgroundColourHex)
    {
        if (BackgroundColourHex.Value != backgroundColourHex)
            BackgroundColourHex.Value = backgroundColourHex;
    }

    public static void SetContentPadding(float contentPadding)
    {
        if (ContentPadding.Value != contentPadding)
            ContentPadding.Value = contentPadding;
    }
}
