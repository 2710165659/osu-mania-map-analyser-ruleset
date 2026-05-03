namespace OsuManiaMapAnalyser.Core;

internal static class AzusaEstimator
{
    private const double RcLnRatioLimit = 0.18;
    private const int MinNotes = 80;
    private const double RowToleranceMs = 2.0;
    private static readonly double[] DecayWindowsMs = [140, 280, 560, 980];
    private static readonly double[] DecayWeights = [0.34, 0.30, 0.22, 0.14];

    private static readonly string[] GreekByIndex =
    [
        "Alpha",
        "Beta",
        "Gamma",
        "Delta",
        "Epsilon",
        "Emik Zeta",
        "Thaumiel Eta",
        "CloverWisp Theta",
        "Iota",
        "Kappa",
    ];

    private static readonly (string Suffix, double Offset)[] RcTierCandidates =
    [
        ("low", -0.4),
        ("mid/low", -0.2),
        ("mid", 0.0),
        ("mid/high", 0.2),
        ("high", 0.4),
    ];

    private static readonly (double Lower, double Upper, double Value)[] AzusaCalibrationLowBlocks =
    [
        (1.9220, 1.9220, 1.0000),
        (2.3660, 2.7684, 1.6667),
        (2.8394, 2.8394, 2.0000),
        (2.8584, 3.7162, 2.3333),
        (3.7798, 3.7798, 3.0000),
        (3.8667, 3.8667, 3.0000),
        (4.2067, 5.2039, 4.3333),
        (5.2506, 5.7713, 5.0667),
        (5.8603, 6.1512, 5.3333),
        (6.3292, 6.8785, 6.0000),
        (7.1715, 7.3617, 6.2000),
        (7.4079, 7.8734, 7.2000),
        (8.0160, 8.4003, 8.2500),
        (8.4133, 8.4133, 9.0000),
        (8.9031, 9.4775, 9.5667),
        (9.6488, 9.6488, 10.0000),
        (9.8301, 9.8301, 10.3000),
    ];

    private static readonly (double Lower, double Upper, double Value)[] AzusaCalibrationHighBlocks =
    [
        (11.4336, 11.4336, 10.4000),
        (11.4436, 11.4436, 10.5000),
        (11.6012, 11.6665, 10.6500),
        (11.6696, 12.2317, 11.5000),
        (12.3295, 12.3919, 11.7500),
        (12.5238, 12.5238, 12.0000),
        (12.5318, 12.8329, 12.1400),
        (12.8605, 12.9781, 12.2800),
        (12.9868, 13.1170, 12.7800),
        (13.2003, 13.4418, 12.7857),
        (13.4660, 13.5829, 12.9250),
        (13.6044, 13.9924, 13.3667),
        (14.0583, 14.0583, 13.4000),
        (14.0795, 14.2266, 13.4600),
        (14.2346, 14.2346, 13.6000),
        (14.2414, 14.2414, 13.7000),
        (14.2903, 14.2903, 14.0000),
        (14.3258, 14.4760, 14.1200),
        (14.5365, 14.6006, 14.1333),
        (14.7269, 14.8716, 14.1333),
        (15.0048, 15.0048, 14.4000),
        (15.0521, 15.0521, 14.4000),
        (15.0521, 15.0521, 14.4000),
        (15.0950, 15.0950, 14.4000),
        (15.2335, 15.2335, 14.4000),
        (15.2388, 15.5821, 14.7385),
        (15.6977, 15.7002, 14.8500),
        (15.7535, 16.1593, 15.0667),
        (16.2009, 16.2958, 15.1000),
        (16.3172, 16.4748, 15.7600),
        (16.5620, 16.9083, 15.9833),
        (16.9485, 16.9485, 16.0000),
        (17.0216, 17.3799, 16.1000),
        (17.4616, 17.4616, 16.4000),
        (17.5167, 17.5167, 16.4000),
        (17.5306, 17.9077, 16.6400),
        (18.1973, 18.1973, 17.2000),
        (18.2026, 18.2026, 17.2000),
        (18.4562, 19.3477, 17.9500),
    ];

