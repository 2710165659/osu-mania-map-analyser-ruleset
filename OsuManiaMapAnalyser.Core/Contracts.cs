using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuManiaMapAnalyser.Core;

public sealed class AnalyzeRequest
{
    [JsonPropertyName("beatmap")]
    public BeatmapInput? Beatmap { get; init; }

    [JsonPropertyName("settings")]
    public AnalyzeSettingsInput? Settings { get; init; }
}

public sealed class BeatmapInput
{
    [JsonPropertyName("osuText")]
    public string? OsuText { get; init; }
}

public sealed class AnalyzeSettingsInput
{
    [JsonPropertyName("speedRate")]
    public double? SpeedRate { get; init; }

    [JsonPropertyName("odFlag")]
    public JsonElement? OdFlag { get; init; }

    [JsonPropertyName("cvtFlag")]
    public string? CvtFlag { get; init; }
}

public sealed class AnalyzeResponse
{
    [JsonPropertyName("metadata")]
    public CardMetadataOutput Metadata { get; init; } = new();

    [JsonPropertyName("beatmap")]
    public CardBeatmapOutput Beatmap { get; init; } = new();

    [JsonPropertyName("card")]
    public CardOutput Card { get; init; } = new();
}

public sealed class CardMetadataOutput
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("titleUnicode")]
    public string TitleUnicode { get; init; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("artistUnicode")]
    public string ArtistUnicode { get; init; } = string.Empty;

    [JsonPropertyName("creator")]
    public string Creator { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("statusText")]
    public string StatusText { get; init; } = string.Empty;
}

public sealed class CardBeatmapOutput
{
    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; init; }

    [JsonPropertyName("lnRatio")]
    public double LnRatio { get; init; }
}

public sealed class CardOutput
{
    [JsonPropertyName("contentBar")]
    public string ContentBar { get; init; } = "None";

    [JsonPropertyName("modeTag")]
    public string ModeTag { get; init; } = "Mix";

    [JsonPropertyName("leftCapsule")]
    public CardCapsuleOutput LeftCapsule { get; init; } = new();

    [JsonPropertyName("difficulty")]
    public CardDifficultyOutput Difficulty { get; init; } = new();
}

public sealed class CardCapsuleOutput
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "ReworkSR";

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("displayValue")]
    public string DisplayValue { get; init; } = "-";

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;
}

public sealed class CardDifficultyOutput
{
    [JsonPropertyName("caption")]
    public string Caption { get; init; } = "Estimate Difficulty";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "-";

    [JsonPropertyName("rawText")]
    public string RawText { get; init; } = "-";

    [JsonPropertyName("numericDifficulty")]
    public double? NumericDifficulty { get; init; }

    [JsonPropertyName("vibro")]
    public bool Vibro { get; init; }
}
