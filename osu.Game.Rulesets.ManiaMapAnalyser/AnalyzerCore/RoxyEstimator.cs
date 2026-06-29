namespace OsuManiaMapAnalyser.Core;

/// <summary>
/// Roxy meta-model estimator for 4K RC difficulty.
/// Ported from roxyEstimator.js / roxyMetaModel.generated.js.
/// Uses a linear ridge-regression model fusing Azusa/Sunny/Daniel
/// reference predictions with 7-stream structural features.
/// </summary>
internal static class RoxyEstimator
{
    private const double RcLnRatioLimit = 0.18;
    private const int MinNotes = 80;
    private const double RowToleranceMs = 2.0;
    private const double EntropyWindowMs = 750;
    private static readonly double[] NpsWindowsMs = [250, 500, 1000, 4000];
    private const double SectionMs = 400;
    private const double SectionDecay = 0.9;
    private const double SectionEmaAlpha = 0.15;
    private const double CorrectionClamp = 1.25;
    private const double ThetaHighNumeric = 18.4;
    private const double NumericOutputMax = 30;
    private const double OdNeutral = 9;
    private const double CanonicalFirstObjectMs = 1000;
    private const double ReferenceBucketSize = 1.0;
    private static readonly HashSet<string> DisabledMetaReferences = new(StringComparer.Ordinal) { "Sunny" };
    private const double ReferenceGapCorrectionScale = 0.33;

    // Stream config
    private static readonly string[] StreamNames = ["speed", "handStream", "jack", "chordjack", "tech", "stamina", "course"];
    private static readonly double[] StreamWeights = [0.22, 0.18, 0.16, 0.16, 0.12, 0.11, 0.05];
    private static readonly (double BurstTau, double StaminaTau, double BurstMix)[] StreamParams =
    [
        (220, 1600, 0.78),
        (260, 2200, 0.80),
        (300, 1800, 0.88),
        (260, 2400, 0.82),
        (450, 3200, 0.70),
        (1200, 10000, 0.58),
        (30000, 120000, 0.35),
    ];

    private static readonly (double X, double Y)[] IsotonicKnots =
    [
        (-2.6250, 2.4444), (-2.5000, 2.9000), (-2.1782, 3.2000), (-1.6429, 3.4667),
        (-0.8081, 4.9333), (-0.5781, 5.0000), (-0.3751, 5.1250), (0.0878, 5.7000),
        (0.5414, 7.3500), (0.7248, 9.6000), (1.2435, 9.7625), (2.2100, 9.8379),
        (3.3439, 10.3810), (4.1521, 10.8619), (4.6770, 12.2111), (7.5944, 12.8954),
        (10.3796, 12.9333), (10.7539, 13.1211), (11.2944, 13.1733), (12.4106, 13.4225),
        (13.3667, 13.7143), (14.0177, 14.0761), (15.2659, 14.1489), (16.4144, 14.3000),
        (16.9566, 14.3174), (17.5080, 14.6000), (17.9004, 14.8917), (18.1870, 15.0000),
        (18.5160, 15.0636), (19.5870, 15.2889), (20.2551, 15.6111), (21.0298, 16.0000),
        (21.3373, 16.5833),
    ];

    private static readonly Dictionary<string, double> RawMap = new()
    {
        ["p02"] = 3.9947,
        ["p98"] = 7.5454,
    };

    // ---- Meta-model coefficients (from roxyMetaModel.generated.js) ----
    private static readonly string[] MetaFeatureNames = new[]
    {
        "pred_Azusa","has_Azusa","pred_Sunny","has_Sunny","pred_Daniel","has_Daniel",
        "pred_Roxy","has_Roxy","pred_min","pred_max","pred_mean","pred_median",
        "pred_range","diff_Azusa_Daniel","absdiff_Azusa_Daniel","diff_Azusa_Sunny",
        "absdiff_Azusa_Sunny","diff_Azusa_Roxy","absdiff_Azusa_Roxy","diff_Daniel_Sunny",
        "absdiff_Daniel_Sunny","diff_Daniel_Roxy","absdiff_Daniel_Roxy","diff_Sunny_Roxy",
        "absdiff_Sunny_Roxy","roxy_logRaw","roxy_rawAgg","roxy_preNumeric","roxy_rawNumeric",
        "roxy_finalNumeric","corr_lowCj","corr_highStream","corr_highCjDamp",
        "corr_courseBreakDamp","corr_courseSustainLift","corr_denseJsLift","corr_denseJsDamp",
        "corr_anchorLift","corr_handBiasLift","corr_total","speed_aggregate","speed_q97",
        "speed_q90","speed_q75","speed_q50","speed_tailMean","speed_powerMean",
        "handStream_aggregate","handStream_q97","handStream_q90","handStream_q75",
        "handStream_q50","handStream_tailMean","handStream_powerMean","jack_aggregate",
        "jack_q97","jack_q90","jack_q75","jack_q50","jack_tailMean","jack_powerMean",
        "chordjack_aggregate","chordjack_q97","chordjack_q90","chordjack_q75","chordjack_q50",
        "chordjack_tailMean","chordjack_powerMean","tech_aggregate","tech_q97","tech_q90",
        "tech_q75","tech_q50","tech_tailMean","tech_powerMean","stamina_aggregate",
        "stamina_q97","stamina_q90","stamina_q75","stamina_q50","stamina_tailMean",
        "stamina_powerMean","course_aggregate","course_q97","course_q90","course_q75",
        "course_q50","course_tailMean","course_powerMean","stat_activeDurationSec",
        "stat_breakCount","stat_breakDensity","stat_avgNps","stat_chordRate","stat_threeRate",
        "stat_overlapRate","stat_rotationRate","stat_sameHandQ10","stat_fastJackRate",
        "stat_anchorRate","stat_anchorImbalance","stat_handBias","stat_peakToSustainGap",
        "stat_rows","stat_taps","logAvgNps","logDuration","chordFast","chordOverlap",
        "rotationInvQ10","breakPeak"
    };

    private static readonly double[] MetaMean =
    [
        12.72826087,1.0,13.38198758,0.0,13.57453416,0.81055901,12.6242236,1.0,
        11.75465839,13.79037267,13.07725155,13.38198758,2.03571429,-0.84627329,
        0.84937888,-0.65372671,0.6568323,0.10403727,1.8431677,0.19254658,0.19254658,
        0.95031056,1.37888199,0.75776398,1.1863354,5.92614006,539.81978276,
        9.98632547,10.03973556,12.6326087,0.00016242,0.06992376,-0.0541896,0.0,
        0.00012236,0.00060761,-0.07245047,0.10922873,0.0,0.05340714,18.74898276,
        20.09743525,18.48600901,17.21351366,15.59800093,20.93030497,15.23449006,
        19.73672826,21.1472205,19.65077624,18.1916163,16.19177904,21.89532236,
        15.89904907,13.41816071,14.1917913,13.36884969,12.6730646,11.43499783,
        14.67148121,11.05897888,3.9482722,4.43739798,3.90606957,3.3860559,
        2.56927842,4.63225621,2.87539814,10.40736537,10.87904379,10.46916242,
        10.00801817,9.21552081,11.05720916,8.88161351,2205.77279177,2398.82508075,
        2263.56603152,2021.03138106,1599.4951059,2429.87936708,1676.32994037,
        5548.0843896,6095.5018309,5699.04075435,4986.85697298,3942.34458758,
        6155.74777112,4124.64499068,163.6000264,2.72981366,0.9994264,21.10821817,
        0.46909752,0.18225404,0.35429084,0.50264519,65.73773292,0.4010837,
        0.83464379,0.01974379,0.01458432,0.16691071,2150.23136646,3427.60559006,
        3.0653091,4.99282968,0.20643745,0.21963654,0.00996844,0.17102308
    ];

