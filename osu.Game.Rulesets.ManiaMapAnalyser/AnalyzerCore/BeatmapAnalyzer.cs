using System.Globalization;
using System.Text.Json;

namespace OsuManiaMapAnalyser.Core;

public static class BeatmapAnalyzer
{
    private const string DefaultEstimatorAlgorithm = "Mixed";
    private const string DefaultEtternaVersion = "0.72.3";
    private const string DefaultCompanellaEtternaVersion = "0.74.0";
    private const bool DefaultVibroDetection = true;
    private const bool DefaultEnableNumericDifficulty = true;
    private const string DefaultContentBar = "None";
    private const string DefaultDiffText = "Difficulty";

    private static readonly HashSet<int> GraphSupportedKeys = [4, 6, 7];

    public static AnalyzeResponse Analyze(AnalyzeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var osuText = request.Beatmap?.OsuText;
        if (string.IsNullOrWhiteSpace(osuText))
        {
            throw new InvalidOperationException("Input JSON must provide beatmap.osuText.");
        }

        var settings = NormalizeSettings(request.Settings);
        var rawChart = BeatmapParser.Parse(osuText);
        if (rawChart.Status == "Fail")
        {
            throw new InvalidOperationException("Beatmap parse failed.");
        }

        if (rawChart.Status == "NotMania")
        {
            throw new InvalidOperationException("Beatmap mode is not mania.");
        }

        var estimatorResult = RunMixedEstimator(rawChart, settings);
        if (!double.IsFinite(estimatorResult.Star))
        {
            throw new InvalidOperationException("Estimator failed to produce a valid result.");
        }

        var fallbackModeTag = ModeTagFromLnRatio(estimatorResult.LnRatio);
        var resolvedDifficulty = estimatorResult.Difficulty;
        var resolvedNumericDifficulty = estimatorResult.NumericDifficulty;
        var resolvedNumericDifficultyHint = estimatorResult.NumericDifficultyHint;

        IReadOnlyDictionary<string, double>? etternaValues = null;
        var vibroDetected = false;
        try
        {
            etternaValues = AnalyzeEtterna(rawChart, settings.CvtFlag, settings.SpeedRate, DefaultEtternaVersion);
            var vibroEligible = estimatorResult.Star > 5.0;
            vibroDetected = DefaultVibroDetection
                && vibroEligible
                && DetectVibro(etternaValues, 0.95);
        }
        catch
        {
            etternaValues = null;
            vibroDetected = false;
        }

        if (estimatorResult.ColumnCount == 4 && estimatorResult.MixedCompanellaPlan != null)
        {
            double? interludeOverall = null;
            try
            {
                interludeOverall = InterludeCalculator.Calculate(rawChart.ApplyConversion(settings.CvtFlag), settings.SpeedRate);
            }
            catch
            {
                interludeOverall = null;
            }

            IReadOnlyDictionary<string, double>? companellaMsdValues = etternaValues;
            if (!string.Equals(DefaultCompanellaEtternaVersion, DefaultEtternaVersion, StringComparison.Ordinal))
            {
                try
                {
                    companellaMsdValues = AnalyzeEtterna(
                        rawChart,
                        settings.CvtFlag,
                        settings.SpeedRate,
                        DefaultCompanellaEtternaVersion);
                }
                catch
                {
                    companellaMsdValues = etternaValues;
                }
            }

            if (companellaMsdValues != null && interludeOverall.HasValue && double.IsFinite(interludeOverall.Value))
            {
                try
                {
                    var companellaResult = CompanellaClassifier.Classify(
                        companellaMsdValues,
                        interludeOverall.Value,
                        estimatorResult.Star);

                    estimatorResult = ApplyCompanellaToMixedResult(estimatorResult, companellaResult);
                    resolvedDifficulty = estimatorResult.Difficulty;
                    resolvedNumericDifficulty = estimatorResult.NumericDifficulty;
                    resolvedNumericDifficultyHint = estimatorResult.NumericDifficultyHint;
                }
                catch
                {
                    // Keep the pre-Companella mixed result when classification inputs are unavailable.
                }
            }
        }

        var leftCapsuleMode = ResolveShortCardSrMode(fallbackModeTag);
        var (leftValue, leftDisplayValue, leftUnit) = ResolveLeftCapsule(leftCapsuleMode, estimatorResult, etternaValues);
        var rawDifficultyText = string.IsNullOrWhiteSpace(resolvedDifficulty) ? "-" : resolvedDifficulty;
        var visibleDifficultyText = vibroDetected && DefaultDiffText == "Difficulty"
            ? "VIBRO"
            : (GraphSupportedKeys.Contains(estimatorResult.ColumnCount)
                ? FormatDiffForDisplay(rawDifficultyText)
                : "Unsupported Keys");

        return new AnalyzeResponse
        {
            Metadata = new CardMetadataOutput
            {
                Title = rawChart.Metadata.Title,
                TitleUnicode = rawChart.Metadata.TitleUnicode,
                Artist = rawChart.Metadata.Artist,
                ArtistUnicode = rawChart.Metadata.ArtistUnicode,
                Creator = rawChart.Metadata.Creator,
                Version = rawChart.Metadata.Version,
                StatusText = FormatMetadataStatus(rawChart.Metadata),
            },
            Beatmap = new CardBeatmapOutput
            {
                ColumnCount = rawChart.ColumnCount,
                LnRatio = rawChart.LnRatio,
            },
            Card = new CardOutput
            {
                ContentBar = DefaultContentBar,
                ModeTag = fallbackModeTag,
                LeftCapsule = new CardCapsuleOutput
                {
                    Mode = leftCapsuleMode,
                    Value = leftValue,
                    DisplayValue = leftDisplayValue,
                    Unit = leftUnit,
                },
                Difficulty = new CardDifficultyOutput
                {
                    Caption = BuildDifficultyCaption(
                        fallbackModeTag,
                        resolvedNumericDifficulty,
                        resolvedNumericDifficultyHint,
                        vibroDetected),
                    Text = visibleDifficultyText,
                    RawText = rawDifficultyText,
                    NumericDifficulty = resolvedNumericDifficulty,
                    Vibro = vibroDetected,
                },
            },
        };
    }

