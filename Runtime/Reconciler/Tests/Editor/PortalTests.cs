using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.Portal"/> and its mount-target registry
    /// <see cref="FiberPortalRegistry"/>.
    /// <list type="bullet">
    /// <item>A Portal renders its children into the registered target VisualElement, not in tree order; the
    /// original tree position holds only a hidden (<see cref="DisplayStyle.None"/>) placeholder.</item>
    /// <item>Patching a Portal updates its children in the target in place.</item>
    /// <item>Removing a Portal removes exactly its own contributed children from the target and drops its
    /// placeholder from the tree.</item>
    /// <item>A target that is not registered (at mount or at patch time) logs a warning and renders no children,
    /// while still placing the placeholder.</item>
    /// <item>Multiple Portals targeting one element each own a contiguous slot range in registration order; a
    /// patch touches only its own range, and growing, shrinking, or removing one Portal shifts the following
    /// Portals' ranges accordingly without disturbing their content.</item>
    /// <item>A Portal nested inside another Portal pointed at the same target mounts deferred, so its slot range
    /// begins at the outer Portal's slot end and both contents coexist; outer removal removes the whole chain.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Drives the reconciler directly via <c>Reconcile(root, oldChildren, newChildren)</c> rather than the
    /// <c>V.Mount</c> + <c>[Component]</c> path, because the contract under test is the Portal slot bookkeeping
    /// in the reconciler itself. The registry's static target table is cleared in <see cref="SetUp"/> and
    /// <see cref="TearDown"/> so registrations never leak across tests.
    /// </remarks>
    [TestFixture]
    internal sealed class PortalTests
    {
        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
            FiberPortalRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            FiberPortalRegistry.Clear();
        }

        #region Rendering into the target

        [Test]
        public void Given_RegisteredTarget_When_PortalMounts_Then_ChildrenRenderIntoTarget()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("modal-root", target);
            var children = new VNode[]
            {
                V.Portal("modal-root", children: new VNode[] { V.Label(text: "Portal Content") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(((Label)target.ElementAt(0)).text, Is.EqualTo("Portal Content"),
                "Portal children render into the registered target element");
        }

        [Test]
        public void Given_RegisteredTarget_When_PortalMounts_Then_TreePositionHoldsHiddenPlaceholder()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("modal-root", target);
            var children = new VNode[]
            {
                V.Portal("modal-root", children: new VNode[] { V.Label(text: "Portal Content") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the tree position holds one node");
            Assert.That(_root.ElementAt(0).style.display.value, Is.EqualTo(DisplayStyle.None),
                "The Portal's original tree position holds only a hidden placeholder");
        }

        [Test]
        public void Given_MountedPortal_When_ChildrenPatched_Then_TargetReflectsNewChildren()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("overlay", target);
            var oldChildren = new VNode[]
            {
                V.Portal("overlay", children: new VNode[] { V.Label(text: "Old") }),
            };
            var newChildren = new VNode[]
            {
                V.Portal("overlay", children: new VNode[] { V.Label(text: "New") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            Assume.That(((Label)target.ElementAt(0)).text, Is.EqualTo("Old"), "Precondition: the old child is mounted");

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(((Label)target.ElementAt(0)).text, Is.EqualTo("New"),
                "Patching the Portal updates its child in the target in place");
        }

        [Test]
        public void Given_MountedPortalWithKeyedChildren_When_ChildrenReordered_Then_TargetChildCountIsStable()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("keyed", target);
            var oldChildren = new VNode[]
            {
                V.Portal("keyed", children: new VNode[]
                {
                    V.Label(key: "a", text: "A"),
                    V.Label(key: "b", text: "B"),
                }),
            };
            var newChildren = new VNode[]
            {
                V.Portal("keyed", children: new VNode[]
                {
                    V.Label(key: "b", text: "B Updated"),
                    V.Label(key: "a", text: "A Updated"),
                }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(target.childCount, Is.EqualTo(2),
                "A keyed reorder reuses both children, leaving the target child count unchanged");
        }

        #endregion

        #region Removal and cleanup

        [Test]
        public void Given_MountedPortal_When_PortalRemoved_Then_TargetChildrenAreCleared()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("cleanup-test", target);
            var children = new VNode[]
            {
                V.Portal("cleanup-test", children: new VNode[]
                {
                    V.Label(text: "Will be removed"),
                    V.Div("some-class"),
                }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);
            Assume.That(target.childCount, Is.EqualTo(2), "Precondition: both children are mounted in the target");

            // Act
            _reconciler.Reconcile(_root, children, Array.Empty<VNode>());

            // Assert
            Assert.That(target.childCount, Is.EqualTo(0),
                "Removing the Portal clears its contributed children from the target");
        }

        [Test]
        public void Given_MountedPortal_When_PortalRemoved_Then_PlaceholderLeavesTree()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("cleanup-test", target);
            var children = new VNode[]
            {
                V.Portal("cleanup-test", children: new VNode[] { V.Label(text: "Will be removed") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the placeholder occupies the tree position");

            // Act
            _reconciler.Reconcile(_root, children, Array.Empty<VNode>());

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0), "Removing the Portal drops its placeholder from the tree");
        }

        #endregion

        #region Unregistered target

        [Test]
        public void Given_UnregisteredTarget_When_PortalMounts_Then_LogsWarningAndRendersNoChildren()
        {
            // Arrange
            var children = new VNode[]
            {
                V.Portal("nonexistent", children: new VNode[] { V.Label(text: "Orphan") }),
            };
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"nonexistent\" is not registered. Children will not be rendered.");

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1),
                "An unregistered target still places the placeholder; the warning above proves no children render");
        }

        [Test]
        public void Given_TargetUnregisteredAfterMount_When_PortalPatched_Then_LogsWarning()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("temp", target);
            var oldChildren = new VNode[]
            {
                V.Portal("temp", children: new VNode[] { V.Label(text: "Initial") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            Assume.That(target.childCount, Is.EqualTo(1), "Precondition: the initial child mounted while registered");
            FiberPortalRegistry.Unregister("temp");
            var newChildren = new VNode[]
            {
                V.Portal("temp", children: new VNode[] { V.Label(text: "Updated") }),
            };
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"temp\" is not registered. Children will not be rendered.");

            // Act + Assert — LogAssert.Expect verifies a patch against a now-unregistered target warns
            _reconciler.Reconcile(_root, oldChildren, newChildren);
        }

        #endregion

        #region Multiple Portals sharing a target

        [Test]
        public void Given_TwoPortalsToOneTarget_When_BothMount_Then_ChildrenAppendInRegistrationOrder()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            var children = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[] { V.Label(text: "First") }),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "Second") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(
                (((Label)target.ElementAt(0)).text, ((Label)target.ElementAt(1)).text),
                Is.EqualTo(("First", "Second")),
                "Both Portals append into the shared target in registration order");
        }

        [Test]
        public void Given_TwoPortalsToDistinctTargets_When_BothMount_Then_EachTargetHoldsItsOwnChild()
        {
            // Arrange
            var targetA = new VisualElement();
            var targetB = new VisualElement();
            FiberPortalRegistry.Register("target-a", targetA);
            FiberPortalRegistry.Register("target-b", targetB);
            var children = new VNode[]
            {
                V.Portal("target-a", key: "p-a", children: new VNode[] { V.Label(text: "Content A") }),
                V.Portal("target-b", key: "p-b", children: new VNode[] { V.Label(text: "Content B") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(
                (((Label)targetA.ElementAt(0)).text, ((Label)targetB.ElementAt(0)).text),
                Is.EqualTo(("Content A", "Content B")),
                "Portals route to distinct targets independently");
        }

        [Test]
        public void Given_TwoPortalsToOneTarget_When_FirstPortalPatched_Then_SecondPortalSlotUntouched()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            var oldChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[] { V.Label(text: "P1-First") }),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-First") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var newChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[] { V.Label(text: "P1-Updated") }),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-First") }),
            };

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(
                (((Label)target.ElementAt(0)).text, ((Label)target.ElementAt(1)).text),
                Is.EqualTo(("P1-Updated", "P2-First")),
                "Patching the first Portal touches only its own slot; the second Portal's child stays put");
        }

        [Test]
        public void Given_TwoPortalsToOneTarget_When_FirstPortalGrows_Then_SecondPortalSlotShiftsRight()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            var oldChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[] { V.Label(text: "P1-A") }),
                V.Portal("shared", key: "portal-2", children: new VNode[]
                {
                    V.Label(text: "P2-A"),
                    V.Label(text: "P2-B"),
                }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var newChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[]
                {
                    V.Label(text: "P1-A"),
                    V.Label(text: "P1-B"),
                    V.Label(text: "P1-C"),
                }),
                V.Portal("shared", key: "portal-2", children: new VNode[]
                {
                    V.Label(text: "P2-A"),
                    V.Label(text: "P2-B"),
                }),
            };

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            var texts = new[]
            {
                ((Label)target.ElementAt(0)).text,
                ((Label)target.ElementAt(1)).text,
                ((Label)target.ElementAt(2)).text,
                ((Label)target.ElementAt(3)).text,
                ((Label)target.ElementAt(4)).text,
            };
            Assert.That(texts, Is.EqualTo(new[] { "P1-A", "P1-B", "P1-C", "P2-A", "P2-B" }),
                "Growing the first Portal shifts the second Portal's slot right so its children follow the grown range");
        }

        [Test]
        public void Given_TwoPortalsToOneTarget_When_FirstPortalEmpties_Then_SecondPortalCollapsesToHead()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            var oldChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[]
                {
                    V.Label(text: "P1-A"),
                    V.Label(text: "P1-B"),
                }),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-A") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var newChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: Array.Empty<VNode>()),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-A") }),
            };

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(target.childCount == 1 && ((Label)target.ElementAt(0)).text == "P2-A", Is.True,
                "Emptying the first Portal collapses the second Portal's slot to the head with its child intact");
        }

        [Test]
        public void Given_TwoPortalsToOneTarget_When_FirstPortalRemoved_Then_SecondPortalSurvivesAtHead()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            var oldChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-1", children: new VNode[]
                {
                    V.Label(text: "P1-A"),
                    V.Label(text: "P1-B"),
                }),
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-A") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var newChildren = new VNode[]
            {
                V.Portal("shared", key: "portal-2", children: new VNode[] { V.Label(text: "P2-A") }),
            };

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(target.childCount == 1 && ((Label)target.ElementAt(0)).text == "P2-A", Is.True,
                "Removing the first Portal drops only its slot range; the surviving Portal's child shifts to the head");
        }

        #endregion

        #region Nested Portals to the same target

        [Test]
        public void Given_OuterPortalContainsInnerPortalSameTarget_When_Mounted_Then_BothContentsCoexistInTarget()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("nested-target", target);
            var children = new VNode[]
            {
                V.Portal("nested-target", key: "outer", children: new VNode[]
                {
                    V.Label(text: "A"),
                    V.Portal("nested-target", key: "inner", children: new VNode[] { V.Label(text: "B") }),
                }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(
                (target.childCount, ((Label)target.ElementAt(0)).text, ((Label)target.ElementAt(2)).text),
                Is.EqualTo((3, "A", "B")),
                "Outer contributes A + inner's placeholder, inner mounts deferred at the outer slot end, so the inner B lands at the tail");
        }

        [Test]
        public void Given_NestedPortalsSameTarget_When_OuterUnmounts_Then_WholeChainIsRemoved()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("nested-cleanup", target);
            var withOuterContainingInner = new VNode[]
            {
                V.Portal("nested-cleanup", key: "outer", children: new VNode[]
                {
                    V.Label(text: "A"),
                    V.Portal("nested-cleanup", key: "inner", children: new VNode[] { V.Label(text: "B") }),
                }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), withOuterContainingInner);
            Assume.That(target.childCount, Is.EqualTo(3), "Precondition: both outer and inner contents are mounted");

            // Act
            _reconciler.Reconcile(_root, withOuterContainingInner, Array.Empty<VNode>());

            // Assert
            Assert.That(target.childCount, Is.EqualTo(0),
                "Outer removal cleans its own slot range and chains through the inner placeholder to remove inner's range");
        }

        #endregion

        #region Edge cases

        [Test]
        public void Given_RegisteredTarget_When_PortalHasNoChildren_Then_TargetStaysEmpty()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("empty", target);
            var children = new VNode[] { V.Portal("empty") };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(target.childCount, Is.EqualTo(0), "A childless Portal contributes nothing to its target");
        }

        [Test]
        public void Given_RegisteredTarget_When_PortalHasNoChildren_Then_PlaceholderStillOccupiesTree()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("empty", target);
            var children = new VNode[] { V.Portal("empty") };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1),
                "A childless Portal still places its placeholder in the tree position");
        }

        #endregion

        #region Registry contract

        [Test]
        public void Given_RegisteredId_When_GetCalled_Then_ReturnsTheTarget()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            FiberPortalRegistry.Register("test-id", element);

            // Assert
            Assert.That(FiberPortalRegistry.Get("test-id"), Is.SameAs(element),
                "Get returns the exact element registered under the id");
        }

        [Test]
        public void Given_RegisteredId_When_Queried_Then_IsRegisteredIsTrue()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            FiberPortalRegistry.Register("test-id", element);

            // Assert
            Assert.That(FiberPortalRegistry.IsRegistered("test-id"), Is.True,
                "IsRegistered reports a registered id as present");
        }

        [Test]
        public void Given_RegisteredId_When_Unregistered_Then_GetReturnsNull()
        {
            // Arrange
            var element = new VisualElement();
            FiberPortalRegistry.Register("test-id", element);

            // Act
            FiberPortalRegistry.Unregister("test-id");

            // Assert
            Assert.That(FiberPortalRegistry.Get("test-id"), Is.Null, "Get returns null after the id is unregistered");
        }

        [Test]
        public void Given_RegisteredId_When_Unregistered_Then_IsRegisteredIsFalse()
        {
            // Arrange
            var element = new VisualElement();
            FiberPortalRegistry.Register("test-id", element);

            // Act
            FiberPortalRegistry.Unregister("test-id");

            // Assert
            Assert.That(FiberPortalRegistry.IsRegistered("test-id"), Is.False,
                "IsRegistered reports an unregistered id as absent");
        }

        [Test]
        public void Given_UnknownId_When_GetCalled_Then_ReturnsNull()
        {
            // Act + Assert
            Assert.That(FiberPortalRegistry.Get("unknown"), Is.Null, "Get returns null for an id that was never registered");
        }

        [Test]
        public void Given_AlreadyRegisteredId_When_RegisteredAgain_Then_LogsWarning()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            FiberPortalRegistry.Register("dup", first);
            LogAssert.Expect(LogType.Warning, "[FiberPortalRegistry] Id \"dup\" is already registered. Overwriting.");

            // Act + Assert — LogAssert.Expect verifies a duplicate registration warns
            FiberPortalRegistry.Register("dup", second);
        }

        [Test]
        public void Given_AlreadyRegisteredId_When_RegisteredAgain_Then_OverwritesWithNewTarget()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            FiberPortalRegistry.Register("dup", first);
            LogAssert.Expect(LogType.Warning, "[FiberPortalRegistry] Id \"dup\" is already registered. Overwriting.");

            // Act
            FiberPortalRegistry.Register("dup", second);

            // Assert
            Assert.That(FiberPortalRegistry.Get("dup"), Is.SameAs(second),
                "A duplicate registration overwrites the previous target with the new one");
        }

        #endregion

        #region has- reactivity across a Portal boundary

        [Test]
        public void Given_HasAncestorOfPortalTarget_When_SweepRunsOutsideThatRegion_Then_PortalTargetIsSeeded()
        {
            // A has-[.flag]: element H holds the Portal target T as a DOM descendant. A Portal child carrying
            // `.flag` mounts into T deferred (after H's own post-children pass), so H is left stale. The
            // settled-flush sweep runs scoped to the Portal OWNER's region (regionRoot = _root), which does NOT
            // contain H on its ancestor chain — only seeding the walk from every active Portal target reaches H.
            var beforeTree = new VNode[]
            {
                V.Div(className: "has-[.flag]:bg-mark", name: "H", children: new VNode[] { V.Div(name: "T") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), beforeTree);
            var hasElement = _root.Q<VisualElement>("H");
            FiberPortalRegistry.Register("t", _root.Q<VisualElement>("T"));
            var withPortal = new VNode[]
            {
                V.Div(className: "has-[.flag]:bg-mark", name: "H", children: new VNode[] { V.Div(name: "T") }),
                V.Portal("t", children: new VNode[] { V.Div(className: "flag", name: "pc") }),
            };
            _reconciler.Reconcile(_root, beforeTree, withPortal);
            Assume.That(hasElement.ClassListContains("bg-mark"), Is.False,
                "Precondition: H is stale because the deferred Portal mount ran after H's own post-children pass");

            // Act — the sweep runs with a region (the Portal owner's MountPoint) that excludes H.
            FiberNodePatcher.RefreshHasVariants(_reconciler.Context, _root);

            // Assert
            Assert.That(hasElement.ClassListContains("bg-mark"), Is.True,
                "Seeding the sweep from the Portal target re-derives a has- ancestor outside the flushing region");
        }

        #endregion
    }
}
