using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Stub component passed to <see cref="RouteDefinition.Element"/> for router-only tests
    /// (Blocker / Redirect / Loader / RouteTree / Router).
    /// Used to verify path matching / loader / blocker logic; not mounted into the Outlet.
    ///
    /// Exposes factory helpers (Route / MakeMatch / Attempt / MakeToggleGuard / BuildRouter)
    /// that eliminate boilerplate envelope construction in routing test files.
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
            Func<RouteLoaderContext, CancellationToken, UniTask<object>> loader = null,
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
            Func<RouteLoaderContext, CancellationToken, UniTask<object>> loader = null,
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
