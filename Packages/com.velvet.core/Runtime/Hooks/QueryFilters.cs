#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Which entries a <see cref="QueryFilters"/> selects by whether a mounted, enabled query reads them:
    /// TanStack Query's filter <c>type</c>.
    /// </summary>
    public enum QueryTypeFilter
    {
        /// <summary>Every entry, TanStack Query's <c>'all'</c>.</summary>
        All,
        /// <summary>Entries an enabled query reads, TanStack Query's <c>'active'</c>.</summary>
        Active,
        /// <summary>Entries no enabled query reads, TanStack Query's <c>'inactive'</c>.</summary>
        Inactive,
    }

    /// <summary>Whether an entry has a request in flight: TanStack Query's filter <c>fetchStatus</c>.</summary>
    public enum QueryFetchStatus
    {
        /// <summary>A request is in flight.</summary>
        Fetching,
        /// <summary>No request is in flight.</summary>
        Idle,
    }

    /// <summary>
    /// Selects the entries a <see cref="QueryClient"/> method works on, TanStack Query's query filters. Every
    /// property that is set must match: an entry is selected when its key matches, it is of the type asked for,
    /// its staleness and fetch status are those asked for, and the predicate accepts it.
    /// </summary>
    public sealed record QueryFilters
    {
        /// <summary>
        /// The key the entries are selected by: the entries whose key it leads, or only the entry it equals
        /// under <see cref="Exact"/>. Null selects every key. See <see cref="Velvet.QueryKey"/> for how a
        /// filter key matches.
        /// </summary>
        public QueryKey? QueryKey { get; init; }

        /// <summary>True selects only the entry whose key equals <see cref="QueryKey"/>.</summary>
        public bool Exact { get; init; }

        /// <summary>Selects by whether an enabled query reads the entry; null is
        /// <see cref="QueryTypeFilter.All"/>, except where a method documents another default.</summary>
        public QueryTypeFilter? Type { get; init; }

        /// <summary>True selects only stale entries and false only fresh ones, as
        /// <see cref="QueryResult{T}.IsStale"/> reads them for a mounted query and as an entry with no data
        /// or an invalidated one is stale for an entry nothing reads. Null selects both.</summary>
        public bool? Stale { get; init; }

        /// <summary>Selects by whether a request is in flight; null selects both.</summary>
        public QueryFetchStatus? FetchStatus { get; init; }

        /// <summary>Accepts or declines each entry the other properties select, handed its
        /// <see cref="QueryInfo"/>.</summary>
        public Func<QueryInfo, bool>? Predicate { get; init; }
    }

    /// <summary>
    /// What <see cref="QueryClient.RefetchQueries"/>, <see cref="QueryClient.ResetQueries"/> and the filter form
    /// of <see cref="QueryClient.InvalidateQueries(QueryFilters, RefetchOptions)"/> do with the requests they
    /// start: TanStack Query's <c>refetchQueries</c> options.
    /// </summary>
    public sealed record RefetchOptions
    {
        /// <summary>True, the default, cancels a request in flight over data the entry holds and starts it
        /// over; false joins it.</summary>
        public bool CancelRefetch { get; init; } = true;

        /// <summary>True makes the returned task fault with the first failure of the requests it waited for,
        /// where the default completes whatever they did.</summary>
        public bool ThrowOnError { get; init; }
    }

    /// <summary>
    /// An entry's state as <see cref="QueryClient.GetQueryState{T}"/> reads it, a snapshot that does not follow
    /// the entry: TanStack Query's <c>QueryState</c>.
    /// </summary>
    /// <typeparam name="T">The entry's data type.</typeparam>
    public sealed class QueryState<T>
    {
        internal QueryState()
        {
        }

        /// <summary>Whether the entry has data; see <see cref="QueryStatus"/>.</summary>
        public QueryStatus Status { get; internal init; }

        /// <summary>Whether the entry holds data.</summary>
        public bool HasData { get; internal init; }

        /// <summary>The entry's data; default while it holds none.</summary>
        public T? Data { get; internal init; }

        /// <summary>The failure of the entry's last request, under <see cref="QueryStatus.Error"/>.</summary>
        public Exception? Error { get; internal init; }

        /// <summary>Whether the entry was invalidated, or its last request failed, since its data landed.</summary>
        public bool IsInvalidated { get; internal init; }

        /// <summary>Whether a request for the entry is in flight.</summary>
        public bool IsFetching { get; internal init; }

        /// <summary>When the entry's data landed, on the client's clock; zero while it holds none.</summary>
        public TimeSpan DataUpdatedAt { get; internal init; }

        /// <summary>How many times the request in flight or the last one has failed.</summary>
        public int FailureCount { get; internal init; }

        /// <summary>The exception of the latest failure of the request in flight or the last one; null once a
        /// request lands.</summary>
        public Exception? FailureReason { get; internal init; }
    }
}
