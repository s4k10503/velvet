using System;
using System.Collections.Generic;
using System.Threading;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Stub component passed to <see cref="RouteDefinition.Element"/> for router-only tests
    /// (Blocker / Redirect / Loader / RouteTree / Router).
    /// Used to verify path matching / loader / blocker logic; not mounted into the Outlet.
    ///
    /// Exposes factory helpers (Route / MakeMatch / Attempt / MakeToggleGuard / MakeOneShotBlocker /
    /// MakeDeferredBlocker / BuildRouter) that eliminate boilerplate envelope construction in
    /// routing test files.
    /// </summary>
    internal static class RouteTestStubs
    {
        [Component]
        public static VNode StubA() => V.Label(text: "stub-a");

        [Component]
        public static VNode StubB() => V.Label(text: "stub-b");

        [Component]
        public static VNode StubC() => V.Label(text: "stub-c");

        /// <summary>
        /// Creates a RouteDefinition with sensible test defaults. Element defaults to <see cref="StubA"/>;
        /// pass an explicit ComponentNode only when the test asserts on which stub was matched.
        /// </summary>
        public static RouteDefinition Route(
            string path,
            ComponentNode element = null,
            string redirectTo = null,
            Func<RouteLoaderContext, string> guard = null,
            Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> loader = null,
            LoaderMode loaderMode = LoaderMode.Await,
            RouteDefinition[] children = null,
            ComponentNode errorElement = null,
            bool caseSensitive = false)
            => new RouteDefinition
            {
                Path = path,
                Element = element ?? V.Component(StubA),
                RedirectTo = redirectTo,
                Guard = guard,
                Loader = loader,
                LoaderMode = loaderMode,
                Children = children,
                ErrorElement = errorElement,
                CaseSensitive = caseSensitive,
            };

        /// <summary>
        /// Builds a single-entry RouteMatch list used by RouteLoaderRunnerTests. Params defaults to
        /// an empty dictionary and MatchedPath defaults to <paramref name="path"/>.
        /// </summary>
        public static List<RouteMatch> MakeMatch(
            string path,
            Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> loader = null,
            LoaderMode loaderMode = LoaderMode.Await)
            => new List<RouteMatch>
            {
                new RouteMatch
                {
                    Route = new RouteDefinition
                    {
                        Path = path,
                        Element = V.Component(StubA),
                        Loader = loader,
                        LoaderMode = loaderMode,
                    },
                    Params = new Dictionary<string, string>(),
                    MatchedPath = path,
                    RouteId = path,
                },
            };

        /// <summary>
        /// NavigationAttempt with sentinel paths. Tests that only care about "any attempt"
        /// can call <c>Attempt()</c>; those that assert specific paths can override.
        /// </summary>
        public static NavigationAttempt Attempt(string from = "/a", string to = "/b")
            => new NavigationAttempt { CurrentPath = from, NextPath = to };

        /// <summary>
        /// Stands in for the re-issue <c>RouteBlockerManager.CheckAsync</c> hands to a Blocker it blocks,
        /// for a manager-level test asserting on the block decision rather than on what
        /// <c>RouteBlockerState.Proceed</c> does with it.
        /// </summary>
        public static readonly Action NoResume = () => { };

        /// <summary>
        /// Creates a toggleable Guard: returns null until <paramref name="enable"/> is invoked,
        /// then returns <paramref name="redirectTo"/>.
        /// </summary>
        public static Func<RouteLoaderContext, string> MakeToggleGuard(out Action enable, string redirectTo = "/login")
        {
            var enabled = false;
            enable = () => enabled = true;
            return _ => enabled ? redirectTo : null;
        }

        /// <summary>
        /// Creates an async Blocker whose FIRST invocation parks on <c>VelvetTask.Never(ct)</c> — raising
        /// OperationCanceledException once the navigation's token is cancelled — and whose later
        /// invocations pass through without blocking. Await the returned <c>Entered</c> to be sure the
        /// navigation has reached the blocker await before cancelling it.
        /// </summary>
        /// <remarks>
        /// <c>Entered</c> is raised from inside the predicate, so a Blocker the router stops consulting
        /// never raises it and the await never returns. Three router fixtures await this signal or
        /// <see cref="MakeDeferredBlocker"/>'s, over sixteen cases between them, and carry a
        /// <c>[Timeout]</c>: left to the runner's own bound those sixteen waits cost more than a mutation
        /// campaign's default cap gives one mutant, which reports the consultation gate as not measured
        /// rather than as killed. <see cref="UnityRunnerDefaultTimeoutTests"/> pins that bound.
        /// </remarks>
        public static (Func<NavigationAttempt, CancellationToken, VelvetTask<bool>> Check, VelvetTaskCompletionSource Entered) MakeOneShotBlocker()
        {
            var entered = new VelvetTaskCompletionSource();
            int invocationCount = 0;
            VelvetTask<bool> Check(NavigationAttempt _, CancellationToken ct)
            {
                var n = Interlocked.Increment(ref invocationCount);
                if (n == 1)
                {
                    entered.TrySetResult();
                    return VelvetTask.Never<bool>(ct);
                }
                return VelvetTask.FromResult(false);
            }
            return (Check, entered);
        }

        /// <summary>
        /// Creates an async Blocker whose FIRST invocation parks on a task that ignores the navigation's
        /// token: cancelling it does not resume the blocker, which resumes only when the caller invokes
        /// <c>ResumeCancelled</c> (raising OperationCanceledException) or <c>ResumeUnblocked</c> (returning
        /// "not blocked"). That separation is the point — it puts the resume after the superseding
        /// navigation has committed, which <see cref="MakeOneShotBlocker"/> cannot do because its parked
        /// task unwinds synchronously inside <c>Cancel()</c>, before the newer navigation proceeds.
        /// The two resumes reach different rollbacks: throwing unwinds through the exception handlers,
        /// while returning falls into the blocker check's own cancellation branch.
        /// </summary>
        /// <remarks>
        /// Its <c>Entered</c> is bounded on the same terms as <see cref="MakeOneShotBlocker"/>'s.
        /// </remarks>
        public static (Func<NavigationAttempt, CancellationToken, VelvetTask<bool>> Check,
            VelvetTaskCompletionSource Entered, Action ResumeCancelled, Action ResumeUnblocked) MakeDeferredBlocker()
        {
            var entered = new VelvetTaskCompletionSource();
            var parked = new VelvetTaskCompletionSource<bool>();
            int invocationCount = 0;
            VelvetTask<bool> Check(NavigationAttempt _, CancellationToken ct)
            {
                var n = Interlocked.Increment(ref invocationCount);
                if (n == 1)
                {
                    entered.TrySetResult();
                    return parked.Task;
                }
                return VelvetTask.FromResult(false);
            }
            return (Check, entered, () => parked.TrySetCanceled(), () => parked.TrySetResult(false));
        }

        /// <summary>
        /// Builds a Router from <paramref name="routes"/> and synchronously navigates to
        /// <paramref name="startPath"/>. Use when a test starts from a known location; otherwise
        /// construct the Router directly.
        /// </summary>
        public static Router BuildRouter(string startPath, params RouteDefinition[] routes)
        {
            var router = new Router(routes);
            var result = router.NavigateSync(startPath);
            if (result != NavigationResult.Success)
            {
                router.Dispose();
                throw new InvalidOperationException(
                    $"BuildRouter: initial navigation to '{startPath}' failed with {result}. " +
                    "Check that the route table contains the start path.");
            }
            return router;
        }
    }

    /// <summary>
    /// Sync wrappers for Router's async API. Used in tests where awaiting is unergonomic
    /// (block setup, history manipulation, navigation chains).
    /// </summary>
    internal static class RouterTestExtensions
    {
        public static NavigationResult NavigateSync(this Router router, string path)
            => router.NavigateAsync(path).GetAwaiter().GetResult();

        public static NavigationResult GoBackSync(this Router router)
            => router.GoBack().GetAwaiter().GetResult();

        public static NavigationResult GoForwardSync(this Router router)
            => router.GoForward().GetAwaiter().GetResult();
    }
}
