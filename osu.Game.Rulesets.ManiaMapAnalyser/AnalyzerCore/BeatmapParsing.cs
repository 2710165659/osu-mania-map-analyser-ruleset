using System.Globalization;

namespace OsuManiaMapAnalyser.Core;

internal sealed class BeatmapChart
{
    public string Status { get; init; } = "Init";

    public int ColumnCount { get; init; }

    public double OverallDifficulty { get; set; }

    public BeatmapMetadata Metadata { get; init; } = new();

    public List<TimingPointData> TimingPoints { get; init; } = new();

    public List<HitObjectData> HitObjects { get; init; } = new();

    public List<(int Start, int End)> Breaks { get; init; } = new();

    public double LnRatio => HitObjects.Count == 0
        ? 0.0
        : HitObjects.Count(static o => o.IsHold) / (double)HitObjects.Count;

    public BeatmapChart Clone()
    {
        return new BeatmapChart
        {
            Status = Status,
            ColumnCount = ColumnCount,
            OverallDifficulty = OverallDifficulty,
            Metadata = Metadata.Clone(),
            TimingPoints = TimingPoints.Select(static item => item.Clone()).ToList(),
            HitObjects = HitObjects.Select(static item => item.Clone()).ToList(),
            Breaks = Breaks.ToList(),
        };
    }

    public double GetBeatLengthAt(double timeMs)
    {
        if (TimingPoints.Count == 0)
        {
            return 500.0;
        }

        var lo = 0;
        var hi = TimingPoints.Count;
        var target = (int)Math.Truncate(timeMs);

        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (TimingPoints[mid].Time <= target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        var index = lo - 1;
        if (index < 0)
        {
            return TimingPoints[0].BeatLength;
        }

        return TimingPoints[index].BeatLength;
    }

    public BeatmapChart ApplyConversion(string? cvtFlag)
    {
        var normalized = string.IsNullOrWhiteSpace(cvtFlag)
            ? string.Empty
            : cvtFlag.Trim().ToUpperInvariant();

        if (normalized.Length == 0)
        {
            return Clone();
        }

        var chart = Clone();
        if (normalized.Contains("IN", StringComparison.Ordinal))
        {
            chart.ApplyInMod();
        }

        if (normalized.Contains("HO", StringComparison.Ordinal))
        {
            chart.ApplyHoMod();
        }

        return chart;
    }

    private void ApplyInMod()
    {
        var startsByColumn = new Dictionary<int, List<int>>();
        foreach (var hitObject in HitObjects)
        {
            if (!startsByColumn.TryGetValue(hitObject.Column, out var list))
            {
                list = new List<int>();
                startsByColumn[hitObject.Column] = list;
            }

            list.Add(hitObject.StartTime);
        }

        var newObjects = new List<HitObjectData>();
        foreach (var pair in startsByColumn)
        {
            var column = pair.Key;
            var locations = pair.Value;
            locations.Sort();

            for (var i = 0; i < locations.Count - 1; i += 1)
            {
                var startTime = locations[i];
                var nextTime = locations[i + 1];
                var duration = (double)(nextTime - startTime);
                var beatLength = GetBeatLengthAt(nextTime);
                duration = Math.Max(duration / 2.0, duration - (beatLength / 4.0));
                var endTime = startTime + duration;

                newObjects.Add(new HitObjectData
                {
                    Column = column,
                    StartTime = startTime,
                    EndTime = (int)Math.Round(endTime),
                    TypeFlags = 128,
                });
            }
        }

        newObjects.Sort(static (a, b) =>
        {
            var time = a.StartTime.CompareTo(b.StartTime);
            return time != 0 ? time : a.Column.CompareTo(b.Column);
        });

        HitObjects.Clear();
        HitObjects.AddRange(newObjects);
        Breaks.Clear();
    }

    private void ApplyHoMod()
    {
        foreach (var hitObject in HitObjects)
        {
            if (!hitObject.IsHold)
            {
                continue;
            }

            hitObject.TypeFlags = 1;
            hitObject.EndTime = hitObject.StartTime;
        }
    }
}

internal sealed class BeatmapMetadata
{
    public string Title { get; set; } = string.Empty;

