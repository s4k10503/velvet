using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies structural variants over an element that holds the rows of two roots: one root's element that another
    /// root's Portal mounts into, and one another root is mounted into, a ScrollView among them, whose rows sit in a
    /// content element of its own, and one whose userData the application wrote over. A change one root makes to the element's rows re-derives the rows of the other, whose
    /// rules it did not register, as CSS's structural pseudo-classes read the whole child list whatever placed it. The
    /// rules are <c>only:</c>, which holds wherever the other root's rows land.
    /// </summary>
    [TestFixture]
    internal sealed class CrossRootStructuralVariantTests
    {
        private VisualElement _targetRoot;
        private VisualElement _otherRoot;
        private static StateUpdater<bool> s_setShown;

        [SetUp]
        public void SetUp()
        {
            (_targetRoot, _otherRoot, s_setShown) = (new VisualElement(), new VisualElement(), default);
        }

        private VisualElement Target => _targetRoot.Q<VisualElement>("target");

        private VisualElement Own => _targetRoot.Q<VisualElement>("own");

        private static VNode TargetTree()
            => V.Div(name: "target", children: new VNode[] { V.Div(name: "own", className: "only:bg-mark") });

        private static VNode ScrollViewTree()
            => V.ScrollView(name: "target", children: new VNode[] { V.Div(name: "own", className: "only:bg-mark") });

        [Component(Compiler = false)]
        private static VNode SwitchableScrollView()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.ScrollView(name: "target",
                children: shown ? new VNode[] { V.Div(name: "own") } : Array.Empty<VNode>());
        }

        [Component(Compiler = false)]
        private static VNode SwitchableTarget()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "target", children: shown ? new VNode[] { V.Div(name: "own") } : Array.Empty<VNode>());
        }

        [Test]
        public void Given_AChildMatchingOnlyInOneRoot_When_AnotherRootsPortalMountsARowBesideIt_Then_ItStopsMatching()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, TargetTree());
            var onlyBeforeThePortal = Own.ClassListContains("bg-mark");

            // Act
            using var portalling = V.Mount(_otherRoot, V.Div(children: new VNode[]
            {
                V.Portal(Target, new VNode[] { V.Div(name: "portalled") }),
            }));

            // Assert — null where "own" did not match only: before the Portal's row joined it.
            Assert.That(onlyBeforeThePortal ? Own.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AChildMatchingOnlyInAnElementWhoseUserDataTheApplicationWroteOver_When_AnotherRootsPortalMountsARowBesideIt_Then_ItStopsMatching()
        {
            // Arrange — "target" sits directly in the element its root is mounted into.
            using var targetTree = V.Mount(_targetRoot, TargetTree());
            Target.userData = "the application's";
            var onlyBeforeThePortal = Own.ClassListContains("bg-mark");

            // Act
            using var portalling = V.Mount(_otherRoot, V.Div(children: new VNode[]
            {
                V.Portal(Target, new VNode[] { V.Div(name: "portalled") }),
            }));

            // Assert — null where "own" did not match only: before the Portal's row joined it.
            Assert.That(onlyBeforeThePortal ? Own.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AnotherRootsPortalRowBesideAChild_When_TheTargetsRootRemovesTheChild_Then_TheRowMatchesOnly()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, V.Component(SwitchableTarget, key: "target"));
            using var portalling = V.Mount(_otherRoot, V.Div(children: new VNode[]
            {
                V.Portal(Target, new VNode[] { V.Div(name: "portalled", className: "only:bg-mark") }),
            }));
            var portalled = _targetRoot.Q<VisualElement>("portalled");
            var notOnlyBeforeTheRemoval = !portalled.ClassListContains("bg-mark");

            // Act — a render of the target's own root, which registered none of the row's rules.
            s_setShown.Invoke(false);
            targetTree.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where the row matched only: while "own" stood beside it.
            Assert.That(notOnlyBeforeTheRemoval ? portalled.ClassListContains("bg-mark") : (bool?)null, Is.True);
        }

        [Test]
        public void Given_AChildMatchingOnlyInAScrollViewOfOneRoot_When_AnotherRootsPortalMountsARowBesideIt_Then_ItStopsMatching()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, ScrollViewTree());
            var onlyBeforeThePortal = Own.ClassListContains("bg-mark");

            // Act
            using var portalling = V.Mount(_otherRoot, V.Div(children: new VNode[]
            {
                V.Portal(Target, new VNode[] { V.Div(name: "portalled") }),
            }));

            // Assert — null where "own" did not match only: before the Portal's row joined it.
            Assert.That(onlyBeforeThePortal ? Own.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AnotherRootsPortalRowInAScrollViewBesideAChild_When_TheScrollViewsRootRemovesTheChild_Then_TheRowMatchesOnly()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, V.Component(SwitchableScrollView, key: "target"));
            using var portalling = V.Mount(_otherRoot, V.Div(children: new VNode[]
            {
                V.Portal(Target, new VNode[] { V.Div(name: "portalled", className: "only:bg-mark") }),
            }));
            var portalled = _targetRoot.Q<VisualElement>("portalled");
            var notOnlyBeforeTheRemoval = !portalled.ClassListContains("bg-mark");

            // Act — a render of the ScrollView's own root, which hands its pass the ScrollView rather than its content.
            s_setShown.Invoke(false);
            targetTree.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where the row matched only: while "own" stood beside it.
            Assert.That(notOnlyBeforeTheRemoval ? portalled.ClassListContains("bg-mark") : (bool?)null, Is.True);
        }

        [Test]
        public void Given_AChildMatchingOnlyInAScrollViewOfOneRoot_When_AnotherRootIsMountedIntoTheScrollView_Then_ItStopsMatching()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, ScrollViewTree());
            var onlyBeforeTheMount = Own.ClassListContains("bg-mark");

            // Act
            using var nested = V.Mount(Target, V.Div(name: "nested"));

            // Assert — null where "own" did not match only: before the other root's row joined it.
            Assert.That(onlyBeforeTheMount ? Own.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AChildMatchingOnlyInOneRoot_When_AnotherRootIsMountedIntoItsParent_Then_ItStopsMatching()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, TargetTree());
            var onlyBeforeTheMount = Own.ClassListContains("bg-mark");

            // Act
            using var nested = V.Mount(Target, V.Div(name: "nested"));

            // Assert — null where "own" did not match only: before the other root's row joined it.
            Assert.That(onlyBeforeTheMount ? Own.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AnotherRootMountedBesideAChildInOneRoot_When_ThatRootIsDisposed_Then_TheChildMatchesOnlyAgain()
        {
            // Arrange
            using var targetTree = V.Mount(_targetRoot, TargetTree());
            var nested = V.Mount(Target, V.Div(name: "nested"));
            var notOnlyWhileNested = !Own.ClassListContains("bg-mark");

            // Act
            nested.Dispose();

            // Assert — null where "own" matched only: while the other root's row stood beside it.
            Assert.That(notOnlyWhileNested ? Own.ClassListContains("bg-mark") : (bool?)null, Is.True);
        }
    }
}
