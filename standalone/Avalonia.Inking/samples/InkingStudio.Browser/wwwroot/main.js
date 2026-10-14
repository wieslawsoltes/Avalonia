import { dotnet } from './_framework/dotnet.js';
import * as storageHost from './host.js';

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
    runtime.setModuleImports('studio-host', storageHost);
    const config = runtime.getConfig();
    const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    // Sample-only diagnostics. Pointer tests enter through the actual browser input pipeline.
    window.inkTest = exports.InkingStudio.Browser.Probe;
    await runtime.runMain(config.mainAssemblyName, [location.href]);
} catch (error) { report(error); }