    private static readonly (double X, double Y)[] AzusaIsotonicPoints =
    [
        (1.2900, 1),
        (1.2900, 1),
        (1.3900, 1),
        (1.3900, 1),
        (1.4700, 1),
        (1.4700, 1),
        (1.9000, 2),
        (1.9000, 2),
        (2.0600, 2),
        (2.2200, 2),
        (2.3200, 2),
        (2.3200, 2),
        (2.5100, 3),
        (2.5100, 3),
        (2.9000, 3.3333333333333335),
        (2.9800, 3.3333333333333335),
        (4.0100, 4),
        (4.0100, 4),
        (4.5100, 4),
        (4.5100, 4),
        (4.8300, 4.2),
        (4.8300, 4.2),
        (4.9400, 5),
        (4.9400, 5),
        (5.0400, 5),
        (5.0400, 5),
        (5.2000, 5),
        (5.2000, 5),
        (5.2800, 5),
        (5.2800, 5),
        (5.3300, 5.666666666666667),
        (5.5900, 5.666666666666667),
        (5.7700, 6),
        (5.7700, 6),
        (5.8700, 6),
        (5.8700, 6),
        (5.8700, 6),
        (5.8700, 6),
        (6.0700, 6.6),
        (6.0700, 6.6),
        (6.3300, 6.733333333333333),
        (6.9200, 6.733333333333333),
        (7.1100, 7),
        (7.1100, 7),
        (7.4600, 8.3),
        (8.0500, 8.3),
        (8.2500, 8.333333333333334),
        (8.4800, 8.333333333333334),
        (9.3200, 9.183333333333334),
        (9.6200, 9.183333333333334),
        (9.6400, 9.5),
        (9.7100, 9.5),
        (9.9800, 10.325),
        (10.1500, 10.325),
        (10.3000, 10.37142857142857),
        (10.9900, 10.37142857142857),
        (11.0000, 10.9),
        (11.0400, 10.9),
        (11.0700, 11.22857142857143),
        (11.3600, 11.22857142857143),
        (11.4500, 11.866666666666667),
        (11.7400, 11.866666666666667),
        (11.9300, 12.0875),
        (12.2000, 12.0875),
        (12.2900, 12.466666666666667),
        (12.5200, 12.466666666666667),
        (12.5600, 12.5),
        (12.6400, 12.5),
        (12.7400, 12.56),
        (12.9200, 12.56),
        (12.9800, 12.6),
        (12.9800, 12.6),
        (12.9900, 12.7),
        (12.9900, 12.7),
        (13.0000, 13),
        (13.0000, 13),
        (13.0400, 13.266666666666667),
        (13.2800, 13.266666666666667),
        (13.2900, 13.533333333333333),
        (13.3300, 13.533333333333333),
        (13.3400, 13.55),
        (13.3600, 13.55),
        (13.4000, 13.62),
        (13.5600, 13.62),
        (13.7200, 13.8),
        (13.7200, 13.8),
        (13.9500, 14),
        (13.9500, 14),
        (14.0200, 14),
        (14.0200, 14),
        (14.0500, 14.05),
        (14.2000, 14.05),
        (14.2100, 14.199999999999998),
        (14.3400, 14.199999999999998),
        (14.3700, 14.266666666666666),
        (14.3700, 14.266666666666666),
        (14.4400, 14.4),
        (14.4400, 14.4),
        (14.4400, 14.4),
        (14.4400, 14.4),
        (14.4700, 14.5),
        (14.4700, 14.5),
        (14.5200, 14.674999999999999),
        (14.6700, 14.674999999999999),
        (14.8000, 14.825),
        (14.9000, 14.825),
        (14.9300, 15),
        (15.1500, 15),
        (15.3100, 15.2),
        (15.3500, 15.2),
        (15.3700, 15.666666666666666),
        (15.5300, 15.666666666666666),
        (15.5400, 15.675),
        (15.7200, 15.675),
        (15.7200, 15.8),
        (15.7200, 15.8),
        (15.7500, 15.9),
        (15.7500, 15.9),
        (15.7800, 16),
        (16.0700, 16),
        (16.0900, 16.266666666666666),
        (16.1500, 16.266666666666666),
        (16.3500, 16.4),
        (16.3500, 16.4),
        (16.3500, 16.4),
        (16.3500, 16.4),
        (16.4100, 16.4),
        (16.5100, 16.4),
        (16.5300, 16.533333333333335),
        (16.6500, 16.533333333333335),
        (17.5500, 17.2),
        (17.5500, 17.2),
        (17.6800, 17.2),
        (17.6800, 17.2),
        (17.9100, 17.95),
        (18.0200, 17.95),
    ];

