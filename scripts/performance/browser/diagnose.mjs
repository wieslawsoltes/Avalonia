import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdir, readFile, readdir, realpath, stat, writeFile } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { validateSnapshot, taskMilliseconds, inspectWasm } from './evidence.mjs';
import { auditReports, summarizeProfile } from './regression-evidence.mjs';

const [buildArgument, resultArgument, outputArgument] = process.argv.slice(2);
if (!buildArgument || !resultArgument || !outputArgument)
    throw new Error('Usage: node diagnose.mjs BROWSER_BUILDS ORIGINAL_RESULTS DIAGNOSTICS_OUTPUT');
const builds = resolve(buildArgument), results = resolve(resultArgument), output = resolve(outputArgument);
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
await mkdir(output, { recursive: true });
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const json = async file => JSON.parse(await readFile(file, 'utf8'));
const save = async (file, value) => writeFile(resolve(output, file), JSON.stringify(value, null, 2) + '\n');
const manifest = { schema: 1, provenance: await json(resolve(builds, 'provenance.json')), files: {}, profiles: [] };
assert(/^[0-9a-f]{40}$/.test(manifest.provenance.base) && /^[0-9a-f]{40}$/.test(manifest.provenance.head), 'Missing exact revisions');
const roots = { base: await realpath(resolve(builds, 'base')), head: await realpath(resolve(builds, 'head')) };

