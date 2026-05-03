import {
    normalizeBooleanSetting,
    normalizeEstimatorAlgorithmValue,
    normalizeEtternaVersionValue,
} from "../legacy/parser/settingsParser.js";
import { OsuFileParser } from "../legacy/parser/osuFileParser.js";
import { calculateInterludeStar } from "../legacy/interlude/index.js";
import { analyzePatternFromText } from "../legacy/patterns/service.js";
import { analyzeEtternaFromText, DEFAULT_SCORE_GOAL } from "../legacy/ett/index.js";
import { runSunnyEstimatorFromText } from "../legacy/estimator/sunnyEstimator.js";
import { runDanielEstimatorFromText } from "../legacy/estimator/danielEstimator.js";
import { runAzusaEstimatorFromText } from "../legacy/estimator/azusaEstimator.js";
import {
    applyCompanellaToMixedResult,
    runMixedEstimatorFromText,
} from "../legacy/estimator/mixedEstimator.js";
import { classifyCompanellaDifficulty } from "../legacy/estimator/companellaEstimator.js";
import {
    applyPatternDebugAdjustments,
    applySvDetection,
    cloneGraph,
    clonePatternReport,
    cloneClusters,
    detectVibro,
    GRAPH_SUPPORTED_KEY_SET,
    mergeDuplicateClusters,
    modeTagFromLnRatio,
    VIBRO_JACKSPEED_RATIO_THRESHOLD,
} from "./helpers.js";

const DEFAULT_SETTINGS = Object.freeze({
    speedRate: 1.0,
    odFlag: null,
    cvtFlag: null,
    estimatorAlgorithm: "Mixed",
    azusaSunnyReferenceHo: true,
    etternaVersion: "0.72.3",
    companellaEtternaVersion: "0.74.0",
    vibroDetection: true,
    debugUseAmount: false,
    debugUseSvDetection: false,
    includeGraph: false,
    includePattern: true,
    includeEtterna: true,
    includeInterlude: true,
});

function normalizeRate(value) {
    const numeric = Number(value);
    if (!Number.isFinite(numeric) || numeric <= 0) {
        return DEFAULT_SETTINGS.speedRate;
    }
    return numeric;
}

function normalizeOptionalNumber(value) {
    if (value === null || value === undefined || value === "") {
        return null;
    }

    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : null;
}

function normalizeCvtFlag(value) {
    const normalized = String(value ?? "").trim().toUpperCase();
    return normalized.length > 0 ? normalized : null;
}

function normalizeSettings(settings = {}) {
    return {
        speedRate: normalizeRate(settings.speedRate),
        odFlag: normalizeOptionalNumber(settings.odFlag),
        cvtFlag: normalizeCvtFlag(settings.cvtFlag),
        estimatorAlgorithm: normalizeEstimatorAlgorithmValue(settings.estimatorAlgorithm)
            || DEFAULT_SETTINGS.estimatorAlgorithm,
        azusaSunnyReferenceHo: normalizeBooleanSetting(
            settings.azusaSunnyReferenceHo,
            DEFAULT_SETTINGS.azusaSunnyReferenceHo,
        ),
        etternaVersion: normalizeEtternaVersionValue(settings.etternaVersion)
            || DEFAULT_SETTINGS.etternaVersion,
        companellaEtternaVersion: normalizeEtternaVersionValue(settings.companellaEtternaVersion)
            || DEFAULT_SETTINGS.companellaEtternaVersion,
        vibroDetection: normalizeBooleanSetting(
            settings.vibroDetection,
            DEFAULT_SETTINGS.vibroDetection,
        ),
        debugUseAmount: normalizeBooleanSetting(
            settings.debugUseAmount,
            DEFAULT_SETTINGS.debugUseAmount,
        ),
        debugUseSvDetection: normalizeBooleanSetting(
            settings.debugUseSvDetection,
            DEFAULT_SETTINGS.debugUseSvDetection,
        ),
        includeGraph: normalizeBooleanSetting(
            settings.includeGraph,
            DEFAULT_SETTINGS.includeGraph,
        ),
        includePattern: normalizeBooleanSetting(
            settings.includePattern,
            DEFAULT_SETTINGS.includePattern,
        ),
        includeEtterna: normalizeBooleanSetting(
            settings.includeEtterna,
            DEFAULT_SETTINGS.includeEtterna,
        ),
        includeInterlude: normalizeBooleanSetting(
            settings.includeInterlude,
            DEFAULT_SETTINGS.includeInterlude,
        ),
    };
}

function extractOsuText(input) {
    if (typeof input?.osuText === "string" && input.osuText.trim().length > 0) {
        return input.osuText;
    }

    if (typeof input?.beatmap?.osuText === "string" && input.beatmap.osuText.trim().length > 0) {
        return input.beatmap.osuText;
    }

    throw new Error("Input JSON must provide beatmap osu text in `osuText` or `beatmap.osuText`.");
}

