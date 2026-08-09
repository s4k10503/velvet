using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the element-valued <see cref="V.Portal"/> form — the one that takes the
    /// container itself rather than an id published in the process-wide registry.
    /// <list type="bullet">
    /// <item>Children mount into the container the caller passed, with no registration.</item>
    /// <item>A different container on a later render moves them: the old container is emptied and the new
    /// one receives them, which is what <c>createPortal</c> does and where this differs from a registry
    /// target, whose id resolves once at mount and is then held.</item>
    /// <item>The same container on a later render patches in place rather than remounting.</item>
    /// <item>Two containers do not share a namespace, so two trees cannot collide the way two
    /// registrations of one id do.</item>
    /// </list>
    /// </summary>
    internal sealed class PortalElementTargetTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        // A Div rather than a Text: a Text mounts a pooled Label, and a remount returns it to a LIFO pool
        // that the very next rent pops, so an identity reading cannot tell a patch from a remount. A Div's
        // element is never pooled.
        private static VNode[] Tree(VisualElement target, string name) =>
            new VNode[] { V.Portal(target, children: new VNode?[] { V.Div(name: name) }) };

        [Test]
        public void Given_a_container_the_caller_holds_When_a_portal_targets_it_Then_the_children_mount_there()
        {
            // Arrange
            var container = new VisualElement();
            var tree = Tree(container, "hello");

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert — the count keeps the reading load-bearing: an unresolved target renders nothing.
            Assert.That(
                (container.childCount, container.ElementAt(0).name),
                Is.EqualTo((1, "hello")));
        }

        [Test]
        public void Given_a_mounted_portal_When_the_container_changes_Then_the_children_move_to_the_new_one()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            var before = Tree(first, "hello");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, Tree(second, "hello"));

            // Assert
            Assert.That(
                (first.childCount, second.childCount, second.ElementAt(0).name),
                Is.EqualTo((0, 1, "hello")));
        }

        [Test]
        public void Given_a_mounted_portal_When_only_the_children_change_Then_the_same_element_is_patched()
        {
            // Arrange
            var container = new VisualElement();
            var before = Tree(container, "hello");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var mounted = container.ElementAt(0);

            // Act
            _reconciler.Reconcile(_root, before, Tree(container, "goodbye"));

            // Assert — identity separates a patch from the remount the container-change case takes.
            Assert.That(
                (ReferenceEquals(container.ElementAt(0), mounted), mounted.name),
                Is.EqualTo((true, "goodbye")));
        }

        [Test]
        public void Given_two_portals_on_two_containers_When_both_mount_Then_neither_sees_the_other()
        {
            // Arrange
            var left = new VisualElement();
            var right = new VisualElement();
            var tree = new VNode[]
            {
                V.Portal(left, children: new VNode?[] { V.Div(name: "left") }),
                V.Portal(right, children: new VNode?[] { V.Div(name: "right") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (left.ElementAt(0).name, right.ElementAt(0).name),
                Is.EqualTo(("left", "right")));
        }
    }
}
