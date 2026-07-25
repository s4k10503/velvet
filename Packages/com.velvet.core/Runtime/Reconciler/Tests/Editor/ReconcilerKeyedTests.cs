using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies key-based child reconciliation, where children are matched by key across renders
    /// instead of by position. The whole sibling list reconciles by key when any sibling carries one.
    /// <list type="bullet">
    /// <item>A keyed child keeps its element instance across add, remove, reorder, and patch: a key
    /// present on both sides is patched in place, a key only in the new tree is created, a key only in
    /// the old tree is removed.</item>
    /// <item>A key whose element type changed is replaced; sibling keys whose type is unchanged keep
    /// their instances and are patched.</item>
    /// <item>A reorder is achieved with the minimum number of DOM moves: the longest increasing
    /// subsequence of retained elements acts as unmoved anchors and only the remaining elements move.
    /// An already-sorted list performs no move and reuses every instance.</item>
    /// <item>Reconciliation runs a head linear scan that stops at the first key mismatch, then resolves
    /// the remainder by map lookup plus the anchor-preserving reorder; a head reorder that breaks the
    /// linear scan still anchors the already-sorted tail.</item>
    /// <item>When keyed and unkeyed siblings mix, each child reconciles by its full sibling index:
    /// keyed siblings occupy an index too, so an unkeyed sibling is patched in place only when it lands
    /// on the same full index, and is recreated when its full index now holds a keyed sibling. Explicit
    /// keys (including Fragment-scoped ones) and positional indices never collide because they are
    /// distinguished by kind.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerKeyedTests : ReconcilerTestFixture
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
        public void Given_KeyedList_When_KeyInserted_Then_NewKeyPlacedInOrder()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "C", key: "c"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
                V.Label(text: "C", key: "c"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void Given_KeyedList_When_KeyRemoved_Then_RemainingKeysKeepOrder()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
                V.Label(text: "C", key: "c"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "C", key: "c"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "A", "C" }));
        }

        [Test]
        public void Given_KeyedList_When_Reordered_Then_ElementsFollowKeysToNewOrder()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
                V.Label(text: "C", key: "c"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "C", key: "c"),
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "C", "A", "B" }));
        }

        [Test]
        public void Given_KeyedList_When_RotatedSoLisBlockMovesToFront_Then_OrderIsCorrect()
        {
            // A rotation whose retained LIS is the whole BLOCK [k0..k3] sitting at the FRONT of the live DOM
            // while the new order wants it at the BACK. The anchors therefore occupy the wrong absolute slots,
            // so an absolute-index reorder (parent.Insert(slotStart + i, e)) drops the two moved elements among
            // the anchors and swaps a neighbouring pair (k3/k4). The insertBefore-relative reorder anchors each
            // move on the already-placed next sibling, so it is correct regardless of where the anchors are.
            // Arrange — establish the live DOM as [4,5,0,1,2,3].
            var domOrder = new VNode[]
            {
                V.Label(text: "4", key: "k4"),
                V.Label(text: "5", key: "k5"),
                V.Label(text: "0", key: "k0"),
                V.Label(text: "1", key: "k1"),
                V.Label(text: "2", key: "k2"),
                V.Label(text: "3", key: "k3"),
            };
            var sorted = new VNode[]
            {
                V.Label(text: "0", key: "k0"),
                V.Label(text: "1", key: "k1"),
                V.Label(text: "2", key: "k2"),
                V.Label(text: "3", key: "k3"),
                V.Label(text: "4", key: "k4"),
                V.Label(text: "5", key: "k5"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), domOrder);
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "4", "5", "0", "1", "2", "3" }));

            // Act
            Reconciler.Reconcile(Root, domOrder, sorted);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0", "1", "2", "3", "4", "5" }));
        }

        [Test]
        public void Given_KeyedList_When_TextChangedUnderSameKeys_Then_EachKeyPatched()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A-old", key: "a"),
                V.Label(text: "B-old", key: "b"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "A-new", key: "a"),
                V.Label(text: "B-new", key: "b"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "A-new", "B-new" }));
        }

        [Test]
        public void Given_KeyedList_When_AddRemoveAndReorderCombined_Then_ResolvesToNewKeyOrder()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
                V.Label(text: "C", key: "c"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "C", key: "c"),
                V.Label(text: "D", key: "d"),
                V.Label(text: "A", key: "a"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "C", "D", "A" }));
        }

        [Test]
        public void Given_KeyedButtonsWithHandlers_When_Reordered_Then_FollowKeysToNewOrder()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Button(text: "A", onClick: () => { }, key: "a"),
                V.Button(text: "B", onClick: () => { }, key: "b"),
            };
            var newTree = new VNode[]
            {
                V.Button(text: "B", onClick: () => { }, key: "b"),
                V.Button(text: "A", onClick: () => { }, key: "a"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (((Button)Root.ElementAt(0)).text, ((Button)Root.ElementAt(1)).text),
                Is.EqualTo(("B", "A")));
        }

        [Test]
        public void Given_SameKey_When_ElementTypeChanges_Then_ElementReplacedWithNewType()
        {
            // Arrange
            var oldTree = new VNode[] { V.Label(text: "A", key: "x") };
            var newTree = new VNode[] { V.Button(text: "A", key: "x") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            Assume.That(Root.ElementAt(0), Is.InstanceOf<Label>(), "Precondition: the key holds a Label");

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(0), Is.InstanceOf<Button>());
        }

        [Test]
        public void Given_SameKey_When_NodeKindChanges_Then_FreshElementCreated()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Div(key: "x", children: new VNode[] { V.Label(text: "child") }),
            };
            var newTree = new VNode[] { V.Label(key: "x", text: "text") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var oldElement = Root.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(0), Is.Not.SameAs(oldElement),
                "A node-kind change under the same key recreates the element");
        }

        [Test]
        public void Given_PartialTypeChange_When_Reconciled_Then_TypeChangedKeyReplaced()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
            };
            var newTree = new VNode[]
            {
                V.Button(text: "A", key: "a"),
                V.Label(text: "B-new", key: "b"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(0), Is.InstanceOf<Button>());
        }

        [Test]
        public void Given_PartialTypeChange_When_Reconciled_Then_UnchangedKeyKeepsInstance()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
            };
            var newTree = new VNode[]
            {
                V.Button(text: "A", key: "a"),
                V.Label(text: "B-new", key: "b"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var elementB = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(1), Is.SameAs(elementB),
                "The key whose type is unchanged keeps its instance and is patched");
        }

        #region LIS minimal-move reorder

        [Test]
        public void Given_OddEvenInterleave_When_Reordered_Then_LisAnchorsReachTargetOrder()
        {
            // Arrange — [A,B,C,D,E] -> [B,D,A,C,E]; LIS {A,C,E} anchors, only B,D move
            var oldTree = Keyed("A", "B", "C", "D", "E");
            var newTree = Keyed("B", "D", "A", "C", "E");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "B", "D", "A", "C", "E" }));
        }

        [Test]
        public void Given_FullReverse_When_Reordered_Then_ReachesReversedOrder()
        {
            // Arrange — [A,B,C,D] -> [D,C,B,A]; LIS {A} anchors, the rest move
            var oldTree = Keyed("A", "B", "C", "D");
            var newTree = Keyed("D", "C", "B", "A");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "D", "C", "B", "A" }));
        }

        [Test]
        public void Given_HeadMovesToTail_When_Reordered_Then_TailAnchorsHoldAndHeadMoves()
        {
            // Arrange — [A,B,C,D,E] -> [B,C,D,E,A]; LIS {B,C,D,E} anchors, only A moves
            var oldTree = Keyed("A", "B", "C", "D", "E");
            var newTree = Keyed("B", "C", "D", "E", "A");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "B", "C", "D", "E", "A" }));
        }

        [Test]
        public void Given_TailMovesToHead_When_Reordered_Then_HeadAnchorsHoldAndTailMoves()
        {
            // Arrange — [A,B,C,D,E] -> [E,A,B,C,D]; LIS {A,B,C,D} anchors, only E moves
            var oldTree = Keyed("A", "B", "C", "D", "E");
            var newTree = Keyed("E", "A", "B", "C", "D");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "E", "A", "B", "C", "D" }));
        }

        [Test]
        public void Given_ReorderWithAddAndRemove_When_Reconciled_Then_StableAnchorsPreserveOrder()
        {
            // Arrange — [A,B,C,D] -> [C,E,A,B]; remove D, add E, LIS {A,B} anchors
            var oldTree = Keyed("A", "B", "C", "D");
            var newTree = Keyed("C", "E", "A", "B");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "C", "E", "A", "B" }));
        }

        [Test]
        public void Given_AlreadySortedKeys_When_Reconciled_Then_EveryInstanceReused()
        {
            // Arrange — [A,B,C,D,E] -> same order with new text; LIS spans all, zero moves
            var oldTree = Keyed("A", "B", "C", "D", "E");
            var newTree = new VNode[]
            {
                V.Label(text: "A-new", key: "a"),
                V.Label(text: "B-new", key: "b"),
                V.Label(text: "C-new", key: "c"),
                V.Label(text: "D-new", key: "d"),
                V.Label(text: "E-new", key: "e"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var saved = CurrentElements();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(CurrentElements(), Is.EqualTo(saved),
                "An already-sorted list reuses every element instance (patch only, no move)");
        }

        [Test]
        public void Given_HeadReorderBreaksLinearScan_When_Reconciled_Then_SortedTailAnchorsAndOnlyHeadMoves()
        {
            // Arrange — [Z,A,B,C] -> [A,B,C,Z]; the head mismatch breaks Pass 1, LIS {A,B,C} anchors
            var oldTree = new VNode[]
            {
                V.Label(text: "Z", key: "z"),
                V.Label(text: "A", key: "a"),
                V.Label(text: "B", key: "b"),
                V.Label(text: "C", key: "c"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "A-new", key: "a"),
                V.Label(text: "B-new", key: "b"),
                V.Label(text: "C-new", key: "c"),
                V.Label(text: "Z-new", key: "z"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var elemZ = Root.ElementAt(0);
            var anchorsBefore = (Root.ElementAt(1), Root.ElementAt(2), Root.ElementAt(3));

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — A,B,C anchors stay put and Z moves to the tail, all instances reused
            Assert.That(
                (Root.ElementAt(0), Root.ElementAt(1), Root.ElementAt(2), Root.ElementAt(3)),
                Is.EqualTo((anchorsBefore.Item1, anchorsBefore.Item2, anchorsBefore.Item3, elemZ)));
        }

        #endregion

        #region Unkeyed sibling positional fallback

        [Test]
        public void Given_UnkeyedSiblingOvertakesKeyed_When_Reconciled_Then_RecreatedNotStateBled()
        {
            // Arrange — old=[A(keyed),p(unkeyed@1)] -> new=[q(unkeyed@0),A(keyed@1)]. The new unkeyed q
            // looks up full index 0, which the old side gave to the keyed A, so q finds no unkeyed match.
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "P"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "Q"),
                V.Label(text: "A", key: "a"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var oldUnkeyed = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — q is a fresh instance, not the old unkeyed element patched in place
            Assert.That(Root.ElementAt(0), Is.Not.SameAs(oldUnkeyed));
        }

        [Test]
        public void Given_UnkeyedSiblingBesideFragmentScopedKey_When_Reconciled_Then_NoFalseCollision()
        {
            // Arrange — a Fragment-scoped key and a bare unkeyed sibling must not collide even if a
            // composed scope string resembles a positional key.
            var oldTree = new VNode[]
            {
                V.Label(text: "Head", key: "head"),
                V.Fragment(key: "__pos", children: new VNode[] { V.Label(text: "inner") }),
                V.Label(text: "plain"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "Head-2", key: "head2"),
                V.Fragment(key: "__pos", children: new VNode[] { V.Label(text: "inner-new") }),
                V.Label(text: "plain-new"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var fragmentChild = Root.ElementAt(1);
            var plainChild = Root.ElementAt(2);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — both the Fragment-scoped inner child and the unkeyed plain sibling kept instances
            Assert.That(
                (Root.ElementAt(1), Root.ElementAt(2)),
                Is.EqualTo((fragmentChild, plainChild)));
        }

        [Test]
        public void Given_KeyedReorderExposesUnkeyedTail_When_Reconciled_Then_TailPatchedNotRecreated()
        {
            // Arrange — old=[x*,a*,plain] -> new=[a*,x*,plain]; the head reorder breaks Pass 1 and the
            // unkeyed tail occupies the same full index, so it is patched in place.
            var oldTree = new VNode[]
            {
                V.Label(text: "X", key: "x"),
                V.Label(text: "A", key: "a"),
                V.Label(text: "P"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "X", key: "x"),
                V.Label(text: "P-new"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var plainElement = Root.ElementAt(2);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(2), Is.SameAs(plainElement),
                "The unkeyed tail at the same full index is patched, not destroyed and recreated");
        }

        [Test]
        public void Given_KeyedHeadChangeWithTwoUnkeyedSiblings_When_Reconciled_Then_EachPatchedByPosition()
        {
            // Arrange — old=[k*,p0,p1] -> new=[k2*,p0,p1]; the keyed-head change breaks Pass 1, so both
            // unkeyed siblings patch their own full index.
            var oldTree = new VNode[]
            {
                V.Label(text: "K", key: "k"),
                V.Label(text: "P0"),
                V.Label(text: "P1"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "K", key: "k2"),
                V.Label(text: "P0-new"),
                V.Label(text: "P1-new"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var p0 = Root.ElementAt(1);
            var p1 = Root.ElementAt(2);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((Root.ElementAt(1), Root.ElementAt(2)), Is.EqualTo((p0, p1)),
                "Each unkeyed sibling patched its own positional slot");
        }

        [Test]
        public void Given_KeyedHeadChangeWithUnkeyedSiblingRemoved_When_Reconciled_Then_SurvivorPatched()
        {
            // Arrange — old=[k*,p0,p1] -> new=[k2*,p0]; the survivor at full index 1 is patched while
            // the absent index 2 is removed.
            var oldTree = new VNode[]
            {
                V.Label(text: "K", key: "k"),
                V.Label(text: "P0"),
                V.Label(text: "P1"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "K", key: "k2"),
                V.Label(text: "P0-new"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var p0 = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(1), Is.SameAs(p0),
                "The surviving unkeyed sibling keeps its instance; the absent slot is removed");
        }

        [Test]
        public void Given_KeyedHeadChangeWithUnkeyedSiblingAdded_When_Reconciled_Then_ExistingPatched()
        {
            // Arrange — old=[k*,p0] -> new=[k2*,p0,p1]; full index 1 is patched and the new index 2 is
            // created.
            var oldTree = new VNode[]
            {
                V.Label(text: "K", key: "k"),
                V.Label(text: "P0"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "K", key: "k2"),
                V.Label(text: "P0-new"),
                V.Label(text: "P1"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var p0 = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(1), Is.SameAs(p0),
                "The existing unkeyed sibling at full index 1 is patched, not recreated");
        }

        [Test]
        public void Given_UnkeyedSiblingBetweenKeyedSiblings_When_KeyedReordered_Then_MiddlePatchedInPlace()
        {
            // Arrange — old=[a*,plain,b*] -> new=[b*,plain,a*]; the unkeyed middle stays at full index 1.
            var oldTree = new VNode[]
            {
                V.Label(text: "A", key: "a"),
                V.Label(text: "P"),
                V.Label(text: "B", key: "b"),
            };
            var newTree = new VNode[]
            {
                V.Label(text: "B", key: "b"),
                V.Label(text: "P-new"),
                V.Label(text: "A", key: "a"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var plainElement = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(1), Is.SameAs(plainElement),
                "The unkeyed middle sibling at the same full index is patched in place");
        }

        #endregion

        // Desync recovery (TryRebuildDesyncedSlotRange) must not delete a FOLLOWING sibling fiber's rows.
        // Several inline-mount fibers can share one parent, each owning a slot range [slotStart, slotLimit).
        // When a non-last tenant's live range is SHORTER than its keyed baseline (a transient AnimatePresence
        // ghost overlap), the recovery rebuilds that range — bounded by slotLimit (the next sibling's
        // MountSlotStart), so it must NOT reach past this fiber's range into the trailing sibling's committed
        // rows. Here fiber A owns slot 1 with a 4-key baseline but only 1 live row survives, a trailing sibling
        // row sits at slot 2, and A reconciles with slotLimit = 2; A's recovery must rebuild [1, 2) and leave the
        // trailing sibling intact. (The recovery is shared by the sync keyed path exercised here and the
        // time-sliced keyed path.)
        [Test]
        public void Given_DesyncedNonLastTenant_When_RangeRebuilt_Then_TrailingSiblingRowSurvives()
        {
            // Arrange — a shared parent: [leading sibling][A's single surviving row][trailing sibling row]. A's keyed
            // baseline claims 4 rows (3 dropped to a ghost overlap), so reconciling A's range hits desync recovery.
            Root.Add(new Label { text = "lead" });
            Root.Add(new Label { text = "aLive" });
            var trailing = new Label { text = "bTrail" };
            Root.Add(trailing);
            Assume.That(Root.childCount, Is.EqualTo(3), "Precondition: lead + 1 live A row + 1 trailing sibling row");

            // Act — fiber A reconciles its keyed range [slotStart 1, slotLimit 2); oldNodes.Length (4) > available
            // (1) fires the desync rebuild, bounded to A's range.
            Reconciler.Reconcile(Root, Keyed("A0", "A1", "A2", "A3"), Keyed("A0", "A1", "A2", "A3"), slotStart: 1, slotLimit: 2);

            // Assert — the trailing sibling's row must survive A's range rebuild. (Checking element identity is not
            // enough: the deleted row is pooled and rented back into A's rebuilt range, so the same instance
            // reappears with A's content — its sibling row is still destroyed. Assert the row's content survives.)
            var bTrailRows = 0;
            foreach (var c in Root.Children())
            {
                if (c is Label l && l.text == "bTrail") bTrailRows++;
            }
            Assert.That(bTrailRows, Is.EqualTo(1));
        }

        private static VNode[] Keyed(params string[] letters)
        {
            var nodes = new VNode[letters.Length];
            for (var i = 0; i < letters.Length; i++)
            {
                nodes[i] = V.Label(text: letters[i], key: letters[i].ToLowerInvariant());
            }
            return nodes;
        }

        private VisualElement[] CurrentElements()
        {
            var elements = new VisualElement[Root.childCount];
            for (var i = 0; i < Root.childCount; i++)
            {
                elements[i] = Root.ElementAt(i);
            }
            return elements;
        }
    }

    /// <summary>
    /// Specifies the keyed-diff suffix-trim prepass: the symmetric counterpart to the existing head linear
    /// scan. When the tail of the old and new child lists matches (key-equal and patch-compatible) and the
    /// middle that remains after trimming both ends collapses to a single contiguous insert or remove, the
    /// Pass-2 map build + LIS reorder is skipped entirely — the headline case being a pure prepend.
    ///
    /// Two contracts are pinned: (1) ORACLE — the final child order equals the new key order for every edit
    /// shape (append / prepend / interior insert / interior remove / head remove / swap / rotation / reverse),
    /// including unkeyed and duplicate-key lists, so the prepass can never reorder differently from the
    /// pure-LIS path it short-circuits; and (2) the prepass actually fires — a pure prepend rents NO Pass-2
    /// buffer (observed via reflection on the pooled <c>oldKeyMap</c>), while a rotation still does.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerKeyedSuffixTrimTests : ReconcilerTestFixture
    {
        private static VNode K(string key) => V.Label(text: key, key: key);

        private static VNode[] Tree(params string[] keys) => keys.Select(K).ToArray();

        private string[] DomKeys()
        {
            var keys = new string[Root.childCount];
            for (var i = 0; i < Root.childCount; i++)
            {
                keys[i] = ((Label)Root.ElementAt(i)).text;
            }
            return keys;
        }

        // Final-order oracle: across every edit shape, the reconciled DOM order must equal the new key order —
        // the property the suffix-trim prepass must preserve relative to the pure-LIS path.
        private static IEnumerable<TestCaseData> OrderOracleCases()
        {
            TestCaseData C(string name, string[] from, string[] to) =>
                new TestCaseData((object)from, (object)to).SetName(name);

            yield return C("Append", new[] { "a", "b", "c" }, new[] { "a", "b", "c", "d" });
            yield return C("Prepend", new[] { "a", "b", "c" }, new[] { "x", "a", "b", "c" });
            yield return C("PrependMany", new[] { "a", "b", "c" }, new[] { "x", "y", "a", "b", "c" });
            yield return C("InteriorInsert", new[] { "a", "b", "c" }, new[] { "a", "x", "b", "c" });
            yield return C("InteriorRemove", new[] { "a", "b", "c", "d" }, new[] { "a", "c", "d" });
            yield return C("HeadRemove", new[] { "a", "b", "c" }, new[] { "b", "c" });
            yield return C("HeadReplaceTailKept", new[] { "x", "a", "b", "c" }, new[] { "y", "a", "b", "c" });
            yield return C("Swap", new[] { "a", "b", "c" }, new[] { "a", "c", "b" });
            yield return C("Rotation", new[] { "a", "b", "c", "d" }, new[] { "d", "a", "b", "c" });
            yield return C("Reverse", new[] { "a", "b", "c" }, new[] { "c", "b", "a" });
            yield return C("FullReplace", new[] { "a", "b", "c" }, new[] { "x", "y", "z" });
            yield return C("EmptyToList", Array.Empty<string>(), new[] { "a", "b" });
            yield return C("ListToEmpty", new[] { "a", "b" }, Array.Empty<string>());
        }

        [TestCaseSource(nameof(OrderOracleCases))]
        public void Given_AnyEditShape_When_Reconciled_Then_DomOrderEqualsNewKeyOrder(string[] from, string[] to)
        {
            // Arrange
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(from));
            Assume.That(DomKeys(), Is.EqualTo(from), "Precondition: the initial list mounted in order");

            // Act
            Reconciler.Reconcile(Root, Tree(from), Tree(to));

            // Assert
            Assert.That(DomKeys(), Is.EqualTo(to),
                "The reconciled DOM order must equal the new key order for every edit shape");
        }

        [Test]
        public void Given_Prepend_When_Reconciled_Then_UnchangedTailKeepsItsElementInstances()
        {
            // Arrange — capture a tail element's instance before the prepend.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            var tailB = FindByText("b");
            Assume.That(tailB, Is.Not.Null, "Precondition: the tail element 'b' is mounted");

            // Act — prepend a new head; the suffix-trim path patches the tail in place rather than rebuilding.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("x", "a", "b", "c"));

            // Assert — the unchanged tail element 'b' is the same instance (not remounted).
            Assert.That(FindByText("b"), Is.SameAs(tailB),
                "A prepend must reuse the unchanged tail's element instances (patch in place, not rebuild)");
        }

        private VisualElement FindByText(string text)
        {
            for (var i = 0; i < Root.childCount; i++)
            {
                if (Root.ElementAt(i) is Label l && l.text == text) return l;
            }
            return null;
        }

        // Reflection probe: the count of pooled oldKeyMap instances. A fresh Reconciler's pool is empty; a
        // reconcile that enters Pass 2 rents (and returns) one, leaving the count at 1, while the suffix-trim
        // fast path rents nothing, leaving it at 0. Reaching the pool internals via reflection keeps the
        // perf-path contract observable without adding a *ForTest hook to production.
        private static int PooledOldKeyMapCount(Reconciler reconciler)
        {
            var pool = reconciler.Context.BufferPool;
            var clearable = pool.GetType()
                .GetField("_oldKeyMapPool", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(pool);
            var stack = clearable.GetType()
                .GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(clearable);
            return (int)stack.GetType().GetProperty("Count").GetValue(stack);
        }

        [Test]
        public void Given_PurePrepend_When_Reconciled_Then_NoPass2BufferIsRented()
        {
            // Arrange — initial mount is a tail-add and rents no Pass-2 buffer, so the pool starts empty.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount took the tail-add path and rented no Pass-2 buffer");

            // Act — a pure prepend.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("x", "a", "b", "c"));

            // Assert — the suffix-trim prepass short-circuited Pass 2, so no oldKeyMap was rented.
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "A pure prepend must skip the Pass-2 map build + LIS (no oldKeyMap rented)");
        }

        [Test]
        public void Given_DuplicateKeyedList_When_ReconciledIntoAPrependShape_Then_DefersToPass2()
        {
            // Arrange — a prepend whose new list repeats a key. The shape would otherwise collapse to a single
            // insert, but a duplicate key must defer to Pass 2 (which de-duplicates) to preserve its behavior.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount rented no Pass-2 buffer");

            // Act — new list has 'c' twice.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("c", "a", "b", "c"));

            // Assert — the suffix-trim uniqueness guard rejected the duplicate and Pass 2 ran (oldKeyMap rented).
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(1),
                "A duplicate-keyed list must defer to Pass 2 rather than take the suffix-trim fast path");
        }

        [Test]
        public void Given_Rotation_When_Reconciled_Then_Pass2BufferIsRented()
        {
            // Arrange
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c", "d"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount rented no Pass-2 buffer");

            // Act — a rotation genuinely needs the LIS reorder, so it must fall through to Pass 2.
            Reconciler.Reconcile(Root, Tree("a", "b", "c", "d"), Tree("d", "a", "b", "c"));

            // Assert — Pass 2 ran, renting (and returning) exactly one oldKeyMap.
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(1),
                "A rotation must fall through to the Pass-2 map build + LIS (oldKeyMap rented)");
        }
    }
}