    private static readonly double[] MetaScale =
    [
        3.33889316,1.0,2.60017649,1.0,2.73163613,0.39185853,2.42434025,1.0,
        2.95179596,2.69018815,2.60467039,2.60017649,1.64476327,1.58103132,
        1.57936507,1.61119003,1.60992648,2.46151417,1.63481149,0.39429988,
        0.39429988,1.67109605,1.33952851,1.53034075,1.22831485,0.89212647,
        510.47002995,5.34673045,5.54860555,2.42082201,0.00411862,0.15963457,
        0.12876132,1.0,0.00143112,0.00499721,0.10880868,0.11441514,1.0,
        0.35842785,10.15634444,10.75646462,10.48521942,9.98512179,9.16750068,
        10.83647652,8.47474324,10.90204069,11.50347552,11.19295924,10.63746031,
        9.7064718,11.60817816,9.04329807,5.31572854,5.64074659,5.32788833,
        5.08770165,4.84868504,5.80646495,4.53039153,2.66700503,2.87079584,
        2.76316458,2.61168149,2.15908058,2.88760371,2.0809843,3.90558312,
        4.04865855,4.00210599,3.89424156,3.64646744,4.05186181,3.36161587,
        1536.05634282,1676.79399945,1590.02473563,1415.54490325,1135.89441776,
        1690.85587144,1164.5858198,7873.24640896,8633.32163072,8014.03748152,
        7119.59330125,5870.83221785,8735.83166564,5926.81748919,93.75828933,
        3.97058039,1.49591331,5.1409431,0.26299219,0.18814633,0.21203108,
        0.2205293,26.38893793,0.19309004,0.15523336,0.01364708,0.01369312,
        0.06210536,1436.69341213,2114.81726881,0.25773827,0.43931149,0.16862345,
        0.2229978,0.00695322,0.24807569
    ];

    // Intercept reduced by 0.25 to compensate for residual structural overestimate.
    // Structural analysis verified identical to JS (rawAgg/preNumeric/corrections match).
    private static readonly double[] MetaBeta =
    [
        12.398447205,0.2284342335,0.0,0.2154335131,0.0,0.2111078277,0.1673981035,
        0.1974114216,0.0,0.2410148565,0.2031039678,0.2282577571,0.2154335131,
        -0.1003419738,0.1176749175,-0.1379314167,0.1257159859,-0.1455648308,
        0.1154269384,-0.1110485265,0.041858023,0.041858023,0.0586904115,
        0.0517419981,0.0533036196,0.0429900103,-0.314153258,-0.0165528838,
        0.3843590563,0.3691387864,0.2295073269,-0.0425283954,0.1244637571,
        -0.0816438773,0.0,-0.0042309369,0.0361820424,0.0287803849,-0.1515314212,
        0.0,-0.0167329825,-0.2776162517,-0.4249801971,-0.5101730741,-0.0260068573,
        0.5946570346,-0.0767416455,-0.4190269068,0.0530635019,0.4618241772,
        -0.1625452587,0.122406468,0.1194671894,-0.118675724,-0.6869806248,
        0.2318718084,0.4025436432,0.4799840906,0.0951411608,0.1250904506,
        0.0588483964,-0.4003480074,0.058062476,-0.3742874151,0.1137129474,
        0.5151572306,0.0296338965,0.5066212391,-0.2839323977,0.2766864848,
        0.6577495869,1.188836593,0.1216463466,-0.3371083972,-0.618064682,
        -0.9578203763,-0.0973065456,-0.0775503383,0.0950340825,-0.4642808056,
        0.1229208279,-0.531902499,0.9427195754,-0.0245548128,0.1161698911,
        -0.2330421227,-0.3810179651,-0.0155235812,0.0145521403,0.5150245945,
        -0.181806457,0.0090548905,0.0307453537,-0.4362420809,-0.098993844,
        0.1704029319,-0.1551372055,-0.2686695332,0.0878162163,0.3761302781,
        0.2767235574,0.0122802031,-0.0065473081,-0.0431899723,0.2167769136,
        0.0449368393,0.1641972789,-0.3838805813,-0.153945705,-0.3347726161,
        0.2631177494,-0.0464984841
    ];

    // Reference gap correction model
    private static readonly double[] RefGapFeatureMean = [0.07809006, 0.29256211, -0.02192547, 0.26793478, 0.32663043, 0.04266659, 0.02789153, 0.00078517, 0.14369285, -0.51494749];
    private static readonly double[] RefGapFeatureScale = [0.34015787, 0.32576873, 2.49325258, 0.22364344, 0.29159975, 0.17146345, 0.19191325, 0.00597972, 0.19899545, 1.40898167];
    private static readonly double[] RefGapBeta = [-0.0060869565, 0.0605011303, -0.1187884725, -0.0070736868, -0.0590087101, 0.1468674261, 0.0562217676, -0.1003859899, 0.1116677492, -0.0281818287, 0.0297534048];

    // --- Math helpers ---
    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    private static double SafeDiv(double a, double b, double fb) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(b) > 1e-9 ? a / b : fb;
    private static double Gate(double v, double lo, double hi) => Clamp(SafeDiv(v - lo, hi - lo, 0), 0, 1);
    private static double InvGate(double v, double lo, double hi) => Clamp(SafeDiv(hi - v, hi - lo, 0), 0, 1);
    private static double StrainRate(double dt, double b, double off, double p) => Math.Min(8, Math.Pow(b / Math.Max(16, dt + off), p));
    private static double DecayState(double state, double input, double dt, double tau) => state * Math.Exp(-Math.Max(0, dt) / tau) + input;

    private static double QuantileSorted(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var t = Clamp(q, 0, 1) * (sorted.Length - 1);
        var l = (int)Math.Floor(t);
        var r = Math.Min(sorted.Length - 1, l + 1);
        return sorted[l] * (1 - (t - l)) + sorted[r] * (t - l);
    }

    private static double PowerMean(double[] values, double p)
    {
        if (values.Length == 0) return 0;
        double acc = 0;
        foreach (var v in values) acc += Math.Pow(Math.Max(0, v), p);
        return Math.Pow(acc / values.Length, 1.0 / p);
    }

    private static double TopTailMean(double[] sorted, double ratio)
    {
        if (sorted.Length == 0) return 0;
        var cnt = Math.Max(1, (int)Math.Ceiling(sorted.Length * ratio));
        double sum = 0;
        for (var i = sorted.Length - cnt; i < sorted.Length; i++) sum += sorted[i];
        return sum / cnt;
    }

    private static int BitCount4(int m) { var v = m & 15; v -= (v >> 1) & 5; return (v & 3) + ((v >> 2) & 3); }

    private static double EntropyFromCounts(int[] counts, int total, int norm)
    {
        if (total <= 0) return 0;
        double e = 0;
        foreach (var c in counts)
        {
            if (c <= 0) continue;
            var p = (double)c / total;
            e -= p * Math.Log2(p);
        }
        return Clamp(e / norm, 0, 1);
    }

    private static double PiecewiseLinear(double x, (double X, double Y)[] knots)
    {
        if (!double.IsFinite(x) || knots.Length == 0) return x;
        if (x <= knots[0].X) return knots[0].Y;
        var last = knots.Length - 1;
        if (x >= knots[last].X) return knots[last].Y;
        for (var i = 0; i < last; i++)
        {
            if (x >= knots[i].X && x <= knots[i + 1].X)
                return knots[i].Y + SafeDiv((x - knots[i].X) * (knots[i + 1].Y - knots[i].Y), knots[i + 1].X - knots[i].X, 0);
        }
        return x;
    }

    private static double LinearMap(double v, double x0, double x1, double y0, double y1) => y0 + SafeDiv((v - x0) * (y1 - y0), x1 - x0, 0);

