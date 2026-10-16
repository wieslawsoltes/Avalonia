import test from 'node:test';
import assert from 'node:assert/strict';
import { median, pairedChange, taskMilliseconds, inspectWasm, validateSnapshot } from './evidence.mjs';

test('median handles odd and even pairs without mutating input', () => {
    const values = [3, 1, 4, 2];
    assert.equal(median(values), 2.5); assert.deepEqual(values, [3, 1, 4, 2]);
    assert.equal(median([3, 1, 2]), 2); assert.throws(() => median([])); assert.throws(() => median([NaN]));
});
test('paired differences reject invalid data and report zero baselines explicitly', () => {
    assert.ok(Math.abs(pairedChange([10, 20, 30], [8, 16, 24]) + 20) < 1e-10);
    assert.equal(pairedChange([0, 1], [1, 1]), null);
    assert.throws(() => pairedChange([1], [])); assert.throws(() => pairedChange([1], [Infinity]));
});
test('task CPU must exist and be monotonic', () => {
    assert.equal(taskMilliseconds({ TaskDuration: 1 }, { TaskDuration: 2 }), 1000);
    assert.throws(() => taskMilliseconds({}, {}));
    assert.throws(() => taskMilliseconds({ TaskDuration: 2 }, { TaskDuration: 1 }));
});
test('snapshot rejects missing geometry and fabricated counts', () => {
    const value = { Ready: true, Scenario: 'list', Offset: 0, Extent: 100, Viewport: 50,
        ThumbX: 0, ThumbY: 0, ThumbWidth: 10, ThumbHeight: 10, Prepared: 1, Cleared: 0, Realized: 1, TextChecksum: 42 };
    assert.equal(validateSnapshot(value), value);
    assert.throws(() => validateSnapshot({ ...value, Offset: NaN }));
    assert.throws(() => validateSnapshot({ ...value, Prepared: -1 }));
    assert.throws(() => validateSnapshot({ ...value, Ready: false }));
});
test('actual module name metadata and code identities are inspected', () => {
    const header = Buffer.from([0,97,115,109,1,0,0,0]);
    const named = Buffer.concat([header, Buffer.from([0,12,4,110,97,109,101,1,5,1,0,2,102,110]), Buffer.from([10,1,0])]);
    const inspected = inspectWasm(named);
    assert.equal(inspected.functionNames, 1); assert.deepEqual(inspected.customSections, ['name']);
    assert.equal(inspected.codeSha256.length, 64);
    assert.equal(inspectWasm(header).codeSha256, null);
    assert.throws(() => inspectWasm(Buffer.from([1,2,3])));
    assert.throws(() => inspectWasm(Buffer.concat([header, Buffer.from([10,10,0])])));
});
