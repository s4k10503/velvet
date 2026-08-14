using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    // LoaderMode.Await loaders are awaited before the round completes, so the caller can hold the commit
    // until their data is in; LoaderMode.Suspend loaders run in the background and report through the two
    // events below.
    internal sealed class RouteLoaderRunner : IDisposable
    {
        // Notification event fired with (routeId, result) when a Suspend loader of the LIVE round succeeds.
        // A round is live between the caller promoting it and the caller promoting another, which is the
        // window in which the caller has a location for the result to be recorded under. Off it, the result
        // is still recorded on the round, and the caller reads it from there.
        public event Action<string?, object>? OnSuspendLoaderCompleted;

        // Notification event fired with (routeId, exception) when a Suspend loader of the live round fails.
        // The failure is recorded in the round's Errors either way; only the announcement is withheld from a
        // round that is not live, as on OnSuspendLoaderCompleted.
        public event Action<string?, Exception>? OnSuspendLoaderFailed;

        private int _activeSuspendTaskCount;

        // One RunLoadersAsync call's results, errors, outstanding-loader count and completion flag. They live
        // on the round rather than on the runner so that a round asked anything answers for itself: a loader
        // delegate is free to start a navigation, which begins another round from inside the one still
        // launching, and runner-wide state would be cleared and then written under it.
        internal sealed class LoaderRound
        {
            // Loader results and errors are keyed by RouteId (stable per-route identity) so sibling index
            // routes, whose MatchedPath is the empty string, do not collide.
            internal readonly Dictionary<string?, object> Results = new();

            internal readonly Dictionary<string?, Exception> Errors = new();

            internal int Pending;

            internal bool AllCompleted = true;

            // The source every loader of this round was launched under. Cancelling it is what ends the round;
            // it is nulled at that point so a round can be retired twice without disposing its source twice.
            internal CancellationTokenSource? Cts;

            internal LoaderRound(CancellationTokenSource cts) => Cts = cts;

            // False while a Suspend loader of this round has not terminated. The router records it on the
            // history entry it commits, which is what separates an unfinished round's data from the data a
            // finished one produced.
            internal bool Settled => Pending == 0;
        }

        private LoaderRound _currentRound;

        // The round whose Suspend loaders may announce. Null until the caller promotes one.
        private LoaderRound? _liveRound;

        public RouteLoaderRunner() => _currentRound = new LoaderRound(new CancellationTokenSource());

        // The round a Suspend completion belongs to: the events fire only after the live-round check, so a
        // subscriber reading this from inside one of them is reading its own round.
        internal LoaderRound? LiveRound => _liveRound;

        // Number of live Suspend loader tasks. Incremented at the start of RunSuspendLoader and
        // decremented in the finally block at completion (success / failure / cancel alike).
        // Internal accessor used for test verification.
        // When RunLoadersAsync is invoked back-to-back in quick succession, tasks from the previous
        // round that are still cancelling can temporarily coexist with tasks from the new round.
        // This counter therefore tracks "all live tasks across rounds", not "tasks of the current round".
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // The round is handed back rather than left for the caller to read off the runner afterwards, because
        // a loader that navigates has by then started a nested round of its own.
        // Every loader delegate is invoked before the first await, so the Await-mode ones run concurrently
        // rather than one after the next, and a delegate that navigates still supersedes this round from
        // inside the launch loop as it did when nothing here awaited.
        public async UniTask<LoaderRound> RunLoadersAsync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken)
        {
            var round = BeginRound(CancellationTokenSource.CreateLinkedTokenSource(externalToken));
            // A loader delegate is free to navigate, which reaches this method again and retires this round.
            // The token is therefore read once for the round rather than per loader: the source is gone by
            // the time such a round launches its remaining loaders, and they must still launch under the
            // token they belong to rather than borrow the currency of the round that superseded them.
            var roundToken = round.Cts!.Token;

            var awaitTasks = new List<(string? routeId, UniTask<object> task)>();

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

                UniTask<object> task;
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

            // A retired round's tasks are not counted off here: a loader that ignores its token keeps its
            // state machine alive, and _activeSuspendTaskCount drops in RunSuspendLoader's finally whenever
            // that eventually completes.
        }

        private async UniTask RunSuspendLoader(string? routeId, UniTask<object> task, LoaderRound round)
        {
            try
            {
                _activeSuspendTaskCount++;
                var result = await task;
                // Counted off before the live-round check and before the event, not in the finally below: a
                // round no longer live is still owed this task's departure, and a subscriber reading the
                // round from inside the callback — the router's history write-back does — must see it
                // already gone.
                round.Pending--;
                // Written into the round's results and not only announced through the event: a loader may hand
                // back a task that is already complete, and the caller assigns these results over whatever the
                // event wrote.
                round.Results[routeId] = result;
                // A round that is not live has no location of the caller's to be announced into: either it
                // has not been promoted yet, in which case the caller reads its results at the promotion, or
                // it has been replaced, in which case what it holds belongs to a location already left.
                if (!ReferenceEquals(round, _liveRound)) return;
                OnSuspendLoaderCompleted?.Invoke(routeId, result);
            }
            catch (OperationCanceledException)
            {
                round.Pending--;
            }
            catch (Exception ex)
            {
                round.Pending--;
                // Recorded ahead of the live-round guard, as the success path records its result: Pending is
                // counted off whatever the round's currency, so a round that recorded nothing for a route it
                // counted off would report itself settled holding neither a result nor a failure for it.
                round.Errors[routeId] = ex;
                if (!ReferenceEquals(round, _liveRound)) return;
                OnSuspendLoaderFailed?.Invoke(routeId, ex);
            }
            finally
            {
                _activeSuspendTaskCount--;
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
