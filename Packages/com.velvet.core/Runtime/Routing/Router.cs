#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Navigation controller: consults its blocker, matches paths against a route tree, runs guards and
    /// loaders, and maintains a history stack with Back/Forward. <c>V.RouterProvider</c> publishes one to the
    /// routing hooks beneath it.
    /// </summary>
    public sealed class Router : IDisposable
    {
        private readonly RouteTree _routeTree;
        private readonly RouteLoaderRunner _loaderRunner;
        private readonly List<string> _history = new();
        private readonly RouteBlockerManager _blockerManager = new();
        private int _historyIndex = -1;
        private Dictionary<string?, object> _loaderData = new();
        private Dictionary<string?, Exception> _loaderErrors = new();
        private Dictionary<string?, object> _actionData = EmptyActionData;
        private static readonly Dictionary<string?, object> EmptyActionData = new();
        private const int MaxRedirects = 5;
        // Cancellation for the currently in-flight navigation (null when idle). A newer navigation
        // that matches cancels it on its way past the match, so the prior attempt unwinds
        // (NavigationResult.Cancelled) wherever it is parked and concurrent navigations resolve to the most
        // recent one that had somewhere to go.
        private RouteCancellationSource? _activeNavigation;
        private bool _disposed;
        // Identifies whoever currently owns Status. An attempt that has lost the claim must not put Status
        // back: cancelling its token does not force it to resume at that moment, so it can reach its rollback
        // after a newer navigation has established its own Status, and by then the value it would write
        // describes a router that no longer exists.
        private int _navigationSequence;

        // Every router constructed and not yet disposed, in construction order.
        private static readonly List<Router> s_live = new();

        /// <summary>
        /// The most recently constructed router that has not been disposed; null when there is none. The
        /// routing hooks do not read it: they act on the router <c>V.RouterProvider</c> publishes above them.
        /// </summary>
        public static Router? Current => s_live.Count == 0 ? null : s_live[s_live.Count - 1];

        private RouterStatus _status = RouterStatus.Idle;

        /// <summary>Current processing state of the router.</summary>
        public RouterStatus Status
        {
            get => _status;
            private set
            {
                if (_status == value)
                {
                    return;
                }
                _status = value;
                OnStatusChanged?.Invoke(value);
            }
        }
        /// <summary>Location information for the most recently successful navigation. null before the first navigation.</summary>
        public RouterLocation? CurrentLocation { get; private set; }
        /// <summary>
        /// The location an in-flight navigation is heading for — resolved against the route tree, so it
        /// carries the destination's parameters and matches. Set once the path has matched, and cleared by
        /// the commit that makes it <see cref="CurrentLocation"/>, by the attempt that gives up on it, and
        /// by <see cref="Dispose"/>. <c>UseNavigation</c> reports it as
        /// <see cref="NavigationState.Location"/>; the routing guide states where that lands relative to
        /// React Router's <c>navigation.location</c>.
        /// </summary>
        public RouterLocation? PendingLocation { get; private set; }
        // The submission of the attempt that last published a destination, which UseNavigation reads while
        // one is in flight.
        internal Submission? PendingSubmission { get; private set; }
        /// <summary>True when the history stack can be moved backward.</summary>
        public bool CanGoBack => _historyIndex > 0;
        /// <summary>True when the history stack can be moved forward.</summary>
        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        /// <summary>Blocker manager attached to this router. Referenced from the UseBlocker hook.</summary>
        public RouteBlockerManager RouteBlockerManager => _blockerManager;
        internal int HistoryIndex => _historyIndex;
        internal IRouteScopeFactory? ScopeFactory => _scopeFactory;
        // One per router, so the setter UseSearchParams hands back keeps its identity across renders. Built
        // on first use, which leaves a router nobody reads search params from without one.
        private SearchParamsSetter? _searchParamsSetter;
        internal SearchParamsSetter SearchParamsSetter => _searchParamsSetter ??= new SearchParamsSetter(this);

        /// <summary>
        /// Raised after each successful navigation with the new location. Also re-emitted (with a fresh
        /// location identity) when a Suspend-mode loader resolves within the current location. A subscriber
        /// that throws out of that re-emit is reported to the console and the resolution stands: the loader's
        /// round settles and the route keeps what the loader produced. The same holds for the re-emit that
        /// follows a loader failing, where what it keeps is that failure.
        /// </summary>
        public event Action<RouterLocation> OnLocationChanged = null!;

        /// <summary>
        /// Raised whenever <see cref="Status"/> transitions (idle/matching/loading/etc.), letting hooks
        /// such as <c>UseNavigation</c> observe an in-flight navigation.
        /// </summary>
        public event Action<RouterStatus> OnStatusChanged = null!;

        private readonly IRouteScopeFactory? _scopeFactory;

        /// <summary>
        /// Builds a router over the given <paramref name="routes"/>.
        /// </summary>
        /// <param name="routes">Root route definitions (may contain nested <see cref="RouteDefinition.Children"/>).</param>
        /// <param name="scopeFactory">Optional factory for per-route DI scopes; null disables route scoping.</param>
        public Router(RouteDefinition[] routes, IRouteScopeFactory? scopeFactory = null)
        {
            _routeTree = new RouteTree(routes ?? throw new ArgumentNullException(nameof(routes)));
            _loaderRunner = new RouteLoaderRunner();
            _loaderRunner.OnSuspendLoaderFailed += (routeId, ex) =>
            {
                UnityEngine.Debug.LogException(ex);
                // Suspend-mode loader failed: record the error keyed by RouteId and re-emit so the nearest
                // ErrorElement renders, mirroring the synchronous Await-mode error commit.
                _loaderErrors = new Dictionary<string?, Exception>(_loaderErrors) { [routeId] = ex };
                RepublishCurrentLocation(routeId);
            };
            _loaderRunner.OnSuspendLoaderCompleted += (routeId, result) =>
            {
                // Suspend-mode loader completed: replace _loaderData with a new instance so a re-render
                // re-reads the resolved data. The location content is unchanged, so RepublishCurrentLocation
                // re-emits OnLocationChanged with a fresh identity to force that re-render.
                var updated = new Dictionary<string?, object>(_loaderData) { [routeId] = result };
                _loaderData = updated;
                RepublishCurrentLocation(routeId);
            };
            _scopeFactory = scopeFactory;
            s_live.Add(this);
        }

        /// <summary>
        /// Navigates to the given path. Evaluation order is Blocker -&gt; Guard -&gt; Loader: the Blocker is
        /// asked before the path is matched, and a Guard's redirect is not put to it.
        /// A successful Guard redirect records the final target rather than intermediate targets while
        /// preserving the originating navigation's history effect. At most four redirects are followed: a
        /// fifth is refused and the navigation ends with <see cref="NavigationResult.Error"/>.
        /// </summary>
        /// <param name="path">Target path to navigate to.</param>
        /// <param name="mode">How the destination is recorded in the history stack. Defaults to <see cref="NavigationMode.Push"/>.</param>
        /// <param name="cancellationToken">Token forwarded to Loaders.</param>
        /// <returns>
        /// A <see cref="NavigationResult"/> indicating the outcome:
        /// <see cref="NavigationResult.Success"/> on completion,
        /// <see cref="NavigationResult.NotFound"/> when no route matches,
        /// <see cref="NavigationResult.Blocked"/> when a Blocker rejects the attempt,
        /// <see cref="NavigationResult.Cancelled"/> when concurrent navigation or the cancellation token aborts it,
        /// when it is asked of a router already disposed,
        /// or when <paramref name="mode"/> is <see cref="NavigationMode.Back"/> / <see cref="NavigationMode.Forward"/>
        /// and the history has no entry to step onto,
        /// or <see cref="NavigationResult.Error"/> on redirect overflow.
        /// </returns>
        public VelvetTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode = NavigationMode.Push,
            CancellationToken cancellationToken = default) =>
            Begin(ResolvePath(path), mode, cancellationToken, submission: null);

        /// <summary>
        /// Navigates with relative resolution anchored to a specific matched-route level
        /// (<paramref name="baseRouteIndex"/>), so a <c>..</c> is interpreted relative to the route the
        /// caller is rendered in rather than the leaf route. <c>UseNavigate</c>/<c>V.Navigate</c> pass the
        /// caller's Outlet depth here so a relative target anchors at the caller's route level;
        /// <c>-1</c> falls back to the leaf route.
        /// </summary>
        public VelvetTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode,
            int baseRouteIndex,
            CancellationToken cancellationToken = default) =>
            Begin(ResolvePath(path, baseRouteIndex), mode, cancellationToken, submission: null);

        /// <summary>
        /// Submits <paramref name="formData"/> as React Router's <c>router.navigate(to, { formMethod,
        /// formData })</c> does. A <c>get</c> submission navigates to the action with the form data as its query
        /// string. Any other method calls the action of the route the target path matches — the leaf route
        /// when its path is empty and the query string holds a bare <c>index</c>, otherwise the deepest route
        /// with a path — reporting <see cref="RouterStatus.Submitting"/> while it runs, then runs every matched
        /// loader and commits with the action's result for <c>UseActionData</c>. An action that throws, or a
        /// route with no action, commits the exception as that route's error, and only the loaders above the
        /// route that renders it run.
        /// </summary>
        /// <param name="formData">What the submission sends. A <c>get</c> submission takes an
        /// <see cref="ISearchParams"/> or null.</param>
        /// <param name="options">How to submit; null takes every default. A null
        /// <see cref="SubmitOptions.Action"/> submits to the current location.</param>
        /// <param name="cancellationToken">Token forwarded to the action and the loaders.</param>
        /// <returns>The outcome, as <see cref="NavigateAsync(string, NavigationMode, CancellationToken)"/> reports
        /// it. An action's failure commits, so it reports <see cref="NavigationResult.Success"/>.</returns>
        /// <exception cref="ArgumentException">The method is none of the five, or a <c>get</c> submission's
        /// form data is not an <see cref="ISearchParams"/>.</exception>
        public VelvetTask<NavigationResult> SubmitAsync(
            object? formData,
            SubmitOptions? options = null,
            CancellationToken cancellationToken = default) =>
            SubmitAsync(formData, options, baseRouteIndex: -1, cancellationToken);

        internal VelvetTask<NavigationResult> SubmitAsync(
            object? formData,
            SubmitOptions? options,
            int baseRouteIndex,
            CancellationToken cancellationToken)
        {
            options ??= DefaultSubmitOptions;
            var method = options.Method?.ToUpperInvariant();
            if (Array.IndexOf(FormMethods, method) < 0)
            {
                throw new ArgumentException($"'{options.Method}' is not a form method.", nameof(options));
            }
            var action = options.Action == null ? FormActionFor(baseRouteIndex) : ResolvePath(options.Action, baseRouteIndex);
            var submission = new Submission(method!, action, formData, options.Replace);
            var path = action;
            if (!submission.IsMutation)
            {
                if (formData is not (null or ISearchParams))
                {
                    throw new ArgumentException("A get submission sends an ISearchParams.", nameof(formData));
                }
                path = RouteQuery.StripQuery(action) + RouteQuery.BuildQuery((ISearchParams)formData!);
            }
            // React Router replaces on a mutation submitted to the location it is on, so the entry the form was
            // on is not left under the one it produced.
            var replace = options.Replace ?? (submission.IsMutation && action == CurrentLocation?.Path);
            return Begin(path, replace ? NavigationMode.Replace : NavigationMode.Push, cancellationToken, submission);
        }

        private static readonly SubmitOptions DefaultSubmitOptions = new();
        private static readonly string[] FormMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

        // React Router's useFormAction() with no action: the route's own path and the current query string
        // without a bare index, which an index route puts back in front so the action it names is its own.
        private string FormActionFor(int baseRouteIndex)
        {
            var kept = new SearchParams();
            var current = RouteQuery.ParseQuery(CurrentLocation?.Path ?? string.Empty);
            foreach (var key in current.Keys)
            {
                foreach (var value in current.GetAll(key))
                {
                    if (key != "index" || value.Length > 0)
                    {
                        kept.Append(key, value);
                    }
                }
            }
            var query = RouteQuery.BuildQuery(kept);
            var matches = CurrentLocation?.Matches;
            if (matches != null && matches[AnchorIndex(matches, baseRouteIndex)].Route?.Path == "")
            {
                query = query.Length == 0 ? "?index" : "?index&" + query.Substring(1);
            }
            return ResolvePath(".", baseRouteIndex) + query;
        }

        // Refused here rather than inside the attempt, for the reason StepHasNoEntryToLandOn gives.
        private VelvetTask<NavigationResult> Begin(
            string? path, NavigationMode mode, CancellationToken cancellationToken, Submission? submission) =>
            StepHasNoEntryToLandOn(mode)
                ? VelvetTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(path, mode, cancellationToken, initiator: null, submission);

        // Refusing the step before the navigation starts, rather than partway through it, is what makes
        // NavigateAsync and GoBack/GoForward agree on everything the refusal skips: no in-flight attempt
        // cancelled out from under its caller, and no Status transition left to put back.
        // The discard is what carries a mode outside the enum through to the commit, whose own switch
        // answers it with ArgumentOutOfRangeException — the router's one report of such a cast, and the
        // one RouterUnfinishedNavigationTests reaches the commit's unwind through. Naming these four arms
        // would raise the cast here instead, before Status has anything to put back.
        private bool StepHasNoEntryToLandOn(NavigationMode mode) => mode switch
        {
            NavigationMode.Back => !CanGoBack,
            NavigationMode.Forward => !CanGoForward,
            _ => false,
        };

        /// <summary>
        /// Resolves a relative navigation target (<c>.</c>, <c>..</c>, <c>../sibling</c>, or a bare
        /// <c>segment</c>) against the current location, returning an absolute path. Absolute paths
        /// (starting with <c>/</c>) pass through unchanged.
        /// <para/>
        /// Relative resolution is <b>route-relative</b>:
        /// each leading <c>..</c> drops one matched-route level — and therefore that route's <i>entire</i>
        /// URL contribution, which may be several segments for a multi-segment route pattern — anchored at
        /// <paramref name="baseRouteIndex"/> (the caller's route level; <c>-1</c> = the leaf route). After
        /// the leading <c>./..</c> are consumed, the remaining target is appended segment-wise to the
        /// resolved base. When no route matches are available yet (e.g. the very first navigation), it
        /// falls back to URL-segment-relative resolution against the current path.
        /// </summary>
        internal string? ResolvePath(string path, int baseRouteIndex = -1)
        {
            if (path == null)
            {
                return null;
            }

            // Absolute paths pass through. An empty string is invalid and handled downstream by RouteTree.
            if (path.Length == 0 || path[0] == '/')
            {
                return path;
            }

            var matches = CurrentLocation?.Matches;
            if (matches == null || matches.Count == 0)
            {
                // No route context yet: fall back to URL-segment-relative resolution.
                return ResolvePathBySegments(path);
            }

            var targetParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            var cursor = AnchorIndex(matches, baseRouteIndex);

            // Consume leading "." (no-op) and ".." (pop one route level each).
            var start = 0;
            while (start < targetParts.Length && (targetParts[start] == "." || targetParts[start] == ".."))
            {
                if (targetParts[start] == "..")
                {
                    cursor--;
                }
                start++;
            }

            // The resolved base is the popped route level's cumulative pathname (or the root once we pop
            // past the top of the matched chain).
            var basePath = cursor < 0 ? "/" : matches[cursor].PathnameBase;

            var baseSegments = new List<string>(
                basePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

            // Append the remainder segment-wise (any interior "./.." in the tail still resolves URL-wise).
            return FoldSegments(baseSegments, targetParts, start);
        }

        // The caller's route level, clamped into range; -1 is the leaf.
        private static int AnchorIndex(IReadOnlyList<RouteMatch> matches, int baseRouteIndex) =>
            baseRouteIndex < 0 ? matches.Count - 1 : Math.Min(baseRouteIndex, matches.Count - 1);

        // Folds the tail segments (from start) into baseSegments — "." is a no-op, ".." pops one level (only
        // when non-empty), anything else appends — then rebuilds the absolute path ("/" when empty). The
        // core URL-folding step shared by the route-relative and URL-segment-relative resolvers; the caller
        // supplies the already-built base list since the base source differs per resolver.
        private static string FoldSegments(List<string> baseSegments, string[] tail, int start)
        {
            for (var i = start; i < tail.Length; i++)
            {
                var part = tail[i];
                if (part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (baseSegments.Count > 0)
                    {
                        baseSegments.RemoveAt(baseSegments.Count - 1);
                    }
                    continue;
                }
                baseSegments.Add(part);
            }

            return baseSegments.Count == 0 ? "/" : "/" + string.Join("/", baseSegments);
        }

        /// <summary>
        /// URL-segment-relative fallback: resolves a relative target against <see cref="CurrentLocation"/>'s
        /// raw path by dropping/appending single URL segments. Used only before any route match exists.
        /// </summary>
        private string ResolvePathBySegments(string path)
        {
            var basePath = CurrentLocation?.Path ?? "/";

            // CurrentLocation.Path retains the query string; strip it before splitting so a "?..."
            // tail does not fold into a path segment and corrupt relative resolution.
            basePath = RouteQuery.StripQuery(basePath);

            var baseSegments = new List<string>(
                basePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

            return FoldSegments(baseSegments, path.Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
        }

        private async VelvetTask<NavigationResult> NavigateInternalAsync(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            PendingNavigation? initiator,
            Submission? submission = null)
        {
            // Redirect recursion shares the initiating cancellation and claim without cancelling or
            // dispossessing the navigation it belongs to.
            RouteCancellationSource? myCancellation = null;
            CancellationToken navToken = cancellationToken;
            if (!initiator.HasValue)
            {
                // Built here so the phases run under it, but not installed here: taking over from the
                // in-flight navigation is NavigateCore's, on the far side of the match.
                myCancellation = new RouteCancellationSource(cancellationToken);
                navToken = myCancellation.Token;
            }

            NavigationResult? result = null;
            try
            {
                result = await NavigateCore(path, mode, navToken, initiator, myCancellation, submission);
                return result.Value;
            }
            catch (OperationCanceledException) when (myCancellation != null && navToken.IsCancellationRequested)
            {
                // Cancellation came from a newer navigation taking over, from the router's disposal, or from
                // the caller's own token, which `myCancellation` is linked to. Map each of them
                // to NavigationResult.Cancelled to match the loader-phase behavior in NavigateCore
                // (the early `if (cancellationToken.IsCancellationRequested) return Cancelled` check)
                // — callers branch on `nav != Success` and don't catch OCE.
                return NavigationResult.Cancelled;
            }
            finally
            {
                if (myCancellation != null)
                {
                    // Only clear the active field if this navigation is the one holding it: an attempt that
                    // matched nothing never took the field, and one a newer navigation took over from has
                    // already had the field replaced with that navigation's own.
                    if (ReferenceEquals(_activeNavigation, myCancellation)) _activeNavigation = null;
                    // Unlinked rather than cancelled: the round this navigation committed runs on under a
                    // token linked to this one.
                    myCancellation.Unlink();
                    // Settled with no navigation left under way and not before, as React Router keeps a blocker
                    // proceeding until a navigation completes: the attempt that took over from the one it
                    // released passes it too. With none left under way, nothing is left for it to proceed with.
                    // A blocked attempt settles nothing, as React Router's blocked navigation completes nothing.
                    if (_activeNavigation == null && result != NavigationResult.Blocked)
                    {
                        _blockerManager.SettleProceeding();
                    }
                }
            }
        }

        private async VelvetTask<NavigationResult> NavigateCore(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            PendingNavigation? initiator,
            RouteCancellationSource? takeover,
            Submission? submission)
        {
            if (_disposed)
            {
                return NavigationResult.Cancelled;
            }

            if (initiator?.Redirects >= MaxRedirects)
            {
                WithdrawInitiatorsDestination(initiator);
                ReportUnclaimedOutcome(RouterStatus.Error);
                return NavigationResult.Error;
            }

            if (path == null)
            {
                ReportUnclaimedOutcome(RouterStatus.NotFound);
                return NavigationResult.NotFound;
            }

            // Ahead of the match and of everything that touches the router at large, as React Router consults
            // its blocker before it starts the navigation: a blocked attempt changes nothing but the Blocker's
            // state, not even the attempt already in flight. A redirect is part of an attempt that got past it.
            if (!initiator.HasValue && Blocks(path, mode, submission))
            {
                return NavigationResult.Blocked;
            }

            // Match against the path only; the query string (?key=value) is not part of route matching but
            // is preserved on CurrentLocation.Path so UseSearchParams can read it.
            var pathForMatch = RouteQuery.StripQuery(path);
            var matches = _routeTree.Match(pathForMatch);

            if (matches == null)
            {
                WithdrawInitiatorsDestination(initiator);
                ReportUnclaimedOutcome(RouterStatus.NotFound);
                return NavigationResult.NotFound;
            }

            PendingNavigation pending;
            if (initiator.HasValue)
            {
                // A redirect inherits its initiator's claim rather than taking one, so it does not dispossess
                // the navigation it is part of, and it commits into the slot the initiator resolved.
                pending = initiator.Value;
            }
            else
            {
                // Everything past the Blocker an attempt does to the router at large happens on this side of
                // the match, and that is the point of taking the claim here: an attempt that matches no route
                // must not dispossess one still under way in a guard or a loader, because that attempt is the
                // only one able to put Status back and the only one its destination and its token belong to.
                if (takeover != null)
                {
                    // Installed before the predecessor is cancelled, as RouteLoaderRunner.BeginRound installs a
                    // round: a navigation that a cancellation callback starts inside that cancel displaces this
                    // one, and this one ends there.
                    var displaced = _activeNavigation;
                    _activeNavigation = takeover;
                    // Contained on RouteLoaderRunner.Retire's terms: a predecessor parked on an Await loader
                    // runs its round under a token linked to the one cancelled here, so that round's Loaders
                    // have their cancellation callbacks run from here.
                    try
                    {
                        displaced?.Cancel();
                    }
                    catch (Exception cancellationFailure)
                    {
                        FiberLogger.LogException(nameof(Router), cancellationFailure);
                    }
                    if (!ReferenceEquals(_activeNavigation, takeover))
                    {
                        return NavigationResult.Cancelled;
                    }
                }
                pending = new PendingNavigation(++_navigationSequence, CommitIndexFor(mode), redirects: 0);
            }

            // Built here rather than at the commit so the phases below have a destination to publish while
            // they run, and reused as the committed location so the two are one object.
            var location = BuildLocation(path, matches);
            // Written before the Status write for the reason ReleaseClaim clears it before one: that write
            // raises OnStatusChanged, and a navigation issued from inside it reaches ReportUnclaimedOutcome,
            // which reads this field to decide whether an attempt holds the claim.
            PendingLocation = location;
            PendingSubmission = submission;
            Status = submission?.IsMutation == true ? RouterStatus.Submitting : RouterStatus.Matching;

            RouteLoaderRunner.LoaderRound round;
            ActionOutcome? action = null;
            try
            {
                var guardResult = await RunGuardChecks(matches, mode, pending, cancellationToken);
                if (guardResult.HasValue)
                {
                    return guardResult.Value;
                }

                // An attempt a newer navigation took over from — one a Guard started, say — or whose caller
                // cancelled it unwinds here rather than starting a loader round: beginning one retires the
                // round current before it, which can be the round of the navigation that took over.
                if (cancellationToken.IsCancellationRequested)
                {
                    ReleaseClaim(pending, RouterStatus.Idle);
                    return NavigationResult.Cancelled;
                }

                if (submission?.IsMutation == true)
                {
                    action = await RunAction(matches, path, submission, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        ReleaseClaim(pending, RouterStatus.Idle);
                        return NavigationResult.Cancelled;
                    }
                    // React Router pushes after a failed action unless told to replace, so Back returns to the
                    // form.
                    mode = action.Error == null || submission.Replace == true ? mode : NavigationMode.Push;
                }

                var (loaderResult, loaderRound) = await RunLoaderPhase(matches, pending, cancellationToken,
                    KeptLoaders(path, matches, action), LaunchLimit(matches, action));
                if (loaderResult.HasValue)
                {
                    return loaderResult.Value;
                }
                round = loaderRound;
                // Inside the try: the commit throws on a navigation mode outside the enum, and leaving that
                // to escape past the handlers is what left Status mid-flight before.
                CommitHistoryEntry(path, mode, pending);
            }
            catch (OperationCanceledException)
            {
                // A Guard throwing OperationCanceledException, this attempt's or a redirect's, unwinds by
                // exception, skipping the in-line rollback the cancellation checks use. Status was set before
                // the Guards run, so an aborted attempt would otherwise leave UseNavigation reporting a
                // navigation that is no longer in flight.
                ReleaseClaim(pending, RouterStatus.Idle);
                throw;
            }
            catch (Exception)
            {
                // Broad because a Guard delegate's own throw lands here, as does the InvalidOperationException
                // a route declaring both RedirectTo and Guard raises. Both propagate; what must not survive
                // is this attempt's Status claim, which a newer owner would otherwise find held.
                ReleaseClaim(pending, RouterStatus.Error);
                throw;
            }

            CurrentLocation = location;
            PendingLocation = null;
            CommitAction(matches, action);
            // Only now may the round's late results reach the live state: the republish they trigger reads
            // CurrentLocation, which describes this round's location from here on. This is also where the
            // round it replaces ends — up to this line that round's loaders were streaming into the route the
            // user was still looking at.
            _loaderRunner.Promote(round);
            Status = RouterStatus.Ready;
            // Before the notification, so a handler reading a Blocker off it sees one that has started over
            // rather than one still holding the attempt this commit completed.
            _blockerManager.ResetAll();
            OnLocationChanged?.Invoke(location);

            return NavigationResult.Success;
        }

        #region Per-attempt navigation state

        // Where one navigation attempt will land, and the sequence deciding whether it still owns Status.
        // The destination stays here until the attempt commits, because the Guard and loader phases await
        // application code and a navigation starting in that window resolves its own destination from the
        // shared index: a parked Back that had already moved it puts a Push's forward truncation one entry
        // too low, taking the entry the user is looking at with it.
        private readonly struct PendingNavigation
        {
            internal readonly int Sequence;
            // The history slot this attempt commits into. Unused by a Push, which appends.
            internal readonly int CommitIndex;
            // How many Guard redirects led to this attempt.
            internal readonly int Redirects;

            internal PendingNavigation(int sequence, int commitIndex, int redirects)
            {
                Sequence = sequence;
                CommitIndex = commitIndex;
                Redirects = redirects;
            }

            internal PendingNavigation Redirected() => new(Sequence, CommitIndex, Redirects + 1);
        }

        // Same reason for the discard as StepHasNoEntryToLandOn: this runs before the commit, and the
        // commit is where a mode outside the enum is reported.
        private int CommitIndexFor(NavigationMode mode) => mode switch
        {
            NavigationMode.Back => _historyIndex - 1,
            NavigationMode.Forward => _historyIndex + 1,
            _ => _historyIndex,
        };

        private bool StillCurrent(PendingNavigation pending) =>
            pending.Sequence == _navigationSequence;

        // The two returns a redirect can reach above its own claim end the attempt its initiator published a
        // destination for, and the initiator does nothing afterwards but forward the result — so the
        // destination is withdrawn here or not at all. Status is left to the caller.
        private void WithdrawInitiatorsDestination(PendingNavigation? initiator)
        {
            if (initiator.HasValue && StillCurrent(initiator.Value))
            {
                PendingLocation = null;
            }
        }

        // Status for an attempt that ended above the claim. Having none, it may only report into a router
        // where nobody holds one: an attempt still under way in a guard or a loader is what Status describes,
        // and it is the only one able to put Status back. A published destination is what says such an
        // attempt exists — published in the same step as the claim, and cleared by whatever ends it.
        private void ReportUnclaimedOutcome(RouterStatus status)
        {
            if (PendingLocation != null)
            {
                return;
            }
            Status = status;
        }

        private void ReleaseClaim(PendingNavigation pending, RouterStatus status)
        {
            if (!StillCurrent(pending))
            {
                return;
            }

            // Before the Status write, which raises OnStatusChanged to subscribers that read the destination
            // alongside the status.
            PendingLocation = null;
            Status = status;
        }

        #endregion

        #region Guard check (after Match, before Loader)

        private async VelvetTask<NavigationResult?> RunGuardChecks(
            IReadOnlyList<RouteMatch> matches,
            NavigationMode mode,
            PendingNavigation pending,
            CancellationToken cancellationToken)
        {
            foreach (var match in matches)
            {
                if (match.Route == null) continue;

                if (match.Route.RedirectTo != null && match.Route.Guard != null)
                {
                    throw new InvalidOperationException(
                        $"RouteDefinition '{match.Route.Path}' has both RedirectTo and Guard set. These are mutually exclusive.");
                }

                string? redirectTarget = null;
                if (match.Route.RedirectTo != null)
                {
                    redirectTarget = match.Route.RedirectTo;
                }
                else if (match.Route.Guard != null)
                {
                    var loaderContext = new RouteLoaderContext
                    {
                        Params = match.Params,
                        Path = match.MatchedPath,
                    };
                    redirectTarget = match.Route.Guard(loaderContext);
                }

                if (redirectTarget != null)
                {
                    // Committing the rejected path before the target would leave a ghost entry when the target
                    // is blocked or fails, so it inherits the initiator's history slot and effect.
                    return await NavigateInternalAsync(
                        redirectTarget,
                        mode == NavigationMode.Push ? NavigationMode.Push : NavigationMode.Replace,
                        cancellationToken,
                        pending.Redirected());
                }
            }
            return null;
        }

        #endregion

        #region Blocker check

        // Split from Consult so that nothing Consult builds is built for a router with no Blocker registered.
        private bool Blocks(string path, NavigationMode mode, Submission? submission) =>
            _blockerManager.HasBlockers && Consult(path, mode, submission);

        private bool Consult(string path, NavigationMode mode, Submission? submission)
        {
            var args = new BlockerFunctionArgs
            {
                CurrentLocation = CurrentLocation,
                NextLocation = new RouterLocation { Path = path, Params = EmptyParams },
                HistoryAction = mode,
            };
            return _blockerManager.Check(args, () => ResumeAsync(path, mode, submission).Forget());
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyParams = new Dictionary<string, string>();

        // Sends the released attempt through again as the caller made it: its path, its mode, so a Back or
        // Forward goes again as the same history step, and its submission.
        private async VelvetTask ResumeAsync(string path, NavigationMode mode, Submission? submission)
        {
            var result = NavigationResult.Cancelled;
            try
            {
                result = await Begin(path, mode, default, submission);
            }
            finally
            {
                // A Back or Forward with no entry left to step onto is refused before it becomes an attempt,
                // so the settle a navigation makes on its way out is not made for it. A blocked one is
                // NavigateInternalAsync's to leave unsettled.
                if (_activeNavigation == null && result != NavigationResult.Blocked)
                {
                    _blockerManager.SettleProceeding();
                }
            }
        }

        #endregion

        #region Loading

        // Returns a null outcome on a normal completion, leaving _loaderData/_loaderErrors set for the commit
        // along with the round that produced them; returns Cancelled when the run observes cancellation.
        // A Back or Forward step decides which loaders run as a Push does: React Router keeps no loader data
        // per history entry.
        private async VelvetTask<(NavigationResult? outcome, RouteLoaderRunner.LoaderRound round)> RunLoaderPhase(
            IReadOnlyList<RouteMatch> matches,
            PendingNavigation pending,
            CancellationToken cancellationToken,
            Predicate<int>? keeps,
            int launchLimit = int.MaxValue)
        {
            Status = RouterStatus.Loading;
            // An Await-mode loader suspends here, holding the commit — and so the route on screen — until it
            // resolves. A newer navigation that matches, arriving inside that window, cancels this token,
            // which is what the check below is reading.
            var round = await _loaderRunner.RunLoadersAsync(matches, cancellationToken, keeps, launchLimit);

            if (cancellationToken.IsCancellationRequested)
            {
                // This attempt has committed nothing, so neither the live loader state nor the claim on Status
                // is its to reset: both describe wherever the user actually is, which a loader that cancelled
                // this attempt by navigating may already have moved.
                ReleaseClaim(pending, RouterStatus.Idle);
                return (NavigationResult.Cancelled, round);
            }

            // Copied rather than aliased: a Suspend loader of this round that resolves after the commit writes
            // into round.Results, and CurrentLoaderData publishes whatever this field holds as a read-only
            // snapshot.
            _loaderData = new Dictionary<string?, object>(round.Results);

            // A loader error does not abort navigation. The location commits and
            // the nearest RouteDefinition.ErrorElement renders in place of the route's Element. Errors
            // are surfaced through RouterContext.Errors (keyed by RouteId) for UseRouteError.
            _loaderErrors = new Dictionary<string?, Exception>(round.Errors);
            return (null, round);
        }

        // React Router's default shouldRevalidate (getMatchesToLoad): the same route at the same place in the
        // committed chain, over the same pathname, keeps its loader data unless an action ran, the search
        // changed or the URL did not.
        private Predicate<int>? KeptLoaders(string path, IReadOnlyList<RouteMatch> next, ActionOutcome? action)
        {
            var current = CurrentLocation;
            if (action != null || current?.Matches == null || current.Path == path
                || SearchOf(current.Path!) != SearchOf(path))
            {
                return null;
            }
            return SameRouteAndPathname(current.Matches, next);
        }

        private static Predicate<int> SameRouteAndPathname(IReadOnlyList<RouteMatch> previous, IReadOnlyList<RouteMatch> next)
            => index => index < previous.Count
                        && ReferenceEquals(previous[index].Route, next[index].Route)
                        && previous[index].PathnameBase == next[index].PathnameBase;

        private static string SearchOf(string path) => path.Substring(RouteQuery.StripQuery(path).Length);

        #endregion

        #region History management

        private static RouterLocation BuildLocation(string path, IReadOnlyList<RouteMatch> matches)
        {
            var allParams = new Dictionary<string, string>();
            foreach (var match in matches)
            {
                foreach (var kvp in match.Params)
                {
                    allParams[kvp.Key] = kvp.Value;
                }
            }

            return new RouterLocation
            {
                Path = path,
                Params = allParams,
                Matches = matches,
            };
        }

        private void CommitHistoryEntry(string path, NavigationMode mode, PendingNavigation pending)
        {
            switch (mode)
            {
                case NavigationMode.Push:
                    PushHistoryEntry(path);
                    break;
                case NavigationMode.Replace:
                    if (pending.CommitIndex >= 0)
                    {
                        _history[pending.CommitIndex] = path;
                        _historyIndex = pending.CommitIndex;
                    }
                    else
                    {
                        _history.Add(path);
                        _historyIndex = 0;
                    }
                    break;
                case NavigationMode.Back:
                case NavigationMode.Forward:
                    _historyIndex = pending.CommitIndex;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        #endregion

        /// <summary>
        /// Re-emits <see cref="OnLocationChanged"/> with a fresh <see cref="RouterLocation"/> instance
        /// carrying the same content, so a Suspend-mode loader that resolved within the current location
        /// forces a re-render. The path/params/matches are unchanged, but <c>V.RouterProvider</c>
        /// stores the location in a <c>UseState</c> whose setter bails on a referentially-equal value
        /// (Object.is). Reusing the same instance would silently drop the re-render, leaving
        /// <c>UseLoaderData</c> / <c>UseRouteError</c> on the pre-resolution snapshot. The new identity forces
        /// the re-render that re-reads the resolved data.
        /// <para/>
        /// Skips the re-emit when <paramref name="resolvedRouteId"/> is no longer part of the current
        /// location's matches: the user navigated away before the loader resolved, so the result is stale and
        /// must not churn the unrelated current location (a navigated-away loader's result is discarded).
        /// </summary>
        private void RepublishCurrentLocation(string? resolvedRouteId)
        {
            if (CurrentLocation?.Matches == null)
            {
                return;
            }

            var routeIsCurrent = false;
            foreach (var match in CurrentLocation.Matches)
            {
                if (match.RouteId == resolvedRouteId)
                {
                    routeIsCurrent = true;
                    break;
                }
            }

            if (!routeIsCurrent)
            {
                return;
            }

            CurrentLocation = new RouterLocation
            {
                Path = CurrentLocation.Path,
                Params = CurrentLocation.Params,
                Matches = CurrentLocation.Matches,
            };
            OnLocationChanged?.Invoke(CurrentLocation);
        }

        private void PushHistoryEntry(string path)
        {
            if (CanGoForward)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            _history.Add(path);
            _historyIndex = _history.Count - 1;
        }

        /// <summary>
        /// Moves one step back on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoBack"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token forwarded to Loaders.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no previous entry.</returns>
        public VelvetTask<NavigationResult> GoBack(CancellationToken cancellationToken = default)
        {
            if (!CanGoBack)
            {
                return VelvetTask.FromResult(NavigationResult.Cancelled);
            }

            return NavigateAsync(_history[_historyIndex - 1], NavigationMode.Back, cancellationToken);
        }

        /// <summary>
        /// Moves one step forward on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoForward"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token forwarded to Loaders.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no next entry.</returns>
        public VelvetTask<NavigationResult> GoForward(CancellationToken cancellationToken = default)
        {
            if (!CanGoForward)
            {
                return VelvetTask.FromResult(NavigationResult.Cancelled);
            }

            return NavigateAsync(_history[_historyIndex + 1], NavigationMode.Forward, cancellationToken);
        }

        /// <summary>
        /// Returns the loader data corresponding to the given <paramref name="routeId"/>; null when not present.
        /// </summary>
        /// <param name="routeId">The route identity used as the loader-data key (see <see cref="RouteMatch.RouteId"/>).</param>
        /// <returns>The loader result for <paramref name="routeId"/>, or <c>null</c> when no loader has produced data for it.</returns>
        public object GetLoaderData(string routeId) =>
            _loaderData.GetValueOrDefault(routeId);

        /// <summary>
        /// Snapshot of the current loader data, keyed by <see cref="RouteMatch.RouteId"/>.
        /// <c>V.RouterProvider</c> exposes this through <see cref="RouterContext.LoaderData"/> for the
        /// <c>UseLoaderData</c> hook.
        /// </summary>
        public IReadOnlyDictionary<string?, object> CurrentLoaderData => _loaderData;

        /// <summary>
        /// Snapshot of the current loader errors, keyed by <see cref="RouteMatch.RouteId"/>.
        /// <c>V.RouterProvider</c> exposes this through <see cref="RouterContext.Errors"/> for the
        /// <c>UseRouteError</c> hook and for <c>ErrorElement</c> rendering.
        /// </summary>
        public IReadOnlyDictionary<string?, Exception> CurrentLoaderErrors => _loaderErrors;

        /// <summary>
        /// The result of the action the last committed navigation called, keyed by the action's
        /// <see cref="RouteMatch.RouteId"/>; empty after any other navigation, and after an action that threw.
        /// <c>V.RouterProvider</c> exposes it through <see cref="RouterContext.ActionData"/> for
        /// <c>UseActionData</c>.
        /// </summary>
        public IReadOnlyDictionary<string?, object> CurrentActionData => _actionData;

        internal sealed class Submission
        {
            internal readonly string Method;
            internal readonly string Action;
            internal readonly object? FormData;
            internal readonly bool? Replace;

            internal Submission(string method, string action, object? formData, bool? replace)
            {
                Method = method;
                Action = action;
                FormData = formData;
                Replace = replace;
            }

            internal bool IsMutation => Method != "GET";
        }

        // React Router's getTargetMatch: the leaf index route when the query string holds a bare index,
        // otherwise the deepest route with a path. A leaf with a path is the deepest such route either way.
        private static int ActionTargetIndex(IReadOnlyList<RouteMatch> matches, string path)
        {
            var leaf = matches.Count - 1;
            foreach (var value in RouteQuery.ParseQuery(path).GetAll("index"))
            {
                if (value.Length == 0)
                {
                    return leaf;
                }
            }
            // MUTANT_SURVIVES(equivalent, boundary): the root iteration and the fallthrough below both answer 0.
            for (var index = leaf; index > 0; index--)
            {
                if (!string.IsNullOrEmpty(matches[index].Route?.Path))
                {
                    return index;
                }
            }
            return 0;
        }

        // What a submission's action produced, for the route at Target.
        private sealed class ActionOutcome
        {
            internal object? Data;
            internal Exception? Error;
            internal int Target;
        }

        // React Router revalidates every loader after an action, and after a failed one only those above the
        // route that renders the error.
        private static int LaunchLimit(IReadOnlyList<RouteMatch> matches, ActionOutcome? action) =>
            action?.Error == null ? int.MaxValue : RouteOutlet.NearestErrorBoundary(matches, action.Target);

        // The action's result replaces the action data every commit clears, and its failure is recorded as the
        // error of the route it belongs to.
        private void CommitAction(IReadOnlyList<RouteMatch> matches, ActionOutcome? action)
        {
            _actionData = EmptyActionData;
            if (action == null)
            {
                return;
            }
            var routeId = matches[action.Target].RouteId;
            if (action.Error == null)
            {
                _actionData = new Dictionary<string?, object> { [routeId] = action.Data! };
                return;
            }
            _loaderErrors = new Dictionary<string?, Exception>(_loaderErrors) { [routeId] = action.Error };
        }

        private static async VelvetTask<ActionOutcome> RunAction(
            IReadOnlyList<RouteMatch> matches, string path, Submission submission, CancellationToken cancellationToken)
        {
            var outcome = new ActionOutcome { Target = ActionTargetIndex(matches, path) };
            var match = matches[outcome.Target];
            if (match.Route?.Action == null)
            {
                outcome.Error = new InvalidOperationException(
                    $"You made a {submission.Method} request to \"{RouteQuery.StripQuery(path)}\" but did not provide an "
                    + $"action for route \"{match.RouteId}\", so there is no way to handle the request.");
                return outcome;
            }
            var context = new RouteActionContext
            {
                Params = match.Params,
                Path = match.MatchedPath,
                Method = submission.Method,
                FormData = submission.FormData,
            };
            try
            {
                outcome.Data = await match.Route.Action(context, cancellationToken);
            }
            catch (Exception failure)
            {
                outcome.Error = failure;
            }
            return outcome;
        }

        public void Dispose()
        {
            // Before the Cancel: a callback it runs can start a navigation, which NavigateCore then refuses.
            _disposed = true;
            // Retire the outstanding claim BEFORE the Cancel, which inverts the ordering a navigation uses.
            // A navigation takes its claim afterwards so that a prior attempt unwinding synchronously inside
            // the Cancel still restores its own state; here there is no such attempt worth restoring, and
            // that same synchronous unwind would write an index back and raise OnStatusChanged on a router
            // being torn down.
            _navigationSequence++;
            // Retiring the claim above is what stops the unwinding attempt from clearing this itself, and a
            // destination left published would outlive the navigation that was heading for it.
            PendingLocation = null;
            // Cancel any in-flight navigation so a pending Loader await unwinds cleanly during shutdown.
            // Contained on RouteLoaderRunner.Retire's terms: a navigation parked on an Await loader runs its
            // round under a token linked to this source, so that round's Loaders have their cancellation
            // callbacks run from here.
            try
            {
                _activeNavigation?.Cancel();
            }
            catch (Exception cancellationFailure)
            {
                FiberLogger.LogException(nameof(Router), cancellationFailure);
            }
            _activeNavigation = null;
            _loaderRunner.Dispose();
            s_live.Remove(this);
        }
    }
}
