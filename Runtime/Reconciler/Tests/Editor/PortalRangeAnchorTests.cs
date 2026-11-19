using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a portal's range is read from the rows it holds, so a row put ahead of the range by
    /// something that never told the portal — other code, or another mounted tree — does not make its next
    /// patch land on somebody else's row.
    /// </summary>
    internal sealed class PortalRangeAnchorTests
    {
        private Reconciler _portalTree = null!;
        private Reconciler _containerTree = null!;
        private VisualElement _portalRoot = null!;
        private VisualElement _containerRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _portalTree = new Reconciler();
            _containerTree = new Reconciler();
            _portalRoot = new VisualElement();
            _containerRoot = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _portalTree.Dispose();
            _containerTree.Dispose();
        }

        // A Div rather than a Text, whose pooled Label a remount would hand straight back to the next rent.
        private static VNode[] PortalInto(VisualElement container, string child) =>
            new VNode[] { V.Portal(container, children: new VNode?[] { V.Div(name: child) }) };

        private static string Names(VisualElement parent) =>
            string.Join("|", parent.Children().Select(child => child.name));

        [Test]
        public void Given_ARowOtherCodePutAheadOfThePortalsRange_When_ThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            var container = new VisualElement();
            container.Add(new VisualElement { name = "own" });
            var before = PortalInto(container, "p");
            _portalTree.Reconcile(_portalRoot, Array.Empty<VNode>(), before);
            container.Insert(0, new VisualElement { name = "foreign" });

            // Act
            _portalTree.Reconcile(_portalRoot, before, PortalInto(container, "p2"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("foreign|own|p2"));
        }

        private static VNode[] ContainerWith(params string[] rows) =>
            new VNode[]
            {
                V.Div(name: "container", key: "container",
                    children: rows.Select(row => (VNode?)V.Div(name: row, key: row)).ToArray()),
            };

        [Test]
        public void Given_ARowAnotherTreePutAheadOfThePortalsRange_When_ThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange — the container belongs to a tree of its own, whose reconciler records nothing of the
            // portal the other tree mounts into it.
            var containerBefore = ContainerWith("a");
            _containerTree.Reconcile(_containerRoot, Array.Empty<VNode>(), containerBefore);
            var container = _containerRoot.Q<VisualElement>("container");
            var before = PortalInto(container, "p");
            _portalTree.Reconcile(_portalRoot, Array.Empty<VNode>(), before);
            _containerTree.Reconcile(_containerRoot, containerBefore, ContainerWith("z", "a"));

            // Act
            _portalTree.Reconcile(_portalRoot, before, PortalInto(container, "p2"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("z|a|p2"));
        }
    }
}
