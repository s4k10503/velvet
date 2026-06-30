using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the slot bookkeeping that keeps a LIVE sibling correctly positioned when an adjacent
    /// fragment collapses to empty or a keyed run shrinks. Flattening a fragment expands its children into the
    /// parent's slot range; when that range shrinks the reconciler must rebase the following siblings' pending slot
    /// start (<c>RebasePendingSlotStart</c>) so the survivor is neither dropped, duplicated, nor left stranded at a
    /// stale index. These cases sit between the keyed-reorder tests and the positional-patch tests and are exercised
    /// end-to-end through a store-driven re-render. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class FragmentCollapseAndShrinkReconcileTests
    {
        private readonly record struct ModeState(int Mode);

        private sealed class ModeStore : Store<ModeState>
        {
            public ModeStore() : base(new ModeState(0)) { }
            public void Set(int mode) => SetState(_ => new ModeState(mode));
            protected override void ResetCore() => SetState(_ => new ModeState(0));
        }

        private static ModeStore s_store;
        private static Func<int, VNode> s_render;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_render = null;
        }

        [Component]
        private static VNode Host()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            return s_render(mode);
        }

        private FiberBatchScheduler Mount(Func<int, VNode> render, out ModeStore store)
        {
            s_render = render;
            store = new ModeStore();
            s_store = store;
            var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            return mounted.Root.Reconciler.Context.BatchScheduler;
        }

        private static string[] HostChildNames(VisualElement root)
            => root.Q<VisualElement>("host").Children().Select(c => c.name).ToArray();

        // A fragment of two labels followed by a live sibling label; mode 1 empties the fragment.
        private static VNode FragmentBesideLive(int mode) => V.Div(name: "host", children: new VNode[]
        {
            V.Fragment(mode == 0
                ? new VNode[] { V.Label(name: "fa", text: "a"), V.Label(name: "fb", text: "b") }
                : Array.Empty<VNode>()),
            V.Label(name: "live", text: "L"),
        });

        // Fragment collapses to empty beside a live sibling

        [Test]
        public void Given_AFragmentBesideALiveSibling_When_TheFragmentCollapsesToEmpty_Then_ItsChildrenAreRemoved()
        {
            // Arrange — a fragment [a,b] flattened ahead of a live sibling.
            var scheduler = Mount(FragmentBesideLive, out var store);
            using (store)
            {
                Assume.That(_root.Q<Label>("fa"), Is.Not.Null, "Precondition: the fragment child is mounted");

                // Act — the fragment collapses to empty.
                store.Set(1);
                scheduler.DrainImmediateForTest();

                // Assert — the fragment's child is removed.
                Assert.IsNull(_root.Q<Label>("fa"));
            }
        }

        [Test]
        public void Given_AFragmentBesideALiveSibling_When_TheFragmentCollapsesToEmpty_Then_TheLiveSiblingSurvivesAsTheOnlyChild()
        {
            // Arrange — a fragment [a,b] flattened ahead of a live sibling.
            var scheduler = Mount(FragmentBesideLive, out var store);
            using (store)
            {
                // Act — the fragment collapses to empty.
                store.Set(1);
                scheduler.DrainImmediateForTest();

                // Assert — only the live sibling remains, correctly positioned (not dropped or duplicated).
                Assert.AreEqual(new[] { "live" }, HostChildNames(_root));
            }
        }

        [Test]
        public void Given_ACollapsedFragment_When_ItReExpandsBesideTheLiveSibling_Then_OrderIsRestoredWithoutDuplication()
        {
            // Arrange — the fragment collapsed to empty (its labels returned to the pool).
            var scheduler = Mount(FragmentBesideLive, out var store);
            using (store)
            {
                store.Set(1);
                scheduler.DrainImmediateForTest();
                Assume.That(HostChildNames(_root), Is.EqualTo(new[] { "live" }), "Precondition: collapsed to the live sibling");

                // Act — the fragment re-expands, renting its labels back from the pool.
                store.Set(0);
                scheduler.DrainImmediateForTest();

                // Assert — the children are restored in order ahead of the live sibling, with no leftover duplicates.
                Assert.AreEqual(new[] { "fa", "fb", "live" }, HostChildNames(_root));
            }
        }

        // Keyed run shrinks ahead of a trailing live sibling

        private static readonly string[] s_full = { "a", "b", "c", "d" };

        // A keyed run of labels followed by a trailing live sibling; mode 1 shrinks the run to its first item.
        private static VNode KeyedRunBeforeLive(int mode)
        {
            var keys = mode == 0 ? s_full : new[] { "a" };
            var children = keys
                .Select(k => (VNode)V.Label(key: k, name: "item-" + k, text: k))
                .Append(V.Label(name: "live", text: "L"))
                .ToArray();
            return V.Div(name: "host", children: children);
        }

        [Test]
        public void Given_AKeyedRunBeforeATrailingSibling_When_TheRunShrinks_Then_TheDroppedItemsAreRemoved()
        {
            // Arrange — a keyed run [a,b,c,d] ahead of a trailing live sibling.
            var scheduler = Mount(KeyedRunBeforeLive, out var store);
            using (store)
            {
                Assume.That(_root.Q<Label>("item-d"), Is.Not.Null, "Precondition: the run's tail item is mounted");

                // Act — the run shrinks to [a].
                store.Set(1);
                scheduler.DrainImmediateForTest();

                // Assert — the dropped tail item is removed.
                Assert.IsNull(_root.Q<Label>("item-d"));
            }
        }

        [Test]
        public void Given_AKeyedRunBeforeATrailingSibling_When_TheRunShrinks_Then_TheTrailingSiblingStaysLast()
        {
            // Arrange — a keyed run [a,b,c,d] ahead of a trailing live sibling.
            var scheduler = Mount(KeyedRunBeforeLive, out var store);
            using (store)
            {
                // Act — the run shrinks to [a].
                store.Set(1);
                scheduler.DrainImmediateForTest();

                // Assert — the trailing sibling stays last, rebased correctly behind the surviving item.
                Assert.AreEqual(new[] { "item-a", "live" }, HostChildNames(_root));
            }
        }
    }
}
