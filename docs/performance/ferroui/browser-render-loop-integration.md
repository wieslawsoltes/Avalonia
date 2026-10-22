# Browser timer wakeup and render-loop integration

The demand timer uses the render worker's captured synchronization context to marshal
start/stop operations to its JavaScript realm. A non-null *base* SynchronizationContext
is not such a context: its default Post method schedules thread-pool work. This host
case now uses the same safe polling fallback as a missing context. Real browser workers
provide their JavaScript event-loop context. No arbitrary context-type name or reflection
against an internal runtime implementation is required.

A throwing Post still propagates its exception. The coalescing flag is reset so a later
explicit timer update can retry instead of leaving the timer permanently unwakeable.
This is not automatic recovery from worker termination or arbitrary host dispatch errors.

Five tests complement the seven existing managed timer tests and seven TypeScript RAF
scheduler tests. The new integration cases use the actual DefaultRenderLoop and linked
production BrowserRenderTimer (only JS interop is stubbed). Continuous render work keeps
the timer active; returning no work sleeps it; Wakeup during Render preserves the next
tick; removing and readding the last task restarts cleanly; stale fired callbacks do no
work. Base-context fallback and Post-failure recovery have explicit regression tests.

The earlier real-browser interpreter validation at `0d35c08` reported 180 -> 0 RAF
callbacks in all four unchanged three-second idle intervals and zero screenshot mismatches
([job 113948566530](https://github.com/wieslawsoltes/Avalonia/actions/runs/37968425305/job/113948566530)).
Those observations validate that earlier revision, not this later host-edge-case change.
The normal browser workload, warm-up and time windows are unchanged. Host tests establish
render-loop state transitions; they do not certify a threaded WebAssembly application.
