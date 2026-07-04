using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the structural (child-position) variants — <c>first:</c> / <c>last:</c> / <c>odd:</c> /
    /// <c>even:</c> / <c>only:</c> and the arbitrary <c>[&amp;:nth-child(N)]:</c> form. They are declared on a
    /// child but resolved against its position among siblings by the reconciler's post-children pass, so the
    /// payload re-derives when the child set changes (add / remove / reorder). Off-panel; positions come from
    /// the live child order, payloads are asserted via the class list. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StructuralVariantTests
    {
        // A container of `count` children, each carrying childClass + a stable key + name c{i}.
        private static VNode Container(int count, string childClass)
        {
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Div(className: childClass, key: i.ToString(), name: "c" + i);
            }
            return V.Div(className: "container", children: children);
        }

        private static VisualElement Child(ReconcilerScope scope, int i) => scope.Root.Q<VisualElement>("c" + i);

        [Test]
        public void Given_OddVariant_When_Mounted_Then_FirstChildHasPayload()
        {
            // Arrange/Act — odd: marks the 1st, 3rd, … children (1-based odd).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "odd:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_OddVariant_When_Mounted_Then_SecondChildLacksPayload()
        {
            // Arrange/Act — the 2nd child (1-based even) is not matched by odd:.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "odd:bg-mark") });

            // Assert
            Assert.IsFalse(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_EvenVariant_When_Mounted_Then_SecondChildHasPayload()
        {
            // Arrange/Act — even: marks the 2nd, 4th, … children.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "even:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_FirstVariant_When_Mounted_Then_FirstChildHasPayload()
        {
            // Arrange/Act
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "first:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_FirstVariant_When_Mounted_Then_NonFirstChildLacksPayload()
        {
            // Arrange/Act — first: must not leak onto a middle child.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "first:bg-mark") });

            // Assert
            Assert.IsFalse(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_Mounted_Then_LastChildHasPayload()
        {
            // Arrange/Act
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "last:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 2).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_ArbitraryNthChild_When_Mounted_Then_TargetedChildHasPayload()
        {
            // Arrange/Act — [&:nth-child(2)]: targets the 2nd child (1-based).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(3, "[&:nth-child(2)]:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 1).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_OnlyVariant_When_SingleChild_Then_ChildHasPayload()
        {
            // Arrange/Act — only: matches a sole child.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { Container(1, "only:bg-mark") });

            // Assert
            Assert.IsTrue(Child(scope, 0).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_AChildIsAppended_Then_PreviousLastClearsPayload()
        {
            // Arrange — three children, the 3rd is last.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { Container(3, "last:bg-mark") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Child(scope, 2).ClassListContains("bg-mark"), Is.True, "Precondition: 3rd child is last");

            // Act — a 4th child is appended (the structural pass re-derives every position).
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { Container(4, "last:bg-mark") });

            // Assert — the previously-last child drops the payload (reactivity).
            Assert.IsFalse(Child(scope, 2).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariant_When_AChildIsAppended_Then_NewLastGainsPayload()
        {
            // Arrange — three children.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { Container(3, "last:bg-mark") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);

            // Act — a 4th child is appended.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { Container(4, "last:bg-mark") });

            // Assert — the new last child gains the payload.
            Assert.IsTrue(Child(scope, 3).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_LastVariantOnShadowWrappedChild_When_Mounted_Then_InnerGetsPayload()
        {
            // Arrange — the last child carries both last: and shadow-md, so it is wrapped in a shadow wrapper;
            // the structural pass must resolve the wrapper back to the inner (the side-table is keyed by it).
            using var scope = new ReconcilerScope();
            var children = new VNode[]
            {
                V.Div(className: "last:bg-mark", key: "0", name: "c0"),
                V.Div(className: "last:bg-mark", key: "1", name: "c1"),
                V.Div(className: "last:bg-mark shadow-md", key: "2", name: "c2"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: "container", children: children) });

            // Assert — the inner of the wrapped last child gets the payload.
            Assert.IsTrue(Child(scope, 2).ClassListContains("bg-mark"));
        }
    }
}