    public string TitleUnicode { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string ArtistUnicode { get; set; } = string.Empty;

    public string Creator { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public string BeatmapId { get; set; } = string.Empty;

    public string BeatmapSetId { get; set; } = string.Empty;

    public BeatmapMetadata Clone()
    {
        return new BeatmapMetadata
        {
            Title = Title,
            TitleUnicode = TitleUnicode,
            Artist = Artist,
            ArtistUnicode = ArtistUnicode,
            Creator = Creator,
            Version = Version,
            Tags = Tags,
            BeatmapId = BeatmapId,
            BeatmapSetId = BeatmapSetId,
        };
    }
}

internal sealed class TimingPointData
{
    public int Time { get; init; }

    public double BeatLength { get; init; }

    public TimingPointData Clone()
    {
        return new TimingPointData
        {
            Time = Time,
            BeatLength = BeatLength,
        };
    }
}

internal sealed class HitObjectData
{
    public int Column { get; set; }

    public int StartTime { get; set; }

    public int EndTime { get; set; }

    public int TypeFlags { get; set; }

    public bool IsHold => (TypeFlags & 128) != 0 && EndTime > StartTime;

    public HitObjectData Clone()
    {
        return new HitObjectData
        {
            Column = Column,
            StartTime = StartTime,
            EndTime = EndTime,
            TypeFlags = TypeFlags,
        };
    }
}

internal static class BeatmapParser
{
    public static BeatmapChart Parse(string osuText)
    {
        if (string.IsNullOrWhiteSpace(osuText))
        {
            throw new InvalidOperationException("Input JSON must provide beatmap.osuText.");
        }

        var lines = osuText.Split(["\r\n", "\n"], StringSplitOptions.None);
        var metadata = new BeatmapMetadata();
        var timingPoints = new List<TimingPointData>();
        var hitObjects = new List<HitObjectData>();
        var breaks = new List<(int Start, int End)>();

        var overallDifficulty = -1.0;
        var columnCount = -1;
        var gameMode = string.Empty;
        var status = "Init";

        var inMetadataSection = false;
        var inEventsSection = false;
        var inTimingSection = false;

        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line == "[Metadata]")
            {
                inMetadataSection = true;
                inEventsSection = false;
                inTimingSection = false;
                continue;
            }

            if (line == "[Events]")
            {
                inMetadataSection = false;
                inEventsSection = true;
                inTimingSection = false;
                continue;
            }

            if (line == "[TimingPoints]")
            {
                inMetadataSection = false;
                inEventsSection = false;
                inTimingSection = true;
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inMetadataSection = false;
                inEventsSection = false;
                inTimingSection = false;
            }

            if (inMetadataSection)
            {
                ParseMetadataLine(metadata, line);
            }

            if (inEventsSection)
            {
                ParseEventLine(breaks, line);
            }

            if (inTimingSection)
            {
                ParseTimingPointLine(timingPoints, line);
            }

            if (line.StartsWith("OverallDifficulty:", StringComparison.Ordinal))
            {
                var value = line["OverallDifficulty:".Length..].Trim();
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    overallDifficulty = parsed;
                }
            }

            if (line.StartsWith("CircleSize:", StringComparison.Ordinal))
            {
                var value = line["CircleSize:".Length..].Trim();
                if (value == "0")
                {
                    columnCount = 10;
                }
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    columnCount = (int)Math.Truncate(parsed);
                }
            }

            if (line.StartsWith("Mode:", StringComparison.Ordinal))
            {
                gameMode = line["Mode:".Length..].Trim();
                if (!string.Equals(gameMode, "3", StringComparison.Ordinal))
                {
                    status = "NotMania";
                }
            }

