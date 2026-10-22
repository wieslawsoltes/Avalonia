# Browser regression diagnostics

The original `compare.mjs`, fixture, deployment settings, three timed baseline/head pairs, input sequences, screenshots and task-CPU arithmetic remain unchanged. A separate workflow step runs `diagnose.mjs` after those measurements.

The diagnostic repeats the unmodified comparison with the exact published head as both inputs (A/A). This measures run-to-run variation, not independent-build variation. Raw run identities, all six scenario names, paired CPU samples, backend identity, idle duration, pixel parity and deployment inventory/code identities are validated. The original and A/A reports are fingerprinted; every actual published file and each separate profile receives a SHA-256 digest. A final fingerprint check rejects files changed during diagnostics.

The audit retains every pair and the original absolute CPU values, resource/list state and prepare/clear counts. Different final scroll offsets, extents, checksums or realized counts are marked inconclusive rather than mistaken for a speedup. The descriptive screen calls a slowdown consistent only when every ratio exceeds the maximum symmetric A/A factor plus a fixed 5% margin. A/A variation over 10% makes other results explicitly inconclusive. Neither threshold changes the original native screen or browser benchmark. This is not a confidence interval, all-workload guarantee or timing gate; report generation can succeed with unresolved observations.

Separate exact-build Chrome CPU profiles cover **list/tree wheel and thumb input, and idle with the overlay off/on**, in both Software2D and default/WebGL, for both revisions. Profiling occurs outside the unprofiled pairs. Each profile has raw before/after snapshots and a sampled-self-time summary retaining call frame identities. Sampled durations must not replace task CPU, and unknown wasm indices must not be aligned across different native links. Named-interpreter outputs are diagnostic deployments, not substituted production timings. SwiftShader can be software; none of this measures physical GPU presentation, frame rate or power.

Reproduce after publishing and the original comparison:

```sh
node --test scripts/performance/browser/*.test.mjs
node scripts/performance/browser/diagnose.mjs /path/to/browser-builds /path/to/browser-results /path/to/browser-diagnostics
```

Eight new Node test cases cover consistently slow results, noisy A/A, mixed pairs, zero CPU, malformed/missing/duplicate runs, environment/deployment/pixel drift, different timed scroll endpoints and malformed CPU profiles. No outlier is dropped and no unfavorable scenario is renamed or removed.
