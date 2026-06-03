using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform;
using Silk.NET.Windowing;

namespace Avalonia.SilkNet
{
    internal class SilkNetScreen : PlatformScreen
    {
        public SilkNetScreen(double scaling, PixelRect bounds, PixelRect workingArea, bool isPrimary, string displayName)
            : base(new PlatformHandle(IntPtr.Zero, "SilkNetMonitor"))
        {
            Scaling = scaling;
            Bounds = bounds;
            WorkingArea = workingArea;
            IsPrimary = isPrimary;
            DisplayName = displayName;
        }
    }

    internal class SilkNetScreenImpl : IScreenImpl
    {
        public int ScreenCount
        {
            get
            {
                try
                {
                    var glfw = Silk.NET.GLFW.Glfw.GetApi();
                    unsafe
                    {
                        glfw.GetMonitors(out int count);
                        return count;
                    }
                }
                catch
                {
                    return 1;
                }
            }
        }

        public IReadOnlyList<Screen> AllScreens
        {
            get
            {
                string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
                System.IO.File.AppendAllText(logPath, $"[SCREEN] Querying screens on thread {System.Threading.Thread.CurrentThread.ManagedThreadId}\n");
                var screens = new List<Screen>();
                try
                {
                    var glfw = Silk.NET.GLFW.Glfw.GetApi();
                    unsafe
                    {
                        var monitors = glfw.GetMonitors(out int count);
                        System.IO.File.AppendAllText(logPath, $"[SCREEN] Found {count} monitors\n");
                        for (int i = 0; i < count; i++)
                        {
                            var m = monitors[i];
                            glfw.GetMonitorPos(m, out int x, out int y);
                            glfw.GetMonitorWorkarea(m, out int wx, out int wy, out int wwidth, out int wheight);
                            var vm = glfw.GetVideoMode(m);
                            string name = glfw.GetMonitorName(m);

                            var isPrimary = i == 0;
                            float xscale, yscale;
                            glfw.GetMonitorContentScale(m, out xscale, out yscale);
                            var scaling = (double)xscale;

                            var fullBounds = vm != null
                                ? new PixelRect((int)(x * scaling), (int)(y * scaling), (int)(vm->Width * scaling), (int)(vm->Height * scaling))
                                : new PixelRect((int)(wx * scaling), (int)(wy * scaling), (int)(wwidth * scaling), (int)(wheight * scaling));

                            var workingArea = new PixelRect((int)(wx * scaling), (int)(wy * scaling), (int)(wwidth * scaling), (int)(wheight * scaling));

                            System.IO.File.AppendAllText(logPath, $"[SCREEN] Monitor {i}: Name={name}, Primary={isPrimary}, Scaling={scaling}, FullBounds={fullBounds}, WorkingArea={workingArea}\n");

                            screens.Add(new SilkNetScreen(scaling, fullBounds, workingArea, isPrimary, name));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(logPath, $"[SCREEN] Error: {ex.Message}\n");
                    var isPrimary = true;
                    var fullBounds = new PixelRect(0, 0, 1920, 1080);
                    screens.Add(new SilkNetScreen(1.0, fullBounds, fullBounds, isPrimary, "Display Fallback"));
                }
                return screens;
            }
        }

        public Action? Changed { get; set; }

        public Screen? ScreenFromWindow(IWindowBaseImpl window) => ScreenFromTopLevel(window);

        public Screen? ScreenFromTopLevel(ITopLevelImpl topLevel)
        {
            return AllScreens.FirstOrDefault();
        }

        public Screen? ScreenFromPoint(PixelPoint point)
        {
            return AllScreens.FirstOrDefault(s => s.Bounds.Contains(point)) ?? AllScreens.FirstOrDefault();
        }

        public Screen? ScreenFromRect(PixelRect rect)
        {
            return AllScreens.FirstOrDefault(s => s.Bounds.Intersects(rect)) ?? AllScreens.FirstOrDefault();
        }

        public Task<bool> RequestScreenDetails() => Task.FromResult(true);
    }
}
