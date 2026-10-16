/** A demand-driven RAF chain; all calls belong to one Window/Worker realm. */
export class AnimationFrameLoop {
    private handle: number | undefined;
    private generation = 0;
    private running = false;

    private readonly request: (callback: FrameRequestCallback) => number;
    private readonly cancel: (handle: number) => void;
    private readonly tick: (timestamp: number) => void;

    public constructor(request: (callback: FrameRequestCallback) => number,
        cancel: (handle: number) => void, tick: (timestamp: number) => void) {
        this.request = request;
        this.cancel = cancel;
        this.tick = tick;
    }

    public start(): void {
        if (this.running) { return; }
        this.running = true;
        const generation = ++this.generation;
        const render = (time: number): void => {
            // A cancelled callback can already have been dispatched when stop/restart runs.
            // It must not clear the new chain's handle, invoke managed code or schedule twice.
            if (!this.running || generation !== this.generation) { return; }
            this.handle = undefined;
            try { this.tick(time); }
            finally {
                if (this.running && generation === this.generation && this.handle === undefined) {
                    this.handle = this.request(render);
                }
            }
        };
        this.handle = this.request(render);
    }

    public stop(): void {
        this.running = false;
        ++this.generation;
        if (this.handle !== undefined) {
            this.cancel(this.handle);
            this.handle = undefined;
        }
    }
}
