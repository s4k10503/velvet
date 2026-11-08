#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    // What a render of UseQueries read: the observer each query is matched to, in the queries' order, with the
    // settings and the entry it read and the result it made.
    internal sealed class QueriesRead<TQueryFnData, TData>
    {
        internal QueriesRead(int count)
        {
            Observers = new QueryObserver<TQueryFnData, TData>[count];
            Settings = new QuerySettings<TQueryFnData, TData>[count];
            Entries = new QueryEntry<TQueryFnData>[count];
            Results = new QueryResult<TData>[count];
        }

        internal QueryObserver<TQueryFnData, TData>[] Observers { get; }
        internal QuerySettings<TQueryFnData, TData>[] Settings { get; }
        internal QueryEntry<TQueryFnData>[] Entries { get; }
        internal QueryResult<TData>[] Results { get; }

        // TanStack's useQueries throws the error of the first query whose getHasError holds.
        internal void ThrowFirstError(QueryErrorResetBoundary boundary)
        {
            for (var i = 0; i < Observers.Length; i++)
            {
                var current = Observers[i].Current;
                if (Settings[i].HasError(current, Entries[i], boundary)) ExceptionDispatchInfo.Capture(current.Error!).Throw();
            }
        }
    }

    // TanStack's trackResult: a property one result is read for is tracked by every observer of the hook, so a
    // change to it in any query re-renders the component.
    internal sealed class QueriesResultSource<TQueryFnData, TData> : IQueryResultSource
    {
        private readonly QueryObserver<TQueryFnData, TData>[] _observers;
        private readonly int _index;

        internal QueriesResultSource(QueryObserver<TQueryFnData, TData>[] observers, int index)
        {
            _observers = observers;
            _index = index;
        }

        public void Refetch() => _observers[_index].Refetch();

        public void Track(QueryProperties property)
        {
            foreach (var observer in _observers)
            {
                observer.Track(property);
            }
        }
    }

    // TanStack's QueriesObserver: one QueryObserver per query, matched across commits by the entry it reads, and
    // the combine of their results.
    internal sealed class QueriesObserver<TQueryFnData, TData, TCombined>
    {
        private readonly Action _rerender;
        private QueryObserver<TQueryFnData, TData>[] _observers = Array.Empty<QueryObserver<TQueryFnData, TData>>();
        // TanStack's #result: replaced whenever an observer the hook holds tells it of a change, and by a commit that
        // changes the observers or what they read. Its replacement is what lets combine run again.
        private int _resultVersion;
        // Set while a commit hands the observers their options, when an observer that tells of a change is told
        // of once they all have, as v5's setQueries notifies once at its end.
        private bool _syncing;
        private bool _changedWhileSyncing;
        private QuerySnapshot<TData>[] _committed = Array.Empty<QuerySnapshot<TData>>();
        private Func<IReadOnlyList<QueryResult<TData>>, TCombined>? _combine;
        // TanStack's #lastCombine, #lastResult, #lastQueryHashes and #combinedResult.
        private Func<IReadOnlyList<QueryResult<TData>>, TCombined>? _lastCombine;
        private int _lastResultVersion = -1;
        private QueryKey[] _lastKeys = Array.Empty<QueryKey>();
        private bool _hasCombined;
        private TCombined _combined = default!;
#if UNITY_EDITOR
        private bool _warnedDuplicates;
#endif

        internal QueriesObserver(Action rerender)
        {
            _rerender = rerender;
            UnmountEffect = () => LeaveAll;
        }

        internal Func<Action?> UnmountEffect { get; }

        // TanStack's getOptimisticResult: each query is matched to an observer and read.
        internal QueriesRead<TQueryFnData, TData> Read(
            QueryClient client, IReadOnlyList<QueryOptions<TQueryFnData, TData>> queries, bool suspense,
            QueryErrorResetBoundary boundary)
        {
            var read = new QueriesRead<TQueryFnData, TData>(queries.Count);
            for (var i = 0; i < queries.Count; i++)
            {
                read.Settings[i] = new QuerySettings<TQueryFnData, TData>(queries[i], client, suspense);
            }
            Match(read);
            for (var i = 0; i < queries.Count; i++)
            {
                var entry = read.Settings[i].ReadEntry(client);
                read.Entries[i] = entry;
                read.Settings[i] = read.Settings[i].PreventingErrorBoundaryRetry(entry, boundary);
                var source = new QueriesResultSource<TQueryFnData, TData>(read.Observers, i);
                read.Results[i] = read.Observers[i].Read(entry, read.Settings[i], source);
            }
            return read;
        }

        // TanStack's #findMatchingObservers: a query takes the first observer the last commit held over the same entry
        // that no earlier query took, and a new observer otherwise.
        private void Match(QueriesRead<TQueryFnData, TData> read)
        {
            var byKey = new Dictionary<QueryKey, Queue<QueryObserver<TQueryFnData, TData>>>();
            foreach (var observer in _observers)
            {
                if (observer.CommittedKeyId is not { } id) continue;
                if (!byKey.TryGetValue(id, out var queue)) byKey.Add(id, queue = new Queue<QueryObserver<TQueryFnData, TData>>());
                queue.Enqueue(observer);
            }
            for (var i = 0; i < read.Observers.Length; i++)
            {
                read.Observers[i] = byKey.TryGetValue(read.Settings[i].KeyId, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : NewObserver();
            }
        }

        private QueryObserver<TQueryFnData, TData> NewObserver()
        {
            QueryObserver<TQueryFnData, TData>? created = null;
            created = new QueryObserver<TQueryFnData, TData>(() => OnObserverChanged(created!));
            return created;
        }

        // TanStack's setQueries, run from the commit's effects: the observers the render matched become the hook's,
        // those it dropped leave their entries, and each takes this commit's options.
        internal void Sync(QueryClient client, QueriesRead<TQueryFnData, TData> read, Func<IReadOnlyList<QueryResult<TData>>, TCombined>? combine)
        {
            _combine = combine;
            var previous = _observers;
            var next = read.Observers;
            var structural = previous.Length != next.Length;
            for (var i = 0; !structural && i < next.Length; i++)
            {
                structural = !ReferenceEquals(previous[i], next[i]);
            }
            _observers = next;
            foreach (var observer in previous)
            {
                if (Array.IndexOf(next, observer) < 0) observer.Leave();
            }
            _syncing = true;
            _changedWhileSyncing = false;
            try
            {
                for (var i = 0; i < next.Length; i++)
                {
                    next[i].Sync(client, read.Settings[i]);
                }
            }
            finally
            {
                _syncing = false;
            }
#if UNITY_EDITOR
            WarnOnDuplicates(read);
#endif
            if (_changedWhileSyncing) Notify();
            else if (structural || ResultsChanged()) RecordResults();
        }

        private bool ResultsChanged()
        {
            for (var i = 0; i < _observers.Length; i++)
            {
                if (_observers[i].Current.DifferencesFrom(_committed[i]) != QueryProperties.None) return true;
            }
            return false;
        }

        private void RecordResults()
        {
            var committed = new QuerySnapshot<TData>[_observers.Length];
            for (var i = 0; i < committed.Length; i++)
            {
                committed[i] = _observers[i].Current;
            }
            _committed = committed;
            _resultVersion++;
        }

        // TanStack's #onUpdate and #notify: an observer the hook holds changed, so the results are replaced; without
        // a combine the component re-renders, and with one only when what it combines to changed.
        private void OnObserverChanged(QueryObserver<TQueryFnData, TData> observer)
        {
            if (Array.IndexOf(_observers, observer) < 0) return;
            if (_syncing)
            {
                _changedWhileSyncing = true;
                return;
            }
            Notify();
        }

        private void Notify()
        {
            RecordResults();

            var combine = _combine;
            if (combine == null)
            {
                _rerender();
                return;
            }
            var hadCombined = _hasCombined;
            var before = _combined;
            var after = Combine(CommittedResults(), KeysOf(), combine);
            if (!hadCombined || !QueryData.Same(before, after)) _rerender();
        }

        private QueryResult<TData>[] CommittedResults()
        {
            var results = new QueryResult<TData>[_observers.Length];
            for (var i = 0; i < results.Length; i++)
            {
                results[i] = new QueryResult<TData>(_observers[i].Current, new QueriesResultSource<TQueryFnData, TData>(_observers, i));
            }
            return results;
        }

        // TanStack's #combineResult: combine runs again only when the results were replaced, the queries' keys
        // changed, or the function is another instance, and what it returns is shared with what it returned before.
        internal TCombined Combine(QueryResult<TData>[] results, QueryKey[] keys, Func<IReadOnlyList<QueryResult<TData>>, TCombined> combine)
        {
            if (_hasCombined && _lastResultVersion == _resultVersion && ReferenceEquals(_lastCombine, combine) && SameKeys(keys))
            {
                return _combined;
            }
            _lastCombine = combine;
            _lastResultVersion = _resultVersion;
            _lastKeys = keys;
            _combined = QueryStructuralSharing.Replace(_hasCombined ? _combined : default, combine(results));
            _hasCombined = true;
            return _combined;
        }

        // The keys the observers the hook holds read; every one of them has committed.
        private QueryKey[] KeysOf()
        {
            var keys = new QueryKey[_observers.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = _observers[i].CommittedKeyId!;
            }
            return keys;
        }


        private bool SameKeys(QueryKey[] keys)
        {
            if (keys.Length != _lastKeys.Length) return false;
            for (var i = 0; i < keys.Length; i++)
            {
                if (!keys[i].Equals(_lastKeys[i])) return false;
            }
            return true;
        }

        private void LeaveAll()
        {
            var observers = _observers;
            _observers = Array.Empty<QueryObserver<TQueryFnData, TData>>();
            foreach (var observer in observers)
            {
                observer.Leave();
            }
        }

#if UNITY_EDITOR
        // TanStack's development warning, once for each hook.
        private void WarnOnDuplicates(QueriesRead<TQueryFnData, TData> read)
        {
            if (_warnedDuplicates) return;
            var ids = new HashSet<QueryKey>();
            foreach (var settings in read.Settings)
            {
                if (ids.Add(settings.KeyId)) continue;
                _warnedDuplicates = true;
                FiberLogger.LogWarning("UseQueries", "[QueriesObserver]: Duplicate Queries found. This might result in unexpected behavior.");
                return;
            }
        }
#endif
    }
}