function parseMetadataFromBeatmap(osuText) {
    const parser = new OsuFileParser(osuText);
    parser.process();
    const parsed = parser.getParsedData();

    if (parsed.status === "Fail") {
        throw new Error("Beatmap parse failed.");
    }

    if (parsed.status === "NotMania") {
        throw new Error("Beatmap mode is not mania.");
    }

    return {
        metadata: parsed.metaData || {},
        lnRatio: Number(parsed.lnRatio) || 0,
        columnCount: Number(parsed.columnCount) || 0,
    };
}

function buildEstimatorOptions(settings) {
    return {
        speedRate: settings.speedRate,
        odFlag: settings.odFlag,
        cvtFlag: settings.cvtFlag,
        withGraph: settings.includeGraph,
    };
}

function isValidEstimatorResult(result) {
    return Boolean(result)
        && Number.isFinite(result.star)
        && Number.isFinite(result.numericDifficulty)
        && typeof result.estDiff === "string";
}

function buildEtternaOptions(settings, etternaVersion) {
    return {
        musicRate: settings.speedRate,
        scoreGoal: DEFAULT_SCORE_GOAL,
        cvtFlag: settings.cvtFlag,
        etternaVersion,
    };
}

function pushIssue(issues, scope, error) {
    issues.push({
        scope,
        message: error instanceof Error ? error.message : String(error),
    });
}

function buildEstimatorOutput(rework, algorithmInfo, companellaResult, includeGraph) {
    return {
        requestedAlgorithm: algorithmInfo.requested,
        resolvedAlgorithm: algorithmInfo.resolved,
        star: Number(rework.star) || 0,
        difficulty: String(rework.estDiff ?? "-"),
        numericDifficulty: Number.isFinite(rework.numericDifficulty)
            ? Number(rework.numericDifficulty)
            : null,
        numericDifficultyHint: rework.numericDifficultyHint ?? null,
        graph: includeGraph ? cloneGraph(rework.graph) : null,
        graphSupported: GRAPH_SUPPORTED_KEY_SET.has(Number(rework.columnCount)),
        companella: companellaResult
            ? {
                difficulty: companellaResult.estDiff,
                numericDifficulty: Number.isFinite(companellaResult.numericDifficulty)
                    ? Number(companellaResult.numericDifficulty)
                    : null,
                danLabel: companellaResult.danLabel ?? null,
                variant: companellaResult.variant ?? null,
                confidence: Number.isFinite(companellaResult.confidence)
                    ? Number(companellaResult.confidence)
                    : null,
                rawModelOutput: Number.isFinite(companellaResult.rawModelOutput)
                    ? Number(companellaResult.rawModelOutput)
                    : null,
            }
            : null,
    };
}