async function fingerprint(directory) {
    const files = [];
    async function visit(path) {
        for (const entry of await readdir(path, { withFileTypes: true })) {
            const file = resolve(path, entry.name);
            assert(!entry.isSymbolicLink(), 'Deployment symlinks are not supported');
            if (entry.isDirectory()) await visit(file);
            else if (entry.isFile()) {
                const bytes = await readFile(file);
                files.push({ file: file.slice(directory.length + 1), bytes: bytes.length, sha256: sha(bytes),
                    ...(file.endsWith('.wasm') ? { wasm: inspectWasm(bytes) } : {}) });
            }
        }
    }
    await visit(directory);
    return files.sort((a, b) => a.file.localeCompare(b.file));
}
for (const label of ['base', 'head']) manifest.files[label] = await fingerprint(roots[label]);
manifest.harnessSha256 = sha(await readFile(resolve(scriptDirectory, 'compare.mjs')));
await save('manifest.json', manifest);
let browser, server;
try {
    // Additive diagnostic: execute the original comparison unchanged against the exact
    // same published head twice. It does not replace the baseline/head measurements.
    await new Promise((done, reject) => {
        const child = spawn(process.execPath, [resolve(scriptDirectory, 'compare.mjs'), roots.head, roots.head,
            resolve(output, 'calibration'), '3'], { stdio: 'inherit' });
        child.once('error', reject);
        child.once('exit', (code, signal) => code === 0 ? done() : reject(new Error(`A/A comparison failed: ${code ?? signal}`)));
    });
    const comparison = await json(resolve(results, 'comparison.json'));
    const calibration = await json(resolve(output, 'calibration/comparison.json'));
    const audit = auditReports(comparison, calibration);
    manifest.originalReportSha256 = sha(await readFile(resolve(results, 'comparison.json')));
    manifest.calibrationReportSha256 = sha(await readFile(resolve(output, 'calibration/comparison.json')));
    await save('calibration-audit.json', audit);
    const lines = ['# Browser regression diagnostics', '',
        `Base: ${manifest.provenance.base}; head: ${manifest.provenance.head}.`,
        'Original unprofiled baseline/head pairs are unchanged. A/A uses the identical head deployment.', '',
        '| Mode | Scenario | A/B paired change | Absolute change ms | A/A ratios | Classification |',
        '|---|---|---:|---:|---|---|'];
    for (const row of audit.rows)
        lines.push(`| ${row.software ? 'Software2D' : 'default/WebGL'} | ${row.scenario} | ${row.deltaPercent?.toFixed(1) ?? 'n/a'}% | ${row.medianAbsoluteDeltaMs.toFixed(2)} | ${row.aaRatios.map(x => x?.toFixed(3) ?? 'n/a').join(', ')} | ${row.status} |`);
    lines.push('', audit.limitations, '', '## Separate scenario profiles', '',
        'Sampled self-time is diagnostic, not task CPU, GPU time or presented FPS. Frames belong only to the exact fingerprinted build. Unknown wasm indices are not matched across different links.', '');
    await writeFile(resolve(output, 'diagnostics.md'), lines.join('\n') + '\n');
    console.log(lines.join('\n'));

    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
        '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css' };
    server = createServer(async (request, response) => {
        try {
            const [, label, ...parts] = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).split('/');
            if (!Object.hasOwn(roots, label)) { response.writeHead(404).end(); return; }
            const root = roots[label];
            let file = resolve(root, parts.join('/') || 'index.html');
            if (file !== root && !file.startsWith(root + sep)) { response.writeHead(403).end(); return; }
            if ((await stat(file)).isDirectory()) file = resolve(file, 'index.html');
            file = await realpath(file);
            if (!file.startsWith(root + sep)) { response.writeHead(403).end(); return; }
            const bytes = await readFile(file);
            response.writeHead(200, { 'Content-Type': mime[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
            response.end(bytes);
        } catch { response.writeHead(404).end(); }
    });
    await new Promise((done, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', done); });
    const origin = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
    assert.equal(browser.version(), comparison.browser, 'Profiling browser drift');
    for (const software of [true, false]) {
        // Reverse order for the other render mode. Profile durations are never AB timings.
        for (const label of software ? ['base', 'head'] : ['head', 'base']) {
            const context = await browser.newContext({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
            try {
                const page = await context.newPage(), errors = [];
                page.on('pageerror', error => errors.push(String(error)));
                const cdp = await context.newCDPSession(page);
                await cdp.send('Performance.enable');
                await cdp.send('Profiler.enable');
                await cdp.send('Profiler.setSamplingInterval', { interval: 100 });
                const metrics = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(x => [x.name, x.value]));
                const settle = () => page.evaluate(() => window.ferroPerf.settle());
                const snapshot = async () => validateSnapshot(await page.evaluate(() => window.ferroPerf.snapshot()));
                await page.goto(`${origin}/${label}/?software=${software ? 1 : 0}`, { waitUntil: 'load', timeout: 120000 });
                await page.waitForFunction(() => window.ferroPerfError || (window.ferroPerf?.snapshot().Ready && Number.isFinite(window.ferroPerfReadyAt)), null, { timeout: 120000 });
                const error = await page.evaluate(() => window.ferroPerfError);
                if (error) throw new Error(error);
                async function profile(name, action, scroll = false) {
                    const before = await snapshot(), start = await metrics();
                    await cdp.send('Profiler.start');
                    await action();
                    await settle();
                    const { profile: trace } = await cdp.send('Profiler.stop');
                    const end = await metrics(), after = await snapshot();
                    if (scroll) assert(after.Offset > before.Offset, `${name}: real input did not scroll`);
                    const file = `${label}-${software}-${name}.cpuprofile`;
                    await save(file, trace);
                    const top = summarizeProfile(trace);
                    const entry = { label, software, name, file, before, after,
                        diagnosticCpuMs: taskMilliseconds(start, end), profileSha256: sha(await readFile(resolve(output, file))), top };
                    manifest.profiles.push(entry);
                    await save('manifest.json', manifest);
                    const display = top.slice(0, 8).map(x => `${String(x.callFrame.functionName || '(unnamed)').replaceAll('|', '/')} ${(x.sampledMicroseconds / 1000).toFixed(1)} ms`).join('; ');
                    lines.push(`### ${label} / ${software ? 'Software2D' : 'default/WebGL'} / ${name}`, '', display, '');
                    console.log(`PROFILE ${label}/${software}/${name}: ${display}`);
                }
                for (const scenario of ['list', 'tree']) {
                    await page.evaluate(s => { window.ferroPerf.scenario(s); window.ferroPerf.theme(false); window.ferroPerf.overlay(false); window.ferroPerf.offset(0); }, scenario);
                    await page.waitForFunction(() => window.ferroPerf.snapshot().Ready);
                    await settle();
                    await page.mouse.move(400, 300);
                    await profile(`${scenario}-wheel`, async () => {
                        for (let i = 0; i < 20; ++i) { await page.mouse.wheel(0, 120); await page.waitForTimeout(20); }
                    }, true);
                    await page.evaluate(() => window.ferroPerf.offset(0));
                    await settle();
                    const thumb = await snapshot();
                    assert(thumb.ThumbWidth > 0 && thumb.ThumbHeight > 0, 'Missing scrollbar thumb');
                    const x = thumb.ThumbX + thumb.ThumbWidth / 2;
                    await page.mouse.move(x, thumb.ThumbY + thumb.ThumbHeight / 2);
                    await profile(`${scenario}-thumb`, async () => {
                        await page.mouse.down();
                        try { await page.mouse.move(x, Math.min(550, thumb.ThumbY + 180), { steps: 20 }); }
                        finally { await page.mouse.up(); }
                    }, true);
                }
                await page.evaluate(() => { window.ferroPerf.scenario('list'); window.ferroPerf.theme(false); window.ferroPerf.offset(480); });
                await page.mouse.move(0, 0);
                for (const overlay of [false, true]) {
                    await page.evaluate(value => window.ferroPerf.overlay(value), overlay);
                    await page.waitForTimeout(500);
                    await profile(`idle-overlay-${overlay}`, () => page.waitForTimeout(3000));
                }
                if (errors.length) throw new Error(errors.join('\n'));
            } finally { await context.close(); }
        }
    }
    for (const label of ['base', 'head'])
        assert.deepEqual(await fingerprint(roots[label]), manifest.files[label], 'Deployment changed during diagnostics');
    await save('manifest.json', manifest);
    await writeFile(resolve(output, 'diagnostics.md'), lines.join('\n') + '\n');
} catch (error) {
    await save('failure.json', { error: String(error?.stack ?? error), completedProfiles: manifest.profiles.length });
    throw error;
} finally {
    if (browser) await browser.close();
    if (server) await new Promise(done => server.close(done));
}
