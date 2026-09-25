#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // A string parsed again gets the same array back for as long as it stays in use. The reconciler depends
    // on that: FiberNodePatcher's DiffClassList returns on an identical array before comparing content, and
    // its ResolveVariantClasses reports a change for any other array.
    //
    // Entries live in two generations. A lookup that finds its key in the previous generation carries the
    // same array into the current one, and when the current one fills, the previous one is dropped whole —
    // so a string looked up at least once between one rotation and the next is never dropped, and a moving
    // arbitrary value (left-[{x}px]) is dropped two rotations after its last use. A fixed capacity, cleared
    // whole or evicted least-recently-used, was rejected: once a screen's strings outnumber it and are parsed
    // in a repeating order, it misses on every lookup.
    //
    // The generation doubles when more than half of the one just ended was reuse. Reuse counts a carried
    // entry and also a miss on a key whose hash is still remembered from its drop: a set too large for two
    // generations misses on every lookup exactly as a moving value does, and the remembered hashes are what
    // tell the two apart. Hashes rather than entries, so nothing a moving value parsed is held past its
    // second rotation. Half rather than any reuse, because under a moving value every generation carries
    // the strings that stay put, and any-reuse would double on every rotation.
    //
    // Nothing is logged: a moving value and a large screen are both supported styling.
    //
    // Not thread-safe. Acceptable because Velvet's reconciler is main-thread only.
    internal sealed class ClassNameParseCache
    {
        internal const int InitialGenerationSize = 256;
        private const int RotationsPerDroppedSet = 8;

        private Dictionary<string, string[]> _current = new(InitialGenerationSize);
        private Dictionary<string, string[]> _previous = new(InitialGenerationSize);
        private HashSet<int> _droppedRecent = new();
        private HashSet<int> _droppedOlder = new();
        private int _rotationsInDroppedRecent;
        private int _generationSize = InitialGenerationSize;
        private int _reusedThisGeneration;

        internal string[] Parse(string classNames)
        {
            if (_current.TryGetValue(classNames, out var tokens))
            {
                return tokens;
            }

            var carried = _previous.TryGetValue(classNames, out tokens);
            var reused = carried || WasDropped(classNames.GetHashCode());
            if (_current.Count >= _generationSize)
            {
                Rotate();
            }
            if (reused)
            {
                _reusedThisGeneration++;
            }

            tokens ??= classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _current.Add(classNames, tokens);
            return tokens;
        }

        internal void Clear()
        {
            _current.Clear();
            _previous.Clear();
            _droppedRecent.Clear();
            _droppedOlder.Clear();
            _rotationsInDroppedRecent = 0;
            _generationSize = InitialGenerationSize;
            _reusedThisGeneration = 0;
        }

        private bool WasDropped(int hash) => _droppedRecent.Contains(hash) || _droppedOlder.Contains(hash);

        private void Rotate()
        {
            if (_reusedThisGeneration > _generationSize / 2)
            {
                _generationSize *= 2;
            }

            foreach (var key in _previous.Keys)
            {
                _droppedRecent.Add(key.GetHashCode());
            }
            if (++_rotationsInDroppedRecent == RotationsPerDroppedSet)
            {
                (_droppedOlder, _droppedRecent) = (_droppedRecent, _droppedOlder);
                _droppedRecent.Clear();
                _rotationsInDroppedRecent = 0;
            }

            (_previous, _current) = (_current, _previous);
            _current.Clear();
            _reusedThisGeneration = 0;
        }
    }
}
