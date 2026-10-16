# Browser profiling: reconstruct timestamps before assigning durations

## Observed failure

The symbol-retaining interpreter job [113855330633](https://github.com/wieslawsoltes/Avalonia/actions/runs/37941087407/job/113855330633), at `ad96c386f5753545143c8844a76fb257e2ad1d5a`, passed publishing, real input, resource/pixel equality and the unprofiled A/B and same-build A/A runs. Its first separate CPU-profile summary then failed with `Invalid sample duration` in `summarizeProfile`. This was not a rendering failure, nor a successful complete profiling run. The old error did not print the rejected delta, so the exact failed sample is not inferred from that message alone.

## Cause and repair

The old helper treated each CDP `timeDeltas` entry as a non-negative duration belonging to the sampled node. The [CDP Profile definition](https://chromedevtools.github.io/devtools-protocol/tot/Profiler/#type-Profile) instead defines differences between sample timestamps, with the first relative to `startTime`. Chrome DevTools' [CPUProfileDataModel](https://github.com/ChromeDevTools/devtools-frontend/blob/main/front_end/models/cpu_profile/CPUProfileDataModel.ts) reconstructs timestamps and sorts the associated samples before deriving intervals. Out-of-order samples can therefore have a negative delta without representing negative execution time.

`profile-timeline.mjs` reconstructs absolute timestamps, stably sorts timestamp/node pairs and estimates each sample's forward interval to the next observation. The last interval ends at the actual recorded `endTime`; the interval before the first sample is explicitly unattributed. It preserves all nodes and sample counts, including coincident zero-length intervals. It neither clamps negative deltas nor discards samples, and does not mutate the raw profile. Unknown nodes, unsafe/non-finite deltas, mismatched arrays and reconstructed timestamps outside recording boundaries remain errors.

Profile timing metadata is versioned and records negative deltas, reordered/coincident samples, sampled time and unattributed time. This is a corrected diagnostic self-time convention, so previous per-frame summaries must not be compared as though they used the same algorithm. Raw `.cpuprofile` files remain available for independent interpretation.

Seven new timeline tests cover inversions, stable coincident samples, singleton/empty profiles, invalid structures/deltas/bounds, conservation of recorded duration and frozen input. The existing summary test now verifies forward intervals rather than assigning the pre-sample delay to the first frame.

## Validation with actual out-of-order samples

At `ddcb036f011b0ec60dfa5da15a8cee8d2c5a1c0f`, the [named-interpreter job 113932061565](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061565) passed all 20 Node validation tests, original A/B input/pixel comparisons, same-build A/A and **all 24 separate profiles**. Two profiles actually exercised signed-delta handling:

| Profile | Negative deltas | Reordered samples | Unattributed initial interval |
|---|---:|---:|---:|
| head / Software2D / list-thumb | 1 | 2 | 12.738 ms |
| baseline / default-WebGL / list-wheel | 1 | 2 | 49.273 ms |

Both were summarized successfully without dropping or clamping either sample. The other 22 profiles in this job reported zero negative deltas, and all reported zero coincident samples. The [ordinary interpreter job](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061368) also completed all 24 profiles and the surrounding validation; its traces had no negative deltas. Thus the fix is exercised by both focused tests and actual recorded browser data, rather than only a favorable run without inversions.

The named-interpreter results archive [11632449464](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11632449464) retains the original profiles, per-frame summaries, versioned timing metadata, exact build/file hashes and unprofiled A/B/A/A reports. Its published SHA-256 is `faa6b61d19e03f2c70703851d6ee174c6919602570d4da60a5621b11ea0c4c4e` (14-day retention). The exact named builds are a separate 7-day artifact. Final all-mode validation and remaining timing uncertainty are recorded in [validation-paired-storage.md](validation-paired-storage.md).

## Scope

No unprofiled native or browser scenario, input sequence, timer, warm-up, operation count, screenshot assertion, calibration rule or performance threshold changed. This fixes the diagnostics pipeline; it is not counted as an Avalonia speedup. Profile self-time is sampled diagnostic wall-time attribution, not the independent task-CPU metric, GPU time or physical presentation.
