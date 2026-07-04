using System;
using System.Collections.Generic;

namespace Velvet.Editor.DevTools
{
    /// <summary>
    /// Ring buffer (capacity N) holding a component's state-change history.
    /// Managed per component on the DevTools side to avoid intruding on the Runtime.
    /// </summary>
    public sealed class StateHistoryBuffer
    {
        private readonly StateHistoryEntry[] _buffer;
        private int _head;
        private int _count;
        private int _version;
        private int _cachedVersion = -1;
        private IReadOnlyList<StateHistoryEntry> _cachedNewestFirst;

        /// <summary>Buffer capacity.</summary>
        public int Capacity { get; }

        /// <summary>Number of entries currently stored.</summary>
        public int Count => _count;

        public StateHistoryBuffer(int capacity = 64)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");
            }

            Capacity = capacity;
            _buffer = new StateHistoryEntry[capacity];
        }

        /// <summary>
        /// Adds an entry. When the buffer is full the oldest entry is overwritten.
        /// </summary>
        public void Push(StateHistoryEntry entry)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
            _version++;
        }

        /// <summary>
        /// Returns entries newest-first (index 0 is the newest).
        /// Reuses the cached list when the buffer has not changed, reducing GC.
        /// </summary>
        public IReadOnlyList<StateHistoryEntry> GetNewestFirst()
        {
            if (_cachedVersion == _version)
            {
                return _cachedNewestFirst;
            }

            var result = new List<StateHistoryEntry>(_count);
            for (var i = _count - 1; i >= 0; i--)
            {
                var index = (_head - 1 - i + Capacity * 2) % Capacity;
                result.Add(_buffer[index]);
            }

            _cachedNewestFirst = result;
            _cachedVersion = _version;
            return _cachedNewestFirst;
        }

        /// <summary>Clears all entries.</summary>
        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
            _version++;
        }
    }
}
