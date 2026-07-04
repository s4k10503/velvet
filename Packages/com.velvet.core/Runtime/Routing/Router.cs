#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// Navigation controller: matches paths against a route tree, runs guards / blockers / loaders, and
    /// maintains a history stack with Back/Forward. The active instance is exposed as <see cref="Current"/>.
    /// </summary>
    /// <remarks>Models the same navigation controller role as React Router for users migrating from React Router.</remarks>
    public sealed class Router : IDisposable
    {
        private readonly RouteTree _routeTree;
        private readonly RouteLoaderRunner _loaderRunner;
        private readonly List<(string path, IReadOnlyList<RouteMatch> matches, Dictionary<string?, object>? loaderData, Dictionary<string?, Exception>? loaderErrors)> _history = new();
        private readonly RouteBlockerManager _blockerManager = new();
        private int _historyIndex = -1;
        private Dictionary<string?, object> _loaderData = new();
        private Dictionary<string?, Exception> _loaderErrors = new();
        private const int MaxRedirects = 5;
        private const int MaxHistoryEntries = 50;
        // Cancellation token for the currently in-flight navigation (null when idle). When a new
        // navigation arrives during an async Blocker await, we cancel the previous CTS so the prior
        // nav unwinds (NavigationResult.Cancelled) and the latest nav takes over, so concurrent
        // navigations during the blocker window resolve to the most recent one.
        private CancellationTokenSource? _activeNavigationCts;

        /// <summary>
        /// The currently active <see cref="Router"/> instance, or null when none is mounted. Set when a
        /// router is constructed and cleared on <see cref="Dispose"/>.
        /// </summary>
        public static Router? Current { get; private set; }

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
        /// <summary>True when the history stack can be moved backward.</summary>
        public bool CanGoBack => _historyIndex > 0;
        /// <summary>True when the history stack can be moved forward.</summary>
        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        /// <summary>Blocker manager attached to this router. Referenced from the UseBlocker hook.</summary>
        public RouteBlockerManager RouteBlockerManager => _blockerManager;
        internal int HistoryIndex => _historyIndex;
        internal IRouteScopeFactory? ScopeFactory => _scopeFactory;

        /// <summary>
        /// Raised after each successful navigation with the new location. Also re-emitted (with a fresh
        /// location identity) when a Suspend-mode loader resolves within the current location.
        /// </summary>
        public event Action<RouterLocation> OnLocationChanged = null!;

        /// <summary>
        /// Raised whenever <see cref="Status"/> transitions (idle/matching/loading/etc.), letting hooks
        /// such as <c>UseNavigation</c> observe an in-flight navigation.
        /// </summary>
        public event Action<RouterStatus> OnStatusChanged = null!;

        private readonly IRouteScopeFactory? _scopeFactory;

        /// <summary>
        /// Builds a router over the given <paramref name="routes"/> and sets it as <see cref="Current"/>.
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
                SyncCurrentHistorySnapshot();
                RepublishCurrentLocation(routeId);
            };
            _loaderRunner.OnSuspendLoaderCompleted += (routeId, result) =>
            {
                // Suspend-mode loader completed: replace _loaderData with a new instance so a re-render
                // re-reads the resolved data. The location content is unchanged, so RepublishCurrentLocation
                // re-emits OnLocationChanged with a fresh identity to force that re-render.
                var updated = new Dictionary<string?, object>(_loaderData) { [routeId] = result };
                _loaderData = updated;
                SyncCurrentHistorySnapshot();
                RepublishCurrentLocation(routeId);
            };
            _scopeFactory = scopeFactory;
            if (Current != null && Current != this)
            {
                UnityEngine.Debug.LogWarning(
                    "[Router] Router.Current is being overwritten. Dispose the previous router first.");
            }

            Current = this;
        }

        /// <summary>
        /// Navigates to the given path. Evaluation order is Guard -&gt; Blocker -&gt; Loader.
        /// When a Guard returns a redirect, recursively navigates to the redirect target with
        /// NavigationMode.Replace (the original path is not pushed onto the history). Up to 5 redirects.
        /// </summary>
        /// <param name="path">Target path to navigate to.</param>
        /// <param name="mode">How the destination is recorded in the history stack. Defaults to <see cref="NavigationMode.Push"/>.</param>
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
        /// <returns>
        /// A <see cref="NavigationResult"/> indicating the outcome:
        /// <see cref="NavigationResult.Success"/> on completion,
        /// <see cref="NavigationResult.NotFound"/> when no route matches,
        /// <see cref="NavigationResult.Blocked"/> when a Blocker rejects the attempt,
        /// <see cref="NavigationResult.Cancelled"/> when concurrent navigation or the cancellation token aborts it,
        /// or <see cref="NavigationResult.Error"/> on Loader failure or redirect overflow.
        /// </returns>
        public UniTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode = NavigationMode.Push,
            CancellationToken cancellationToken = default) =>
            NavigateInternalAsync(ResolvePath(path), mode, cancellationToken, redirectCount: 0);

        /// <summary>
        /// Navigates with relative resolution anchored to a specific matched-route level
        /// (<paramref name="baseRouteIndex"/>), so a <c>..</c> is interpreted relative to the route the
        /// caller is rendered in rather than the leaf route. <c>UseNavigate</c>/<c>V.Navigate</c> pass the
        /// caller's Outlet depth here so a relative target anchors at the caller's route level;
        /// <c>-1</c> falls back to the leaf route.
        /// </summary>
        public UniTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode,
            int baseRouteIndex,
            CancellationToken cancellationToken = default) =>
            NavigateInternalAsync(ResolvePath(path, baseRouteIndex), mode, cancellationToken, redirectCount: 0);

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

            // Anchor at the caller's route level (clamped into range; -1 -> leaf).
            var cursor = baseRouteIndex < 0
                ? matches.Count - 1
                : System.Math.Min(baseRouteIndex, matches.Count - 1);

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
            var queryIndex = basePath.IndexOf('?');
            if (queryIndex >= 0)
            {
                basePath = basePath.Substring(0, queryIndex);
            }

            var baseSegments = new List<string>(
                basePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

            return FoldSegments(baseSegments, path.Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
        }

        private async UniTask<NavigationResult> NavigateInternalAsync(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            int redirectCount)
        {
            // Concurrent-navigation handling. Recursive redirect calls (redirectCount > 0) reuse the
            // outer navigation's CTS so a redirect doesn't cancel its own initiator.
            CancellationTokenSource? myCts = null;
            CancellationToken navToken = cancellationToken;
            if (redirectCount == 0)
            {
                // Cancel any in-flight navigation so it unwinds (Blocker.CheckAsync await observes
                // the cancellation). Dispose of the prior CTS is left to the prior navigation's own
                // finally — disposing here would double-dispose and confuse ownership, and the
                // synchronous Cancel chain may already run the prior finally before we proceed.
                _activeNavigationCts?.Cancel();
                myCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNavigationCts = myCts;
                navToken = myCts.Token;
            }

            try
            {
                return await NavigateCore(path, mode, navToken, redirectCount);
            }
            catch (OperationCanceledException) when (myCts != null && myCts.IsCancellationRequested)
            {
                // Cancellation came either from a newer navigation taking over OR from the caller's
                // own token (both flow through `myCts` because we linked it to the caller). Map both
                // to NavigationResult.Cancelled to match the loader-phase behavior in NavigateCore
                // (the early `if (cancellationToken.IsCancellationRequested) return Cancelled` check)
                // — callers branch on `nav != Success` and don't catch OCE.
                return NavigationResult.Cancelled;
            }
            finally
            {
                if (myCts != null)
                {
                    // Only clear the active-CTS field if we're still the active navigation; a newer
                    // navigation that took over will have already replaced the field with its own CTS.
                    if (ReferenceEquals(_activeNavigationCts, myCts)) _activeNavigationCts = null;
                    myCts.Dispose();
                }
            }
        }

        private async UniTask<NavigationResult> NavigateCore(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            int redirectCount)
        {
            if (redirectCount >= MaxRedirects)
            {
                Status = RouterStatus.Error;
                return NavigationResult.Error;
            }

            if (path == null)
            {
                Status = RouterStatus.NotFound;
                return NavigationResult.NotFound;
            }

            Status = RouterStatus.Matching;
            // Match against the path only; the query string (?key=value) is not part of route matching but
            // is preserved on CurrentLocation.Path so UseSearchParams can read it.
            var queryIndex = path.IndexOf('?');
            var pathForMatch = queryIndex < 0 ? path : path.Substring(0, queryIndex);
            var matches = _routeTree.Match(pathForMatch);

            if (matches == null)
            {
                Status = RouterStatus.NotFound;
                return NavigationResult.NotFound;
            }

            #region Provisional history index for Back/Forward
            // Apply the index provisionally before the Guard/Blocker checks.
            // Required so a Guard redirect (Replace) overwrites the correct entry.
            // Roll it back if blocked by a Blocker.
            var savedHistoryIndex = _historyIndex;
            if (mode == NavigationMode.Back)
            {
                _historyIndex--;
            }
            else if (mode == NavigationMode.Forward)
            {
                _historyIndex++;
            }

            #endregion

            #region Guard check (after Match, before Loader)
            // NOTE: design choice - Guard runs before the Blocker check.
            // Routes rejected by a Guard are not subject to Blocker evaluation.
            // This lets auth redirects bypass Blockers (such as unsaved-changes prompts), satisfying
            // the UX requirement of "do not show a leave-confirmation to unauthenticated users".
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
                    // When a redirect happens during Push, provisionally append the Push entry first.
                    // The redirect target's Replace overwrites this entry, preserving the previous
                    // (originating) history.
                    // Snapshot the FULL history (list + index) before any provisional mutation so a failed
                    // redirect restores the prior state EXACTLY. A count-based rollback is insufficient: a Push
                    // with forward history first TRUNCATES the forward entries (PushHistoryEntry → RemoveRange)
                    // and then appends, so the net count can be ≤ the pre-push count and the truncated forward
                    // entries would be lost. The snapshot captures them.
                    var historySnapshot = _history.ToArray();
                    if (mode == NavigationMode.Push)
                    {
                        PushHistoryEntry(path, matches);
                    }
                    var redirectResult = await NavigateInternalAsync(
                        redirectTarget, NavigationMode.Replace, cancellationToken, redirectCount + 1);
                    if (redirectResult != NavigationResult.Success)
                    {
                        // On redirect failure, restore the snapshot: undoes the provisional Push (incl. its forward
                        // truncation) and the provisional Back/Forward index move (savedHistoryIndex is the
                        // pre-move value).
                        _history.Clear();
                        _history.AddRange(historySnapshot);
                        _historyIndex = savedHistoryIndex;
                    }
                    return redirectResult;
                }
            }
            #endregion

            #region Blocker check
            var currentPath = CurrentLocation?.Path ?? "";
            var attempt = new NavigationAttempt { CurrentPath = currentPath, NextPath = path, NavigationMode = mode };
            // Auto-reset the previous block state. By design, a different navigation lifts the block
            // even if Proceed() was not called (intentional).
            _blockerManager.ResetAllBlocked();

            var blocked = await _blockerManager.CheckAsync(attempt, cancellationToken);
            // A superseded navigation (a newer attempt cancelled our linked token) must unwind at the blocker
            // boundary. CheckAsync forwards the token to each blocker but cannot force one to honor it — a
            // blocker that returns false (or a synchronous blocker) leaves the loop returning false, which
            // would otherwise fall through and run the loader phase or commit a cached Back/Forward entry
            // (the cached branch below never reaches the loader-phase cancellation check). Roll back the
            // provisional Back/Forward index like the Blocked path so the aborted attempt leaves no trace.
            if (cancellationToken.IsCancellationRequested)
            {
                _historyIndex = savedHistoryIndex;
                Status = RouterStatus.Idle;
                return NavigationResult.Cancelled;
            }
            if (blocked)
            {
                _historyIndex = savedHistoryIndex;
                Status = RouterStatus.Idle;
                return NavigationResult.Blocked;
            }
            #endregion

            #region Loading
            // For Back/Forward navigation, skip loading when cached loader data is already in the history.
            var cachedLoaderData = (mode == NavigationMode.Back || mode == NavigationMode.Forward)
                ? _history[_historyIndex].loaderData
                : null;

            if (cachedLoaderData != null)
            {
                _loaderData = cachedLoaderData;
                // Restore the cached errors too: a Back/Forward cache hit must re-present a route that errored
                // on its first load (UseRouteError / ErrorElement), symmetrically with loaderData.
                var cachedErrors = _history[_historyIndex].loaderErrors;
                _loaderErrors = cachedErrors != null
                    ? new Dictionary<string?, Exception>(cachedErrors)
                    : new Dictionary<string?, Exception>();
                Status = RouterStatus.Loading; // for status-transition consistency
            }
            else
            {
                Status = RouterStatus.Loading;
                // allCompleted is unused (errors are detected via the runner's Errors map).
                var (results, _) = _loaderRunner.RunLoadersSync(matches, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    _loaderData = new Dictionary<string?, object>();
                    _loaderErrors = new Dictionary<string?, Exception>();
                    Status = RouterStatus.Idle;
                    return NavigationResult.Cancelled;
                }

                _loaderData = results;

                // A loader error does not abort navigation. The location commits and
                // the nearest RouteDefinition.ErrorElement renders in place of the route's Element. Errors
                // are surfaced through RouterContext.Errors (keyed by RouteId) for UseRouteError.
                _loaderErrors = new Dictionary<string?, Exception>(_loaderRunner.Errors);
            }
            #endregion

            #region History management
            var allParams = new Dictionary<string, string>();
            foreach (var match in matches)
            {
                foreach (var kvp in match.Params)
                {
                    allParams[kvp.Key] = kvp.Value;
                }
            }

            var location = new RouterLocation
            {
                Path = path,
                Params = allParams,
                Matches = matches,
            };

            switch (mode)
            {
                case NavigationMode.Push:
                    PushHistoryEntry(path, matches, new Dictionary<string?, object>(_loaderData),
                        new Dictionary<string?, Exception>(_loaderErrors));
                    break;
                case NavigationMode.Replace:
                {
                    var loaderDataSnapshot = new Dictionary<string?, object>(_loaderData);
                    var loaderErrorsSnapshot = new Dictionary<string?, Exception>(_loaderErrors);
                    if (_historyIndex >= 0)
                    {
                        _history[_historyIndex] = (path, matches, loaderDataSnapshot, loaderErrorsSnapshot);
                    }
                    else
                    {
                        _history.Add((path, matches, loaderDataSnapshot, loaderErrorsSnapshot));
                        _historyIndex = 0;
                    }
                    break;
                }
                case NavigationMode.Back:
                case NavigationMode.Forward:
                    // The index was already provisionally applied before Guard/Blocker checks.
                    // The loader data has already been restored from the cache.
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
            #endregion

            CurrentLocation = location;
            Status = RouterStatus.Ready;
            OnLocationChanged?.Invoke(location);

            return NavigationResult.Success;
        }

        /// <summary>
        /// Re-emits <see cref="OnLocationChanged"/> with a fresh <see cref="RouterLocation"/> instance
        /// carrying the same content, so a Suspend-mode loader that resolved within the current location
        /// forces a re-render. The path/params/matches are unchanged, but the canonical router-root Provider
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

        /// <summary>
        /// Writes the live loader data/errors back into the current history entry so a later
        /// Back/Forward cache hit restores the post-resolution state. Suspend-mode loaders resolve
        /// asynchronously after the navigation commit, while the history snapshot is frozen at commit
        /// time; without this write-back the cache would replay the stale pre-resolution snapshot.
        /// </summary>
        private void SyncCurrentHistorySnapshot()
        {
            // Guard against a not-yet-committed router (no current location / no history entry).
            if (_historyIndex < 0 || _historyIndex >= _history.Count || CurrentLocation == null)
            {
                return;
            }

            var entry = _history[_historyIndex];

            // Only sync when the current entry is the location whose loaders just resolved. If the user
            // navigated away before the Suspend loader completed, _historyIndex points at a different
            // entry and the live state belongs to that other location, not this one.
            if (entry.path != CurrentLocation.Path)
            {
                return;
            }

            // A provisional redirect entry (loaderData == null) is overwritten by the redirect target's
            // Replace; do not seed it here before that commit lands.
            if (entry.loaderData == null)
            {
                return;
            }

            _history[_historyIndex] = (
                entry.path,
                entry.matches,
                new Dictionary<string?, object>(_loaderData),
                new Dictionary<string?, Exception>(_loaderErrors));
        }

        private void PushHistoryEntry(string path, IReadOnlyList<RouteMatch> matches,
            Dictionary<string?, object>? loaderData = null, Dictionary<string?, Exception>? loaderErrors = null)
        {
            if (CanGoForward)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            _history.Add((path, matches, loaderData, loaderErrors));
            _historyIndex = _history.Count - 1;

            // FIFO: drop the head entry when we exceed the cap.
            if (_history.Count > MaxHistoryEntries)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        /// <summary>
        /// Moves one step back on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoBack"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no previous entry.</returns>
        public UniTask<NavigationResult> GoBack(CancellationToken cancellationToken = default)
        {
            if (!CanGoBack)
            {
                return UniTask.FromResult(NavigationResult.Cancelled);
            }

            var (path, _, _, _) = _history[_historyIndex - 1];
            return NavigateAsync(path, NavigationMode.Back, cancellationToken);
        }

        /// <summary>
        /// Moves one step forward on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoForward"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no next entry.</returns>
        public UniTask<NavigationResult> GoForward(CancellationToken cancellationToken = default)
        {
            if (!CanGoForward)
            {
                return UniTask.FromResult(NavigationResult.Cancelled);
            }

            var (path, _, _, _) = _history[_historyIndex + 1];
            return NavigateAsync(path, NavigationMode.Forward, cancellationToken);
        }

        /// <summary>
        /// Returns the loader data corresponding to the given <paramref name="routeId"/>; null when not present.
        /// </summary>
        /// <param name="routeId">The route identity used as the loader-data key (see <see cref="RouteMatch.RouteId"/>).</param>
        /// <returns>The loader result for <paramref name="routeId"/>, or <c>null</c> when no loader has produced data for it.</returns>
        public object GetLoaderData(string routeId) =>
            _loaderData.GetValueOrDefault(routeId);

        /// <summary>
        /// Snapshot of the current loader data, keyed by <see cref="RouteMatch.RouteId"/>. The router's
        /// root Provider exposes this through <see cref="RouterContext.LoaderData"/> for the
        /// <c>UseLoaderData</c> hook.
        /// </summary>
        public IReadOnlyDictionary<string?, object> CurrentLoaderData => _loaderData;

        /// <summary>
        /// Snapshot of the current loader errors, keyed by <see cref="RouteMatch.RouteId"/>. The router's
        /// root Provider exposes this through <see cref="RouterContext.Errors"/> for the
        /// <c>UseRouteError</c> hook and for <c>ErrorElement</c> rendering.
        /// </summary>
        public IReadOnlyDictionary<string?, Exception> CurrentLoaderErrors => _loaderErrors;

        public void Dispose()
        {
            // Cancel and dispose any in-flight navigation CTS so a pending Blocker await unwinds
            // cleanly during shutdown.
            _activeNavigationCts?.Cancel();
            _activeNavigationCts?.Dispose();
            _activeNavigationCts = null;
            _loaderRunner.Dispose();
            if (Current == this)
            {
                Current = null;
            }
        }
    }
}
