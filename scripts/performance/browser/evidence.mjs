import { createHash } from 'node:crypto';

export function median(values) {
    if (!values.length || values.some(x => !Number.isFinite(x))) throw new Error('Expected finite samples');
    const ordered = [...values].sort((a, b) => a - b);
    const middle = Math.floor(ordered.length / 2);
    return ordered.length % 2 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
}

export function taskMilliseconds(before, after) {
    const a = before?.TaskDuration;
    const b = after?.TaskDuration;
    if (!Number.isFinite(a) || !Number.isFinite(b) || a < 0 || b < a)
        throw new Error('Missing or non-monotonic Chrome task CPU measurement');
    return (b - a) * 1000;
}

export function pairedChange(before, after) {
    if (!before.length || before.length !== after.length || [...before, ...after].some(x => !Number.isFinite(x) || x < 0))
        throw new Error('Invalid paired samples');
    // A zero CPU baseline has no meaningful percentage; retain absolute data instead.
    return before.some(x => x === 0) ? null : (median(before.map((x, i) => after[i] / x)) - 1) * 100;
}

export function validateSnapshot(value) {
    if (!value || value.Ready !== true || !['list', 'tree'].includes(value.Scenario)) throw new Error('Invalid browser snapshot');
    for (const key of ['Offset', 'Extent', 'Viewport', 'ThumbX', 'ThumbY', 'ThumbWidth', 'ThumbHeight'])
        if (!Number.isFinite(value[key])) throw new Error(`Non-finite snapshot field ${key}`);
    for (const key of ['Prepared', 'Cleared', 'Realized', 'TextChecksum'])
        if (!Number.isInteger(value[key]) || value[key] < 0) throw new Error(`Invalid snapshot count ${key}`);
    if (value.Viewport <= 0 || value.Extent < 0 || value.Offset < 0) throw new Error('Invalid scroll geometry');
    return value;
}

/** Read names and exact code-section identity from the actual published bytes, without editing them. */
export function inspectWasm(data) {
    if (data.length < 8 || data.readUInt32LE(0) !== 0x6d736100 || data.readUInt32LE(4) !== 1)
        throw new Error('Invalid WebAssembly module');
    let position = 8;
    let codeSha256 = null;
    let functionNames = 0;
    const customSections = [];
    function uint(end) {
        let result = 0;
        for (let shift = 0; shift < 35; shift += 7) {
            if (position >= end) throw new Error('Truncated WebAssembly integer');
            const byte = data[position++];
            if (shift === 28 && (byte & 0xf0)) throw new Error('WebAssembly integer overflow');
            result += (byte & 127) * 2 ** shift;
            if (!(byte & 128)) return result;
        }
        throw new Error('Invalid WebAssembly integer');
    }
    function text(end) {
        const length = uint(end);
        if (position + length > end) throw new Error('Truncated WebAssembly name');
        const value = data.toString('utf8', position, position + length);
        position += length;
        return value;
    }
    while (position < data.length) {
        const id = data[position++];
        const length = uint(data.length);
        const end = position + length;
        if (end > data.length) throw new Error('Truncated WebAssembly section');
        if (id === 10) codeSha256 = createHash('sha256').update(data.subarray(position, end)).digest('hex');
        if (id === 0) {
            const name = text(end);
            customSections.push(name);
            if (name === 'name') {
                while (position < end) {
                    const kind = data[position++];
                    const size = uint(end);
                    const subsectionEnd = position + size;
                    if (subsectionEnd > end) throw new Error('Truncated WebAssembly name subsection');
                    if (kind === 1) {
                        const count = uint(subsectionEnd);
                        for (let i = 0; i < count; ++i) { uint(subsectionEnd); text(subsectionEnd); }
                        functionNames += count;
                    }
                    position = subsectionEnd;
                }
            }
        }
        position = end;
    }
    return { codeSha256, functionNames, customSections };
}
