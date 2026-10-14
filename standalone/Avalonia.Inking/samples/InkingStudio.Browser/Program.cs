using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
namespace InkingStudio.Browser;
internal static class Program
{
    private static async Task Main(string[] args)
    {
        await JSHost.ImportAsync("studio-host", "./host.js");
        App.StorageFactory = _ => new BrowserStudioStorage();
        await AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");
    }
}