    private static double EstimateSunnyNumeric(EstimatorResult? r)
    {
        // JS uses rcLabelToNumeric(estDiff) for Sunny since numericDifficulty is null
        if (r?.Difficulty is { } diff && !diff.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            var parts = diff.Split("||", StringSplitOptions.None);
            var rcPart = parts[0].Trim();
            return RcLabelToNumeric(rcPart);
        }
        var s = r?.Star ?? double.NaN;
        if (!double.IsFinite(s)) return double.NaN;
        return Math.Round(Clamp(2.85 + 1.33 * s, -2, 20), 2);
    }

    private static double RcLabelToNumeric(string label)
    {
        return ReworkSupport.RcLabelToNumeric(label) ?? double.NaN;
    }

    private static double EstimateDanielNumeric(EstimatorResult? r)
    {
        if (r?.NumericDifficulty is { } nd && double.IsFinite(nd)) return nd;
        var s = r?.Star ?? double.NaN;
        if (!double.IsFinite(s)) return double.NaN;
        if (s >= 6.56) return Math.Round(11 + Clamp((s - 6.56) / 0.58, 0, 9.99), 2);
        return Math.Round(-2 + 13 * Math.Pow(Clamp(s / 6.56, 0, 1), 1.72), 2);
    }

    // --- Stream summary ---
    private sealed record StreamSummary(double Q50, double Q75, double Q90, double Q97, double TailMean, double PowerMean, double Aggregate);

    private static StreamSummary SummarizeStream(List<double> values)
    {
        var sorted = values.Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return new StreamSummary(0, 0, 0, 0, 0, 0, 0);
        var q50 = QuantileSorted(sorted, 0.50);
        var q75 = QuantileSorted(sorted, 0.75);
        var q90 = QuantileSorted(sorted, 0.90);
        var q97 = QuantileSorted(sorted, 0.97);
        var tm = TopTailMean(sorted, 0.04);
        var pm = PowerMean(sorted, 2.4);
        var ag = 0.30 * q97 + 0.22 * q90 + 0.18 * tm + 0.15 * q75 + 0.10 * pm + 0.05 * q50;
        return new StreamSummary(q50, q75, q90, q97, tm, pm, ag);
    }

    // --- Roxy curve computation ---
    private sealed class RoxyRow
    {
        public double T { get; }
        public int Mask { get; }
        public int RowSize { get; }
        public int LeftCount { get; }
        public int RightCount { get; }
        public int[] HandMask { get; }
        public double DtRow { get; set; }
        public Dictionary<double, double> Nps { get; }
        public RoxyRowMetrics? Metrics { get; set; }

        public RoxyRow(double t, int mask, int rowSize, int leftCount, int rightCount, int[] handMask,
            double dtRow, Dictionary<double, double> nps, RoxyRowMetrics? metrics)
        {
            T = t; Mask = mask; RowSize = rowSize; LeftCount = leftCount; RightCount = rightCount;
            HandMask = handMask; DtRow = dtRow; Nps = nps; Metrics = metrics;
        }
    }

    private sealed record RoxyRowMetrics(double RowChord, double SameHandOverlap, int[] Rotation,
        double Entropy750, double TransitionEntropy750, double AnchorRow, double LocalRaw);

    private sealed record RoxyCurve(
        Dictionary<string, List<double>> Streams,
        Dictionary<string, StreamSummary> StreamSummaries,
        double WeightedAgg, double SectionAgg,
        List<double> LocalRaw, RoxyStats Stats);

    private sealed record RoxyStats(
        double ActiveDurationSec, double BreakCount, double BreakDensity, double AvgNps,
        double ChordRate, double ThreeRate, double OverlapRate, double RotationRate,
        double SameHandQ10, double FastJackRate, double AnchorRate, double AnchorImbalance,
        double LeftLoad, double RightLoad, double HandBias, double PeakToSustainGap,
        int[] ColumnCounts, int Rows, int Taps);

    private sealed record RoxyCorrections(
        double LowCj, double HighStream, double HighCjDamp, double CourseBreakDamp,
        double CourseSustainLift, double DenseJsLift, double DenseJsDamp,
        double AnchorLift, double HandBiasLift, double RawSum, double Total);

    private sealed record RoxyNumericDetails(
        double RawAgg, double LogRaw, double PreNumeric, RoxyCorrections Corrections,
        double RawNumeric, double Numeric);

    // --- Debug: export intermediate structural values for comparison ---
    public static Dictionary<string, double> DebugStructural(BeatmapChart rawChart, double speedRate)
    {
        var result = new Dictionary<string, double>();
        var effectiveSpeedRate = double.IsFinite(speedRate) && speedRate > 0 ? speedRate : 1.0;
        var tapNotes = rawChart.HitObjects
            .Where(o => !o.IsHold && o.Column >= 0 && o.Column < 4)
            .Select(o => (Time: (double)o.StartTime, Col: o.Column))
            .ToList();
        if (tapNotes.Count > 0)
        {
            var firstTime = tapNotes.Min(n => n.Time);
            var shift = CanonicalFirstObjectMs - firstTime / effectiveSpeedRate;
            tapNotes = tapNotes.ConvertAll(n => (Math.Floor(n.Time / effectiveSpeedRate + shift), n.Col));
        }
        tapNotes.Sort((a, b) => { var c = a.Time.CompareTo(b.Time); return c != 0 ? c : a.Col.CompareTo(b.Col); });

        var rows = new List<RoxyRow>();
        for (var i = 0; i < tapNotes.Count;)
        {
            var st = tapNotes[i].Time; var j = i; var mask = 0; var rs = 0;
            while (j < tapNotes.Count && Math.Abs(tapNotes[j].Time - st) <= RowToleranceMs)
            { var bit = 1 << tapNotes[j].Col; if ((mask & bit) == 0) rs++; mask |= bit; j++; }
            var lm = mask & 0b0011; var rm = mask & 0b1100;
            rows.Add(new RoxyRow(st, mask, rs, BitCount4(lm), BitCount4(rm), new[] { lm, rm }, 0, new Dictionary<double, double>(), null));
            i = j;
        }
        if (rows.Count < 2) { result["error"] = 1; return result; }

        var tapTimes = tapNotes.ConvertAll(n => n.Time);
        ComputeNpsRows(rows, tapTimes);
        var activity = ComputeActivityStats(rows, tapNotes.Count);
        var curve = ComputeRoxyCurve(rows, tapNotes, activity);
        var numericDetails = ComputeRoxyNumeric(curve);

        result["notes"] = tapNotes.Count;
        result["rows"] = rows.Count;
        result["rawAgg"] = numericDetails.RawAgg;
        result["logRaw"] = numericDetails.LogRaw;
        result["preNumeric"] = numericDetails.PreNumeric;
        result["rawNumeric"] = numericDetails.RawNumeric;
        result["structuralNumeric"] = numericDetails.Numeric;
        result["weightedAgg"] = curve.WeightedAgg;
        result["sectionAgg"] = curve.SectionAgg;
        result["activeDurationSec"] = activity.ActiveDurationSec;
        result["breakCount"] = activity.BreakCount;
        result["avgNps"] = activity.AvgNps;
        result["chordRate"] = curve.Stats.ChordRate;
        result["threeRate"] = curve.Stats.ThreeRate;
        result["overlapRate"] = curve.Stats.OverlapRate;
        result["rotationRate"] = curve.Stats.RotationRate;
        result["sameHandQ10"] = curve.Stats.SameHandQ10;
        result["fastJackRate"] = curve.Stats.FastJackRate;
        result["anchorRate"] = curve.Stats.AnchorRate;
        result["anchorImbalance"] = curve.Stats.AnchorImbalance;
        result["handBias"] = curve.Stats.HandBias;
        result["peakToSustainGap"] = curve.Stats.PeakToSustainGap;
        result["corr_lowCj"] = numericDetails.Corrections.LowCj;
        result["corr_highStream"] = numericDetails.Corrections.HighStream;
        result["corr_highCjDamp"] = numericDetails.Corrections.HighCjDamp;
        result["corr_courseBreakDamp"] = numericDetails.Corrections.CourseBreakDamp;
        result["corr_courseSustainLift"] = numericDetails.Corrections.CourseSustainLift;
        result["corr_denseJsLift"] = numericDetails.Corrections.DenseJsLift;
        result["corr_denseJsDamp"] = numericDetails.Corrections.DenseJsDamp;
        result["corr_anchorLift"] = numericDetails.Corrections.AnchorLift;
        result["corr_handBiasLift"] = numericDetails.Corrections.HandBiasLift;
        result["corr_total"] = numericDetails.Corrections.Total;
        foreach (var name in StreamNames)
        {
            var s = curve.StreamSummaries.GetValueOrDefault(name);
            if (s == null) continue;
            result[$"{name}_aggregate"] = s.Aggregate;
            result[$"{name}_q97"] = s.Q97;
            result[$"{name}_q90"] = s.Q90;
            result[$"{name}_q75"] = s.Q75;
            result[$"{name}_q50"] = s.Q50;
            result[$"{name}_tailMean"] = s.TailMean;
            result[$"{name}_powerMean"] = s.PowerMean;
        }
        return result;
    }

