using System.Collections.ObjectModel;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OsuManiaMapAnalyser.Core;

internal sealed class EstimatorResult
{
    public double Star { get; init; }

    public double LnRatio { get; init; }

    public int ColumnCount { get; init; }

    public string Difficulty { get; init; } = "-";

    public double? NumericDifficulty { get; init; }

    public string? NumericDifficultyHint { get; init; }

    public MixedCompanellaPlan? MixedCompanellaPlan { get; init; }
}

internal sealed class MixedCompanellaPlan
{
    public double LnRatio { get; init; }

    public string LnDifficulty { get; init; } = "-";
}

internal sealed class CompanellaResult
{
    public string Difficulty { get; init; } = "-";

    public double NumericDifficulty { get; init; }

    public string? NumericDifficultyHint { get; init; }
}

internal static class SunnyCalculator
{
    public static double Calculate(BeatmapChart chart, double rate, double? overallDifficultyOverride = null)
    {
        if (chart.HitObjects.Count == 0)
        {
            return -1.0;
        }

        var noteSeq = new List<(int Column, int Head, int Tail)>(chart.HitObjects.Count);
        var timeScale = rate != 0 ? 1.0 / rate : 1.0;

        foreach (var hitObject in chart.HitObjects)
        {
            var head = (int)Math.Floor(hitObject.StartTime * timeScale);
            var tail = hitObject.IsHold ? (int)Math.Floor(hitObject.EndTime * timeScale) : -1;
            noteSeq.Add((hitObject.Column, head, tail));
        }

        var overallDifficulty = overallDifficultyOverride ?? chart.OverallDifficulty;
        var x = 0.3 * Math.Sqrt((64.5 - Math.Ceiling(overallDifficulty * 3.0)) / 500.0);
        x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);

        noteSeq = noteSeq.OrderBy(static item => item.Head).ThenBy(static item => item.Column).ToList();
        if (noteSeq.Count == 0)
        {
            return -1.0;
        }

        var keyCount = chart.ColumnCount;
        var maxTail = noteSeq.Max(static item => item.Tail);
        var totalTime = Math.Max(noteSeq.Max(static item => item.Head), maxTail) + 1;

        var noteSeqByColumn = noteSeq
            .GroupBy(static item => item.Column)
            .OrderBy(static group => group.Key)
            .Select(static group => group.ToList())
            .ToList();

        var longNotes = noteSeq.Where(static item => item.Tail >= 0).ToList();
        var tailSeq = longNotes.OrderBy(static item => item.Tail).ToList();

        var corners = GetSunnyCorners(totalTime, noteSeq);
        var keyUsage = GetSunnyKeyUsage(keyCount, totalTime, noteSeq, corners.BaseCorners);

        var activeColumns = new List<List<int>>(corners.BaseCorners.Length);
        for (var i = 0; i < corners.BaseCorners.Length; i += 1)
        {
            var columns = new List<int>();
            for (var column = 0; column < keyCount; column += 1)
            {
                if (keyUsage[column][i])
                {
                    columns.Add(column);
                }
            }

            activeColumns.Add(columns);
        }

        var keyUsage400 = GetSunnyKeyUsage400(keyCount, totalTime, noteSeq, corners.BaseCorners);
        var anchor = ComputeSunnyAnchor(keyCount, keyUsage400, corners.BaseCorners);
        var (deltaKs, jbar) = ComputeSunnyJbar(keyCount, x, noteSeqByColumn, corners.BaseCorners);
        jbar = InterpValues(corners.AllCorners, corners.BaseCorners, jbar);

        var xbar = ComputeSunnyXbar(keyCount, x, noteSeqByColumn, activeColumns, corners.BaseCorners);
        xbar = InterpValues(corners.AllCorners, corners.BaseCorners, xbar);

        var lnRep = BuildSunnyLnRepresentation(longNotes, totalTime);
        var pbar = ComputeSunnyPbar(x, noteSeq, lnRep, anchor, corners.BaseCorners);
        pbar = InterpValues(corners.AllCorners, corners.BaseCorners, pbar);

        var abar = ComputeSunnyAbar(keyCount, activeColumns, deltaKs, corners.ACorners, corners.BaseCorners);
        abar = InterpValues(corners.AllCorners, corners.ACorners, abar);

        var rbar = ComputeSunnyRbar(x, noteSeqByColumn, tailSeq, corners.BaseCorners);
        rbar = InterpValues(corners.AllCorners, corners.BaseCorners, rbar);

        var (cStep, ksStep) = ComputeSunnyCAndKs(keyCount, noteSeq, keyUsage, corners.BaseCorners);
        var cAll = StepInterp(corners.AllCorners, corners.BaseCorners, cStep);
        var ksAll = StepInterp(corners.AllCorners, corners.BaseCorners, ksStep);

        var dAll = new double[corners.AllCorners.Length];
        for (var i = 0; i < corners.AllCorners.Length; i += 1)
        {
            var term1 = Math.Pow(abar[i], 3.0 / ksAll[i]) * Math.Min(jbar[i], 8 + (0.85 * jbar[i]));
            var term2 = Math.Pow(abar[i], 2.0 / 3.0) * ((0.8 * pbar[i]) + (rbar[i] * 35.0 / (cAll[i] + 8)));
            var sAll = Math.Pow((0.4 * Math.Pow(term1, 1.5)) + (0.6 * Math.Pow(term2, 1.5)), 2.0 / 3.0);
            var tAll = Math.Pow(abar[i], 3.0 / ksAll[i]) * xbar[i] / (xbar[i] + sAll + 1);
            dAll[i] = (2.7 * Math.Pow(sAll, 0.5) * Math.Pow(tAll, 1.5)) + (sAll * 0.27);
        }

