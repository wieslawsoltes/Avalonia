# Browser profiling: reconstruct timestamps before assigning durations

## Observed failure

The symbol-retaining interpreter job [113855330633](https://github.com/wieslawsoltes/Avalonia/actions/runs/37941087407/job/113855330633), at `ad96c386f5753545143c8844a76fb257e2ad1d5a`, passed publishing, real input, resource/pixel equality and the unprofiled A/B and same-build A/A runs. Its first separate CPU-profile summary then failed with `Invalid sample duration` in `summarizeProfile`. This is not a rendering failure, nor a successful complete profiling run.

## Cause and repair

The old helper treated each CDP `timeDeltas` entry as a non-negative duration belonging to the sampled node. The [CDP Profile definition](https://chromedevtools.github.io/devtools-protocol/tot/Profiler/#type-Profile) instead defines differences between sample timestamps, with the first relative to `startTime`. Chrome DevTools' [CPUProfileDataModel](https://github.com/ChromeDevTools/devtools-frontend/blob/main/front_end/models/cpu_profile/CPUProfileDataModel.ts) reconstructs timestamps and sorts the associated samples before deriving intervals. Out-of-order samples can therefore have a negative delta without representing negative execution time.

`profile-timeline.mjs` reconstructs absolute timestamps, stably sorts timestamp/node pairs and estimates each sample's forward interval to the next observation. The last interval ends at the actual recorded `endTime`; the interval before the first sample is explicitly unattributed. It preserves all nodes and sample counts, including coincident zero-length intervals. It neither clamps negative deltas nor discards samples, and does not mutate the raw profile. Unknown nodes, unsafe/non-finite deltas, mismatched arrays and reconstructed timestamps outside recording boundaries remain errors.

Profile timing metadata is versioned and records negative deltas, reordered/coincident samples, sampled time and unattributed time. This is a corrected diagnostic self-time convention, so previous per-frame summaries must not be compared as though they used the same algorithm. Raw `.cpuprofile` files remain available for independent interpretation.

Seven new timeline tests cover inversions, stable coincident samples, singleton/empty profiles, invalid structures/deltas/bounds, conservation of recorded duration and frozen input. The existing summary test now verifies forward intervals rather than assigning the pre-sample delay to the first frame.

## Scope

No unprofiled native or browser scenario, input sequence, timer, warm-up, operation count, screenshot assertion, calibration rule or performance threshold changes. This fixes the diagnostics pipeline; it is not counted as an Avalonia speedup. The repaired helper and full browser workflow must pass at their own revision before profiling is reported complete.
