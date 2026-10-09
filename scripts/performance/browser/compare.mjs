import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { mkdir, readFile, writeFile, readdir, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { resolve, extname, sep } from 'node:path';
import { gzipSync } from 'node:zlib';
import { median, pairedChange, taskMilliseconds, validateSnapshot, inspectWasm } from './evidence.mjs';

const [baseDirectory, headDirectory, outputDirectory, pairArgument = '3'] = process.argv.slice(2);
const pairs = Number(pairArgument);
if (!baseDirectory || !headDirectory || !outputDirectory || !Number.isInteger(pairs) || pairs < 3 || pairs > 10)
    throw new Error('Usage: node compare.mjs BASE_WWWROOT HEAD_WWWROOT OUTPUT [3..10 pairs]');
const output = resolve(outputDirectory);
await mkdir(output, { recursive: true });
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css' };
const server = createServer(async (request, response) => {
    try {
        const url = new URL(request.url, 'http://localhost');
        const [, label, ...parts] = decodeURIComponent(url.pathname).split('/');
        if (!['base', 'head'].includes(label)) { response.writeHead(404).end(); return; }
        const root = resolve(label === 'base' ? baseDirectory : headDirectory);
        let file = resolve(root, parts.join('/') || 'index.html');
        if (file !== root && !file.startsWith(root + sep)) { response.writeHead(403).end(); return; }
        if ((await stat(file)).isDirectory()) file = resolve(file, 'index.html');
        response.writeHead(200, { 'Content-Type': mime[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(await readFile(file));
    } catch { response.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
let browser;
const reports = [];
let visualDifferences = 0;

async function sizes(directory) {
    const files = [];
    async function visit(path) {
        for (const entry of await readdir(path, { withFileTypes: true })) {
            const file = resolve(path, entry.name);
            if (entry.isDirectory()) await visit(file);
            else {
                const data = await readFile(file);
                files.push({ file: file.slice(resolve(directory).length + 1), bytes: data.length, gzip: gzipSync(data).length,
                    ...(file.endsWith('.wasm') ? { wasm: inspectWasm(data) } : {}) });
            }
        }
    }
    await visit(resolve(directory));
    const runtime = files.filter(x => !/\.(br|gz|pdb|map|symbols)$/.test(x.file));
    return { files, runtimeBytes: runtime.reduce((n, x) => n + x.bytes, 0), runtimeGzipBytes: runtime.reduce((n, x) => n + x.gzip, 0),
        namedFunctions: files.reduce((n, x) => n + (x.wasm?.functionNames ?? 0), 0) };
}

async function pixels(file) { return PNG.sync.read(await readFile(resolve(output, file))); }
function equalPixels(a, b) { return a.width === b.width && a.height === b.height && a.data.equals(b.data); }

async function one(label, pair, software) {
    const context = await browser.newContext({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    const cdp = await context.newCDPSession(page);
    await cdp.send('Performance.enable');
    const metrics = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(x => [x.name, x.value]));
    const snapshot = async () => validateSnapshot(await page.evaluate(() => window.ferroPerf.snapshot()));
    const settle = () => page.evaluate(() => window.ferroPerf.settle());
    const result = { label, pair, software, scenarios: [], screenshots: [] };
    async function screenshot(name) {
        const file = `${label}-${pair}-${software}-${name}.png`;
        await page.screenshot({ path: resolve(output, file) });
        result.screenshots.push({ name, file, snapshot: await snapshot() });
        return file;
    }
    try {
        await page.goto(`${origin}/${label}/?software=${software ? 1 : 0}`, { waitUntil: 'load', timeout: 120000 });
        await page.waitForFunction(() => window.ferroPerfError || (window.ferroPerf && window.ferroPerf.snapshot().Ready && Number.isFinite(window.ferroPerfReadyAt)), null, { timeout: 120000 });
        const bootError = await page.evaluate(() => window.ferroPerfError);
        if (bootError) throw new Error(bootError);
        result.firstReadyMs = await page.evaluate(() => window.ferroPerfReadyAt);
        await settle();
        for (const scenario of ['list', 'tree']) {
            await page.evaluate(s => window.ferroPerf.scenario(s), scenario);
            await page.waitForFunction(() => window.ferroPerf.snapshot().Ready);
            await settle();
            const before = await snapshot();
            await page.mouse.move(400, 300);
            const start = await metrics();
            for (let i = 0; i < 20; ++i) { await page.mouse.wheel(0, 120); await page.waitForTimeout(20); }
            await settle();
            const end = await metrics();
            const after = await snapshot();
            if (!(after.Offset > before.Offset)) throw new Error(`${label}/${scenario}: real wheel input did not scroll`);
            result.scenarios.push({ name: `${scenario}-wheel`, cpuMs: taskMilliseconds(start, end), before, after });
            await page.evaluate(() => window.ferroPerf.offset(0));
            await settle();
            const thumb = await snapshot();
            if (!(thumb.ThumbWidth > 0 && thumb.ThumbHeight > 0)) throw new Error(`${scenario}: scrollbar thumb not found`);
            await page.mouse.move(thumb.ThumbX + thumb.ThumbWidth / 2, thumb.ThumbY + thumb.ThumbHeight / 2);
            const dragStart = await metrics();
            await page.mouse.down();
            await page.mouse.move(thumb.ThumbX + thumb.ThumbWidth / 2, Math.min(550, thumb.ThumbY + 180), { steps: 20 });
            await page.mouse.up();
            await settle();
            const dragEnd = await metrics();
            const dragged = await snapshot();
            if (!(dragged.Offset > 0)) throw new Error(`${label}/${scenario}: real thumb drag did not scroll`);
            result.scenarios.push({ name: `${scenario}-thumb`, cpuMs: taskMilliseconds(dragStart, dragEnd), after: dragged });
            await page.evaluate(() => { window.ferroPerf.offset(480); window.ferroPerf.overlay(false); });
            await page.mouse.move(0, 0);
            await page.waitForTimeout(250);
            for (const dark of [false, true]) {
                await page.evaluate(d => window.ferroPerf.theme(d), dark);
                await settle();
                await screenshot(`${scenario}-${dark ? 'dark' : 'light'}`);
            }
        }
        await page.evaluate(() => { window.ferroPerf.scenario('list'); window.ferroPerf.theme(false); window.ferroPerf.offset(480); });
        await page.waitForTimeout(500);
        const original = await screenshot('resource-original');
        await page.evaluate(() => window.ferroPerf.resource(true));
        await settle();
        const replaced = await screenshot('resource-replaced');
        if (equalPixels(await pixels(original), await pixels(replaced)))
            throw new Error('The dynamic resource replacement did not change rendered pixels');
        await page.evaluate(() => window.ferroPerf.resource(false));
        await settle();
        const restored = await screenshot('resource-restored');
        if (!equalPixels(await pixels(original), await pixels(restored)))
            throw new Error('Restoring the dynamic resource did not restore rendered pixels');
        for (const overlay of [false, true]) {
            await page.evaluate(value => window.ferroPerf.overlay(value), overlay);
            await page.waitForTimeout(500);
            const start = await metrics();
            const frames = await page.evaluate(() => window.ferroPerf.frames());
            await page.waitForTimeout(3000);
            const end = await metrics();
            const lastFrames = await page.evaluate(() => window.ferroPerf.frames());
            const callbacks = lastFrames.completed - frames.completed;
            if (!Number.isInteger(callbacks) || callbacks < 0) throw new Error('Invalid RAF count');
            result.scenarios.push({ name: `idle-overlay-${overlay}`, cpuMs: taskMilliseconds(start, end), rafCallbacks: callbacks, seconds: 3 });
        }
        // One separate diagnostic trace per build/mode. Profiling never contaminates timed pairs.
        if (pair === 0) {
            await page.evaluate(() => { window.ferroPerf.overlay(false); window.ferroPerf.offset(0); });
            await settle();
            await cdp.send('Profiler.enable');
            await cdp.send('Profiler.setSamplingInterval', { interval: 100 });
            await cdp.send('Profiler.start');
            await page.mouse.move(400, 300);
            for (let i = 0; i < 30; ++i) { await page.mouse.wheel(0, 120); await page.waitForTimeout(20); }
            await settle();
            const { profile } = await cdp.send('Profiler.stop');
            result.profile = `${label}-${software}-scroll.cpuprofile`;
            result.profileSamples = profile.samples?.length ?? 0;
            await writeFile(resolve(output, result.profile), JSON.stringify(profile));
        }
        result.backend = await page.evaluate(() => {
            const canvas = document.createElement('canvas');
            const gl = canvas.getContext('webgl2');
            const debug = gl?.getExtension('WEBGL_debug_renderer_info');
            return { webgl2: !!gl, renderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null,
                userAgent: navigator.userAgent, devicePixelRatio };
        });
        if (errors.length) throw new Error(errors.join('\n'));
        return result;
    } finally { await context.close(); }
}

try {
    browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
    for (const software of [true, false]) {
        for (let pair = 0; pair < pairs; ++pair) {
            const current = {};
            for (const label of pair % 2 ? ['head', 'base'] : ['base', 'head']) {
                current[label] = await one(label, pair, software);
                reports.push(current[label]);
                await writeFile(resolve(output, `${label}-${pair}-${software}.json`), JSON.stringify(current[label], null, 2));
            }
            if (current.base.screenshots.length !== current.head.screenshots.length) throw new Error('Different screenshot cases');
            for (let i = 0; i < current.base.screenshots.length; ++i) {
                const a = current.base.screenshots[i];
                const b = current.head.screenshots[i];
                if (a.name !== b.name) throw new Error('Screenshot cases do not align');
                if (!equalPixels(await pixels(a.file), await pixels(b.file))) {
                    ++visualDifferences;
                    console.error(`Pixel mismatch: ${a.file} vs ${b.file}`);
                }
                if (a.snapshot.TextChecksum !== b.snapshot.TextChecksum || Math.abs(a.snapshot.Offset - b.snapshot.Offset) > 0.01)
                    throw new Error(`Logical render state differs: ${a.name}`);
            }
        }
    }
    const grouped = [], startup = [];
    for (const software of [true, false]) {
        const base = reports.filter(x => x.label === 'base' && x.software === software);
        const head = reports.filter(x => x.label === 'head' && x.software === software);
        const beforeReady = base.map(x => x.firstReadyMs), afterReady = head.map(x => x.firstReadyMs);
        startup.push({ software, baseMs: median(beforeReady), headMs: median(afterReady), pairedDeltaPercent: pairedChange(beforeReady, afterReady) });
        for (const scenario of base[0].scenarios.map(x => x.name)) {
            const before = base.map(x => x.scenarios.find(s => s.name === scenario));
            const after = head.map(x => x.scenarios.find(s => s.name === scenario));
            if ([...before, ...after].some(x => !x)) throw new Error('Missing scenario');
            grouped.push({ software, scenario, baseCpuMs: median(before.map(x => x.cpuMs)), headCpuMs: median(after.map(x => x.cpuMs)),
                pairedCpuDeltaPercent: pairedChange(before.map(x => x.cpuMs), after.map(x => x.cpuMs)),
                ...(scenario.startsWith('idle-') ? { seconds: 3, baseRafCallbacks: median(before.map(x => x.rafCallbacks)), headRafCallbacks: median(after.map(x => x.rafCallbacks)) } : {}) });
        }
    }
    const report = { schema: 2, browser: browser.version(), pairs, visualDifferences,
        sizes: { base: await sizes(baseDirectory), head: await sizes(headDirectory) }, startup, results: grouped, runs: reports,
        limitations: 'Shared hosted runner; SwiftShader can be software. Task CPU is not GPU time or presented FPS. Ready-after-two-RAF is not hardware presentation. RAF activity can continue without rendering. Separate CPU profiles identify the exact deployed build; names/code hashes are inspected, not presumed to align across different links.' };
    await writeFile(resolve(output, 'comparison.json'), JSON.stringify(report, null, 2));
    const mode = software => software ? 'Software2D' : 'default/WebGL';
    const delta = value => value === null ? 'n/a (zero baseline)' : `${value.toFixed(1)}%`;
    const lines = ['# Browser performance and pixel parity', '', `Browser: ${report.browser}; ${pairs} alternating pairs per rendering mode.`,
        `Exact screenshot mismatches: ${visualDifferences}; dynamic resource changes and restoration verified.`, '',
        '| Deployment bytes | Base | Head |', '|---|---:|---:|',
        `| Runtime raw | ${report.sizes.base.runtimeBytes} | ${report.sizes.head.runtimeBytes} |`,
        `| Runtime gzip estimate | ${report.sizes.base.runtimeGzipBytes} | ${report.sizes.head.runtimeGzipBytes} |`,
        `| Native function names found | ${report.sizes.base.namedFunctions} | ${report.sizes.head.namedFunctions} |`, '',
        '| Startup mode | Base ready ms | Head ready ms | Paired change |', '|---|---:|---:|---:|',
        ...startup.map(x => `| ${mode(x.software)} | ${x.baseMs.toFixed(1)} | ${x.headMs.toFixed(1)} | ${delta(x.pairedDeltaPercent)} |`), '',
        '| Mode | Scenario | Base task ms | Head task ms | Paired change |', '|---|---|---:|---:|---:|',
        ...grouped.map(x => `| ${mode(x.software)} | ${x.scenario} | ${x.baseCpuMs.toFixed(2)} | ${x.headCpuMs.toFixed(2)} | ${delta(x.pairedCpuDeltaPercent)} |`), '',
        '| Idle mode (3 seconds) | Base RAF callbacks | Head RAF callbacks |', '|---|---:|---:|',
        ...grouped.filter(x => x.scenario.startsWith('idle-')).map(x => `| ${mode(x.software)} ${x.scenario} | ${x.baseRafCallbacks} | ${x.headRafCallbacks} |`), '', report.limitations];
    await writeFile(resolve(output, 'comparison.md'), lines.join('\n') + '\n');
    console.log(lines.join('\n'));
    if (visualDifferences) process.exitCode = 1;
} catch (error) {
    await writeFile(resolve(output, 'failure.json'), JSON.stringify({ error: String(error?.stack ?? error), completedRuns: reports }, null, 2));
    throw error;
} finally { await browser?.close(); await new Promise(resolve => server.close(resolve)); }