    public static AnalyzeResponse AnalyzeJson(string json)
    {
        var request = JsonSerializer.Deserialize<AnalyzeRequest>(json)
            ?? throw new InvalidOperationException("Input JSON is invalid.");
        return Analyze(request);
    }

    public static string AnalyzeToJson(AnalyzeRequest request, bool indented = true)
    {
        return JsonSerializer.Serialize(Analyze(request), CreateJsonOptions(indented));
    }

    public static string AnalyzeJsonToJson(string json, bool indented = true)
    {
        return JsonSerializer.Serialize(AnalyzeJson(json), CreateJsonOptions(indented));
    }

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = indented,
        };
    }

    private static NormalizedSettings NormalizeSettings(AnalyzeSettingsInput? input)
    {
        var speedRate = input?.SpeedRate is { } value && double.IsFinite(value) && value > 0
            ? value
            : 1.0;

        var cvtFlag = string.IsNullOrWhiteSpace(input?.CvtFlag)
            ? null
            : input!.CvtFlag!.Trim().ToUpperInvariant();

        return new NormalizedSettings(
            speedRate,
            NormalizeOdFlag(input?.OdFlag),
            string.IsNullOrWhiteSpace(cvtFlag) ? null : cvtFlag);
    }

    private static string? NormalizeOdFlag(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        var value = element.Value;
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
            JsonValueKind.Number => value.TryGetDouble(out var numeric)
                ? numeric.ToString("0.###############", CultureInfo.InvariantCulture)
                : null,
            _ => null,
        };
    }

    private static EstimatorResult RunSunnyEstimator(BeatmapChart rawChart, NormalizedSettings settings, string? cvtOverride = null)
    {
        var chart = rawChart.ApplyConversion(cvtOverride ?? settings.CvtFlag);
        var effectiveOd = ResolveOverallDifficulty(rawChart.OverallDifficulty, settings.OdFlag);
        var star = SunnyCalculator.Calculate(chart, settings.SpeedRate, effectiveOd);
        return new EstimatorResult
        {
            Star = star,
            LnRatio = chart.LnRatio,
            ColumnCount = chart.ColumnCount,
            Difficulty = ReworkSupport.EstimateDifficulty(star, chart.LnRatio, chart.ColumnCount),
            NumericDifficulty = null,
            NumericDifficultyHint = null,
        };
    }

    private static EstimatorResult RunDanielEstimator(BeatmapChart rawChart, NormalizedSettings settings)
    {
        if (rawChart.ColumnCount != 4)
        {
            return RunSunnyEstimator(rawChart, settings);
        }

        var star = DanielCalculator.CalculateStar(rawChart, settings.SpeedRate);
        var danielDifficulty = ReworkSupport.EstimateDanielDan(star);
        return new EstimatorResult
        {
            Star = star,
            LnRatio = rawChart.LnRatio,
            ColumnCount = rawChart.ColumnCount,
            Difficulty = danielDifficulty.Label,
            NumericDifficulty = danielDifficulty.Numeric,
            NumericDifficultyHint = danielDifficulty.Numeric.HasValue ? null : "N/A",
        };
    }

    private static EstimatorResult RunAzusaEstimator(
        BeatmapChart rawChart,
        NormalizedSettings settings,
        bool forceSunnyReferenceHo,
        EstimatorResult? precomputedDanielResult,
        EstimatorResult? precomputedSunnyResult)
    {
        return AzusaEstimator.Estimate(
            rawChart,
            settings.SpeedRate,
            forceSunnyReferenceHo,
            precomputedDanielResult,
            precomputedSunnyResult,
            () => RunDanielEstimator(rawChart, settings),
            sunnyOverride => RunSunnyEstimator(rawChart, settings, sunnyOverride));
    }

    private static EstimatorResult RunMixedEstimator(BeatmapChart rawChart, NormalizedSettings settings)
    {
        var sunnyBaseline = RunSunnyEstimator(rawChart, settings);
        var columnCount = sunnyBaseline.ColumnCount;
        if (columnCount is not (4 or 6 or 7))
        {
            return CloneEstimatorResult(sunnyBaseline, mixedCompanellaPlan: null, overrideMixedCompanellaPlan: true);
        }

        var (inEnabled, hoEnabled) = ParseCvtFlags(settings.CvtFlag);
        var mixedModeTag = hoEnabled ? "RC" : ModeTagFromLnRatio(sunnyBaseline.LnRatio);

        if (mixedModeTag == "RC" && columnCount != 4)
        {
            return CloneEstimatorResult(sunnyBaseline, mixedCompanellaPlan: null, overrideMixedCompanellaPlan: true);
        }

        var selectedRework = sunnyBaseline;
        var estDiff = sunnyBaseline.Difficulty;
        var numericDifficulty = sunnyBaseline.NumericDifficulty;
        var numericDifficultyHint = sunnyBaseline.NumericDifficultyHint;
        MixedCompanellaPlan? companellaPlan = null;

        if (mixedModeTag == "RC")
        {
            if (!inEnabled)
            {
                var azusaResult = TryRunAzusaFallback(rawChart, settings, sunnyBaseline);
                if (CanUseAzusaResult(azusaResult))
                {
                    selectedRework = azusaResult!;
                    estDiff = azusaResult!.Difficulty;
                    numericDifficulty = azusaResult.NumericDifficulty;
                    numericDifficultyHint = azusaResult.NumericDifficultyHint;
                }
                else
                {
                    var danielResult = TryRunDanielFallback(rawChart, settings);
                    var canUseDaniel = danielResult != null
                        && danielResult.ColumnCount == 4
                        && !ReworkSupport.IsDanielTooLowDifficulty(danielResult.Difficulty);

                    if (canUseDaniel)
                    {
                        selectedRework = danielResult!;
                        estDiff = danielResult!.Difficulty;
                        numericDifficulty = danielResult.NumericDifficulty;
                        numericDifficultyHint = danielResult.NumericDifficultyHint;
                    }
                }
            }
        }
        else
        {
            var sunnyParts = SplitDifficultyParts(sunnyBaseline.Difficulty);
            var lnRatio = sunnyBaseline.LnRatio;
            var lnDifficulty = sunnyParts.Ln;

            var rcDifficulty = sunnyParts.Rc;
            var rcNumericDifficulty = sunnyBaseline.NumericDifficulty;
            var rcNumericDifficultyHint = sunnyBaseline.NumericDifficultyHint;

            if (columnCount == 4)
            {
                if (sunnyBaseline.Star < 9)
                {
                    companellaPlan = new MixedCompanellaPlan
                    {
                        LnRatio = lnRatio,
                        LnDifficulty = lnDifficulty,
                    };
                }
                else
                {
                    var danielResult = TryRunDanielFallback(rawChart, settings);
                    var canUseDaniel = danielResult != null
                        && danielResult.ColumnCount == 4
                        && !ReworkSupport.IsDanielTooLowDifficulty(danielResult.Difficulty);

                    if (canUseDaniel)
                    {
                        rcDifficulty = danielResult!.Difficulty;
                        rcNumericDifficulty = danielResult.NumericDifficulty;
                        rcNumericDifficultyHint = danielResult.NumericDifficultyHint;
                    }
                }
            }

            estDiff = ReworkSupport.ComposeDifficultyFromRcLn(rcDifficulty, lnDifficulty, lnRatio);
            numericDifficulty = rcNumericDifficulty;
            numericDifficultyHint = rcNumericDifficultyHint;
        }

        var forcedLnRatio = hoEnabled ? 0.0 : selectedRework.LnRatio;
        return CloneEstimatorResult(
            selectedRework,
            lnRatio: double.IsFinite(forcedLnRatio) ? forcedLnRatio : 0.0,
            difficulty: estDiff,
            numericDifficulty: numericDifficulty,
            numericDifficultyHint: numericDifficultyHint,
            mixedCompanellaPlan: companellaPlan,
            overrideNumericDifficulty: true,
            overrideNumericDifficultyHint: true,
            overrideMixedCompanellaPlan: true);
    }

    private static EstimatorResult ApplyCompanellaToMixedResult(EstimatorResult mixedResult, CompanellaResult companellaResult)
    {
        var plan = mixedResult.MixedCompanellaPlan;
        if (plan == null)
        {
            return mixedResult;
        }

        return CloneEstimatorResult(
            mixedResult,
            difficulty: ReworkSupport.ComposeDifficultyFromRcLn(
                companellaResult.Difficulty,
                plan.LnDifficulty,
                plan.LnRatio),
            numericDifficulty: companellaResult.NumericDifficulty,
            numericDifficultyHint: companellaResult.NumericDifficultyHint,
            mixedCompanellaPlan: null,
            overrideNumericDifficulty: true,
            overrideNumericDifficultyHint: true,
            overrideMixedCompanellaPlan: true);
    }

    private static EstimatorResult? TryRunDanielFallback(BeatmapChart rawChart, NormalizedSettings settings)
    {
        try
        {
            return RunDanielEstimator(rawChart, settings);
        }
        catch
        {
            return null;
        }
    }

    private static EstimatorResult? TryRunAzusaFallback(BeatmapChart rawChart, NormalizedSettings settings, EstimatorResult sunnyBaseline)
    {
        try
        {
            return RunAzusaEstimator(
                rawChart,
                settings,
                forceSunnyReferenceHo: false,
                precomputedDanielResult: null,
                precomputedSunnyResult: sunnyBaseline);
        }
        catch
        {
            return null;
        }
    }

    private static bool CanUseAzusaResult(EstimatorResult? result)
    {
        if (result == null || result.ColumnCount != 4)
        {
            return false;
        }

        var estDiff = (result.Difficulty ?? string.Empty).Trim();
        return estDiff.Length > 0
            && !estDiff.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool InEnabled, bool HoEnabled) ParseCvtFlags(string? value)
    {
        var normalized = (value ?? string.Empty).ToUpperInvariant();
        return (
            normalized.Contains("IN", StringComparison.Ordinal),
            normalized.Contains("HO", StringComparison.Ordinal));
    }

    private static (string Rc, string Ln) SplitDifficultyParts(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return ("-", "-");
        }

        var parts = text
            .Split("||", StringSplitOptions.None)
            .Select(static part => part.Trim())
            .Where(static part => part.Length > 0)
            .ToArray();

        if (parts.Length >= 2)
        {
            return (parts[0], parts[1]);
        }

        return (parts[0], parts[0]);
    }

    private static IReadOnlyDictionary<string, double> AnalyzeEtterna(BeatmapChart rawChart, string? cvtFlag, double speedRate, string version)
    {
        var chart = rawChart.ApplyConversion(cvtFlag);
        return EtternaRuntime.Analyze(chart, speedRate, version);
    }

    private static double ResolveOverallDifficulty(double originalOd, string? odFlag)
    {
        if (string.IsNullOrWhiteSpace(odFlag))
        {
            return originalOd;
        }

        if (string.Equals(odFlag, "HR", StringComparison.OrdinalIgnoreCase))
        {
            return 6.462 + (0.715 * originalOd);
        }

        if (string.Equals(odFlag, "EZ", StringComparison.OrdinalIgnoreCase))
        {
            return -20.761 + (2.566 * originalOd);
        }

        if (double.TryParse(odFlag, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        throw new InvalidOperationException($"Invalid odFlag: {odFlag}");
    }

    private static string ModeTagFromLnRatio(double lnRatio)
    {
        if (!double.IsFinite(lnRatio))
        {
            return "Mix";
        }

        if (lnRatio <= 0.15)
        {
            return "RC";
        }

        if (lnRatio >= 0.9)
        {
            return "LN";
        }

        return "Mix";
    }

    private static bool DetectVibro(IReadOnlyDictionary<string, double>? values, double threshold)
    {
        if (values == null)
        {
            return false;
        }

        var overall = TryGetMsdValue(values, "Overall");
        var jackSpeed = TryGetMsdValue(values, "JackSpeed");
        if (!overall.HasValue || overall.Value <= 0 || !jackSpeed.HasValue)
        {
            return false;
        }

        return (jackSpeed.Value / overall.Value) >= threshold;
    }

    private static double? TryGetMsdValue(IReadOnlyDictionary<string, double>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out var value) || !double.IsFinite(value))
        {
            return null;
        }

        return value;
    }

    private static string ResolveShortCardSrMode(string modeTag)
    {
        return string.Equals(modeTag, "RC", StringComparison.Ordinal) ? "MSD" : "ReworkSR";
    }

    private static (double Value, string DisplayValue, string Unit) ResolveLeftCapsule(
        string mode,
        EstimatorResult estimatorResult,
        IReadOnlyDictionary<string, double>? etternaValues)
    {
        if (string.Equals(mode, "MSD", StringComparison.Ordinal))
        {
            var overall = TryGetMsdValue(etternaValues, "Overall");
            if (overall.HasValue)
            {
                return (overall.Value, overall.Value.ToString("0.00", CultureInfo.InvariantCulture), "MSD");
            }

            return (
                estimatorResult.Star,
                estimatorResult.Star.ToString("0.00", CultureInfo.InvariantCulture),
                "SR");
        }

        return (
            estimatorResult.Star,
            estimatorResult.Star.ToString("0.00", CultureInfo.InvariantCulture),
            "SR");
    }

    private static string FormatDiffForDisplay(string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
        {
            return "-";
        }

        return string.Join(
            '\n',
            diffText
                .Split("||", StringSplitOptions.None)
                .Select(static part => part.Trim()));
    }

    private static string FormatMetadataStatus(BeatmapMetadata metadata)
    {
        var artist = string.IsNullOrWhiteSpace(metadata.Artist) ? "Unknown Artist" : metadata.Artist;
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? "Unknown Title" : metadata.Title;
        var version = string.IsNullOrWhiteSpace(metadata.Version) ? "Unknown Difficulty" : metadata.Version;
        var creator = string.IsNullOrWhiteSpace(metadata.Creator) ? "Unknown Mapper" : metadata.Creator;
        return $"{artist} - {title} [{version}] // {creator}";
    }

    private static string BuildDifficultyCaption(
        string modeTag,
        double? numericDifficulty,
        string? numericDifficultyHint,
        bool forceHideNumericDifficulty)
    {
        const string baseCaption = "Estimate Difficulty";
        if (!DefaultEnableNumericDifficulty || forceHideNumericDifficulty)
        {
            return baseCaption;
        }

        static string FormatRcCaptionValue(string modeTagValue, string rawValue)
        {
            var text = rawValue.Trim();
            if (text.Length == 0)
            {
                return text;
            }

            if (string.Equals(modeTagValue, "RC", StringComparison.Ordinal))
            {
                return text;
            }

            return text.StartsWith("RC", StringComparison.OrdinalIgnoreCase) ? text : $"RC{text}";
        }

        if (numericDifficulty.HasValue && double.IsFinite(numericDifficulty.Value))
        {
            var valueText = FormatRcCaptionValue(modeTag, numericDifficulty.Value.ToString("0.00", CultureInfo.InvariantCulture));
            return $"{baseCaption}({valueText})";
        }

        if (!string.IsNullOrWhiteSpace(numericDifficultyHint))
        {
            var valueText = FormatRcCaptionValue(modeTag, numericDifficultyHint);
            return $"{baseCaption}({valueText})";
        }

        return baseCaption;
    }

    private static EstimatorResult CloneEstimatorResult(
        EstimatorResult source,
        double? star = null,
        double? lnRatio = null,
        int? columnCount = null,
        string? difficulty = null,
        double? numericDifficulty = null,
        string? numericDifficultyHint = null,
        MixedCompanellaPlan? mixedCompanellaPlan = null,
        bool overrideNumericDifficulty = false,
        bool overrideNumericDifficultyHint = false,
        bool overrideMixedCompanellaPlan = false)
    {
        return new EstimatorResult
        {
            Star = star ?? source.Star,
            LnRatio = lnRatio ?? source.LnRatio,
            ColumnCount = columnCount ?? source.ColumnCount,
            Difficulty = difficulty ?? source.Difficulty,
            NumericDifficulty = overrideNumericDifficulty ? numericDifficulty : source.NumericDifficulty,
            NumericDifficultyHint = overrideNumericDifficultyHint ? numericDifficultyHint : source.NumericDifficultyHint,
            MixedCompanellaPlan = overrideMixedCompanellaPlan ? mixedCompanellaPlan : source.MixedCompanellaPlan,
        };
    }

    private sealed record NormalizedSettings(double SpeedRate, string? OdFlag, string? CvtFlag);
}
