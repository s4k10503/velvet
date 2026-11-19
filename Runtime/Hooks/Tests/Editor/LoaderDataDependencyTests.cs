// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>Hooks.UseLoaderData</c> reads the loader-data context on every render, the early return for a
    /// reader with no matched route included, so the reader depends on that context whichever arm it takes.
    /// </summary>
    [TestFixture]
    internal sealed class LoaderDataDependencyTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            LoaderDataHost.Reset();
            CountingReader.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        [Test]
        public void Given_AReaderOutsideAMatchedRoute_When_TheLoaderDataProviderChanges_Then_ItReRenders()
        {
            // Arrange — the reader is memoized with no props and sits at depth 0, where UseLoaderData has no route
            // to read and returns the default, so a parent-driven render bails at it and only a dependency on the
            // loader-data context can render it again. The router and the location stay the same instances, so
            // neither of the other contexts it reads changes.
            LoaderDataHost.Router = new Router(Array.Empty<RouteDefinition>());
            LoaderDataHost.Location = new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = Array.Empty<RouteMatch>(),
            };
            using var mounted = V.Mount(_root, V.Component(LoaderDataHost.Render, key: "host"));
            var rendersAtMount = CountingReader.Renders;

            // Act
            LoaderDataHost.SetData.Invoke(new Dictionary<string, object> { { "route", "loaded" } });
            mounted.FlushStateForTest();

            // Assert
            Assert.That((rendersAtMount, CountingReader.Renders), Is.EqualTo((1, 2)),
                "A reader outside a matched route still depends on the loader-data context, so a new value "
                + "renders it again");
        }

        private static class LoaderDataHost
        {
            public static Router? Router;
            public static RouterLocation? Location;
            public static StateUpdater<IReadOnlyDictionary<string, object>> SetData;

            public static void Reset()
            {
                Router = null;
                Location = null;
                SetData = default;
            }

            [Component(Compiler = false)]
            public static VNode Render()
            {
                var (data, setData) = Hooks.UseState<IReadOnlyDictionary<string, object>>(
                    new Dictionary<string, object>());
                SetData = setData;
                return V.Provider(RouterContext.Router, Router!, new VNode[]
                {
                    V.Provider(RouterContext.Location, Location, new VNode[]
                    {
                        V.Provider(RouterContext.LoaderData, data, new VNode[]
                        {
                            V.Provider(RouterContext.Depth, 0, new VNode[]
                            {
                                V.Component(CountingReader.Render, key: "reader"),
                            }),
                        }),
                    }),
                });
            }
        }

        private static class CountingReader
        {
            public static int Renders;

            public static void Reset() => Renders = 0;

            // Memoized so the host's own render bails here; opted out of the weaver so every render that reaches
            // the body counts.
            [Component(Memoize = true, Compiler = false)]
            public static VNode Render()
            {
                Renders++;
                Hooks.UseLoaderData<string>();
                return V.Label(text: "reader");
            }
        }
    }
}