            if (line == "[HitObjects]")
            {
                for (var j = i + 1; j < lines.Length; j += 1)
                {
                    var objectLine = lines[j].Trim();
                    if (objectLine.Length == 0)
                    {
                        continue;
                    }

                    if (!TryParseHitObject(hitObjects, columnCount, objectLine))
                    {
                        status = "Fail";
                    }
                }

                break;
            }
        }

        if (timingPoints.Count == 0)
        {
            timingPoints.Add(new TimingPointData
            {
                Time = 0,
                BeatLength = 500.0,
            });
        }

        timingPoints.Sort(static (a, b) => a.Time.CompareTo(b.Time));

        if (status != "Fail" && status != "NotMania")
        {
            status = "OK";
        }

        return new BeatmapChart
        {
            Status = status,
            ColumnCount = columnCount,
            OverallDifficulty = overallDifficulty,
            Metadata = metadata,
            TimingPoints = timingPoints,
            HitObjects = hitObjects,
            Breaks = breaks,
        };
    }

    private static void ParseMetadataLine(BeatmapMetadata metadata, string line)
    {
        var splitIndex = line.IndexOf(':');
        if (splitIndex < 0)
        {
            return;
        }

        var key = line[..splitIndex].Trim();
        var value = line[(splitIndex + 1)..].Trim();

        switch (key)
        {
            case "Title":
                metadata.Title = value;
                break;
            case "TitleUnicode":
                metadata.TitleUnicode = value;
                break;
            case "Artist":
                metadata.Artist = value;
                break;
            case "ArtistUnicode":
                metadata.ArtistUnicode = value;
                break;
            case "Creator":
                metadata.Creator = value;
                break;
            case "Version":
                metadata.Version = value;
                break;
            case "Tags":
                metadata.Tags = value;
                break;
            case "BeatmapID":
                metadata.BeatmapId = value;
                break;
            case "BeatmapSetID":
                metadata.BeatmapSetId = value;
                break;
        }
    }

    private static void ParseEventLine(List<(int Start, int End)> breaks, string line)
    {
        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        var parts = line.Split(',').Select(static item => item.Trim()).ToArray();
        if (parts.Length < 3)
        {
            return;
        }

        if (!string.Equals(parts[0], "2", StringComparison.Ordinal)
            && !string.Equals(parts[0], "Break", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var breakStart)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var breakEnd))
        {
            return;
        }

        if (breakEnd > breakStart)
        {
            breaks.Add((breakStart, breakEnd));
        }
    }

    private static void ParseTimingPointLine(List<TimingPointData> timingPoints, string line)
    {
        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        var parts = line.Split(',').Select(static item => item.Trim()).ToArray();
        if (parts.Length < 2)
        {
            return;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rawTime)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var beatLength))
        {
            return;
        }

        var uninherited = 1;
        if (parts.Length > 6 && parts[6].Length > 0)
        {
            _ = int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uninherited);
        }

        if (uninherited == 1 && beatLength > 0)
        {
            timingPoints.Add(new TimingPointData
            {
                Time = (int)Math.Truncate(rawTime),
                BeatLength = beatLength,
            });
        }
    }

    private static bool TryParseHitObject(List<HitObjectData> hitObjects, int columnCount, string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 5)
        {
            return true;
        }

        try
        {
            var x = (int)Math.Truncate(double.Parse(parts[0], CultureInfo.InvariantCulture));
            var noteStart = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var noteType = int.Parse(parts[3], CultureInfo.InvariantCulture);

            var column = 0;
            if (columnCount > 0)
            {
                column = (int)Math.Truncate((x * columnCount) / 512.0);
                column = Math.Min(columnCount - 1, Math.Max(0, column));
            }

            var noteEnd = noteStart;
            if ((noteType & 128) != 0 && parts.Length >= 6)
            {
                var tailParts = parts[5].Split(':');
                noteEnd = int.Parse(tailParts[0], CultureInfo.InvariantCulture);
            }

            hitObjects.Add(new HitObjectData
            {
                Column = column,
                StartTime = noteStart,
                EndTime = noteEnd,
                TypeFlags = noteType,
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}
