import { PATTERNS_CONFIG } from "../legacy/patterns/config.js";

export const GRAPH_SUPPORTED_KEY_SET = new Set([4, 6, 7]);
export const VIBRO_JACKSPEED_RATIO_THRESHOLD = 0.95;

export function modeTagFromLnRatio(lnRatio) {
    if (!Number.isFinite(lnRatio)) {
        return "Mix";
    }
    if (lnRatio <= 0.15) {
        return "RC";
    }
    if (lnRatio >= 0.9) {
        return "LN";
    }
    return "Mix";
}

function pickNumber(obj, keys) {
    if (!obj || typeof obj !== "object") {
        return null;
    }

    for (const key of keys) {
        const value = Number(obj[key]);
        if (Number.isFinite(value)) {
            return value;
        }
    }

    return null;
}

export function detectVibro(values, threshold = VIBRO_JACKSPEED_RATIO_THRESHOLD) {
    const overall = pickNumber(values, ["Overall", "overall"]);
    const jackSpeed = pickNumber(values, ["JackSpeed", "Jackspeed", "jackSpeed", "jackspeed"]);

    if (!Number.isFinite(overall) || overall <= 0 || !Number.isFinite(jackSpeed)) {
        return false;
    }

    return (jackSpeed / overall) >= threshold;
}

export function mergeDuplicateClusters(clusters) {
    const mergedMap = new Map();

    for (const cluster of clusters || []) {
        const key = cluster?.Pattern;
        if (!mergedMap.has(key)) {
            mergedMap.set(key, {
                Pattern: cluster?.Pattern ?? "-",
                Amount: 0,
                BPM: cluster?.BPM,
                SpecificTypes: new Map(),
            });
        }

        const merged = mergedMap.get(key);
        merged.Amount += Number(cluster?.Amount) || 0;
        merged.BPM = Math.max(Number(merged.BPM) || 0, Number(cluster?.BPM) || 0);

        const specificTypes = Array.isArray(cluster?.SpecificTypes) ? cluster.SpecificTypes : [];
        for (const [name, ratio] of specificTypes) {
            const weighted = (Number(ratio) || 0) * (Number(cluster?.Amount) || 0);
            merged.SpecificTypes.set(name, (merged.SpecificTypes.get(name) || 0) + weighted);
        }
    }

    return [...mergedMap.values()]
        .map((item) => {
            const total = item.Amount > 0 ? item.Amount : 1;
            const normalizedSpecific = [...item.SpecificTypes.entries()]
                .map(([name, weighted]) => [name, weighted / total])
                .sort((a, b) => b[1] - a[1]);
            return {
                Pattern: item.Pattern,
                Amount: item.Amount,
                BPM: item.BPM,
                SpecificTypes: normalizedSpecific,
            };
        });
}

function cloneSpecificTypes(specificTypes) {
    return (Array.isArray(specificTypes) ? specificTypes : [])
        .map((entry) => {
            const name = Array.isArray(entry) ? entry[0] : null;
            const ratio = Array.isArray(entry) ? entry[1] : null;
            return [String(name ?? "-"), Number(ratio) || 0];
        });
}

export function cloneClusters(clusters) {
    return (Array.isArray(clusters) ? clusters : []).map((cluster) => ({
        Pattern: String(cluster?.Pattern ?? "-"),
        Amount: Number(cluster?.Amount) || 0,
        BPM: Number(cluster?.BPM) || 0,
        Importance: Number(cluster?.Importance) || 0,
        SpecificTypes: cloneSpecificTypes(cluster?.SpecificTypes),
    }));
}

export function clonePatternReport(report) {
    if (!report || typeof report !== "object") {
        return null;
    }

    return {
        category: String(report.Category ?? "-"),
        modeTag: String(report.ModeTag ?? "-"),
        lnPercent: Number(report.LNPercent) || 0,
        hbRowRatio: Number(report.HBRowRatio) || 0,
        svAmount: Number(report.SVAmount) || 0,
        duration: Number(report.Duration) || 0,
        clusters: cloneClusters(report.Clusters),
        importantClusters: cloneClusters(report.ImportantClusters),
    };
}

export function applyPatternDebugAdjustments(patternReport, mergedClusters, debugUseAmount) {
    if (!patternReport || typeof patternReport !== "object") {
        return;
    }

    if (!debugUseAmount) {
        return;
    }

    mergedClusters.sort((a, b) => b.Amount - a.Amount);
    if (mergedClusters.length === 0) {
        return;
    }

    const topSpecific = mergedClusters[0]?.SpecificTypes?.[0];
    if (topSpecific && Number(topSpecific[1]) > 0.05) {
        patternReport.Category = topSpecific[0];
        return;
    }

    patternReport.Category = mergedClusters[0].Pattern;
}

export function applySvDetection(patternReport, debugUseSvDetection) {
    if (!patternReport || typeof patternReport !== "object" || !debugUseSvDetection) {
        return false;
    }

    const svAmount = Number(patternReport.SVAmount);
    if (!Number.isFinite(svAmount) || svAmount < PATTERNS_CONFIG.SV_AMOUNT_THRESHOLD) {
        return false;
    }

    patternReport.Category = "SV";
    return true;
}

export function cloneGraph(graph) {
    if (!graph || typeof graph !== "object") {
        return null;
    }

    const times = graph.times && typeof graph.times[Symbol.iterator] === "function"
        ? Array.from(graph.times)
        : [];
    const values = graph.values && typeof graph.values[Symbol.iterator] === "function"
        ? Array.from(graph.values)
        : [];

    return {
        times: times.map((value) => Number(value) || 0),
        values: values.map((value) => Number(value) || 0),
    };
}
