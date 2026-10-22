import assert from 'node:assert/strict';
import { median, pairedChange, validateSnapshot } from './evidence.mjs';
import { analyzeProfile } from './profile-timeline.mjs';

export const scenarios = Object.freeze(['list-wheel', 'list-thumb', 'tree-wheel', 'tree-thumb',
    'idle-overlay-false', 'idle-overlay-true']);
const finite = (value, message) => assert(Number.isFinite(value) && value >= 0, message);
const close = (a, b) => Math.abs(a - b) <= 0.01;

function inventory(size) {
    assert(Array.isArray(size?.files) && size.files.length, 'Missing deployment inventory');
    const seen = new Set();
    return size.files.map(file => {
        assert(typeof file.file === 'string' && !seen.has(file.file), 'Duplicate deployment file');
        seen.add(file.file);
        finite(file.bytes, 'Invalid deployment size');
        return [file.file, file.bytes, file.wasm?.codeSha256 ?? null];
    }).sort((a, b) => a[0].localeCompare(b[0]));
}

/** Validate raw runs rather than trusting transcribed/rounded summary percentages. */
export function validateReport(report) {
    assert.equal(report?.schema, 2, 'Unsupported comparison schema');
    assert.equal(report.visualDifferences, 0, 'Pixel mismatches invalidate performance acceptance');
    assert(typeof report.browser === 'string' && report.browser.length, 'Missing browser identity');
    assert(Number.isInteger(report.pairs) && report.pairs >= 3 && report.pairs <= 10, 'Invalid pair count');
    assert.equal(report.runs?.length, report.pairs * 4, 'Missing or extra raw runs');
    const runs = new Map();
    const backends = new Map();
    for (const run of report.runs) {
        assert(['base', 'head'].includes(run.label) && typeof run.software === 'boolean', 'Invalid run identity');
        assert(Number.isInteger(run.pair) && run.pair >= 0 && run.pair < report.pairs, 'Invalid pair identity');
        const key = `${run.software}/${run.pair}/${run.label}`;
        assert(!runs.has(key), 'Duplicate raw run');
        finite(run.firstReadyMs, 'Invalid readiness measurement');
        assert(run.backend && typeof run.backend.webgl2 === 'boolean' &&
            typeof run.backend.userAgent === 'string' && run.backend.userAgent.length &&
            run.backend.devicePixelRatio === 1, 'Missing or invalid backend identity');
        const backend = { webgl2: run.backend.webgl2, renderer: run.backend.renderer,
            userAgent: run.backend.userAgent, devicePixelRatio: run.backend.devicePixelRatio };
        if (backends.has(run.software)) assert.deepEqual(backend, backends.get(run.software), 'Backend drift within pairs');
        else backends.set(run.software, backend);
        assert.equal(run.scenarios?.length, scenarios.length, 'Missing scenario');
        const cases = new Map();
        for (const row of run.scenarios) {
            assert(scenarios.includes(row.name) && !cases.has(row.name), 'Unknown or duplicate scenario');
            finite(row.cpuMs, 'Invalid task CPU measurement');
            if (row.name.startsWith('idle-')) {
                assert.equal(row.seconds, 3, 'Different idle workload');
                assert(Number.isInteger(row.rafCallbacks) && row.rafCallbacks >= 0, 'Invalid RAF count');
            } else {
                validateSnapshot(row.after);
                assert.equal(row.after.Scenario, row.name.split('-')[0], 'Wrong scroll scenario');
                if (row.name.endsWith('-wheel')) {
                    validateSnapshot(row.before);
                    assert.equal(row.before.Scenario, row.after.Scenario);
                    assert(row.after.Offset > row.before.Offset, 'Wheel input did not scroll');
                } else assert(row.after.Offset > 0, 'Thumb input did not scroll');
            }
            cases.set(row.name, row);
        }
        runs.set(key, { ...run, cases });
    }
    inventory(report.sizes?.base);
    inventory(report.sizes?.head);
    return { report, runs, backends };
}

