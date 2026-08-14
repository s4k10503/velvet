using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    internal sealed class RouteLoaderRunner : IDisposable
    {
        // Late results stay on the round that produced them; only the live round emits announcements.
        public event Action<string?, object>? OnSuspendLoaderCompleted;

        // Failures follow the same ownership rule as OnSuspendLoaderCompleted.
        public event Action<string?, Exception>? OnSuspendLoaderFailed;

        private int _activeSuspendTaskCount;

        // Per-round state prevents a navigation started inside a Loader from overwriting the outer run.
        internal sealed class LoaderRound
        {
            // Keyed on RouteId rather than MatchedPath, which is the route's own trimmed path and so is
            // shared by two levels of one chain whenever their paths trim alike — a pathless layout above
            // an index child, both "", or a segment repeated as in "users" under "users". RouteId is
            // cumulative and keeps them apart; one key would let the second loader's result win.
            internal readonly Dictionary<string?, object> Results = new();

            internal readonly Dictionary<string?, Exception> Errors = new();

            internal int Pending;

            internal bool AllCompleted = true;

            // The source every loader of this round was launched under. Cancelling it is what ends the round,
            // and it is nulled there so retiring a round twice disposes its source once.
            internal CancellationTokenSource? Cts;

            internal LoaderRound(CancellationTokenSource cts) => Cts = cts;

            internal bool Settled => Pending == 0;
        }

        private LoaderRound _currentRound;

        // The round whose Suspend loaders may announce. Null until the caller promotes one.
        private LoaderRound? _liveRound;

        public RouteLoaderRunner() => _currentRound = new LoaderRound(new CancellationTokenSource());

        // The events fire only after the live-round check, so a subscriber reading this from inside one of
        // them is reading its own round.
        internal LoaderRound? LiveRound => _liveRound;

        // Cancellation can overlap rounds, so this counts live tasks across all rounds.
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // A Loader may make a nested round current before its outer RunLoadersAsync returns.
        // Every loader delegate is invoked before the first await below, so an Await-mode chain runs
        // concurrently and a delegate that navigates still supersedes this round from inside the launch
        // loop; awaiting inside that loop would change both.
        public async VelvetTask<LoaderRound> RunLoadersAsync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken)
        {
            var round = BeginRound(CancellationTokenSource.CreateLinkedTokenSource(externalToken));
            // Captured once for the round: a nested navigation retires this one, and its remaining Loaders
            // must still launch under the token they belong to rather than the nested round's.
            var roundToken = round.Cts!.Token;

            var awaitTasks = new List<(string? routeId, VelvetTask<object> task)>();

            foreach (var match in matches)
            {
                if (match.Route?.Loader == null)
                {
                    continue;
                }

                var route = match.Route;

                var loaderContext = new RouteLoaderContext
                {
                    Params = match.Params,
                    Path = match.MatchedPath,
                };

                var key = match.RouteId;

                VelvetTask<object> task;
                try
                {
                    task = route.Loader(loaderContext, roundToken);
                }
                catch (Exception ex)
                {
                    round.Errors[key] = ex;
                    round.AllCompleted = false;
                    continue;
                }

                if (route.LoaderMode == LoaderMode.Await)
                {
                    awaitTasks.Add((key, task));
                }
                else
                {
                    round.AllCompleted = false;
                    round.Pending++;
                    RunSuspendLoader(key, task, round).Forget();
                }
            }

            foreach (var (routeId, task) in awaitTasks)
            {
                try
                {
                    round.Results[routeId] = await task;
                }
                catch (OperationCanceledException)
                {
                    round.AllCompleted = false;
                }
                catch (Exception ex)
                {
                    round.Errors[routeId] = ex;
                    round.AllCompleted = false;
                }
            }

            return round;
        }

        /// <summary>
        /// A round that ran no loaders, already settled. A Back/Forward step served from the history cache
        /// runs none and still needs a round to promote: promoting the round it is leaving instead would let
        /// that round's late results announce themselves into the entry the step restored, which shares its
        /// RouteId whenever both entries matched the same route.
        /// </summary>
        public LoaderRound EmptyRound() => BeginRound(new CancellationTokenSource());

        // The outgoing round is retired here unless it is the live one: the live round belongs to the
        // location on screen, and Promote — called by the commit that leaves that location — is what ends it.
        // Anything else current belongs to an attempt that never reached its commit.
        private LoaderRound BeginRound(CancellationTokenSource cts)
        {
            var outgoing = _currentRound;
            var round = new LoaderRound(cts);
            _currentRound = round;
            if (!ReferenceEquals(outgoing, _liveRound))
            {
                Retire(outgoing);
            }
            return round;
        }

        /// <summary>
        /// Makes <paramref name="round"/> the one whose Suspend loaders announce, and ends the round that
        /// held that place.
        /// </summary>
        public void Promote(LoaderRound round)
        {
            var departing = _liveRound;
            // Assigned before the Retire below, not after: a loader continuation that resumes synchronously
            // inside Cancel() and completes successfully would otherwise still read the departing round as
            // the live one and announce into the location that has just replaced it.
            _liveRound = round;
            if (departing != null && !ReferenceEquals(departing, round))
            {
                Retire(departing);
            }
        }

        private static void Retire(LoaderRound round)
        {
            var cts = round.Cts;
            if (cts == null)
            {
                return;
            }
            round.Cts = null;
            cts.Cancel();
            cts.Dispose();
        }

        private async VelvetTask RunSuspendLoader(string? routeId, VelvetTask<object> task, LoaderRound round)
        {
            try
            {
                _activeSuspendTaskCount++;
                object result;
                try
                {
                    result = await task;
                }
                catch (OperationCanceledException)
                {
                    round.Pending--;
                    return;
                }
                catch (Exception ex)
                {
                    round.Pending--;
                    // A round that is not live suppresses announcements, not its own accounting.
                    round.Errors[routeId] = ex;
                    if (!ReferenceEquals(round, _liveRound)) return;
                    Announce(OnSuspendLoaderFailed, routeId, ex);
                    return;
                }

                // Above the live-round return: Settled is Pending == 0, and a round that never settles
                // is one HistoryEntry.LoadersSettled keeps unservable, so Back onto that entry re-runs its
                // loaders every time.
                round.Pending--;
                round.Results[routeId] = result;
                // A round that is not live has no location of the caller's to be announced into: either it
                // has not been promoted yet, in which case the caller reads its results at the promotion, or
                // it has been replaced, in which case what it holds belongs to a location already left.
                if (!ReferenceEquals(round, _liveRound)) return;
                Announce(OnSuspendLoaderCompleted, routeId, result);
            }
            finally
            {
                _activeSuspendTaskCount--;
            }
        }

        // The runner must report subscriber failures because it forgets the Suspend task.
        private static void Announce<T>(Action<string?, T>? subscribers, string? routeId, T payload)
        {
            try
            {
                subscribers?.Invoke(routeId, payload);
            }
            catch (Exception announcementFailure)
            {
                FiberLogger.LogException(nameof(RouteLoaderRunner), announcementFailure);
            }
        }

        public void Dispose()
        {
            // Cleared before either Retire, on the ordering Promote states.
            var live = _liveRound;
            _liveRound = null;
            Retire(_currentRound);
            if (live != null)
            {
                Retire(live);
            }
        }
    }
}
