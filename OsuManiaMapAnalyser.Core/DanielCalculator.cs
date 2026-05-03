namespace OsuManiaMapAnalyser.Core;

internal static class DanielCalculator
{
    public static double CalculateStar(BeatmapChart chart, double speedRate)
    {
        if (chart.Status == "Fail")
        {
            throw new InvalidOperationException("Beatmap parse failed.");
        }

        if (chart.Status == "NotMania")
        {
            throw new InvalidOperationException("Beatmap mode is not mania.");
        }

        if (chart.ColumnCount != 4)
        {
            throw new InvalidOperationException("Daniel only supports 4K.");
        }

        var timeScale = speedRate != 0 ? 1.0 / speedRate : 1.0;
        var noteSeq = chart.HitObjects
            .Select(item => (Column: item.Column, Head: (int)Math.Floor(item.StartTime * timeScale)))
            .OrderBy(static item => item.Head)
            .ThenBy(static item => item.Column)
            .ToList();

        if (noteSeq.Count == 0)
        {
            throw new InvalidOperationException("Beatmap parse failed.");
        }

        const double od = 9.0;
        var x = 0.3 * Math.Sqrt((64.5 - Math.Ceiling(od * 3.0)) / 500.0);
        x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);

        var keyCount = chart.ColumnCount;
        var totalTime = noteSeq[^1].Head + 1;
        var noteSeqByColumn = Enumerable.Range(0, keyCount)
            .Select(column => noteSeq.Where(item => item.Column == column).ToList())
            .ToList();

        var corners = GetCorners(totalTime, noteSeq);
        var keyUsage = GetKeyUsage(keyCount, totalTime, noteSeq, corners.BaseCorners);
        var activeColumns = corners.BaseCorners
            .Select((_, index) => Enumerable.Range(0, keyCount).Where(column => keyUsage[column][index] != 0).ToList())
            .ToList();

        var keyUsage400 = GetKeyUsage400(keyCount, noteSeq, corners.BaseCorners);
        var anchor = ComputeAnchor(keyCount, keyUsage400, corners.BaseCorners);
        var (deltaKs, jbar) = ComputeJbar(keyCount, x, noteSeqByColumn, corners.BaseCorners);
        var jbarAll = InterpValues(corners.AllCorners, corners.BaseCorners, jbar);
        var xbar = ComputeXbar(keyCount, x, noteSeqByColumn, activeColumns, corners.BaseCorners);
        var xbarAll = InterpValues(corners.AllCorners, corners.BaseCorners, xbar);
        var pbar = ComputePbar(x, noteSeq, anchor, corners.BaseCorners);
        var pbarAll = InterpValues(corners.AllCorners, corners.BaseCorners, pbar);
        var abar = ComputeAbar(keyCount, activeColumns, deltaKs, corners.ACorners, corners.BaseCorners);
        var abarAll = InterpValues(corners.AllCorners, corners.ACorners, abar);
        var (cStep, ksStep) = ComputeCAndKs(keyCount, noteSeq, keyUsage, corners.BaseCorners);
        var cAll = StepInterp(corners.AllCorners, corners.BaseCorners, cStep);
        var ksAll = StepInterp(corners.AllCorners, corners.BaseCorners, ksStep);

        var dAll = new double[corners.AllCorners.Length];
        for (var i = 0; i < corners.AllCorners.Length; i += 1)
        {
            var term1 = Math.Pow(abarAll[i], 3.0 / ksAll[i]) * Math.Min(jbarAll[i], 8 + (0.85 * jbarAll[i]));
            var term2 = Math.Pow(abarAll[i], 2.0 / 3.0) * (0.8 * pbarAll[i]);
            var s = Math.Pow((0.4 * Math.Pow(term1, 1.5)) + (0.6 * Math.Pow(term2, 1.5)), 2.0 / 3.0);
            var tValue = Math.Pow(abarAll[i], 3.0 / ksAll[i]) * xbarAll[i] / (xbarAll[i] + s + 1);
            dAll[i] = (2.7 * Math.Pow(s, 0.5) * Math.Pow(tValue, 1.5)) + (s * 0.27);
        }

        var gated = ApplyProximityEnvelope(corners.AllCorners, dAll, noteSeq);
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

        var sortedIndices = Enumerable.Range(0, corners.AllCorners.Length).OrderBy(index => gated[index]).ToArray();
        var dSorted = sortedIndices.Select(index => gated[index]).ToArray();
        var wSorted = sortedIndices.Select(index => effectiveWeights[index]).ToArray();

