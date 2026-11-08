using System;
using System.Collections.Generic;

namespace Velvet
{
    // A listener that unsubscribes while being notified still hears this event: the walk is over a snapshot.
    internal sealed class CacheListeners<TEvent>
    {
        private readonly List<Action<TEvent>> _listeners = new();

        public Action Subscribe(Action<TEvent> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (!_listeners.Contains(listener)) _listeners.Add(listener);
            return () => _listeners.Remove(listener);
        }

        public void Notify(TEvent cacheEvent)
        {
            foreach (var listener in _listeners.ToArray())
            {
                listener(cacheEvent);
            }
        }
    }
}
