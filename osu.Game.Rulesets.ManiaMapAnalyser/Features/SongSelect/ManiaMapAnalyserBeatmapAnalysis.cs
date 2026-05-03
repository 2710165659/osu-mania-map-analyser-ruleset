using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsuManiaMapAnalyser.Core;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Features.SongSelect;

internal static class ManiaMapAnalyserBeatmapAnalysis
{
    public static AnalysisSnapshot? TryCreate(WorkingBeatmap? workingBeatmap, IReadOnlyList<Mod> mods)
    {
        if (workingBeatmap == null)
            return null;

        string? beatmapPath = workingBeatmap.BeatmapInfo.Path;
        if (string.IsNullOrWhiteSpace(beatmapPath))
            return null;

        string? storagePath = workingBeatmap.BeatmapSetInfo.GetPathForFile(beatmapPath);
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        return new AnalysisSnapshot(
            workingBeatmap,
            storagePath,
            resolveSpeedRate(mods),
            resolveOdFlag(mods),
            resolveCvtFlag(mods));
    }

    public static string? AnalyzeDifficultyText(AnalysisSnapshot snapshot)
    {
        using Stream? stream = snapshot.WorkingBeatmap.GetStream(snapshot.StoragePath);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        string osuText = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(osuText))
            return null;

        string inputJson = JsonSerializer.Serialize(new
        {
            beatmap = new
            {
                osuText,
            },
            settings = new
            {
                speedRate = snapshot.SpeedRate,
                odFlag = snapshot.OdFlag,
                cvtFlag = snapshot.CvtFlag,
            },
        });

        string outputJson = BeatmapAnalyzer.AnalyzeJsonToJson(inputJson, indented: false);
        AnalyzeResponse? output = JsonSerializer.Deserialize<AnalyzeResponse>(outputJson);
        return normalizeSingleLine(output?.Card.Difficulty.RawText);
    }

    private static double resolveSpeedRate(IReadOnlyList<Mod> mods)
    {
        double rate = ModUtils.CalculateRateWithMods(mods);
        return double.IsFinite(rate) && rate > 0 ? rate : 1;
    }

    private static object? resolveOdFlag(IReadOnlyList<Mod> mods)
    {
        if (mods.OfType<ModDifficultyAdjust>().FirstOrDefault() is { } difficultyAdjust
            && !difficultyAdjust.OverallDifficulty.IsDefault
            && difficultyAdjust.OverallDifficulty.Value is float overriddenOd)
        {
            return Math.Round(overriddenOd, 1);
        }

        if (hasMod(mods, "HR"))
            return "HR";

        if (hasMod(mods, "EZ"))
            return "EZ";

        return null;
    }

    private static string? resolveCvtFlag(IReadOnlyList<Mod> mods)
    {
        List<string> flags = new();

        if (hasMod(mods, "IN"))
            flags.Add("IN");

        if (hasMod(mods, "HO"))
            flags.Add("HO");

        return flags.Count == 0 ? null : string.Concat(flags);
    }

    private static bool hasMod(IReadOnlyList<Mod> mods, string acronym)
        => mods.Any(m => string.Equals(m.Acronym, acronym, StringComparison.OrdinalIgnoreCase));

    private static string? normalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string text = value.ReplaceLineEndings(" ")
                           .Trim();

        return text.Length == 0 ? null : text;
    }

    internal sealed record AnalysisSnapshot(
        WorkingBeatmap WorkingBeatmap,
        string StoragePath,
        double SpeedRate,
        object? OdFlag,
        string? CvtFlag);
}
