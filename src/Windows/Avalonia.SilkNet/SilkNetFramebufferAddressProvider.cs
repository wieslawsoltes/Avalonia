using System;
using System.Runtime.InteropServices;

namespace Avalonia.SilkNet
{
    internal sealed class SilkNetFramebufferAddressProvider : ISilkNetFramebufferAddressProvider, IDisposable
    {
        private readonly object _lock = new();
        private byte[]? _buffer;
        private GCHandle _bufferHandle;
        private bool _disposed;

        internal int Capacity
        {
            get
            {
                lock (_lock)
                    return _buffer?.Length ?? 0;
            }
        }

        public IntPtr GetAddress(int requiredBufferSize)
        {
            if (requiredBufferSize <= 0 || requiredBufferSize > Array.MaxLength)
                throw new ArgumentOutOfRangeException(nameof(requiredBufferSize));

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_buffer == null || _buffer.Length < requiredBufferSize)
                {
                    if (_bufferHandle.IsAllocated)
                        _bufferHandle.Free();

                    var doubledCapacity = _buffer == null
                        ? 0
                        : _buffer.Length <= Array.MaxLength / 2
                            ? _buffer.Length * 2
                            : Array.MaxLength;
                    var capacity = Math.Max(requiredBufferSize, doubledCapacity);
                    _buffer = new byte[capacity];
                    _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                }

                return _bufferHandle.AddrOfPinnedObject();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                if (_bufferHandle.IsAllocated)
                    _bufferHandle.Free();
                _buffer = null;
                _disposed = true;
            }
        }
    }
}
