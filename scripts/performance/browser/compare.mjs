import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { mkdir, readFile, writeFile, readdir, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { resolve, extname, sep } from 'node:path';
import { gzipSync } from 'node:zlib';

const [baseDirectory, headDirectory, outputDirectory, pairArgument = '3'] = process.argv.slice(2);
const pairs = Number(pairArgument);
if (!baseDirectory || !headDirectory || !outputDirectory || !Number.isInteger(pairs) || pairs < 3 || pairs > 10)
    throw new Error('Usage: node compare.mjs BASE_WWWROOT HEAD_WWWROOT OUTPUT [3..10 pairs]');
const output = resolve(outputDirectory);
await mkdir(output, { recursive: true });
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css', '.dat': 'application/octet-stream', '.dll': 'application/octet-stream', '.pdb': 'application/octet-stream' };
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
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
const reports = [];
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
let visualDifferences = 0;

async function sizes(directory) {
    const files = [];
    async function visit(path) {
        for (const entry of await readdir(path, { withFileTypes: true })) {
            const file = resolve(path, entry.name);
            if (entry.isDirectory()) await visit(file);
            else {
                const data = await readFile(file);
                files.push({ file: file.slice(resolve(directory).length + 1), bytes: data.length, gzip: gzipSync(data).length });
            }
        }
    }
    await visit(resolve(directory));
    // Precompressed duplicates and debug symbols are reported separately, not counted twice.
    const runtime = files.filter(x => !/\.(br|gz|pdb|map|symbols)$/.test(x.file));
    return { files, runtimeBytes: runtime.reduce((n, x) => n + x.bytes, 0), runtimeGzipBytes: runtime.reduce((n, x) => n + x.gzip, 0) };
}

async function one(label, pair, software) {
    const context = await browser.newContext({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    const cdp = await context.newCDPSession(page);
    await cdp.send('Performance.enable');
    const metrics = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(x => [x.name, x.value]));
    const result = { label, pair, software, scenarios: [], screenshots: [] };
    try {
        await page.goto(`${origin}/${label}/?software=${software ? 1 : 0}`, { waitUntil: 'load', timeout: 120000 });
        await page.waitForFunction(() => window.ferroPerfError || (window.ferroPerf && window.ferroPerf.snapshot().Ready), null, { timeout: 120000 });
        const bootError = await page.evaluate(() => window.ferroPerfError);
        if (bootError) throw new Error(bootError);
        result.firstReadyMs = await page.evaluate(() => window.ferroPerfReadyAt);
        await page.evaluate(() => window.ferroPerf.settle());
        for (const scenario of ['list', 'tree']) {
            await page.evaluate(s => window.ferroPerf.scenario(s), scenario);
            await page.waitForFunction(() => window.ferroPerf.snapshot().Ready);
            await page.evaluate(() => window.ferroPerf.settle());
            const before = await page.evaluate(() => window.ferroPerf.snapshot());
            await page.mouse.move(400, 300);
            const start = await metrics();
            for (let i = 0; i < 20; ++i) { await page.mouse.wheel(0, 120); await page.waitForTimeout(20); }
            await page.evaluate(() => window.ferroPerf.settle());
            const end = await metrics();
            const after = await page.evaluate(() => window.ferroPerf.snapshot());
            if (!(after.Offset > before.Offset)) throw new Error(`${label}/${scenario}: real wheel input did not scroll`);
            result.scenarios.push({ name: `${scenario}-wheel`, cpuMs: (end.TaskDuration - start.TaskDuration) * 1000, before, after });
            await page.evaluate(() => window.ferroPerf.offset(0));
            await page.evaluate(() => window.ferroPerf.settle());
            const thumb = await page.evaluate(() => window.ferroPerf.snapshot());
            if (!(thumb.ThumbWidth > 0 && thumb.ThumbHeight > 0)) throw new Error(`${scenario}: scrollbar thumb not found`);
            await page.mouse.move(thumb.ThumbX + thumb.ThumbWidth / 2, thumb.ThumbY + thumb.ThumbHeight / 2);
            const dragStart = await metrics();
            await page.mouse.down();
            await page.mouse.move(thumb.ThumbX + thumb.ThumbWidth / 2, Math.min(550, thumb.ThumbY + 180), { steps: 20 });
            await page.mouse.up();
            await page.evaluate(() => window.ferroPerf.settle());
            const dragEnd = await metrics();
            const dragged = await page.evaluate(() => window.ferroPerf.snapshot());
            if (!(dragged.Offset > 0)) throw new Error(`${label}/${scenario}: real thumb drag did not scroll`);
            result.scenarios.push({ name: `${scenario}-thumb`, cpuMs: (dragEnd.TaskDuration - dragStart.TaskDuration) * 1000, after: dragged });
            // Inputs may coalesce differently. Visual parity uses the same explicit final offset.
            await page.evaluate(() => { window.ferroPerf.offset(480); window.ferroPerf.overlay(false); });
            await page.mouse.move(0, 0);
            await page.waitForTimeout(250);
            for (const dark of [false, true]) {
                await page.evaluate(d => window.ferroPerf.theme(d), dark);
                await page.evaluate(() => window.ferroPerf.settle());
                const name = `${scenario}-${dark ? 'dark' : 'light'}`;
                const file = `${label}-${pair}-${software}-${name}.png`;
                await page.screenshot({ path: resolve(output, file) });
                result.screenshots.push({ name, file, snapshot: await page.evaluate(() => window.ferroPerf.snapshot()) });
            }
        }
        await page.evaluate(() => { window.ferroPerf.scenario('list'); window.ferroPerf.theme(false); });
        await page.waitForTimeout(500);
        for (const overlay of [false, true]) {
            await page.evaluate(value => window.ferroPerf.overlay(value), overlay);
            await page.waitForTimeout(500);
            const start = await metrics();
            const frames = await page.evaluate(() => window.ferroPerf.frames());
            await page.waitForTimeout(3000);
            const end = await metrics();
            const lastFrames = await page.evaluate(() => window.ferroPerf.frames());
            result.scenarios.push({ name: `idle-overlay-${overlay}`, cpuMs: (end.TaskDuration - start.TaskDuration) * 1000,
                rafCallbacks: lastFrames.completed - frames.completed, seconds: 3 });
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
    for (const software of [true, false]) {
        for (let pair = 0; pair < pairs; ++pair) {
            const order = pair % 2 ? ['head', 'base'] : ['base', 'head'];
            const current = {};
            for (const label of order) {
                current[label] = await one(label, pair, software);
                reports.push(current[label]);
                await writeFile(resolve(output, `${label}-${pair}-${software}.json`), JSON.stringify(current[label], null, 2));
            }
            for (let i = 0; i < current.base.screenshots.length; ++i) {
                const a = current.base.screenshots[i];
                const b = current.head.screenshots[i];
                const first = PNG.sync.read(await readFile(resolve(output, a.file)));
                const second = PNG.sync.read(await readFile(resolve(output, b.file)));
                if (first.width !== second.width || first.height !== second.height || !first.data.equals(second.data)) {
                    ++visualDifferences;
                    console.error(`Pixel mismatch: ${a.file} vs ${b.file}`);
                }
                if (a.snapshot.TextChecksum !== b.snapshot.TextChecksum || Math.abs(a.snapshot.Offset - b.snapshot.Offset) > 0.01)
                    throw new Error(`Logical render state differs: ${a.name}`);
            }
        }
    }
    const grouped = [];
    for (const software of [true, false]) {
        const base = reports.filter(x => x.label === 'base' && x.software === software);
        const head = reports.filter(x => x.label === 'head' && x.software === software);
        for (const scenario of base[0].scenarios.map(x => x.name)) {
            const ratios = base.map((before, i) => head[i].scenarios.find(x => x.name === scenario).cpuMs /
                before.scenarios.find(x => x.name === scenario).cpuMs);
            grouped.push({ software, scenario, pairedCpuDeltaPercent: (median(ratios) - 1) * 100 });
        }
    }
    const report = { schema: 1, browser: browser.version(), pairs, visualDifferences,
        sizes: { base: await sizes(baseDirectory), head: await sizes(headDirectory) }, results: grouped, runs: reports,
        limitations: 'Shared hosted runner, SwiftShader may be software; task CPU is not GPU time or presented FPS. First-ready includes initialization but is not a hardware presentation timestamp.' };
    await writeFile(resolve(output, 'comparison.json'), JSON.stringify(report, null, 2));
    const lines = ['# Browser performance and pixel parity', '', `Browser: ${report.browser}; ${pairs} alternating pairs per rendering mode.`,
        `Exact screenshot mismatches: ${visualDifferences}.`, '', '| Mode | Scenario | Paired task CPU change |', '|---|---|---:|',
        ...grouped.map(x => `| ${x.software ? 'Software2D' : 'default/WebGL'} | ${x.scenario} | ${x.pairedCpuDeltaPercent.toFixed(1)}% |`), '', report.limitations];
    await writeFile(resolve(output, 'comparison.md'), lines.join('\n') + '\n');
    console.log(lines.join('\n'));
    if (visualDifferences) process.exitCode = 1;
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
