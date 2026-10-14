import { dotnet } from './_framework/dotnet.js';

window.inkStartupErrors = [];
function report(error) {
    const message = String(error?.stack || error);
    window.inkStartupErrors.push(message);
    console.error(message);
    const panel = document.getElementById('fatal');
    panel.style.display = 'block'; panel.textContent = message;
}
window.addEventListener('unhandledrejection', event => report(event.reason));
try {
    const runtime = await dotnet.withDiagnosticTracing(false).withApplicationArgumentsFromQuery().create();
    const config = runtime.getConfig();
    const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    // Sample-only diagnostics. Pointer tests still enter through the browser's actual input pipeline.
    window.inkTest = exports.InkingStudio.Browser.Probe;
    await runtime.runMain(config.mainAssemblyName, [location.href]);
} catch (error) { report(error); }
