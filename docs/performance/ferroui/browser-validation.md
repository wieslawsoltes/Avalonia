# Designs 07–09: real browser validation and deployment experiments

The standalone `samples/FerroUi.Browser.Performance` application uses the repository's browser/Skia backend and Simple theme. It contains five-column bound ListBox rows and expanded TreeView data. It does not alter the published ControlCatalog or its default FPS overlay. JS exports expose measured state and deterministic reset controls; JavaScript instrumentation counts application RAF callbacks without charging harness frame waits to those counters.

`build-browser.py` publishes exactly the same harness sources against the original baseline and current branch, retaining source hashes and revision IDs. `browser/compare.mjs` serves both outputs on loopback, alternates three fresh-context pairs for software and default/WebGL rendering, sends real wheel input and real scrollbar-thumb drags, and verifies that offsets actually change. Rendering comparisons then reset to identical offsets and compare decoded PNG pixels exactly in light and dark themes. Matching screenshots do not stand in for input checks, and browser input coalescing is not required to produce identical intermediate offsets.

The report records first-ready time, per-scenario Chrome task CPU, three-second idle CPU/RAF counts with the overlay off/on, backend details, and file-by-file raw/gzip size. Precompressed duplicates and symbols are separated from runtime transfer estimates. Task CPU is not GPU time, and a double-RAF ready point is not a hardware presentation timestamp. Hosted Chromium can use SwiftShader; its results are not physical-GPU or user-device FPS claims.

The fork-only workflow installs the SDK WebAssembly workload, publishes both applications, runs semantic/input/pixel gates, and retains exact builds and measurement artifacts. Elapsed-time differences remain report-only. Optional workflow inputs enable `RunAOTCompilation` through `FerroUiBrowserAot`, or preserve symbols in the exact measured output through `FerroUiBrowserSymbols`. These are app-level experiments, not global changes to Avalonia deployment defaults. Run each setting and compare its complete size/startup/CPU report before selecting an application policy; no unmeasured AOT or SIMD win is assumed.

Reproduce with the SDK in `global.json` and initialized submodules:

```sh
dotnet workload install wasm-tools
cd scripts/performance/browser && npm install --ignore-scripts && npx playwright install chromium && cd ../../..
python3 scripts/performance/build-browser.py --output artifacts/browser-builds
node scripts/performance/browser/compare.mjs artifacts/browser-builds/base artifacts/browser-builds/head artifacts/browser-results 3
```

The browser harness intentionally does not bypass immutable-versus-mutable renderer semantics or promise idle scheduling behavior before measuring it. CI results must be read for the exact commit; adding this workflow alone is not evidence of a passing browser run.
