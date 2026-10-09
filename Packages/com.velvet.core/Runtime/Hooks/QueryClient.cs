#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Defaults a <see cref="QueryClient"/> applies to every query that does not set its own, plus the clock
    /// it measures staleness and garbage collection against.
    /// </summary>
    public sealed class QueryClientOptions
    {
        /// <summary>
        /// How long a result stays fresh after it lands. TanStack Query's default, zero, makes every result
        /// stale at once, so each component mounting over it fetches again in the background.
        /// <see cref="TimeSpan.MaxValue"/> keeps a result fresh until it is invalidated.
        /// </summary>
        public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// How long an entry nothing observes is kept; after that it reads as absent and the next sweep
        /// removes it, as the remarks on <see cref="QueryClient"/> say. TanStack Query's default is five
        /// minutes.
        /// <see cref="TimeSpan.MaxValue"/> keeps it until <see cref="QueryClient.Clear"/>.
        /// </summary>
        public TimeSpan GcTime { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How many times a failed request runs again before the failure is the entry's error: TanStack
        /// Query's <c>retry</c> given as a number, whose default is three. Zero or less retries nothing.
        /// </summary>
        public int Retry { get; init; } = 3;

        /// <summary>
        /// The wait before the next request, from the number of failures before this one and this failure's
        /// exception: TanStack Query's <c>retryDelay</c>. When null, one second doubled at each failure and
        /// capped at thirty seconds, TanStack Query's default. The wait is measured on <see cref="Clock"/>
        /// and checked once a frame, and lasts at least one frame.
        /// </summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Whether a query fetches again when the application becomes visible, as <see cref="NetworkSignals"/>
        /// reads it: TanStack Query's <c>refetchOnWindowFocus</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnWindowFocus { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// Whether a query fetches again when the device comes back online, as <see cref="NetworkSignals"/>
        /// reads it: TanStack Query's <c>refetchOnReconnect</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnReconnect { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// A monotonic clock the client reads instead of its own stopwatch, for a host that measures time
        /// differently — game time that pauses, or a clock a test advances by hand.
        /// </summary>
        public Func<TimeSpan>? Clock { get; init; }
    }

    /// <summary>
    /// Provides a <see cref="QueryClient"/> to the <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> calls below it:
    /// <c>V.Provider(QueryClientContext.Ref, value: client, children: ...)</c>, TanStack Query's
    /// <c>QueryClientProvider</c>. Read it with <c>Hooks.UseContext(QueryClientContext.Ref)</c> to reach the
    /// client from a component, as <c>useQueryClient()</c> does.
    /// </summary>
    public static class QueryClientContext
    {
        /// <summary>
        /// The context <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> reads. Its default is null, so a query with no
        /// Provider above it and no client passed to it throws rather than sharing a hidden one.
        /// </summary>
        public static readonly ComponentContext<QueryClient?> Ref
            = ComponentContext<QueryClient?>.Create(defaultValue: null);
    }

    /// <summary>
    /// TanStack Query's <c>QueryClient</c>: a cache of query results keyed by <see cref="QueryKey"/>, shared
    /// by every <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> that reaches it. One entry holds one result and at most one
    /// request in flight, whichever component started it, and keeps both after the components reading it
    /// unmount, until it is collected.
    /// </summary>
    /// <remarks>
    /// Main thread only, like the rest of Velvet. <b>Deviation:</b> garbage collection runs no timer. An
    /// entry whose time has run out reads as absent from then on, and is removed the next time a query
    /// subscribes to this client, <see cref="InvalidateQueries"/> runs or
    /// <see cref="SetQueryData{T}(QueryKey, T)"/> writes, where TanStack Query removes it when its timer fires.
    /// An entry with a request in flight has not run out, as v5 puts its removal off while it fetches: it runs
    /// out a whole garbage-collection time after the request settles.
    /// </remarks>
    public sealed class QueryClient
    {
        private readonly Dictionary<QueryKey, QueryEntry> _entries = new();
        private readonly List<QueryEntry> _inactive = new();
        private static readonly Func<int, Exception, TimeSpan> s_exponentialBackoff
            = (failureCount, _) => TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2d, failureCount), 30000d));

        private readonly Func<TimeSpan> _clock;
        // Advanced by every render that reads this client, so an entry whose last reader left under the current
        // value left after the render whose effects are subscribing now.
        private int _readEpoch;
        private int _observerCount;

        /// <summary>Creates an empty client.</summary>
        /// <param name="options">Defaults and clock; null takes <see cref="QueryClientOptions"/>' own.</param>
        public QueryClient(QueryClientOptions? options = null)
        {
            options ??= new QueryClientOptions();
            DefaultStaleTime = RequireNonNegative(options.StaleTime, nameof(QueryClientOptions.StaleTime));
            DefaultGcTime = RequireNonNegative(options.GcTime, nameof(QueryClientOptions.GcTime));
            DefaultRetry = options.Retry;
            DefaultRetryDelay = options.RetryDelay ?? s_exponentialBackoff;
            DefaultRefetchOnWindowFocus = options.RefetchOnWindowFocus;
            DefaultRefetchOnReconnect = options.RefetchOnReconnect;
            if (options.Clock != null)
            {
                _clock = options.Clock;
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => stopwatch.Elapsed;
            }
        }

        /// <summary>The stale time a query that sets none of its own uses.</summary>
        public TimeSpan DefaultStaleTime { get; }

        /// <summary>The garbage-collection time a query that sets none of its own uses.</summary>
        public TimeSpan DefaultGcTime { get; }

        /// <summary>The number of retries a query that sets none of its own uses.</summary>
        public int DefaultRetry { get; }

        /// <summary>The retry delay a query that sets none of its own uses.</summary>
        public Func<int, Exception, TimeSpan> DefaultRetryDelay { get; }

        /// <summary>Whether a query that sets none of its own fetches again when the application becomes
        /// visible.</summary>
        public QueryRefetchMode DefaultRefetchOnWindowFocus { get; }

        /// <summary>Whether a query that sets none of its own fetches again when the device comes back
        /// online.</summary>
        public QueryRefetchMode DefaultRefetchOnReconnect { get; }

        /// <summary>
        /// Marks every entry whose key <paramref name="queryKey"/> matches stale, and fetches again each
        /// one a mounted query is reading — TanStack Query's <c>invalidateQueries({ queryKey })</c>, with the
        /// partial match of its filter that <see cref="QueryKey"/> describes. For such
        /// an entry, a request already in flight is cancelled and started over when the entry holds data, so
        /// a result fetched before the change that invalidated it does not land as current, and joined when
        /// the entry is still waiting on its first result. An entry nothing reads is not fetched: a query
        /// mounting over it later fetches it, and a request it already had in flight is left to land.
        /// </summary>
        /// <param name="queryKey">The filter; null matches every entry.</param>
        public void InvalidateQueries(QueryKey? queryKey = null)
        {
            CollectGarbage();
            foreach (var entry in SnapshotEntries())
            {
                if (queryKey == null || entry.Key.Matches(queryKey))
                {
                    entry.Invalidate();
                }
            }
        }

        /// <summary>
        /// The data the entry <paramref name="queryKey"/> names holds, TanStack Query's <c>getQueryData</c>:
        /// default when there is no such entry, it has no data yet, or it has gone unread past its
        /// garbage-collection time. Placeholder data is never an entry's.
        /// </summary>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? GetQueryData<T>(QueryKey queryKey)
        {
            if (queryKey == null) throw new ArgumentNullException(nameof(queryKey));
            var entry = _entries.TryGetValue(queryKey, out var found) && !IsExpired(found, Now) ? Typed<T>(found) : null;
            return entry != null ? entry.Data : default;
        }

        /// <summary>
        /// Writes <paramref name="data"/> into the entry <paramref name="queryKey"/> names, creating it when there
        /// is none, TanStack Query's <c>setQueryData</c>: the entry becomes <see cref="QueryStatus.Success"/>
        /// with fresh data and no error, and the queries reading it are told, re-rendering for what changed. The data is
        /// shared structurally with what the entry held, as a request landing is. A request in flight is left
        /// running and lands over it. A new entry nothing reads expires after
        /// <see cref="DefaultGcTime"/>. A null <paramref name="data"/> writes nothing.
        /// </summary>
        /// <returns>The data the entry holds afterwards; default when nothing was written.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? SetQueryData<T>(QueryKey queryKey, T data)
        {
            if (queryKey == null) throw new ArgumentNullException(nameof(queryKey));
            if (data is null) return default;
            return Ensure<T>(queryKey, DefaultGcTime).SetData(data);
        }

        /// <summary>
        /// <see cref="SetQueryData{T}(QueryKey, T)"/> with the data <paramref name="updater"/> makes of the data
        /// the entry holds, default when it holds none, as TanStack Query's functional updater. An updater
        /// returning null writes nothing.
        /// </summary>
        /// <returns>The data the entry holds afterwards; default when nothing was written.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? SetQueryData<T>(QueryKey queryKey, Func<T?, T> updater)
        {
            if (updater == null) throw new ArgumentNullException(nameof(updater));
            return SetQueryData<T>(queryKey, updater(GetQueryData<T>(queryKey)));
        }

        /// <summary>
        /// Removes every entry and cancels every request in flight, as TanStack Query's <c>clear()</c>. A
        /// mounted query re-renders without data and fetches into a new entry.
        /// </summary>
        public void Clear()
        {
            var entries = SnapshotEntries();
            _entries.Clear();
            _inactive.Clear();
            foreach (var entry in entries)
            {
                entry.Remove();
            }
        }

        internal TimeSpan Now => _clock();

        // The client watches the application's visibility and connection while a query observes it, which is
        // when v5's onFocus and onOnline can find an observer to refetch for.
        internal void ObserverAdded()
        {
            if (_observerCount++ == 0) QueryClientSignals.Watch(this);
        }

        internal void ObserverRemoved()
        {
            if (--_observerCount == 0) QueryClientSignals.Unwatch(this);
        }

        // TanStack's queryCache.onFocus and onOnline.
        internal void OnSignal(bool reconnect)
        {
            foreach (var entry in SnapshotEntries())
            {
                entry.OnSignal(reconnect);
            }
        }

        // An expired entry reads as absent before a sweep removes it, so a render never shows an entry that had
        // already expired and that a sibling's subscription in the same commit would sweep.
        internal QueryEntry<T>? Peek<T>(QueryKey key)
        {
            _readEpoch++;
            return _entries.TryGetValue(key, out var entry) && !IsExpired(entry, Now) ? Typed<T>(entry) : null;
        }

        internal QueryEntry<T> Build<T>(QueryKey key, TimeSpan gcTime)
        {
            // A reader leaving in this commit's effects hands its entry to the reader arriving in them — a
            // keyed remount, two readers swapped — however short its gcTime, as v5's addObserver clears the
            // removal timeout. Taken back before the sweep, which otherwise replaces an expired entry rather
            // than handing it back.
            if (_entries.TryGetValue(key, out var existing) && existing.InactiveEpoch == _readEpoch)
            {
                MarkActive(existing);
            }
            CollectGarbage();
            if (_entries.TryGetValue(key, out existing))
            {
                var typed = Typed<T>(existing);
                typed.RaiseGcTime(gcTime);
                return typed;
            }

            var entry = new QueryEntry<T>(this, key, gcTime);
            _entries.Add(key, entry);
            return entry;
        }

        // TanStack's queryCache.build outside a subscription: an entry made here has no reader yet, so it is
        // collected after its gcTime unless one subscribes, as v5 schedules a new query's removal at once.
        internal QueryEntry<T> Ensure<T>(QueryKey key, TimeSpan gcTime)
        {
            CollectGarbage();
            // An entry that exists is returned as it is, gcTime included, as v5's build returns the query it finds.
            if (_entries.TryGetValue(key, out var existing)) return Typed<T>(existing);

            var entry = new QueryEntry<T>(this, key, gcTime);
            _entries.Add(key, entry);
            MarkInactive(entry);
            return entry;
        }

        internal void MarkInactive(QueryEntry entry)
        {
            entry.InactiveSince = Now;
            entry.InactiveEpoch = _readEpoch;
            _inactive.Add(entry);
        }

        internal void MarkActive(QueryEntry entry)
        {
            entry.InactiveSince = null;
            // MUTANT_SURVIVES(equivalent, line removed): an entry left listed has no InactiveSince, so no sweep expires it; the list only grows.
            _inactive.Remove(entry);
        }

        internal void CollectGarbage()
        {
            var now = Now;
            for (var i = _inactive.Count - 1; i >= 0; i--)
            {
                var entry = _inactive[i];
                if (!IsExpired(entry, now)) continue;
                _inactive.RemoveAt(i);
                _entries.Remove(entry.Key);
                entry.Remove();
            }
        }

        // An entry fetching is not expired whatever its age, as v5's optionalRemove keeps a query whose fetch is
        // not idle; QueryEntry.RescheduleCollection moves its deadline once the request settles.
        private static bool IsExpired(QueryEntry entry, TimeSpan now)
        {
            var since = entry.InactiveSince;
            return since != null && !entry.IsFetching && now - since.Value >= entry.GcTime;
        }

        internal static TimeSpan RequireNonNegative(TimeSpan value, string name)
            => value < TimeSpan.Zero
                ? throw new ArgumentOutOfRangeException(name, value, $"{name} must not be negative.")
                : value;

        private QueryEntry[] SnapshotEntries()
        {
            var entries = new QueryEntry[_entries.Count];
            _entries.Values.CopyTo(entries, 0);
            return entries;
        }

        private static QueryEntry<T> Typed<T>(QueryEntry entry)
            => entry as QueryEntry<T> ?? throw new InvalidOperationException(
                $"Query {entry.Key} holds {entry.DataType.Name} and was read as {typeof(T).Name}. " +
                "A key names one entry, so every query sharing it must agree on its data type.");
    }

    internal abstract class QueryEntry
    {
        protected QueryEntry(QueryClient client, QueryKey key, TimeSpan gcTime)
        {
            Client = client;
            Key = key;
            GcTime = gcTime;
        }

        internal QueryClient Client { get; }
        internal QueryKey Key { get; }
        internal TimeSpan GcTime { get; private set; }
        internal TimeSpan? InactiveSince { get; set; }
        internal int InactiveEpoch { get; set; }
        internal abstract Type DataType { get; }
        internal abstract bool IsFetching { get; }

        // v5's fetch calls scheduleGc as it settles, which starts the removal timer over, so an entry nothing reads
        // goes a whole gcTime after its request settled. An entry something reads has no removal to start over.
        internal void RescheduleCollection(TimeSpan now)
        {
            if (InactiveSince != null) InactiveSince = now;
        }

        // The longest any query asked for, as TanStack's updateGcTime keeps.
        internal void RaiseGcTime(TimeSpan gcTime)
        {
            // MUTANT_SURVIVES(equivalent, boundary): at equal times the assignment writes the value already held.
            if (gcTime > GcTime) GcTime = gcTime;
        }

        internal abstract void Invalidate();
        internal abstract void Remove();
        internal abstract void OnSignal(bool reconnect);
    }

    // One cached result and the request that fills it. The request belongs to the entry rather than to the
    // component that started it, so an observer leaving takes nothing from the observers that stay.
    internal sealed class QueryEntry<T> : QueryEntry
    {
        private readonly List<QueryEntryObserver<T>> _observers = new();
        private CancellationTokenSource? _inFlight;
        private bool _retriesStopped;
        private TimeSpan _dataUpdatedAt;
        // TanStack's query.options: what the last query to commit over the entry, or to start a request for it,
        // handed it. An invalidation's refetch runs with them, whichever observer they came from.
        private QueryFetchOptions<T>? _options;

        internal QueryEntry(QueryClient client, QueryKey key, TimeSpan gcTime) : base(client, key, gcTime)
        {
        }

        internal override Type DataType => typeof(T);
        internal QueryStatus Status { get; private set; } = QueryStatus.Pending;
        internal bool HasData { get; private set; }
        internal T Data { get; private set; } = default!;
        internal Exception? Error { get; private set; }
        internal int FailureCount { get; private set; }
        internal Exception? FailureReason { get; private set; }
        internal bool IsInvalidated { get; private set; }
        internal bool IsRemoved { get; private set; }
        internal override bool IsFetching => _inFlight != null;

        internal TimeSpan DataUpdatedAt => _dataUpdatedAt;

        internal bool IsStaleFor(TimeSpan staleTime)
            => !HasData || IsInvalidated || Client.Now - _dataUpdatedAt >= staleTime;

        internal void SetOptions(QueryFetchOptions<T> options) => _options = options;

        internal void Subscribe(QueryEntryObserver<T> observer)
        {
            _observers.Add(observer);
            Client.ObserverAdded();
            if (_observers.Count == 1) Client.MarkActive(this);
            if (observer.Enabled && IsStaleFor(observer.StaleTime))
            {
                Fetch(observer.FetchOptions, cancelRefetch: false);
            }
        }

        // With the last observer gone, a request in flight finishes but is not retried: TanStack's
        // removeObserver calls cancelRetry where it leaves the request running.
        internal void Unsubscribe(QueryEntryObserver<T> observer)
        {
            // The options this observer handed are rewritten in place at its next commit, where v5's query keeps
            // a copy of what it was handed.
            if (ReferenceEquals(_options, observer.FetchOptions)) _options = observer.FetchOptions.Copy();
            if (!_observers.Remove(observer)) return;
            Client.ObserverRemoved();
            if (_observers.Count > 0 || IsRemoved) return;
            _retriesStopped = true;
            Client.MarkInactive(this);
        }

        // cancelRefetch is TanStack's fetch option of the same name: an explicit refetch over an entry that
        // holds data starts over, and every other request joins the one in flight, which retries again if
        // its retries had been stopped.
        internal void Fetch(QueryFetchOptions<T> options, bool cancelRefetch)
        {
            if (IsRemoved) return;
            if (_inFlight != null)
            {
                if (!cancelRefetch || !HasData)
                {
                    _retriesStopped = false;
                    return;
                }
                CancelInFlight();
            }
            // v5's fetch takes the options it is handed only once it is not joining a request in flight.
            _options = options;

            var request = new CancellationTokenSource();
            _inFlight = request;
            _retriesStopped = false;
            FailureCount = 0;
            FailureReason = null;
            if (!HasData)
            {
                Status = QueryStatus.Pending;
                Error = null;
            }

            Notify();
            RunAsync(options, request).Forget();
        }

        private async VelvetTask RunAsync(QueryFetchOptions<T> options, CancellationTokenSource request)
        {
            var queryFn = options.QueryFn;
            var maxRetries = options.MaxRetries;
            var retryDelay = options.RetryDelay;
            var sharing = options.StructuralSharing;
            for (var failures = 0; ; failures++)
            {
                T data = default!;
                Exception? failure = null;
                var token = request.Token;
                var task = Start(queryFn, token);
                try
                {
                    // A task that has already completed is held back one frame, so that readers subscribing in
                    // the commit that started this request join it rather than finding its result, as v5's
                    // result arrives a microtask later. UseQueryHookTests pins it with two readers mounting
                    // together.
                    if (task.Status.IsCompleted()) await VelvetTask.Yield();
                    data = await task.AttachExternalCancellation(token);
                }
                // Same gate as FiberAsyncResource.AwaitAsync: a cancellation this entry asked for records
                // nothing, and one raised by a token the query function owns is an error.
                catch (OperationCanceledException) when (request.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (request.IsCancellationRequested) return;
                if (failure == null)
                {
                    // v5 shares a landing with the query's options as they stand when it settles; a request started
                    // with options the entry was never handed falls back to its own.
                    Succeed(request, data, _options != null ? _options.StructuralSharing : sharing, failures);
                    return;
                }

                if (failures >= maxRetries || _retriesStopped)
                {
                    Fail(request, failure, failures);
                    return;
                }

                TimeSpan delay;
                try
                {
                    delay = retryDelay(failures, failure);
                }
                catch (Exception delayFailure)
                {
                    Fail(request, delayFailure, failures);
                    return;
                }

                RecordFailure(request, failures + 1, failure);
                await WaitFor(delay, request);
                if (request.IsCancellationRequested) return;
                if (_retriesStopped)
                {
                    Fail(request, failure, failures);
                    return;
                }
            }
        }

        // A query function that throws before returning a task fails the request as one returning a faulted
        // task does.
        private static VelvetTask<T> Start(Func<CancellationToken, VelvetTask<T>> queryFn, CancellationToken token)
        {
            try
            {
                return queryFn(token);
            }
            catch (Exception ex)
            {
                return VelvetTask.FromException<T>(ex);
            }
        }

        // At least one frame, as v5 sleeps through a timer even for a zero delay. With the retries stopped the
        // wait ends early, since its only outcome is the failure.
        private async VelvetTask WaitFor(TimeSpan delay, CancellationTokenSource request)
        {
            var start = Client.Now;
            do
            {
                await VelvetTask.Yield();
            }
            while (!request.IsCancellationRequested && !_retriesStopped && Client.Now - start < delay);
        }

        private void Succeed(CancellationTokenSource request, T data, Func<T?, T, T>? sharing, int failures)
        {
            // A request stops being the one in flight when something cancels it or removes the entry, and a
            // completion already posted to the main thread by then still arrives here.
            if (!ReferenceEquals(_inFlight, request)) return;
            T shared;
            try
            {
                var held = HasData ? Data : default;
                shared = sharing != null ? sharing(held, data) : QueryStructuralSharing.Replace(held, data);
            }
            catch (Exception ex)
            {
                Fail(request, ex, failures);
                return;
            }

            End(request);
            Data = shared;
            HasData = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            FailureCount = 0;
            FailureReason = null;
            _dataUpdatedAt = Client.Now;
            Notify();
        }

        // TanStack's setData with manual set, as setQueryData calls it: the data lands as a request's would, but a
        // request in flight keeps running and its failure count stays.
        internal T SetData(T data)
        {
            var held = HasData ? Data : default;
            // v5's setData shares with the query's own options, those the entry was last handed.
            var sharing = _options?.StructuralSharing;
            var shared = sharing != null ? sharing(held, data) : QueryStructuralSharing.Replace(held, data);
            Data = shared;
            HasData = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            _dataUpdatedAt = Client.Now;
            Notify();
            return shared;
        }

        private void Fail(CancellationTokenSource request, Exception error, int failures)
        {
            if (!ReferenceEquals(_inFlight, request)) return;
            End(request);
            // The last good data stays beside the error, as TanStack keeps it, and stale: TanStack flags it
            // invalidated so that the next reader fetches again.
            Error = error;
            Status = QueryStatus.Error;
            IsInvalidated = true;
            FailureCount = failures + 1;
            FailureReason = error;
            Notify();
        }

        private void RecordFailure(CancellationTokenSource request, int failureCount, Exception error)
        {
            if (!ReferenceEquals(_inFlight, request)) return;
            FailureCount = failureCount;
            FailureReason = error;
            Notify();
        }

        private void End(CancellationTokenSource request)
        {
            _inFlight = null;
            RescheduleCollection(Client.Now);
            // MUTANT_SURVIVES(equivalent, line removed): nothing cancels a settled request again, so its source only waits for collection.
            request.Dispose();
        }

        // Only an entry an enabled query reads is fetched, as v5 refetches the active queries.
        internal override void Invalidate()
        {
            IsInvalidated = true;
            foreach (var observer in _observers)
            {
                if (!observer.Enabled) continue;
                // An observer hands the entry its options when it subscribes, so an entry with one holds some.
                Fetch(_options!, cancelRefetch: true);
                return;
            }
        }

        // TanStack's query.onFocus and onOnline: the first observer that asks for it refetches, joining a request
        // already in flight.
        internal override void OnSignal(bool reconnect)
        {
            foreach (var observer in _observers)
            {
                var mode = reconnect ? observer.RefetchOnReconnect : observer.RefetchOnWindowFocus;
                if (!observer.ShouldFetchOn(mode, this)) continue;
                Fetch(observer.FetchOptions, cancelRefetch: false);
                return;
            }
        }

        internal override void Remove()
        {
            IsRemoved = true;
            CancelInFlight();
            foreach (var observer in _observers.ToArray())
            {
                observer.OnRemoved();
            }
        }

        private void CancelInFlight()
        {
            var cts = _inFlight;
            if (cts == null) return;
            _inFlight = null;
            // Contained on FiberAsyncResource.Dispose's terms: the token went to the query function, so a
            // callback firing here can be the application's.
            try
            {
                cts.Cancel();
            }
            catch (Exception cancellationFailure)
            {
                FiberLogger.LogException(nameof(QueryClient), cancellationFailure);
            }
            // MUTANT_SURVIVES(equivalent, line removed): the source is cancelled already, and RunAsync's catch reads only IsCancellationRequested, which disposal leaves alone.
            cts.Dispose();
        }

        private void Notify()
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnQueryUpdate();
            }
        }
    }

    // TanStack's focusManager and onlineManager, as the clients observing queries subscribe to them: the readings
    // NetworkSignals takes are polled once a frame while a client watches, and a reading turning true since the
    // client's last one is the event. Each client keeps its own last reading, taken first when it starts
    // watching: a change while it was not watching is not one it is told of.
    internal static class QueryClientSignals
    {
        private sealed class Watcher
        {
            internal Watcher(QueryClient client, bool visible, bool online)
            {
                Client = client;
                Visible = visible;
                Online = online;
            }

            internal QueryClient Client { get; }
            internal bool Visible { get; set; }
            internal bool Online { get; set; }
        }

        private static readonly List<Watcher> s_watchers = new();
        // The watchers as a poll found them, refilled each frame rather than copied anew.
        private static readonly List<Watcher> s_polled = new();
        private static readonly Func<bool> s_readVisible = NetworkSignals.ReadVisible;
        private static readonly Func<bool> s_readOnline = NetworkSignals.ReadOnline;
        private static bool s_polling;
        // Whether a failing reading has been logged, so one that fails every frame logs once.
        private static bool s_visibleFailureLogged;
        private static bool s_onlineFailureLogged;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_watchers.Clear();
            // MUTANT_SURVIVES(equivalent, line removed): a poll refills the list from the watchers before it reads it.
            s_polled.Clear();
            s_polling = false;
            s_visibleFailureLogged = false;
            s_onlineFailureLogged = false;
            NetworkSignals.IsVisible = null;
            NetworkSignals.IsOnline = null;
        }

        internal static void Watch(QueryClient client)
        {
            s_watchers.Add(new Watcher(
                client, IsVisible(), TryRead(s_readOnline, ref s_onlineFailureLogged) ?? true));
            // MUTANT_SURVIVES(equivalent, guard removed): a second poll runs after the first has stored the frame's readings in every watcher, so it finds no change to report.
            if (s_polling) return;
            // MUTANT_SURVIVES(equivalent, line removed): left unset, each watch starts a poll of its own, and a second poll finds no change for the reason above.
            s_polling = true;
            Poll().Forget();
        }

        // TanStack's focusManager.isFocused(), which a refetch interval asks at each tick.
        internal static bool IsVisible() => TryRead(s_readVisible, ref s_visibleFailureLogged) ?? true;

        internal static void Unwatch(QueryClient client)
        {
            // MUTANT_SURVIVES(equivalent, boundary): a client is unwatched only after it was watched, so the loop returns before the bound.
            for (var i = 0; i < s_watchers.Count; i++)
            {
                if (!ReferenceEquals(s_watchers[i].Client, client)) continue;
                s_watchers.RemoveAt(i);
                return;
            }
        }

        private static async VelvetTask Poll()
        {
            while (s_watchers.Count > 0)
            {
                await VelvetTask.Yield();
                var visible = TryRead(s_readVisible, ref s_visibleFailureLogged);
                var online = TryRead(s_readOnline, ref s_onlineFailureLogged);
                // MUTANT_SURVIVES(equivalent, line removed): a watcher visited again finds the readings its first visit stored, so it reports no change, and a client told twice in a frame joins the request the first signal started.
                s_polled.Clear();
                s_polled.AddRange(s_watchers);
                foreach (var watcher in s_polled)
                {
                    var focused = visible == true && !watcher.Visible;
                    var reconnected = online == true && !watcher.Online;
                    if (visible is { } nowVisible) watcher.Visible = nowVisible;
                    if (online is { } nowOnline) watcher.Online = nowOnline;
                    if (focused) watcher.Client.OnSignal(reconnect: false);
                    if (reconnected) watcher.Client.OnSignal(reconnect: true);
                }
            }

            s_polling = false;
        }

        // A reading an override throws from is taken as no reading, so one failure does not end the polling for
        // every client, and only its first failure is logged.
        private static bool? TryRead(Func<bool> read, ref bool failureLogged)
        {
            try
            {
                return read();
            }
            catch (Exception readFailure)
            {
                if (!failureLogged) FiberLogger.LogException(nameof(NetworkSignals), readFailure);
                failureLogged = true;
                return null;
            }
        }
    }
}
