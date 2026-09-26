#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // A cached string parsed again gets the same array back, and the reconciler depends on that:
    // FiberNodePatcher's DiffClassList returns on an identical array before comparing content, and its
    // ResolveVariantClasses reports a change for any other array.
    //
    // Counting in first parses — parses that split a string no structure here remembers — a string is
    // cached when it is parsed a second time within FirstSightingsPerWindow of them, and may be up to twice
    // that. Within the last ProbationGenerationSize to twice that it is cached with the array its first
    // parse returned; past that, only its hash is remembered, and it is cached with a fresh one. A cached
    // string keeps its array while it is parsed again within FirstSightingsPerWindow first parses, and is
    // dropped once twice that many pass without it. So a moving arbitrary value (left-[{x}px]) whose strings
    // are each parsed once enters the cache only where a string's hash collides with a remembered one, and
    // ages it only by its own first parses; a render's cached strings stay cached while it parses fewer
    // moving strings than a window holds. A screen shown for the first time counts its own first parses as
    // well, so its distinct strings plus a render's moving strings are what has to fit.
    //
    // Rejected: bounding the cache by its size — cleared whole, least-recently-used, or generations turning
    // over when full. Such a cache keeps a string beside M moving strings per render only while its size
    // exceeds M, so it holds the moving strings themselves as entries, about 4096 of them at this cache's
    // reach, where here they occupy at most twice ProbationGenerationSize entries and twice
    // FirstSightingsPerWindow hashes.
    //
    // Nothing is logged: a moving value and a large screen are both supported styling.
    //
    // Nothing here is synchronized; Documentation~/async.md owns the rule that code resumed off the main
    // thread does not call back into Velvet.
    internal sealed class ClassNameParseCache
    {
        internal const int ProbationGenerationSize = 128;
        internal const int FirstSightingsPerWindow = 2048;
        private const int InitialCacheCapacity = 256;

        private Dictionary<string, string[]> _current = new(InitialCacheCapacity);
        private Dictionary<string, string[]> _previous = new(InitialCacheCapacity);
        private Dictionary<string, string[]> _probationCurrent = new(ProbationGenerationSize);
        private Dictionary<string, string[]> _probationPrevious = new(ProbationGenerationSize);
        private HashSet<int> _seenRecent = new();
        private HashSet<int> _seenOlder = new();
        private int _firstSightingsThisWindow;

        internal string[] Parse(string classNames)
        {
            if (_current.TryGetValue(classNames, out var tokens))
            {
                return tokens;
            }

            if (_previous.TryGetValue(classNames, out tokens)
                || _probationCurrent.TryGetValue(classNames, out tokens)
                || _probationPrevious.TryGetValue(classNames, out tokens))
            {
                _current.Add(classNames, tokens);
                return tokens;
            }

            tokens = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var hash = classNames.GetHashCode();
            if (_seenRecent.Contains(hash) || _seenOlder.Contains(hash))
            {
                _current.Add(classNames, tokens);
            }
            else
            {
                _seenRecent.Add(hash);
                AddToProbation(classNames, tokens);
                if (++_firstSightingsThisWindow == FirstSightingsPerWindow)
                {
                    EndWindow();
                }
            }
            return tokens;
        }

        internal void Clear()
        {
            _current.Clear();
            _previous.Clear();
            _probationCurrent.Clear();
            _probationPrevious.Clear();
            _seenRecent.Clear();
            _seenOlder.Clear();
            _firstSightingsThisWindow = 0;
        }

        private void AddToProbation(string classNames, string[] tokens)
        {
            if (_probationCurrent.Count >= ProbationGenerationSize)
            {
                (_probationPrevious, _probationCurrent) = (_probationCurrent, _probationPrevious);
                _probationCurrent.Clear();
            }
            _probationCurrent.Add(classNames, tokens);
        }

        private void EndWindow()
        {
            (_previous, _current) = (_current, _previous);
            _current.Clear();
            (_seenOlder, _seenRecent) = (_seenRecent, _seenOlder);
            _seenRecent.Clear();
            _firstSightingsThisWindow = 0;
        }
    }
}
