# Designs 07–09: real browser validation and deployment experiments

`samples/FerroUi.Browser.Performance` uses repository browser/Skia code and the Simple theme, with five-column bound ListBox rows and expanded TreeView data. It does not alter published ControlCatalog behavior or diagnostics defaults. The fixture exposes measured state, deterministic reset controls, actual theme/resource changes and overlay control. RAF instrumentation excludes harness frame waits.

`build-browser.py` publishes identical fixture sources against the exact original baseline and current branch, recording hashes and refs. `browser/compare.mjs` alternates three fresh-context pairs for Software2D and default/WebGL rendering, sends actual wheel input and scrollbar-thumb drags, and verifies that offsets move. It then compares decoded PNG pixels at equivalent states in light/dark themes. Dynamic resource replacement must visibly change foreground pixels and restoration must restore them, in addition to baseline/head equality.

Reports include first-ready timestamps, Chrome task CPU, three-second idle CPU and application RAF activity with overlay off/on, backend details and file-by-file raw/gzip runtime-size estimates. Precompressed duplicates and symbols are not double-counted. Invalid/absent numeric evidence fails; zero CPU baselines do not produce fabricated percentages. Separate post-measurement CPU profiles preserve function stacks without contaminating timed runs. Actual module name sections and code hashes are inspected for correlation with exact builds. See [browser-evidence.md](browser-evidence.md).

The fork workflow has three explicit deployment modes: interpreter, AOT (`RunAOTCompilation` through `FerroUiBrowserAot`), and symbol-retaining interpreter (`FerroUiBrowserSymbols`). Every mode builds/tests both revisions and both rendering paths, retaining reports, profiles, exact builds and the resolved npm lockfile. Five Node tests validate measurement arithmetic and module parsing before running Chromium. Modes are application-level experiments, not global library defaults; compare size, first-ready and workload results before choosing a deployment policy.

```sh
dotnet workload install wasm-tools
cd scripts/performance/browser
npm install --ignore-scripts
node --test evidence.test.mjs
npx playwright install chromium
cd ../../..
python3 scripts/performance/build-browser.py --output artifacts/browser-builds
node scripts/performance/browser/compare.mjs artifacts/browser-builds/base artifacts/browser-builds/head artifacts/browser-results 3
# Separate deployment outputs:
python3 scripts/performance/build-browser.py --aot --output artifacts/browser-aot
python3 scripts/performance/build-browser.py --symbols --output artifacts/browser-named
```

A double-RAF ready point is not a physical presentation timestamp. Browser task CPU is not GPU time, and a hosted default backend may use SwiftShader. RAF callbacks may continue while the compositor is idle; report their activity without assuming continuous rendering or an idle-loop optimization. Named flags are checked through emitted metadata rather than presumed successful. A workflow definition is not a passing run: use evidence for the exact evaluated revision.
