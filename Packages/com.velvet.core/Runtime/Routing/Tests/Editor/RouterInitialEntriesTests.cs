// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="RouterOptions"/> and the initial load, as React Router's
    /// <c>createMemoryHistory</c>, <c>createRouter</c> and <c>initialize()</c> do them.
    /// <list type="bullet">
    /// <item>The history holds the initial entries, the first keyed <c>"default"</c>, and the router stands on
    /// <see cref="RouterOptions.InitialIndex"/>, clamped, or on the last.</item>
    /// <item>A starting location with a loader leaves the router uninitialized while it loads, reporting no
    /// navigation, and the routes render the hydrate fallback, or nothing, until it settles.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterInitialEntriesTests
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

        private static RouteDefinition[] Pages() => new[] { Route("a"), Route("b"), Route("c") };

        private static Router Start(int? index, params string[] entries)
            => new(Pages(), options: new RouterOptions { InitialEntries = entries, InitialIndex = index });

        [Test]
        public void Given_SeveralInitialEntries_When_NoIndexIsGiven_Then_TheRouterStandsOnTheLast()
        {
            // Arrange / Act
            var router = Start(null, "/a", "/b", "/c");

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/c"));
        }

        [Test]
        public void Given_AnInitialIndex_When_Constructed_Then_TheRouterStandsOnThatEntry()
        {
            // Arrange / Act
            var router = Start(1, "/a", "/b", "/c");

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/b"));
        }

        [Test]
        public void Given_AnInitialIndexPastTheEnd_When_Constructed_Then_ItIsClampedOntoTheLast()
        {
            // Arrange / Act
            var router = Start(9, "/a", "/b");

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/b"));
        }

        [Test]
        public void Given_AnInitialIndexOnTheFirstEntry_When_Read_Then_ItsKeyIsDefault()
        {
            // Arrange / Act
            var router = Start(0, "/a", "/b");

            // Assert
            Assert.That(router.CurrentLocation?.Key, Is.EqualTo("default"));
        }

        [Test]
        public void Given_AnInitialIndexOnTheFirstEntry_When_SteppingForward_Then_TheNextEntryIsThere()
        {
            // Arrange
            var router = Start(0, "/a", "/b");

            // Act
            router.GoForwardSync();

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/b"));
        }

        [Test]
        public void Given_AnInitialEntryWithAFragment_When_Constructed_Then_TheHashHoldsIt()
        {
            // Arrange / Act
            var router = Start(null, "/a?q=1#top");

            // Assert
            Assert.That($"{router.CurrentLocation?.Path} {router.CurrentLocation?.Hash}", Is.EqualTo("/a?q=1 #top"));
        }

        [Test]
        public void Given_NoInitialEntry_When_Constructed_Then_ItThrows()
        {
            // Arrange
            var options = new RouterOptions { InitialEntries = Array.Empty<string>() };

            // Act
            TestDelegate construct = () => _ = new Router(Pages(), options: options);

            // Assert
            Assert.That(construct, Throws.ArgumentException);
        }

        [Test]
        public void Given_NoLoaderAtTheStart_When_Constructed_Then_TheRouterIsInitialized()
        {
            // Arrange / Act
            var router = Start(null, "/a");

            // Assert
            Assert.That(router.Initialized, Is.True);
        }

        private static Router Loading(VelvetTaskCompletionSource<object> pending, ComponentNode? fallback = null)
            => new(new[]
            {
                new RouteDefinition
                {
                    Path = "/",
                    Element = V.Component(StubA),
                    Loader = (_, _) => pending.Task,
                    HydrateFallbackElement = fallback,
                },
            });

        [Test]
        public void Given_ALoaderStillRunningAtTheStart_When_AFragmentOnlyNavigationFollows_Then_TheLoaderRunsAgain()
        {
            // Arrange — the router stands on `/` without having loaded it, so a fragment is no reason to skip loading.
            var calls = 0;
            var pending = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                new RouteDefinition
                {
                    Path = "/",
                    Element = V.Component(StubA),
                    Loader = (_, _) =>
                    {
                        calls++;
                        return pending.Task;
                    },
                },
            });

            // Act
            router.NavigateAsync("/#top").Forget();

            // Assert
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnInitializedRouter_When_ItsRouteOwesData_Then_NoHydrateFallbackShows()
        {
            // Arrange — a location handed to the Outlet directly, whose route has an Await loader with no data, under
            // a router that has finished its initial load.
            var router = BuildRouter("/", Route("/"));
            var route = new RouteDefinition
            {
                Path = "/",
                Element = V.Component(StubB),
                Loader = (_, _) => VelvetTask.FromResult<object>("never-read"),
            };
            var location = new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new[]
                {
                    new RouteMatch { Route = route, RouteId = "0", MatchedPath = "/", Params = new Dictionary<string, string>() },
                },
            };

            // Act
            using var mounted = V.Mount(_root, V.Provider(RouterContext.Router, router, children: new VNode[]
            {
                V.Provider(RouterContext.Location, location, children: new VNode[] { V.Outlet() }),
            }));

            // Assert
            Assert.That(_root.FindLabelByText("stub-b"), Is.Not.Null);
        }

        [Test]
        public void Given_ALoaderStillRunningAtTheStart_When_Read_Then_TheRouterIsNotInitialized()
        {
            // Arrange / Act
            var router = Loading(new VelvetTaskCompletionSource<object>());

            // Assert
            Assert.That(router.Initialized, Is.False);
        }

        [Test]
        public void Given_ALoaderStillRunningAtTheStart_When_TheNavigationIsRead_Then_ItIsIdle()
        {
            // Arrange / Act
            var router = Loading(new VelvetTaskCompletionSource<object>());

            // Assert
            Assert.That(router.Navigation.State, Is.EqualTo(NavigationLifecycle.Idle));
        }

        [UnityTest]
        public IEnumerator Given_ALoaderAtTheStart_When_ItSettles_Then_TheRouterIsInitialized()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            var router = Loading(pending);

            // Act
            pending.TrySetResult("root");
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.Initialized, Is.True);
        });

        [Test]
        public void Given_AHydrateFallback_When_TheStartingLoaderIsStillRunning_Then_TheFallbackRenders()
        {
            // Arrange
            var router = Loading(new VelvetTaskCompletionSource<object>(), V.Component(StubB));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("stub-b"), Is.Not.Null);
        }

        [Test]
        public void Given_NoHydrateFallback_When_TheStartingLoaderIsStillRunning_Then_TheRouteDoesNotRender()
        {
            // Arrange
            var router = Loading(new VelvetTaskCompletionSource<object>());

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("stub-a"), Is.Null);
        }

        [UnityTest]
        public IEnumerator Given_AHydrateFallback_When_TheStartingLoaderSettles_Then_TheRouteRenders()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            var router = Loading(pending, V.Component(StubB));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            pending.TrySetResult("root");
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("stub-a"), Is.Not.Null);
        });
    }
}
