#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>Owns a route tree and the history produced by its navigations.</summary>
    public sealed class Router : IDisposable
    {
        private readonly RouteTree _routeTree;
        private readonly RouteLoaderRunner _loaderRunner;
        private readonly List<HistoryEntry> _history = new();
        private readonly RouteBlockerManager _blockerManager = new();
        private int _historyIndex = -1;
        private Dictionary<string?, object> _loaderData = new();
        private Dictionary<string?, Exception> _loaderErrors = new();
        private const int MaxRedirects = 5;
        private const int MaxHistoryEntries = 50;
        // A new top-level navigation cancels the prior source; its attempt disposes it during unwind.
        private CancellationTokenSource? _activeNavigationCts;
        // A superseded attempt must not overwrite the Status owned by a newer navigation.
        private int _navigationSequence;
        // The loader round whose data the current location was committed with. A round that has not reached
        // its commit has no location to write under: the live loader state and _historyIndex still describe
        // where the user is, so a loader that resolves before that commit would record its result against the
        // entry being navigated away from. The commit takes such a result from the round's results instead.
        private RouteLoaderRunner.LoaderRound? _committedRound;

        /// <summary>Construction installs the active instance; disposing the active instance clears it.</summary>
        public static Router? Current { get; private set; }

        private RouterStatus _status = RouterStatus.Idle;

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
        public bool CanGoBack => _historyIndex > 0;
        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        /// <summary>Blocker manager attached to this router. Referenced from the UseBlocker hook.</summary>
        public RouteBlockerManager RouteBlockerManager => _blockerManager;
        internal int HistoryIndex => _historyIndex;
        internal IRouteScopeFactory? ScopeFactory => _scopeFactory;

        /// <summary>
        /// Raised after each successful navigation with the new location. Also re-emitted (with a fresh
        /// location identity) when a Suspend-mode loader resolves within the current location. A subscriber
        /// that throws out of that re-emit is reported to the console and the resolution stands: the loader's
        /// round settles and the route keeps what the loader produced. The same holds for the re-emit that
        /// follows a loader failing, where what it keeps is that failure.
        /// </summary>
        public event Action<RouterLocation> OnLocationChanged = null!;

        /// <summary>Raised only when <see cref="Status"/> changes value.</summary>
        public event Action<RouterStatus> OnStatusChanged = null!;

        private readonly IRouteScopeFactory? _scopeFactory;

        /// <param name="scopeFactory">Optional factory for per-route DI scopes; null disables route scoping.</param>
        public Router(RouteDefinition[] routes, IRouteScopeFactory? scopeFactory = null)
        {
            _routeTree = new RouteTree(routes ?? throw new ArgumentNullException(nameof(routes)));
            _loaderRunner = new RouteLoaderRunner();
            _loaderRunner.OnSuspendLoaderFailed += (routeId, ex) =>
            {
                UnityEngine.Debug.LogException(ex);
                if (!ResolvedIntoTheCommittedRound()) return;
                // Suspend-mode loader failed: record the error keyed by RouteId and re-emit so the nearest
                // ErrorElement renders, mirroring the synchronous Await-mode error commit.
                _loaderErrors = new Dictionary<string?, Exception>(_loaderErrors) { [routeId] = ex };
                SyncCurrentHistorySnapshot();
                RepublishCurrentLocation(routeId);
            };
            _loaderRunner.OnSuspendLoaderCompleted += (routeId, result) =>
            {
                if (!ResolvedIntoTheCommittedRound()) return;
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
        /// When a Guard returns a redirect, recursively navigates to the redirect target and records only
        /// that target, with this navigation's own history effect: a Push appends it where the originating
        /// path would have gone, and a Replace or a Back/Forward step overwrites the entry at the slot this
        /// navigation resolved. Up to 5 redirects.
        /// </summary>
        /// <param name="path">Target path to navigate to.</param>
        /// <param name="mode">How the destination is recorded in the history stack. Defaults to <see cref="NavigationMode.Push"/>.</param>
        /// <param name="cancellationToken">Token forwarded to Blockers and Loaders.</param>
        /// <returns>
        /// A <see cref="NavigationResult"/> indicating the outcome:
        /// <see cref="NavigationResult.Success"/> on completion,
        /// <see cref="NavigationResult.NotFound"/> when no route matches,
        /// <see cref="NavigationResult.Blocked"/> when a Blocker rejects the attempt,
        /// <see cref="NavigationResult.Cancelled"/> when concurrent navigation or the cancellation token aborts it,
        /// or when <paramref name="mode"/> is <see cref="NavigationMode.Back"/> / <see cref="NavigationMode.Forward"/>
        /// and the history has no entry to step onto,
        /// or <see cref="NavigationResult.Error"/> on Loader failure or redirect overflow.
        /// </returns>
        public UniTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode = NavigationMode.Push,
            CancellationToken cancellationToken = default) =>
            StepHasNoEntryToLandOn(mode)
                ? UniTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(ResolvePath(path), mode, cancellationToken, redirectCount: 0,
                    initiator: null);

        /// <summary>
        /// Anchors relative resolution at <paramref name="baseRouteIndex"/>; when the current location has
        /// matches, a negative or oversized index selects its leaf.
        /// </summary>
        public UniTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode,
            int baseRouteIndex,
            CancellationToken cancellationToken = default) =>
            StepHasNoEntryToLandOn(mode)
                ? UniTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(ResolvePath(path, baseRouteIndex), mode, cancellationToken,
                    redirectCount: 0, initiator: null);

        // Refuse a missing history slot before cancelling another attempt or changing Status.
        // Invalid modes that reach CommitHistoryEntry use its shared exception and Status unwind.
        private bool StepHasNoEntryToLandOn(NavigationMode mode) => mode switch
        {
            NavigationMode.Back => !CanGoBack,
            NavigationMode.Forward => !CanGoForward,
            _ => false,
        };

        /// <summary>
        /// Leading <c>..</c> segments remove whole matched-route contributions from the selected route level;
        /// without a current match chain, resolution falls back to URL segments. Absolute paths pass through
        /// unchanged.
        /// </summary>
        internal string? ResolvePath(string path, int baseRouteIndex = -1)
        {
            if (path == null)
            {
                return null;
            }

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

            var cursor = baseRouteIndex < 0
                ? matches.Count - 1
                : System.Math.Min(baseRouteIndex, matches.Count - 1);

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

        private async UniTask<NavigationResult> NavigateInternalAsync(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            int redirectCount,
            PendingNavigation? initiator)
        {
            // Redirect recursion shares its initiator's cancellation source and Status claim.
            CancellationTokenSource? myCts = null;
            CancellationToken navToken = cancellationToken;
            if (redirectCount == 0)
            {
                // The prior attempt owns disposal of its source, including synchronous cancellation unwind.
                _activeNavigationCts?.Cancel();
                myCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNavigationCts = myCts;
                navToken = myCts.Token;
            }

            try
            {
                return await NavigateCore(path, mode, navToken, redirectCount, initiator);
            }
            catch (OperationCanceledException) when (myCts != null && myCts.IsCancellationRequested)
            {
                // Linked caller cancellation and supersession share the public Cancelled outcome.
                return NavigationResult.Cancelled;
            }
            finally
            {
                if (myCts != null)
                {
                    // Do not clear the source installed by a newer navigation.
                    if (ReferenceEquals(_activeNavigationCts, myCts)) _activeNavigationCts = null;
                    myCts.Dispose();
                }
            }
        }

        private async UniTask<NavigationResult> NavigateCore(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            int redirectCount,
            PendingNavigation? initiator)
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
            var pathForMatch = RouteQuery.StripQuery(path);
            var matches = _routeTree.Match(pathForMatch);

            if (matches == null)
            {
                Status = RouterStatus.NotFound;
                return NavigationResult.NotFound;
            }

            PendingNavigation pending;
            if (initiator.HasValue)
            {
                // Redirects retain their initiator's Status claim and resolved history slot.
                pending = initiator.Value;
            }
            else
            {
                // A matching failure takes no claim, preserving a parked attempt's ownership.
                pending = new PendingNavigation(++_navigationSequence, CommitIndexFor(mode), path, mode);
            }

            RouterLocation location;
            RouteLoaderRunner.LoaderRound round;
            try
            {
                var guardResult = await RunGuardChecks(matches, mode, pending, cancellationToken, redirectCount);
                if (guardResult.HasValue)
                {
                    return guardResult.Value;
                }

                var blockerResult = await RunBlockerCheck(path, mode, pending, cancellationToken);
                if (blockerResult.HasValue)
                {
                    return blockerResult.Value;
                }

                var (loaderResult, loaderRound) = await RunLoaderPhase(matches, mode, pending, cancellationToken);
                if (loaderResult.HasValue)
                {
                    return loaderResult.Value;
                }
                round = loaderRound;
                // Commit failures share the same Status-claim unwind as earlier phases.
                location = CommitHistoryEntry(path, matches, mode, pending, round);
            }
            catch (OperationCanceledException)
            {
                // Release the abandoned attempt's Status claim before unwinding.
                ReleaseClaim(pending, RouterStatus.Idle);
                throw;
            }
            catch (Exception)
            {
                // Release this attempt's Status claim before propagating; a newer owner remains untouched.
                ReleaseClaim(pending, RouterStatus.Error);
                throw;
            }

            CurrentLocation = location;
            // Only now may the round's late results reach the live state: the write-back they trigger reads
            // CurrentLocation and _historyIndex, and both describe this round's location from here on.
            _committedRound = round;
            Status = RouterStatus.Ready;
            // Settled before the notification, so a handler reading a Blocker off it sees one that has
            // finished proceeding rather than one still holding the attempt this commit completed.
            _blockerManager.SettleProceeding();
            OnLocationChanged?.Invoke(location);

            return NavigationResult.Success;
        }

        #region Per-attempt navigation state

        // Capture the history slot and Status ownership before awaited phases let shared router state move.
        private readonly struct PendingNavigation
        {
            internal readonly int Sequence;
            internal readonly int CommitIndex;
            // Preserve the caller's request because a rewritten path and mode no longer identify its step.
            internal readonly string OriginPath;
            internal readonly NavigationMode OriginMode;

            internal PendingNavigation(int sequence, int commitIndex, string originPath, NavigationMode originMode)
            {
                Sequence = sequence;
                CommitIndex = commitIndex;
                OriginPath = originPath;
                OriginMode = originMode;
            }
        }

        // Same invalid-mode ownership as StepHasNoEntryToLandOn.
        private int CommitIndexFor(NavigationMode mode) => mode switch
        {
            NavigationMode.Back => _historyIndex - 1,
            NavigationMode.Forward => _historyIndex + 1,
            _ => _historyIndex,
        };

        // Whether the Suspend loader now reporting belongs to the round the current location was committed
        // with. The runner fires its events only for the round it holds as current, so this compares that
        // round against the committed one.
        private bool ResolvedIntoTheCommittedRound() =>
            ReferenceEquals(_loaderRunner.CurrentRound, _committedRound);

        private bool StillCurrent(PendingNavigation pending) =>
            pending.Sequence == _navigationSequence;

        private void ReleaseClaim(PendingNavigation pending, RouterStatus status)
        {
            if (!StillCurrent(pending))
            {
                return;
            }

            Status = status;
        }

        #endregion

        #region Guard check (after Match, before Loader)

        // Guard runs before the Blocker check, so an attempt naming a path a Guard rejected is not put
        // to a Blocker. That is not a way past Blockers: the redirect goes out through
        // NavigateInternalAsync, which puts its target to them on the same terms as any other
        // navigation. The navigation-blocking guide says what that leaves a dirty form doing.
        // Returns null when no match redirected, so the caller falls through to the Blocker check.
        private async UniTask<NavigationResult?> RunGuardChecks(
            IReadOnlyList<RouteMatch> matches,
            NavigationMode mode,
            PendingNavigation pending,
            CancellationToken cancellationToken,
            int redirectCount)
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
                    // The redirect target is the only entry the pair records, and it records it with the
                    // originating navigation's own history effect: a Push appends the target where the
                    // originating path would have gone, and a Back/Forward replaces the entry at the slot
                    // that navigation resolved. The rejected alternative was to append the originating path
                    // up front for the target's Replace to overwrite — a redirect abandoned in a Blocker
                    // then leaves an entry for a path the user never reached, and it cannot be taken back
                    // once a newer navigation has built on the stack that entry sits in.
                    return await NavigateInternalAsync(
                        redirectTarget,
                        mode == NavigationMode.Push ? NavigationMode.Push : NavigationMode.Replace,
                        cancellationToken,
                        redirectCount + 1,
                        pending);
                }
            }
            return null;
        }

        #endregion

        #region Blocker check

        // Returns null when the attempt is neither cancelled nor blocked, so the caller falls through
        // to the Loader phase.
        private async UniTask<NavigationResult?> RunBlockerCheck(
            string path,
            NavigationMode mode,
            PendingNavigation pending,
            CancellationToken cancellationToken)
        {
            var currentPath = CurrentLocation?.Path ?? "";
            var attempt = new NavigationAttempt { CurrentPath = currentPath, NextPath = path, NavigationMode = mode };
            // Unconditional by design: an attempt reaching here lifts a standing block whether or not
            // anything answered its dialog.
            _blockerManager.ResetAllBlocked();

            var blocked = await _blockerManager.CheckAsync(attempt, () => Resume(pending), cancellationToken);
            // A superseded navigation (a newer attempt cancelled our linked token) must unwind at the blocker
            // boundary. CheckAsync forwards the token to each blocker but cannot force one to honor it — a
            // blocker that returns false (or a synchronous blocker) leaves the loop returning false, which
            // would otherwise fall through and commit a location the router has already navigated past. The
            // loader phase's own cancellation check cannot stand in for this one: a Back/Forward cache hit
            // commits without ever reaching it. Both exits go through ReleaseClaim, since a blocker that
            // awaits without forwarding the token returns here rather than throwing, and can do so after a
            // newer navigation has established its Status.
            if (cancellationToken.IsCancellationRequested)
            {
                ReleaseClaim(pending, RouterStatus.Idle);
                return NavigationResult.Cancelled;
            }
            if (blocked)
            {
                ReleaseClaim(pending, RouterStatus.Idle);
                return NavigationResult.Blocked;
            }
            return null;
        }

        private void Resume(PendingNavigation pending) => ResumeAsync(pending).Forget();

        private async UniTask ResumeAsync(PendingNavigation pending)
        {
            try
            {
                await NavigateAsync(pending.OriginPath, pending.OriginMode);
            }
            finally
            {
                // An attempt that reaches no commit leaves the Blockers that released it holding a
                // navigation that is over.
                _blockerManager.SettleProceeding();
            }
        }

        #endregion

        #region Loading

        // Returns a null outcome on a normal completion (cached or fresh), leaving _loaderData/_loaderErrors
        // set for CommitHistoryEntry along with the round that produced them; returns Cancelled only when a
        // fresh (non-cached) loader run observes cancellation.
        private async UniTask<(NavigationResult? outcome, RouteLoaderRunner.LoaderRound round)> RunLoaderPhase(
            IReadOnlyList<RouteMatch> matches,
            NavigationMode mode,
            PendingNavigation pending,
            CancellationToken cancellationToken)
        {
            // Only a settled entry may be served; see HistoryEntry.LoadersSettled for what an unsettled one
            // holds.
            var restoring = (mode == NavigationMode.Back || mode == NavigationMode.Forward)
                && _history[pending.CommitIndex].LoadersSettled;

            if (restoring)
            {
                // The else branch cancels the previous round by reaching RunLoadersSync; this one commits
                // without ever reaching it. Leaving that round's CTS installed keeps it current for
                // RouteLoaderRunner's supersession guard, so the round being navigated away from would write
                // its late result into the entry restored here. Nothing downstream separates the two: RouteId
                // is built from the route pattern, which both entries share whenever they match the same one.
                _loaderRunner.CancelPending();
                var entry = _history[pending.CommitIndex];
                _loaderData = entry.LoaderData;
                // Restore the cached errors too: a Back/Forward cache hit must re-present a route that errored
                // on its first load (UseRouteError / ErrorElement), symmetrically with the loader data.
                _loaderErrors = new Dictionary<string?, Exception>(entry.LoaderErrors);
                Status = RouterStatus.Loading; // for status-transition consistency
                // A restored entry ran no loaders of its own, and Back/Forward rewrites no entry, so the round
                // handed back is only the empty one CancelPending installed.
                return (null, _loaderRunner.CurrentRound);
            }

            Status = RouterStatus.Loading;
            var round = _loaderRunner.RunLoadersSync(matches, cancellationToken);

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

        #endregion

        #region History management

        private readonly struct HistoryEntry
        {
            internal readonly string Path;
            internal readonly IReadOnlyList<RouteMatch> Matches;
            internal readonly Dictionary<string?, object> LoaderData;
            internal readonly Dictionary<string?, Exception> LoaderErrors;
            // An entry committed while a Suspend loader is still running holds whatever its loader round had
            // produced by then, and nothing in the contents distinguishes that from what the route's loaders
            // would finally have returned — so the Back/Forward cache reads this flag rather than the data.
            internal readonly bool LoadersSettled;

            internal HistoryEntry(string path, IReadOnlyList<RouteMatch> matches,
                Dictionary<string?, object> loaderData, Dictionary<string?, Exception> loaderErrors,
                bool loadersSettled)
            {
                Path = path;
                Matches = matches;
                LoaderData = loaderData;
                LoaderErrors = loaderErrors;
                LoadersSettled = loadersSettled;
            }
        }

        private HistoryEntry NewEntry(string path, IReadOnlyList<RouteMatch> matches,
            RouteLoaderRunner.LoaderRound round) =>
            new(path, matches,
                new Dictionary<string?, object>(_loaderData),
                new Dictionary<string?, Exception>(_loaderErrors),
                round.Settled);

        private RouterLocation CommitHistoryEntry(string path, IReadOnlyList<RouteMatch> matches, NavigationMode mode,
            PendingNavigation pending, RouteLoaderRunner.LoaderRound round)
        {
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
                    PushHistoryEntry(NewEntry(path, matches, round));
                    break;
                case NavigationMode.Replace:
                    if (pending.CommitIndex >= 0)
                    {
                        _history[pending.CommitIndex] = NewEntry(path, matches, round);
                        _historyIndex = pending.CommitIndex;
                    }
                    else
                    {
                        _history.Add(NewEntry(path, matches, round));
                        _historyIndex = 0;
                    }
                    break;
                case NavigationMode.Back:
                case NavigationMode.Forward:
                    _historyIndex = pending.CommitIndex;
                    // A round still running has nothing worth recording, and the write-back it triggers on
                    // settling is what makes the entry servable. A round already settled by here has no such
                    // write-back coming: it settled before this navigation had a location for its results to
                    // be written under, which is where _committedRound refused them. Without this the entry
                    // would stay unservable and re-run its loaders on every step onto it.
                    if (round.Settled)
                    {
                        _history[_historyIndex] = NewEntry(path, matches, round);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            return location;
        }

        #endregion

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
        /// time; without this write-back the entry would keep the pre-resolution snapshot.
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
            if (entry.Path != CurrentLocation.Path)
            {
                return;
            }

            _history[_historyIndex] = NewEntry(entry.Path, entry.Matches, _loaderRunner.CurrentRound);
        }

        private void PushHistoryEntry(HistoryEntry entry)
        {
            if (CanGoForward)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            _history.Add(entry);
            _historyIndex = _history.Count - 1;

            // Evicting the head entry when the cap is exceeded shifts every remaining index down by
            // one, so _historyIndex must decrement too to keep pointing at the same logical entry.
            if (_history.Count > MaxHistoryEntries)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        /// <summary>Returns Cancelled without starting an attempt when no previous entry exists.</summary>
        public UniTask<NavigationResult> GoBack(CancellationToken cancellationToken = default)
        {
            if (!CanGoBack)
            {
                return UniTask.FromResult(NavigationResult.Cancelled);
            }

            return NavigateAsync(_history[_historyIndex - 1].Path, NavigationMode.Back, cancellationToken);
        }

        /// <summary>Returns Cancelled without starting an attempt when no next entry exists.</summary>
        public UniTask<NavigationResult> GoForward(CancellationToken cancellationToken = default)
        {
            if (!CanGoForward)
            {
                return UniTask.FromResult(NavigationResult.Cancelled);
            }

            return NavigateAsync(_history[_historyIndex + 1].Path, NavigationMode.Forward, cancellationToken);
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
            // Retire the claim before cancellation so synchronous unwind leaves Status untouched during disposal.
            _navigationSequence++;
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
