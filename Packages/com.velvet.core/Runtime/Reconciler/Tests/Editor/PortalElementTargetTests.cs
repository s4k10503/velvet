using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

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

        [Test]
        public void Given_a_mounted_portal_When_it_unmounts_Then_its_container_carries_no_bridge()
        {
            // Arrange
            var container = new VisualElement();
            var tree = Tree(container, "hello");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            var bridgedWhileMounted = _reconciler.Context.EventManager.IsBridgeAnchor(container);

            // Act
            _reconciler.Reconcile(_root, tree, Array.Empty<VNode>());

            // Assert — the mounted reading is folded in, as in the bridge-table case above.
            Assert.That(
                (bridgedWhileMounted, _reconciler.Context.EventManager.IsBridgeAnchor(container)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_a_portal_of_three_rows_in_a_container_on_a_panel_When_it_unmounts_Then_the_rows_leave_the_panel_first_to_last()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            var container = new VisualElement();
            host.Root.Add(container);
            var tree = new VNode[]
            {
                V.Portal(container, children: new VNode?[] { V.Div(name: "a"), V.Div(name: "b"), V.Div(name: "c") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            var left = new List<string>();
            foreach (var row in container.Children())
            {
                row.RegisterCallback<DetachFromPanelEvent>(_ => left.Add(row.name));
            }

            // Act
            _reconciler.Reconcile(_root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(string.Join("|", left), Is.EqualTo("a|b|c"));
        }

        private static VisualElement s_container;
        private static bool s_innerFirst;
        private static StateUpdater<bool> s_setOuterShown;
        private static StateUpdater<int> s_setCount;

        [Component(Compiler = false)]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            return V.Label(text: "count " + count);
        }

        // An outer Portal of two rows and a Portal into the same container, beside a Portal whose Component mounts
        // inline on that container.
        [Component(Compiler = false)]
        private static VNode NestedPortalBesideACounter()
        {
            var (outerShown, setOuterShown) = Hooks.UseState(true);
            s_setOuterShown = setOuterShown;
            var inner = V.Portal(s_container, children: new VNode?[] { V.Div(name: "b") }, key: "inner");
            var rows = s_innerFirst
                ? new VNode?[] { inner, V.Div(name: "a1"), V.Div(name: "a2") }
                : new VNode?[] { V.Div(name: "a1"), V.Div(name: "a2"), inner };
            return V.Fragment(
                outerShown ? V.Portal(s_container, children: rows, key: "outer") : null,
                V.Portal(s_container, children: new VNode?[] { V.Component(Counter, key: "counter") }, key: "beside"));
        }

        // Mounts the outer Portal with its nested one first or last among its rows, then unmounts it.
        private static MountedTree MountThenUnmountTheOuter(bool innerFirst)
        {
            s_container = new VisualElement();
            s_innerFirst = innerFirst;
            var mounted = V.Mount(new VisualElement(), V.Component(NestedPortalBesideACounter, key: "root"));
            s_setOuterShown.Invoke(false);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static string RowsOf(VisualElement container)
            => string.Join("|", container.Children().Select(row => row is Label label ? label.text : row.name));

        // GREEN_ON_BASE(characterization): the base takes the nested Portal out before the outer's rows ahead of it.
        // The Component beside them then keeps addressing its own row.
        [Test]
        public void Given_a_portal_whose_last_row_is_a_portal_into_the_same_container_When_it_unmounts_and_a_component_beside_it_renders_again_Then_the_component_patches_its_own_row()
        {
            // Arrange
            using var mounted = MountThenUnmountTheOuter(innerFirst: false);

            // Act
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(RowsOf(s_container), Is.EqualTo("count 1"));
        }

        [Test]
        public void Given_a_portal_whose_first_row_is_a_portal_into_the_same_container_When_it_unmounts_and_a_component_beside_it_renders_again_Then_the_component_patches_its_own_row()
        {
            // Arrange
            using var mounted = MountThenUnmountTheOuter(innerFirst: true);

            // Act
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(RowsOf(s_container), Is.EqualTo("count 1"));
        }
    }
}
