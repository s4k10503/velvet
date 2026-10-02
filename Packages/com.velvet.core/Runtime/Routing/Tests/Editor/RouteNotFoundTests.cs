// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // A path no route matches, as React Router's handleNavigational404 commits it. No case here names the
    // 404's error type, so a tree without that type still compiles this file; RouteErrorResponseTests holds
    // the cases that name it.
    [TestFixture]
    internal sealed class RouteNotFoundTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        [Component]
        private static VNode Caught() => V.Label(text: "caught");

        private static RouteDefinition[] RootWithHome() => new[]
        {
            Route("/", children: new[] { Route("home") }),
        };

        private static string ErrorOfTheMatchedRoute(Router router)
            => router.CurrentLoaderErrors.TryGetValue(router.CurrentLocation!.Matches![0].RouteId, out var error)
                ? error.Message
                : "none";

        private static string Committed(Router router)
            => $"{router.CurrentLocation!.Path} {router.CurrentLocation.Matches!.Count} "
               + (router.CurrentLocation.Matches[0].Route?.Path ?? "null");

        #region What commits

        [Test]
        public void Given_AnUnmatchedPath_When_Navigating_Then_ThePathCommitsWithTheRootRouteAlone()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(Committed(router), Is.EqualTo("/nowhere 1 /"));
        }

        [Test]
        public void Given_AnUnmatchedPath_When_Navigating_Then_TheMatchedRoutesErrorNamesThePath()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(ErrorOfTheMatchedRoute(router), Is.EqualTo("No route matches URL \"/nowhere\""));
        }

        [Test]
        public void Given_AnUnmatchedPathWithAQuery_When_Navigating_Then_TheErrorNamesThePathWithoutTheQuery()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());

            // Act
            router.NavigateSync("/nowhere?tab=2");

            // Assert
            Assert.That(ErrorOfTheMatchedRoute(router), Is.EqualTo("No route matches URL \"/nowhere\""));
        }

        [Test]
        public void Given_ATableOfOneRoute_When_NavigatingToAnUnmatchedPath_Then_ThatRouteTakesTheError()
        {
            // Arrange
            var router = BuildRouter("/about", Route("about"));

            // Act
            router.NavigateSync("/x");

            // Assert
            Assert.That(Committed(router), Is.EqualTo("/x 1 about"));
        }

        [Test]
        public void Given_SeveralTopLevelRoutesOneOfThemTheRoot_When_NavigatingToAnUnmatchedPath_Then_TheRootTakesTheError()
        {
            // Arrange
            var router = BuildRouter("/a", Route("a"), Route("/", element: V.Component(StubB)));

            // Act
            router.NavigateSync("/x");

            // Assert
            Assert.That(Committed(router), Is.EqualTo("/x 1 /"));
        }

        [Test]
        public void Given_SeveralTopLevelRoutesOneOfThemPathless_When_NavigatingToAnUnmatchedPath_Then_ThePathlessRouteTakesTheError()
        {
            // Arrange
            var router = BuildRouter("/a", Route("a"), Route("", children: new[] { Route("b") }));

            // Act
            router.NavigateSync("/x");

            // Assert
            Assert.That(Committed(router), Is.EqualTo("/x 1 "));
        }

        [Test]
        public void Given_SeveralTopLevelRoutesNoneOfThemTheRoot_When_NavigatingToAnUnmatchedPath_Then_ARouteOfNoElementTakesTheError()
        {
            // Arrange
            var router = BuildRouter("/a", Route("a"), Route("b"));

            // Act
            router.NavigateSync("/x");

            // Assert
            Assert.That(
                (Committed(router), router.CurrentLocation!.Matches![0].Route?.Element == null),
                Is.EqualTo(("/x 1 null", true)));
        }

        [Test]
        public void Given_ACancelledToken_When_NavigatingToAnUnmatchedPath_Then_NothingCommits()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            // Act
            var result = router.NavigateAsync("/nowhere", NavigationMode.Push, cancelled.Token)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(
                (result, router.CurrentLocation!.Path, router.Status),
                Is.EqualTo((NavigationResult.Cancelled, "/home", RouterStatus.Idle)));
        }

        #endregion

        #region What the commit keeps

        [Test]
        public void Given_ARootAndAChildWithSettledData_When_NavigatingToAnUnmatchedPath_Then_OnlyTheRootKeepsItsData()
        {
            // Arrange
            var router = BuildRouter("/a",
                Route("/", loader: (_, _) => VelvetTask.FromResult<object>("root"), children: new[]
                {
                    Route("a", loader: (_, _) => VelvetTask.FromResult<object>("a")),
                }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(string.Join(",", router.CurrentLoaderData.Values), Is.EqualTo("root"));
        }

        [Test]
        public void Given_ASuspendLoaderStillRunning_When_AnUnmatchedPathCommits_Then_ItsTokenIsCancelled()
        {
            // Arrange
            var token = CancellationToken.None;
            var router = BuildRouter("/feed",
                Route("/", children: new[]
                {
                    Route("feed", loaderMode: LoaderMode.Suspend, loader: (_, ct) =>
                    {
                        token = ct;
                        return new VelvetTaskCompletionSource<object>().Task;
                    }),
                }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(ReadTokenState(token), Is.EqualTo("cancelled"));
        }

        [Test]
        public void Given_CommittedActionData_When_NavigatingToAnUnmatchedPath_Then_ItIsCleared()
        {
            // Arrange
            var router = BuildRouter("/items",
                Route("/", children: new[]
                {
                    V.Route("items", V.Component(StubA), action: (_, _) => VelvetTask.FromResult<object>("saved")),
                }));
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }).GetAwaiter().GetResult();
            var before = router.CurrentActionData.Count;

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That((before, router.CurrentActionData.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnActionStillRunning_When_AnUnmatchedPathCommits_Then_TheNextNavigationKeepsTheRootsData()
        {
            // An action that has started requires every loader to run on the next commit; the 404's commit
            // is that commit, so the one after it keeps what it may.
            // Arrange
            var rootLoads = 0;
            var router = BuildRouter("/items",
                Route("/", loader: (_, _) => VelvetTask.FromResult<object>(++rootLoads), children: new[]
                {
                    V.Route("items", V.Component(StubA),
                        action: (_, _) => new VelvetTaskCompletionSource<object>().Task),
                    Route("a"),
                }));
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }).Forget();
            router.NavigateSync("/nowhere");

            // Act
            router.NavigateSync("/a");

            // Assert
            Assert.That(rootLoads, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABlockerHoldingANavigation_When_ALaterBlockerLetsAnUnmatchedPathCommit_Then_TheFirstReturnsToIdle()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());
            var holding = new RouteBlockerState();
            router.RouteBlockerManager.Register(args => args.NextLocation.Path == "/held", holding);
            router.NavigateSync("/held");
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());
            var before = holding.Status;

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That((before, holding.Status), Is.EqualTo((RouteBlockerStatus.Blocked, RouteBlockerStatus.Idle)));
        }

        #endregion

        #region What renders

        [Test]
        public void Given_NoErrorElement_When_NavigatingToAnUnmatchedPath_Then_TheDefaultErrorElementShowsTheStatus()
        {
            // Arrange
            var router = BuildRouter("/home", RootWithHome());
            router.NavigateSync("/nowhere");
            LogAssert.Expect(LogType.Exception, new Regex("No route matches URL"));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(_root.FindLabelByText("404 Not Found") != null, Is.True);
        }

        [Test]
        public void Given_ARootWithAnErrorElement_When_NavigatingToAnUnmatchedPath_Then_ItRenders()
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("/", errorElement: V.Component(Caught), children: new[] { Route("home") }));
            router.NavigateSync("/nowhere");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("caught") != null, Is.True);
        }

        #endregion
    }
}
