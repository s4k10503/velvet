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

            // One source per loader rather than one per round, so a route whose result the next round keeps
            // takes its token along at the promotion and the retire of this round leaves it running.
            internal readonly Dictionary<string?, RouteCancellationSource> Loads = new();

            internal bool Retired;
        }

        private LoaderRound _currentRound;

        // The round whose Suspend loaders may announce. Null until the caller promotes one.
        private LoaderRound? _liveRound;

        private bool _disposed;

        public RouteLoaderRunner() => _currentRound = new LoaderRound();

        // Cancellation can overlap rounds, so this counts live tasks across all rounds.
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // A Loader may make a nested round current before its outer RunLoadersAsync returns.
        // Every loader delegate is invoked before the first await below, so an Await-mode chain runs
        // concurrently and a delegate that navigates still supersedes this round from inside the launch
        // loop; awaiting inside that loop would change both.
        // A match past launchLimit runs no loader, and neither does the one at it, which takes the live round's
        // result where the live round holds one, as does a match at the place it held in keptFrom.
        public async VelvetTask<LoaderRound> RunLoadersAsync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken,
            IReadOnlyList<RouteMatch>? keptFrom = null,
            int launchLimit = int.MaxValue)
        {
            var round = BeginRound();

            var awaitTasks = new List<(string? routeId, VelvetTask<object> task)>();

            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                if (match.Route?.Loader == null)
                {
                    continue;
                }

                var route = match.Route;
                var key = match.RouteId;
                if ((index == launchLimit || HeldTheSamePlace(keptFrom, matches, index)) && Keep(round, key))
                {
                    continue;
                }
                if (index >= launchLimit)
                {
                    continue;
                }

                var loaderContext = new RouteLoaderContext
                {
                    Params = match.Params,
                    Path = match.MatchedPath,
                };

                var load = new RouteCancellationSource(externalToken);
                round.Loads[key] = load;
                // Retiring the outgoing round in BeginRound runs application code, and so does a loader
                // delegate above: a round either of them starts retires this one, and the loaders still to
                // launch here belong to it all the same, so they launch cancelled.
                if (round.Retired)
                {
                    load.Cancel();
                }

                VelvetTask<object> task;
                try
                {
                    task = route.Loader(loaderContext, load.Token);
                }
                catch (Exception ex)
                {
                    round.Errors[key] = ex;
                    continue;
                }

                if (route.LoaderMode == LoaderMode.Await)
                {
                    awaitTasks.Add((key, task));
                }
                else
                {
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
                    // A cancellation is not this route's load failure. What this loop leaves in
                    // Errors is what the navigation's commit publishes for UseRouteError.
                }
                catch (Exception ex)
                {
                    round.Errors[routeId] = ex;
                }
            }

            return round;
        }

        // The outgoing round is retired here unless it is the live one: the live round belongs to the
        // location on screen, and Promote — called by the commit that leaves that location — is what ends it.
        // Anything else current has not reached a commit.
        private LoaderRound BeginRound()
        {
            var round = new LoaderRound();
            // Dispose ends the rounds it finds, and a cancellation callback it runs can begin another: one
            // begun on a disposed runner is ended here, before any of its loaders launch, and never becomes
            // current.
            if (_disposed)
            {
                Retire(round);
                return round;
            }
            var outgoing = _currentRound;
            _currentRound = round;
            if (!ReferenceEquals(outgoing, _liveRound))
            {
                Retire(outgoing);
            }
            return round;
        }

        /// <summary>
        /// Makes <paramref name="round"/> the one whose Suspend loaders announce, and ends the round that
        /// held that place. A disposed runner makes none live: it announces nothing.
        /// </summary>
        public void Promote(LoaderRound round)
        {
            if (_disposed)
            {
                return;
            }
            var departing = _liveRound;
            // Assigned before the Retire below, not after: a loader continuation that resumes synchronously
            // inside Cancel() and completes successfully would otherwise still read the departing round as
            // the live one and announce into the location that has just replaced it.
            _liveRound = round;
            if (departing != null && !ReferenceEquals(departing, round))
            {
                // A result with no load of this round's beside it is one Keep took from the departing round,
                // and the token it was produced under goes with it.
                foreach (var result in round.Results)
                {
                    if (round.Loads.ContainsKey(result.Key))
                    {
                        continue;
                    }
                    departing.Loads.Remove(result.Key, out var load);
                    if (load != null)
                    {
                        round.Loads[result.Key] = load;
                    }
                }
                Retire(departing);
            }
        }

        // React Router's default shouldRevalidate (getMatchesToLoad): the same route at the same place in the
        // committed chain, over the same pathname.
        private static bool HeldTheSamePlace(IReadOnlyList<RouteMatch>? previous, IReadOnlyList<RouteMatch> next, int index)
            => previous != null
               && index < previous.Count
               && ReferenceEquals(previous[index].Route, next[index].Route)
               && previous[index].PathnameBase == next[index].PathnameBase;

        // The live round's result for a route, taken into round in place of running its loader. An unsettled
        // or failed load has none, as React Router holds no data for a route whose load failed.
        private bool Keep(LoaderRound round, string? key)
        {
            object? result = null;
            var settled = _liveRound != null && _liveRound.Results.TryGetValue(key, out result);
            if (!settled)
            {
                return false;
            }
            round.Results[key] = result!;
            return true;
        }

        private static void Retire(LoaderRound round)
        {
            round.Retired = true;
            foreach (var load in round.Loads)
            {
                // A callback the application registered on this round's token must not decide the outcome of
                // the operation ending the round: the caller is installing a different round or disposing the
                // runner, and the callback belongs to neither. Reported on Announce's terms.
                try
                {
                    load.Value.Cancel();
                }
                catch (Exception cancellationFailure)
                {
                    FiberLogger.LogException(nameof(RouteLoaderRunner), cancellationFailure);
                }
            }
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
                    return;
                }
                catch (Exception ex)
                {
                    // A round that is not live suppresses announcements, not its own record of the failure.
                    round.Errors[routeId] = ex;
                    if (!ReferenceEquals(round, _liveRound)) return;
                    Announce(OnSuspendLoaderFailed, routeId, ex);
                    return;
                }

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
            // Both set before either Retire, on the orderings BeginRound and Promote state.
            _disposed = true;
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