    public static EstimatorResult Estimate(
        BeatmapChart rawChart,
        double speedRate,
        bool forceSunnyReferenceHo,
        EstimatorResult? precomputedDanielResult,
        EstimatorResult? precomputedSunnyResult,
        Func<EstimatorResult> danielFactory,
        Func<string?, EstimatorResult> sunnyFactory)
    {
        var effectiveSpeedRate = double.IsFinite(speedRate) && speedRate > 0 ? speedRate : 1.0;
        var lnRatio = rawChart.LnRatio;
        var columnCount = rawChart.ColumnCount;

        if (rawChart.Status == "Fail")
        {
            return BuildErrorResult("ParseFailed", "Beatmap parse failed", lnRatio, columnCount);
        }

        if (rawChart.Status == "NotMania")
        {
            return BuildErrorResult("NotMania", "Beatmap mode is not mania", lnRatio, columnCount);
        }

        if (columnCount != 4)
        {
            return BuildErrorResult("UnsupportedKeys", "Azusa only supports 4K", lnRatio, columnCount);
        }

        var taps = BuildTapNotes(rawChart);
        if (taps.Count < MinNotes)
        {
            return BuildErrorResult(
                "TooShort",
                $"Insufficient notes for stable estimate ({taps.Count})",
                lnRatio,
                columnCount);
        }

        var timeScale = effectiveSpeedRate != 0 ? 1.0 / effectiveSpeedRate : 1.0;
        var scaledTaps = Math.Abs(timeScale - 1.0) < 1e-12
            ? taps.Select(static note => note.Clone()).ToList()
            : taps.Select(note => note.WithTime(note.Time * timeScale)).ToList();

        AnnotateRows(scaledTaps, RowToleranceMs * timeScale);

        var curve = BuildDifficultyCurve(scaledTaps);
        var primaryNumeric = ComputeAzusaNumericFromCurve(curve, taps.Count);

        var maxColumn = curve.ColumnCounts.Max();
        var anchorImbalance = SafeDiv((maxColumn / Math.Max(taps.Count, 1.0)) - 0.25, 0.75, 0.0);
        var chordRate = SafeDiv(curve.ChordNoteCount, Math.Max(taps.Count, 1.0), 0.0);
        var jackSorted = curve.JackRawSeries.OrderBy(static value => value).ToArray();
        var jackQ95 = QuantileFromSorted(jackSorted, 0.95);

        var danielResult = precomputedDanielResult;
        double? danielNumeric = null;
        var danielHasNativeNumeric = false;
        try
        {
            danielResult ??= danielFactory();
            danielNumeric = EstimateDanielNumeric(danielResult);
            danielHasNativeNumeric = HasDanielNativeNumeric(danielResult);
        }
        catch
        {
            danielResult = null;
            danielNumeric = null;
            danielHasNativeNumeric = false;
        }

        var sunnyResult = precomputedSunnyResult;
        double? sunnyNumeric = null;
        try
        {
            sunnyResult ??= sunnyFactory(forceSunnyReferenceHo ? "HO" : null);
            sunnyNumeric = EstimateSunnyNumeric(sunnyResult);
        }
        catch
        {
            sunnyResult = null;
            sunnyNumeric = null;
        }

        var danielNumericForBlend = danielNumeric;
        if (!danielHasNativeNumeric && danielNumericForBlend.HasValue)
        {
            var highSignal = new[]
            {
                double.IsFinite(primaryNumeric) ? primaryNumeric : double.NegativeInfinity,
                sunnyNumeric ?? double.NegativeInfinity,
                danielNumericForBlend.Value,
            }.Max();

            if (highSignal < 14.0)
            {
                var speedDelta = effectiveSpeedRate - 1.0;
                var fallbackScale = speedDelta < 0
                    ? Math.Clamp((-speedDelta) * 0.43, 0.0, 1.0)
                    : Math.Clamp(speedDelta * 0.35, 0.0, 1.0);

                danielNumericForBlend = danielNumericForBlend.Value * fallbackScale;
            }
        }

        var blendDetails = ResolveRcBlendComponents(
            primaryNumeric,
            danielNumericForBlend,
            sunnyNumeric,
            anchorImbalance,
            chordRate,
            jackQ95);

        var numericDifficulty = blendDetails.Value;
        var calibratedNumeric = CalibrateAzusaNumeric(
            numericDifficulty,
            blendDetails.LowGate,
            blendDetails.HighGate);

        var curveGapResidual = ComputeCurveGapResidualCorrection(
            calibratedNumeric,
            blendDetails,
            anchorImbalance,
            chordRate,
            jackQ95,
            primaryNumeric,
            sunnyNumeric,
            danielNumericForBlend);

        var preOutputNumeric = Math.Clamp((calibratedNumeric ?? 0.0) + curveGapResidual, -2.0, 20.0);
        var outputNumeric = CalibrateAzusaOutputNumeric(preOutputNumeric);
        var postCurveGapResidual = ComputePostOutputCurveGapResidualCorrection(
            outputNumeric,
            blendDetails,
            anchorImbalance,
            chordRate,
            jackQ95,
            primaryNumeric,
            sunnyNumeric,
            danielNumericForBlend);

        var finalNumeric = Math.Clamp(outputNumeric + postCurveGapResidual, -2.0, 20.0);
        var estDiff = NumericToRcLabel(finalNumeric);

        return new EstimatorResult
        {
            Star = Math.Round(3.4 + (0.38 * finalNumeric), 4),
            LnRatio = lnRatio,
            ColumnCount = columnCount,
            Difficulty = estDiff,
            NumericDifficulty = Math.Round(finalNumeric, 2),
            NumericDifficultyHint = "azusa-rc-v1",
        };
    }

    private static EstimatorResult BuildErrorResult(string code, string message, double lnRatio, int columnCount)
    {
        return new EstimatorResult
        {
            Star = double.NaN,
            LnRatio = double.IsFinite(lnRatio) ? lnRatio : 0.0,
            ColumnCount = columnCount,
            Difficulty = $"Invalid: {message}",
            NumericDifficulty = null,
            NumericDifficultyHint = code,
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double SafeDiv(double a, double b, double fallback)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(b) < 1e-9)
        {
            return fallback;
        }

        return a / b;
    }

    private static string FormatRcBaseLabel(int baseValue)
    {
        if (baseValue <= 0)
        {
            var introLevel = Math.Clamp(baseValue + 3, 1, 3);
            return $"Intro {introLevel}";
        }

        if (baseValue <= 10)
        {
            return $"Reform {baseValue}";
        }

        var greekIndex = Math.Clamp(baseValue - 11, 0, GreekByIndex.Length - 1);
        return GreekByIndex[greekIndex];
    }

