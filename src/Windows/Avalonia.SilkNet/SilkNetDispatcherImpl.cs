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
            while (!cancellationToken.IsCancellationRequested)
            {
                // Process input and window events
                SilkNetPlatform.Instance.DoEvents();

                // Fire timer if due
                if (_dueTimeInMs.HasValue && Now >= _dueTimeInMs.Value)
                {
                    _dueTimeInMs = null;
                    FireTimer();
                }

                // Process pending dispatcher jobs
                if (_isSignaled)
                {
                    _isSignaled = false;
                    bool hasSignaledSubscribers = Signaled != null;
                    if (hasSignaledSubscribers)
                    {
                        Signaled!.Invoke();
                    }
                }

                // Sleep to avoid CPU pegging
                _event.WaitOne(1);
            }
        }

        public long Now => _clock.ElapsedMilliseconds;
    }
}
