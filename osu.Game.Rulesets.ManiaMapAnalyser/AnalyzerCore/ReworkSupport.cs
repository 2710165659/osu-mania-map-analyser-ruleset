using System.Globalization;
using System.Text.RegularExpressions;

namespace OsuManiaMapAnalyser.Core;

internal static class ReworkSupport
{
    private static readonly Lazy<Dictionary<string, List<IntervalEntry>>> IntervalMap = new(LoadIntervals);

    private static readonly (string Name, double Mean)[] DanielMeans =
    [
        ("Alpha", 6.562),
        ("Beta", 6.957),
        ("Gamma", 7.459),
        ("Delta", 7.939),
        ("Epsilon", 9.095),
        ("Zeta", 9.473),
        ("Eta", 10.162),
        ("Theta", 10.782),
    ];

    private static readonly (double Lower, double Upper)[] DanielBoundaries = BuildDanielBoundaries();

    public static string EstimateDifficulty(double star, double lnRatio, int columnCount)
    {
        return columnCount switch
        {
            4 => ComposeReworkDifficulty(star, lnRatio, "RC_intervals_4K", "LN_intervals_4K", "< Intro 1 low", "> Theta high", "< LN 5 mid", "> LN 17 high"),
            6 => ComposeReworkDifficulty(star, lnRatio, "RC_intervals_6K", "LN_intervals_6K", "< Regular 0 low", "> Regular 9 high", "< LN 0 low", "> LN Finish high"),
            7 => ComposeReworkDifficulty(star, lnRatio, "RC_intervals_7K", "LN_intervals_7K", "< Regular 0 low", "> Regular Stellium high", "< LN 3 low", "> LN Stellium high"),
            _ => "Unknown difficulty",
        };
    }

    public static (string Label, double? Numeric) EstimateDanielDan(double star)
    {
        if (!double.IsFinite(star))
        {
            return ("Unknown", null);
        }

        if (star < DanielBoundaries[0].Lower)
        {
            return ($"< {DanielMeans[0].Name} Low", null);
        }

        if (star >= DanielBoundaries[^1].Upper)
        {
            return ($"> {DanielMeans[^1].Name} High", null);
        }

        for (var i = 0; i < DanielMeans.Length; i += 1)
        {
            var (lower, upper) = DanielBoundaries[i];
            if (star < lower || star >= upper)
            {
                continue;
            }

            var tRaw = (star - lower) / (upper - lower);
            var t = Math.Max(0, Math.Min(tRaw, 1));
            var numeric = Math.Round(11 + i + t, 2);

            var label = t switch
            {
                < 1.0 / 3.0 => $"{DanielMeans[i].Name} Low",
                < 2.0 / 3.0 => $"{DanielMeans[i].Name} Mid",
                _ => $"{DanielMeans[i].Name} High",
            };

            label = i switch
            {
                5 => $"Emik {label}",
                6 => $"Thaumiel {label}",
                7 => $"CloverWisp {label}",
                _ => label,
            };

            return (label, numeric);
        }

        return ("Unknown", null);
    }

    public static string ComposeDifficultyFromRcLn(string rcLabel, string lnLabel, double lnRatio)
    {
        var rc = (rcLabel ?? string.Empty).Trim();
        var ln = (lnLabel ?? string.Empty).Trim();
        if (!double.IsFinite(lnRatio) || lnRatio < 0.15)
        {
            return rc.Length > 0 ? rc : (ln.Length > 0 ? ln : "-");
        }

        if (rc.Length == 0)
        {
            return ln.Length > 0 ? ln : "-";
        }

        if (ln.Length == 0)
        {
            return rc;
        }

        return $"{rc} || {ln}";
    }

    public static bool IsDanielTooLowDifficulty(string value)
    {
        return Regex.IsMatch(value ?? string.Empty, @"^<\s*alpha\b", RegexOptions.IgnoreCase);
    }

    private static string ComposeReworkDifficulty(double star, double lnRatio, string rcKey, string lnKey, string lowRc, string highRc, string lowLn, string highLn)
    {
        var rcDifficulty = FindIntervalLabel(rcKey, star);
        if (rcDifficulty == null)
        {
            rcDifficulty = star < IntervalMap.Value[rcKey][0].Lower ? lowRc : highRc;
        }

        if (lnRatio < 0.15)
        {
            return rcDifficulty;
        }

        var lnDifficulty = FindIntervalLabel(lnKey, star);
        if (lnDifficulty == null)
        {
            lnDifficulty = star < IntervalMap.Value[lnKey][0].Lower ? lowLn : highLn;
        }

        return $"{rcDifficulty} || {lnDifficulty}";
    }

    private static string? FindIntervalLabel(string section, double star)
    {
        foreach (var interval in IntervalMap.Value[section])
        {
            if (interval.Lower <= star && star <= interval.Upper)
            {
                return interval.Label;
            }
        }

        return null;
    }

    private static (double Lower, double Upper)[] BuildDanielBoundaries()
    {
        var boundaries = new (double Lower, double Upper)[DanielMeans.Length];
        for (var i = 0; i < DanielMeans.Length; i += 1)
        {
            var mean = DanielMeans[i].Mean;
            var lower = i > 0
                ? (DanielMeans[i - 1].Mean + mean) / 2.0
                : mean - ((((DanielMeans[1].Mean + mean) / 2.0) - mean));
            var upper = i < DanielMeans.Length - 1
                ? (mean + DanielMeans[i + 1].Mean) / 2.0
                : mean + ((mean - DanielMeans[i - 1].Mean) / 2.0);
            boundaries[i] = (lower, upper);
        }

        return boundaries;
    }

    private static Dictionary<string, List<IntervalEntry>> LoadIntervals()
    {
        var result = new Dictionary<string, List<IntervalEntry>>(StringComparer.Ordinal);
        var sectionRegex = new Regex(@"^(?<name>[A-Za-z0-9_]+)\s*:\s*\[$", RegexOptions.Singleline);
        var entryRegex = new Regex(@"^\[\s*(?<lower>-?\d+(?:\.\d+)?)\s*,\s*(?<upper>-?\d+(?:\.\d+)?)\s*,\s*""(?<label>[^""]+)""\s*\],?$", RegexOptions.Singleline);

        string? currentSection = null;
        List<IntervalEntry>? currentEntries = null;

        using StreamReader reader = AnalyzerResources.OpenTextReader("intervals.js");

        while (reader.ReadLine() is string rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (currentSection == null)
            {
                var sectionMatch = sectionRegex.Match(line);
                if (!sectionMatch.Success)
                {
                    continue;
                }

                currentSection = sectionMatch.Groups["name"].Value;
                currentEntries = new List<IntervalEntry>();
                continue;
            }

            if (line.StartsWith(']'))
            {
                if (currentEntries is { Count: > 0 })
                {
                    result[currentSection] = currentEntries;
                }

                currentSection = null;
                currentEntries = null;
                continue;
            }

            var entryMatch = entryRegex.Match(line);
            if (!entryMatch.Success || currentEntries == null)
            {
                continue;
            }

            currentEntries.Add(new IntervalEntry(
                double.Parse(entryMatch.Groups["lower"].Value, CultureInfo.InvariantCulture),
                double.Parse(entryMatch.Groups["upper"].Value, CultureInfo.InvariantCulture),
                entryMatch.Groups["label"].Value));
        }

        return result;
    }

    private sealed record IntervalEntry(double Lower, double Upper, string Label);
}
