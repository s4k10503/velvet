using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an <see cref="IRouteScope"/> whose <c>Dispose</c> throws does to the teardown that
    /// invoked it. The scope is built by the application's <see cref="IRouteScopeFactory"/>, so every
    /// entrance below reaches the caller's code, and each contains it the way a <c>refCallback</c>
    /// cleanup is contained.
    /// <list type="bullet">
    /// <item>Changing route: the Outlet still mounts the route it navigated to, the incoming route's own
    /// scope is built regardless of the failure, and the scope it left behind is disposed once rather
    /// than again by the teardown sweep.</item>
    /// <item>Removing the Outlet: the removal batch continues, so the rows the walk had not reached yet
    /// leave too.</item>
    /// <item>An AnimatePresence beside the Outlet renders at its position exactly as it does with no
    /// failure in play; the retirement itself is pinned by AnimatePresenceStateRetirementTests.</item>
    /// <item>Disposing the whole reconciler: the sweep attempts every other scope still registered.</item>
    /// </list>
    /// Every case that reads the tree folds in whether the failure left the reconcile call, because a
    /// tree where it escapes never reaches the state the case is named for and a bare rethrow says
    /// nothing about which of the two moved.
    /// </summary>
    [TestFixture]
    internal sealed class RouteScopeDisposalFailureTests
    {
        private const string DisposeFailureMessage = "arranged failure out of a route scope Dispose";

        private static int s_disposeAttempts;

        // Switched off so a case can run its arrangement once without the failure, as the control its
        // measured run is compared against.
        private static bool s_throwOnDispose;

        private sealed class ThrowingRouteScope : IRouteScope
        {
            public int DisposeCount { get; private set; }

            public T Resolve<T>() => throw new NotSupportedException();

            public void Dispose()
            {
                DisposeCount++;
                s_disposeAttempts++;
                if (s_throwOnDispose)
                {
                    throw new InvalidOperationException(DisposeFailureMessage);
                }
            }
        }

        private const string CreateFailureMessage = "arranged failure out of a route scope CreateScope";

        private sealed class ThrowingRouteScopeFactory : IRouteScopeFactory
        {
            public List<ThrowingRouteScope> Scopes { get; } = new();

            public bool ThrowOnCreate { get; set; }

            public IRouteScope CreateScope(RouteDefinition? route, IRouteScope? parent)
            {
                if (ThrowOnCreate)
                {
                    throw new InvalidOperationException(CreateFailureMessage);
                }
                var scope = new ThrowingRouteScope();
                Scopes.Add(scope);
                return scope;
            }
        }

        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;
        private Router _router = null!;
        private ThrowingRouteScopeFactory _scopeFactory = null!;

        [SetUp]
        public void SetUp()
        {
            s_disposeAttempts = 0;
            s_throwOnDispose = true;
            _reconciler = new Reconciler();
            _root = new VisualElement();
            _scopeFactory = new ThrowingRouteScopeFactory();
            _router = new Router(
                V.Routes(
                    V.Route(path: "/", element: V.Component(RouteBody, key: "route")),
                    V.Route(path: "/other", element: V.Component(OtherRouteBody, key: "other"))),
                _scopeFactory);
            _router.NavigateAsync("/").GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            // A case that leaves a scope registered has the fixture's own disposal sweep it, and a throw
            // from there is fixture noise rather than anything a case arranged. Each case arms the log
            // expectations for the failures it does arrange, and LogAssert settles them before this runs.
            s_throwOnDispose = false;
            _router.Dispose();
            _reconciler.Dispose();
        }

        [Test]
        public void Given_AFactoryThatThrowsOnCreate_When_TheRouteChanges_Then_TheThrowDoesNotEscapeTheReconcile()
        {
            // Arrange — the outgoing scope disposes cleanly, so the only failure is the replacement's
            // creation: the entrance the containment work around it left open.
            s_throwOnDispose = false;
            var mounted = MountRoutedApp();
            _router.NavigateAsync("/other").GetAwaiter().GetResult();
            _scopeFactory.ThrowOnCreate = true;
            // One line, not two: with no error boundary mounted the propagation falls back to
            // Debug.LogException, where the drop-and-log shape beside it logs a tag and the exception.
            LogAssert.Expect(LogType.Exception,
                             new Regex("InvalidOperationException: " + CreateFailureMessage));

            // Act
            var escaped = EscapesFrom(() => ReRenderAppAtCurrentLocation(mounted));

            // Assert — what the incoming route renders rides along, because a reconcile that escaped
            // would leave the old label standing and satisfy neither half on its own.
            Assert.That((escaped, LabelTextsUnder(_root)), Is.EqualTo((false, "other")));
        }

        [Test]
        public void Given_ARouteScopeDisposeThatThrows_When_TheRouteChanges_Then_TheIncomingRouteStillMounts()
        {
            // Arrange — the departing route's scope is disposed by the patch that installs the incoming one.
            var mounted = MountRoutedApp();
            _router.NavigateAsync("/other").GetAwaiter().GetResult();
            ContainedFailureLog.Expect<InvalidOperationException>("FiberNodePatcher", DisposeFailureMessage);

            // Act
            var escaped = EscapesFrom(() => ReRenderAppAtCurrentLocation(mounted));

            // Assert
            Assert.That((escaped, LabelTextsUnder(_root)), Is.EqualTo((false, "other")));
        }

        // GREEN_ON_BASE(characterization): the shipped patch already builds the replacement scope.
        // Nothing read that. Measured, conditioning the build on a clean dispose reddens this case on the
        // count it asserts, and the case below it only through the sweep log it arms and then never gets.
        [Test]
        public void Given_ARouteScopeDisposeThatThrows_When_TheRouteChanges_Then_TheIncomingRouteStillGetsAScope()
        {
            // Arrange — the case above reads that the incoming route mounts, which it does with no scope at
            // all; this one reads that the factory was asked for the replacement anyway. Conditioning the
            // build on a clean disposal is the change it exists to catch.
            var mounted = MountRoutedApp();
            _router.NavigateAsync("/other").GetAwaiter().GetResult();
            ContainedFailureLog.Expect<InvalidOperationException>("FiberNodePatcher", DisposeFailureMessage);

            // Act
            var escaped = EscapesFrom(() => ReRenderAppAtCurrentLocation(mounted));

            // Assert
            Assert.That((escaped, _scopeFactory.Scopes.Count), Is.EqualTo((false, 2)));
        }

        [Test]
        public void Given_ARouteScopeDisposeThatThrows_When_TheRouteChanges_Then_TheDepartedScopeIsNotSweptAgain()
        {
            // Arrange — whether the failure left the reconcile call is the case above's subject; this one
            // reads only how many times the route the navigation left behind had its scope disposed. That
            // count does not localise to the catch: measured, removing the catch alone leaves it at 1 and
            // reddens this case only through its unmatched log expectations, while reversing the registry
            // removal back behind the Dispose leaves it wholly green.
            var mounted = MountRoutedApp();
            _router.NavigateAsync("/other").GetAwaiter().GetResult();
            ContainedFailureLog.Expect<InvalidOperationException>("FiberNodePatcher", DisposeFailureMessage);
            ContainedFailureLog.Expect<InvalidOperationException>("Reconciler", DisposeFailureMessage);
            _ = EscapesFrom(() => ReRenderAppAtCurrentLocation(mounted));

            // Act — the sweep that disposes whatever is still registered.
            _reconciler.Dispose();
            _reconciler = new Reconciler();

            // Assert
            Assert.That(_scopeFactory.Scopes[0].DisposeCount, Is.EqualTo(1));
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
        public void Given_ARouteScopeDisposeThatThrows_When_APresenceBesideItIsRemoved_Then_ThePresenceRendersAsItDoesWithoutTheFailure()
        {
            // Arrange — the presence and the Outlet share a container, so one removal pass answers for both.
            // The expected names are measured from the same sequence run with the scope not throwing, so a
            // presence that stops retiring for its own reasons moves both readings and leaves this case
            // green: what it separates is the contained failure from no failure, and nothing else.
            var withoutFailure = PresenceNamesAfterRemovalBeside(throwOnDispose: false);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", DisposeFailureMessage);

            // Act
            var withFailure = PresenceNamesAfterRemovalBeside(throwOnDispose: true);

            // Assert
            Assert.That(withFailure, Is.EqualTo(("escaped:False", withoutFailure.Names)));
        }

        [Test]
        public void Given_TwoOutletsWhoseScopesBothThrow_When_TheReconcilerIsDisposed_Then_TheSweepAttemptsBoth()
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

        [Component]
        private static VNode OtherRouteBody() => V.Label(text: "other");

        [Component]
        private static VNode RoutedApp(RouterLocation location)
            => V.Provider(RouterContext.Location, location,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Depth, 0, children: new VNode[] { V.Outlet() }),
                });

        // The spine renders inside a component so the location move on the second render has a fiber under
        // it. Reconciled from the walk root instead, GeneralPathReconciler.NotifyContextValueChange finds
        // none to propagate from and asserts that it is skipping live propagation — a diagnostic these two
        // cases would then have to expect. The Outlet patch reaches the route change from either shape.
        private VNode[] MountRoutedApp()
        {
            var tree = new VNode[] { V.Component(RoutedApp, _router.CurrentLocation!, key: "app") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            return tree;
        }

        private void ReRenderAppAtCurrentLocation(VNode[] mounted)
            => _reconciler.Reconcile(_root, mounted,
                new VNode[] { V.Component(RoutedApp, _router.CurrentLocation!, key: "app") });

        // The same spine at the top level, which the cases that never move the location can take.
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

        // Mounts a presence beside a routed Outlet on a reconciler of its own, removes both in one pass,
        // then renders a second presence at the same position and reports what stands there.
        private static (string Escaped, string Names) PresenceNamesAfterRemovalBeside(bool throwOnDispose)
        {
            s_throwOnDispose = throwOnDispose;
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = new VNode[] { Presence("a"), RoutedOutlet() };
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed);
            var empty = Array.Empty<VNode>();
            var escaped = EscapesFrom(() => reconciler.Reconcile(root, committed, empty));
            reconciler.Reconcile(root, empty, new VNode[] { Presence("b") });
            return ("escaped:" + escaped, NamesOf(root));
        }

        // Filtered on the arranged messages so any other InvalidOperationException still leaves the case.
        // Both of them, so that a case whose arrangement is uncontained answers `true` rather than
        // raising: a reader of this suite that has no containment is one of the two readings it takes.
        private static bool EscapesFrom(Action reconcile)
        {
            try
            {
                reconcile();
                return false;
            }
            catch (InvalidOperationException exception) when (exception.Message == DisposeFailureMessage
                                                              || exception.Message == CreateFailureMessage)
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

        private static string LabelTextsUnder(VisualElement parent)
        {
            var texts = new List<string>();
            void Walk(VisualElement element)
            {
                if (element is Label label) texts.Add(label.text);
                for (var i = 0; i < element.childCount; i++) Walk(element.ElementAt(i));
            }
            Walk(parent);
            return string.Join(",", texts);
        }

        #endregion
    }
}
