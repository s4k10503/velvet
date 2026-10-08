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
        /// A monotonic clock the client reads instead of its own stopwatch, for a host that measures time
        /// differently — game time that pauses, or a clock a test advances by hand.
        /// </summary>
        public Func<TimeSpan>? Clock { get; init; }
    }

    /// <summary>
    /// Provides a <see cref="QueryClient"/> to the <see cref="Hooks.UseQuery{T}"/> calls below it:
    /// <c>V.Provider(QueryClientContext.Ref, value: client, children: ...)</c>, TanStack Query's
    /// <c>QueryClientProvider</c>. Read it with <c>Hooks.UseContext(QueryClientContext.Ref)</c> to reach the
    /// client from a component, as <c>useQueryClient()</c> does.
    /// </summary>
    public static class QueryClientContext
    {
        /// <summary>
        /// The context <see cref="Hooks.UseQuery{T}"/> reads. Its default is null, so a query with no
        /// Provider above it and no client passed to it throws rather than sharing a hidden one.
        /// </summary>
        public static readonly ComponentContext<QueryClient?> Ref
            = ComponentContext<QueryClient?>.Create(defaultValue: null);
    }

    /// <summary>
    /// TanStack Query's <c>QueryClient</c>: a cache of query results keyed by <see cref="QueryKey"/>, shared
    /// by every <see cref="Hooks.UseQuery{T}"/> that reaches it. One entry holds one result and at most one
    /// request in flight, whichever component started it, and keeps both after the components reading it
    /// unmount, until it is collected.
    /// </summary>
    /// <remarks>
    /// Main thread only, like the rest of Velvet. <b>Deviation:</b> garbage collection runs no timer. An
    /// entry whose time has run out reads as absent from then on, and is removed, cancelling its request,
    /// the next time a query subscribes to this client or <see cref="InvalidateQueries"/> runs, where
    /// TanStack Query removes it when its timer fires.
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

        /// <summary>Creates an empty client.</summary>
        /// <param name="options">Defaults and clock; null takes <see cref="QueryClientOptions"/>' own.</param>
        public QueryClient(QueryClientOptions? options = null)
        {
            options ??= new QueryClientOptions();
            DefaultStaleTime = RequireNonNegative(options.StaleTime, nameof(QueryClientOptions.StaleTime));
            DefaultGcTime = RequireNonNegative(options.GcTime, nameof(QueryClientOptions.GcTime));
            DefaultRetry = options.Retry;
            DefaultRetryDelay = options.RetryDelay ?? s_exponentialBackoff;
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

        private static bool IsExpired(QueryEntry entry, TimeSpan now)
            => entry.InactiveSince is { } since && now - since >= entry.GcTime;

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

        // The longest any query asked for, as TanStack's updateGcTime keeps.
        internal void RaiseGcTime(TimeSpan gcTime)
        {
            // MUTANT_SURVIVES(equivalent, boundary): at equal times the assignment writes the value already held.
            if (gcTime > GcTime) GcTime = gcTime;
        }

        internal abstract void Invalidate();
        internal abstract void Remove();
    }

    // One cached result and the request that fills it. The request belongs to the entry rather than to the
    // component that started it, so an observer leaving takes nothing from the observers that stay.
    internal sealed class QueryEntry<T> : QueryEntry
    {
        private readonly List<QueryObserver<T>> _observers = new();
        private CancellationTokenSource? _inFlight;
        private bool _retriesStopped;
        private TimeSpan _dataUpdatedAt;

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
        internal bool IsFetching => _inFlight != null;

        internal TimeSpan DataUpdatedAt => _dataUpdatedAt;

        internal bool IsStaleFor(TimeSpan staleTime)
            => !HasData || IsInvalidated || Client.Now - _dataUpdatedAt >= staleTime;

        internal void Subscribe(QueryObserver<T> observer)
        {
            _observers.Add(observer);
            if (_observers.Count == 1) Client.MarkActive(this);
            if (IsStaleFor(observer.StaleTime))
            {
                Fetch(observer, cancelRefetch: false);
            }
        }

        // With the last observer gone, a request in flight finishes but is not retried: TanStack's
        // removeObserver calls cancelRetry where it leaves the request running.
        internal void Unsubscribe(QueryObserver<T> observer)
        {
            if (!_observers.Remove(observer) || _observers.Count > 0 || IsRemoved) return;
            _retriesStopped = true;
            Client.MarkInactive(this);
        }

        // cancelRefetch is TanStack's fetch option of the same name: an explicit refetch over an entry that
        // holds data starts over, and every other request joins the one in flight, which retries again if
        // its retries had been stopped.
        internal void Fetch(QueryObserver<T> observer, bool cancelRefetch)
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
            RunAsync(observer, request).Forget();
        }

        private async VelvetTask RunAsync(QueryObserver<T> observer, CancellationTokenSource request)
        {
            var queryFn = observer.QueryFn;
            var maxRetries = observer.MaxRetries;
            var retryDelay = observer.RetryDelay;
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
                    Succeed(request, data, observer.StructuralSharing, failures);
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
            // MUTANT_SURVIVES(equivalent, line removed): nothing cancels a settled request again, so its source only waits for collection.
            request.Dispose();
        }

        internal override void Invalidate()
        {
            IsInvalidated = true;
            if (_observers.Count > 0) Fetch(_observers[0], cancelRefetch: true);
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
                observer.Update();
            }
        }
    }
}
