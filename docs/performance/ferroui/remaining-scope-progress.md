# Applicable remaining designs: implementation validation

All work remains in fork PR #8 on `perf/ferroui-runtime-optimizations`. Source base remains `a9429a328057befa287ffb5e981f58b86a86eda0`.

Implemented since the earlier scope report: [typed template bindings](typed-template-bindings.md), [resource resolution caching](resource-lookup.md), [selector evaluation plans](selector-plans.md), [shared native glyph geometry and blobs](shared-native-glyphs.md), [default line-metrics reuse and adaptive cache probing](default-line-metrics.md), and [real browser validation](browser-validation.md). Compile-time counters now cover property/binding/selector/resource work, shaping, native glyph reuse/blob creation, line metrics, and layout/compositor passes. These types and call sites are preprocessor-excluded from ordinary builds.

The browser workflow now runs interpreter, AOT, and named-interpreter deployments separately. Each mode publishes the identical fixture against baseline and head, and validates both software and default rendering. A deployment matrix definition is not a successful measurement; the exact run evidence will be recorded after completion. Application and ControlCatalog defaults remain unchanged.

Verified checkpoints: `48d1839f89d6e3d184e421958833c2390cc54c60` passed six Release suites and affected .NET 8 libraries in run 37897046082. Shared-glyph Skia tests at `02b5bfc1b9be942dc8ea8825959f74805edf5c17` passed in run 37897997008. The initial browser fixture at `dbc15971ecf35501d5634a5e35d55057ab15891d` passed real-input and exact light/dark screenshot checks in run 37898719933. The line-metrics integration passed Base and Skia tests before its same-branch commit `ad1c32edc12bfb24bf19b3008102ec416354f58a` in run 37901862119. Newer source still needs its own complete validation.

No result from the earlier branch can be relabeled as the performance of these additions. In particular, the earlier 336-byte shaping allocation result predates the line-metrics metadata on ShapedBuffer; current measurements must account for that metadata and weigh it against avoided layout work.
