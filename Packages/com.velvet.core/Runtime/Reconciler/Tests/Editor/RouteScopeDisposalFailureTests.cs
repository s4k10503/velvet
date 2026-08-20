using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an <see cref="IRouteScope"/> whose <c>Dispose</c> throws does to the teardown that
    /// invoked it. The scope is built by the application's <see cref="IRouteScopeFactory"/>, so each
    /// entrance below reaches the caller's code — the reason a <c>refCallback</c> cleanup is contained at
    /// the same two.
    /// <list type="bullet">
    /// <item>Removing the Outlet: the removal batch continues, so the rows the walk had not reached yet
    /// leave too.</item>
    /// <item>An AnimatePresence beside the Outlet retires with those removals, so a later render at its
    /// position does not splice the departed child back as an exiting ghost.</item>
    /// <item>Disposing the whole reconciler: every other scope still registered is disposed anyway.</item>
    /// </list>
    /// The first two fold in whether the failure left the reconcile call, because a tree where it escapes
    /// never reaches the state each is named for and a bare rethrow says nothing about which of the two
    /// moved.
    /// </summary>
    [TestFixture]
    internal sealed class RouteScopeDisposalFailureTests
    {
        private const string DisposeFailureMessage = "arranged failure out of a route scope Dispose";

        private static int s_disposeAttempts;

        private sealed class ThrowingRouteScope : IRouteScope
        {
            public T Resolve<T>() => throw new NotSupportedException();

            public void Dispose()
            {
                s_disposeAttempts++;
                throw new InvalidOperationException(DisposeFailureMessage);
            }
        }

        private sealed class ThrowingRouteScopeFactory : IRouteScopeFactory
        {
            public IRouteScope CreateScope(RouteDefinition? route, IRouteScope? parent) => new ThrowingRouteScope();
        }

        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;
        private Router _router = null!;

        [SetUp]
        public void SetUp()
        {
            s_disposeAttempts = 0;
            _reconciler = new Reconciler();
            _root = new VisualElement();
            _router = new Router(
                V.Routes(V.Route(path: "/", element: V.Component(RouteBody, key: "route"))),
                new ThrowingRouteScopeFactory());
            _router.NavigateAsync("/").GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _router.Dispose();
            _reconciler.Dispose();
        }

        [Test]
        public void Given_ARouteScopeDisposeThatThrows_When_TheOutletIsRemovedFirstOfABatch_Then_TheRestOfTheRowsGoToo()
        {
            // Arrange — the walk removes from the tail, so the Outlet is the one it reaches first.
            var mounted = new VNode[]
            {
                V.Div(name: "head"),
                V.Div(name: "middle"),
                RoutedOutlet(),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", DisposeFailureMessage);

            // Act
            var escaped = EscapesFrom(() => _reconciler.Reconcile(_root, mounted, Array.Empty<VNode>()));

            // Assert
            Assert.That((escaped, NamesOf(_root)), Is.EqualTo((false, "")));
        }

        [Test]
        public void Given_ARouteScopeDisposeThatThrows_When_APresenceBesideItIsRemoved_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — the presence and the Outlet share a container, so one removal pass answers for both.
            var committed = new VNode[] { Presence("a"), RoutedOutlet() };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), committed);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", DisposeFailureMessage);
            var empty = Array.Empty<VNode>();
            var escaped = EscapesFrom(() => _reconciler.Reconcile(_root, committed, empty));

            // Act
            _reconciler.Reconcile(_root, empty, new VNode[] { Presence("b") });

            // Assert
            Assert.That((escaped, NamesOf(_root)), Is.EqualTo((false, "item-b")));
        }

        [Test]
        public void Given_TwoOutletsStillMounted_When_TheReconcilerIsDisposedAndTheFirstScopeThrows_Then_TheOtherScopeIsDisposedAnyway()
        {
            // Arrange — two registered scopes, both throwing, so which one the sweep reaches first cannot
            // decide the reading.
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), new VNode[] { RoutedOutlet(), RoutedOutlet() });
            ContainedFailureLog.Expect<InvalidOperationException>("Reconciler", DisposeFailureMessage);
            ContainedFailureLog.Expect<InvalidOperationException>("Reconciler", DisposeFailureMessage);

            // Act
            _reconciler.Dispose();
            _reconciler = new Reconciler();

            // Assert — one attempt means the first throw stranded the rest of the sweep.
            Assert.That(s_disposeAttempts, Is.EqualTo(2));
        }

        #region Helpers

        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        [Component]
        private static VNode RouteBody() => V.Label(text: "route");

        private static VNode RoutedOutlet()
            => V.Provider(RouterContext.Location, Router.Current!.CurrentLocation!,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Depth, 0, children: new VNode[] { V.Outlet() }),
                });

        private static VNode Presence(string childKey) => V.AnimatePresence(key: "presence", children: new VNode[]
        {
            V.Motion(name: "item-" + childKey, key: childKey,
                variants: s_fade, animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f }),
        });

        // Filtered on the arranged message so any other InvalidOperationException still leaves the case.
        private static bool EscapesFrom(Action reconcile)
        {
            try
            {
                reconcile();
                return false;
            }
            catch (InvalidOperationException exception) when (exception.Message == DisposeFailureMessage)
            {
                return true;
            }
        }

        private static string NamesOf(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.childCount; i++) names.Add(parent.ElementAt(i).name);
            return string.Join(",", names);
        }

        #endregion
    }
}
