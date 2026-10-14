using Avalonia;
using Avalonia.Browser;
namespace InkingStudio.Browser;
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // main.js registers the already imported storage module before starting managed code.
        // Do not resolve relative JSHost imports against the runtime's _framework directory.
        App.StorageFactory = _ => new BrowserStudioStorage();
        await AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");
    }
}
