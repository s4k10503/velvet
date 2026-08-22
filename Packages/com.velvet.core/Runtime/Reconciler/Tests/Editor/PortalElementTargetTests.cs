using System;
using System.Linq;
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
    /// <item>Two portals sharing one container each own their own slot range, and an unmount takes only
    /// that range — the container itself belongs to the caller.</item>
    /// <item>Unmounting releases the container from the synthetic-bubbling bridge table, which a caller's
    /// own container makes visible: it dies with the row that rendered it.</item>
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
        public void Given_two_portals_on_one_container_When_both_mount_Then_each_keeps_its_own_slot_range()
        {
            // Arrange
            var shared = new VisualElement();
            var tree = new VNode[]
            {
                V.Portal(shared, children: new VNode?[] { V.Div(name: "first") }),
                V.Portal(shared, children: new VNode?[] { V.Div(name: "second") }),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert — a second portal that appended over the first's range would leave one child, not two.
            Assert.That(
                string.Join("|", shared.Children().Select(c => c.name)),
                Is.EqualTo("first|second"));
        }

        [Test]
        public void Given_a_mounted_portal_When_it_unmounts_Then_only_its_own_slot_range_leaves_the_container()
        {
            // Arrange — the container belongs to the caller, so what it held before the portal must survive.
            var container = new VisualElement();
            container.Add(new VisualElement { name = "caller-owned" });
            var tree = Tree(container, "hello");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Act
            _reconciler.Reconcile(_root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(
                (container.childCount, container.ElementAt(0).name),
                Is.EqualTo((1, "caller-owned")));
        }

        [Test]
        public void Given_a_mounted_portal_When_it_unmounts_Then_the_container_leaves_the_bridge_table()
        {
            // Arrange
            var container = new VisualElement();
            var tree = Tree(container, "hello");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            var attachedWhileMounted = _reconciler.Context.SamePanelPortalBridges.Count;

            // Act
            _reconciler.Reconcile(_root, tree, Array.Empty<VNode>());

            // Assert — the mounted count is folded in, since a bridge that never attached would otherwise
            // satisfy the release reading on its own.
            Assert.That(
                (attachedWhileMounted, _reconciler.Context.SamePanelPortalBridges.Count),
                Is.EqualTo((1, 0)));
        }
    }
}
