using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <see cref="StyleTextBalanceManipulator"/> lifecycle contract: a text leaf gets one
    /// exactly when its resolved text-wrap-style is balance or pretty, wherever in the tree the class was
    /// written, loses it when the class goes, and a pooled <see cref="Label"/> carries neither a ghost
    /// manipulator nor a ghost white-space into its next consumer.
    /// </summary>
    /// <remarks>
    /// EditMode has no resolved layout (<c>ReconcilerScope.Root</c> is never attached to a panel), so the
    /// manipulator never has a width to break in. These tests therefore pin the wiring only; where the
    /// lines break is covered by <see cref="TextLineBreakerTests"/> over the choice and by
    /// <see cref="TextWrapBreakPanelTests"/> over a laid-out panel.
    /// </remarks>
    [TestFixture]
    internal sealed class TextBalanceParityTests
    {
        [SetUp]
        public void SetUp()
        {
            // The Label pool is a process-wide static; start every test from empty so a pool-count
            // assertion is deterministic regardless of what earlier tests in this fixture rented/returned.
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        [Test]
        public void Given_TextBalanceClass_When_Reconciled_Then_RegistersOneTextBalanceManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(className: "text-balance", text: "hello") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_NoTextBalanceClass_When_Reconciled_Then_RegistersNoTextBalanceManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(text: "hello") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(0));
        }

        [Test]
        public void Given_TextBalanceManipulator_When_ClassPatchedAway_Then_ManipulatorRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var attached = GetManipulatorCount(scope.Reconciler);

            // Act — patch the same label without the text-balance class.
            var tree2 = new VNode[] { V.Label(text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the registration is folded in rather than assumed: a reconciler that registered
            // nothing in the first place also holds none here, and detaching is the claim.
            Assert.That((attached, GetManipulatorCount(scope.Reconciler)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ATextBalanceLabel_When_RemovedAndPooledThenRecreated_Then_ExactlyOneManipulatorRegistered()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            scope.Reconciler.Reconcile(scope.Root, tree1, System.Array.Empty<VNode>());
            Assume.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(0),
                "Precondition: removing the label detached its manipulator");

            // Act — recreate a text-balance label at the same position, renting the pooled instance back.
            var tree3 = new VNode[] { V.Label(className: "text-balance", text: "world") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree3);

            // Assert — exactly one manipulator registered, not a stale duplicate left by the pool round-trip.
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_ATextBalanceClassOnAMotionsOwnElement_When_Reconciled_Then_RegistersOneTextBalanceManipulator()
        {
            // Arrange — elementType: typeof(Label) makes the Motion's OWN underlying element a Label, so
            // this exercises the Motion-creation path of the text-effect pass (a call site distinct from
            // the plain-ElementNode one every other test in this fixture goes through, since a Div/Label
            // wrapped BY a Motion is created via that Motion path instead).
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Motion(className: "text-balance", elementType: typeof(Label),
                    props: new FiberElementProps { Text = "hello" }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_TextBalanceOnAContainer_When_Reconciled_Then_ItsTextLeafGetsAManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "text-balance", V.Label(text: "hello")) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the leaf carries no class of its own, so the style reached it by inheritance.
            Assert.That(
                scope.Reconciler.Context.TextBalanceManipulators.ContainsKey(scope.Root.Q<Label>()), Is.True);
        }

        [Test]
        public void Given_TextBalanceOnAContainer_When_ClassPatchedAway_Then_TheLeafLosesItsManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Div(className: "text-balance", V.Label(text: "hello")) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var attached = GetManipulatorCount(scope.Reconciler);

            // Act
            var tree2 = new VNode[] { V.Div(className: "", V.Label(text: "hello")) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the attachment rides along: a reconciler that attached nothing removes nothing.
            Assert.That((attached, GetManipulatorCount(scope.Reconciler)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_TextBalanceOnAContainerAndTextWrapOnTheLeaf_When_Reconciled_Then_TheLeafHasNoManipulator()
        {
            // Arrange — text-wrap resets text-wrap-style, as it does in CSS.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "text-balance", V.Label(className: "text-wrap", text: "hello")) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(0));
        }

        private static int GetManipulatorCount(Reconciler reconciler)
        {
            var ctxField = typeof(Reconciler).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(ctxField, Is.Not.Null, "_ctx field not found");
            var ctx = ctxField.GetValue(reconciler);
            var prop = ctx.GetType().GetProperty("TextBalanceManipulators");
            Assert.That(prop, Is.Not.Null, "TextBalanceManipulators property not found");
            var dict = prop.GetValue(ctx) as System.Collections.IDictionary;
            return dict?.Count ?? 0;
        }
    }
}
