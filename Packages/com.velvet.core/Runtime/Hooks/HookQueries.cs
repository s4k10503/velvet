#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    // One query of a UseQueries render, whatever its data types: its options and settings, the observer that holds
    // its subscription, the observer that computes its own result, and the entry it read.
    internal abstract class QueriesItem
    {
        internal abstract QueryKey KeyId { get; }
        internal abstract QueryObserver Observer { get; }
        // TanStack's trackResult hands back a result whose query lists notifyOnChangeProps untracked.
        internal abstract bool ListsNotifyProps { get; }
        internal abstract Type ResultType { get; }
        internal abstract object Computer { get; }
        internal abstract void Settle(QueryClient client, bool suspense);
        internal abstract void Match(QueryObserver? held, object? computer, Action<QueryObserver> onChange);
        internal abstract object Read(QueryClient client, QueryErrorResetBoundary boundary, IQueryResultSource source);
        internal abstract object Refresh(IQueryResultSource source);
        // The query's own result computed afresh from its entry, and what differs from the one before.
        internal abstract QueryProperties RefreshChanges();
        internal abstract QueryProperties? NotifyOnChangeProps { get; }
        internal abstract QueryProperties ErrorWatch { get; }
        internal abstract void AnnounceResultsUpdated();
        internal abstract void Sync(QueryClient client);
        internal abstract void ThrowIfError(QueryErrorResetBoundary boundary);
        // TanStack's shouldSuspend and fetchOptimistic for one query: a pending result starts the entry's request, and
        // is waited on until the entry holds data or the request settles.
        internal abstract Func<QueryErrorResetBoundary, CancellationToken, VelvetTask<Unit>>? Suspend(QueryClient client, out object request);
    }

    // The observer is matched by its entry alone, as v5 matches one by queryHash whatever each query selects, so it
    // holds the entry's own data under the options without a select; the query's TData comes from its computer.
    internal sealed class QueriesItem<TQueryFnData, TData> : QueriesItem
    {
        private readonly QueryOptionsBase<TQueryFnData, TData> _options;
        private QuerySettings<TQueryFnData, TData> _settings;
        private QuerySettings<TQueryFnData, TQueryFnData> _shared;
        private QueryObserver<TQueryFnData, TQueryFnData> _observer = null!;
        private QueryObserver<TQueryFnData, TData> _computer = null!;
        private QueryEntry<TQueryFnData> _entry = null!;

        internal QueriesItem(QueryOptionsBase<TQueryFnData, TData> options) => _options = options;

        internal override QueryKey KeyId => _settings.KeyId;
        internal override QueryObserver Observer => _observer;
        internal override bool ListsNotifyProps => _options.NotifyOnChangeProps != null;
        internal override Type ResultType => typeof(QueryObserver<TQueryFnData, TData>);
        internal override object Computer => _computer;

        internal override void Settle(QueryClient client, bool suspense)
        {
            _settings = new QuerySettings<TQueryFnData, TData>(_options, client, suspense);
            _shared = new QuerySettings<TQueryFnData, TQueryFnData>(_options.Raw(), client, suspense);
        }

        // An observer held over the entry for another entry type reads an entry this query cannot, so a new one reads
        // it, and reading it throws as UseQuery does.
        internal override void Match(QueryObserver? held, object? computer, Action<QueryObserver> onChange)
        {
            if (held is QueryObserver<TQueryFnData, TQueryFnData> typed)
            {
                _observer = typed;
            }
            else
            {
                QueryObserver<TQueryFnData, TQueryFnData>? created = null;
                created = new QueryObserver<TQueryFnData, TQueryFnData>(() => onChange(created!));
                _observer = created;
            }
            _computer = computer as QueryObserver<TQueryFnData, TData> ?? new QueryObserver<TQueryFnData, TData>(static () => { });
            _computer.Host(_observer);
        }

        internal override object Read(QueryClient client, QueryErrorResetBoundary boundary, IQueryResultSource source)
        {
            _entry = _settings.ReadEntry(client);
            _settings = _settings.PreventingErrorBoundaryRetry(_entry, boundary);
            _shared = _shared.PreventingErrorBoundaryRetry(_entry, boundary);
            // The observer's own reading is what its next update is compared with, as for UseQuery.
            _observer.Read(_entry, _shared, source);
            return _computer.Read(_entry, _settings, source);
        }

        internal override object Refresh(IQueryResultSource source)
            => _computer.Refresh(_observer.Entry ?? _entry, _settings, source);

        internal override QueryProperties RefreshChanges() => _computer.RefreshChanges(_observer.Entry ?? _entry, _settings);

        internal override QueryProperties? NotifyOnChangeProps => _options.NotifyOnChangeProps;
        internal override QueryProperties ErrorWatch => _settings.ErrorWatch;

        // TanStack's observerResultsUpdated, for this query's own result.
        internal override void AnnounceResultsUpdated()
        {
            var entry = _observer.Entry ?? _entry;
            entry.Client.QueryCache.Notify(new QueryCacheEvent(QueryCacheEventType.ObserverResultsUpdated, entry.Query));
        }

        internal override void Sync(QueryClient client) => _observer.Sync(client, _shared);

        internal override void ThrowIfError(QueryErrorResetBoundary boundary)
        {
            var current = _computer.Current;
            if (_settings.HasError(current, _entry, boundary)) ExceptionDispatchInfo.Capture(current.Error!).Throw();
        }

        internal override Func<QueryErrorResetBoundary, CancellationToken, VelvetTask<Unit>>? Suspend(QueryClient client, out object request)
        {
            request = _entry;
            if (!_settings.Suspense || _computer.Current.Status != QueryStatus.Pending) return null;
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

    // A query's options without the select, for the observer that holds a UseQueries query's subscription.
    internal sealed record RawQueryOptions<TQueryFnData> : QueryOptionsBase<TQueryFnData, TQueryFnData>
    {
        private RawQueryOptions(QueryOptionsBase origin, QueryFunction<TQueryFnData> function)
            : base(origin.QueryKey)
        {
            Origin = origin;
            Fn = function;
        }

        private QueryFunction<TQueryFnData> Fn { get; }

        // The options the query was built with, which the entry reports as its options and whose equality, select
        // included, decides whether a commit announces ObserverOptionsUpdated, as v5 compares the options it holds.
        internal QueryOptionsBase Origin { get; }

        internal override QueryFunction<TQueryFnData> Function => Fn;

        // The observer tells the hook of every change to the entry, and each query decides from its own
        // result whether it re-renders, as v5's observer compares its selected result's tracked properties.
        internal static RawQueryOptions<TQueryFnData> Of<TData>(QueryOptionsBase<TQueryFnData, TData> source) => new(source, source.Function)
        {
            Meta = source.Meta,
            StaleTime = source.StaleTime,
            StaleTimeFn = source.StaleTimeFn,
            GcTime = source.GcTime,
            Retry = source.Retry,
            RetryDelay = source.RetryDelay,
            NetworkMode = source.NetworkMode,
            QueryKeyHashFn = source.QueryKeyHashFn,
            StructuralSharing = source.StructuralSharing,
            NotifyOnChangeProps = QueryProperties.All,
            Enabled = source.Enabled,
            EnabledFn = source.EnabledFn,
            RetryOnMount = source.RetryOnMount,
            RetryOnMountFn = source.RetryOnMountFn,
            InitialData = source.InitialData,
            InitialDataUpdatedAt = source.InitialDataUpdatedAt,
            InitialDataUpdatedAtFn = source.InitialDataUpdatedAtFn,
            ThrowOnError = source.ThrowOnError,
            ThrowOnErrorFn = source.ThrowOnErrorFn,
            PlaceholderData = source.PlaceholderData,
            RefetchInterval = source.RefetchInterval,
            RefetchIntervalFn = source.RefetchIntervalFn,
            RefetchIntervalInBackground = source.RefetchIntervalInBackground,
            RefetchOnMount = source.RefetchOnMount,
            RefetchOnMountFn = source.RefetchOnMountFn,
            RefetchOnWindowFocus = source.RefetchOnWindowFocus,
            RefetchOnWindowFocusFn = source.RefetchOnWindowFocusFn,
            RefetchOnReconnect = source.RefetchOnReconnect,
            RefetchOnReconnectFn = source.RefetchOnReconnectFn,
        };
    }

    // TanStack's trackResult: a property one result is read for is tracked by every observer of the hook, so a
    // change to it in any query re-renders the component.
    internal sealed class QueriesResultSource : IQueryResultSource
    {
        private readonly QueryObserver[] _observers;
        private readonly int _index;
        private readonly Action<QueryProperties>? _track;

        // A null track is a result handed back untracked.
        internal QueriesResultSource(QueryObserver[] observers, int index, Action<QueryProperties>? track)
        {
            _observers = observers;
            _index = index;
            _track = track;
        }

        public void Refetch() => ((IQueryResultSource)_observers[_index]).Refetch();

        public void Track(QueryProperties property) => _track?.Invoke(property);
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
        private QueriesItem[] _items = Array.Empty<QueriesItem>();
        // TanStack's #result: replaced whenever an observer the hook holds tells it of a change, and by a commit that
        // changes the observers or what they read. Its replacement is what lets combine run again.
        private int _resultVersion;
        // Set while a commit hands the observers their options, when an observer that tells of a change is told
        // of once they all have, as v5's setQueries notifies once at its end.
        private bool _syncing;
        private bool _changedWhileSyncing;
        // The mount's commit, and a StrictMode replay of it after LeaveAll, records the results without telling of
        // them, since the render it commits read them.
        private bool _committed;
        private Delegate? _combine;
        private Func<object[], TCombined>? _invokeCombine;
        // TanStack's #lastCombine, #lastResult, #lastQueryHashes and #combinedResult.
        private Delegate? _lastCombine;
        private int _lastResultVersion = -1;
        private QueryKey[] _lastKeys = Array.Empty<QueryKey>();
        private bool _hasCombined;
        private TCombined _combined = default!;

        private readonly Action<QueryProperties> _trackProperty;

        internal QueriesObserver(Action rerender)
        {
            _rerender = rerender;
            _trackProperty = property => _tracked |= property;
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
                read.Results[i] = items[i].Read(client, boundary, new QueriesResultSource(read.Observers, i, TrackOf(items[i])));
            }
            return read;
        }

        // TanStack's #findMatchingObservers: a query is handed, whatever its data types, the first observer the last
        // commit held over its entry that no query before it was handed, and a new one where none is left. Each
        // query's computer is kept for the next query over the entry with the same types, in the order they come, so
        // its select memo carries over.
        private void Match(QueriesRead read)
        {
            var byKey = new Dictionary<QueryKey, Queue<QueryObserver>>();
            foreach (var observer in _observers)
            {
                if (observer.CommittedKeyId is not { } id) continue;
                if (!byKey.TryGetValue(id, out var held)) byKey.Add(id, held = new Queue<QueryObserver>());
                held.Enqueue(observer);
            }
            var computers = new Dictionary<(QueryKey, Type), Queue<object>>();
            foreach (var item in _items)
            {
                var slot = (item.KeyId, item.ResultType);
                if (!computers.TryGetValue(slot, out var queue)) computers.Add(slot, queue = new Queue<object>());
                queue.Enqueue(item.Computer);
            }
            for (var i = 0; i < read.Items.Length; i++)
            {
                var item = read.Items[i];
                var computer = computers.TryGetValue((item.KeyId, item.ResultType), out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
                var observer = byKey.TryGetValue(item.KeyId, out var held) && held.Count > 0 ? held.Dequeue() : null;
                item.Match(observer, computer, OnObserverChanged);
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
            _items = read.Items;
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
            var committed = _committed;
            _committed = true;
            var notify = _changedWhileSyncing && AnyQueryNotifies();
            if (!notify && !structural && !ResultsChanged()) return;
            RecordResults();
            if (notify || committed) Notify();
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

        // TanStack's #onUpdate, for an observer the hook holds that told of a change its own query notifies of: that
        // observer's result alone is recorded and the listeners told. Recording every observer here would record one
        // over the same entry that the entry has not yet told of the change.
        private void OnObserverChanged(QueryObserver observer)
        {
            var index = Array.IndexOf(_observers, observer);
            if (index < 0) return;
            if (_syncing)
            {
                _changedWhileSyncing = true;
                return;
            }
            if (!Notifies(_items[index])) return;
            observer.RecordResult();
            _resultVersion++;
            Notify();
        }

        // TanStack's trackResult: a property read from one result is tracked for them all, and a result whose query
        // lists notifyOnChangeProps is handed back untracked.
        private QueryProperties _tracked;

        private Action<QueryProperties>? TrackOf(QueriesItem item) => item.ListsNotifyProps ? null : _trackProperty;

        // Every query is asked, not only those before the first that notifies, so every result is computed afresh.
        private bool AnyQueryNotifies()
        {
            var notifies = false;
            foreach (var item in _items)
            {
                if (Notifies(item)) notifies = true;
            }
            return notifies;
        }

        // TanStack's shouldNotifyListeners, asked of a query's own result: its listed properties, an empty list
        // included, else the ones read from any result, else any change. The result is computed afresh, so the next
        // comparison starts from it.
        private bool Notifies(QueriesItem item)
        {
            var changed = item.RefreshChanges();
            if (changed == QueryProperties.None) return false;
            item.AnnounceResultsUpdated();
            var listed = item.NotifyOnChangeProps;
            if (listed == null && _tracked == QueryProperties.None) return true;
            return (changed & ((listed ?? _tracked) | item.ErrorWatch)) != QueryProperties.None;
        }

        // TanStack's #notify: without a combine the component re-renders; with one, while a suspending query holds no
        // data yet the combine is passed over and the component re-renders, and otherwise it re-renders only when
        // what the combine returns changed.
        private void Notify()
        {
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
                results[i] = _items[i].Refresh(new QueriesResultSource(_observers, i, TrackOf(_items[i])));
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
            _items = Array.Empty<QueriesItem>();
            _committed = false;
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