        var cumulativeWeights = new double[wSorted.Length];
        cumulativeWeights[0] = wSorted[0];
        for (var i = 1; i < wSorted.Length; i += 1)
        {
            cumulativeWeights[i] = cumulativeWeights[i - 1] + wSorted[i];
        }

        var totalWeight = cumulativeWeights[^1];
        var normalized = cumulativeWeights.Select(weight => totalWeight > 0 ? weight / totalWeight : 0.0).ToArray();
        var targetPercentiles = new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 };
        var picks = targetPercentiles
            .Select(percentile =>
            {
                var index = Math.Min(SearchSortedLeft(normalized, percentile), dSorted.Length - 1);
                return dSorted[index];
            })
            .ToArray();

        var percentile93 = (picks[0] + picks[1] + picks[2] + picks[3]) / 4.0;
        var percentile83 = (picks[4] + picks[5] + picks[6] + picks[7]) / 4.0;

        double sumD5W = 0;
        double sumW = 0;
        for (var i = 0; i < dSorted.Length; i += 1)
        {
            sumD5W += Math.Pow(dSorted[i], 5) * wSorted[i];
            sumW += wSorted[i];
        }

        var weightedMean = sumW > 0 ? Math.Pow(sumD5W / sumW, 0.2) : 0.0;
        var sr = (0.88 * percentile93 * 0.25) + (0.94 * percentile83 * 0.2) + (weightedMean * 0.55);
        sr *= noteSeq.Count / (noteSeq.Count + 60.0);
        sr = RescaleHigh(sr) * 0.975;
        return sr;
    }

    private static DanielCorners GetCorners(int totalTime, List<(int Column, int Head)> noteSeq)
    {
        var baseSet = new HashSet<double>();
        foreach (var (_, head) in noteSeq)
        {
            baseSet.Add(head);
            baseSet.Add(head + 501);
            baseSet.Add(head - 499);
            baseSet.Add(head + 1);
        }

        baseSet.Add(0);
        baseSet.Add(totalTime);
        var baseCorners = baseSet.Where(value => value >= 0 && value <= totalTime).OrderBy(static value => value).ToArray();

        var aSet = new HashSet<double>();
        foreach (var (_, head) in noteSeq)
        {
            aSet.Add(head);
            aSet.Add(head + 1000);
            aSet.Add(head - 1000);
        }

        aSet.Add(0);
        aSet.Add(totalTime);
        var aCorners = aSet.Where(value => value >= 0 && value <= totalTime).OrderBy(static value => value).ToArray();
        var allCorners = baseCorners.Union(aCorners).OrderBy(static value => value).ToArray();
        return new DanielCorners(allCorners, baseCorners, aCorners);
    }

    private static byte[][] GetKeyUsage(int keyCount, int totalTime, List<(int Column, int Head)> noteSeq, double[] baseCorners)
    {
        var keyUsage = new byte[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            keyUsage[column] = new byte[baseCorners.Length];
        }

        foreach (var (column, head) in noteSeq)
        {
            var startTime = Math.Max(head - 150, 0);
            var endTime = Math.Min(head + 150, totalTime - 1);
            var leftIndex = SearchSortedLeft(baseCorners, startTime);
            var rightIndex = SearchSortedLeft(baseCorners, endTime);

            for (var index = leftIndex; index < rightIndex; index += 1)
            {
                keyUsage[column][index] = 1;
            }
        }

        return keyUsage;
    }

    private static double[][] GetKeyUsage400(int keyCount, List<(int Column, int Head)> noteSeq, double[] baseCorners)
    {
        var keyUsage400 = new double[keyCount][];
        for (var column = 0; column < keyCount; column += 1)
        {
            keyUsage400[column] = new double[baseCorners.Length];
        }

        foreach (var (column, head) in noteSeq)
        {
            var left400 = SearchSortedLeft(baseCorners, head - 400);
            var center = SearchSortedLeft(baseCorners, head);
            var right400 = SearchSortedLeft(baseCorners, head + 400);

            if (center >= 0 && center < baseCorners.Length)
            {
                keyUsage400[column][center] += 3.75;
            }

            for (var index = left400; index < center; index += 1)
            {
                keyUsage400[column][index] += 3.75 - ((3.75 / (400.0 * 400.0)) * Math.Pow(baseCorners[index] - head, 2));
            }

            for (var index = center + 1; index < right400; index += 1)
            {
                keyUsage400[column][index] += 3.75 - ((3.75 / (400.0 * 400.0)) * Math.Pow(baseCorners[index] - head, 2));
            }
        }

        return keyUsage400;
    }

    private static double[] ComputeAnchor(int keyCount, double[][] keyUsage400, double[] baseCorners)
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
            var nonZero = counts.Where(static value => value > 0).ToArray();
            double raw = 0;
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

                raw = maxWalk > 0 ? walk / maxWalk : 0;
            }

            anchor[index] = 1 + Math.Min(raw - 0.18, 5 * Math.Pow(raw - 0.22, 3));
        }

        return anchor;
    }

    private static (double[][] DeltaKs, double[] Jbar) ComputeJbar(int keyCount, double x, List<List<(int Column, int Head)>> noteSeqByColumn, double[] baseCorners)
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
            var notes = noteSeqByColumn[column];
            for (var i = 0; i < notes.Count - 1; i += 1)
            {
                var start = notes[i].Head;
                var end = notes[i + 1].Head;
                if (end <= start)
                {
                    continue;
                }

                var leftIndex = SearchSortedLeft(baseCorners, start);
                var rightIndex = SearchSortedLeft(baseCorners, end);
                if (leftIndex >= rightIndex)
                {
                    continue;
                }

                var delta = 0.001 * (end - start);
                var value = Math.Pow(delta, -1) * Math.Pow(delta + (0.11 * Math.Pow(x, 0.25)), -1) * JackNerfer(delta);
                for (var index = leftIndex; index < rightIndex; index += 1)
                {
                    jKs[column][index] = value;
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
                var weight = 1.0 / Math.Max(deltaKs[column][index], 1e-9);
                numerator += Math.Pow(Math.Max(value, 0), 5) * weight;
                denominator += weight;
            }

            jbar[index] = Math.Pow(numerator / Math.Max(1e-9, denominator), 0.2);
        }

        return (deltaKs, jbar);
    }

    private static double[] ComputeXbar(int keyCount, double x, List<List<(int Column, int Head)>> noteSeqByColumn, List<List<int>> activeColumns, double[] baseCorners)
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
            List<(int Column, int Head)> notesInPair;
            if (column == 0)
            {
                notesInPair = noteSeqByColumn[0];
            }
            else if (column == keyCount)
            {
                notesInPair = noteSeqByColumn[keyCount - 1];
            }
            else
            {
                notesInPair = noteSeqByColumn[column - 1].Concat(noteSeqByColumn[column]).OrderBy(static item => item.Head).ToList();
            }

            for (var i = 1; i < notesInPair.Count; i += 1)
            {
                var start = notesInPair[i - 1].Head;
                var end = notesInPair[i].Head;
                var leftIndex = SearchSortedLeft(baseCorners, start);
                var rightIndex = SearchSortedLeft(baseCorners, end);
                if (leftIndex >= rightIndex)
                {
                    continue;
                }

                var delta = 0.001 * (end - start);
                var value = 0.16 * Math.Pow(Math.Max(x, delta), -2);
                var prevStartInactive = !activeColumns[leftIndex].Contains(column - 1);
                var prevEndInactive = !activeColumns[rightIndex < activeColumns.Count ? rightIndex : activeColumns.Count - 1].Contains(column - 1);
                var currStartInactive = !activeColumns[leftIndex].Contains(column);
                var currEndInactive = !activeColumns[rightIndex < activeColumns.Count ? rightIndex : activeColumns.Count - 1].Contains(column);

                if ((prevStartInactive && prevEndInactive) || (currStartInactive && currEndInactive))
                {
                    value *= 1 - crossCoeff[column];
                }

                var fastValue = Math.Max(0, (0.4 * Math.Pow(Math.Max(Math.Max(delta, 0.06), 0.75 * x), -2)) - 80);
                for (var index = leftIndex; index < rightIndex; index += 1)
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
            double sum2 = 0;
            for (var column = 0; column <= keyCount; column += 1)
            {
                sum1 += xKs[column][index] * crossCoeff[column];
            }

            for (var column = 0; column < keyCount; column += 1)
            {
                var pair = fastCross[column][index] * crossCoeff[column] * fastCross[column + 1][index] * crossCoeff[column + 1];
                if (pair > 0)
                {
                    sum2 += Math.Sqrt(pair);
                }
            }

            xBase[index] = sum1 + sum2;
        }

        return SmoothOnCorners(baseCorners, xBase, 500, 0.001, average: false);
    }

    private static double[] ComputePbar(double x, List<(int Column, int Head)> noteSeq, double[] anchor, double[] baseCorners)
    {
        static double StreamBooster(double delta)
        {
            var bpm = Math.Max(0, Math.Min(7.5 / Math.Max(delta, 1e-9), 420));
            var primary = 0.10 / (1 + Math.Exp(-0.06 * (bpm - 175)));
            var secondary = bpm >= 200 && bpm <= 350 ? 0.30 * (1 - Math.Exp(-0.02 * (bpm - 200))) : 0;
            return 1 + primary + secondary;
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
                    pStep[index] += spike;
                }

                continue;
            }

            var leftIndex2 = SearchSortedLeft(baseCorners, leftHead);
            var rightIndex2 = SearchSortedLeft(baseCorners, rightHead);
            if (rightIndex2 <= leftIndex2)
            {
                continue;
            }

            var delta = 0.001 * deltaTime;
            var baseIncrease = Math.Pow(0.08 * Math.Pow(x, -1) * (1 - (24 * Math.Pow(x, -1) * Math.Pow(x / 6, 2))), 0.25);
            double increase;
            if (delta < (2 * x) / 3)
            {
                increase = Math.Pow(delta, -1)
                    * Math.Pow(0.08 * Math.Pow(x, -1) * (1 - (24 * Math.Pow(x, -1) * Math.Pow(delta - (x / 2), 2))), 0.25)
                    * Math.Max(StreamBooster(delta), 1);
            }
            else
            {
                increase = Math.Pow(delta, -1) * baseIncrease * Math.Max(StreamBooster(delta), 1);
            }

            for (var index = leftIndex2; index < rightIndex2; index += 1)
            {
                var boosted = increase * anchor[index];
                pStep[index] += Math.Min(boosted, Math.Max(increase, (increase * 2) - 10));
            }
        }

        return SmoothOnCorners(baseCorners, pStep, 500, 0.001, average: false);
    }

    private static double[] ComputeAbar(int keyCount, List<List<int>> activeColumns, double[][] deltaKs, double[] aCorners, double[] baseCorners)
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
                dks[k0][index] = Math.Abs(deltaKs[k0][index] - deltaKs[k1][index]) + (0.4 * Math.Max(0, Math.Max(deltaKs[k0][index], deltaKs[k1][index]) - 0.11));
            }
        }

        var aStep = Enumerable.Repeat(1.0, aCorners.Length).ToArray();
        for (var index = 0; index < aCorners.Length; index += 1)
        {
            var baseIndex = SearchSortedLeft(baseCorners, aCorners[index]);
            if (baseIndex >= baseCorners.Length)
            {
                baseIndex = baseCorners.Length - 1;
            }

            var columns = activeColumns[baseIndex];
            for (var j = 0; j < columns.Count - 1; j += 1)
            {
                var k0 = columns[j];
                var k1 = columns[j + 1];
                var value = dks[k0][baseIndex];
                var dk0 = deltaKs[k0][baseIndex];
                var dk1 = deltaKs[k1][baseIndex];
                if (value < 0.02)
                {
                    aStep[index] *= Math.Min(0.75 + (0.5 * Math.Max(dk0, dk1)), 1);
                }
                else if (value < 0.07)
                {
                    aStep[index] *= Math.Min(0.65 + (5 * value) + (0.5 * Math.Max(dk0, dk1)), 1);
                }
            }
        }

        return SmoothOnCorners(aCorners, aStep, 250, 1.0, average: true);
    }

    private static (double[] CStep, double[] KsStep) ComputeCAndKs(int keyCount, List<(int Column, int Head)> noteSeq, byte[][] keyUsage, double[] baseCorners)
    {
        var noteTimes = noteSeq.Select(static item => item.Head).OrderBy(static value => value).ToList();
        var cStep = new double[baseCorners.Length];
        var lo = 0;
        var hi = 0;
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var center = baseCorners[index];
            var low = center - 500;
            var high = center + 500;
            while (lo < noteTimes.Count && noteTimes[lo] < low)
            {
                lo += 1;
            }

            while (hi < noteTimes.Count && noteTimes[hi] < high)
            {
                hi += 1;
            }

            cStep[index] = hi - lo;
        }

        var ksStep = new double[baseCorners.Length];
        for (var index = 0; index < baseCorners.Length; index += 1)
        {
            var count = 0;
            for (var column = 0; column < keyCount; column += 1)
            {
                if (keyUsage[column][index] != 0)
                {
                    count += 1;
                }
            }

            ksStep[index] = Math.Max(count, 1);
        }

        return (cStep, ksStep);
    }

    private static double[] ApplyProximityEnvelope(double[] allCorners, double[] values, List<(int Column, int Head)> noteSeq)
    {
        if (noteSeq.Count == 0)
        {
            return values.ToArray();
        }

        var noteTimes = noteSeq.Select(static item => (double)item.Head).OrderBy(static value => value).ToArray();
        var output = new double[allCorners.Length];
        for (var i = 0; i < allCorners.Length; i += 1)
        {
            var time = allCorners[i];
            var index = SearchSortedLeft(noteTimes, time);
            var after = index < noteTimes.Length ? Math.Abs(noteTimes[index] - time) : double.PositiveInfinity;
            var before = index > 0 ? Math.Abs(noteTimes[index - 1] - time) : double.PositiveInfinity;
            var distance = Math.Min(after, before);
            var ratio = Math.Max(0, Math.Min(distance / 500.0, 1.0));
            var envelope = 0.5 * (1 + Math.Cos(Math.PI * ratio));
            output[i] = values[i] * envelope;
        }

        return output;
    }

    private static double[] InterpValues(double[] newX, double[] oldX, double[] oldValues)
    {
        var output = new double[newX.Length];
        var index = 0;
        for (var i = 0; i < newX.Length; i += 1)
        {
            var value = newX[i];
            if (value <= oldX[0])
            {
                output[i] = oldValues[0];
                continue;
            }

            if (value >= oldX[^1])
            {
                output[i] = oldValues[^1];
                continue;
            }

            while (index + 1 < oldX.Length && oldX[index + 1] < value)
            {
                index += 1;
            }

            var x0 = oldX[index];
            var x1 = oldX[index + 1];
            var y0 = oldValues[index];
            var y1 = oldValues[index + 1];
            output[i] = x1 == x0 ? y0 : y0 + (((value - x0) / (x1 - x0)) * (y1 - y0));
        }

        return output;
    }

    private static double[] StepInterp(double[] newX, double[] oldX, double[] oldValues)
    {
        var output = new double[newX.Length];
        var index = 0;
        for (var i = 0; i < newX.Length; i += 1)
        {
            var value = newX[i];
            while (index + 1 < oldX.Length && oldX[index + 1] <= value)
            {
                index += 1;
            }

            output[i] = oldValues[Math.Clamp(index, 0, oldValues.Length - 1)];
        }

        return output;
    }

    private static double[] SmoothOnCorners(double[] x, double[] values, double window, double scale, bool average)
    {
        var cumulative = CumulativeSum(x, values);
        var output = new double[values.Length];
        for (var i = 0; i < x.Length; i += 1)
        {
            var center = x[i];
            var a = Math.Max(center - window, x[0]);
            var b = Math.Min(center + window, x[^1]);
            var value = QueryCumulative(b, x, cumulative, values) - QueryCumulative(a, x, cumulative, values);
            output[i] = average
                ? (b - a > 0 ? value / (b - a) : 0)
                : scale * value;
        }

        return output;
    }

    private static double[] CumulativeSum(double[] x, double[] values)
    {
        var output = new double[x.Length];
        for (var i = 1; i < x.Length; i += 1)
        {
            output[i] = output[i - 1] + (values[i - 1] * (x[i] - x[i - 1]));
        }

        return output;
    }

    private static double QueryCumulative(double query, double[] x, double[] cumulative, double[] values)
    {
        if (query <= x[0])
        {
            return 0;
        }

        if (query >= x[^1])
        {
            return cumulative[^1];
        }

        var index = SearchSortedRight(x, query) - 1;
        return cumulative[index] + (values[index] * (query - x[index]));
    }

    private static int SearchSortedLeft(IReadOnlyList<double> values, double target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
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
            var mid = (lo + hi) >> 1;
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
            var mid = (lo + hi) >> 1;
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

    private static double RescaleHigh(double star)
    {
        return star <= 9 ? star : 9 + ((star - 9) * (1 / 1.2));
    }

    private sealed record DanielCorners(double[] AllCorners, double[] BaseCorners, double[] ACorners);
}
