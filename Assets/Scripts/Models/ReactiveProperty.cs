using System;
using System.Collections.Generic;

namespace PoRumble.Models
{
    /// <summary>Read-only view of an observable value.</summary>
    public interface IReadOnlyReactiveProperty<T>
    {
        T Value { get; }
        IDisposable Subscribe(Action<T> onNext);
    }

    /// <summary>
    /// Minimal observable value. Hand-rolled so the Models assembly depends on nothing —
    /// R3 would require NuGetForUnity, which cannot be scripted.
    /// </summary>
    public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>
    {
        private readonly List<Action<T>> _observers = new();
        private T _value;

        public ReactiveProperty(T initialValue = default)
        {
            _value = initialValue;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                {
                    return;
                }

                _value = value;

                for (int observerIndex = 0; observerIndex < _observers.Count; observerIndex++)
                {
                    _observers[observerIndex].Invoke(_value);
                }
            }
        }

        /// <summary>Subscribes and immediately pushes the current value.</summary>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null)
            {
                throw new ArgumentNullException(nameof(onNext));
            }

            _observers.Add(onNext);
            onNext.Invoke(_value);
            return new Subscription(this, onNext);
        }

        private sealed class Subscription : IDisposable
        {
            private ReactiveProperty<T> _owner;
            private Action<T> _onNext;

            internal Subscription(ReactiveProperty<T> owner, Action<T> onNext)
            {
                _owner = owner;
                _onNext = onNext;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner._observers.Remove(_onNext);
                _owner = null;
                _onNext = null;
            }
        }
    }
}
