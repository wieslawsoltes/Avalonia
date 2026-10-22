const nativeRaf = window.requestAnimationFrame.bind(window);
const frameCounts = { requested: 0, completed: 0 };
window.requestAnimationFrame = callback => {
    ++frameCounts.requested;
    return nativeRaf(time => { ++frameCounts.completed; callback(time); });
};
try {
    const { dotnet } = await import('./_framework/dotnet.js');
    const runtime = await dotnet.create();
    const config = runtime.getConfig();
    const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    await runtime.runMain(config.mainAssemblyName, [location.href]);
    const api = exports.FerroUi.Browser.Performance.Program;
    window.ferroPerf = {
        snapshot: () => JSON.parse(api.Snapshot()),
        offset: value => api.SetOffset(value),
        scenario: value => api.SetScenario(value),
        theme: dark => api.SetTheme(dark),
        resource: alternate => api.ChangeResource(alternate),
        overlay: enabled => api.SetOverlay(enabled),
        frames: () => ({ ...frameCounts }),
        // Harness waits are not charged to application RAF counts.
        settle: () => new Promise(resolve => nativeRaf(() => nativeRaf(resolve)))
    };
    await window.ferroPerf.settle();
    window.ferroPerfReadyAt = performance.now();
} catch (error) {
    window.ferroPerfError = String(error?.stack ?? error);
    const element = document.getElementById('error');
    element.hidden = false;
    element.textContent = window.ferroPerfError;
    throw error;
}
