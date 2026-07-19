using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>[&amp;&gt;*]:&lt;utility&gt;</c> child-combinator variant, which applies the wrapped
    /// utility to every direct child of the container (CSS <c>&amp; &gt; *</c>). UITK has no <c>&gt; *</c>
    /// selector, so <see cref="StyleChildVariantManipulator"/> — attached to the CONTAINER — walks the child
    /// list and delegates each child to <see cref="StyleVariantPayload"/>, the same resolver the variant
    /// manipulators use, so a plain class (<c>text-red-500</c>), an arbitrary value (<c>mt-[8px]</c>) and a
    /// state variant (<c>hover:bg-red-500</c>) all compose. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ChildVariantClassParityTests
    {
        private const float Space4 = 16f; // --space-4 (gap-x-4)

        #region Parse

        [Test]
        public void Given_ChildVariantToken_When_Parsed_Then_ExtractsPayload()
        {
            // Act — the wrapped payload is the token minus the [&>*]: prefix.
            var ok = StyleChildVariantClass.TryParse("[&>*]:mt-2", out var payload);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a child-combinator variant");
            Assert.That(payload, Is.EqualTo("mt-2"));
        }

        [Test]
        public void Given_NonChildVariantToken_When_Parsed_Then_Declines()
        {
            // Act — a plain utility carries no [&>*]: prefix.
            var ok = StyleChildVariantClass.TryParse("mt-2", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ChildVariantWrappingStructuralPayload_When_Parsed_Then_Declines()
        {
            // Act — a structural payload (first:) has no gating owner reachable through StyleVariantPayload.Apply,
            // so it is rejected rather than admitted as a dead token.
            var ok = StyleChildVariantClass.TryParse("[&>*]:first:mt-2", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ChildVariantWrappingChildVariantPayload_When_Parsed_Then_Declines()
        {
            // Act — a child is never itself the CONTAINER a nested [&>*]: would walk, so a self-nested wrap has
            // no gating owner either, exactly like the structural / has- / attribute- / supports- rejects above.
            var ok = StyleChildVariantClass.TryParse("[&>*]:[&>*]:mt-2", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ChildVariantWrappingStateVariantPayload_When_Parsed_Then_AcceptsComposedPayload()
        {
            // Act — a state variant (hover:) DOES compose through GateStackedVariant, so the whole
            // hover:bg-red-500 remainder is kept as the payload (unlike the structural reject above).
            var ok = StyleChildVariantClass.TryParse("[&>*]:hover:bg-red-500", out var payload);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a state-variant payload is accepted");
            Assert.That(payload, Is.EqualTo("hover:bg-red-500"));
        }

        [Test]
        public void Given_MultipleChildVariantTokens_When_Extracted_Then_CollectsAllPayloads()
        {
            // Act — every [&>*]: token contributes an independent payload (not last-wins like gap/divide).
            var ok = StyleChildVariantClass.TryExtract(new[] { "[&>*]:mt-2", "[&>*]:text-red-500" }, out var payloads);

            // Assert — both payloads survive, in token order, not just a matching count.
            Assume.That(ok, Is.True, "Precondition: at least one token resolved");
            Assert.That(payloads, Is.EqualTo(new[] { "mt-2", "text-red-500" }));
        }

        [Test]
        public void Given_ClassNamesWithChildVariant_When_HasChildVariantClassProbed_Then_GateReturnsTrue()
        {
            // Act — the FiberNodePatcher early-out depends on this gate recognizing the prefix.
            var has = StyleChildVariantClass.HasChildVariantClass(new[] { "flex", "[&>*]:mt-2" });

            // Assert
            Assert.That(has, Is.True);
        }

        #endregion

        #region End-to-end (manipulator applies the payload to each child)

        [Test]
        public void Given_ChildVariantNamedClass_When_Reconciled_Then_EveryDirectChildGetsClass()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("[&>*]:text-red-500", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var container = scope.Root[0];

            // Assert — the payload lands on every direct child.
            Assert.That(container.Children().All(c => c.ClassListContains("text-red-500")), Is.True);
        }

        [Test]
        public void Given_ChildVariantArbitraryValue_When_Reconciled_Then_EveryChildGetsInlineStyle()
        {
            // Arrange — an arbitrary value resolves to an inline style rather than a USS class.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("[&>*]:mt-[8px]", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.marginTop.value.value, Is.EqualTo(8f));
        }

        [Test]
        public void Given_ChildVariantRow_When_ClassRemovedByPatch_Then_StaleClassCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("[&>*]:text-red-500", 3) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].ClassListContains("text-red-500"), Is.True, "Precondition: payload applied");

            // Act — patch the same container without the [&>*]: class.
            var tree2 = new VNode[] { Row("flex", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the manipulator cleared the payload it applied to the children (no ghost).
            Assert.That(scope.Root[0][1].ClassListContains("text-red-500"), Is.False);
        }

        [Test]
        public void Given_ChildVariantNamedClass_When_PayloadTokenSwapped_Then_OldClassClearedNewApplied()
        {
            // Arrange — the load-bearing swap: Apply only ever ADDS the current set, so without a pre-clear in
            // UpdatePayloads the dropped mt-2 would stay stuck on every child forever.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("[&>*]:mt-2", 3) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].ClassListContains("mt-2"), Is.True, "Precondition: the old payload is applied");

            // Act — swap the wrapped token.
            var tree2 = new VNode[] { Row("[&>*]:mt-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the old token is turned off before the new one is applied.
            Assert.That(scope.Root[0][1].ClassListContains("mt-2"), Is.False);
        }

        [Test]
        public void Given_ChildAddedToChildVariantContainer_When_Reconciled_Then_NewChildAlsoGetsPayload()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("[&>*]:text-red-500", 2) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree1);

            // Act — add a third child (same class); the post-children pass re-derives against the new set.
            var tree2 = new VNode[] { Row("[&>*]:text-red-500", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the newly added child carries the payload too.
            Assert.That(scope.Root[0][2].ClassListContains("text-red-500"), Is.True);
        }

        [Test]
        public void Given_ChildMovedOutOfChildVariantContainer_When_Reapplied_Then_NoResidualPayloadOnDetachedChild()
        {
            // Arrange — capture the manipulator and the 3rd child (which carries the payload).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("[&>*]:text-red-500", 3) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = GetManipulator(scope, container);
            var movedChild = container[2];
            Assume.That(movedChild.ClassListContains("text-red-500"), Is.True,
                "Precondition: the child carries the payload while in the container");

            // Act — move the child out of the container (a sibling reparent / removal), then re-apply.
            container.Remove(movedChild);
            var sink = new VisualElement();
            sink.Add(movedChild);
            manipulator.Apply();

            // Assert — the manipulator cleared the payload on the element that left its container.
            Assert.That(movedChild.ClassListContains("text-red-500"), Is.False);
        }

        [Test]
        public void Given_OutOfFlowChild_When_Reconciled_Then_NotGivenChildVariantPayload()
        {
            // Arrange — the middle child is out of flow (position: absolute), so like gap/divide it is excluded
            // from the walk while its in-flow siblings are not.
            using var scope = new ReconcilerScope();
            var children = new VNode[]
            {
                V.Div(className: "child"),
                V.Div(className: "absolute child"),
                V.Div(className: "child"),
            };
            var tree = new VNode[] { V.Div(className: "[&>*]:text-red-500", children: children) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            Assume.That(container[1].ClassListContains("absolute"), Is.True, "Precondition: the middle child is out of flow");

            // Assert — the absolute child is skipped while an in-flow sibling is styled.
            Assert.That(
                (container[1].ClassListContains("text-red-500"), container[0].ClassListContains("text-red-500")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ChildVariantWithStateVariantPayload_When_ChildHovered_Then_PayloadAppliesOnlyOnHover()
        {
            // Arrange — [&>*]:hover:bg-red-500 spawns a per-child stacked manipulator gated by the child's own
            // hover; the payload must be OFF until the child is actually hovered.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("[&>*]:hover:bg-red-500", 3) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var child = scope.Root[0][0];
            Assume.That(child.ClassListContains("bg-red-500"), Is.False, "Precondition: payload off before hover");

            // Act — hover the child.
            using (var evt = PointerOverEvent.GetPooled()) child.SimulateEvent(evt);

            // Assert — the composed hover payload lights up on the child.
            Assert.That(child.ClassListContains("bg-red-500"), Is.True);
        }

        [Test]
        public void Given_ScrollViewChildVariant_When_Reconciled_Then_ContentChildrenGetPayload()
        {
            // Arrange — a ScrollView redirects children into its contentContainer; the payload must land on the
            // reconciled content, not the ScrollView's internal hierarchy (mirrors the gap / divide cases).
            using var scope = new ReconcilerScope();
            var children = new VNode[] { V.Div(className: "child"), V.Div(className: "child"), V.Div(className: "child") };
            var tree = new VNode[] { V.ScrollView("[&>*]:text-red-500", children) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var content = ((ScrollView)scope.Root[0]).contentContainer;
            Assume.That(content.childCount, Is.EqualTo(3), "Precondition: children reconcile into the contentContainer");

            // Assert
            Assert.That(content.Children().All(c => c.ClassListContains("text-red-500")), Is.True);
        }

        [Test]
        public void Given_NoChildVariantClass_When_Reconciled_Then_NoManipulatorRegistered()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — no [&>*]: class → no manipulator (the cheap early-out path).
            Assert.That(GetManipulator(scope, scope.Root[0]), Is.Null);
        }

        [Test]
        public void Given_NoRelevantChange_When_ApplyCalledAgain_Then_EarlyReturnsWithoutRewriting()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("[&>*]:mt-[8px]", 3) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = GetManipulator(scope, container);

            // Poke a sentinel value onto an applied child; a no-op Apply (no child-set change) must NOT rewrite
            // it, while a real child-set change afterwards must re-apply the payload.
            container[1].style.marginTop = 999f;
            manipulator.Apply();
            var afterNoOp = container[1].style.marginTop.value.value;

            // Act — a real child-set change re-applies.
            container.Add(new VisualElement());
            manipulator.Apply();
            var afterRealChange = container[1].style.marginTop.value.value;

            // Assert — sentinel survived the no-op (signature early-out held), then the real change re-applied.
            Assert.That((afterNoOp, afterRealChange), Is.EqualTo((999f, 8f)));
        }

        [Test]
        public void Given_GapAndChildVariantSameEdge_When_Reconciled_Then_GapWins()
        {
            // Arrange — [&>*]:ml-[2px] and gap-x-4 both target margin-left; the child-variant pass runs BEFORE
            // gap, so gap owns the shared edge (its existing precedence over any per-child margin source).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4 [&>*]:ml-[2px]", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the 2nd child's leading margin is the gap value, not the child-variant's 2px.
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        #endregion

        private static VNode Row(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            return V.Div(className: className, children: children);
        }

        private static StyleChildVariantManipulator GetManipulator(ReconcilerScope scope, VisualElement element)
            => scope.Reconciler.Context.ChildVariantManipulators.TryGetValue(element, out var m) ? m : null;
    }
}
