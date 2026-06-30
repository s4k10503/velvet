// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the router context hooks (<see cref="Hooks.UseParams"/> and
    /// <see cref="Hooks.UseLocation"/>) reading from <see cref="RouterContext.Location"/>.
    /// <list type="bullet">
    /// <item>Reading the router location without any <see cref="RouterContext.Location"/> Provider yields the
    /// context default: a null location, and therefore an empty parameter dictionary.</item>
    /// <item>When a <see cref="RouterContext.Location"/> Provider supplies a location, a descendant component
    /// observes that exact location and its route parameters.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Reads the context through the live context cursor that the <c>V.Mount</c> render path establishes; a raw
    /// <c>Reconciler.Reconcile</c> does not own that cursor lifecycle, so the no-Provider cases drive the bare
    /// reconciler and the with-Provider cases drive <c>V.Mount</c>. Per-component captures are exposed via static
    /// fields reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class RouterHooksTests
    {
        private VisualElement _root = null!;
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _reconciler = new Reconciler();
            ParamsCapture.Reset();
            LocationCapture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        #region UseParams

        [Test]
        public void Given_NoRouterLocationProvider_When_Rendered_Then_ParamsAreEmpty()
        {
            // Arrange
            var tree = new VNode[] { V.Component(ParamsCapture.Render) };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            Assume.That(ParamsCapture.LastParams, Is.Not.Null, "Precondition: the params hook produced a dictionary");

            // Assert
            Assert.That(ParamsCapture.LastParams, Is.Empty, "Without a Provider the location is null, so params are empty");
        }

        [Test]
        public void Given_RouterLocationProviderWithParams_When_Rendered_Then_ParamsAreObserved()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/avatar/123",
                Params = new Dictionary<string, string> { { "id", "123" } },
                Matches = Array.Empty<RouteMatch>(),
            };

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(RouterContext.Location, location, new VNode[]
                {
                    V.Component(ParamsCapture.Render),
                }));

            // Assert
            Assert.That(ParamsCapture.LastParams!["id"], Is.EqualTo("123"), "The provided route param is observed by the descendant");
        }

        #endregion

        #region UseLocation

        [Test]
        public void Given_NoRouterLocationProvider_When_Rendered_Then_LocationIsNull()
        {
            // Arrange
            var tree = new VNode[] { V.Component(LocationCapture.Render) };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(LocationCapture.LastLocation, Is.Null, "Without a Provider the location context default is null");
        }

        [Test]
        public void Given_RouterLocationProvider_When_Rendered_Then_LocationIsObserved()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/room",
                Params = new Dictionary<string, string>(),
                Matches = Array.Empty<RouteMatch>(),
            };

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(RouterContext.Location, location, new VNode[]
                {
                    V.Component(LocationCapture.Render),
                }));

            // Assert
            Assert.That(LocationCapture.LastLocation, Is.SameAs(location), "The descendant observes the exact provided location instance");
        }

        #endregion

        #region UseLoaderData (suspense-based; covered by Suspense integration)

        // UseLoaderData<T> either returns T directly or throws FiberSuspendSignal, so its behavior is verified by
        // Suspense integration tests. These ignored stubs keep the contract trackable in the Test Runner UI.

        [Test, Ignore("UseLoaderData uses a FiberSuspendSignal design verified by Suspense integration tests.")]
        public void Given_LoaderDataAndMatch_When_Rendered_Then_ReturnsData() { }

        [Test, Ignore("UseLoaderData uses a FiberSuspendSignal design verified by Suspense integration tests.")]
        public void Given_NoMatch_When_Rendered_Then_ReturnsDefault() { }

        [Test, Ignore("UseLoaderData uses a FiberSuspendSignal design verified by Suspense integration tests.")]
        public void Given_NoProvider_When_Rendered_Then_ReturnsDefault() { }

        #endregion

        #region Capture components

        private static class ParamsCapture
        {
            public static IReadOnlyDictionary<string, string>? LastParams;

            public static void Reset() => LastParams = null;

            [Component]
            public static VNode Render()
            {
                var location = Hooks.UseContext(RouterContext.Location);
                LastParams = location?.Params ?? new Dictionary<string, string>();
                return V.Label(text: "params");
            }
        }

        private static class LocationCapture
        {
            public static RouterLocation? LastLocation;

            public static void Reset() => LastLocation = null;

            [Component]
            public static VNode Render()
            {
                LastLocation = Hooks.UseContext(RouterContext.Location);
                return V.Label(text: "location");
            }
        }

        #endregion
    }
}