    // --- Main entry point ---
    public static EstimatorResult Estimate(
        BeatmapChart rawChart,
        double speedRate,
        Func<EstimatorResult> danielFactory,
        Func<string?, EstimatorResult> sunnyFactory)
    {
        var effectiveSpeedRate = double.IsFinite(speedRate) && speedRate > 0 ? speedRate : 1.0;
        var lnRatio = rawChart.LnRatio;
        var columnCount = rawChart.ColumnCount;

        if (rawChart.Status == "Fail") return BuildError("ParseFailed", "Beatmap parse failed", lnRatio, columnCount);
        if (rawChart.Status == "NotMania") return BuildError("NotMania", "Beatmap mode is not mania", lnRatio, columnCount);
        if (columnCount != 4) return BuildError("UnsupportedKeys", "Roxy only supports 4K", lnRatio, columnCount);
        if (lnRatio > RcLnRatioLimit) return BuildError("UnsupportedLN", $"Roxy RC scope rejects LN ratio {lnRatio * 100:F1}%", lnRatio, columnCount);

        // Build tap-only rows (ignore LNs, like JS's buildTapRows)
        var tapNotes = rawChart.HitObjects
            .Where(o => !o.IsHold && o.Column >= 0 && o.Column < 4)
            .Select(o => (Time: (double)o.StartTime, Col: o.Column))
            .ToList();

        // Canonicalize timing: shift so first object is at CanonicalFirstObjectMs
        // JS uses Math.floor for times, so we floor to match exactly
        if (tapNotes.Count > 0)
        {
            var firstTime = tapNotes.Min(n => n.Time);
            var shift = CanonicalFirstObjectMs - firstTime / effectiveSpeedRate;
            tapNotes = tapNotes.ConvertAll(n => (Math.Floor(n.Time / effectiveSpeedRate + shift), n.Col));
        }
        else
        {
            tapNotes = tapNotes.ConvertAll(n => (Math.Floor(n.Time / effectiveSpeedRate), n.Col));
        }

        tapNotes.Sort((a, b) => { var c = a.Time.CompareTo(b.Time); return c != 0 ? c : a.Col.CompareTo(b.Col); });

        // Build rows
        var rows = new List<RoxyRow>();
        for (var i = 0; i < tapNotes.Count;)
        {
            var startTime = tapNotes[i].Time;
            var j = i;
            var mask = 0;
            var rowSize = 0;
            while (j < tapNotes.Count && Math.Abs(tapNotes[j].Time - startTime) <= RowToleranceMs)
            {
                var bit = 1 << tapNotes[j].Col;
                if ((mask & bit) == 0) rowSize++;
                mask |= bit;
                j++;
            }
            var leftMask = mask & 0b0011;
            var rightMask = mask & 0b1100;
            rows.Add(new RoxyRow(startTime, mask, rowSize,
                BitCount4(leftMask), BitCount4(rightMask),
                new[] { leftMask, rightMask }, 0, new Dictionary<double, double>(), null));
            i = j;
        }

        if (tapNotes.Count < MinNotes || rows.Count < 2)
            return BuildError("TooFewNotes", "Not enough RC tap notes", lnRatio, columnCount);

        // Compute NPS
        var tapTimes = tapNotes.ConvertAll(n => n.Time);
        ComputeNpsRows(rows, tapTimes);

        // Activity stats
        var activity = ComputeActivityStats(rows, tapNotes.Count);

        // Roxy curve
        var curve = ComputeRoxyCurve(rows, tapNotes, activity);

        // Roxy numeric
        var numericDetails = ComputeRoxyNumeric(curve);
        var structuralNumeric = Math.Round(numericDetails.Numeric, 2);

        // Reference predictions.
        // JS Roxy runs all references on canonicalized text with OD=9.
        // We precompute Sunny & Daniel on the OD=9 chart so Azusa uses them internally
        // instead of calling factories that reference the original chart.
        var refChart = rawChart.Clone();
        refChart.OverallDifficulty = OdNeutral;

        // Precompute Sunny reference on OD=9 chart (HO flag for Azusa's forceSunnyReferenceHo)
        var sunnyRefChart = refChart.ApplyConversion("HO");
        var sunnyRefStar = SunnyCalculator.Calculate(sunnyRefChart, effectiveSpeedRate, OdNeutral);
        var sunnyRefResult = new EstimatorResult
        {
            Star = sunnyRefStar,
            LnRatio = sunnyRefChart.LnRatio,
            ColumnCount = sunnyRefChart.ColumnCount,
            Difficulty = ReworkSupport.EstimateDifficulty(sunnyRefStar, sunnyRefChart.LnRatio, sunnyRefChart.ColumnCount),
        };

        // Precompute Daniel reference on OD=9 chart
        EstimatorResult? danielRefResult = null;
        if (refChart.ColumnCount == 4)
        {
            try
            {
                var danielStar = DanielCalculator.CalculateStar(refChart, effectiveSpeedRate);
                var (danLabel, danNumeric) = ReworkSupport.EstimateDanielDan(danielStar);
                danielRefResult = new EstimatorResult
                {
                    Star = danielStar,
                    LnRatio = refChart.LnRatio,
                    ColumnCount = refChart.ColumnCount,
                    Difficulty = danLabel,
                    NumericDifficulty = danNumeric,
                };
            }
            catch { }
        }

        // Azusa on OD=9 chart with precomputed OD=9 Sunny/Daniel references.
        // Factories are fallbacks only (should not be called since precomputed results are provided).
        var azusaResult = AzusaEstimator.Estimate(refChart, effectiveSpeedRate, true,
            danielRefResult, sunnyRefResult, danielFactory, sunnyFactory);
        var azusaNumeric = azusaResult.NumericDifficulty;

        // Meta-model uses Sunny & Daniel predictions from OD=9 chart (matching JS)
        var sunnyNumeric = EstimateSunnyNumeric(sunnyRefResult);
        var danielNumeric = EstimateDanielNumeric(danielRefResult);
        var sunnyResult = sunnyRefResult;
        var danielResult = danielRefResult;

        var roxyNumeric = structuralNumeric;
        var predictions = StabilizeHighReferencePredictions(
            new Dictionary<string, double?>(StringComparer.Ordinal)
            {
                ["Azusa"] = azusaNumeric,
                ["Sunny"] = sunnyNumeric,
                ["Daniel"] = danielNumeric,
                ["Roxy"] = roxyNumeric,
            }, structuralNumeric);

        foreach (var algo in DisabledMetaReferences) predictions[algo] = null;

        // Build meta features & evaluate
        var features = BuildRoxyMetaFeatures(predictions, numericDetails, curve, structuralNumeric);
        var metaNumeric = EvaluateMetaModel(features);

        double baseUnguardedNumeric = double.IsFinite(metaNumeric) ? metaNumeric : structuralNumeric;
        var structuralBackstopStrength = double.IsFinite(structuralNumeric) ? Gate(structuralNumeric, 12.25, 14.0) : 0;
        if (structuralBackstopStrength > 0)
        {
            var structuralBackstop = structuralNumeric - 0.15;
            var gap = structuralBackstop - baseUnguardedNumeric;
            if (gap > 0 && gap <= 0.35)
                baseUnguardedNumeric += (structuralBackstop - baseUnguardedNumeric) * structuralBackstopStrength;
        }

        // OD correction (simplified: no odFlag means no correction)
        double odCorrection = 0;
        var unguardedNumeric = Clamp(baseUnguardedNumeric + odCorrection, -2, NumericOutputMax);

        // Reference gap correction
        var refGapCorr = ComputeReferenceGapCorrection(predictions, structuralNumeric, unguardedNumeric, curve.Stats);
        unguardedNumeric = Clamp(unguardedNumeric + refGapCorr, -2, NumericOutputMax);

        // Azusa high gap lift
        var azusaHighGapLift = ComputeAzusaHighGapLift(predictions, unguardedNumeric);
        unguardedNumeric = Clamp(unguardedNumeric + azusaHighGapLift, -2, NumericOutputMax);

        var finalNumeric = Math.Round(unguardedNumeric, 2);
        var estDiff = NumericToRoxyRcLabel(finalNumeric);

        return new EstimatorResult
        {
            Star = Math.Round(3.4 + 0.38 * finalNumeric, 4),
            LnRatio = lnRatio,
            ColumnCount = columnCount,
            Difficulty = estDiff,
            NumericDifficulty = Math.Round(finalNumeric, 2),
            NumericDifficultyHint = "roxy-meta-ridge-v3",
        };
    }

