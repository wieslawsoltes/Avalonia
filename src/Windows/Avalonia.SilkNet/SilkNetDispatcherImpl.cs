using System;
using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;

namespace Avalonia.SilkNet
{
    internal class SilkNetDispatcherImpl : IControlledDispatcherImpl
    {
        private static Thread? s_uiThread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly AutoResetEvent _event = new(false);

        public SilkNetDispatcherImpl()
        {
            s_uiThread = Thread.CurrentThread;
        }

        public bool CurrentThreadIsLoopThread => s_uiThread == Thread.CurrentThread;

        private volatile bool _isSignaled;

        public void Signal()
        {
            _isSignaled = true;
            _event.Set();
        }

        public event Action? Signaled;
        public event Action? Timer;

        public void FireTimer() => Timer?.Invoke();

        private long? _dueTimeInMs;

        public void UpdateTimer(long? dueTimeInMs)
        {
            _dueTimeInMs = dueTimeInMs;
        }

        public bool CanQueryPendingInput => false;
        public bool HasPendingInput => false;

        public void RunLoop(CancellationToken cancellationToken)
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            System.IO.File.AppendAllText(logPath, "[DISPATCHER] RunLoop started\n");
            while (!cancellationToken.IsCancellationRequested)
            {
                // Process input and window events
                SilkNetPlatform.Instance.DoEvents();

                // Fire timer if due
                if (_dueTimeInMs.HasValue && Now >= _dueTimeInMs.Value)
                {
                    _dueTimeInMs = null;
                    System.IO.File.AppendAllText(logPath, "[DISPATCHER] Firing timer\n");
                    FireTimer();
                }

                // Process pending dispatcher jobs
                if (_isSignaled)
                {
                    _isSignaled = false;
                    bool hasSignaledSubscribers = Signaled != null;
                    if (hasSignaledSubscribers)
                    {
                        System.IO.File.AppendAllText(logPath, "[DISPATCHER] Invoking Signaled handler\n");
                        Signaled!.Invoke();
                    }
                }

                // Sleep to avoid CPU pegging
                _event.WaitOne(1);
            }
            System.IO.File.AppendAllText(logPath, "[DISPATCHER] RunLoop exited\n");
        }

        public long Now => _clock.ElapsedMilliseconds;
    }
}
