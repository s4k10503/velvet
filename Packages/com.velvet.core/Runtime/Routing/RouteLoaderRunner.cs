using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    // LoaderMode.Await loaders must complete synchronously; LoaderMode.Suspend loaders run
    // asynchronously in the background.
    internal sealed class RouteLoaderRunner : IDisposable
    {
        private CancellationTokenSource? _cts;

        // Notification event fired with (routeId, result) when a Suspend loader of the CURRENT round
        // succeeds. A loader that ignores the CancellationToken and resolves after a newer RunLoadersSync
        // (or after disposal) belongs to a superseded round; its result is recorded on that round but not
        // announced, so a navigated-away route cannot pollute the live state.
        public event Action<string?, object>? OnSuspendLoaderCompleted;

        // Notification event fired with (routeId, exception) when a Suspend loader of the current round
        // fails. The failure is recorded in the round's Errors either way; only the announcement is withheld
        // from a superseded round, as on OnSuspendLoaderCompleted.
        public event Action<string?, Exception>? OnSuspendLoaderFailed;

        private int _activeSuspendTaskCount;

        // One RunLoadersSync call's results, errors, outstanding-loader count and completion flag. They live
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

            // False while a Suspend loader of this round has not terminated. The router records it on the
            // history entry it commits, which is what separates an unfinished round's data from the data a
            // finished one produced.
            internal bool Settled => Pending == 0;
        }

        private LoaderRound _currentRound = new();

        // The round a Suspend completion belongs to: the events fire only after the supersession check, so a
        // subscriber reading this from inside one of them is reading its own round.
        internal LoaderRound CurrentRound => _currentRound;

        // Number of live Suspend loader tasks. Incremented at the start of RunSuspendLoader and
        // decremented in the finally block at completion (success / failure / cancel alike).
        // Internal accessor used for test verification.
        // When RunLoadersSync is invoked back-to-back in quick succession, tasks from the previous
        // round that are still cancelling can temporarily coexist with tasks from the new round.
        // This counter therefore tracks "all live tasks across rounds", not "tasks of the current round".
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // The round is handed back rather than read off CurrentRound afterwards, because a loader that
        // navigates has by then made a nested round current.
        public LoaderRound RunLoadersSync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken)
        {
            CancelPending();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _cts = cts;
            // A loader delegate is free to navigate, which reaches this method again and replaces _cts. The
            // source and the token are therefore read once for the round rather than per loader: a round that
            // the nested one superseded must launch its remaining loaders under its own cancelled token, not
            // lend them the currency of the round that superseded it.
            var roundToken = cts.Token;
            var round = new LoaderRound();
            _currentRound = round;

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
                    RunSuspendLoader(key, task, cts, round).Forget();
                }
            }

            foreach (var (routeId, task) in awaitTasks)
            {
                try
                {
                    if (!task.Status.IsCompleted())
                    {
                        throw new InvalidOperationException(
                            $"Await mode loader for route '{routeId}' returned an incomplete task. " +
                            "Use LoaderMode.Suspend for async loaders.");
                    }
                    var result = task.GetAwaiter().GetResult();
                    round.Results[routeId] = result;
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

        private async VelvetTask RunSuspendLoader(string? routeId, VelvetTask<object> task, CancellationTokenSource ownCts,
            LoaderRound round)
        {
            try
            {
                _activeSuspendTaskCount++;
                var result = await task;
                // Counted off before the supersession check and before the event, not in the finally below: a
                // superseded round is still owed this task's departure, and a subscriber reading the round
                // from inside the callback — the router's history write-back does — must see it already gone.
                round.Pending--;
                // Written into the round's results and not only announced through the event: a loader may hand
                // back a task that is already complete, and the caller assigns these results over whatever the
                // event wrote.
                round.Results[routeId] = result;
                // A loader that ignored its token can resolve after CancelPending replaced (or nulled) _cts.
                // That makes this a superseded round: its own record above stands, and what must not happen
                // is announcing it into the live state of an unrelated current location.
                if (ownCts != _cts) return;
                OnSuspendLoaderCompleted?.Invoke(routeId, result);
            }
            catch (OperationCanceledException)
            {
                round.Pending--;
            }
            catch (Exception ex)
            {
                round.Pending--;
                // Recorded ahead of the supersession guard, as the success path records its result: Pending is
                // counted off whatever the round's currency, so a round that recorded nothing for a route it
                // counted off would report itself settled holding neither a result nor a failure for it.
                round.Errors[routeId] = ex;
                if (ownCts != _cts) return;
                OnSuspendLoaderFailed?.Invoke(routeId, ex);
            }
            finally
            {
                _activeSuspendTaskCount--;
            }
        }

        // This also runs automatically at the start of the next RunLoadersSync call.
        public void CancelPending()
        {
            // A fresh round rather than a reset of the outgoing one: whoever holds the outgoing round is
            // asking whether it finished, and it did not.
            _currentRound = new LoaderRound();
            if (_cts != null)
            {
                // Cleared before the Cancel, not after: a loader continuation that resumes synchronously
                // inside Cancel() and completes successfully would otherwise still read _cts as its own
                // and fire its completion event for a round that is being torn down.
                var cancelling = _cts;
                _cts = null;
                cancelling.Cancel();
                cancelling.Dispose();
            }

            // _activeSuspendTaskCount is decremented in the finally block of RunSuspendLoader.
            // If the loader honors the CancellationToken, the awaited task ends with
            // OperationCanceledException and the counter naturally returns to 0.
            // If the loader ignores the ct, the async state machine remains alive and the counter
            // does not drop until it eventually completes (see the ActiveSuspendTaskCount comment).
        }

        public void Dispose() => CancelPending();
    }
}
