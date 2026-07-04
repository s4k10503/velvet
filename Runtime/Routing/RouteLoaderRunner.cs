using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    // Runner that manages execution of route loaders.
    // LoaderMode.Await loaders must complete synchronously, while
    // LoaderMode.Suspend loaders run asynchronously in the background.
    internal sealed class RouteLoaderRunner : IDisposable
    {
        private CancellationTokenSource _cts;

        // Per-route errors (keyed by RouteId) raised by the most recent RunLoadersSync call.
        public IReadOnlyDictionary<string, Exception> Errors => _errors;
        private readonly Dictionary<string, Exception> _errors = new();

        // Notification event fired with (routeId, result) when a Suspend loader of the CURRENT round
        // succeeds. A loader that ignores the CancellationToken and resolves after a newer RunLoadersSync
        // (or after disposal) belongs to a superseded round; its result is stale and is dropped without
        // firing this event or touching Errors, so a navigated-away route cannot pollute the live state.
        public event Action<string, object> OnSuspendLoaderCompleted;

        // Notification event fired with (routeId, exception) when a Suspend loader of the current round
        // fails. The failure is also recorded in Errors. A superseded round's late failure is dropped
        // (no event, no Errors write) for the same reason as OnSuspendLoaderCompleted.
        public event Action<string, Exception> OnSuspendLoaderFailed;

        private int _activeSuspendTaskCount;

        // Number of live Suspend loader tasks. Incremented at the start of RunSuspendLoader and
        // decremented in the finally block at completion (success / failure / cancel alike).
        // Internal accessor used for test verification.
        // When RunLoadersSync is invoked back-to-back in quick succession, tasks from the previous
        // round that are still cancelling can temporarily coexist with tasks from the new round.
        // This counter therefore tracks "all live tasks across rounds", not "tasks of the current round".
        internal int ActiveSuspendTaskCount => _activeSuspendTaskCount;

        // Runs loaders for the given matches. Await loaders must complete synchronously; Suspend loaders
        // are started in the background. On error, the error is recorded per-route in Errors; the caller
        // is expected to inspect Errors.
        public (Dictionary<string, object> results, bool allCompleted) RunLoadersSync(
            IReadOnlyList<RouteMatch> matches,
            CancellationToken externalToken)
        {
            CancelPending();
            _errors.Clear();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            var results = new Dictionary<string, object>();
            var awaitTasks = new List<(string routeId, UniTask<object> task)>();
            var allCompleted = true;

            foreach (var match in matches)
            {
                if (match.Route.Loader == null)
                {
                    continue;
                }

                var loaderContext = new RouteLoaderContext
                {
                    Params = match.Params,
                    Path = match.MatchedPath,
                };

                // Loader results / errors are keyed by RouteId (stable per-route identity) so sibling index
                // routes (whose MatchedPath is the empty string) do not collide.
                var key = match.RouteId;

                UniTask<object> task;
                try
                {
                    task = match.Route.Loader(loaderContext, _cts.Token);
                }
                catch (Exception ex)
                {
                    _errors[key] = ex;
                    allCompleted = false;
                    continue;
                }

                if (match.Route.LoaderMode == LoaderMode.Await)
                {
                    awaitTasks.Add((key, task));
                }
                else
                {
                    allCompleted = false;
                    RunSuspendLoader(key, task, _cts).Forget();
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
                    results[routeId] = result;
                }
                catch (OperationCanceledException)
                {
                    allCompleted = false;
                }
                catch (Exception ex)
                {
                    _errors[routeId] = ex;
                    allCompleted = false;
                }
            }

            return (results, allCompleted);
        }

        private async UniTask RunSuspendLoader(string routeId, UniTask<object> task, CancellationTokenSource ownCts)
        {
            try
            {
                _activeSuspendTaskCount++;
                var result = await task;
                // A loader that ignored its token can resolve after CancelPending replaced (or nulled) _cts.
                // That makes this a superseded round; drop the stale result rather than firing into the live
                // state of an unrelated current location.
                if (ownCts != _cts) return;
                OnSuspendLoaderCompleted?.Invoke(routeId, result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Same supersession guard as the success path: a stale round's failure must not record an
                // error nor re-emit under the current location.
                if (ownCts != _cts) return;
                _errors[routeId] = ex;
                OnSuspendLoaderFailed?.Invoke(routeId, ex);
            }
            finally
            {
                _activeSuspendTaskCount--;
            }
        }

        // Cancels the loaders that are currently in progress. Also runs automatically on the next
        // RunLoadersSync call.
        public void CancelPending()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            // _activeSuspendTaskCount is decremented in the finally block of RunSuspendLoader.
            // If the loader honors the CancellationToken, the awaited task ends with
            // OperationCanceledException and the counter naturally returns to 0.
            // If the loader ignores the ct, the async state machine remains alive and the counter
            // does not drop (see the OnSuspendLoaderCompleted XmlDoc).
        }

        public void Dispose() => CancelPending();
    }
}
