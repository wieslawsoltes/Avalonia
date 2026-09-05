#!/usr/bin/env python3
"""Finalize permanent maintenance tooling and accurate package/resource guidance."""
from pathlib import Path
import json
import subprocess
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    subprocess.run(['python3', 'eng/macos26/update-tokens.py'], cwd=ROOT, check=True)
    path = ROOT / 'eng/macos26/audit.py'
    text = path.read_text()
    if 'update-tokens.py' not in text:
        text = text.replace('import re\n', 'import re\nimport subprocess\nimport sys\n')
        text = text.replace('def main():\n', "def main():\n    subprocess.run([sys.executable, str(ROOT / 'eng/macos26/update-tokens.py'), '--check'], check=True)\n")
    path.write_text(text)
    path = ROOT / 'docs/themes/macos26/README.md'
    text = path.read_text()
    manifest = json.loads((ROOT / 'src/Avalonia.Themes.MacOS/Tokens/token-manifest.json').read_text())
    count = len(manifest['tokens'])
    text = text.replace('bring the current inventory to 1,050 keys.', f'bring the current inventory to {count:,} keys, including the optional ColorPicker visual values.')
    text = text.replace('Legacy Fluent-style value keys are normalized on theme lookup for compatibility\nwith existing application resources; new application code should use `MacOS.*`.',
        'Legacy Fluent-style value keys are normalized for **resource reads** from the theme.\nThis does not translate writes to application dictionaries: an unprefixed\n`Application.Resources["ButtonPadding"]` does not override `MacOS.ButtonPadding`.\nUse `MacOS.*` keys or the typed API for overrides.')
    if '## Maintaining the token contract' not in text:
        text += '''
## Maintaining the token contract

Add or edit `MacOS.*` visual resources in the checked-in XAML, then run:

```sh
python3 eng/macos26/update-tokens.py
python3 eng/macos26/update-tokens.py --check
python3 eng/macos26/audit.py
```

The permanent generator preserves existing public names and rejects silent token
removal, CLR-type changes and C# identifier collisions. It updates the typed keys,
source inventory and optional ColorPicker control inventory. CI detects generated
contract drift. Python is not needed to compile or consume the theme package.

The core assembly owns ColorPicker's visual-value defaults using only Avalonia
core types. Its optional companion owns the control templates and converters.
This keeps token overrides effective without introducing a runtime dependency
from the core theme to ColorPicker or from ColorPicker to the theme assembly.

`MacOS.Color.Accent` is a contrast-safe filled-surface derivative of the raw OS
`MacOS.SystemAccentColor`. Medium/dark accents favor white labels; bright accents
retain black labels. The supported test palette verifies at least 4.5:1 opaque
fill/label contrast. This does not constitute a full application accessibility
audit. Raw OS accent tokens remain available for application-specific use.

For switches, `MacOS.Switch.Travel` is the distance expected by Avalonia's actual
ToggleSwitch template contract. When changing switch geometry together, preserve
`Travel = TrackWidth - TrackHeight` and keep the knob within its travel container.

## Development packages versus production packaging

`dotnet pack` produces the repository's **intermediate** packages. The theme's
intermediate package depends on matching `Avalonia.Base`, `Avalonia.Controls`,
`Avalonia.Dialogs` and `Avalonia.Markup.Xaml` packages. It is not a standalone
replacement for an arbitrary public Avalonia release. The repository's existing
production pipeline uses `nukebuild/numerge.json` to merge the framework package
and rewrite dependent packages. That pipeline remains authoritative for publishing.

`eng/macos26/package-smoke.py` builds a matching intermediate dependency feed,
restores a fresh package-only application with an isolated NuGet cache, loads the
packaged compiled XAML and verifies a live token update. It publishes no packages.
Its evidence is recorded in `artifacts/macos-theme/package-consumption.json`.

## Screenshot fidelity

The refined capture harness renders at 2× / 192 DPI. This is an offscreen render
scale, not evidence of native Retina compositor behavior. Headless captures use
an explicitly selected Inter fallback from the repository's existing font package;
native captures use the platform default. Each frame records its render scale,
font policy, operating system, backend, source commit and SHA-256.

The catalog's Customize appearance popover changes appearance, density, contrast,
motion, transparency and accent tokens live. It is functional UI, not a screenshot
of a settings panel. No Apple font or SF Symbols files are distributed by this theme.
'''
    path.write_text(text)
    path = ROOT / 'src/Avalonia.Themes.MacOS/README.md'
    text = path.read_text()
    if 'intermediate package' not in text:
        text += '\nDirect `dotnet pack` output is an intermediate package requiring a matching\nframework feed. Production publishing uses the repository\'s existing Numerge\npipeline. See the repository guide for the isolated package-consumer test.\n'
    path.write_text(text)
    commit('docs(macos): finalize deterministic token maintenance and development-feed contracts',
           'src/Avalonia.Themes.MacOS', 'eng/macos26/audit.py', 'docs/themes/macos26')

if __name__ == '__main__':
    main()
