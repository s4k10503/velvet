using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a Fragment participates in child reconciliation as a transparent grouping node.
    /// <list type="bullet">
    /// <item>A Fragment's children are flattened into the parent's child slots in order, around its
    /// siblings; nested Fragments flatten recursively.</item>
    /// <item>An unkeyed Fragment groups its children but adds no identity scope: its children reconcile
    /// by their position within the parent.</item>
    /// <item>A keyed Fragment scopes its children's identity by the Fragment key, so children under
    /// Fragments with different keys never collide even when their own keys overlap; reordering keyed
    /// Fragments moves each Fragment's children as a unit while preserving their element instances.</item>
    /// <item>Nested keyed Fragments compose their scopes, so an inner Fragment's children keep identity
    /// when an outer Fragment is reordered.</item>
    /// <item>A Fragment key may not contain the reserved NUL (U+0000) scope delimiter; the factory
    /// rejects such a key with an <see cref="ArgumentException"/> naming the key parameter.</item>
    /// <item>Reconciliation never mutates a child VNode: a Fragment's scoped key lives in a per-pass
    /// side channel, so the user's VNode.Key stays the supplied value and the VNode is safe to reuse.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerFragmentTests : ReconcilerTestFixture
    {
        private string[] LabelTexts()
        {
            var texts = new string[Root.childCount];
            for (var i = 0; i < Root.childCount; i++)
            {
                texts[i] = ((Label)Root.ElementAt(i)).text;
            }
            return texts;
        }

        [Test]
        public void Given_FragmentAmongSiblings_When_Reconciled_Then_ChildrenFlattenedInOrder()
        {
            // Arrange
            var newTree = new VNode[]
            {
                V.Label(text: "before"),
                V.Fragment(new VNode[]
                {
                    V.Label(text: "frag-a"),
                    V.Label(text: "frag-b"),
                }),
                V.Label(text: "after"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "before", "frag-a", "frag-b", "after" }));
        }

        [Test]
        public void Given_NestedFragments_When_Reconciled_Then_FlattenedRecursively()
        {
            // Arrange
            var newTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Label(text: "outer"),
                    V.Fragment(new VNode[]
                    {
                        V.Label(text: "inner-a"),
                        V.Label(text: "inner-b"),
                    }),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "outer", "inner-a", "inner-b" }));
        }

        [Test]
        public void Given_KeyedFragmentChildren_When_InnerKeysReordered_Then_ChildrenFollowKeys()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Label(text: "A", key: "a"),
                    V.Label(text: "B", key: "b"),
                }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            var newTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Label(text: "B-updated", key: "b"),
                    V.Label(text: "A-updated", key: "a"),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "B-updated", "A-updated" }));
        }

        [Test]
        public void Given_KeyedFragments_When_Reordered_Then_ChildVisualElementsMoveAsAUnit()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Label(text: "row-1-a"),
                    V.Label(text: "row-1-b"),
                }, key: "row-1"),
                V.Fragment(new VNode[]
                {
                    V.Label(text: "row-2-a"),
                    V.Label(text: "row-2-b"),
                }, key: "row-2"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var row1A = Root.ElementAt(0);
            var row1B = Root.ElementAt(1);
            var row2A = Root.ElementAt(2);
            var row2B = Root.ElementAt(3);

            var newTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Label(text: "row-2-a"),
                    V.Label(text: "row-2-b"),
                }, key: "row-2"),
                V.Fragment(new VNode[]
                {
                    V.Label(text: "row-1-a"),
                    V.Label(text: "row-1-b"),
                }, key: "row-1"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (Root.ElementAt(0), Root.ElementAt(1), Root.ElementAt(2), Root.ElementAt(3)),
                Is.EqualTo((row2A, row2B, row1A, row1B)),
                "Each keyed Fragment's children move together and keep their element instances");
        }

        [Test]
        public void Given_KeyedFragmentsWithOverlappingInnerKeys_When_Reordered_Then_IdentityScopedByFragmentKey()
        {
            // Arrange — two keyed Fragments contain a child with the same inner key "x"
            var oldTree = new VNode[]
            {
                V.Fragment(new VNode[] { V.Label(text: "row-1-x", key: "x") }, key: "row-1"),
                V.Fragment(new VNode[] { V.Label(text: "row-2-x", key: "x") }, key: "row-2"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var row1X = Root.ElementAt(0);
            var row2X = Root.ElementAt(1);

            var newTree = new VNode[]
            {
                V.Fragment(new VNode[] { V.Label(text: "row-2-x", key: "x") }, key: "row-2"),
                V.Fragment(new VNode[] { V.Label(text: "row-1-x", key: "x") }, key: "row-1"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the Fragment key scopes identity, so the two "x" children do not collide
            Assert.That((Root.ElementAt(0), Root.ElementAt(1)), Is.EqualTo((row2X, row1X)));
        }

        [Test]
        public void Given_UnkeyedFragment_When_ChildrenChange_Then_PositionalPairingPatches()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "before"),
                V.Fragment(new VNode[]
                {
                    V.Label(text: "a"),
                    V.Label(text: "b"),
                }),
                V.Label(text: "after"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            var newTree = new VNode[]
            {
                V.Label(text: "before"),
                V.Fragment(new VNode[]
                {
                    V.Label(text: "A"),
                    V.Label(text: "B"),
                }),
                V.Label(text: "after"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "before", "A", "B", "after" }));
        }

        [Test]
        public void Given_NestedKeyedFragments_When_OuterReordered_Then_InnerChildrenKeepIdentity()
        {
            // Arrange — outer scope composes with inner key, so reordering the outer Fragments keeps the
            // inner Fragments' children identified through the move.
            var oldTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Fragment(new VNode[] { V.Label(text: "outer-1-inner-a") }, key: "inner"),
                }, key: "outer-1"),
                V.Fragment(new VNode[]
                {
                    V.Fragment(new VNode[] { V.Label(text: "outer-2-inner-a") }, key: "inner"),
                }, key: "outer-2"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var outer1Inner = Root.ElementAt(0);
            var outer2Inner = Root.ElementAt(1);

            var newTree = new VNode[]
            {
                V.Fragment(new VNode[]
                {
                    V.Fragment(new VNode[] { V.Label(text: "outer-2-inner-a") }, key: "inner"),
                }, key: "outer-2"),
                V.Fragment(new VNode[]
                {
                    V.Fragment(new VNode[] { V.Label(text: "outer-1-inner-a") }, key: "inner"),
                }, key: "outer-1"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the same inner key "inner" under each outer did not collide across siblings
            Assert.That((Root.ElementAt(0), Root.ElementAt(1)), Is.EqualTo((outer2Inner, outer1Inner)));
        }

        [Test]
        public void Given_FragmentKeyContainingNul_When_Created_Then_RejectedNamingKeyParameter()
        {
            // Act + Assert — NUL is reserved as the internal scope delimiter, so the factory rejects it
            var ex = Assert.Throws<ArgumentException>(() =>
                V.Fragment(new VNode[] { V.Label(text: "x") }, key: "row\0one"));
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        [Test]
        public void Given_FragmentScopedChild_When_Reconciled_Then_ChildVNodeKeyStaysUserSupplied()
        {
            // Arrange — the scoped key lives in a per-pass side channel, never on the VNode itself
            var sharedLabel = V.Label(text: "shared", key: "shared-key");
            var tree = new VNode[]
            {
                V.Fragment(new VNode[] { sharedLabel }, key: "row-1"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(sharedLabel.Key, Is.EqualTo("shared-key"),
                "Reconcile does not mutate VNode state, so the child stays reusable across passes");
        }
    }

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
                // Joined rather than compared as arrays: NUnit's tuple comparison matches a nested array by
                // reference, so a tuple of string[] passes only when both sides are the same instance.
                var collapsedOrder = string.Join(",", HostChildNames(_root));

                // Act — the fragment re-expands, renting its labels back from the pool.
                store.Set(0);
                scheduler.DrainImmediateForTest();

                // Assert — the children are restored in order ahead of the live sibling, with no leftover
                // duplicates. The collapsed shape is asserted with the restored one: a fragment that never
                // collapsed already reads the restored order, and pins neither the collapse nor the re-expand.
                Assert.That((collapsedOrder, string.Join(",", HostChildNames(_root))),
                    Is.EqualTo(("live", "fa,fb,live")));
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
