#!/usr/bin/env node

import { readFile } from "node:fs/promises";
import process from "node:process";
import { analyzeBeatmap } from "./index.js";

function parseArgs(argv) {
    const options = {
        inputPath: null,
        pretty: false,
    };

    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (arg === "--input" || arg === "-i") {
            options.inputPath = argv[i + 1] || null;
            i += 1;
            continue;
        }

        if (arg === "--pretty") {
            options.pretty = true;
            continue;
        }
    }

    return options;
}

async function readStdin() {
    const chunks = [];
    for await (const chunk of process.stdin) {
        chunks.push(chunk);
    }
    return Buffer.concat(chunks).toString("utf8");
}

async function loadInputJson(inputPath) {
    if (inputPath) {
        return await readFile(inputPath, "utf8");
    }

    if (process.stdin.isTTY) {
        throw new Error("Provide JSON via --input <file> or stdin.");
    }

    return await readStdin();
}

function writeJson(value, pretty, exitCode = 0) {
    const spacing = pretty ? 2 : 0;
    process.stdout.write(`${JSON.stringify(value, null, spacing)}\n`);
    process.exitCode = exitCode;
}

async function main() {
    const { inputPath, pretty } = parseArgs(process.argv.slice(2));
    const rawInput = await loadInputJson(inputPath);
    const payload = JSON.parse(rawInput);
    const result = await analyzeBeatmap(payload);
    writeJson(result, pretty, 0);
}

main().catch((error) => {
    writeJson({
        error: {
            message: error instanceof Error ? error.message : String(error),
        },
    }, true, 1);
});