    private static string NumericToRcLabel(double numeric)
    {
        if (!double.IsFinite(numeric))
        {
            return "Invalid";
        }

        var clamped = Clamp(numeric, -2.4, 20.4);
        (int Base, string Suffix, double Distance)? bestMatch = null;

        for (var baseValue = -2; baseValue <= 20; baseValue += 1)
        {
            foreach (var (suffix, offset) in RcTierCandidates)
            {
                var centerValue = baseValue + offset;
                var distance = Math.Abs(clamped - centerValue);
                if (bestMatch == null || distance < bestMatch.Value.Distance)
                {
                    bestMatch = (baseValue, suffix, distance);
                }
            }
        }

        if (bestMatch == null)
        {
            return "Invalid";
        }

        return $"{FormatRcBaseLabel(bestMatch.Value.Base)} {bestMatch.Value.Suffix}";
    }

    private static double? EstimateDanielNumeric(EstimatorResult? result)
    {
        if (result?.NumericDifficulty is { } numericDifficulty && double.IsFinite(numericDifficulty))
        {
            return numericDifficulty;
        }

        var star = result?.Star ?? double.NaN;
        if (!double.IsFinite(star))
        {
            return null;
        }

        if (star >= 6.56)
        {
            var normalized = Clamp((star - 6.56) / 0.58, 0.0, 9.99);
            return Math.Round(11 + normalized, 2);
        }

        var lowPart = -2 + (13 * Math.Pow(Clamp(star / 6.56, 0.0, 1.0), 1.72));
        return Math.Round(lowPart, 2);
    }

    private static bool HasDanielNativeNumeric(EstimatorResult? result)
    {
        return result?.NumericDifficulty is { } value && double.IsFinite(value);
    }

    private static double? EstimateSunnyNumeric(EstimatorResult? result)
    {
        var star = result?.Star ?? double.NaN;
        if (!double.IsFinite(star))
        {
            return null;
        }

        return Math.Round(Clamp(2.85 + (1.33 * star), -2.0, 20.0), 2);
    }

    private static double QuantileFromSorted(IReadOnlyList<double> sortedValues, double q)
    {
        if (sortedValues.Count == 0)
        {
            return 0.0;
        }

        var t = Clamp(q, 0.0, 1.0) * (sortedValues.Count - 1);
        var left = (int)Math.Floor(t);
        var right = Math.Min(sortedValues.Count - 1, left + 1);
        var weight = t - left;
        return (sortedValues[left] * (1 - weight)) + (sortedValues[right] * weight);
    }

    private static double PowerMean(IEnumerable<double> values, double p)
    {
        var source = values.ToArray();
        if (source.Length == 0)
        {
            return 0.0;
        }

        double acc = 0;
        foreach (var value in source)
        {
            acc += Math.Pow(Math.Max(value, 0.0), p);
        }

        return Math.Pow(acc / source.Length, 1.0 / p);
    }

    private static List<TapNote> BuildTapNotes(BeatmapChart chart)
    {
        return chart.HitObjects
            .Select(hitObject => new TapNote
            {
                Time = hitObject.StartTime,
                Column = hitObject.Column,
                Hand = hitObject.Column < 2 ? 0 : 1,
                RowSize = 1,
            })
            .Where(static note => note.Column >= 0 && note.Column < 4)
            .OrderBy(static note => note.Time)
            .ThenBy(static note => note.Column)
            .ToList();
    }

    private static void AnnotateRows(IReadOnlyList<TapNote> taps, double toleranceMs)
    {
        if (taps.Count == 0)
        {
            return;
        }

        var rowStart = 0;
        for (var i = 1; i <= taps.Count; i += 1)
        {
            var shouldFlush = i == taps.Count || Math.Abs(taps[i].Time - taps[rowStart].Time) > toleranceMs;
            if (!shouldFlush)
            {
                continue;
            }

            var rowSize = i - rowStart;
            for (var j = rowStart; j < i; j += 1)
            {
                taps[j].RowSize = rowSize;
            }

            rowStart = i;
        }
    }

    private static double ExpDecayFactor(double dtMs, double tauMs)
    {
        if (!double.IsFinite(dtMs) || dtMs <= 0)
        {
            return 1.0;
        }

        return Math.Exp(-dtMs / tauMs);
    }

    private static double SkillFromStates(IReadOnlyList<double> states)
    {
        double sum = 0;
        for (var i = 0; i < states.Count; i += 1)
        {
            sum += states[i] * DecayWeights[i];
        }

        return sum;
    }

