# Demand-driven browser render timer

The render loop already sets `IRenderTimer.Tick` to null when its tasks have no work and
sets it again on `Wakeup`. The browser adapter previously ignored this lifecycle: after
its first start, JavaScript requested another animation frame forever. It also omitted
the timestamp argument when invoking the managed export.

The adapter now starts/stops its RAF chain on active callback transitions and passes the
actual timestamp. Animations, diagnostic overlays, grace ticks, custom render tasks and
compositor invalidation remain controlled by the existing render loop; none is disabled.
Cancelled callbacks carry a generation so a late delivery cannot disrupt a restarted
chain. Start/stop are idempotent, including RAF handle zero. A callback can stop/restart
the timer without scheduling duplicate successors.

For threaded applications, JS calls are posted to the render worker's captured
SynchronizationContext. Posts are asynchronous and coalesced, and read the latest
callback rather than replaying stale start/stop commands. A host without a worker posting
context keeps the previous polling behavior rather than losing cross-thread wakeups.
This fallback is deliberate; zero idle callbacks is not promised for such a host.

Seven Node tests execute the actual TypeScript RAF scheduler using Node type stripping.
Seven managed tests link the production timer source with only host/JS interop stubs,
covering pre-initialization wakeups, coalescing, restart and worker fallbacks. These host
tests do not substitute for running a threaded WebAssembly browser.

The original browser benchmark inputs, timings, three-second idle intervals and
RAF counter instrumentation are unchanged. Real browser input/pixel/resource validation
must run on the new build before claiming measured CPU savings or deployment acceptance.
