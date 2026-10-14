import { dotnet } from './_framework/dotnet.js';
try {
  const runtime = await dotnet.withDiagnosticTracing(false).withApplicationArgumentsFromQuery().create();
  const config = runtime.getConfig();
  await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  const bridge = exports.Kanban.Sample.Browser.BrowserBridge;
  globalThis.kanbanTest = { snapshot: () => JSON.parse(bridge.Snapshot()), action: (name, value = '') => bridge.Action(name, String(value)) };
  document.getElementById('loading').remove();
  globalThis.kanbanReady = true;
} catch (error) {
  console.error(error); const el = document.getElementById('loading');
  if (el) { el.className = 'error'; el.textContent = `Unable to start Atlas: ${error.message}`; }
  throw error;
}