    private static CurveResult BuildDifficultyCurve(IReadOnlyList<TapNote> taps)
    {
        var speedStates = new double[DecayWindowsMs.Length];
        var staminaStates = new double[DecayWindowsMs.Length];
        var chordStates = new double[DecayWindowsMs.Length];
        var techStates = new double[DecayWindowsMs.Length];

        var lastByColumn = new[] { -1e9, -1e9, -1e9, -1e9 };
        var lastByHand = new[] { -1e9, -1e9 };

        var density250 = new List<double>(taps.Count);
        var density500 = new List<double>(taps.Count);
        var jackRawSeries = new List<double>(taps.Count);
        var columnCounts = new[] { 0, 0, 0, 0 };

        var local = new List<double>(taps.Count);
        var speedSeries = new List<double>(taps.Count);
        var staminaSeries = new List<double>(taps.Count);
        var chordSeries = new List<double>(taps.Count);
        var techSeries = new List<double>(taps.Count);

        var chordNoteCount = 0;
        var cursor250 = 0;
        var cursor500 = 0;

        var prevTime = taps[0].Time;
        var prevAny1 = -1e9;
        var prevAny2 = -1e9;
        var prevCol = 0;

        for (var i = 0; i < taps.Count; i += 1)
        {
            var note = taps[i];
            var t = note.Time;
            var c = note.Column;
            columnCounts[c] += 1;
            if (note.RowSize >= 2)
            {
                chordNoteCount += 1;
            }

            var dtGlobal = i == 0 ? 0.0 : Math.Max(0.0, t - prevTime);
            var dtSame = Math.Max(0.0, t - lastByColumn[c]);
            var dtHand = Math.Max(0.0, t - lastByHand[note.Hand]);
            var dtAny = Math.Max(0.0, t - prevAny1);

            while (cursor250 < i && t - taps[cursor250].Time > 250)
            {
                cursor250 += 1;
            }

            while (cursor500 < i && t - taps[cursor500].Time > 500)
            {
                cursor500 += 1;
            }

            var d250 = ((i - cursor250) + 1) / 0.25;
            var d500 = ((i - cursor500) + 1) / 0.5;
            density250.Add(d250);
            density500.Add(d500);

            var jack = Math.Pow(190 / (dtSame + 35), 1.16);
            jackRawSeries.Add(jack);
            var stream = Math.Pow(170 / (dtAny + 30), 1.07);
            var handStream = Math.Pow(185 / (dtHand + 42), 1.08);

            var movement = Math.Abs(c - prevCol) / 3.0;
            var rhythmRatio = SafeDiv(Math.Max(dtAny, 1.0), Math.Max(t - prevAny2, 1.0), 1.0);
            var rhythmChaos = Math.Abs(Math.Log2(Clamp(rhythmRatio, 0.2, 5.0)));

            var rowChord = Math.Max(0, note.RowSize - 1);
            var chord = Math.Pow(rowChord + 1, 1.22) - 1;

            var speedInput = (0.54 * stream) + (0.28 * handStream) + (0.18 * jack);
            var staminaInput = (0.48 * (d500 / 11)) + (0.27 * (d250 / 15)) + (0.25 * stream);
            var chordInput = chord * (1 + (0.22 * Math.Min(1.5, stream)));
            var techInput = (0.45 * rhythmChaos)
                + (0.30 * movement)
                + (0.25 * (rowChord > 0 ? 1 + (0.3 * rowChord) : 0));

            for (var j = 0; j < DecayWindowsMs.Length; j += 1)
            {
                var tau = DecayWindowsMs[j];
                var decay = ExpDecayFactor(dtGlobal, tau);
                speedStates[j] = (speedStates[j] * decay) + speedInput;
                staminaStates[j] = (staminaStates[j] * decay) + staminaInput;
                chordStates[j] = (chordStates[j] * decay) + chordInput;
                techStates[j] = (techStates[j] * decay) + techInput;
            }

            var speedSkill = SkillFromStates(speedStates);
            var staminaSkill = SkillFromStates(staminaStates);
            var chordSkill = SkillFromStates(chordStates);
            var techSkill = SkillFromStates(techStates);

            const double p = 2.15;
            var combined = Math.Pow(
                (
                    (0.38 * Math.Pow(Math.Max(speedSkill, 0.0), p))
                    + (0.26 * Math.Pow(Math.Max(staminaSkill, 0.0), p))
                    + (0.18 * Math.Pow(Math.Max(chordSkill, 0.0), p))
                    + (0.18 * Math.Pow(Math.Max(techSkill, 0.0), p))
                ) / (0.38 + 0.26 + 0.18 + 0.18),
                1.0 / p);

            local.Add(combined);
            speedSeries.Add(speedSkill);
            staminaSeries.Add(staminaSkill);
            chordSeries.Add(chordSkill);
            techSeries.Add(techSkill);

            prevAny2 = prevAny1;
            prevAny1 = t;
            prevTime = t;
            prevCol = c;
            lastByColumn[c] = t;
            lastByHand[note.Hand] = t;
        }

        return new CurveResult(
            local.ToArray(),
            speedSeries.ToArray(),
            staminaSeries.ToArray(),
            chordSeries.ToArray(),
            techSeries.ToArray(),
            density250.ToArray(),
            density500.ToArray(),
            jackRawSeries.ToArray(),
            columnCounts,
            chordNoteCount);
    }

