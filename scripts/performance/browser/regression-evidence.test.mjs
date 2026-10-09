import test from 'node:test';
import assert from 'node:assert/strict';
import { auditReports, scenarios, screenPairs, summarizeProfile } from './regression-evidence.mjs';

function snapshot(scenario, offset) {
    return { Ready: true, Scenario: scenario, Offset: offset, Extent: 10000, Viewport: 500,
        ThumbX: 780, ThumbY: 5, ThumbWidth: 10, ThumbHeight: 25, Prepared: 30, Cleared: 10,
        Realized: 20, TextChecksum: 42 };
}
function report(scale = 1) {
    const runs = [];
    for (const software of [true, false])
        for (let pair = 0; pair < 3; ++pair)
            for (const label of ['base', 'head']) runs.push({ label, software, pair, firstReadyMs: 100,
                backend: { webgl2: true, renderer: 'test', userAgent: 'test', devicePixelRatio: 1 },
                scenarios: scenarios.map(name => ({ name, cpuMs: label === 'head' ? 100 * scale : 100,
                    ...(name.startsWith('idle-') ? { seconds: 3, rafCallbacks: 180 } : {
                        after: snapshot(name.split('-')[0], 100),
                        ...(name.endsWith('-wheel') ? { before: snapshot(name.split('-')[0], 0) } : {}) }) })) });
    const size = { files: [{ file: 'app.wasm', bytes: 8, wasm: { codeSha256: 'exact-code' } }] };
    return { schema: 2, browser: 'test', pairs: 3, visualDifferences: 0, runs, sizes: { base: size, head: structuredClone(size) } };
}

test('audit retains paired data and flags a consistent slowdown', () => {
    const result = auditReports(report(1.3), report());
    assert.equal(result.rows.length, 12);
    assert(result.rows.every(x => x.status === 'consistent-slowdown'));
    assert.deepEqual(result.rows[0].ratios, [1.3, 1.3, 1.3]);
    assert.equal(result.rows[0].medianAbsoluteDeltaMs, 30);
    assert.equal(result.reportOnly, true);
});

test('noisy calibration is explicitly inconclusive, not a clean screen', () => {
    const result = auditReports(report(1.1), report(1.4));
    assert(result.rows.every(x => x.status === 'inconclusive-calibration'));
});

test('mixed pairs and positive medians are not silently discarded', () => {
    const pair = ratio => ({ baseCpuMs: 100, headCpuMs: 100 * ratio });
    const result = screenPairs([pair(1.2), pair(0.8), pair(1.2)], [pair(1), pair(1), pair(1)]);
    assert.equal(result.status, 'inconclusive-positive-median');
    assert.deepEqual(result.ratios, [1.2, 0.8, 1.2]);
});

test('zero baseline keeps absolute timing and marks the ratio inconclusive', () => {
    const zero = { baseCpuMs: 0, headCpuMs: 10 };
    const result = screenPairs([zero, zero, zero], [zero, zero, zero]);
    assert.equal(result.deltaPercent, null);
    assert.equal(result.status, 'inconclusive-zero-timing');
    assert.equal(result.medianAbsoluteDeltaMs, 10);
});

test('duplicate, missing, malformed and non-finite raw runs are rejected', () => {
    for (const mutate of [r => r.runs.pop(), r => r.runs[1] = r.runs[0],
        r => r.runs[0].scenarios.pop(), r => r.runs[0].scenarios[1] = r.runs[0].scenarios[0],
        r => r.runs[0].scenarios[0].cpuMs = NaN, r => r.runs[0].pair = 3,
        r => r.runs[0].scenarios[4].seconds = 2]) {
        const value = report(); mutate(value);
        assert.throws(() => auditReports(value, report()));
    }
});

test('browser, backend, deployment and pixel differences invalidate calibration', () => {
    for (const mutate of [r => r.browser = 'other', r => r.runs[0].backend.renderer = 'other',
        r => r.sizes.base.files[0].bytes++, r => r.sizes.head.files[0].wasm.codeSha256 = 'other',
        r => r.visualDifferences = 1]) {
        const value = report(); mutate(value);
        assert.throws(() => auditReports(report(), value));
    }
});

test('different timed scroll endpoints remain visible and inconclusive', () => {
    const value = report(0.5);
    value.runs[1].scenarios[0].after.Offset = 200;
    const row = auditReports(value, report()).rows[0];
    assert.equal(row.status, 'inconclusive-workload-state');
    assert.deepEqual(row.comparison[0].stateDifferences, ['after.Offset']);
});

test('CPU profile summaries retain exact frame identity and forward sample intervals', () => {
    const nodes = [1, 2].map(id => ({ id, callFrame: { functionName: `function-${id}`, url: 'exact.wasm' } }));
    const profile = { startTime: 1000, endTime: 1400, nodes, samples: [1, 2, 1], timeDeltas: [100, 50, 200] };
    const summary = summarizeProfile(profile);
    assert.equal(summary[0].nodeId, 2);
    assert.equal(summary[0].sampledMicroseconds, 200);
    assert.equal(summary[1].nodeId, 1);
    assert.equal(summary[1].samples, 2);
    assert.equal(summary[1].sampledMicroseconds, 100);
    assert.strictEqual(summary[1].callFrame, nodes[0].callFrame);
    assert.throws(() => summarizeProfile({ ...profile, samples: [3], timeDeltas: [100] }));
    assert.throws(() => summarizeProfile({ ...profile, samples: [1], timeDeltas: [-1] }));
    assert.throws(() => summarizeProfile({ ...profile, samples: [1], timeDeltas: [] }));
});
