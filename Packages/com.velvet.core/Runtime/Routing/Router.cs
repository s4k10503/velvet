#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Navigation controller: matches paths against a route tree, runs guards / blockers / loaders, and
    /// maintains a history stack with Back/Forward. The active instance is exposed as <see cref="Current"/>.
    /// </summary>
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
        // Cancellation token for the currently in-flight navigation (null when idle). When a new
        // navigation arrives during an async Blocker await, we cancel the previous CTS so the prior
        // nav unwinds (NavigationResult.Cancelled) and the latest nav takes over, so concurrent
        // navigations during the blocker window resolve to the most recent one.
        private CancellationTokenSource? _activeNavigationCts;
        // Identifies whoever currently owns Status. An attempt that has lost the claim must not put Status
        // back: cancelling its token does not force it to resume at that moment, so it can reach its rollback
        // after a newer navigation has established its own Status, and by then the value it would write
        // describes a router that no longer exists.
        private int _navigationSequence;
        // The loader round whose data the current location was committed with. A round that has not reached
        // its commit has no location to write under: the live loader state and _historyIndex still describe
        // where the user is, so a loader that resolves before that commit would record its result against the
        // entry being navigated away from. The commit takes such a result from the round's results instead.
        private RouteLoaderRunner.LoaderRound? _committedRound;

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
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
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
        public VelvetTask<NavigationResult> NavigateAsync(
            string path,
            NavigationMode mode = NavigationMode.Push,
            CancellationToken cancellationToken = default) =>
            StepHasNoEntryToLandOn(mode)
                ? VelvetTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(ResolvePath(path), mode, cancellationToken, redirectCount: 0,
                    initiator: null);

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
            StepHasNoEntryToLandOn(mode)
                ? VelvetTask.FromResult(NavigationResult.Cancelled)
                : NavigateInternalAsync(ResolvePath(path, baseRouteIndex), mode, cancellationToken,
                    redirectCount: 0, initiator: null);

        // Refusing the step before the navigation starts, rather than partway through it, is what makes
        // NavigateAsync and GoBack/GoForward agree on everything the refusal skips: no in-flight attempt
        // cancelled out from under its caller, and no Status transition left to put back. It is also where
        // the Back/Forward branch of the loader phase, which indexes the history directly, gets its
        // assurance that the slot it reads existed when the attempt started.
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
            basePath = RouteQuery.StripQuery(basePath);

            var baseSegments = new List<string>(
                basePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

            return FoldSegments(baseSegments, path.Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
        }

        private async VelvetTask<NavigationResult> NavigateInternalAsync(
            string? path,
            NavigationMode mode,
            CancellationToken cancellationToken,
            int redirectCount,
            PendingNavigation? initiator)
        {
            // Concurrent-navigation handling. Recursive redirect calls (redirectCount > 0) reuse the
            // outer navigation's CTS so a redirect doesn't cancel its own initiator, and inherit its
            // claim so a redirect's rollback is judged by whether the INITIATOR still holds it.
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
                return await NavigateCore(path, mode, navToken, redirectCount, initiator);
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

        private async VelvetTask<NavigationResult> NavigateCore(
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
                // A redirect inherits its initiator's claim rather than taking one, so it does not dispossess
                // the navigation it is part of, and it commits into the slot the initiator resolved.
                pending = initiator.Value;
            }
            else
            {
                // The claim is taken after the match and not when the navigation started: every return above
                // this line leaves Status describing its own outcome, so a navigation that matches no route
                // must not dispossess an attempt parked in a blocker — that attempt is then the only one able
                // to put Status back.
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
                // Inside the try: the commit throws on a navigation mode outside the enum, and leaving that
                // to escape past the handlers is what left Status mid-flight before.
                location = CommitHistoryEntry(path, matches, mode, pending, round);
            }
            catch (OperationCanceledException)
            {
                // A Guard redirect or a Blocker that honors its token unwinds by exception, skipping the
                // in-line rollback the blocked path uses. Status was set before both of those awaits, so an
                // aborted attempt would otherwise leave UseNavigation reporting a navigation that is no
                // longer in flight.
                ReleaseClaim(pending, RouterStatus.Idle);
                throw;
            }
            catch (Exception)
            {
                // A Guard delegate is application code invoked with nothing between it and the caller, and the
                // guard phase's mutual-exclusion throw reaches here the same way. The exception is left to
                // the caller, and the router records the failure rather than the phase it died in — Matching
                // and Loading are what UseNavigation renders a pending branch for.
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

        // Where one navigation attempt will land, and the sequence deciding whether it still owns Status.
        // The destination stays here until the attempt commits, because the Guard and Blocker phases await
        // application code and a navigation starting in that window resolves its own destination from the
        // shared index: a parked Back that had already moved it puts a Push's forward truncation one entry
        // too low, taking the entry the user is looking at with it.
        private readonly struct PendingNavigation
        {
            internal readonly int Sequence;
            // The history slot this attempt commits into. Unused by a Push, which appends.
            internal readonly int CommitIndex;
            // What the caller asked for, which a redirect inherits rather than restates: a Guard rewrites
            // the path, and rewrites a Back or Forward into a Replace, so the attempt a Blocker is handed
            // no longer says which slot it belongs in. Blocker.Proceed() re-issues these two, and the
            // redirect is taken again from a navigation resolving the slot this one did.
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

        // Same reason for the discard as StepHasNoEntryToLandOn: this runs before the commit, and the
        // commit is where a mode outside the enum is reported.
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
        private async VelvetTask<NavigationResult?> RunGuardChecks(
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
        private async VelvetTask<NavigationResult?> RunBlockerCheck(
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

        private async VelvetTask ResumeAsync(PendingNavigation pending)
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
        private async VelvetTask<(NavigationResult? outcome, RouteLoaderRunner.LoaderRound round)> RunLoaderPhase(
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

        /// <summary>
        /// Moves one step back on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoBack"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no previous entry.</returns>
        public VelvetTask<NavigationResult> GoBack(CancellationToken cancellationToken = default)
        {
            if (!CanGoBack)
            {
                return VelvetTask.FromResult(NavigationResult.Cancelled);
            }

            return NavigateAsync(_history[_historyIndex - 1].Path, NavigationMode.Back, cancellationToken);
        }

        /// <summary>
        /// Moves one step forward on the history stack. Returns <see cref="NavigationResult.Cancelled"/> when <see cref="CanGoForward"/> is false.
        /// </summary>
        /// <param name="cancellationToken">Token observed by Guards, Blockers, and Loaders to abort the navigation.</param>
        /// <returns>The <see cref="NavigationResult"/> from the underlying <see cref="NavigateAsync"/>, or <see cref="NavigationResult.Cancelled"/> when the history has no next entry.</returns>
        public VelvetTask<NavigationResult> GoForward(CancellationToken cancellationToken = default)
        {
            if (!CanGoForward)
            {
                return VelvetTask.FromResult(NavigationResult.Cancelled);
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
            // Retire the outstanding claim BEFORE the Cancel, which inverts the ordering a navigation uses.
            // A navigation takes its claim afterwards so that a prior attempt unwinding synchronously inside
            // the Cancel still restores its own state; here there is no such attempt worth restoring, and
            // that same synchronous unwind would write an index back and raise OnStatusChanged on a router
            // being torn down.
            _navigationSequence++;
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
