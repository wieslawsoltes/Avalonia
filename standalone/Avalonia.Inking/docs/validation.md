# Reproducing validation

All test programs exit nonzero on failure. A successful publish is not itself a browser test result.

```sh
dotnet run --project tests/Avalonia.Inking.Specs -c Release -- artifacts/validation/core.json
dotnet run --project tests/Avalonia.Inking.HeadlessSpecs -c Release -- artifacts/validation/headless.json
dotnet run --project tests/InkingStudio.UiSpecs -c Release -- artifacts/validation/studio-ui.json
dotnet publish samples/InkingStudio.Browser -c Release -o artifacts/browser
python -m pip install -r validation/requirements.txt
python -m playwright install chromium --with-deps
python validation/browser.py
python validation/layers.py
```

The core executable checks geometry, immutable documents, transactions, persistence and spatial queries. The same suite executes again inside the actual published WebAssembly application.

The native headless control suite checks template replacement, detachment, capture cancellation, session replacement, routed events, read-only rules, straightedge projection and touch input. The separate studio UI suite creates the actual shared sample view and verifies layer command bindings and pointer clicks, order/removal, undo and read-only behavior.

The browser suite uses genuine Chromium mouse/keyboard events, synthetic CDP pen and touch input, clipboard permissions, actual file inputs/downloads and pixel inspection. The focused layer regression verifies that the add button's center hits the button rather than an overlay scrollbar, and that one real press/release causes one command invocation. It does not move the test click to an artificially exposed edge or invoke the command directly.

`validation/package-consumer.sh` restores locally built packages into a fresh package cache in a separate directory, then compiles and executes a consumer that references the themes package and its transitive controls/core packages. `validation/package.py` packages tracked source and actual evidence, reopens every ZIP, checks CRC and verifies the SHA-256 of each extracted entry against the input bytes.

The distribution includes machine-readable reports, screenshots, exported files, build logs, an exact commit/subtree identity, and archive checksums. Missing reports are explicitly marked as not executed. Read the workflow conclusion and the complete report, not just a screenshot or a successful compilation line.

Physical digitizer latency, every browser engine, hardware-specific palm rejection and platform screen readers require additional device acceptance testing. The documented 10,000-stroke timings are observations of that runner and scene, not universal performance guarantees.
