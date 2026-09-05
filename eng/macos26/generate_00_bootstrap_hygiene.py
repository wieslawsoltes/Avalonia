#!/usr/bin/env python3
"""Keep one-time source migration commits restricted to source and documentation."""
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'eng/macos26'


def main():
    path = DIRECTORY / 'generate_59_package_consumer.py'
    text = path.read_text()
    start = text.find("    path = ROOT / '.github/workflows/macos-theme.yml'")
    if start >= 0:
        end = text.index('    commit(', start)
        text = text[:start] + text[end:]
        text = text.replace(", '.github/workflows/macos-theme.yml')", ")")
        path.write_text(text)
    (DIRECTORY / '.gitignore').write_text('__pycache__/\n*.pyc\n')
    shutil.rmtree(DIRECTORY / '__pycache__', ignore_errors=True)
    subprocess.run(['git', 'rm', '-r', '--cached', '--ignore-unmatch', 'eng/macos26/__pycache__'], cwd=ROOT, check=True)
    subprocess.run(['git', 'add', '--', 'eng/macos26/generate_59_package_consumer.py', 'eng/macos26/.gitignore'], cwd=ROOT, check=True)
    result = subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=ROOT)
    if result.returncode == 1:
        subprocess.run(['git', 'commit', '-m', 'build(macos): remove generated caches and isolate source migration writes'], cwd=ROOT, check=True)
    elif result.returncode:
        raise RuntimeError('Unable to inspect staged source changes')

if __name__ == '__main__':
    main()