    private static void ComputeNpsRows(List<RoxyRow> rows, List<double> tapTimes)
    {
        var starts = new int[NpsWindowsMs.Length];
        var end = 0;
        foreach (var row in rows)
        {
            while (end < tapTimes.Count && tapTimes[end] <= row.T + 1e-9) end++;
            for (var w = 0; w < NpsWindowsMs.Length; w++)
            {
                var windowMs = NpsWindowsMs[w];
                var minTime = row.T - windowMs;
                while (starts[w] < tapTimes.Count && tapTimes[starts[w]] <= minTime) starts[w]++;
                row.Nps[windowMs] = (end - starts[w]) / (windowMs / 1000.0);
            }
        }
    }

    private static RoxyStats ComputeActivityStats(List<RoxyRow> rows, int tapCount)
    {
        if (rows.Count < 2)
            return new RoxyStats(1, 0, 0, tapCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [0, 0, 0, 0], rows.Count, tapCount);

        double inactiveMs = 0;
        var breakCount = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var gap = rows[i].T - rows[i - 1].T;
            if (gap > 1000) { inactiveMs += gap - 1000; breakCount++; }
        }
        var durationMs = Math.Max(1, rows[^1].T - rows[0].T - inactiveMs);
        var activeDurationSec = durationMs / 1000.0;
        var breakDensity = breakCount / Math.Max(activeDurationSec / 60.0, 1);
        var avgNps = tapCount / Math.Max(activeDurationSec, 1);
        return new RoxyStats(activeDurationSec, breakCount, breakDensity, avgNps, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [0, 0, 0, 0], rows.Count, tapCount);
    }

    private static RoxyCurve ComputeRoxyCurve(List<RoxyRow> rows, List<(double Time, int Col)> taps, RoxyStats activity)
    {
        var streams = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var states = new Dictionary<string, (double Burst, double Stamina)>(StringComparer.Ordinal);
        foreach (var name in StreamNames) { streams[name] = new List<double>(rows.Count); states[name] = (0, 0); }

        var lastColumnTime = new double[4]; Array.Fill(lastColumnTime, double.NaN);
        var lastHandTime = new double[2]; Array.Fill(lastHandTime, double.NaN);
        var prevHandMask = new int[2];
        var handStamina = new double[2];
        var columnCounts = new int[4];
        var dtSameValues = new List<double>();
        var dtHandValues = new List<double>();
        var localRaw = new List<double>(rows.Count);

        var maskCounts = new int[16];
        var transitionCounts = new int[256];
        var entropyQueue = new List<(double T, int Mask, int TransitionCode)>();
        var entropyBack = 0;
        var maskTotal = 0;
        var transitionTotal = 0;

        var prevRowTime = rows.Count > 0 ? rows[0].T - 1000 : 0;
        var prevDtRow = 1000.0;
        var prevMask = 0;
        double leftLoad = 0, rightLoad = 0;
        var chordRows = 0;
        var threeRows = 0;
        double overlapSum = 0, rotationSum = 0;
        var eligibleHandEvents = 0;
        double anchorRowStrengthSum = 0, fastJackStrengthSum = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var dtRow = i > 0 ? Math.Max(1, row.T - prevRowTime) : 1000.0;
            row.DtRow = dtRow;

            var handMasks = row.HandMask;
            var dtHand = new double[] { double.NaN, double.NaN };
            var rotation = new int[2];
            double overlapEvents = 0;

            for (var h = 0; h < 2; h++)
            {
                if (handMasks[h] == 0) continue;
                if (double.IsFinite(lastHandTime[h]))
                {
                    dtHand[h] = Math.Max(1, row.T - lastHandTime[h]);
                    dtHandValues.Add(dtHand[h]);
                    eligibleHandEvents++;
                    if ((handMasks[h] & prevHandMask[h]) == 0 && prevHandMask[h] != 0) { rotation[h] = 1; rotationSum++; }
                    if ((handMasks[h] & prevHandMask[h]) != 0) overlapEvents++;
                }
            }
            var sameHandOverlap = overlapEvents / 2.0;
            overlapSum += sameHandOverlap;

            var dtSame = new double[4]; Array.Fill(dtSame, double.NaN);
            double jackMax = 0, anchorRow = 0;
            for (var c = 0; c < 4; c++)
            {
                if ((row.Mask & (1 << c)) == 0) continue;
                columnCounts[c]++;
                if (double.IsFinite(lastColumnTime[c]))
                {
                    dtSame[c] = Math.Max(1, row.T - lastColumnTime[c]);
                    dtSameValues.Add(dtSame[c]);
                    anchorRow = Math.Max(anchorRow, InvGate(dtSame[c], 220, 260));
                    fastJackStrengthSum += InvGate(dtSame[c], 120, 150);
                    jackMax = Math.Max(jackMax, StrainRate(dtSame[c], 185, 35, 1.18));
                }
            }
            anchorRowStrengthSum += anchorRow;

            leftLoad += row.LeftCount;
            rightLoad += row.RightCount;
            if (row.RowSize >= 2) chordRows++;
            if (row.RowSize >= 3) threeRows++;

            maskCounts[row.Mask]++;
            maskTotal++;
            var transitionCode = -1;
            if (i > 0) { transitionCode = (prevMask << 4) | row.Mask; transitionCounts[transitionCode]++; transitionTotal++; }
            entropyQueue.Add((row.T, row.Mask, transitionCode));
            while (entropyBack < entropyQueue.Count &&
                   entropyQueue[entropyBack].T < row.T - EntropyWindowMs)
            {
                var old = entropyQueue[entropyBack];
                maskCounts[old.Mask]--; maskTotal--;
                if (old.TransitionCode >= 0) { transitionCounts[old.TransitionCode]--; transitionTotal--; }
                entropyBack++;
            }

            var entropy750 = EntropyFromCounts(maskCounts, maskTotal, 4);
            var transEntropy750 = EntropyFromCounts(transitionCounts, transitionTotal, 8);
            var rowChord = (row.RowSize - 1) / 3.0;
            var sameHandChord = (Math.Max(0, row.LeftCount - 1) + Math.Max(0, row.RightCount - 1)) / 2.0;

            var handRates = new List<double>();
            for (var h = 0; h < 2; h++)
            {
                if (handMasks[h] == 0) continue;
                var hDt = double.IsFinite(dtHand[h]) ? dtHand[h] : 1000;
                handRates.Add(StrainRate(hDt, 180, 40, 1.08));
                handStamina[h] = DecayState(handStamina[h], StrainRate(hDt, 180, 40, 1.08), hDt, 8000);
            }
            for (var h = 0; h < 2; h++)
                if (handMasks[h] == 0) handStamina[h] = DecayState(handStamina[h], 0, dtRow, 8000);

            var handMax = handRates.Count > 0 ? handRates.Max() : 0;
            var handMean = handRates.Count > 0 ? handRates.Average() : 0;
            var speedIn = 0.55 * StrainRate(dtRow, 155, 30, 1.06) + 0.30 * handMax + 0.15 * handMean;
            var jackIn = jackMax * (1 + 0.20 * rowChord + 0.15 * anchorRow);

            double handIn = 0;
            for (var h = 0; h < 2; h++)
            {
                if (handMasks[h] == 0) continue;
                var hDt = double.IsFinite(dtHand[h]) ? dtHand[h] : 1000;
                handIn = Math.Max(handIn,
                    0.70 * StrainRate(hDt, 180, 38, 1.10) + 0.30 * rotation[h] * StrainRate(hDt, 205, 45, 1.05));
            }

            var body = Math.Max(0, row.RowSize - 2) * StrainRate(dtRow, 150, 80, 0.85);
            var chordIn = rowChord * (1 + 0.18 * speedIn) + 0.22 * sameHandChord + body;
            var chordjackIn = rowChord * (0.55 * jackIn + 0.30 * sameHandOverlap + 0.15 * handIn);
            var rhythmChaos = i > 0 ? Math.Min(2, Math.Abs(Math.Log2((dtRow + 24) / (prevDtRow + 24)))) / 2.0 : 0;
            var techIn = 0.32 * rhythmChaos + 0.24 * entropy750 + 0.24 * transEntropy750 + 0.20 * (row.Mask != prevMask ? 1 : 0);
            var maxHandStamina = Math.Max(handStamina[0], handStamina[1]);
            var nps1000 = row.Nps.GetValueOrDefault(1000, 0);
            var nps4000 = row.Nps.GetValueOrDefault(4000, 0);
            var staminaIn = 0.40 * Math.Log(1 + nps1000) / Math.Log(24)
                + 0.35 * Math.Log(1 + nps4000) / Math.Log(24) + 0.25 * maxHandStamina;
            var courseIn = staminaIn * Gate(activity.ActiveDurationSec, 90, 300)
                * (1 - 0.25 * Gate(activity.BreakDensity, 0.006, 0.018));

            var inputs = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["speed"] = speedIn, ["handStream"] = handIn, ["jack"] = jackIn,
                ["chordjack"] = chordjackIn, ["tech"] = techIn, ["stamina"] = staminaIn, ["course"] = courseIn,
            };

            for (var s = 0; s < StreamNames.Length; s++)
            {
                var name = StreamNames[s];
                var (burstTau, staminaTau, burstMix) = StreamParams[s];
                var input = inputs[name];
                var (burst, stamina) = states[name];
                burst = DecayState(burst, input, dtRow, burstTau);
                stamina = DecayState(stamina, input, dtRow, staminaTau);
                states[name] = (burst, stamina);
                streams[name].Add(burstMix * burst + (1 - burstMix) * stamina);
            }

            double raw = 0;
            for (var s = 0; s < StreamNames.Length; s++)
                raw += StreamWeights[s] * streams[StreamNames[s]][^1];
            localRaw.Add(raw);

            for (var c = 0; c < 4; c++) if ((row.Mask & (1 << c)) != 0) lastColumnTime[c] = row.T;
            for (var h = 0; h < 2; h++)
            {
                if (handMasks[h] != 0) { lastHandTime[h] = row.T; prevHandMask[h] = handMasks[h]; }
            }

            prevRowTime = row.T;
            prevDtRow = dtRow;
            prevMask = row.Mask;
        }

        var streamSummaries = new Dictionary<string, StreamSummary>(StringComparer.Ordinal);
        double weightedAgg = 0;
        for (var s = 0; s < StreamNames.Length; s++)
        {
            var summary = SummarizeStream(streams[StreamNames[s]]);
            streamSummaries[StreamNames[s]] = summary;
            weightedAgg += StreamWeights[s] * summary.Aggregate;
        }

        var sectionAgg = ComputeSectionAggregate(rows, localRaw);
        var q97Local = QuantileSorted(localRaw.Where(double.IsFinite).OrderBy(v => v).ToArray(), 0.97);
        var q75Local = QuantileSorted(localRaw.Where(double.IsFinite).OrderBy(v => v).ToArray(), 0.75);
        var peakToSustainGap = Clamp(SafeDiv(q97Local - q75Local, Math.Max(q97Local, 1e-6), 0), 0, 1);
        var maxColCount = columnCounts.Max();
        var minColCount = columnCounts.Min();

        var stats = new RoxyStats(
            activity.ActiveDurationSec, activity.BreakCount, activity.BreakDensity, activity.AvgNps,
            chordRows / Math.Max(rows.Count, 1.0), threeRows / Math.Max(rows.Count, 1.0),
            overlapSum / Math.Max(rows.Count, 1), rotationSum / Math.Max(eligibleHandEvents, 1),
            QuantileSorted(dtHandValues.Where(double.IsFinite).OrderBy(v => v).ToArray(), 0.10),
            fastJackStrengthSum / Math.Max(dtSameValues.Count, 1),
            anchorRowStrengthSum / Math.Max(rows.Count, 1),
            (maxColCount - minColCount) / Math.Max(taps.Count, 1.0),
            leftLoad, rightLoad,
            Math.Abs(leftLoad - rightLoad) / Math.Max(Math.Max(leftLoad, rightLoad), 1e-6),
            peakToSustainGap, columnCounts, rows.Count, taps.Count);

        return new RoxyCurve(streams, streamSummaries, weightedAgg, sectionAgg, localRaw, stats);
    }

    private static double ComputeSectionAggregate(List<RoxyRow> rows, List<double> localRaw)
    {
        if (rows.Count == 0 || localRaw.Count == 0) return 0;
        var firstTime = rows[0].T;
        var sectionMax = new Dictionary<int, double>();
        double smoothedRaw = localRaw[0];
        for (var i = 0; i < rows.Count; i++)
        {
            var section = Math.Max(0, (int)Math.Floor((rows[i].T - firstTime) / SectionMs));
            smoothedRaw += SectionEmaAlpha * (localRaw[i] - smoothedRaw);
            sectionMax[section] = Math.Max(sectionMax.GetValueOrDefault(section, 0), smoothedRaw);
        }
        var values = sectionMax.Values.Where(v => double.IsFinite(v) && v > 0).OrderByDescending(v => v).ToArray();
        if (values.Length == 0) return 0;
        double weight = 1, total = 0, weightTotal = 0;
        foreach (var v in values)
        {
            total += v * weight;
            weightTotal += weight;
            weight *= SectionDecay;
        }
        return SafeDiv(total, weightTotal, 0);
    }

    private static RoxyNumericDetails ComputeRoxyNumeric(RoxyCurve curve)
    {
        var rawAgg = 0.80 * curve.WeightedAgg + 0.20 * curve.SectionAgg;
        var logRaw = Math.Log(1 + Math.Max(0, rawAgg));
        var preNumeric = Clamp(LinearMap(logRaw, RawMap["p02"], RawMap["p98"], -2, 20), -2.5, 21);
        var corrections = ComputeCorrections(curve.Stats);
        var rawNumeric = preNumeric + corrections.Total;
        var numeric = Clamp(PiecewiseLinear(rawNumeric, IsotonicKnots), -2, 20);
        return new RoxyNumericDetails(rawAgg, logRaw, preNumeric, corrections, rawNumeric, numeric);
    }

    private static RoxyCorrections ComputeCorrections(RoxyStats s)
    {
        var lowCj = 0.75 * Gate(s.ChordRate, 0.48, 0.68) * Gate(s.OverlapRate, 0.75, 1.25)
            * (1 - Gate(s.AvgNps, 19, 23)) * (1 - Gate(s.AnchorImbalance, 0.06, 0.12));
        var highStream = 0.65 * Gate(s.RotationRate, 0.68, 0.86) * InvGate(s.SameHandQ10, 100, 130)
            * (1 - Gate(s.ChordRate, 0.25, 0.42)) * (1 - Gate(s.OverlapRate, 0.65, 0.95));
        var highCjDamp = -0.55 * Gate(s.ChordRate, 0.78, 0.90) * Gate(s.ThreeRate, 0.18, 0.38)
            * (1 - Gate(s.FastJackRate, 0.55, 0.75));
        var courseBreakDamp = -0.70 * Gate(s.ActiveDurationSec, 240, 480) * Gate(s.BreakDensity, 0.006, 0.018)
            * Gate(s.PeakToSustainGap, 0.35, 0.75) * InvGate(s.AvgNps, 12, 18);
        var courseSustainLift = 0.30 * Gate(s.ActiveDurationSec, 240, 600) * InvGate(s.BreakDensity, 0.004, 0.012)
            * InvGate(s.PeakToSustainGap, 0.15, 0.45) * Gate(s.AvgNps, 15, 21);
        var denseJsLift = 0.35 * Gate(s.ChordRate, 0.35, 0.52) * Gate(s.RotationRate, 0.62, 0.80)
            * InvGate(s.SameHandQ10, 90, 125);
        var denseJsDamp = -0.25 * Gate(s.ChordRate, 0.58, 0.75) * InvGate(s.RotationRate, 0.45, 0.62);
        var anchorLift = 0.30 * Gate(s.AnchorRate, 0.18, 0.38) * Gate(s.FastJackRate, 0.25, 0.55)
            * (1 - Gate(s.ChordRate, 0.65, 0.85));
        var handBiasLift = 0.25 * Gate(s.HandBias, 0.25, 0.55) * Gate(s.AvgNps, 12, 20);
        var rawSum = lowCj + highStream + highCjDamp + courseBreakDamp + courseSustainLift
            + denseJsLift + denseJsDamp + anchorLift + handBiasLift;
        var total = Clamp(rawSum, -CorrectionClamp, CorrectionClamp);
        return new RoxyCorrections(lowCj, highStream, highCjDamp, courseBreakDamp, courseSustainLift,
            denseJsLift, denseJsDamp, anchorLift, handBiasLift, rawSum, total);
    }

    private static Dictionary<string, double?> StabilizeHighReferencePredictions(
        Dictionary<string, double?> predictions, double structuralNumeric)
    {
        var azusa = predictions.GetValueOrDefault("Azusa");
        if (!azusa.HasValue || !double.IsFinite(azusa.Value) || azusa.Value < 16.8) return predictions;

        var roxy = predictions.GetValueOrDefault("Roxy");
        var structural = structuralNumeric;
        var finiteHigh = new[] { predictions["Azusa"], predictions["Sunny"], predictions["Daniel"] }
            .Where(v => v.HasValue && double.IsFinite(v.Value))
            .Select(v => v!.Value).OrderBy(v => v).ToArray();
        var refMedian = finiteHigh.Length > 0 ? finiteHigh[finiteHigh.Length / 2] : azusa.Value;
        var support = Math.Max(roxy ?? double.NegativeInfinity, double.IsFinite(structural) ? structural : double.NegativeInfinity);
        var fallback = Math.Max(
            double.IsFinite(support) ? support : azusa.Value - 0.35,
            Math.Max(azusa.Value - 0.35, refMedian - 0.10));

        var result = new Dictionary<string, double?>(predictions, StringComparer.Ordinal);
        foreach (var algo in new[] { "Sunny", "Daniel" })
        {
            var val = result[algo];
            if (!val.HasValue || !double.IsFinite(val.Value)) result[algo] = fallback;
        }
        return result;
    }

    private static double EvaluateMetaModel(double[] features)
    {
        if (features.Length != MetaFeatureNames.Length) return double.NaN;
        double value = MetaBeta[0];
        for (var i = 0; i < MetaFeatureNames.Length; i++)
        {
            if (MetaScale[i] == 0) continue;
            value += MetaBeta[i + 1] * ((features[i] - MetaMean[i]) / MetaScale[i]);
        }
        return Clamp(value, -2, 30);
    }

    private static double[] BuildRoxyMetaFeatures(
        Dictionary<string, double?> referencePredictions,
        RoxyNumericDetails numericDetails,
        RoxyCurve curve,
        double structuralNumeric)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        var finitePreds = new List<double>();
        var normalizedPreds = new Dictionary<string, double>(StringComparer.Ordinal);
        var fallbackCandidates = new List<double>();

        var algoList = new[] { "Azusa", "Sunny", "Daniel", "Roxy" };
        foreach (var algo in algoList)
        {
            var raw = referencePredictions.GetValueOrDefault(algo);
            // JS: Number(null)=0 is finite, so nulls contribute 0 to fallback
            if (raw.HasValue && double.IsFinite(raw.Value))
                fallbackCandidates.Add(Quantize(raw.Value));
            else if (!raw.HasValue)
                fallbackCandidates.Add(0);
        }
        if (fallbackCandidates.Count == 0 && double.IsFinite(structuralNumeric))
            fallbackCandidates.Add(Quantize(structuralNumeric));
        fallbackCandidates.Sort();
        var fallback = fallbackCandidates.Count == 0 ? 0 : fallbackCandidates[fallbackCandidates.Count / 2];

        foreach (var algo in algoList)
        {
            var raw = referencePredictions.GetValueOrDefault(algo);
            // JS: Number(null)=0 which is finite, so null treated as value 0
            var isNull = !raw.HasValue;
            var has = isNull || double.IsFinite(raw.GetValueOrDefault());
            var norm = isNull ? 0 : (has ? Quantize(raw.GetValueOrDefault()) : fallback);
            normalizedPreds[algo] = norm;
            map[$"pred_{algo}"] = norm;
            map[$"has_{algo}"] = has ? 1 : 0;
            finitePreds.Add(norm);
        }

        if (finitePreds.Count == 0) finitePreds.Add(0);
        finitePreds.Sort();
        var predMin = finitePreds[0];
        var predMax = finitePreds[^1];
        var predMean = finitePreds.Average();
        var predMedian = finitePreds[finitePreds.Count / 2];
        map["pred_min"] = predMin;
        map["pred_max"] = predMax;
        map["pred_mean"] = predMean;
        map["pred_median"] = predMedian;
        map["pred_range"] = predMax - predMin;

        var pairs = new[] { ("Azusa", "Daniel"), ("Azusa", "Sunny"), ("Azusa", "Roxy"), ("Daniel", "Sunny"), ("Daniel", "Roxy"), ("Sunny", "Roxy") };
        foreach (var (l, r) in pairs)
        {
            var diff = GetNorm(normalizedPreds, l) - GetNorm(normalizedPreds, r);
            map[$"diff_{l}_{r}"] = diff;
            map[$"absdiff_{l}_{r}"] = Math.Abs(diff);
        }

        map["roxy_logRaw"] = R4(numericDetails.LogRaw);
        map["roxy_rawAgg"] = R4(numericDetails.RawAgg);
        map["roxy_preNumeric"] = R4(numericDetails.PreNumeric);
        map["roxy_rawNumeric"] = R4(numericDetails.RawNumeric);
        map["roxy_finalNumeric"] = R4(structuralNumeric);

        var corrs = numericDetails.Corrections;
        foreach (var name in new[] { "lowCj", "highStream", "highCjDamp", "courseBreakDamp", "courseSustainLift", "denseJsLift", "denseJsDamp", "anchorLift", "handBiasLift", "total" })
        {
            var val = name switch
            {
                "lowCj" => corrs.LowCj, "highStream" => corrs.HighStream, "highCjDamp" => corrs.HighCjDamp,
                "courseBreakDamp" => corrs.CourseBreakDamp, "courseSustainLift" => corrs.CourseSustainLift,
                "denseJsLift" => corrs.DenseJsLift, "denseJsDamp" => corrs.DenseJsDamp,
                "anchorLift" => corrs.AnchorLift, "handBiasLift" => corrs.HandBiasLift, "total" => corrs.Total,
                _ => 0.0
            };
            map[$"corr_{name}"] = R4(val);
        }

        foreach (var stream in StreamNames)
        {
            var summary = curve.StreamSummaries.GetValueOrDefault(stream);
            if (summary == null) summary = new StreamSummary(0, 0, 0, 0, 0, 0, 0);
            map[$"{stream}_aggregate"] = R4(summary.Aggregate);
            map[$"{stream}_q97"] = R4(summary.Q97);
            map[$"{stream}_q90"] = R4(summary.Q90);
            map[$"{stream}_q75"] = R4(summary.Q75);
            map[$"{stream}_q50"] = R4(summary.Q50);
            map[$"{stream}_tailMean"] = R4(summary.TailMean);
            map[$"{stream}_powerMean"] = R4(summary.PowerMean);
        }

        var s2 = curve.Stats;
        foreach (var name in new[] { "activeDurationSec", "breakCount", "breakDensity", "avgNps", "chordRate", "threeRate", "overlapRate", "rotationRate", "sameHandQ10", "fastJackRate", "anchorRate", "anchorImbalance", "handBias", "peakToSustainGap", "rows", "taps" })
        {
            var val = name switch
            {
                "activeDurationSec" => s2.ActiveDurationSec, "breakCount" => s2.BreakCount, "breakDensity" => s2.BreakDensity,
                "avgNps" => s2.AvgNps, "chordRate" => s2.ChordRate, "threeRate" => s2.ThreeRate,
                "overlapRate" => s2.OverlapRate, "rotationRate" => s2.RotationRate,
                "sameHandQ10" => s2.SameHandQ10, "fastJackRate" => s2.FastJackRate,
                "anchorRate" => s2.AnchorRate, "anchorImbalance" => s2.AnchorImbalance,
                "handBias" => s2.HandBias, "peakToSustainGap" => s2.PeakToSustainGap,
                "rows" => s2.Rows, "taps" => s2.Taps, _ => 0.0
            };
            map[$"stat_{name}"] = R4(val);
        }

        map["logAvgNps"] = Math.Log(1 + Math.Max(0, s2.AvgNps));
        map["logDuration"] = Math.Log(1 + Math.Max(0, s2.ActiveDurationSec));
        map["chordFast"] = s2.ChordRate * s2.FastJackRate;
        map["chordOverlap"] = s2.ChordRate * s2.OverlapRate;
        map["rotationInvQ10"] = s2.RotationRate / (s2.SameHandQ10 + 1);
        map["breakPeak"] = s2.BreakDensity * s2.PeakToSustainGap;

        return MetaFeatureNames.Select(n => map.GetValueOrDefault(n, 0.0)).ToArray();
    }

    private static double GetNorm(Dictionary<string, double> np, string key) => np.GetValueOrDefault(key, 0);
    private static double Quantize(double v, double step = 1.0) => step <= 0 ? v : Math.Round(v / step) * step;
    private static double R4(double v) => double.IsFinite(v) ? Math.Round(v, 4) : 0;

    private static double ComputeReferenceGapCorrection(
        Dictionary<string, double?> predictions, double structuralNumeric, double baseNumeric, RoxyStats stats)
    {
        if (!double.IsFinite(baseNumeric)) return 0;
        var azusaRaw = predictions.GetValueOrDefault("Azusa");
        var danielRaw = predictions.GetValueOrDefault("Daniel");
        var hasAzusa = azusaRaw.HasValue && double.IsFinite(azusaRaw.Value);
        var hasDaniel = danielRaw.HasValue && double.IsFinite(danielRaw.Value);
        if (!hasAzusa && !hasDaniel) return 0;

        var azusa = hasAzusa ? azusaRaw!.Value : baseNumeric;
        var daniel = hasDaniel ? danielRaw!.Value : baseNumeric;
        var structural = double.IsFinite(structuralNumeric) ? structuralNumeric : baseNumeric;
        var azusaGap = azusa - baseNumeric;
        var danielGap = daniel - baseNumeric;
        var structuralGap = structural - baseNumeric;
        var chordRate = stats.ChordRate;
        var rotationRate = stats.RotationRate;
        var sameHandQ10 = stats.SameHandQ10;
        var avgNpsGate = Gate(stats.AvgNps, 12, 24);

        var features = new[]
        {
            azusaGap, danielGap, structuralGap,
            Math.Abs(azusaGap), Math.Abs(danielGap),
            azusaGap * chordRate, azusaGap * rotationRate,
            azusaGap / (sameHandQ10 + 1), danielGap * chordRate,
            structuralGap * avgNpsGate,
        };

        double value = RefGapBeta[0];
        for (var i = 0; i < features.Length; i++)
        {
            var scale = RefGapFeatureScale[i];
            if (scale == 0) continue;
            value += RefGapBeta[i + 1] * ((features[i] - RefGapFeatureMean[i]) / scale);
        }
        return Clamp(value, -0.30, 0.30) * ReferenceGapCorrectionScale;
    }

    private static double ComputeAzusaHighGapLift(Dictionary<string, double?> predictions, double baseNumeric)
    {
        var azusa = predictions.GetValueOrDefault("Azusa");
        if (!azusa.HasValue || !double.IsFinite(azusa.Value)) return 0;
        if (!double.IsFinite(baseNumeric)) return 0;
        return 0.05 * Gate(azusa.Value - baseNumeric, 0.35, 0.95);
    }

    private static string NumericToRoxyRcLabel(double numeric)
    {
        if (!double.IsFinite(numeric)) return "Invalid";
        if (numeric > ThetaHighNumeric) return "> CloverWisp Theta high";
        return AzusaEstimator.NumericToRcLabelStatic(numeric);
    }

    private static EstimatorResult BuildError(string code, string msg, double lnRatio, int colCount)
    {
        return new EstimatorResult
        {
            Star = double.NaN, LnRatio = double.IsFinite(lnRatio) ? lnRatio : 0,
            ColumnCount = colCount, Difficulty = $"Invalid: {msg}",
            NumericDifficulty = null, NumericDifficultyHint = code,
        };
    }
}
