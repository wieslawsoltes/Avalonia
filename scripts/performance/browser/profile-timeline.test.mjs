import test from 'node:test';
import assert from 'node:assert/strict';
import { analyzeProfile } from './profile-timeline.mjs';

const profile = (samples = [1, 2, 1], timeDeltas = [100, 200, -100]) => ({
    startTime: 1000, endTime: 1500, samples, timeDeltas,
    nodes: [1, 2].map(id => ({ id, callFrame: { functionName: `node-${id}`, url: 'exact.wasm' } })),
});

test('signed deltas are reconstructed and sorted with their sample IDs', () => {
    const input = profile();
    const original = structuredClone(input);
    const { frames, timing } = analyzeProfile(input);
    assert.deepEqual(frames.map(x => [x.nodeId, x.samples, x.sampledMicroseconds]), [[1, 2, 200], [2, 1, 200]]);
    assert.equal(timing.negativeDeltas, 1);
    assert.equal(timing.reorderedSamples, 2);
    assert.equal(timing.sampledMicroseconds, 400);
    assert.equal(timing.unattributedMicroseconds, 100);
    assert.deepEqual(input, original);
    assert.equal(frames.reduce((sum, frame) => sum + frame.sampledMicroseconds, 0) +
        timing.unattributedMicroseconds, input.endTime - input.startTime);
});

test('equal timestamps remain stable and zero intervals retain sample counts', () => {
    const { frames, timing } = analyzeProfile(profile([1, 2], [100, 0]));
    assert.equal(timing.reorderedSamples, 0);
    assert.equal(timing.coincidentSamples, 1);
    assert.deepEqual(frames.map(x => [x.nodeId, x.samples, x.sampledMicroseconds]), [[2, 1, 400], [1, 1, 0]]);
});

test('singleton uses the actual end boundary and leaves the pre-sample interval unattributed', () => {
    const { frames, timing } = analyzeProfile(profile([1], [150]));
    assert.equal(frames[0].sampledMicroseconds, 350);
    assert.equal(timing.unattributedMicroseconds, 150);
});

test('empty profiles do not fabricate frame durations', () => {
    const { frames, timing } = analyzeProfile(profile([], []));
    assert.deepEqual(frames, []);
    assert.equal(timing.sampledMicroseconds, 0);
    assert.equal(timing.unattributedMicroseconds, 500);
    assert.equal(timing.samples, 0);
});

test('invalid structure, identities and non-finite deltas remain errors', () => {
    for (const mutate of [p => p.nodes = [], p => p.nodes[1] = p.nodes[0],
        p => p.nodes[0].callFrame = null, p => p.samples[0] = 99,
        p => p.timeDeltas.pop(), p => p.timeDeltas[0] = NaN,
        p => p.timeDeltas[0] = Infinity, p => p.timeDeltas[0] = 0.5,
        p => p.timeDeltas[0] = Number.MAX_SAFE_INTEGER + 1,
        p => delete p.startTime, p => p.endTime = 900]) {
        const value = profile(); mutate(value);
        assert.throws(() => analyzeProfile(value));
    }
});

test('timestamp inversions are supported but samples outside recording bounds are rejected', () => {
    assert.throws(() => analyzeProfile(profile([1], [-1])), /outside profile boundaries/);
    assert.throws(() => analyzeProfile(profile([1], [501])), /outside profile boundaries/);
    assert.throws(() => analyzeProfile(profile([1, 2], [100, -101])), /outside profile boundaries/);
    assert.doesNotThrow(() => analyzeProfile(profile()));
});

test('input arrays can be frozen and all sampled nodes remain in the result', () => {
    const value = profile([1, 2, 1, 2], [100, 0, 100, 0]);
    Object.freeze(value.samples);
    Object.freeze(value.timeDeltas);
    Object.freeze(value.nodes);
    const { frames, timing } = analyzeProfile(value);
    assert.equal(frames.length, 2);
    assert.equal(frames.reduce((sum, frame) => sum + frame.samples, 0), 4);
    assert.equal(timing.coincidentSamples, 2);
    assert.equal(timing.samples, 4);
});
