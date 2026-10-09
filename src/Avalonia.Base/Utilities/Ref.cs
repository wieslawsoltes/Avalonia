using System;
using System.Runtime.ConstrainedExecution;
using System.Threading;

namespace Avalonia.Utilities
{
    /// <summary>
    /// A ref-counted wrapper for a disposable object.
    /// </summary>
    /// <typeparam name="T">
    /// The type of the item being ref-counted.
    /// Must be a reference type to avoid issues with copying value types and must be disposable
    /// to ensure the item is cleaned up when the refcount reaches 0.
    /// </typeparam>
    internal interface IRef<out T> : IDisposable where T : class
    {
        /// <summary>The item that is being ref-counted.</summary>
        T Item { get; }
        /// <summary>Create another reference to this object and increment the refcount.</summary>
        IRef<T> Clone();
        /// <summary>Create another reference to the same object, cast to a different type.</summary>
        IRef<TResult> CloneAs<TResult>() where TResult : class;
        /// <summary>Gets whether the reference still tracks a valid item.</summary>
        bool IsAlive { get; }
        /// <summary>The current refcount, for debugging/unit test use only.</summary>
        int RefCount { get; }
    }

    /// <summary>
    /// Optional reference-local metadata. Ordinary references carry no state field.
    /// Cloning the item does not implicitly clone metadata about a particular view.
    /// </summary>
    internal interface IRefWithState<out TState>
    {
        TState State { get; }
    }

    internal static class RefCountable
    {
        /// <summary>Create a reference counted object wrapping the given item.</summary>
        public static IRef<T> Create<T>(T item) where T : class, IDisposable =>
            new Ref<T>(item, new RefCounter(item));

        /// <summary>Create a reference with optional metadata without an additional wrapper.</summary>
        public static IRef<T> Create<T, TState>(T item, TState state) where T : class, IDisposable =>
            new StatefulRef<T, TState>(item, new RefCounter(item), state);

        /// <summary>
        /// Clone an owned reference with new reference-local metadata. The caller still owns
        /// its original reference and must dispose it when replacing that reference.
        /// </summary>
        public static IRef<T> CloneWithState<T, TState>(IRef<T> reference, TState state) where T : class =>
            reference is Ref<T> source ? source.CloneWithState(state) :
            throw new ArgumentException("Reference was not created by RefCountable.", nameof(reference));

        class RefCounter
        {
            private IDisposable? _item;
            private volatile int _refs;

            public RefCounter(IDisposable item)
            {
                _item = item ?? throw new ArgumentNullException();
                _refs = 1;
            }

            internal bool TryAddRef()
            {
                var old = _refs;
                while (true)
                {
                    if (old == 0) return false;
                    var current = Interlocked.CompareExchange(ref _refs, old + 1, old);
                    if (current == old) break;
                    old = current;
                }
                return true;
            }

            public void Release()
            {
                var old = _refs;
                while (true)
                {
                    var current = Interlocked.CompareExchange(ref _refs, old - 1, old);
                    if (current == old)
                    {
                        if (old == 1)
                        {
                            _item?.Dispose();
                            _item = null;
                        }
                        break;
                    }
                    old = current;
                }
            }

            internal int RefCount => _refs;
        }

        class Ref<T> : CriticalFinalizerObject, IRef<T> where T : class
        {
            private volatile T? _item;
            private volatile RefCounter? _counter;

            public Ref(T item, RefCounter counter)
            {
                _item = item;
                _counter = counter;
            }

            public void Dispose() => Dispose(true);
            void Dispose(bool disposing)
            {
                var item = Interlocked.Exchange(ref _item, null);
                if (item != null)
                {
                    var counter = _counter!;
                    _counter = null;
                    if (disposing) GC.SuppressFinalize(this);
                    counter.Release();
                }
            }

            ~Ref() { Dispose(false); }

            public T Item => _item ?? throw new ObjectDisposedException("Ref<" + typeof(T) + ">");

            public IRef<T> Clone()
            {
                var counter = _counter;
                var item = _item;
                if (item == null || counter == null || !counter.TryAddRef())
                    throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
                return new Ref<T>(item, counter);
            }

            public IRef<TResult> CloneAs<TResult>() where TResult : class
            {
                var counter = _counter;
                var item = (TResult?)(object?)_item;
                if (item == null || counter == null || !counter.TryAddRef())
                    throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
                return new Ref<TResult>(item, counter);
            }

            internal IRef<T> CloneWithState<TState>(TState state)
            {
                var counter = _counter;
                var item = _item;
                if (item == null || counter == null || !counter.TryAddRef())
                    throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
                return new StatefulRef<T, TState>(item, counter, state);
            }

            public bool IsAlive => _item is not null;
            public int RefCount => _counter?.RefCount ?? throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
        }

        sealed class StatefulRef<T, TState>(T item, RefCounter counter, TState state)
            : Ref<T>(item, counter), IRefWithState<TState> where T : class
        {
            public TState State { get; } = state;
        }
    }
}
