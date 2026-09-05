#!/usr/bin/env python3
"""Remove one-time source migrations after their checked-in output is validated.
Workflow files are removed separately through the authorized GitHub connector.
"""
from pathlib import Path
import shutil
import subprocess
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    subprocess.run(['python3', 'eng/macos26/update-tokens.py'], cwd=ROOT, check=True)
    subprocess.run(['python3', 'eng/macos26/audit.py'], cwd=ROOT, check=True)
    directory = ROOT / 'eng/macos26'
    for path in [directory / 'initialize.py', *directory.glob('generate_*.py')]:
        path.unlink(missing_ok=True)
    shutil.rmtree(directory / '__pycache__', ignore_errors=True)
    commit('build(macos): remove completed source migrations and retain permanent maintenance tools',
           'eng/macos26', 'src/Avalonia.Themes.MacOS')

if __name__ == '__main__':
    main()
