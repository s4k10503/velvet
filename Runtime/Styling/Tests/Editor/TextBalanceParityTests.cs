using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>text-balance</c> classifier + <see cref="StyleTextBalanceManipulator"/> lifecycle
    /// contract: the manipulator attaches exactly when the class is present, detaches when the class is
    /// removed (clearing any inline <c>maxWidth</c> it may have written), and a pooled <see cref="Label"/>
    /// carries neither a ghost manipulator nor a ghost <c>maxWidth</c> value into its next consumer.
    /// </summary>
    /// <remarks>
    /// EditMode has no resolved layout (<c>ReconcilerScope.Root</c> is never attached to a panel), so
    /// <see cref="StyleTextBalanceManipulator"/>'s own <c>Apply</c> always defers (no resolved parent
    /// width to measure against) — exactly like <see cref="StyleGridManipulator"/>'s off-panel column
    /// width. These tests therefore pin the wiring (attach / detach / pool hygiene) only; the actual
    /// measure-and-narrow behavior needs a real panel and is covered by the PlayMode spec
    /// (<c>TextBalancePlaybackTests</c>). A stale <c>maxWidth</c> is seeded by hand where a test needs one,
    /// standing in for what a prior LIVE computation would have written.
    /// </remarks>
    [TestFixture]
    internal sealed class TextBalanceParityTests
    {
        [SetUp]
        public void SetUp()
        {
            // The Label pool is a process-wide static; start every test from empty so a pool-count
            // assertion is deterministic regardless of what earlier tests in this fixture rented/returned.
            VNodePool.ClearLabelPoolForTesting();
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
            Assume.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(1),
                "Precondition: the text-balance class registered a manipulator");

            // Act — patch the same label without the text-balance class.
            var tree2 = new VNode[] { V.Label(text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler), Is.EqualTo(0));
        }

        [Test]
        public void Given_TextBalanceManipulator_When_ClassPatchedAway_Then_InlineMaxWidthCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var label = scope.Root.Q<Label>();
            Assume.That(label, Is.Not.Null, "Precondition: the label mounted");
            // Off-panel Apply() defers (no resolved parent width to measure against), so seed the value a
            // LIVE computation would have written, pinning the detach path independently of layout.
            label.style.maxWidth = new StyleLength(50f);

            // Act — patch the same label without the text-balance class.
            var tree2 = new VNode[] { V.Label(text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(label.style.maxWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // Pins the END-TO-END pool-reuse pipeline, not the manipulator's OWN Clear() in isolation: a full
        // removal also runs FiberElementPoolReset's generic maxWidth null (applied to every pooled
        // element regardless of text-balance), so a green result here does not by itself prove the
        // manipulator's own Clear() ran at all — the generic reset alone would produce the same outcome.
        // Given_TextBalanceManipulator_When_ClassPatchedAway_Then_InlineMaxWidthCleared below is the
        // manipulator-specific pin: a same-element class-removal PATCH never returns the element to the
        // pool, so it isolates Clear() from the generic reset.
        [Test]
        public void Given_ATextBalanceLabelWithAStaleMaxWidth_When_RemovedAndPooledThenRecreated_Then_NoStaleMaxWidthGhosts()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var original = scope.Root.Q<Label>();
            Assume.That(original, Is.Not.Null, "Precondition: the label mounted");
            // Simulate a prior LIVE balance computation's result (off-panel Apply() never writes one itself).
            original.style.maxWidth = new StyleLength(42f);

            // Act — remove (returns the label to the pool with the stale maxWidth still on it), then
            // recreate a text-balance label at the same position, renting the SAME pooled instance back.
            scope.Reconciler.Reconcile(scope.Root, tree1, System.Array.Empty<VNode>());
            Assume.That(VNodePool.LabelPoolCountForTesting, Is.EqualTo(1),
                "Precondition: the label was returned to the pool");
            var tree3 = new VNode[] { V.Label(className: "text-balance", text: "world") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree3);
            var recreated = scope.Root.Q<Label>();
            Assume.That(ReferenceEquals(original, recreated), Is.True,
                "Precondition: the same pooled Label instance was rented back");

            // Assert
            Assert.That(recreated.style.maxWidth.keyword, Is.EqualTo(StyleKeyword.Null));
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
            // this exercises FiberNodeFactory's Motion-creation call to ApplyTextBalanceManipulator (a
            // call site distinct from the plain-ElementNode one every other test in this fixture goes
            // through, since a Div/Label wrapped BY a Motion is created via that Motion path instead).
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
        public void Given_ATextBalanceLabelWithACoPresentMaxWidthUtility_When_TextBalanceClassPatchedAway_Then_TheUtilitysMaxWidthIsRestored()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "text-balance max-w-[50px]", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var label = scope.Root.Q<Label>();
            Assume.That(label, Is.Not.Null, "Precondition: the label mounted");
            Assume.That(label.style.maxWidth.value.value, Is.EqualTo(50f),
                "Precondition: the co-present max-w-[50px] utility resolved its own value at mount");

            // Act — patch away JUST the text-balance token; the max-w-[50px] utility stays in the class list.
            var tree2 = new VNode[] { V.Label(className: "max-w-[50px]", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the utility's own value survives text-balance's teardown instead of being left
            // cleared by it (StyleTextBalanceManipulator.Clear unconditionally nulls maxWidth first; the
            // reconciler must restore the co-present utility's value right after).
            Assert.That(label.style.maxWidth.value.value, Is.EqualTo(50f));
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
