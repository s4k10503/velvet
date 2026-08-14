// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.RouterProvider</c>: the one component an application mounts over a
    /// <see cref="Router"/>, which publishes everything the routing hooks read from it.
    /// <list type="bullet">
    /// <item>It renders the matched route, whether the router navigated before it mounted or after.</item>
    /// <item>It publishes the loader data and the loader errors as well as the location, so
    /// <c>UseLoaderData</c> and <c>UseRouteError</c> answer beneath it.</item>
    /// <item>It stays subscribed: a Suspend loader that resolves after the commit reaches
    /// <c>UseLoaderData</c> without a further navigation.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterProviderTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static class Capture
        {
            public static RouterLocation? Location;
            public static string? LoaderData;
            public static Exception? Error;

            public static void Reset()
            {
                Location = null;
                LoaderData = null;
                Error = null;
            }

            [Component]
            public static VNode Render()
            {
                Location = Hooks.UseLocation();
                LoaderData = Hooks.UseLoaderData<string>();
                Error = Hooks.UseRouteError();
                return V.Label(text: "capture");
            }
        }

        private static ComponentNode CaptureElement() => V.Component(Capture.Render, key: "cap");

        [Test]
        public void Given_ARouterThatHasAlreadyNavigated_When_TheProviderMounts_Then_TheMatchedRouteRenders()
        {
            // Arrange
            var router = BuildRouter("/dash", Route("dash", element: CaptureElement()));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(Capture.Location?.Path, Is.EqualTo("/dash"));
        }

        [Test]
        public void Given_AProviderMountedBeforeAnyNavigation_When_TheRouterNavigates_Then_TheMatchedRouteRenders()
        {
            // The host mounts before starting the first navigation, so the subscription is what puts the
            // opening route on screen as much as every later one.
            // Arrange
            var router = new Router(new[] { Route("dash", element: CaptureElement()) });
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            router.NavigateSync("/dash");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.Location?.Path, Is.EqualTo("/dash"));
        }

        [Test]
        public void Given_AMountedProvider_When_TheRouterNavigatesAgain_Then_TheNewLocationReachesTheRoute()
        {
            // Arrange
            var router = BuildRouter("/dash",
                Route("dash", element: CaptureElement()),
                Route("about", element: CaptureElement()));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            router.NavigateSync("/about");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.Location?.Path, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_ARouteWhoseLoaderProducedData_When_TheProviderMounts_Then_UseLoaderDataReadsIt()
        {
            // Arrange
            var router = BuildRouter("/data",
                Route("data", element: CaptureElement(),
                    loader: (ctx, ct) => UniTask.FromResult((object)"hello")));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(Capture.LoaderData, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_ARouteWhoseLoaderThrew_When_TheProviderMounts_Then_UseRouteErrorReadsIt()
        {
            // Arrange
            var router = BuildRouter("/boom",
                Route("boom",
                    element: CaptureElement(),
                    errorElement: V.Component(Capture.Render, key: "cap-error"),
                    loader: (ctx, ct) => throw new InvalidOperationException("loader-boom")));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(Capture.Error?.Message, Is.EqualTo("loader-boom"));
        }

        [UnityTest]
        public IEnumerator Given_AMountedProvider_When_ASuspendLoaderResolvesAfterTheCommit_Then_UseLoaderDataSeesIt()
            => UniTask.ToCoroutine(async () =>
        {
            // The commit publishes no data for a Suspend loader, so this is the case a bridge that snapshots
            // the router once at mount cannot answer.
            // Arrange
            var deferred = new UniTaskCompletionSource<object>();
            var router = BuildRouter("/data",
                Route("data", element: CaptureElement(), loaderMode: LoaderMode.Suspend,
                    loader: (ctx, ct) => deferred.Task));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            deferred.TrySetResult("deferred-data");
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.LoaderData, Is.EqualTo("deferred-data"));
        });
    }
}
