using System;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies index-based (unkeyed) child reconciliation, where children are paired by their array
    /// position across renders.
    /// <list type="bullet">
    /// <item>Children present only in the new tree are created; children present only in the old tree
    /// are removed; the resulting child count matches the new tree.</item>
    /// <item>A child whose element type is unchanged at its position is patched in place, reusing the
    /// same VisualElement instance and applying changed props (text, class list, tooltip, ...).</item>
    /// <item>A child whose element type changed at its position is replaced with a fresh element of the
    /// new type.</item>
    /// <item>Across a mixed update, type-changed positions are replaced while same-type positions keep
    /// their instances and receive patched props.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerIndexedTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_EmptyTree_When_ElementsAdded_Then_ChildrenCreatedInOrder()
        {
            // Arrange
            var newChildren = new VNode[]
            {
                V.Label(text: "first"),
                V.Label(text: "second"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), newChildren);

            // Assert
            Assert.That(
                (Root.childCount, ((Label)Root.ElementAt(0)).text, ((Label)Root.ElementAt(1)).text),
                Is.EqualTo((2, "first", "second")));
        }

        [Test]
        public void Given_MountedElements_When_AllRemoved_Then_ParentEmptied()
        {
            // Arrange
            var children = new VNode[] { V.Label(text: "a"), V.Label(text: "b") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);
            Assume.That(Root.childCount, Is.EqualTo(2), "Precondition: two elements are mounted");

            // Act
            Reconciler.Reconcile(Root, children, Array.Empty<VNode>());

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_SameTypeAtPosition_When_TextChanged_Then_TextPatched()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Button(text: "Old") };
            var newChildren = new VNode[] { V.Button(text: "New") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert
            Assert.That(((Button)Root.ElementAt(0)).text, Is.EqualTo("New"));
        }

        [Test]
        public void Given_SameTypeAtPosition_When_ClassChanged_Then_ClassListPatched()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Div("class-a") };
            var newChildren = new VNode[] { V.Div("class-b") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert
            var element = Root.ElementAt(0);
            Assert.That(
                (element.ClassListContains("class-a"), element.ClassListContains("class-b")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_SameTypeAtPosition_When_MultiplePropsChanged_Then_AllApplied()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Button(text: "A", tooltip: "tip-a") };
            var newChildren = new VNode[] { V.Button(text: "B", tooltip: "tip-b") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert
            var button = (Button)Root.ElementAt(0);
            Assert.That((button.text, button.tooltip), Is.EqualTo(("B", "tip-b")));
        }

        [Test]
        public void Given_TypeMismatchAtPosition_When_Patched_Then_ElementReplacedWithNewType()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Button(text: "btn") };
            var newChildren = new VNode[] { V.Label(text: "lbl") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);
            Assume.That(Root.ElementAt(0), Is.InstanceOf<Button>(), "Precondition: the position holds a Button");

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert
            Assert.That(Root.ElementAt(0), Is.InstanceOf<Label>());
        }

        [Test]
        public void Given_EmptyTree_When_MixedTypesAdded_Then_EachCreatedWithItsType()
        {
            // Arrange
            var newChildren = new VNode[]
            {
                V.Div("a"),
                V.Label(text: "b"),
                V.Button(text: "c"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), newChildren);

            // Assert
            Assert.That(
                (Root.ElementAt(1) is Label, Root.ElementAt(2) is Button),
                Is.EqualTo((true, true)),
                "Each position is created with the element type its VNode declares");
        }

        [Test]
        public void Given_MountedMixedTypes_When_AllRemoved_Then_ParentEmptied()
        {
            // Arrange
            var children = new VNode[]
            {
                V.Div("a"),
                V.Label(text: "b"),
                V.Button(text: "c"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);
            Assume.That(Root.childCount, Is.EqualTo(3), "Precondition: three elements are mounted");

            // Act
            Reconciler.Reconcile(Root, children, Array.Empty<VNode>());

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_MixedTypeChange_When_Patched_Then_TypeChangedPositionReplaced()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Button(text: "btn"),
                V.Label(text: "lbl"),
                V.TextField(value: "txt"),
            };
            var newChildren = new VNode[]
            {
                V.Div("container"),
                V.Label(text: "lbl-updated"),
                V.TextField(value: "txt-updated"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert — Button -> Div is a type mismatch, so the position is replaced
            Assert.That(Root.ElementAt(0), Is.Not.InstanceOf<Button>());
        }

        [Test]
        public void Given_MixedTypeChange_When_Patched_Then_SameTypePositionKeepsInstance()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Button(text: "btn"),
                V.Label(text: "lbl"),
                V.TextField(value: "txt"),
            };
            var newChildren = new VNode[]
            {
                V.Div("container"),
                V.Label(text: "lbl-updated"),
                V.TextField(value: "txt-updated"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldChildren);
            var originalLabel = Root.ElementAt(1);

            // Act
            Reconciler.Reconcile(Root, oldChildren, newChildren);

            // Assert — Label -> Label keeps its instance and is patched
            Assert.That(Root.ElementAt(1), Is.SameAs(originalLabel));
        }

        // The indexed desync recovery (the Common-phase slotExists check + the create-on-missing inserts) must not
        // reach into a FOLLOWING sibling fiber's rows. Several inline-mount fibers share one parent, each owning a
        // slot range [slotStart, slotLimit). When a non-last tenant's live range is SHORTER than its baseline (the
        // same transient desync the keyed path recovers from), `slotStart + i < parent.childCount` is true for an
        // index that actually points at the trailing sibling, so the sibling element gets PATCHED as if it were
        // this fiber's row. Bounding by slotLimit (the next sibling's MountSlotStart) makes the recovery create
        // within this fiber's range instead. Mirrors the keyed Given_DesyncedNonLastTenant test.
        [Test]
        public void Given_IndexedDesyncedNonLastTenant_When_Reconciled_Then_TrailingSiblingRowSurvives()
        {
            // Arrange — a shared parent [leading sibling][A's single live row][trailing sibling row].
            Root.Add(new Label { text = "lead" });
            Root.Add(new Label { text = "aLive" });
            var trailing = new Label { text = "bTrail" };
            Root.Add(trailing);
            Assume.That(Root.childCount, Is.EqualTo(3), "Precondition: lead + 1 live A row + 1 trailing sibling row");

            // Act — fiber A reconciles UNKEYED with a 2-node baseline (claims 2 rows) but only 1 live row, so its
            // range index 1 (slot 2) points at the trailing sibling. The second node's text changes, so a misplaced
            // patch writes into the trailing sibling. slotLimit = 2 bounds A's range.
            var oldNodes = new VNode[] { V.Label(text: "a0"), V.Label(text: "a1-old") };
            var newNodes = new VNode[] { V.Label(text: "a0"), V.Label(text: "a1-new") };
            Reconciler.Reconcile(Root, oldNodes, newNodes, slotStart: 1, slotLimit: 2);

            // Assert — the trailing sibling's row was not patched into one of A's rows.
            Assert.That(trailing.text, Is.EqualTo("bTrail"));
        }
    }
}
