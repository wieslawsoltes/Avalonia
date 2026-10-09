import { JsExports } from "./jsExports";
import { AnimationFrameLoop } from "./animationFrameLoop";

export class TimerHelper {
    private static readonly loop = new AnimationFrameLoop(
        callback => self.requestAnimationFrame(callback),
        handle => self.cancelAnimationFrame(handle),
        time => JsExports.TimerHelper?.JsExportOnAnimationFrame(time));

    public static runAnimationFrames(): void { TimerHelper.loop.start(); }
    public static stopAnimationFrames(): void { TimerHelper.loop.stop(); }
}
