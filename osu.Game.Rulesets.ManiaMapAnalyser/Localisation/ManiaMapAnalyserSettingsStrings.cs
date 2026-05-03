using System;
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Localisation;

public static class ManiaMapAnalyserSettingsStrings
{
    public static LocalisableString SettingsMenuHeader => text("Mania Map Analyser", "Mania Map Analyser");
    public static LocalisableString EnableInSettingsMenu => text("启用 ManiaMapAnalyser", "Enable Mania Map Analyser");
    public static LocalisableString EnableInSettingsMenuHint => text("在 mania 选歌界面启用分析器叠加层。", "Enable the analyser overlay on mania song select.");
    public static LocalisableString OverlayPositionX => text("叠加层位置 X", "Overlay position X");
    public static LocalisableString OverlayPositionXHint => text("实时调整叠加层的水平位置。", "Move the overlay horizontally in real time.");
    public static LocalisableString OverlayPositionY => text("叠加层位置 Y", "Overlay position Y");
    public static LocalisableString OverlayPositionYHint => text("实时调整叠加层的垂直位置。", "Move the overlay vertically in real time.");
    public static LocalisableString OverlayOpacity => text("叠加层透明度", "Overlay opacity");
    public static LocalisableString OverlayOpacityHint => text("实时调整叠加层透明度。", "Adjust the overlay transparency in real time.");
    public static LocalisableString TextSize => text("文字大小", "Text size");
    public static LocalisableString TextSizeHint => text("实时调整两行文字的字号。", "Adjust the font size of both text lines in real time.");
    public static LocalisableString TextColourHex => text("文字颜色", "Text colour");
    public static LocalisableString TextColourHexHint => text("填写十六进制 RGBA，例如 #D2DBEBFF。", "Enter RGBA hex, for example #D2DBEBFF.");
    public static LocalisableString BackgroundColourHex => text("背景颜色", "Background colour");
    public static LocalisableString BackgroundColourHexHint => text("填写十六进制 RGBA，例如 #12161FD2。", "Enter RGBA hex, for example #12161FD2.");
    public static LocalisableString ContentPadding => text("边距", "Padding");
    public static LocalisableString ContentPaddingHint => text("实时调整文字到背景外边缘的边距。", "Adjust the padding between the text and the background edge in real time.");

    private static LocalisableString text(string zhCn, string en) => new LocalisableString(new BilingualString(zhCn, en));

    private sealed class BilingualString : ILocalisableStringData
    {
        private readonly string zhCn;
        private readonly string en;

        public BilingualString(string zhCn, string en)
        {
            this.zhCn = zhCn;
            this.en = en;
        }

        public string GetLocalised(LocalisationParameters parameters)
        {
            string? language = parameters.Store?.EffectiveCulture.TwoLetterISOLanguageName;
            return string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) ? zhCn : en;
        }

        public bool Equals(ILocalisableStringData? other)
            => other is BilingualString bilingual && Equals(bilingual);

        private bool Equals(BilingualString? other)
            => other != null && zhCn == other.zhCn && en == other.en;

        public override bool Equals(object? obj)
            => obj is BilingualString bilingual && Equals(bilingual);

        public override int GetHashCode()
            => HashCode.Combine(zhCn, en);
    }
}
