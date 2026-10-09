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
        // Identifies whichever attempt currently owns Navigation. An attempt that has lost the claim must
        // not put Navigation back: cancelling its token does not force it to resume at that moment, so it can
        // reach its rollback after a newer navigation has published itself, and by then the value it would
        // write describes a router that no longer exists.
        private int _navigationSequence;

        // The routers constructed, oldest first, held weakly so that a router nobody disposed and nothing else
        // holds stays collectable. The newest is held strongly as well, so Current names it however the caller
        // holds it.
        private static readonly List<WeakReference<Router>> s_constructed = new();
        private static Router? s_newest;

        /// <summary>
        /// The most recently constructed router that has not been disposed. Once that one is disposed, the most
        /// recently constructed of the earlier ones still undisposed and still referenced elsewhere; null when
        /// there is none. The routing hooks do not read it: they act on the router <c>V.RouterProvider</c>
        /// publishes above them.
        /// </summary>
        public static Router? Current => s_newest ?? NewestEarlierRouter();

        private static Router? NewestEarlierRouter()
        {
            ForgetFinishedRouters();
            if (s_constructed.Count == 0)
            {
                return null;
            }
            s_constructed[s_constructed.Count - 1].TryGetTarget(out var router);
            return router;
        }

        private static void ForgetFinishedRouters()
        {
            for (var index = s_constructed.Count - 1; index >= 0; index--)
            {
                s_constructed[index].TryGetTarget(out var router);
                if (router == null || router._disposed)
                {
                    s_constructed.RemoveAt(index);
                }
            }
        }

        // Entering Play Mode without a domain reload keeps statics, and the routers of the session before are
        // not this session's.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetEveryRouter()
        {
            s_constructed.Clear();
            s_newest = null;
        }

        private NavigationLifecycle _navigationPhase;
        private RouterLocation? _pendingLocation;
        // The submission of the attempt holding the claim, or of the initiator a redirect is part of.
        private Submission? _pendingSubmission;

        /// <summary>Location information for the most recently successful navigation. null before the first navigation.</summary>
        public RouterLocation? CurrentLocation { get; private set; }

        /// <summary>
        /// The navigation in flight, as React Router's <c>router.state.navigation</c> describes it: from the
        /// moment its path has matched a route until it commits or gives up, and <c>default</c> — Idle, every
        /// other member null — while none is. It describes that attempt and nothing else: an attempt that
        /// ends without committing changes nothing <see cref="CurrentLocation"/>,
        /// <see cref="CurrentLoaderData"/> or <see cref="CurrentLoaderErrors"/> holds, and reports how it ended
        /// through the <see cref="NavigationResult"/> it returns or the exception it throws. The destination
        /// is resolved against the route tree, so it carries the destination's parameters and matches. The
        /// form members are null for a submission whose method no form takes or whose body cannot be
        /// encoded, as React Router's navigation carries no submission then.
        /// </summary>
        public NavigationState Navigation
        {
            get
            {
                if (_navigationPhase == NavigationLifecycle.Idle)
                {
                    return default;
                }
                var submission = _pendingSubmission?.Refusal == null ? _pendingSubmission : null;
                return new NavigationState
                {
                    State = _navigationPhase,
                    Location = _pendingLocation,
                    FormMethod = submission?.FormMethod,
                    FormAction = submission?.Action,
                    FormData = submission?.FormData,
                };
            }
        }
        // React Router's isRevalidationRequired: set when an action starts, cleared by a commit, so a navigation
        // that takes over from a submission whose action has started keeps no loader data.
        private bool _revalidationRequired;
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
        /// Raised after each successful navigation with the new location. Also re-emitted, with a fresh
        /// location identity, when a Suspend-mode loader resolves within the current location, and when a
        /// submission's action returns a result for a route the current location matches. A subscriber that
        /// throws out of a loader's re-emit is reported to the console and the resolution stands: the loader's
        /// round settles and the route keeps what the loader produced. The same holds for the re-emit that
        /// follows a loader failing, where what it keeps is that failure. A subscriber whose own navigation
        /// commits while the router is announcing a location is the last that location reaches: the
        /// subscribers after it hear the newer one, which that navigation announces, and not the one it left.
        /// </summary>
        public event Action<RouterLocation> OnLocationChanged
        {
            add
            {
                if (value == null)
                {
                    return;
                }
                (_locationListeners ??= new List<Action<RouterLocation>>()).Add(value);
                _locationSnapshot = null;
            }
            remove
            {
                // The last registration goes first, as removing a handler from a multicast delegate takes it.
                var index = value == null || _locationListeners == null ? -1 : _locationListeners.LastIndexOf(value);
                if (index < 0)
                {
                    return;
                }
                _locationListeners!.RemoveAt(index);
                _locationSnapshot = null;
            }
        }

        // Created at the first subscription, so a router nobody listens to allocates neither. The snapshot is
        // what an announcement walks, rebuilt only after the list changes: a subscription a subscriber adds or
        // removes mid-announcement reaches the next one, as with a multicast delegate.
        private List<Action<RouterLocation>>? _locationListeners;
        private Action<RouterLocation>[]? _locationSnapshot;

        private void AnnounceLocation(RouterLocation location)
        {
            if (_locationListeners == null)
            {
                return;
            }
            var snapshot = _locationSnapshot ??= _locationListeners.ToArray();
            for (var index = 0; index < snapshot.Length; index++)
            {
                // A navigation a subscriber started that has committed announced its own location to every
                // subscriber, so what is left of this pass would arrive after it, carrying one the router has
                // left.
                if (!ReferenceEquals(CurrentLocation, location))
                {
                    return;
                }
                snapshot[index](location);
            }
        }

        /// <summary>
        /// Raised with the new <see cref="Navigation"/> whenever it changes. <see cref="Dispose"/> clears it
        /// without raising this.
        /// </summary>
        public event Action<NavigationState> OnNavigationChanged = null!;

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
            // Both write a new dictionary rather than into the current one, which CurrentLoaderErrors and
            // CurrentLoaderData hand out as a snapshot.
            _loaderRunner.OnSuspendLoaderFailed += (routeId, ex) =>
            {
                UnityEngine.Debug.LogException(ex);
                _loaderErrors = new Dictionary<string?, Exception>(_loaderErrors) { [routeId] = ex };
                RepublishCurrentLocation(routeId);
            };
            _loaderRunner.OnSuspendLoaderCompleted += (routeId, result) =>
            {
                var updated = new Dictionary<string?, object>(_loaderData) { [routeId] = result };
                _loaderData = updated;
                RepublishCurrentLocation(routeId);
            };
            _scopeFactory = scopeFactory;
            ForgetFinishedRouters();
            s_constructed.Add(new WeakReference<Router>(this));
            s_newest = this;
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
        /// formData })</c> does; <c>routing.md</c> owns what each method does. A <c>get</c> submission navigates
        /// with the form data as its query string. Another form method calls the action of the route the target
        /// path matches, reporting <see cref="NavigationLifecycle.Submitting"/> while it runs, and commits its result
        /// for <c>UseActionData</c> or its failure as that route's error. A method no form takes commits React
        /// Router's 405 error.
        /// </summary>
        /// <param name="formData">What the submission sends. A <c>get</c> submission encodes the body as
        /// <c>routing.md</c> lists, in order, as its query string; a body that cannot be encoded commits an error in
        /// the navigation.</param>
        /// <param name="options">How to submit; null takes every default. A null
        /// <see cref="SubmitOptions.Action"/> submits to the current location.</param>
        /// <param name="cancellationToken">Token forwarded to the action and the loaders.</param>
        /// <returns>The outcome, as <see cref="NavigateAsync(string, NavigationMode, CancellationToken)"/> reports
        /// it. A failure an action or a method commits reports <see cref="NavigationResult.Success"/>.</returns>
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
            var action = SubmissionPath(string.IsNullOrEmpty(options.Action) ? null : options.Action, baseRouteIndex);
            var submission = new Submission(
                string.IsNullOrEmpty(options.Method) ? "get" : options.Method, action, formData, options.Replace);
            var path = action;
            if (submission.Method == "GET" && submission.Refusal == null)
            {
                path = RouteQuery.StripQuery(action) + RouteQuery.BuildQuery(submission.Query);
            }
            // React Router replaces on a mutation submitted to the location it is on, so the entry the form was
            // on is not left under the one it produced.
            var replace = options.Replace ?? (submission.IsMutation && action == CurrentLocation?.Path);
            return Begin(path, replace ? NavigationMode.Replace : NavigationMode.Push, cancellationToken, submission);
        }

        private static readonly SubmitOptions DefaultSubmitOptions = new();

        // React Router's normalizeTo for a submission, whose target useSubmit passes as options.action or null.
        // No target keeps the current search; no target or "." also gives the submitting route a bare index
        // when it is an index route, and takes one away when it is not.
        private string SubmissionPath(string? to, int baseRouteIndex)
        {
            var path = ResolvePath(to ?? ".", baseRouteIndex)!;
            if (to != null && to != ".")
            {
                return path;
            }
            var search = to == null ? SearchOf(CurrentLocation?.Path ?? string.Empty) : string.Empty;
            var matches = CurrentLocation?.Matches;
            var index = matches != null && IsIndexRoute(matches[AnchorIndex(matches, baseRouteIndex)].Route);
            var naked = HasBareIndex(search);
            if (index && !naked)
            {
                search = search.Length == 0 ? "?index" : "?index&" + search.Substring(1);
            }
            else if (!index && naked)
            {
                search = WithoutBareIndex(search);
            }
            return path + search;
        }

        // React Router's route.index. A route with an empty path and children is a pathless layout.
        private static bool IsIndexRoute(RouteDefinition? route)
            => route?.Path == "" && (route.Children == null || route.Children.Length == 0);

        private static bool HasBareIndex(string path)
        {
            var bare = false;
            foreach (var value in RouteQuery.ParseQuery(path).GetAll("index"))
            {
                bare |= value.Length == 0;
            }
            return bare;
        }

        private static string WithoutBareIndex(string search)
        {
            var kept = new SearchParams();
            var current = RouteQuery.ParseQuery(search);
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
            return RouteQuery.BuildQuery(kept);
        }

        // Refused here rather than inside the attempt, for the reason StepHasNoEntryToLandOn gives.
        private VelvetTask<NavigationResult> Begin(
            string? path, NavigationMode mode, CancellationToken cancellationToken, Submission? submission) =>
            StepHasNoEntryToLandOn(mode)
                ? VelvetTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(path, mode, cancellationToken, initiator: null, submission);

        // Refusing the step before the navigation starts, rather than partway through it, is what makes
        // NavigateAsync and GoBack/GoForward agree on everything the refusal skips: no in-flight attempt
        // cancelled out from under its caller, and no Navigation left to put back.
        // The discard is what carries a mode outside the enum through to the commit, whose own switch
        // answers it with ArgumentOutOfRangeException — the router's one report of such a cast, and the
        // one RouterUnfinishedNavigationTests reaches the commit's unwind through. Naming these four arms
        // would raise the cast here instead, before Navigation has anything to put back.
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
                return ResolvePathBySegments(path);
            }

            var targetParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            var cursor = AnchorIndex(matches, baseRouteIndex);

            var start = 0;
            while (start < targetParts.Length && (targetParts[start] == "." || targetParts[start] == ".."))
            {
                if (targetParts[start] == "..")
                {
                    cursor--;
                }
                start++;
            }

            var basePath = cursor < 0 ? "/" : matches[cursor].PathnameBase;

            var baseSegments = new List<string>(
                basePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

            return FoldSegments(baseSegments, targetParts, start);
        }

        // The caller's route level, clamped into range; -1 is the leaf.
        private static int AnchorIndex(IReadOnlyList<RouteMatch> matches, int baseRouteIndex) =>
            baseRouteIndex < 0 ? matches.Count - 1 : Math.Min(baseRouteIndex, matches.Count - 1);

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
                // the caller's own token, which `myCancellation` is linked to. Reported as the in-line
                // cancellation checks in NavigateCore and RunLoaderPhase report it, so a caller branches on the
                // result rather than catching.
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

            // A redirect whose initiator a newer navigation took over from — one its Guard started, say — is
            // part of an attempt that has been superseded, and ends as that attempt does, whatever its target:
            // it has no claim left to end, and nothing of its own to publish over the newer navigation's.
            if (initiator.HasValue && !StillCurrent(initiator.Value))
            {
                return NavigationResult.Cancelled;
            }

            if (initiator?.Redirects >= MaxRedirects)
            {
                EndInitiatorsNavigation(initiator);
                return NavigationResult.Error;
            }

            if (path == null)
            {
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
                EndInitiatorsNavigation(initiator);
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
                // only one able to put Navigation back and the only one its token belongs to.
                // Taken before the predecessor is cancelled, as Dispose retires the claim before its Cancel: a
                // predecessor that unwinds inside the cancel has lost the claim by then, so it does not end the
                // navigation this one is about to publish in its place.
                var sequence = ++_navigationSequence;
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
                pending = new PendingNavigation(sequence, CommitIndexFor(mode), redirects: 0);
            }

            // Built here rather than at the commit so the phases below have a destination to publish while
            // they run, and reused as the committed location so the two are one object.
            var location = BuildLocation(path, matches);
            // A redirect keeps its initiator's submission on show while it loads, as React Router's navigation
            // does while it follows a redirect out of an action.
            PublishNavigation(
                submission?.IsMutation == true ? NavigationLifecycle.Submitting : NavigationLifecycle.Loading,
                location,
                initiator.HasValue ? _pendingSubmission : submission);

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
                    ReleaseClaim(pending);
                    return NavigationResult.Cancelled;
                }

                action = await RunAction(matches, path, submission, cancellationToken);
                PublishActionData(matches, action);
                if (cancellationToken.IsCancellationRequested)
                {
                    ReleaseClaim(pending);
                    return NavigationResult.Cancelled;
                }
                mode = ModeAfterAction(mode, submission, action);

                var (loaderResult, loaderRound) = await RunLoaderPhase(matches, pending, cancellationToken,
                    KeptFrom(path), LaunchLimit(matches, action));
                if (loaderResult.HasValue)
                {
                    return loaderResult.Value;
                }
                round = loaderRound;
                // Inside the try: the commit throws on a navigation mode outside the enum, and that throw
                // escaping past the handlers would leave Navigation mid-flight.
                CommitHistoryEntry(path, mode, pending);
            }
            catch (OperationCanceledException)
            {
                // A Guard throwing OperationCanceledException, this attempt's or a redirect's, unwinds by
                // exception, skipping the in-line rollback the cancellation checks use. Navigation was
                // published before the Guards run, so an aborted attempt would otherwise leave UseNavigation
                // reporting a navigation that is no longer in flight.
                ReleaseClaim(pending);
                throw;
            }
            catch (Exception)
            {
                // Broad because a Guard delegate's own throw lands here, as does the InvalidOperationException
                // a route declaring both RedirectTo and Guard raises. Both propagate; what must not survive
                // is this attempt's claim, which a newer owner would otherwise find held.
                ReleaseClaim(pending);
                throw;
            }

            // Written once the history entry has committed, so an attempt whose commit throws leaves the loader
            // state of the location it was on.
            // Copied rather than aliased: a Suspend loader of this round that resolves after the commit writes
            // into round.Results, and CurrentLoaderData publishes whatever this field holds as a read-only
            // snapshot.
            _loaderData = new Dictionary<string?, object>(round.Results);
            _loaderErrors = new Dictionary<string?, Exception>(round.Errors);
            CurrentLocation = location;
            _revalidationRequired = false;
            CommitAction(matches, action);
            // Only now may the round's late results reach the live state: the republish they trigger reads
            // CurrentLocation, which describes this round's location from here on. This is also where the
            // round it replaces ends — up to this line that round's loaders were streaming into the route the
            // user was still looking at.
            _loaderRunner.Promote(round);
            // Before the notifications, so a handler reading a Blocker off either sees one that has started over
            // rather than one still holding the attempt this commit completed.
            _blockerManager.ResetAll();
            PublishNavigation(NavigationLifecycle.Idle, null, null);
            // A navigation a subscriber started from the Idle above announces nothing here while it is still in
            // flight, and this location is the one on show while it runs; AnnounceLocation says what happens to
            // one that has committed.
            AnnounceLocation(location);

            return NavigationResult.Success;
        }

        #region Per-attempt navigation state

        // Where one navigation attempt will land, and the sequence deciding whether it still owns Navigation.
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

        // A redirect refused for overflow or matching nothing ends the attempt its initiator published, and
        // the initiator does nothing afterwards but forward the result — so that navigation ends here or not
        // at all.
        private void EndInitiatorsNavigation(PendingNavigation? initiator)
        {
            if (initiator.HasValue)
            {
                ReleaseClaim(initiator.Value);
            }
        }

        private void ReleaseClaim(PendingNavigation pending)
        {
            if (StillCurrent(pending))
            {
                PublishNavigation(NavigationLifecycle.Idle, null, null);
            }
        }

        private void PublishNavigation(NavigationLifecycle phase, RouterLocation? location, Submission? submission)
        {
            if (_navigationPhase == phase && ReferenceEquals(_pendingLocation, location)
                && ReferenceEquals(_pendingSubmission, submission))
            {
                return;
            }
            _navigationPhase = phase;
            _pendingLocation = location;
            _pendingSubmission = submission;
            OnNavigationChanged?.Invoke(Navigation);
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
                    // matches nothing or fails, so it inherits the initiator's history slot and effect.
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

        // Returns a null outcome on a normal completion, with the round for the commit to publish; returns
        // Cancelled when the run observes cancellation.
        // A Back or Forward step decides which loaders run as a Push does: React Router keeps no loader data
        // per history entry.
        private async VelvetTask<(NavigationResult? outcome, RouteLoaderRunner.LoaderRound round)> RunLoaderPhase(
            IReadOnlyList<RouteMatch> matches,
            PendingNavigation pending,
            CancellationToken cancellationToken,
            IReadOnlyList<RouteMatch>? keptFrom,
            int launchLimit)
        {
            PublishNavigation(NavigationLifecycle.Loading, _pendingLocation, _pendingSubmission);
            // An Await-mode loader suspends here, holding the commit — and so the route on screen — until it
            // resolves. A newer navigation that matches, arriving inside that window, cancels this token,
            // which is what the check below is reading.
            var round = await _loaderRunner.RunLoadersAsync(matches, cancellationToken, keptFrom, launchLimit);

            if (cancellationToken.IsCancellationRequested)
            {
                // This attempt has committed nothing, so the live loader state is not its to reset: it describes
                // wherever the user actually is, which a loader that cancelled this attempt by navigating may
                // already have moved.
                ReleaseClaim(pending);
                return (NavigationResult.Cancelled, round);
            }

            return (null, round);
        }

        // The committed chain a navigation may keep loader data from, as React Router's default
        // shouldRevalidate (getMatchesToLoad) does; none when revalidation is required, the search changed or
        // the URL did not.
        private IReadOnlyList<RouteMatch>? KeptFrom(string path)
        {
            var current = CurrentLocation;
            if (_revalidationRequired || current?.Matches == null || current.Path == path
                || SearchOf(current.Path!) != SearchOf(path))
            {
                return null;
            }
            return current.Matches;
        }

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
        /// carrying the same content, so loader data, a loader error or action data published within the current
        /// location forces a re-render. <c>V.RouterProvider</c> stores the location in a <c>UseState</c> whose setter bails on a
        /// referentially-equal value (Object.is), so reusing the instance would drop the re-render and leave
        /// the routing hooks on the data they last read.
        /// <para/>
        /// Skips the re-emit when <paramref name="resolvedRouteId"/> is not among the current location's
        /// matches, which is where an action's result lands when its route is not the one on screen.
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
            AnnounceLocation(CurrentLocation);
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
            // Upper-case, as an action's request carries it.
            internal readonly string Method;
            // As React Router 6.28 reports navigation.formMethod: lower-case.
            internal readonly string FormMethod;
            internal readonly string Action;
            internal readonly object? FormData;
            internal readonly bool? Replace;
            // The error for a method no form takes or a get body that cannot be encoded, committed in place
            // of an action's result. It carries no status.
            internal readonly Exception? Refusal;
            // A get's body as its query, encoded only for a get.
            internal readonly List<KeyValuePair<string, string>> Query = new();

            internal Submission(string method, string action, object? formData, bool? replace)
            {
                Method = method.ToUpperInvariant();
                FormMethod = method.ToLowerInvariant();
                Action = action;
                FormData = formData;
                Replace = replace;
                if (Array.IndexOf(FormMethods, Method) < 0)
                {
                    Refusal = new InvalidOperationException($"Invalid request method \"{Method}\"");
                }
                else if (Method == "GET" && !SubmissionBody.TryEncode(formData, out Query))
                {
                    Refusal = new InvalidOperationException("Unable to encode submission body");
                }
            }

            private static readonly string[] FormMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

            // A refused submission runs no action and its refusal takes the action's place. A get that is not
            // refused runs nothing at all.
            internal bool Runs => Method != "GET" || Refusal != null;

            internal bool IsMutation => Method != "GET" && Refusal == null;
        }

        // React Router's getTargetMatch: the leaf index route when the query string holds a bare index,
        // otherwise the deepest route with a path.
        private static int ActionTargetIndex(IReadOnlyList<RouteMatch> matches, string path)
        {
            var leaf = matches.Count - 1;
            if (IsIndexRoute(matches[leaf].Route) && HasBareIndex(path))
            {
                return leaf;
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
        // route that renders the error, which keeps its own data (mergeLoaderData).
        private static int LaunchLimit(IReadOnlyList<RouteMatch> matches, ActionOutcome? action) =>
            action?.Error == null ? int.MaxValue : RouteOutlet.NearestErrorBoundary(matches, action.Target);

        // React Router pushes after a failed action unless told to replace, so Back returns to the form.
        private static NavigationMode ModeAfterAction(NavigationMode mode, Submission? submission, ActionOutcome? action)
            => action?.Error == null || submission!.Replace == true ? mode : NavigationMode.Push;

        // React Router's handleLoaders publishes an action's result with the loading navigation, before the
        // loaders settle, so a route already on screen reads it while they run.
        private void PublishActionData(IReadOnlyList<RouteMatch> matches, ActionOutcome? action)
        {
            if (action == null || action.Error != null)
            {
                return;
            }
            var routeId = matches[action.Target].RouteId;
            _actionData = new Dictionary<string?, object> { [routeId] = action.Data! };
            RepublishCurrentLocation(routeId);
        }

        // Every commit clears the action data but the one an action produced, and records a failure as the error
        // of the route it belongs to.
        private void CommitAction(IReadOnlyList<RouteMatch> matches, ActionOutcome? action)
        {
            if (action?.Error == null)
            {
                _actionData = action == null ? EmptyActionData : _actionData;
                return;
            }
            _actionData = EmptyActionData;
            _loaderErrors = new Dictionary<string?, Exception>(_loaderErrors)
            {
                [matches[action.Target].RouteId] = action.Error,
            };
        }

        // Null for a navigation with no action to run.
        private VelvetTask<ActionOutcome?> RunAction(
            IReadOnlyList<RouteMatch> matches, string path, Submission? submission, CancellationToken cancellationToken)
            => submission?.Runs == true
                ? RunActionCore(matches, path, submission, cancellationToken)
                : VelvetTask.FromResult<ActionOutcome?>(null);

        private async VelvetTask<ActionOutcome?> RunActionCore(
            IReadOnlyList<RouteMatch> matches, string path, Submission submission, CancellationToken cancellationToken)
        {
            if (submission.Refusal != null)
            {
                // React Router commits the refusal at the boundary nearest the leaf, running no action.
                return new ActionOutcome { Target = matches.Count - 1, Error = submission.Refusal };
            }
            _revalidationRequired = true;
            var outcome = new ActionOutcome { Target = ActionTargetIndex(matches, path) };
            var match = matches[outcome.Target];
            if (match.Route?.Action == null)
            {
                outcome.Error = new InvalidOperationException(
                    $"You made a {submission.Method} request to \"{RouteQuery.StripQuery(path)}\" but did not provide an "
                    + $"`action` for route \"{match.RouteId}\", so there is no way to handle the request.");
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
            // that same synchronous unwind would raise OnNavigationChanged on a router being torn down.
            _navigationSequence++;
            // Retiring the claim above is what stops the unwinding attempt from clearing this itself, and a
            // navigation left published would outlive the attempt it describes.
            _navigationPhase = NavigationLifecycle.Idle;
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
            if (ReferenceEquals(s_newest, this))
            {
                s_newest = null;
            }
        }
    }
}
