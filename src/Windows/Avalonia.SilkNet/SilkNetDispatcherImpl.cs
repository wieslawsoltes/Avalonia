using System;
using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;

namespace Avalonia.SilkNet
{
    internal class SilkNetDispatcherImpl : IControlledDispatcherImpl
    {
        private const int EventPollIntervalMs = 10;
        private readonly Thread _uiThread;
        private readonly Action _eventPump;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly AutoResetEvent _event = new(false);
        private readonly object _stateLock = new();
        private bool _isSignaled;
        private long? _dueTimeInMs;

        public SilkNetDispatcherImpl()
            : this(SilkNetPlatform.Instance.DoEvents)
        {
        }

        internal SilkNetDispatcherImpl(Action eventPump)
        {
            _uiThread = Thread.CurrentThread;
            _eventPump = eventPump;
        }

        public bool CurrentThreadIsLoopThread => _uiThread == Thread.CurrentThread;

        public void Signal()
        {
            lock (_stateLock)
            {
                _isSignaled = true;
            }

            _event.Set();
        }

        public event Action? Signaled;
        public event Action? Timer;

        public void FireTimer() => Timer?.Invoke();

        public void UpdateTimer(long? dueTimeInMs)
        {
            lock (_stateLock)
            {
                _dueTimeInMs = dueTimeInMs;
            }

            _event.Set();
        }

        public bool CanQueryPendingInput => false;
        public bool HasPendingInput => false;

        public void RunLoop(CancellationToken cancellationToken)
        {
            using var cancellationRegistration = cancellationToken.Register(() => _event.Set());

            while (!cancellationToken.IsCancellationRequested)
            {
                _eventPump();

                bool isSignaled;
                lock (_stateLock)
                {
                    isSignaled = _isSignaled;
                    _isSignaled = false;
                }

                if (isSignaled)
                {
                    Signaled?.Invoke();
                }

                bool fireTimer;
                lock (_stateLock)
                {
                    fireTimer = _dueTimeInMs is { } dueTime && Now >= dueTime;
                    if (fireTimer)
                    {
                        _dueTimeInMs = null;
                    }
                }

                if (fireTimer)
                {
                    FireTimer();
                    continue;
                }

                if (isSignaled)
                {
                    continue;
                }

                _event.WaitOne(GetWaitDuration());
            }
        }

        private TimeSpan GetWaitDuration()
        {
            lock (_stateLock)
            {
                if (_dueTimeInMs is not { } dueTime)
                {
                    return TimeSpan.FromMilliseconds(EventPollIntervalMs);
                }

                var milliseconds = Math.Clamp(dueTime - Now, 0, EventPollIntervalMs);
                return TimeSpan.FromMilliseconds(milliseconds);
            }
        }

        public long Now => _clock.ElapsedMilliseconds;
    }
}
