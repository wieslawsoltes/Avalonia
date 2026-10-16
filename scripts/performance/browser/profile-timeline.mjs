import assert from 'node:assert/strict';

/**
 * CDP timeDeltas encode adjacent sample timestamps, not per-frame durations.
 * Like Chrome DevTools CPUProfileDataModel.convertTimeDeltas/sortSamples, rebuild
 * absolute times and sort samples together before estimating their intervals.
 * Raw input is retained unchanged; no negative delta is clamped or discarded.
 */
export function analyzeProfile(profile) {
    assert(Array.isArray(profile?.nodes) && profile.nodes.length &&
        Array.isArray(profile.samples) && Array.isArray(profile.timeDeltas), 'Missing CPU profile samples');
    assert.equal(profile.samples.length, profile.timeDeltas.length, 'Mismatched CPU profile samples');
    for (const value of [profile.startTime, profile.endTime])
        assert(Number.isFinite(value) && value >= 0 && value <= Number.MAX_SAFE_INTEGER, 'Invalid CPU profile boundary');
    assert(profile.endTime >= profile.startTime, 'Reversed CPU profile boundaries');
    const nodes = new Map();
    for (const node of profile.nodes) {
        assert(Number.isSafeInteger(node.id) && node.id > 0 && !nodes.has(node.id) &&
            node.callFrame && typeof node.callFrame === 'object', 'Invalid profile node');
        nodes.set(node.id, node);
    }

    let timestamp = profile.startTime;
    let negativeDeltas = 0;
    const samples = profile.samples.map((id, index) => {
        assert(nodes.has(id), `Unknown sampled profile node ${id} at index ${index}`);
        const delta = profile.timeDeltas[index];
        assert(Number.isSafeInteger(delta), `Invalid CPU sample timestamp delta ${delta} at index ${index}`);
        if (delta < 0) ++negativeDeltas;
        timestamp += delta;
        assert(Number.isFinite(timestamp) && timestamp >= profile.startTime && timestamp <= profile.endTime &&
            timestamp <= Number.MAX_SAFE_INTEGER,
            `CPU sample timestamp outside profile boundaries: index=${index}, delta=${delta}, time=${timestamp}, start=${profile.startTime}, end=${profile.endTime}`);
        return { id, timestamp, originalIndex: index };
    });
    samples.sort((a, b) => a.timestamp - b.timestamp || a.originalIndex - b.originalIndex);

    let reorderedSamples = 0;
    let coincidentSamples = 0;
    const totals = new Map();
    for (let i = 0; i < samples.length; ++i) {
        const sample = samples[i];
        if (sample.originalIndex !== i) ++reorderedSamples;
        if (i > 0 && sample.timestamp === samples[i - 1].timestamp) ++coincidentSamples;
        const next = i + 1 < samples.length ? samples[i + 1].timestamp : profile.endTime;
        const duration = next - sample.timestamp;
        // Forward intervals assign time until the next observation to the current sample.
        // The final interval ends at the recorded endTime; there is no extrapolated tail.
        const total = totals.get(sample.id) ?? { nodeId: sample.id, callFrame: nodes.get(sample.id).callFrame,
            samples: 0, sampledMicroseconds: 0 };
        ++total.samples;
        total.sampledMicroseconds += duration;
        totals.set(sample.id, total);
    }
    const firstSample = samples[0]?.timestamp ?? profile.endTime;
    return {
        frames: [...totals.values()].sort((a, b) => b.sampledMicroseconds - a.sampledMicroseconds),
        timing: {
            version: 2,
            method: 'forward-intervals-after-stable-timestamp-sort',
            startTime: profile.startTime,
            endTime: profile.endTime,
            samples: samples.length,
            negativeDeltas,
            reorderedSamples,
            coincidentSamples,
            sampledMicroseconds: profile.endTime - firstSample,
            // The interval before the first observation has no sampled owner.
            unattributedMicroseconds: firstSample - profile.startTime,
        },
    };
}