    private static double ComputeAzusaNumericFromCurve(CurveResult curve, int noteCount)
    {
        if (curve.Local.Length == 0)
        {
            return 0.0;
        }

        static CurveSummary Summarize(IReadOnlyList<double> values)
        {
            var sorted = values.OrderBy(static value => value).ToArray();
            var q97 = QuantileFromSorted(sorted, 0.97);
            var q94 = QuantileFromSorted(sorted, 0.94);
            var q90 = QuantileFromSorted(sorted, 0.90);
            var q75 = QuantileFromSorted(sorted, 0.75);
            var q50 = QuantileFromSorted(sorted, 0.50);
            var tailCount = Math.Max(8, (int)Math.Floor(sorted.Length * 0.04));
            var tailSlice = sorted[^tailCount..];
            var tailMean = tailSlice.Average();
            var pm = PowerMean(values, 2.6);
            return new CurveSummary(q97, q94, q90, q75, q50, tailMean, pm);
        }

        var speed = Summarize(curve.SpeedSeries);
        var stamina = Summarize(curve.StaminaSeries);
        var chord = Summarize(curve.ChordSeries);
        var tech = Summarize(curve.TechSeries);

        var density250 = PowerMean(curve.Density250, 1.18);
        var density500 = PowerMean(curve.Density500, 1.12);
        var lengthBoost = Math.Log(1 + (noteCount / 140.0));

        var peakBlend =
            (0.26 * speed.Q97)
            + (0.24 * stamina.Q97)
            + (0.18 * chord.Q97)
            + (0.12 * tech.Q97)
            + (0.07 * speed.Q90)
            + (0.05 * stamina.Q90)
            + (0.03 * chord.Q90)
            + (0.02 * tech.Q90);

        var sustainBlend =
            (0.20 * speed.Q75)
            + (0.18 * stamina.Q75)
            + (0.11 * chord.Q75)
            + (0.08 * tech.Q75)
            + (0.12 * speed.TailMean)
            + (0.10 * stamina.TailMean)
            + (0.06 * chord.TailMean)
            + (0.05 * tech.TailMean);

        var densityBlend = (0.14 * Math.Log(1 + density250)) + (0.22 * Math.Log(1 + density500));
        var midBlend = (0.18 * speed.Q50) + (0.15 * stamina.Q50) + (0.10 * chord.Q50) + (0.08 * tech.Q50);

        var raw = (0.58 * peakBlend) + (0.24 * sustainBlend) + (0.10 * densityBlend) + (0.08 * midBlend) + (0.06 * lengthBoost);
        var scaled = 0.82 + (0.41 * raw);

        var maxColumn = curve.ColumnCounts.Max();
        var anchorImbalance = SafeDiv((maxColumn / Math.Max(noteCount, 1.0)) - 0.25, 0.75, 0.0);
        var chordRate = SafeDiv(curve.ChordNoteCount, Math.Max(noteCount, 1.0), 0.0);
        var jackSorted = curve.JackRawSeries.OrderBy(static value => value).ToArray();
        var jackQ95 = QuantileFromSorted(jackSorted, 0.95);

        var jackAnchorBoost = Clamp(
            1.65
            * Math.Max(0.0, anchorImbalance)
            * Math.Max(0.0, 1 - (1.85 * chordRate))
            * Math.Max(0.0, jackQ95 - 2.2),
            0.0,
            2.2);

        var lowJackBoost = Clamp(
            1.1
            * Clamp((12.2 - scaled) / 4.5, 0.0, 1.0)
            * Math.Max(0.0, anchorImbalance - 0.08)
            * Math.Max(0.0, jackQ95 - 1.7)
            * (0.9 + (0.6 * Math.Max(0.0, 0.22 - chordRate))),
            0.0,
            1.35);

        return Clamp(scaled + jackAnchorBoost + lowJackBoost, -2.0, 20.0);
    }

    private static BlendDetails ResolveRcBlendComponents(
        double? primaryNumeric,
        double? danielNumeric,
        double? sunnyNumeric,
        double? anchorImbalance,
        double? chordRate,
        double? jackQ95)
    {
        var primary = primaryNumeric.HasValue && double.IsFinite(primaryNumeric.Value) ? primaryNumeric : null;
        var daniel = danielNumeric.HasValue && double.IsFinite(danielNumeric.Value) ? danielNumeric : null;
        var sunny = sunnyNumeric.HasValue && double.IsFinite(sunnyNumeric.Value) ? sunnyNumeric : null;

        if (daniel == null && primary == null && sunny == null)
        {
            return new BlendDetails(null, null, null, null, null, null);
        }

        var lowGateSource = daniel ?? sunny ?? primary ?? 0.0;
        var lowGate = Clamp((9.61 - lowGateSource) / 4.94, 0.0, 1.0);
        var highGate = 1 - lowGate;

        double? lowBase = null;
        if (sunny != null)
        {
            var value = -8.317 + (1.536 * sunny.Value);
            if (primary != null)
            {
                value += 0.011 * primary.Value;
            }

            if (daniel != null)
            {
                value += 0.049 * daniel.Value;
            }

            if (lowGate > 0)
            {
                var primaryPart = primary != null ? Math.Max(0.0, primary.Value - 10.4) : 0.0;
                var sunnyPart = Math.Max(0.0, sunny.Value - 9.84);
                var lowSunnyConvex = Math.Pow(Math.Max(0.0, 7.935 - sunny.Value), 2);
                value += lowGate * ((0.442 * sunnyPart) + (0.016 * primaryPart) + (0.235 * lowSunnyConvex));
            }

            lowBase = value;
        }

        double? highBase = null;
        var dUse = daniel ?? sunny ?? primary;
        if (dUse != null)
        {
            var primaryUse = primary ?? dUse.Value;
            var sunnyUse = sunny ?? dUse.Value;
            var value = (0.809 * dUse.Value) + (0.057 * primaryUse) + (0.165 * sunnyUse) + 0.183;

            var highMask = Clamp((lowGateSource - 14.83) / 2.667, 0.0, 1.0);
            if (highMask > 0)
            {
                value += highMask
                    * ((-0.154 * Math.Max(0.0, primaryUse - dUse.Value))
                    + (0.081 * Math.Max(0.0, sunnyUse - dUse.Value)));
            }

            if (anchorImbalance.HasValue && chordRate.HasValue && jackQ95.HasValue)
            {
                var anchorLift = Clamp(
                    0.96
                    * Math.Max(0.0, jackQ95.Value - 2.08)
                    * Math.Max(0.0, 0.24 - chordRate.Value)
                    * Math.Max(0.0, anchorImbalance.Value - 0.10),
                    0.0,
                    0.88);

                value += anchorLift;
            }

            highBase = value;
        }

        var lowLift = double.IsFinite(lowGateSource)
            ? Math.Max(0.0, 9.889 - lowGateSource) * 0.257
            : 0.0;

        if (lowBase == null && highBase == null)
        {
            return new BlendDetails(null, lowGateSource, lowGate, highGate, lowBase, highBase);
        }

        if (lowBase == null)
        {
            return new BlendDetails(highBase, lowGateSource, lowGate, highGate, lowBase, highBase);
        }

        if (highBase == null)
        {
            return new BlendDetails(lowBase + lowLift, lowGateSource, lowGate, highGate, lowBase, highBase);
        }

        return new BlendDetails(
            (lowBase.Value * lowGate) + ((highBase.Value + lowLift) * highGate),
            lowGateSource,
            lowGate,
            highGate,
            lowBase,
            highBase);
    }

