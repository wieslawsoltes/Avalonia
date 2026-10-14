#!/usr/bin/env python3
"""Package exact tracked source and existing build/test output; never invent validation results."""
from __future__ import annotations
import hashlib
import json
import pathlib
import subprocess
import zipfile
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts'
DIST = ARTIFACTS / 'distribution'
DIST.mkdir(parents=True, exist_ok=True)

def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode().strip()

def digest(data):
    return hashlib.sha256(data).hexdigest()

def archive(name, files):
    output = DIST / name
    expected = {}
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path, relative in sorted(files, key=lambda item: item[1]):
            content = path.read_bytes()
            info = zipfile.ZipInfo(relative, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            z.writestr(info, content)
            expected[relative] = digest(content)
    with zipfile.ZipFile(output) as z:
        assert z.testzip() is None, name
        assert {n: digest(z.read(n)) for n in z.namelist()} == expected, name
    return {'name': name, 'files': len(expected), 'bytes': output.stat().st_size, 'sha256': digest(output.read_bytes()), 'verified': True}

tracked = [name for name in git('ls-files', '-z', '.').split('\0') if name]
source = [(ROOT / name, name) for name in tracked if (ROOT / name).is_file()]
identity = {'commit': git('rev-parse', 'HEAD'), 'sourceTree': git('rev-parse', 'HEAD:standalone/Avalonia.Inking'), 'generatedUtc': datetime.now(timezone.utc).isoformat()}
(ARTIFACTS / 'source-identity.json').write_text(json.dumps(identity, indent=2))
source_hashes = {name: digest(path.read_bytes()) for path, name in source}
(ARTIFACTS / 'source-files.sha256.json').write_text(json.dumps(source_hashes, indent=2, sort_keys=True))
outputs = [archive('Avalonia.Inking-source.zip', source)]

browser = ARTIFACTS / 'browser'
roots = [p.parent for p in browser.rglob('index.html') if (p.parent / '_framework').is_dir()]
if roots:
    root = sorted(roots, key=lambda p: len(p.parts))[0]
    outputs.append(archive('Avalonia.Inking-browser.zip', [(p, p.relative_to(root).as_posix()) for p in root.rglob('*') if p.is_file()]))
packages = ARTIFACTS / 'packages'
if packages.is_dir():
    outputs.append(archive('Avalonia.Inking-packages.zip', [(p, p.name) for p in packages.iterdir() if p.is_file()]))

reports = {}
for name in ['core', 'core-browser', 'browser']:
    file = ARTIFACTS / 'validation' / (name + '.json')
    if file.exists():
        report = json.loads(file.read_text())
        reports[name] = {key: report.get(key) for key in ['total', 'passed', 'failed']}
    else:
        reports[name] = {'status': 'not executed or no report produced'}
summary = [
    '# Avalonia Inking validation', '',
    f"Source commit: `{identity['commit']}`", f"Standalone source tree: `{identity['sourceTree']}`", '',
    '| Suite | Passed | Total | Failed |', '| --- | ---: | ---: | ---: |'
]
for name, r in reports.items():
    summary.append(f"| {name} | {r.get('passed', 'Not run')} | {r.get('total', '—')} | {r.get('failed', '—')} |")
summary += ['', 'Browser interaction tests target the published Avalonia WebAssembly application, not a DOM substitute.',
            'Pen and touch input are synthetic Chromium/CDP events; physical digitizer and all-browser certification are not implied.',
            'Desktop compilation is a build check, not a hardware/device acceptance test.',
            'Inspect individual JSON tests and the workflow conclusion. Missing reports are not passes.', '']
(ARTIFACTS / 'validation.md').write_text('\n'.join(summary))
evidence = [(p, p.relative_to(ARTIFACTS).as_posix()) for p in (ARTIFACTS / 'validation').rglob('*') if p.is_file()]
evidence += [(ARTIFACTS / n, n) for n in ['source-identity.json', 'source-files.sha256.json', 'validation.md']]
outputs.append(archive('Avalonia.Inking-validation.zip', evidence))
manifest = {'source': identity, 'reports': reports, 'archives': outputs}
(DIST / 'manifest.json').write_text(json.dumps(manifest, indent=2))
(DIST / 'SHA256SUMS.txt').write_text(''.join(f"{r['sha256']}  {r['name']}\n" for r in outputs))
(DIST / 'final-archive-verification.txt').write_text('\n'.join(f"PASS {r['name']}: reopened ZIP, {r['files']} files, every extracted byte SHA-256 verified" for r in outputs) + '\n')
print(json.dumps(manifest, indent=2))
