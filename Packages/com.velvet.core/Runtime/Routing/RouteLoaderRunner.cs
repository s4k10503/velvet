using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    internal sealed class RouteLoaderRunner : IDisposable
    {
        private CancellationTokenSource? _cts;

        // Late results stay on their superseded round; only the active runner round emits announcements.
        public event Action<string?, object>? OnSuspendLoaderCompleted;

        // Failures follow the same ownership rule as OnSuspendLoaderCompleted.
        public event Action<string?, Exception>? OnSuspendLoaderFailed;

        private int _activeSuspendTaskCount;

        // Per-round state prevents a navigation started inside a Loader from overwriting the outer run.
        internal sealed class LoaderRound
        {
            // RouteId prevents an index route's empty MatchedPath from colliding with its parent.
            internal readonly Dictionary<string?, object> Results = new();

            internal readonly Dictionary<string?, Exception> Errors = new();

            internal int Pending;

            internal bool AllCompleted = true;

            internal bool Settled => Pending == 0;
        }

        private LoaderRound _currentRound = new();

        internal LoaderRound CurrentRound => _currentRound;

        // Cancellation can overlap rounds, so this counts live tasks across all rounds.
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // A Loader may make a nested round current before its outer RunLoadersSync returns.
        public LoaderRound RunLoadersSync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken)
        {
            CancelPending();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _cts = cts;
            // Capture once to keep a nested navigation's token out of the outer round's later Loaders.
            var roundToken = cts.Token;
            var round = new LoaderRound();
            _currentRound = round;

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

        private async UniTask RunSuspendLoader(string? routeId, UniTask<object> task, CancellationTokenSource ownCts,
            LoaderRound round)
        {
            try
            {
                _activeSuspendTaskCount++;
                object result;
                // Subscriber failures must not enter these Loader catches and decrement Pending twice.
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
                    // Supersession suppresses announcements, not the owning round's accounting.
                    round.Errors[routeId] = ex;
                    if (ownCts != _cts) return;
                    Announce(OnSuspendLoaderFailed, routeId, ex);
                    return;
                }

                // Subscribers must observe the completion already removed from its round's Pending count.
                round.Pending--;
                // The round owns the result even when no announcement is emitted.
                round.Results[routeId] = result;
                if (ownCts != _cts) return;
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

        public void CancelPending()
        {
            // Keep the outgoing round intact for callers still holding it.
            _currentRound = new LoaderRound();
            if (_cts != null)
            {
                // Clear before cancellation because it may synchronously complete and announce a Loader.
                var cancelling = _cts;
                _cts = null;
                cancelling.Cancel();
                cancelling.Dispose();
            }
        }

        public void Dispose() => CancelPending();
    }
}
