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

        /// <summary>
        /// Marks every entry whose key starts with <paramref name="queryKey"/> stale, and fetches again each
        /// one a mounted query is reading — TanStack Query's <c>invalidateQueries({ queryKey })</c>. For such
        /// an entry, a request already in flight is cancelled and started over when the entry holds data, so
        /// a result fetched before the change that invalidated it does not land as current, and joined when
        /// the entry is still waiting on its first result. An entry nothing reads is not fetched: a query
        /// mounting over it later fetches it, and a request it already had in flight is left to land.
        /// </summary>
        /// <param name="queryKey">The leading parts to match; null matches every entry.</param>
        public void InvalidateQueries(QueryKey? queryKey = null)
        {
            CollectGarbage();
            foreach (var entry in SnapshotEntries())
            {
                if (queryKey == null || entry.Key.StartsWith(queryKey))
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
        private TimeSpan _dataUpdatedAt;

        internal QueryEntry(QueryClient client, QueryKey key, TimeSpan gcTime) : base(client, key, gcTime)
        {
        }

        internal override Type DataType => typeof(T);
        internal QueryStatus Status { get; private set; } = QueryStatus.Pending;
        internal bool HasData { get; private set; }
        internal T Data { get; private set; } = default!;
        internal Exception? Error { get; private set; }
        internal bool IsInvalidated { get; private set; }
        internal bool IsRemoved { get; private set; }
        internal bool IsFetching => _inFlight != null;

        // Advanced each time a request is left in flight or settles, so an observer subscribing after its
        // render can tell whether that render saw the current state.
        internal int Version { get; private set; }

        internal bool IsStaleFor(TimeSpan staleTime)
            => !HasData || IsInvalidated || Client.Now - _dataUpdatedAt >= staleTime;

        internal void Subscribe(QueryObserver<T> observer)
        {
            _observers.Add(observer);
            if (_observers.Count == 1) Client.MarkActive(this);
            if (IsStaleFor(observer.StaleTime))
            {
                Fetch(observer.QueryFn, cancelRefetch: false);
            }
        }

        internal void Unsubscribe(QueryObserver<T> observer)
        {
            if (!_observers.Remove(observer) || _observers.Count > 0 || IsRemoved) return;
            Client.MarkInactive(this);
        }

        // cancelRefetch is TanStack's fetch option of the same name: an explicit refetch over an entry that
        // holds data starts over, and every other request joins the one in flight.
        internal void Fetch(Func<CancellationToken, VelvetTask<T>> queryFn, bool cancelRefetch)
        {
            if (IsRemoved) return;
            if (_inFlight != null)
            {
                if (!cancelRefetch || !HasData) return;
                CancelInFlight();
            }

            var cts = new CancellationTokenSource();
            _inFlight = cts;
            if (!HasData)
            {
                Status = QueryStatus.Pending;
                Error = null;
            }

            VelvetTask<T> task;
            try
            {
                task = queryFn(cts.Token);
            }
            catch (Exception ex)
            {
                Settle(cts, default!, ex);
                return;
            }

            if (task.Status.IsCompleted())
            {
                T result;
                try
                {
                    result = task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Settle(cts, default!, ex);
                    return;
                }
                Settle(cts, result, null);
                return;
            }

            Version++;
            Notify();
            AwaitAsync(task, cts).Forget();
        }

        private async VelvetTask AwaitAsync(VelvetTask<T> task, CancellationTokenSource cts)
        {
            T result;
            try
            {
                result = await task.AttachExternalCancellation(cts.Token);
            }
            // Same gate as FiberAsyncResource.AwaitAsync: a cancellation this entry asked for records
            // nothing, and one raised by a token the query function owns is an error.
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Settle(cts, default!, ex);
                return;
            }
            Settle(cts, result, null);
        }

        private void Settle(CancellationTokenSource request, T data, Exception? error)
        {
            // A request stops being the one in flight when something cancels it or removes the entry.
            // Cancelling it ends AwaitAsync in its cancellation catch, except where the request completed on
            // another thread first: that completion is already posted to the main thread and arrives here.
            if (!ReferenceEquals(_inFlight, request)) return;
            _inFlight = null;
            // MUTANT_SURVIVES(equivalent, line removed): nothing cancels a settled request again, so its source only waits for collection.
            request.Dispose();
            if (error == null)
            {
                Data = data;
                HasData = true;
                Error = null;
                Status = QueryStatus.Success;
                IsInvalidated = false;
                _dataUpdatedAt = Client.Now;
            }
            else
            {
                // The last good data stays beside the error, as TanStack keeps it.
                Error = error;
                Status = QueryStatus.Error;
            }
            Version++;
            Notify();
        }

        internal override void Invalidate()
        {
            IsInvalidated = true;
            if (_observers.Count > 0) Fetch(_observers[0].QueryFn, cancelRefetch: true);
        }

        internal override void Remove()
        {
            IsRemoved = true;
            CancelInFlight();
            Notify();
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
            // MUTANT_SURVIVES(equivalent, line removed): the source is cancelled already, and AwaitAsync's catch reads only IsCancellationRequested, which disposal leaves alone.
            cts.Dispose();
        }

        private void Notify()
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnChange();
            }
        }
    }
}