        var gaps = new double[corners.AllCorners.Length];
        gaps[0] = (corners.AllCorners[1] - corners.AllCorners[0]) / 2.0;
        gaps[^1] = (corners.AllCorners[^1] - corners.AllCorners[^2]) / 2.0;
        for (var i = 1; i < corners.AllCorners.Length - 1; i += 1)
        {
            gaps[i] = (corners.AllCorners[i + 1] - corners.AllCorners[i - 1]) / 2.0;
        }

        var effectiveWeights = new double[corners.AllCorners.Length];
        for (var i = 0; i < corners.AllCorners.Length; i += 1)
        {
            effectiveWeights[i] = cAll[i] * gaps[i];
        }

        var sortedIndices = Enumerable.Range(0, dAll.Length).OrderBy(i => dAll[i]).ToArray();
        var dSorted = sortedIndices.Select(i => dAll[i]).ToArray();
        var wSorted = sortedIndices.Select(i => effectiveWeights[i]).ToArray();

        var cumulativeWeights = new double[wSorted.Length];
        cumulativeWeights[0] = wSorted[0];
        for (var i = 1; i < wSorted.Length; i += 1)
        {
            cumulativeWeights[i] = cumulativeWeights[i - 1] + wSorted[i];
        }

        var totalWeight = cumulativeWeights[^1];
        var normalizedWeights = cumulativeWeights.Select(weight => weight / totalWeight).ToArray();
        var percentiles = new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 };
        var indices = new int[percentiles.Length];

        for (var i = 0; i < percentiles.Length; i += 1)
        {
            indices[i] = SearchSortedLeft(normalizedWeights, percentiles[i]);
            if (indices[i] >= dSorted.Length)
            {
                indices[i] = dSorted.Length - 1;
            }
        }

        var percentile93 = (dSorted[indices[0]] + dSorted[indices[1]] + dSorted[indices[2]] + dSorted[indices[3]]) / 4.0;
        var percentile83 = (dSorted[indices[4]] + dSorted[indices[5]] + dSorted[indices[6]] + dSorted[indices[7]]) / 4.0;

        double sumD5W = 0;
        double sumW = 0;
        for (var i = 0; i < dSorted.Length; i += 1)
        {
            sumD5W += Math.Pow(dSorted[i], 5) * wSorted[i];
            sumW += wSorted[i];
        }

        var weightedMean = Math.Pow(sumD5W / sumW, 0.2);
        var sr = (0.88 * percentile93 * 0.25) + (0.94 * percentile83 * 0.2) + (weightedMean * 0.55);

        double totalNotes = noteSeq.Count;
        foreach (var item in longNotes)
        {
            totalNotes += 0.5 * Math.Min(item.Tail - item.Head, 1000) / 200.0;
        }

        sr *= totalNotes / (totalNotes + 60.0);
        sr = RescaleHigh(sr);
        sr *= 0.975;

        return sr;
    }

    private static SunnyCorners GetSunnyCorners(int totalTime, List<(int Column, int Head, int Tail)> noteSeq)
    {
        var cornersBase = new HashSet<double>();
        foreach (var (_, head, tail) in noteSeq)
        {
            cornersBase.Add(head);
            if (tail >= 0)
            {
                cornersBase.Add(tail);
            }
        }

        var additional = new List<double>();
        foreach (var corner in cornersBase)
        {
            additional.Add(corner + 501);
            additional.Add(corner - 499);
            additional.Add(corner + 1);
        }

        foreach (var value in additional)
        {
            cornersBase.Add(value);
        }

        cornersBase.Add(0);
        cornersBase.Add(totalTime);
        var baseCorners = cornersBase.Where(value => value >= 0 && value <= totalTime).OrderBy(static value => value).ToArray();

        var cornersA = new HashSet<double>();
        foreach (var (_, head, tail) in noteSeq)
        {
            cornersA.Add(head);
            if (tail >= 0)
            {
                cornersA.Add(tail);
            }
        }

        additional.Clear();
        foreach (var corner in cornersA)
        {
            additional.Add(corner + 1000);
            additional.Add(corner - 1000);
        }

        foreach (var value in additional)
        {
            cornersA.Add(value);
        }

        cornersA.Add(0);
        cornersA.Add(totalTime);
        var aCorners = cornersA.Where(value => value >= 0 && value <= totalTime).OrderBy(static value => value).ToArray();
        var allCorners = baseCorners.Union(aCorners).OrderBy(static value => value).ToArray();

        return new SunnyCorners(allCorners, baseCorners, aCorners);
    }

    private static bool[][] GetSunnyKeyUsage(int keyCount, int totalTime, List<(int Column, int Head, int Tail)> noteSeq, double[] baseCorners)
    {
        var keyUsage = new bool[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            keyUsage[column] = new bool[baseCorners.Length];
        }

        foreach (var (column, head, tail) in noteSeq)
        {
            var startTime = Math.Max(head - 150, 0);
            var endTime = tail < 0 ? head + 150 : Math.Min(tail + 150, totalTime - 1);
            var leftIndex = SearchSortedLeft(baseCorners, startTime);
            var rightIndex = SearchSortedLeft(baseCorners, endTime);

            for (var index = leftIndex; index < rightIndex; index += 1)
            {
                keyUsage[column][index] = true;
            }
        }

        return keyUsage;
    }

    private static double[][] GetSunnyKeyUsage400(int keyCount, int totalTime, List<(int Column, int Head, int Tail)> noteSeq, double[] baseCorners)
    {
        var keyUsage400 = new double[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            keyUsage400[column] = new double[baseCorners.Length];
        }

        foreach (var (column, head, tail) in noteSeq)
        {
            double startTime = Math.Max(head, 0);
            double endTime = tail < 0 ? head : Math.Min(tail, totalTime - 1);
            var left400 = SearchSortedLeft(baseCorners, startTime - 400);
            var leftIndex = SearchSortedLeft(baseCorners, startTime);
            var rightIndex = SearchSortedLeft(baseCorners, endTime);
            var right400 = SearchSortedLeft(baseCorners, endTime + 400);

            for (var index = leftIndex; index < rightIndex; index += 1)
            {
                keyUsage400[column][index] += 3.75 + Math.Min(endTime - startTime, 1500) / 150.0;
            }

            for (var index = left400; index < leftIndex; index += 1)
            {
                var distance = baseCorners[index] - startTime;
                keyUsage400[column][index] += 3.75 - (3.75 / (400.0 * 400.0) * (distance * distance));
            }

            for (var index = rightIndex; index < right400; index += 1)
            {
                var distance = Math.Abs(baseCorners[index] - endTime);
                keyUsage400[column][index] += 3.75 - (3.75 / (400.0 * 400.0) * (distance * distance));
            }
        }

        return keyUsage400;
    }

    private static double[] ComputeSunnyAnchor(int keyCount, double[][] keyUsage400, double[] baseCorners)
    {
        var anchor = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var counts = new double[keyCount];
            for (var column = 0; column < keyCount; column += 1)
            {
                counts[column] = keyUsage400[column][index];
            }

            Array.Sort(counts);
            Array.Reverse(counts);
            var nonZero = counts.Where(static value => value != 0).ToArray();
            if (nonZero.Length > 1)
            {
                double walk = 0;
                double maxWalk = 0;
                for (var i = 0; i < nonZero.Length - 1; i += 1)
                {
                    var ratio = nonZero[i + 1] / nonZero[i];
                    walk += nonZero[i] * (1 - (4 * Math.Pow(0.5 - ratio, 2)));
                    maxWalk += nonZero[i];
                }

                anchor[index] = walk / maxWalk;
            }
        }

        for (var i = 0; i < anchor.Length; i += 1)
        {
            anchor[i] = 1 + Math.Min(anchor[i] - 0.18, 5 * Math.Pow(anchor[i] - 0.22, 3));
        }

        return anchor;
    }

    private static (double[][] DeltaKs, double[] Jbar) ComputeSunnyJbar(int keyCount, double x, List<List<(int Column, int Head, int Tail)>> noteSeqByColumn, double[] baseCorners)
    {
        var jKs = new double[keyCount][];
        var deltaKs = new double[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            jKs[column] = new double[baseCorners.Length];
            deltaKs[column] = Enumerable.Repeat(1e9, baseCorners.Length).ToArray();
        }

        static double JackNerfer(double delta) => 1 - (7e-5 * Math.Pow(0.15 + Math.Abs(delta - 0.08), -4));

        for (var column = 0; column < keyCount; column += 1)
        {
            if (column >= noteSeqByColumn.Count)
            {
                continue;
            }

            var notes = noteSeqByColumn[column];
            for (var i = 0; i < notes.Count - 1; i += 1)
            {
                var start = notes[i].Head;
                var end = notes[i + 1].Head;
                var leftIndex = SearchSortedLeft(baseCorners, start);
                var rightIndex = SearchSortedLeft(baseCorners, end);
                if (leftIndex >= rightIndex)
                {
                    continue;
                }

                var delta = 0.001 * (end - start);
                var value = Math.Pow(delta, -1) * Math.Pow(delta + (0.11 * Math.Pow(x, 0.25)), -1);
                var jackValue = value * JackNerfer(delta);

                for (var index = leftIndex; index < rightIndex; index += 1)
                {
                    jKs[column][index] = jackValue;
                    deltaKs[column][index] = delta;
                }
            }
        }

        var jbarKs = new double[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            jbarKs[column] = SmoothOnCorners(baseCorners, jKs[column], 500, 0.001, average: false);
        }

        var jbar = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            double numerator = 0;
            double denominator = 0;
            for (var column = 0; column < keyCount; column += 1)
            {
                var value = jbarKs[column][index];
                var weight = 1.0 / deltaKs[column][index];
                numerator += Math.Pow(Math.Max(value, 0), 5) * weight;
                denominator += weight;
            }

            jbar[index] = Math.Pow(numerator / Math.Max(1e-9, denominator), 0.2);
        }

        return (deltaKs, jbar);
    }

    private static double[] ComputeSunnyXbar(int keyCount, double x, List<List<(int Column, int Head, int Tail)>> noteSeqByColumn, List<List<int>> activeColumns, double[] baseCorners)
    {
        double[][] crossMatrix =
        [
            [-1.0],
            [0.075, 0.075],
            [0.125, 0.05, 0.125],
            [0.125, 0.125, 0.125, 0.125],
            [0.175, 0.25, 0.05, 0.25, 0.175],
            [0.175, 0.25, 0.175, 0.175, 0.25, 0.175],
            [0.225, 0.35, 0.25, 0.05, 0.25, 0.35, 0.225],
            [0.225, 0.35, 0.25, 0.225, 0.225, 0.25, 0.35, 0.225],
            [0.275, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.275],
            [0.275, 0.45, 0.35, 0.25, 0.275, 0.275, 0.25, 0.35, 0.45, 0.275],
            [0.325, 0.55, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.55, 0.325],
        ];

        var xKs = new double[keyCount + 1][];
        var fastCross = new double[keyCount + 1][];
        for (var column = 0; column <= keyCount; column += 1)
        {
            xKs[column] = new double[baseCorners.Length];
            fastCross[column] = new double[baseCorners.Length];
        }

        var crossCoeff = crossMatrix[keyCount];
        for (var column = 0; column <= keyCount; column += 1)
        {
            List<(int Column, int Head, int Tail)> notesInPair;
            if (column == 0)
            {
                notesInPair = noteSeqByColumn.Count > 0 ? noteSeqByColumn[0] : [];
            }
            else if (column == keyCount)
            {
                notesInPair = noteSeqByColumn.Count > 0 ? noteSeqByColumn[keyCount - 1] : [];
            }
            else
            {
                var left = column - 1 < noteSeqByColumn.Count ? noteSeqByColumn[column - 1] : [];
                var right = column < noteSeqByColumn.Count ? noteSeqByColumn[column] : [];
                notesInPair = left.Concat(right).OrderBy(static item => item.Head).ToList();
            }

            for (var i = 1; i < notesInPair.Count; i += 1)
            {
                var start = notesInPair[i - 1].Head;
                var end = notesInPair[i].Head;
                var indexStart = SearchSortedLeft(baseCorners, start);
                var indexEnd = SearchSortedLeft(baseCorners, end);
                if (indexStart >= indexEnd)
                {
                    continue;
                }

                var delta = 0.001 * (notesInPair[i].Head - notesInPair[i - 1].Head);
                var value = 0.16 * Math.Pow(Math.Max(x, delta), -2);

                var previousStartInactive = !activeColumns[indexStart].Contains(column - 1);
                var previousEndInactive = !activeColumns[indexEnd < activeColumns.Count ? indexEnd : activeColumns.Count - 1].Contains(column - 1);
                var currentStartInactive = !activeColumns[indexStart].Contains(column);
                var currentEndInactive = !activeColumns[indexEnd < activeColumns.Count ? indexEnd : activeColumns.Count - 1].Contains(column);

                if ((previousStartInactive && previousEndInactive) || (currentStartInactive && currentEndInactive))
                {
                    value *= 1 - crossCoeff[column];
                }

                var fastValue = Math.Max(0, (0.4 * Math.Pow(Math.Max(Math.Max(delta, 0.06), 0.75 * x), -2)) - 80);
                for (var index = indexStart; index < indexEnd; index += 1)
                {
                    xKs[column][index] = value;
                    fastCross[column][index] = fastValue;
                }
            }
        }

        var xBase = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            double sum1 = 0;
            for (var column = 0; column <= keyCount; column += 1)
            {
                sum1 += xKs[column][index] * crossCoeff[column];
            }

            double sum2 = 0;
            for (var column = 0; column < keyCount; column += 1)
            {
                sum2 += Math.Sqrt(fastCross[column][index] * crossCoeff[column] * fastCross[column + 1][index] * crossCoeff[column + 1]);
            }

            xBase[index] = sum1 + sum2;
        }

        return SmoothOnCorners(baseCorners, xBase, 500, 0.001, average: false);
    }

    private static SunnyLnRepresentation BuildSunnyLnRepresentation(List<(int Column, int Head, int Tail)> longNotes, int totalTime)
    {
        var diff = new Dictionary<double, double>();
        foreach (var (_, head, tail) in longNotes)
        {
            double t0 = Math.Min(head + 60, tail);
            double t1 = Math.Min(head + 120, tail);
            AddLnDiff(diff, t0, 1.3);
            AddLnDiff(diff, t1, -0.3);
            AddLnDiff(diff, tail, -1.0);
        }

        var pointsSet = new HashSet<double> { 0, totalTime };
        pointsSet.UnionWith(diff.Keys);
        var points = pointsSet.OrderBy(static value => value).ToList();
        var values = new List<double>();
        var cumulative = new List<double> { 0 };
        var current = 0.0;

        for (var i = 0; i < points.Count - 1; i += 1)
        {
            var time = points[i];
            if (diff.TryGetValue(time, out var change))
            {
                current += change;
            }

            var value = Math.Min(current, 2.5 + (0.5 * current));
            values.Add(value);
            var segmentLength = points[i + 1] - points[i];
            cumulative.Add(cumulative[^1] + (segmentLength * value));
        }

        return new SunnyLnRepresentation(points, cumulative, values);
    }

    private static void AddLnDiff(Dictionary<double, double> diff, double time, double value)
    {
        if (!diff.TryAdd(time, value))
        {
            diff[time] += value;
        }
    }

    private static double SunnyLnSum(double start, double end, SunnyLnRepresentation representation)
    {
        var points = representation.Points;
        var cumulative = representation.Cumulative;
        var values = representation.Values;

        var i = SearchSortedRight(points, start) - 1;
        var j = SearchSortedRight(points, end) - 1;
        i = Math.Max(0, i);
        j = Math.Max(0, j);

        if (values.Count == 0)
        {
            return 0;
        }

        i = Math.Min(i, values.Count - 1);
        j = Math.Min(j, values.Count - 1);
        if (i == j)
        {
            return (end - start) * values[i];
        }

        var total = (points[i + 1] - start) * values[i];
        total += cumulative[j] - cumulative[i + 1];
        total += (end - points[j]) * values[j];
        return total;
    }

    private static double[] ComputeSunnyPbar(double x, List<(int Column, int Head, int Tail)> noteSeq, SunnyLnRepresentation lnRepresentation, double[] anchor, double[] baseCorners)
    {
        static double StreamBooster(double delta)
        {
            var nps = 7.5 / delta;
            return 160 < nps && nps < 360
                ? 1 + (1.7e-7 * (nps - 160) * Math.Pow(nps - 360, 2))
                : 1;
        }

        var pStep = new double[baseCorners.Length];
        for (var i = 0; i < noteSeq.Count - 1; i += 1)
        {
            var leftHead = noteSeq[i].Head;
            var rightHead = noteSeq[i + 1].Head;
            var deltaTime = rightHead - leftHead;

            if (deltaTime < 1e-9)
            {
                var spike = 1000 * Math.Pow(0.02 * ((4 / x) - 24), 0.25);
                var leftIndex = SearchSortedLeft(baseCorners, leftHead);
                var rightIndex = SearchSortedRight(baseCorners, leftHead);
                for (var index = leftIndex; index < rightIndex; index += 1)
                {
                    if (index >= 0 && index < pStep.Length)
                    {
                        pStep[index] += spike;
                    }
                }

                continue;
            }

            var startIndex = SearchSortedLeft(baseCorners, leftHead);
            var endIndex = SearchSortedLeft(baseCorners, rightHead);
            if (startIndex >= endIndex)
            {
                continue;
            }

            var delta = 0.001 * deltaTime;
            var value = 1 + (6 * 0.001 * SunnyLnSum(leftHead, rightHead, lnRepresentation));
            var boost = StreamBooster(delta);
            double increase;

            if (delta < (2 * x) / 3)
            {
                increase = Math.Pow(delta, -1)
                    * Math.Pow(0.08 * Math.Pow(x, -1) * (1 - (24 * Math.Pow(x, -1) * Math.Pow(delta - (x / 2), 2))), 0.25)
                    * Math.Max(boost, value);
            }
            else
            {
                increase = Math.Pow(delta, -1)
                    * Math.Pow(0.08 * Math.Pow(x, -1) * (1 - (24 * Math.Pow(x, -1) * Math.Pow(x / 6, 2))), 0.25)
                    * Math.Max(boost, value);
            }

            for (var index = startIndex; index < endIndex; index += 1)
            {
                pStep[index] += Math.Min(increase * anchor[index], Math.Max(increase, (increase * 2) - 10));
            }
        }

        return SmoothOnCorners(baseCorners, pStep, 500, 0.001, average: false);
    }

    private static double[] ComputeSunnyAbar(int keyCount, List<List<int>> activeColumns, double[][] deltaKs, double[] aCorners, double[] baseCorners)
    {
        var dks = new double[keyCount - 1][];
        for (var column = 0; column < keyCount - 1; column += 1)
        {
            dks[column] = new double[baseCorners.Length];
        }

        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var columns = activeColumns[index];
            for (var j = 0; j < columns.Count - 1; j += 1)
            {
                var k0 = columns[j];
                var k1 = columns[j + 1];
                if (k0 < keyCount - 1 && k0 < deltaKs.Length && k1 < deltaKs.Length)
                {
                    dks[k0][index] = Math.Abs(deltaKs[k0][index] - deltaKs[k1][index]) + (0.4 * Math.Max(0, Math.Max(deltaKs[k0][index], deltaKs[k1][index]) - 0.11));
                }
            }
        }

        var aStep = Enumerable.Repeat(1.0, aCorners.Length).ToArray();
        for (var index = 0; index < aCorners.Length; index += 1)
        {
            var corner = aCorners[index];
            var baseIndex = SearchSortedLeft(baseCorners, corner);
            if (baseIndex >= baseCorners.Length)
            {
                baseIndex = baseCorners.Length - 1;
            }

            var columns = activeColumns[baseIndex];
            for (var j = 0; j < columns.Count - 1; j += 1)
            {
                var k0 = columns[j];
                var k1 = columns[j + 1];
                if (k0 >= keyCount - 1 || k0 >= dks.Length)
                {
                    continue;
                }

                var distance = dks[k0][baseIndex];
                if (distance < 0.02)
                {
                    aStep[index] *= Math.Min(0.75 + (0.5 * Math.Max(deltaKs[k0][baseIndex], deltaKs[k1][baseIndex])), 1);
                }
                else if (distance < 0.07)
                {
                    aStep[index] *= Math.Min(0.65 + (5 * distance) + (0.5 * Math.Max(deltaKs[k0][baseIndex], deltaKs[k1][baseIndex])), 1);
                }
            }
        }

        return SmoothOnCorners(aCorners, aStep, 250, 1.0, average: true);
    }

    private static double[] ComputeSunnyRbar(double x, List<List<(int Column, int Head, int Tail)>> noteSeqByColumn, List<(int Column, int Head, int Tail)> tailSeq, double[] baseCorners)
    {
        var rStep = new double[baseCorners.Length];
        if (tailSeq.Count == 0)
        {
            return SmoothOnCorners(baseCorners, rStep, 500, 0.001, average: false);
        }

        var timesByColumn = new Dictionary<int, List<int>>();
        for (var i = 0; i < noteSeqByColumn.Count; i += 1)
        {
            timesByColumn[i] = noteSeqByColumn[i].Select(static item => item.Head).ToList();
        }

        var releaseIndex = new double[tailSeq.Count];
        for (var i = 0; i < tailSeq.Count; i += 1)
        {
            var item = tailSeq[i];
            if (!timesByColumn.ContainsKey(item.Column) || item.Column >= noteSeqByColumn.Count)
            {
                releaseIndex[i] = 0;
                continue;
            }

            var nextNote = FindNextNoteInColumn(item, timesByColumn[item.Column], noteSeqByColumn[item.Column]);
            var iH = 0.001 * Math.Abs(item.Tail - item.Head - 80) / x;
            var iT = 0.001 * Math.Abs(nextNote.Head - item.Tail - 80) / x;
            releaseIndex[i] = 2.0 / (2 + Math.Exp(-5 * (iH - 0.75)) + Math.Exp(-5 * (iT - 0.75)));
        }

        for (var i = 0; i < tailSeq.Count - 1; i += 1)
        {
            var start = tailSeq[i].Tail;
            var end = tailSeq[i + 1].Tail;
            var leftIndex = SearchSortedLeft(baseCorners, start);
            var rightIndex = SearchSortedLeft(baseCorners, end);
            if (leftIndex >= rightIndex)
            {
                continue;
            }

            var deltaR = 0.001 * (tailSeq[i + 1].Tail - tailSeq[i].Tail);
            var value = 0.08 * Math.Pow(deltaR, -0.5) * Math.Pow(x, -1) * (1 + (0.8 * (releaseIndex[i] + releaseIndex[i + 1])));
            for (var index = leftIndex; index < rightIndex; index += 1)
            {
                rStep[index] = value;
            }
        }

        return SmoothOnCorners(baseCorners, rStep, 500, 0.001, average: false);
    }

    private static (double[] CStep, double[] KsStep) ComputeSunnyCAndKs(int keyCount, List<(int Column, int Head, int Tail)> noteSeq, bool[][] keyUsage, double[] baseCorners)
    {
        var hitTimes = noteSeq.Select(static item => item.Head).OrderBy(static value => value).ToList();
        var cStep = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var center = baseCorners[index];
            var low = center - 500;
            var high = center + 500;
            cStep[index] = SearchSortedLeft(hitTimes, high) - SearchSortedLeft(hitTimes, low);
        }

        var ksStep = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var count = 0;
            for (var column = 0; column < keyCount; column += 1)
            {
                if (keyUsage[column][index])
                {
                    count += 1;
                }
            }

            ksStep[index] = Math.Max(count, 1);
        }

        return (cStep, ksStep);
    }

    private static (int Column, int Head, int Tail) FindNextNoteInColumn((int Column, int Head, int Tail) note, List<int> times, List<(int Column, int Head, int Tail)> columnNotes)
    {
        var index = SearchSortedLeft(times, note.Head);
        return index + 1 < columnNotes.Count ? columnNotes[index + 1] : (0, 1_000_000_000, 1_000_000_000);
    }

    private static double[] InterpValues(double[] newX, double[] oldX, double[] oldValues)
    {
        var newValues = new double[newX.Length];
        for (var i = 0; i < newX.Length; i += 1)
        {
            var x = newX[i];
            if (x <= oldX[0])
            {
                newValues[i] = oldValues[0];
            }
            else if (x >= oldX[^1])
            {
                newValues[i] = oldValues[^1];
            }
            else
            {
                var position = SearchSortedLeft(oldX, x);
                var x0 = oldX[position - 1];
                var x1 = oldX[position];
                var y0 = oldValues[position - 1];
                var y1 = oldValues[position];
                newValues[i] = y0 + (((x - x0) / (x1 - x0)) * (y1 - y0));
            }
        }

        return newValues;
    }

    private static double[] StepInterp(double[] newX, double[] oldX, double[] oldValues)
    {
        var result = new double[newX.Length];
        for (var i = 0; i < newX.Length; i += 1)
        {
            var index = SearchSortedRight(oldX, newX[i]) - 1;
            index = Math.Clamp(index, 0, oldValues.Length - 1);
            result[i] = oldValues[index];
        }

        return result;
    }

    private static double[] SmoothOnCorners(double[] x, double[] f, double window, double scale, bool average)
    {
        var cumulative = CumulativeSum(x, f);
        var output = new double[f.Length];
        for (var i = 0; i < x.Length; i += 1)
        {
            var center = x[i];
            var a = Math.Max(center - window, x[0]);
            var b = Math.Min(center + window, x[^1]);
            var value = QueryCumulative(b, x, cumulative, f) - QueryCumulative(a, x, cumulative, f);
            output[i] = average
                ? (b - a > 0 ? value / (b - a) : 0.0)
                : scale * value;
        }

        return output;
    }

    private static double[] CumulativeSum(double[] x, double[] f)
    {
        var output = new double[x.Length];
        for (var i = 1; i < x.Length; i += 1)
        {
            output[i] = output[i - 1] + (f[i - 1] * (x[i] - x[i - 1]));
        }

        return output;
    }

    private static double QueryCumulative(double query, double[] x, double[] cumulative, double[] f)
    {
        if (query <= x[0])
        {
            return 0.0;
        }

        if (query >= x[^1])
        {
            return cumulative[^1];
        }

        var index = SearchSortedLeft(x, query) - 1;
        if (index < 0)
        {
            index = 0;
        }

        return cumulative[index] + (f[index] * (query - x[index]));
    }

    private static int SearchSortedLeft(IReadOnlyList<double> values, double target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int SearchSortedLeft(IReadOnlyList<int> values, double target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int SearchSortedRight(IReadOnlyList<double> values, double target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] <= target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static double RescaleHigh(double sr)
    {
        return sr <= 9 ? sr : 9 + ((sr - 9) * (1.0 / 1.2));
    }

    private sealed record SunnyCorners(double[] AllCorners, double[] BaseCorners, double[] ACorners);

    private sealed record SunnyLnRepresentation(List<double> Points, List<double> Cumulative, List<double> Values);
}

internal static class InterludeCalculator
{
    private const float CurvePower = 0.6f;
    private const float CurveScale = 0.4056f;
    private const float MostImportantNotes = 2500.0f;
    private const float JackCurveCutoff = 230.0f;
    private const float StreamCurveCutoff = 10.0f;
    private const float StreamCurveCutoff2 = 10.0f;
    private const float OhtNerf = 3.0f;
    private const float StreamScale = 6.0f;
    private const float StreamPow = 0.5f;
    private const float StrainScale = 0.01626f;
    private const float StrainTimeCap = 200.0f;

    public static double Calculate(BeatmapChart chart, double rate)
    {
        if (chart.HitObjects.Count == 0 || chart.TimingPoints.Count == 0)
        {
            return 0.0;
        }

        var keyCount = chart.ColumnCount;
        var noteRows = chart.HitObjects
            .GroupBy(static item => item.StartTime)
            .OrderBy(static group => group.Key)
            .Select(static group => new
            {
                Time = (float)group.Key,
                Notes = group.Select(item => new { item.Column, item.IsHold }).ToList(),
            })
            .ToList();

        var lastNoteInColumn = new float[keyCount];
        var strainValues = new float[keyCount];
        var strainDataPoints = new List<float>();
        var handSplit = keyCount / 2;

        foreach (var row in noteRows)
        {
            var time = row.Time / (float)rate;
            var noteDifficulties = new float[keyCount];
            var rowStrains = new float[keyCount];

            for (var key = 0; key < keyCount; key += 1)
            {
                var hasNote = row.Notes.Any(note => note.Column == key);
                if (!hasNote)
                {
                    continue;
                }

                var jackDelta = time - lastNoteInColumn[key];
                var j = jackDelta > 0 ? Math.Min(15000.0f / jackDelta, JackCurveCutoff) : 0.0f;

                var handLo = key < handSplit ? 0 : handSplit;
                var handHi = key < handSplit ? handSplit - 1 : keyCount - 1;
                var sl = 0.0f;
                var sr = 0.0f;

                for (var handKey = handLo; handKey <= handHi; handKey += 1)
                {
                    if (handKey == key)
                    {
                        continue;
                    }

                    var trillDelta = time - lastNoteInColumn[handKey];
                    if (trillDelta <= 0)
                    {
                        continue;
                    }

                    var trillValue = MsToStreamBpm(trillDelta) * JackCompensation(jackDelta, trillDelta);
                    if (handKey < key)
                    {
                        sl = Math.Max(sl, trillValue);
                    }
                    else
                    {
                        sr = Math.Max(sr, trillValue);
                    }
                }

                noteDifficulties[key] = CalculateNoteTotal(j, sl, sr);
                strainValues[key] = StrainFunc(1575.0f, strainValues[key], noteDifficulties[key], Math.Max(0.0f, jackDelta));
                rowStrains[key] = strainValues[key];
                lastNoteInColumn[key] = time;
            }

            foreach (var strain in rowStrains)
            {
                if (strain > 0.0f)
                {
                    strainDataPoints.Add(strain);
                }
            }
        }

        return WeightedOverallDifficulty(strainDataPoints);
    }

    private static double WeightedOverallDifficulty(IEnumerable<float> data)
    {
        var values = data.Where(static value => value > 0.0f).OrderBy(static value => value).ToArray();
        if (values.Length == 0)
        {
            return 0.0;
        }

        var length = values.Length;
        var weight = 0.0f;
        var total = 0.0f;
        for (var i = 0; i < values.Length; i += 1)
        {
            var position = (i + MostImportantNotes - length) / MostImportantNotes;
            var x = Math.Max(0.0f, position);
            var w = 0.002f + (float)Math.Pow(x, 4.0);
            weight += w;
            total += values[i] * w;
        }

        if (weight <= 0.0f)
        {
            return 0.0;
        }

        var result = (float)Math.Pow(total / weight, CurvePower) * CurveScale;
        return float.IsFinite(result) ? result : 0.0;
    }

    private static float MsToStreamBpm(float deltaMs)
    {
        var result = (300.0f / (0.02f * deltaMs))
                     - (300.0f / (float)Math.Pow(0.02f * deltaMs, StreamCurveCutoff) / StreamCurveCutoff2);
        return Math.Max(0.0f, result);
    }

    private static float JackCompensation(float jackDelta, float streamDelta)
    {
        if (streamDelta <= 0.0f)
        {
            return 1.0f;
        }

        var ratio = jackDelta / streamDelta;
        var logRatio = (float)Math.Log(ratio, 2.0);
        return Math.Min(1.0f, (float)Math.Sqrt(Math.Max(0.0f, logRatio)));
    }

    private static float CalculateNoteTotal(float j, float sl, float sr)
    {
        return (float)Math.Pow(
            Math.Pow(StreamScale * (float)Math.Pow(sl, StreamPow), OhtNerf)
            + Math.Pow(StreamScale * (float)Math.Pow(sr, StreamPow), OhtNerf)
            + Math.Pow(j, OhtNerf),
            1.0f / OhtNerf);
    }

    private static float StrainFunc(float halfLifeMs, float currentValue, float input, float deltaMs)
    {
        var decayRate = (float)Math.Log(0.5) / halfLifeMs;
        var decay = (float)Math.Exp(decayRate * Math.Min(StrainTimeCap, deltaMs));
        var timeCapDecay = deltaMs > StrainTimeCap
            ? (float)Math.Exp(decayRate * (deltaMs - StrainTimeCap))
            : 1.0f;

        var a = currentValue * timeCapDecay;
        var b = input * input * StrainScale;
        return b - ((b - a) * decay);
    }
}

internal static class CompanellaClassifier
{
    private static readonly string[] DanLabels =
    [
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "alpha", "beta", "gamma", "delta", "epsilon",
        "Emik Zeta", "Thaumiel Eta", "CloverWisp Theta", "iota", "kappa",
    ];

    private static readonly ReadOnlyDictionary<string, string> VariantText = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--"] = "low",
            ["-"] = "mid/low",
            [""] = "mid",
            ["+"] = "mid/high",
            ["++"] = "high",
        });

    private static readonly object Gate = new();
    private static InferenceSession? _session;

    public static CompanellaResult Classify(IReadOnlyDictionary<string, double> msdValues, double interludeStar, double sunnyStar)
    {
        var features = new float[]
        {
            (float)GetMsdValue(msdValues, "Overall"),
            (float)GetMsdValue(msdValues, "Stream"),
            (float)GetMsdValue(msdValues, "Jumpstream"),
            (float)GetMsdValue(msdValues, "Handstream"),
            (float)GetMsdValue(msdValues, "Stamina"),
            (float)GetMsdValue(msdValues, "JackSpeed"),
            (float)GetMsdValue(msdValues, "Chordjack"),
            (float)GetMsdValue(msdValues, "Technical"),
            (float)interludeStar,
            (float)sunnyStar,
        };

        if (features.Any(static value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("Companella requires valid MSD, InterludeSR, and Sunny SR values.");
        }

        var session = GetSession();
        var inputName = session.InputMetadata.Keys.First();
        var tensor = new DenseTensor<float>(features, [1, 10]);
        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, tensor)]);

        var output = results.First().AsEnumerable<float>().FirstOrDefault();
        var shifted = Math.Clamp(output, 1.0f, 20.0f) + 1.0f;
        var (danIndex, variant) = ParsePrediction(shifted);
        var label = DanLabels[danIndex];
        var rounded = Math.Round(shifted, 2);

        return new CompanellaResult
        {
            Difficulty = BuildDisplayDifficulty(label, variant),
            NumericDifficulty = rounded,
            NumericDifficultyHint = null,
        };
    }

    private static InferenceSession GetSession()
    {
        if (_session != null)
        {
            return _session;
        }

        lock (Gate)
        {
            if (_session == null)
            {
                byte[] modelBytes = AnalyzerResources.ReadBytes("dan_model.onnx");
                _session = new InferenceSession(modelBytes);
            }
        }

        return _session;
    }

    private static double GetMsdValue(IReadOnlyDictionary<string, double> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : double.NaN;
    }

    private static (int DanIndex, string Variant) ParsePrediction(float rawValue)
    {
        if (rawValue < 1.0f)
        {
            return (0, "--");
        }

        if (rawValue >= 20.0f)
        {
            return (19, "++");
        }

        var danLevel = Math.Clamp((int)Math.Round(rawValue), 1, 20);
        var danIndex = danLevel - 1;
        var offset = rawValue - danLevel;

        var variant = offset switch
        {
            <= -0.3f => "--",
            <= -0.1f => "-",
            < 0.1f => "",
            < 0.3f => "+",
            _ => "++",
        };

        return (danIndex, variant);
    }

    private static string BuildDisplayDifficulty(string label, string variant)
    {
        var variantText = VariantText.TryGetValue(variant, out var value) ? value : VariantText[string.Empty];
        var displayLabel = CapitalizeLabel(label);

        if (displayLabel.All(char.IsDigit))
        {
            return $"Reform {displayLabel} {variantText}";
        }

        return $"{displayLabel} {variantText}";
    }

    private static string CapitalizeLabel(string label)
    {
        var text = label.Trim();
        if (text.Length == 0 || text.All(char.IsDigit))
        {
            return text;
        }

        return string.Join(
            " ",
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
}
