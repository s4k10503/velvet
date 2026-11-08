// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the committed errors as React Router 7's <c>state.errors</c>.
    /// <list type="bullet">
    /// <item>A loader's, an action's or a Suspend loader's error is keyed by the nearest route with an
    /// <c>errorElement</c> at or above the route that failed, or by the root (<c>findNearestBoundary</c>); of two
    /// reaching one boundary, the higher route's stays.</item>
    /// <item>A failed action takes the place of the first loader error; with none it is committed at its own
    /// boundary.</item>
    /// <item>Loader data a navigation kept is dropped below the boundary an error is committed at
    /// (<c>mergeLoaderData</c>).</item>
    /// <item>The highest route an error is keyed by renders it, and <c>UseRouteError</c> reads the error keyed by
    /// the calling route.</item>
    /// </list>
    /// </summary>
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouteErrorStateParityTests
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

        private static Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> Fails(string message)
            => (_, _) => throw new InvalidOperationException(message);

        private static Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> Loads(string value)
            => (_, _) => VelvetTask.FromResult<object>(value);

        [Component]
        private static VNode ErrorLabel() => V.Label(text: "error:" + Hooks.UseRouteError()?.Message);

        [Test]
        public void Given_ALoaderThatFailsUnderNoErrorElement_When_Committed_Then_ItsErrorIsKeyedByTheRoot()
        {
            // Arrange / Act
            var router = BuildRouter("/a", Route("/", children: new[] { Route("a", loader: Fails("a-boom")) }));

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("a-boom"));
        }

        [Test]
        public void Given_ALoaderThatFailsUnderAnErrorElement_When_Committed_Then_ItsErrorIsKeyedByThatRoute()
        {
            // Arrange / Act
            var router = BuildRouter("/a/b", Route("/", children: new[]
            {
                Route("a", errorElement: V.Component(StubB), children: new[] { Route("b", loader: Fails("b-boom")) }),
            }));

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0-0")?.Message, Is.EqualTo("b-boom"));
        }

        [Test]
        public void Given_TwoLoadersFailingToOneBoundary_When_Committed_Then_TheHigherRoutesErrorStays()
        {
            // Arrange / Act
            var router = BuildRouter("/a/b", Route("/", children: new[]
            {
                Route("a", loader: Fails("a-boom"), children: new[] { Route("b", loader: Fails("b-boom")) }),
            }));

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("a-boom"));
        }

        [Test]
        public void Given_AHigherErrorAboveALowerBoundarysError_When_Rendered_Then_TheHigherBoundaryRendersIt()
        {
            // Arrange — `a` has no errorElement, so its error is the root's; `b` renders its own.
            var router = BuildRouter("/a/b",
                Route("/", errorElement: V.Component(ErrorLabel), children: new[]
                {
                    Route("a", loader: Fails("a-boom"), children: new[]
                    {
                        Route("b", loader: Fails("b-boom"), errorElement: V.Component(ErrorLabel)),
                    }),
                }));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("error:a-boom"), Is.Not.Null);
        }

        [Test]
        public void Given_AFailedActionAndAFailedLoader_When_Committed_Then_TheActionsErrorTakesTheLoadersPlace()
        {
            // Arrange — `items` renders its own error, so the root's loader runs again after the action, and
            // fails; React Router reports the action's error in that loader error's place, at the root.
            var rootLoads = 0;
            var router = BuildRouter("/items", Route("/",
                loader: (_, _) => ++rootLoads == 1
                    ? VelvetTask.FromResult<object>("root")
                    : throw new InvalidOperationException("root-boom"),
                children: new[]
                {
                    V.Route("items", V.Component(StubA), errorElement: V.Component(StubB),
                        action: (_, _) => throw new InvalidOperationException("action-boom")),
                }));

            // Act
            router.SubmitAsync("a=1", new SubmitOptions { Method = "post" }).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("action-boom"));
        }

        [Test]
        public void Given_KeptLoaderDataBelowTheBoundaryOfANewError_When_Committed_Then_ThatDataIsDropped()
        {
            // Arrange — `b` and `k` hold the same place over the same pathname, so their data is kept; the new
            // `:f` fails, and its error is `b`'s, which `k` sits below.
            var router = BuildRouter("/b/k/one", Route("/", children: new[]
            {
                Route("b", loader: Loads("b"), errorElement: V.Component(StubB), children: new[]
                {
                    Route("k", loader: Loads("k"), children: new[]
                    {
                        Route(":f", loader: (context, _) => context.Params["f"] == "two"
                            ? throw new InvalidOperationException("f-boom")
                            : VelvetTask.FromResult<object>("f")),
                    }),
                }),
            }));

            // Act
            router.NavigateSync("/b/k/two");

            // Assert
            Assert.That(router.GetLoaderData("0-0-0"), Is.Null);
        }

        [UnityTest]
        public IEnumerator Given_TwoSuspendLoadersFailingLowerFirst_When_BothHaveFailed_Then_TheHigherRoutesErrorStays()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — `b`'s failure arrives first and is keyed by the root; `a`'s, the higher route's, arrives
            // after it.
            var a = new VelvetTaskCompletionSource<object>();
            var b = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/a/b", Route("/", children: new[]
            {
                Route("a", loader: (_, _) => a.Task, loaderMode: LoaderMode.Suspend, children: new[]
                {
                    Route("b", loader: (_, _) => b.Task, loaderMode: LoaderMode.Suspend),
                }),
            }));
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("b-boom"));
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("a-boom"));

            // Act
            b.TrySetException(new InvalidOperationException("b-boom"));
            await VelvetTask.Yield();
            a.TrySetException(new InvalidOperationException("a-boom"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("a-boom"));
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderFailingAboveACommittedLoaderError_When_ItFails_Then_ItsErrorTakesTheBoundary()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — `b`'s awaited loader fails and commits at the root; `a`, above it, fails afterwards.
            var a = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/a/b", Route("/", children: new[]
            {
                Route("a", loader: (_, _) => a.Task, loaderMode: LoaderMode.Suspend, children: new[]
                {
                    Route("b", loader: Fails("b-boom")),
                }),
            }));
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("a-boom"));

            // Act
            a.TrySetException(new InvalidOperationException("a-boom"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("a-boom"));
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderFailingUnderNoErrorElement_When_ItFails_Then_ItsErrorIsKeyedByTheRoot()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/deferred", Route("/", children: new[]
            {
                Route("deferred", loader: (_, _) => pending.Task, loaderMode: LoaderMode.Suspend),
            }));
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("deferred-boom"));

            // Act
            pending.TrySetException(new InvalidOperationException("deferred-boom"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderErrors.GetValueOrDefault("0")?.Message, Is.EqualTo("deferred-boom"));
        });
    }
}