    private static double InterpolateCalibration(double value, IReadOnlyList<(double X, double Y)> knots)
    {
        if (!double.IsFinite(value) || knots.Count < 2)
        {
            return value;
        }

        if (value <= knots[0].X)
        {
            return knots[0].Y;
        }

        var last = knots.Count - 1;
        if (value >= knots[last].X)
        {
            return knots[last].Y;
        }

        for (var i = 0; i < last; i += 1)
        {
            var (x0, y0) = knots[i];
            var (x1, y1) = knots[i + 1];
            if (value >= x0 && value <= x1)
            {
                return y0 + SafeDiv((value - x0) * (y1 - y0), x1 - x0, 0.0);
            }
        }

        return value;
    }

    private static double InterpolateCalibrationBlocks(double value, IReadOnlyList<(double Lower, double Upper, double Result)> blocks)
    {
        if (!double.IsFinite(value) || blocks.Count == 0)
        {
            return value;
        }

        if (value <= blocks[0].Lower)
        {
            return blocks[0].Result;
        }

        for (var i = 0; i < blocks.Count; i += 1)
        {
            var (x0, x1, y) = blocks[i];
            if (value >= x0 && value <= x1)
            {
                return y;
            }

            if (i >= blocks.Count - 1)
            {
                continue;
            }

            var next = blocks[i + 1];
            if (value > x1 && value < next.Lower)
            {
                var t = SafeDiv(value - x1, next.Lower - x1, 0.0);
                return (y * (1 - t)) + (next.Result * t);
            }
        }

        return blocks[^1].Result;
    }

