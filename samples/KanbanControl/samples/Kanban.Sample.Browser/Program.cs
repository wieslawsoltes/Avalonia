using Avalonia;
using Avalonia.Browser;
using Kanban.Sample;
internal sealed partial class Program
{
    private static Task Main(string[] args) => AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");
}
