using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the wiring of <see cref="StyleFlexMinSizeManipulator"/>: which elements the reconciler attaches
    /// one to, and that a removed element hands it back. The measuring itself needs a real panel and is
    /// covered by <c>TextItemBaselinePanelTests</c>; EditMode with no panel resolves no layout, so
    /// the manipulator's own derive defers here.
    /// </summary>
    [TestFixture]
    internal sealed class FlexMinSizeManipulatorWiringTests
    {
        [SetUp]
        public void SetUp()
        {
            // The Label pool is a process-wide static; start every test from empty.
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        [Test]
        public void Given_AnElementLabel_When_Reconciled_Then_OneManipulatorIsRegistered()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(text: "hello") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ATextNode_When_Reconciled_Then_OneManipulatorIsRegistered()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Text("hello") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADiv_When_Reconciled_Then_NoManipulatorIsRegistered()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div("flex") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ALabelWithAWrittenMinWidth_When_RemovedAndPooled_Then_NoManipulatorAndNoValueRemain()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var label = scope.Root.Q<Label>();
            var attached = ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators.Count;
            // Stands in for what a live derive wrote; EditMode with no panel never writes one itself.
            label.style.minWidth = new StyleLength(42f);

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, System.Array.Empty<VNode>());

            // Assert — the attachment is folded in: a reconciler that attached nothing leaves no
            // manipulator either, and the pooled reset alone would clear the value.
            Assert.That(
                (attached, ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators.Count, label.style.minWidth.keyword),
                Is.EqualTo((1, 0, StyleKeyword.Null)));
        }
    }
}