export async function analyzeBeatmap(input = {}) {
    const osuText = extractOsuText(input);
    const settings = normalizeSettings(input.settings || {});
    const parsedInfo = parseMetadataFromBeatmap(osuText);
    const issues = [];

    const estimatorOptions = buildEstimatorOptions(settings);
    const azusaOptions = {
        ...estimatorOptions,
        forceSunnyReferenceHo: settings.azusaSunnyReferenceHo,
    };

    let rework = null;
    let requestedAlgorithm = settings.estimatorAlgorithm;
    let resolvedAlgorithm = settings.estimatorAlgorithm;
    let pendingCompanellaEstimate = false;
    let pendingMixedCompanellaContext = null;

    if (requestedAlgorithm === "Daniel") {
        rework = runDanielEstimatorFromText(osuText, estimatorOptions);
    } else if (requestedAlgorithm === "Azusa") {
        rework = runAzusaEstimatorFromText(osuText, azusaOptions);
        if (!isValidEstimatorResult(rework)) {
            rework = runSunnyEstimatorFromText(osuText, estimatorOptions);
            resolvedAlgorithm = "Sunny";
        }
    } else if (requestedAlgorithm === "Companella") {
        rework = runSunnyEstimatorFromText(osuText, estimatorOptions);
        pendingCompanellaEstimate = Number(rework.columnCount) === 4;
    } else if (requestedAlgorithm === "Mixed") {
        rework = runMixedEstimatorFromText(osuText, estimatorOptions);
        pendingMixedCompanellaContext = rework.mixedCompanellaPlan || null;
    } else {
        rework = runSunnyEstimatorFromText(osuText, estimatorOptions);
        requestedAlgorithm = "Sunny";
        resolvedAlgorithm = "Sunny";
    }

    if (!rework || !Number.isFinite(rework.star)) {
        throw new Error("Estimator failed to produce a valid result.");
    }

    const shouldRunPattern = settings.includePattern || settings.debugUseAmount || settings.debugUseSvDetection;
    const shouldRunInterlude = settings.includeInterlude
        || requestedAlgorithm === "Companella"
        || requestedAlgorithm === "Mixed";
    const shouldRunEtterna = settings.includeEtterna
        || settings.vibroDetection
        || requestedAlgorithm === "Companella"
        || requestedAlgorithm === "Mixed";

    let interludeOverall = null;
    let patternResult = null;
    let patternReport = null;
    let mergedPatternClusters = [];
    let etternaResult = null;
    let vibroDetected = false;
    let svDetected = false;
    let companellaResult = null;

    if (shouldRunInterlude) {
        try {
            interludeOverall = await calculateInterludeStar(
                osuText,
                settings.speedRate,
                settings.cvtFlag,
            );
        } catch (error) {
            pushIssue(issues, "interlude", error);
        }
    }

    if (shouldRunPattern) {
        try {
            patternResult = analyzePatternFromText(osuText);
            patternReport = patternResult?.report || null;
            mergedPatternClusters = mergeDuplicateClusters(patternReport?.Clusters || []);
            applyPatternDebugAdjustments(
                patternReport,
                mergedPatternClusters,
                settings.debugUseAmount,
            );
            svDetected = applySvDetection(patternReport, settings.debugUseSvDetection);
        } catch (error) {
            pushIssue(issues, "pattern", error);
        }
    }

    if (shouldRunEtterna) {
        try {
            etternaResult = await analyzeEtternaFromText(
                osuText,
                buildEtternaOptions(settings, settings.etternaVersion),
            );

            const reworkStarValue = Number(rework.star);
            const vibroEligible = Number.isFinite(reworkStarValue) && reworkStarValue > 5.0;
            vibroDetected = settings.vibroDetection
                && vibroEligible
                && detectVibro(
                    etternaResult?.values,
                    VIBRO_JACKSPEED_RATIO_THRESHOLD,
                );
        } catch (error) {
            pushIssue(issues, "etterna", error);
        }
    }

    if (Number(rework.columnCount) === 4 && (pendingCompanellaEstimate || pendingMixedCompanellaContext)) {
        let companellaMsdValues = etternaResult?.values;

        if (settings.companellaEtternaVersion !== settings.etternaVersion) {
            try {
                const forcedCompanellaEtterna = await analyzeEtternaFromText(
                    osuText,
                    buildEtternaOptions(settings, settings.companellaEtternaVersion),
                );
                companellaMsdValues = forcedCompanellaEtterna?.values;
            } catch (error) {
                pushIssue(issues, "companella-etterna", error);
            }
        }

        try {
            companellaResult = await classifyCompanellaDifficulty({
                msdValues: companellaMsdValues,
                interludeStar: interludeOverall,
                sunnyStar: Number(rework.star),
            });

            if (pendingCompanellaEstimate) {
                rework = {
                    ...rework,
                    estDiff: companellaResult.estDiff,
                    numericDifficulty: companellaResult.numericDifficulty,
                    numericDifficultyHint: companellaResult.numericDifficultyHint,
                };
            }

            if (pendingMixedCompanellaContext) {
                rework = applyCompanellaToMixedResult({
                    ...rework,
                    mixedCompanellaPlan: pendingMixedCompanellaContext,
                }, companellaResult);
            }
        } catch (error) {
            pushIssue(issues, "companella", error);
        }
    }

    const fallbackModeTag = modeTagFromLnRatio(Number(rework.lnRatio ?? parsedInfo.lnRatio));
    const resolvedModeTag = patternReport?.ModeTag || fallbackModeTag;

    return {
        metadata: parsedInfo.metadata,
        beatmap: {
            columnCount: Number(rework.columnCount) || parsedInfo.columnCount,
            lnRatio: Number(rework.lnRatio) || parsedInfo.lnRatio,
        },
        classification: {
            fallbackModeTag,
            resolvedModeTag,
            svDetected,
            vibroDetected,
        },
        estimator: buildEstimatorOutput(
            rework,
            {
                requested: requestedAlgorithm,
                resolved: resolvedAlgorithm,
            },
            companellaResult,
            settings.includeGraph,
        ),
        interlude: interludeOverall == null
            ? null
            : {
                overall: Number(interludeOverall) || 0,
            },
        pattern: patternReport
            ? {
                ...clonePatternReport(patternReport),
                mergedTopClusters: cloneClusters(mergedPatternClusters.slice(0, 5)),
            }
            : null,
        etterna: etternaResult
            ? {
                keycount: Number(etternaResult.keycount) || 0,
                requestedVersion: etternaResult.requestedEtternaVersion ?? settings.etternaVersion,
                version: etternaResult.etternaVersion ?? settings.etternaVersion,
                versionFallbackReason: etternaResult.etternaVersionFallbackReason ?? null,
                values: etternaResult.values || {},
            }
            : null,
        issues,
    };
}

