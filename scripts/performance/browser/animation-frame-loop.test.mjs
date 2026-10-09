import assert from 'node:assert/strict';
import test from 'node:test';
import { AnimationFrameLoop } from '../../../src/Browser/Avalonia.Browser/webapp/modules/avalonia/animationFrameLoop.ts';

function fixture(tick = () => {}) {
    let next = 0;
    const pending = new Map(), cancelled = [], times = [];
    const loop = new AnimationFrameLoop(callback => {
        const id = next++;
        pending.set(id, callback);
        return id;
    }, id => { cancelled.push(id); pending.delete(id); }, time => { times.push(time); tick(time); });
    const fire = time => {
        const [id, callback] = pending.entries().next().value;
        pending.delete(id);
        callback(time);
    };
    return { loop, pending, cancelled, times, fire };
}

test('idle loop requests nothing and repeated start owns only one RAF', () => {
    const f = fixture();
    assert.equal(f.pending.size, 0);
    f.loop.start(); f.loop.start(); f.loop.start();
    assert.equal(f.pending.size, 1);
    f.fire(125.5);
    assert.deepEqual(f.times, [125.5]);
    assert.equal(f.pending.size, 1);
});

test('stop cancels even handle zero and remains idempotent', () => {
    const f = fixture(); f.loop.start(); f.loop.stop(); f.loop.stop();
    assert.deepEqual(f.cancelled, [0]);
    assert.equal(f.pending.size, 0);
});

test('stop from managed tick does not rearm the chain', () => {
    const f = fixture(() => f.loop.stop()); f.loop.start(); f.fire(10);
    assert.equal(f.pending.size, 0);
    assert.deepEqual(f.times, [10]);
});

test('a cancelled, already-dispatched callback cannot disturb a restarted chain', () => {
    const f = fixture(); f.loop.start();
    const stale = [...f.pending.values()][0];
    f.loop.stop(); f.loop.start();
    stale(2);
    assert.equal(f.pending.size, 1);
    assert.deepEqual(f.times, []);
    f.fire(3);
    assert.deepEqual(f.times, [3]);
});

test('stop and restart inside a tick schedules exactly one successor', () => {
    const f = fixture(() => { f.loop.stop(); f.loop.start(); });
    f.loop.start();
    for (let i = 0; i < 100; ++i) { f.fire(i); assert.equal(f.pending.size, 1); }
    assert.equal(f.times.length, 100);
});

test('an exception does not orphan the active timer and stop still cancels it', () => {
    const f = fixture(() => { throw new Error('callback'); }); f.loop.start();
    assert.throws(() => f.fire(10), /callback/);
    assert.equal(f.pending.size, 1);
    f.loop.stop();
    assert.equal(f.pending.size, 0);
});

test('timestamp values are forwarded unchanged and stopped chains stay quiet', () => {
    const f = fixture();
    for (let i = 0; i < 100; ++i) { f.loop.start(); f.fire(i + 0.125); f.loop.stop(); }
    assert.deepEqual(f.times, Array.from({ length: 100 }, (_, i) => i + 0.125));
    assert.equal(f.pending.size, 0);
});