function samples(validated, software, name) {
    const pairs = [];
    for (let pair = 0; pair < validated.report.pairs; ++pair) {
        const base = validated.runs.get(`${software}/${pair}/base`);
        const head = validated.runs.get(`${software}/${pair}/head`);
        assert(base && head, 'Missing paired run');
        const a = base.cases.get(name), b = head.cases.get(name);
        const differences = [];
        if (!name.startsWith('idle-')) {
            for (const point of ['before', 'after']) {
                if (!a[point] && !b[point]) continue;
                assert(a[point] && b[point], 'Asymmetric workload state');
                for (const key of ['Offset', 'Extent', 'Viewport'])
                    if (!close(a[point][key], b[point][key])) differences.push(`${point}.${key}`);
                for (const key of ['TextChecksum', 'Realized'])
                    if (a[point][key] !== b[point][key]) differences.push(`${point}.${key}`);
            }
        }
        pairs.push({ pair, baseCpuMs: a.cpuMs, headCpuMs: b.cpuMs,
            ratio: a.cpuMs === 0 ? null : b.cpuMs / a.cpuMs, stateDifferences: differences,
            // Counts are explanatory data, not normalized-away work or substituted timings.
            baseWork: name.startsWith('idle-') ? { rafCallbacks: a.rafCallbacks } : { before: a.before, after: a.after },
            headWork: name.startsWith('idle-') ? { rafCallbacks: b.rafCallbacks } : { before: b.before, after: b.after } });
    }
    return pairs;
}

/** Descriptive A/A screen only, not a confidence interval or a performance gate. */
export function screenPairs(comparison, calibration) {
    assert(comparison.length >= 3 && comparison.length === calibration.length, 'Unmatched sample counts');
    for (const sample of [...comparison, ...calibration]) {
        finite(sample.baseCpuMs, 'Invalid base CPU');
        finite(sample.headCpuMs, 'Invalid head CPU');
    }
    const before = comparison.map(x => x.baseCpuMs), after = comparison.map(x => x.headCpuMs);
    const deltaPercent = pairedChange(before, after);
    const zero = [...comparison, ...calibration].some(x => x.baseCpuMs === 0 || x.headCpuMs === 0);
    const ratios = comparison.map(x => x.baseCpuMs === 0 ? null : x.headCpuMs / x.baseCpuMs);
    const aaRatios = calibration.map(x => x.baseCpuMs === 0 ? null : x.headCpuMs / x.baseCpuMs);
    const noiseFactor = zero ? null : Math.exp(Math.max(...aaRatios.map(x => Math.abs(Math.log(x)))));
    let status = 'inconclusive';
    if ([...comparison, ...calibration].some(x => x.stateDifferences?.length)) status = 'inconclusive-workload-state';
    else if (zero) status = 'inconclusive-zero-timing';
    else if (Math.min(...ratios) > noiseFactor * 1.05) status = 'consistent-slowdown';
    else if (noiseFactor > 1.10) status = 'inconclusive-calibration';
    else if (Math.max(...ratios) < 1 / (noiseFactor * 1.05)) status = 'consistent-speedup';
    else if (ratios.every(x => x <= 1)) status = 'no-slower-pair-observed';
    else if (deltaPercent > 0) status = 'inconclusive-positive-median';
    return { status, deltaPercent, medianAbsoluteDeltaMs: median(comparison.map(x => x.headCpuMs - x.baseCpuMs)),
        ratios, aaRatios, noiseFactor, comparison, calibration };
}

export function auditReports(comparison, calibration) {
    const ab = validateReport(comparison), aa = validateReport(calibration);
    assert.equal(comparison.browser, calibration.browser, 'Different calibration browser');
    assert.equal(comparison.pairs, calibration.pairs, 'Different calibration pair count');
    assert.deepEqual(ab.backends, aa.backends, 'Different calibration backend');
    assert.deepEqual(inventory(comparison.sizes.head), inventory(calibration.sizes.base), 'Calibration is not the head deployment');
    assert.deepEqual(inventory(comparison.sizes.head), inventory(calibration.sizes.head), 'Calibration deployments differ');
    const rows = [];
    for (const software of [true, false])
        for (const scenario of scenarios)
            rows.push({ software, scenario, ...screenPairs(samples(ab, software, scenario), samples(aa, software, scenario)) });
    return { schema: 1, reportOnly: true, browser: comparison.browser, pairs: comparison.pairs, rows,
        limitations: 'A/A is the identical published head served twice, not an independent rebuild. Three pairs are descriptive, not statistical certification. High A/A variation and unequal final scroll states remain inconclusive. All raw timings and work counts are retained; no outliers are removed. Profile timings are separate and never replace these unprofiled pairs.' };
}

export function summarizeProfile(profile) {
    return analyzeProfile(profile).frames;
}