    private static double? CalibrateAzusaNumeric(double? value, double? lowGate, double? highGate)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return value;
        }

        var low = InterpolateCalibrationBlocks(value.Value, AzusaCalibrationLowBlocks);
        var high = InterpolateCalibrationBlocks(value.Value, AzusaCalibrationHighBlocks);

        var lg = lowGate.HasValue && double.IsFinite(lowGate.Value) ? Clamp(lowGate.Value, 0.0, 1.0) : (double?)null;
        var hg = highGate.HasValue && double.IsFinite(highGate.Value) ? Clamp(highGate.Value, 0.0, 1.0) : (double?)null;

        if (lg == null && hg == null)
        {
            return value.Value < 11 ? low : high;
        }

        var lowWeight = lg ?? Math.Max(0.0, 1 - (hg ?? 0.0));
        var highWeight = hg ?? Math.Max(0.0, 1 - lowWeight);
        var weightSum = lowWeight + highWeight;
        if (weightSum <= 1e-6)
        {
            return value.Value < 11 ? low : high;
        }

        return ((lowWeight * low) + (highWeight * high)) / weightSum;
    }

    private static double CalibrateAzusaOutputNumeric(double value)
    {
        if (!double.IsFinite(value))
        {
            return value;
        }

        return InterpolateCalibration(value, AzusaIsotonicPoints);
    }

    private static double ComputeCurveGapResidualCorrection(
        double? baseNumeric,
        BlendDetails blendDetails,
        double? anchorImbalance,
        double? chordRate,
        double? jackQ95,
        double? primaryNumeric,
        double? sunnyNumeric,
        double? danielNumeric)
    {
        var x = baseNumeric ?? double.NaN;
        if (!double.IsFinite(x))
        {
            return 0.0;
        }

        var highGate = blendDetails.HighGate.HasValue && double.IsFinite(blendDetails.HighGate.Value)
            ? Clamp(blendDetails.HighGate.Value, 0.0, 1.0)
            : 0.0;

        var primary = primaryNumeric.HasValue && double.IsFinite(primaryNumeric.Value) ? primaryNumeric.Value : x;
        var sunny = sunnyNumeric.HasValue && double.IsFinite(sunnyNumeric.Value) ? sunnyNumeric.Value : x;
        var daniel = danielNumeric.HasValue && double.IsFinite(danielNumeric.Value) ? danielNumeric.Value : x;
        var ds = daniel - sunny;
        var sp = sunny - primary;
        var anchor = anchorImbalance.HasValue && double.IsFinite(anchorImbalance.Value) ? anchorImbalance.Value : 0.0;
        var chord = chordRate.HasValue && double.IsFinite(chordRate.Value) ? chordRate.Value : 0.0;
        var jack = jackQ95.HasValue && double.IsFinite(jackQ95.Value) ? jackQ95.Value : 0.0;

        var residual =
            4.335282
            + (-0.170459 * x)
            + (-1.622303 * Math.Max(0.0, 11 - x))
            + (1.328125 * Math.Max(0.0, 12.5 - x))
            + (-0.042829 * Math.Max(0.0, 14 - x))
            + (-0.834997 * highGate)
            + (3.060352 * highGate * Math.Max(0.0, 11 - x))
            + (-1.744638 * highGate * Math.Max(0.0, 12.5 - x))
            + (0.409922 * ds)
            + (0.041072 * sp)
            + (-0.388231 * highGate * ds)
            + (-0.170185 * highGate * sp)
            + (3.466868 * anchor)
            + (-1.743778 * chord)
            + (-0.094758 * jack)
            + (2.626366 * anchor * jack)
            + (1.836357 * chord * jack)
            + (-2.612648 * highGate * anchor)
            + (-2.493596 * highGate * chord);

        return Clamp(residual, -1.2, 1.2);
    }

    private static double ComputePostOutputCurveGapResidualCorrection(
        double baseNumeric,
        BlendDetails blendDetails,
        double? anchorImbalance,
        double? chordRate,
        double? jackQ95,
        double? primaryNumeric,
        double? sunnyNumeric,
        double? danielNumeric)
    {
        if (!double.IsFinite(baseNumeric))
        {
            return 0.0;
        }

        var highGate = blendDetails.HighGate.HasValue && double.IsFinite(blendDetails.HighGate.Value)
            ? Clamp(blendDetails.HighGate.Value, 0.0, 1.0)
            : 0.0;

        var primary = primaryNumeric.HasValue && double.IsFinite(primaryNumeric.Value) ? primaryNumeric.Value : baseNumeric;
        var sunny = sunnyNumeric.HasValue && double.IsFinite(sunnyNumeric.Value) ? sunnyNumeric.Value : baseNumeric;
        var daniel = danielNumeric.HasValue && double.IsFinite(danielNumeric.Value) ? danielNumeric.Value : baseNumeric;
        var anchor = anchorImbalance.HasValue && double.IsFinite(anchorImbalance.Value) ? anchorImbalance.Value : 0.0;
        var chord = chordRate.HasValue && double.IsFinite(chordRate.Value) ? chordRate.Value : 0.0;
        var jack = jackQ95.HasValue && double.IsFinite(jackQ95.Value) ? jackQ95.Value : baseNumeric;

        var ds = daniel - sunny;
        var sp = sunny - primary;

        var residual = 0.4 * (
            0.979895
            + (0.053556 * baseNumeric)
            + (-1.050405 * Math.Max(0.0, 11 - baseNumeric))
            + (0.942552 * Math.Max(0.0, 12.5 - baseNumeric))
            + (0.048841 * Math.Max(0.0, 14 - baseNumeric))
            + (-1.636218 * highGate)
            + (0.956025 * highGate * Math.Max(0.0, 11 - baseNumeric))
            + (-0.975188 * highGate * Math.Max(0.0, 12.5 - baseNumeric))
            + (0.195107 * ds)
            + (-0.064291 * sp)
            + (-0.231542 * highGate * ds)
            + (0.082201 * highGate * sp)
            + (-0.634013 * anchor)
            + (-0.490303 * chord)
            + (-0.135176 * jack)
            + (-0.992539 * anchor * jack)
            + (-0.164219 * chord * jack)
            + (-1.027392 * highGate * anchor)
            + (0.961530 * highGate * chord));

        return Clamp(residual, -1.0, 1.0);
    }

    private sealed class TapNote
    {
        public double Time { get; init; }

        public int Column { get; init; }

        public int Hand { get; init; }

        public int RowSize { get; set; }

        public TapNote Clone()
        {
            return new TapNote
            {
                Time = Time,
                Column = Column,
                Hand = Hand,
                RowSize = RowSize,
            };
        }

        public TapNote WithTime(double time)
        {
            return new TapNote
            {
                Time = time,
                Column = Column,
                Hand = Hand,
                RowSize = RowSize,
            };
        }
    }

    private sealed record CurveResult(
        double[] Local,
        double[] SpeedSeries,
        double[] StaminaSeries,
        double[] ChordSeries,
        double[] TechSeries,
        double[] Density250,
        double[] Density500,
        double[] JackRawSeries,
        int[] ColumnCounts,
        int ChordNoteCount);

    private sealed record CurveSummary(
        double Q97,
        double Q94,
        double Q90,
        double Q75,
        double Q50,
        double TailMean,
        double PowerMean);

    private sealed record BlendDetails(
        double? Value,
        double? LowGateSource,
        double? LowGate,
        double? HighGate,
        double? LowBase,
        double? HighBase);
}
