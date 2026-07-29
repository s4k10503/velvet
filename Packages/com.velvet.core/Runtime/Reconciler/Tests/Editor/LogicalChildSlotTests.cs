using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the reconciler addresses LOGICAL child slots — one per rendered VNode — and that a
    /// reconciler-invisible child may sit ANYWHERE among them, not only in a leading or trailing run.
    /// </summary>
    /// <remarks>
    /// The reconciler used to treat <c>physical = logical + LeadingOffset</c> as true across a whole child
    /// list, which pinned every invisible child to one end. A paint that must sit BESIDE the element it
    /// decorates cannot honour that, so <see cref="LogicalChildSlots"/> converts at each DOM touch instead.
    /// The mid-list cases below are the ones that assumption made impossible; the leading (z-index back
    /// container) and trailing (filter bounds spacer) cases are kept alongside them because both still have
    /// to work, and they pull the append position in opposite directions.
    /// </remarks>
    [TestFixture]
    internal sealed class LogicalChildSlotTests
    {
        // A stand-in for any reconciler-invisible child, marked the way the shared predicate recognizes one.
        private static VisualElement Invisible()
        {
            var e = new VisualElement { name = "invisible" };
            e.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            return e;
        }

        private static VisualElement Rendered(string name) => new() { name = name };

        // The one invisible kind that must stay a parent's FIRST physical child, as against the trailing
        // kinds Invisible() stands in for.
        private static VisualElement BackLayerContainer()
        {
            var e = new VisualElement { name = "back" };
            e.AddToClassList(FiberZLayerCoordinator.BackMarkerClass);
            return e;
        }

        private static VisualElement Container(params VisualElement[] children)
        {
            var c = new VisualElement();
            foreach (var child in children)
            {
                c.Add(child);
            }
            return c;
        }

        private static List<string> NamesOf(VisualElement container)
        {
            var names = new List<string>();
            for (var i = 0; i < container.childCount; i++)
            {
                names.Add(container[i].name);
            }
            return names;
        }

        // Unit: the mapping itself

        [Test]
        public void Given_AnInvisibleChildBetweenTwoRendered_When_CountingSlots_Then_ItIsNotASlot()
        {
            // Arrange & Act
            var container = Container(Rendered("a"), Invisible(), Rendered("b"));

            // Assert
            Assert.That(LogicalChildSlots.Count(container), Is.EqualTo(2));
        }

        [Test]
        public void Given_AnInvisibleChildBetweenTwoRendered_When_ResolvingTheSecondSlot_Then_ItSkipsPastIt()
        {
            // Arrange & Act — logical slot 1 is "b", which sits at physical index 2.
            var container = Container(Rendered("a"), Invisible(), Rendered("b"));

            // Assert
            Assert.That(LogicalChildSlots.ToPhysical(container, 1), Is.EqualTo(2));
        }

        [Test]
        public void Given_AnInvisibleChildBetweenTwoRendered_When_ConvertingItsPhysicalIndex_Then_ItReportsTheSlotItPrecedes()
        {
            // Arrange & Act
            var container = Container(Rendered("a"), Invisible(), Rendered("b"));

            // Assert — one rendered child precedes it, so it sits in front of slot 1.
            Assert.That(LogicalChildSlots.ToLogical(container, 1), Is.EqualTo(1));
        }

        [Test]
        public void Given_OnlyABackLayerContainer_When_ResolvingTheAppendPosition_Then_ItLandsAfterIt()
        {
            // Arrange & Act — the back container must stay the parent's first physical child, so an append
            // cannot resolve to 0. Its KIND is what decides this, not its position: see the trailing-kind
            // twin below, which sits at the same index and must get the opposite answer.
            var container = Container(BackLayerContainer());

            // Assert
            Assert.That(LogicalChildSlots.ToPhysical(container, 0), Is.EqualTo(1));
        }

        [Test]
        public void Given_ATrailingInvisibleChild_When_ResolvingTheAppendPosition_Then_ItLandsBeforeIt()
        {
            // Arrange & Act — a filter bounds spacer must stay last, which pulls the append position the
            // opposite way from the back container above.
            var container = Container(Rendered("a"), Invisible());

            // Assert
            Assert.That(LogicalChildSlots.ToPhysical(container, 1), Is.EqualTo(1));
        }

        [Test]
        public void Given_OnlyATrailingInvisibleChild_When_ResolvingTheAppendPosition_Then_ItStillLandsBeforeIt()
        {
            // Arrange & Act — the discriminating pair for the rule being kind-aware rather than positional:
            // this spacer is the parent's ONLY child, exactly as the back container is above, and must get
            // the opposite answer. Reachable — a filtered element whose rendered children have all been
            // removed and which then gains one back.
            var container = Container(Invisible());

            // Assert
            Assert.That(LogicalChildSlots.ToPhysical(container, 0), Is.EqualTo(0));
        }

        // End to end: the reconciler addressing slots across a mid-list invisible child

        [Test]
        public void Given_AMidListInvisibleChild_When_AChildIsPatchedInPlace_Then_TheCorrectSlotIsPatched()
        {
            // Arrange — mount two children, then splice an invisible child between them, exactly where the
            // old trailing-only assumption said one could never be.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            scope.Root.Insert(1, Invisible());

            // Act — patch the second slot's identity.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(name: "a", key: "a"), V.Div(name: "b2", key: "b") });

            // Assert — the invisible child is untouched and "b" was the element renamed, not it.
            Assert.That(NamesOf(scope.Root), Is.EqualTo(new[] { "a", "invisible", "b2" }));
        }

        [Test]
        public void Given_AMidListInvisibleChild_When_AChildIsInserted_Then_ItLandsAtItsLogicalSlot()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            scope.Root.Insert(1, Invisible());

            // Act — a new child between the two existing ones.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(name: "a", key: "a"), V.Div(name: "mid", key: "mid"), V.Div(name: "b", key: "b"),
            });

            // Assert — inserting at logical slot 1 lands before "b" and after the invisible child, which
            // stays attached to the rendered child it precedes.
            Assert.That(NamesOf(scope.Root), Is.EqualTo(new[] { "a", "invisible", "mid", "b" }));
        }

        [Test]
        public void Given_AMidListInvisibleChild_When_ARenderedChildIsRemoved_Then_OnlyThatChildLeaves()
        {
            // Arrange — this is the shape that exposed the old assumption: a keyed removal walks slots, and
            // a miscounted invisible child made it tear out the wrong one.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b"), V.Div(name: "c", key: "c"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            scope.Root.Insert(1, Invisible());

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(name: "a", key: "a"), V.Div(name: "c", key: "c") });

            // Assert
            Assert.That(NamesOf(scope.Root), Is.EqualTo(new[] { "a", "invisible", "c" }));
        }

        [Test]
        public void Given_AMidListInvisibleChild_When_TheLastKeyIsReplaced_Then_TheCreatedRowLandsLast()
        {
            // Reaches ChildElementPlacement, which four fast paths in ReconcileKeyedSync otherwise skip — a
            // plain append exits at the pure-append path and never gets there. REPLACING the last key does:
            // Pass 1 stops before the old list ends, and the trailing keys differ so the suffix trim yields
            // nothing. The created row is the only new-side entry that maps to -1, which ComputeLisAnchors
            // never makes an anchor, so it is the single entry that consults afterRangeAnchor — read
            // physically that anchor is the range's OWN last element, and the new row lands before it.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b"), V.Div(name: "c", key: "c"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            scope.Root.Insert(1, Invisible());

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b"), V.Div(name: "x", key: "x"),
            });

            // Assert
            Assert.That(NamesOf(scope.Root), Is.EqualTo(new[] { "a", "invisible", "b", "x" }));
        }

        [Test]
        public void Given_AMidListInvisibleChild_When_AChildIsAppendedOnTheGeneralPath_Then_ItLandsLast()
        {
            // The general path has no fast paths at all — FinalizeGeneralCommit always ends in
            // ComputeAnchorsAndReorder — so a plain append reaches the same anchor there. A single null child
            // is enough to take that path, since RequiresInlineExpansion includes null.
            using var scope = new ReconcilerScope();
            var before = new VNode?[] { null, V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            scope.Root.Insert(1, Invisible());

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode?[]
            {
                null, V.Div(name: "a", key: "a"), V.Div(name: "b", key: "b"), V.Div(name: "c", key: "c"),
            });

            // Assert
            Assert.That(NamesOf(scope.Root), Is.EqualTo(new[] { "a", "invisible", "b", "c" }));
        }

        [Test]
        public void Given_AMidListInvisibleChild_When_StructuralVariantsAreDerived_Then_ItIsNotASibling()
        {
            // Arrange — last: must land on the final RENDERED child. With the invisible child mid-list, a
            // physical walk would both inflate the sibling count and evaluate the wrong occupant.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(name: "box", key: "box", children: new VNode[]
                {
                    V.Div(name: "a", key: "a", className: "last:underline"),
                    V.Div(name: "b", key: "b", className: "last:underline"),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var box = scope.Root.Q<VisualElement>("box");
            box.Insert(1, Invisible());

            // Act — FRESH VNode instances, not the same array: passing `tree` on both sides makes Pass 1's
            // ReferenceEquals skip return before any PatchNode, so nothing would be re-derived and the case
            // would only be re-reading its own mount-time result.
            scope.Reconciler.Reconcile(scope.Root, tree, new VNode[]
            {
                V.Div(name: "box", key: "box", children: new VNode[]
                {
                    V.Div(name: "a", key: "a", className: "last:underline"),
                    V.Div(name: "b", key: "b", className: "last:underline"),
                }),
            });

            // Assert — "b" is the last rendered child and carries the payload; "a" does not.
            Assert.That((box.Q<VisualElement>("a").ClassListContains("underline"),
                    box.Q<VisualElement>("b").ClassListContains("underline")),
                Is.EqualTo((false, true)));
        }
    }
}
