using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.IntegrationTests.SilkNet.Infrastructure;

namespace Avalonia.IntegrationTests.SilkNet
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            System.IO.File.AppendAllText(logPath, $"[PROGRAM] Main started on thread {Thread.CurrentThread.ManagedThreadId}\n");

            int exitCode = 0;
            var testTask = Task.Run(async () =>
            {
                // Wait for the dispatcher loop to be initialized and running
                await AppManager.EnsureAppInitializedAsync().ConfigureAwait(false);

                try
                {
                    System.IO.File.AppendAllText(logPath, "[PROGRAM] Running test platform on background thread\n");
                    if (args.Any(arg => arg == "-automated" || arg == "@@"))
                    {
                        exitCode = Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
                    }
                    else
                    {
                        exitCode = Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
                    }
                    System.IO.File.AppendAllText(logPath, $"[PROGRAM] Test platform finished with exitCode={exitCode}\n");
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(logPath, $"[PROGRAM] Test platform exception: {ex}\n");
                    exitCode = -1;
                }
                finally
                {
                    AppManager.Stop();
                }
            });

            // Start the main loop on the main thread
            AppManager.StartMainLoop();

            // Wait for tests to finish
            testTask.GetAwaiter().GetResult();

            System.IO.File.AppendAllText(logPath, $"[PROGRAM] Main exiting with exitCode={exitCode}\n");
            return exitCode;
        }
    }
}
