#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// TanStack Query's mutation <c>scope</c>: mutations whose scopes share an <see cref="Id"/> run one after
    /// another, in the order they were started, rather than side by side. A mutation that cannot run yet is
    /// pending and paused until every earlier one in its scope has settled.
    /// </summary>
    /// <param name="Id">The scope's name.</param>
    public sealed record MutationScope(string Id);

    /// <summary>
    /// The options every mutation options record shares: TanStack Query's <c>mutationKey</c>, <c>scope</c>,
    /// <c>meta</c>, <c>gcTime</c>, <c>retry</c> and <c>throwOnError</c>. <see cref="Mutation.Options"/> is the record
    /// the hook was given, with the client's defaults filled in, so a cache callback can read these off any mutation,
    /// and test it for the concrete record to read the rest.
    /// </summary>
    /// <remarks>
    /// A property left null takes the value <see cref="QueryClient.DefaultMutationOptions"/> finds for it: the
    /// defaults <see cref="QueryClient.SetMutationDefaults"/> registered for a key leading
    /// <see cref="MutationKey"/>, and then <see cref="QueryClientOptions.Mutations"/>. A callback or a
    /// <c>MutationFn</c> is taken from a default record of the same type.
    /// </remarks>
    public abstract record MutationOptionsBase
    {
        private protected MutationOptionsBase()
        {
        }

        /// <summary>
        /// Names the mutation for <see cref="MutationFilters.MutationKey"/> and for the defaults
        /// <see cref="QueryClient.SetMutationDefaults"/> registers. Null, the default, matches no filter that sets a
        /// key. A key replaced by an unequal key between two commits of the component detaches the handle from the
        /// mutation it shows, as <see cref="MutationResult{TVariables, TData}.Reset"/> does.
        /// </summary>
        public QueryKey? MutationKey { get; init; }

        /// <summary>Serialises the mutation with the others of its scope; null, the default, runs it at once.</summary>
        public MutationScope? Scope { get; init; }

        /// <summary>
        /// Values the application attaches to the mutation, handed to its function and callbacks in
        /// <see cref="MutationFunctionContext.Meta"/> and read by the <see cref="MutationCache"/> callbacks and filter
        /// predicates as <see cref="Mutation.Meta"/>.
        /// </summary>
        public IReadOnlyDictionary<string, object?>? Meta { get; init; }

        /// <summary>
        /// How long after it is built, or after a handle stops showing it, the mutation is removed from the
        /// <see cref="MutationCache"/>, on the client's clock; one still pending then is checked again that long
        /// later, and it is not collected while a handle shows it. Null takes TanStack Query's default, five
        /// minutes; <see cref="TimeSpan.MaxValue"/> keeps it until it is removed by hand or the cache is cleared.
        /// </summary>
        public TimeSpan? GcTime { get; init; }

        /// <summary>
        /// Retries a failed function within the same call: <see cref="MutationResult{TVariables, TData}.Status"/>,
        /// <see cref="MutationResult{TVariables, TData}.Data"/> and <see cref="MutationResult{TVariables, TData}.Error"/>
        /// stay uncommitted between attempts while <see cref="MutationResult{TVariables, TData}.FailureCount"/>,
        /// <see cref="MutationResult{TVariables, TData}.FailureReason"/> and
        /// <see cref="MutationResult{TVariables, TData}.IsPaused"/> follow them, and the callbacks run once, for the
        /// last attempt's outcome. When null, a failure is not retried, as TanStack Query's mutations default to
        /// <c>retry: 0</c>.
        /// </summary>
        public RetryPolicy? Retry { get; init; }

        /// <summary>
        /// Whether a failure the handle shows is thrown from the component's render to the nearest error boundary
        /// rather than only shown in <see cref="MutationResult{TVariables, TData}.Error"/>: TanStack Query's
        /// <c>throwOnError</c>. Null, like false, throws nothing.
        /// </summary>
        public ThrowOnErrorRule? ThrowOnError { get; init; }

        // The record with each property it leaves null taken from defaults: TanStack's object spread, in which an
        // option the caller set wins. Internal, so no record outside this assembly derives from this one.
        internal abstract MutationOptionsBase WithDefaults(MutationOptionsBase defaults);

        // This record's function and callbacks with the shared options taken from shared, set or not.
        internal MutationOptionsBase WithSharedFrom(MutationOptionsBase shared) => this with
        {
            MutationKey = shared.MutationKey,
            Scope = shared.Scope,
            Meta = shared.Meta,
            GcTime = shared.GcTime,
            Retry = shared.Retry,
            ThrowOnError = shared.ThrowOnError,
        };

        private protected MutationOptionsBase WithSharedDefaults(MutationOptionsBase defaults) => this with
        {
            MutationKey = MutationKey ?? defaults.MutationKey,
            Scope = Scope ?? defaults.Scope,
            Meta = Meta ?? defaults.Meta,
            GcTime = GcTime ?? defaults.GcTime,
            Retry = Retry ?? defaults.Retry,
            ThrowOnError = ThrowOnError ?? defaults.ThrowOnError,
        };
    }

    /// <summary>
    /// Mutation defaults that hold no function or callback, for <see cref="QueryClientOptions.Mutations"/> and
    /// <see cref="QueryClient.SetMutationDefaults"/>; a typed options record serves there too, and also supplies its
    /// function and callbacks to options of its own type.
    /// </summary>
    public sealed record MutationDefaults : MutationOptionsBase
    {
        internal override MutationOptionsBase WithDefaults(MutationOptionsBase defaults) => WithSharedDefaults(defaults);
    }

    /// <summary>
    /// What a mutation's function and callbacks are handed besides their own arguments: TanStack Query's
    /// <c>MutationFunctionContext</c>, the client the mutation runs in and its options' <c>meta</c> and
    /// <c>mutationKey</c>, read when the call starts.
    /// </summary>
    public sealed class MutationFunctionContext
    {
        internal MutationFunctionContext(
            QueryClient client,
            IReadOnlyDictionary<string, object?>? meta,
            QueryKey? mutationKey,
            CancellationToken cancellationToken)
        {
            Client = client;
            Meta = meta;
            MutationKey = mutationKey;
            CancellationToken = cancellationToken;
        }

        /// <summary>The client the mutation runs in.</summary>
        public QueryClient Client { get; }

        /// <summary>The options' <see cref="MutationOptionsBase.Meta"/>.</summary>
        public IReadOnlyDictionary<string, object?>? Meta { get; }

        /// <summary>The options' <see cref="MutationOptionsBase.MutationKey"/>.</summary>
        public QueryKey? MutationKey { get; }

        /// <summary>
        /// The token the caller put on the call's <see cref="MutateOptions{TVariables, TData}.CancellationToken"/>, or
        /// none. Nothing but that caller cancels it.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }

    /// <summary>
    /// TanStack Query's <c>throwOnError</c> as a value: <c>true</c> throws every failure, <c>false</c> none, and
    /// <see cref="When"/> decides from the failure. A bool converts implicitly.
    /// </summary>
    public readonly struct ThrowOnErrorRule : IEquatable<ThrowOnErrorRule>
    {
        private readonly bool _always;
        private readonly Func<Exception, bool>? _when;

        private ThrowOnErrorRule(bool always, Func<Exception, bool>? when)
        {
            _always = always;
            _when = when;
        }

        /// <summary>Throws the failures <paramref name="shouldThrow"/> accepts.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="shouldThrow"/> is null.</exception>
        public static ThrowOnErrorRule When(Func<Exception, bool> shouldThrow) =>
            new(false, shouldThrow ?? throw new ArgumentNullException(nameof(shouldThrow)));

        /// <summary><c>true</c> throws every failure, <c>false</c> none.</summary>
        public static implicit operator ThrowOnErrorRule(bool throwOnError) => new(throwOnError, null);

        internal bool Throws(Exception error) => _when?.Invoke(error) ?? _always;

        /// <inheritdoc/>
        public bool Equals(ThrowOnErrorRule other) => _always == other._always && Equals(_when, other._when);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ThrowOnErrorRule other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(_always, _when);
    }

    /// <summary>
    /// The observer a <see cref="Hooks.UseMutation{TVariables, TData}"/> handle reads its mutation through,
    /// TanStack Query's <c>MutationObserver</c>, as a <see cref="MutationCacheEvent.Observer"/> names it.
    /// </summary>
    public sealed class MutationObserver
    {
        internal MutationObserver(MutationOptionsBase options) => Options = options;

        /// <summary>The options the handle's component last committed, with the client's defaults filled in.</summary>
        public MutationOptionsBase Options { get; internal set; }
    }
}
