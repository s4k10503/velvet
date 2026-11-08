#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    // One query of a UseQueries render, whatever its data types: its options and settings, the observer it is
    // matched to, and the entry it read.
    internal abstract class QueriesItem
    {
        internal abstract QueryKey KeyId { get; }
        internal abstract QueryObserver Observer { get; }
        internal abstract void Settle(QueryClient client, bool suspense);
        internal abstract void Match(QueryObserver? held, Action<QueryObserver> onChange);
        internal abstract object Read(QueryClient client, QueryErrorResetBoundary boundary, IQueryResultSource source);
        internal abstract void Sync(QueryClient client);
        internal abstract void ThrowIfError(QueryErrorResetBoundary boundary);
        // TanStack's shouldSuspend and fetchOptimistic for one query: a pending result starts the entry's request, and
        // is waited on until the entry holds data or the request settles.
        internal abstract Func<QueryErrorResetBoundary, CancellationToken, VelvetTask<Unit>>? Suspend(QueryClient client, out object request);
    }

    internal sealed class QueriesItem<TQueryFnData, TData> : QueriesItem
    {
        private readonly QueryOptionsBase<TQueryFnData, TData> _options;
        private QuerySettings<TQueryFnData, TData> _settings;
        private QueryObserver<TQueryFnData, TData> _observer = null!;
        private QueryEntry<TQueryFnData> _entry = null!;

        internal QueriesItem(QueryOptionsBase<TQueryFnData, TData> options) => _options = options;

        internal override QueryKey KeyId => _settings.KeyId;
        internal override QueryObserver Observer => _observer;

        internal override void Settle(QueryClient client, bool suspense)
            => _settings = new QuerySettings<TQueryFnData, TData>(_options, client, suspense);

        // An observer held over the same entry for other data types reads an entry this query cannot, so a new one
        // reads it, and reading it throws as UseQuery does.
        internal override void Match(QueryObserver? held, Action<QueryObserver> onChange)
        {
            if (held is QueryObserver<TQueryFnData, TData> typed)
            {
                _observer = typed;
                return;
            }
            QueryObserver<TQueryFnData, TData>? created = null;
            created = new QueryObserver<TQueryFnData, TData>(() => onChange(created!));
            _observer = created;
        }

        internal override object Read(QueryClient client, QueryErrorResetBoundary boundary, IQueryResultSource source)
        {
            _entry = _settings.ReadEntry(client);
            _settings = _settings.PreventingErrorBoundaryRetry(_entry, boundary);
            return _observer.Read(_entry, _settings, source);
        }

        internal override void Sync(QueryClient client) => _observer.Sync(client, _settings);

        internal override void ThrowIfError(QueryErrorResetBoundary boundary)
        {
            var current = _observer.Current;
            if (_settings.HasError(current, _entry, boundary)) ExceptionDispatchInfo.Capture(current.Error!).Throw();
        }

        internal override Func<QueryErrorResetBoundary, CancellationToken, VelvetTask<Unit>>? Suspend(QueryClient client, out object request)
        {
            request = _entry;
            if (!_settings.Suspense || _observer.Current.Status != QueryStatus.Pending) return null;
            var entry = _entry;
            entry.Fetch(new QueryFetchOptions<TQueryFnData>().Assign(_options, client), cancelRefetch: false);
            request = entry.Request ?? entry;
            return (boundary, token) => WaitForSuspendedQuery(entry, boundary, token);
        }

        private static async VelvetTask<Unit> WaitForSuspendedQuery(
            QueryEntry<TQueryFnData> entry, QueryErrorResetBoundary boundary, CancellationToken token)
        {
            await entry.WhenSuspendedCanResume(token);
            if (!entry.HasData) boundary.ClearReset();
            return Unit.Default;
        }
    }

    // TanStack's trackResult: a property one result is read for is tracked by every observer of the hook, so a
    // change to it in any query re-renders the component.
    internal sealed class QueriesResultSource : IQueryResultSource
    {
        private readonly QueryObserver[] _observers;
        private readonly int _index;

        internal QueriesResultSource(QueryObserver[] observers, int index)
        {
            _observers = observers;
            _index = index;
        }

        public void Refetch() => ((IQueryResultSource)_observers[_index]).Refetch();

        public void Track(QueryProperties property)
        {
            foreach (var observer in _observers)
            {
                observer.TrackFor(property);
            }
        }
    }

    // What a render of UseQueries read, in the queries' order.
    internal sealed class QueriesRead
    {
        internal QueriesRead(QueriesItem[] items)
        {
            Items = items;
            Observers = new QueryObserver[items.Length];
            Results = new object[items.Length];
            Keys = new QueryKey[items.Length];
        }

        internal QueriesItem[] Items { get; }
        internal QueryObserver[] Observers { get; }
        internal object[] Results { get; }
        internal QueryKey[] Keys { get; }

        // TanStack's useQueries throws the error of the first query whose getHasError holds.
        internal void ThrowFirstError(QueryErrorResetBoundary boundary)
        {
            foreach (var item in Items)
            {
                item.ThrowIfError(boundary);
            }
        }
    }

    // TanStack's QueriesObserver: one QueryObserver per query, matched across commits by the entry it reads, and
    // the combine of their results.
    internal sealed class QueriesObserver<TCombined>
    {
        private readonly Action _rerender;
        private QueryObserver[] _observers = Array.Empty<QueryObserver>();
        // TanStack's #result: replaced whenever an observer the hook holds tells it of a change, and by a commit that
        // changes the observers or what they read. Its replacement is what lets combine run again.
        private int _resultVersion;
        // Set while a commit hands the observers their options, when an observer that tells of a change is told
        // of once they all have, as v5's setQueries notifies once at its end.
        private bool _syncing;
        private bool _changedWhileSyncing;
        private Delegate? _combine;
        private Func<object[], TCombined>? _invokeCombine;
        // TanStack's #lastCombine, #lastResult, #lastQueryHashes and #combinedResult.
        private Delegate? _lastCombine;
        private int _lastResultVersion = -1;
        private QueryKey[] _lastKeys = Array.Empty<QueryKey>();
        private bool _hasCombined;
        private TCombined _combined = default!;

        internal QueriesObserver(Action rerender)
        {
            _rerender = rerender;
            UnmountEffect = () => LeaveAll;
        }

        internal Func<Action?> UnmountEffect { get; }

        // TanStack's getOptimisticResult: each query is matched to an observer and read.
        internal QueriesRead Read(QueryClient client, QueriesItem[] items, bool suspense, QueryErrorResetBoundary boundary)
        {
            var read = new QueriesRead(items);
            foreach (var item in items)
            {
                item.Settle(client, suspense);
            }
            Match(read);
            for (var i = 0; i < items.Length; i++)
            {
                read.Keys[i] = items[i].KeyId;
                read.Results[i] = items[i].Read(client, boundary, new QueriesResultSource(read.Observers, i));
            }
            return read;
        }

        // TanStack's #findMatchingObservers: a query takes the first observer the last commit held over the same entry
        // that no earlier query took, and a new observer otherwise.
        private void Match(QueriesRead read)
        {
            var byKey = new Dictionary<QueryKey, Queue<QueryObserver>>();
            foreach (var observer in _observers)
            {
                if (observer.CommittedKeyId is not { } id) continue;
                if (!byKey.TryGetValue(id, out var queue)) byKey.Add(id, queue = new Queue<QueryObserver>());
                queue.Enqueue(observer);
            }
            for (var i = 0; i < read.Items.Length; i++)
            {
                var item = read.Items[i];
                var held = byKey.TryGetValue(item.KeyId, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
                item.Match(held, OnObserverChanged);
                read.Observers[i] = item.Observer;
            }
        }

        // TanStack's setQueries, run from the commit's effects: the observers the render matched become the hook's,
        // those it dropped leave their entries, and each takes this commit's options.
        internal void Sync(QueryClient client, QueriesRead read, Delegate? combine, Func<object[], TCombined>? invokeCombine)
        {
            _combine = combine;
            _invokeCombine = invokeCombine;
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
                foreach (var item in read.Items)
                {
                    item.Sync(client);
                }
            }
            finally
            {
                _syncing = false;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnOnDuplicates(read.Keys);
#endif
            if (_changedWhileSyncing) Notify();
            else if (structural || ResultsChanged()) RecordResults();
        }

        private bool ResultsChanged()
        {
            foreach (var observer in _observers)
            {
                if (observer.ResultChangedSinceRecorded) return true;
            }
            return false;
        }

        private void RecordResults()
        {
            foreach (var observer in _observers)
            {
                observer.RecordResult();
            }
            _resultVersion++;
        }

        // TanStack's #onUpdate: an observer the hook holds changed, so the results are replaced and the listeners
        // told.
        private void OnObserverChanged(QueryObserver observer)
        {
            if (Array.IndexOf(_observers, observer) < 0) return;
            if (_syncing)
            {
                _changedWhileSyncing = true;
                return;
            }
            Notify();
        }

        // TanStack's #notify: without a combine the component re-renders; with one, while a suspending query holds no
        // data yet the combine is passed over and the component re-renders, and otherwise it re-renders only when
        // what the combine returns changed.
        private void Notify()
        {
            RecordResults();
            var invokeCombine = _invokeCombine;
            if (_combine == null || invokeCombine == null || ShouldSkipCombine())
            {
                _rerender();
                return;
            }
            var hadCombined = _hasCombined;
            var before = _combined;
            var after = Combine(CommittedResults(), KeysOf(), _combine, invokeCombine);
            if (!hadCombined || !QueryData.Same(before, after)) _rerender();
        }

        private bool ShouldSkipCombine()
        {
            foreach (var observer in _observers)
            {
                if (observer.SuspendsWithoutData) return true;
            }
            return false;
        }

        private object[] CommittedResults()
        {
            var results = new object[_observers.Length];
            for (var i = 0; i < results.Length; i++)
            {
                results[i] = _observers[i].ResultOf(new QueriesResultSource(_observers, i));
            }
            return results;
        }

        // TanStack's #combineResult: combine runs again only when the results were replaced, the queries' keys
        // changed, or the function is another instance, and what it returns is shared with what it returned before.
        internal TCombined Combine(object[] results, QueryKey[] keys, Delegate combine, Func<object[], TCombined> invokeCombine)
        {
            if (_hasCombined && _lastResultVersion == _resultVersion && ReferenceEquals(_lastCombine, combine) && SameKeys(keys))
            {
                return _combined;
            }
            _lastCombine = combine;
            _lastResultVersion = _resultVersion;
            _lastKeys = keys;
            _combined = QueryStructuralSharing.Replace(_hasCombined ? _combined : default, invokeCombine(results));
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
            _observers = Array.Empty<QueryObserver>();
            foreach (var observer in observers)
            {
                observer.Leave();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // TanStack's development warning, at every commit whose list holds two queries over one entry, as v5's
        // setQueries warns each time it runs outside production.
        private static void WarnOnDuplicates(QueryKey[] keys)
        {
            var seen = new HashSet<QueryKey>();
            foreach (var key in keys)
            {
                if (seen.Add(key)) continue;
                FiberLogger.LogWarning("UseQueries", "[QueriesObserver]: Duplicate Queries found. This might result in unexpected behavior.");
                return;
            }
        }
#endif
    }
}
