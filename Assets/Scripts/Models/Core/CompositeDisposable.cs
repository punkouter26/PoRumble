using System;
using System.Collections.Generic;

namespace PoRumble.Models
{
    /// <summary>Disposes a group of subscriptions together.</summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private bool _isDisposed;

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (_isDisposed)
            {
                disposable.Dispose();
                return;
            }

            _disposables.Add(disposable);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            for (int disposableIndex = 0; disposableIndex < _disposables.Count; disposableIndex++)
            {
                _disposables[disposableIndex].Dispose();
            }

            _disposables.Clear();
        }
    }

    public static class DisposableExtensions
    {
        public static void AddTo(this IDisposable disposable, CompositeDisposable composite)
        {
            composite.Add(disposable);
        }
    }
}
